/**
 * Where the elements of a net go when nobody has placed them.
 *
 * A net that came out of a converter, a generator or a file written by hand
 * often has no coordinates worth the name: everything sits on one spot, or in
 * the order it was written. Dragging thirty shapes apart by hand is not work
 * anybody should do.
 *
 * The layout is the usual layered one: the flow decides the columns, and inside
 * a column the elements are sorted to where their neighbours are. Written out:
 *
 *  1. Find the edges that point backwards, so that a cycle does not make the
 *     layering impossible. Those are laid out as if they pointed forwards; a
 *     net is full of cycles, and a cycle has to start somewhere.
 *  2. Give every element a column: one further than the furthest element that
 *     leads into it. Sources end up in the first column.
 *  3. Sort each column by the average row of the neighbours in the column
 *     before it, then the other way round, a few times. This is the barycentre
 *     heuristic, and it is what keeps the arcs from crossing more than they
 *     have to.
 *  4. Hand out coordinates, each column centred against the tallest one.
 *
 * Plain data in, plain data out: ids, sizes and arcs in, a position per id out.
 * No canvas, no model, so it can be checked without a browser.
 */

const COLUMN = 150;
const ROW = 110;
const MARGIN = 80;

/** How often the barycentre sort runs in each direction. */
const SWEEPS = 4;

/**
 * @param nodes [{ id, width, height }], in the order the caller wants them
 *   considered; two elements of equal standing keep this order.
 * @param arcs [{ source, target }], ends that are not nodes are ignored.
 * @returns {Map<string, {x: number, y: number}>} the top left corner for each
 *   node, as a shape on a canvas carries it.
 */
export function arrange(nodes, arcs, options = {}) {
  const { column = COLUMN, row = ROW, margin = MARGIN } = options;

  const ids = nodes.map((node) => node.id);
  const known = new Set(ids);
  const edges = arcs
    .filter((arc) => known.has(arc.source) && known.has(arc.target) && arc.source !== arc.target);

  const forward = withoutBackEdges(ids, edges);
  const layer = layers(ids, forward);
  const columns = ordered(ids, layer, forward);

  const tallest = Math.max(...columns.map((members) => members.length), 1);
  const size = new Map(nodes.map((node) => [node.id, node]));
  const at = new Map();

  columns.forEach((members, index) => {
    // Each column is centred against the tallest one, so the net reads as a
    // band rather than as a staircase.
    const offset = (tallest - members.length) / 2;

    members.forEach((id, place) => {
      const node = size.get(id);
      at.set(id, {
        x: Math.round(margin + index * column - (node.width ?? 0) / 2),
        y: Math.round(margin + (place + offset) * row - (node.height ?? 0) / 2),
      });
    });
  });

  return at;
}

/**
 * The edges that do not point backwards, found by a depth first walk: an edge
 * that leads to an element the walk is still inside of closes a cycle.
 */
function withoutBackEdges(ids, edges) {
  const out = new Map(ids.map((id) => [id, []]));
  for (const edge of edges) out.get(edge.source).push(edge.target);

  const done = new Set();
  const inside = new Set();
  const back = new Set();

  for (const root of ids) {
    if (done.has(root)) continue;

    const work = [{ id: root, next: 0 }];
    inside.add(root);

    while (work.length) {
      const frame = work[work.length - 1];
      const neighbours = out.get(frame.id);

      if (frame.next < neighbours.length) {
        const neighbour = neighbours[frame.next];
        frame.next += 1;

        if (inside.has(neighbour)) back.add(`${frame.id}\u0000${neighbour}`);
        else if (!done.has(neighbour)) {
          inside.add(neighbour);
          work.push({ id: neighbour, next: 0 });
        }
        continue;
      }

      work.pop();
      inside.delete(frame.id);
      done.add(frame.id);
    }
  }

  return edges.filter((edge) => !back.has(`${edge.source}\u0000${edge.target}`));
}

/** One column further than the furthest element that leads into it. */
function layers(ids, forward) {
  const into = new Map(ids.map((id) => [id, []]));
  const outOf = new Map(ids.map((id) => [id, []]));
  for (const edge of forward) {
    into.get(edge.target).push(edge.source);
    outOf.get(edge.source).push(edge.target);
  }

  const layer = new Map(ids.map((id) => [id, 0]));
  const left = new Map(ids.map((id) => [id, into.get(id).length]));
  const queue = ids.filter((id) => left.get(id) === 0);

  while (queue.length) {
    const id = queue.shift();
    for (const next of outOf.get(id)) {
      layer.set(next, Math.max(layer.get(next), layer.get(id) + 1));
      left.set(next, left.get(next) - 1);
      if (left.get(next) === 0) queue.push(next);
    }
  }

  return layer;
}

/** The elements of each column, sorted towards their neighbours. */
function ordered(ids, layer, forward) {
  const width = Math.max(...ids.map((id) => layer.get(id)), 0) + 1;
  const columns = Array.from({ length: width }, () => []);
  for (const id of ids) columns[layer.get(id)].push(id);

  const before = new Map(ids.map((id) => [id, []]));
  const after = new Map(ids.map((id) => [id, []]));
  for (const edge of forward) {
    before.get(edge.target).push(edge.source);
    after.get(edge.source).push(edge.target);
  }

  for (let sweep = 0; sweep < SWEEPS; sweep += 1) {
    const down = sweep % 2 === 0;
    const order = down ? [...columns.keys()] : [...columns.keys()].reverse();

    for (const index of order) {
      const rows = new Map();
      columns.forEach((members, i) => members.forEach((id, place) => {
        if (i !== index) rows.set(id, place);
      }));

      const neighbours = down ? before : after;
      columns[index] = sortStably(columns[index], (id) => {
        const places = neighbours.get(id).map((other) => rows.get(other)).filter((place) => place !== undefined);
        // An element without neighbours in that direction stays where it is.
        return places.length === 0 ? null : places.reduce((sum, place) => sum + place, 0) / places.length;
      });
    }
  }

  return columns;
}

/**
 * Sorts by a number, keeping the order of everything the number does not
 * separate, and leaving elements without a number where they are. The second
 * part is what makes the result the same from one run to the next.
 */
function sortStably(members, numberOf) {
  const keyed = members.map((id, place) => ({ id, place, key: numberOf(id) }));
  const fixed = keyed.filter((entry) => entry.key === null);
  const movable = keyed
    .filter((entry) => entry.key !== null)
    .sort((a, b) => a.key - b.key || a.place - b.place);

  // The ones that stay keep their row; the rest fill the rows that are left.
  const free = keyed.map((entry) => entry.place).filter((place) => !fixed.some((entry) => entry.place === place));
  const sorted = [...members];
  fixed.forEach((entry) => { sorted[entry.place] = entry.id; });
  movable.forEach((entry, i) => { sorted[free[i]] = entry.id; });

  return sorted;
}
