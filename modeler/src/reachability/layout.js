/**
 * Where the markings of a state space go on screen.
 *
 * A reachability graph is read as "what can happen next", so the layout is by
 * distance from the initial marking: everything one firing away sits in the
 * first column, everything two firings away in the second. Inside a column the
 * order is the order the search found them, which keeps a net's graph the same
 * from one run to the next.
 *
 * Plain data in, plain data out, so it can be checked without a browser.
 */
const COLUMN = 170;
const ROW = 64;
const MARGIN = 24;

/**
 * @param result what `explore` returned.
 * @returns {{nodes: {key, x, y, initial, deadlock}[], edges: {from, to, transition}[], width, height}}
 */
export function layoutStateSpace(result) {
  const distance = distances(result);
  const columns = new Map();

  for (const { key } of result.markings) {
    const column = distance.get(key) ?? 0;
    if (!columns.has(column)) columns.set(column, []);
    columns.get(column).push(key);
  }

  const deadlocks = new Set(result.deadlocks);
  const position = new Map();
  const nodes = [];

  for (const [column, keys] of [...columns].sort(([a], [b]) => a - b)) {
    keys.forEach((key, row) => {
      const node = {
        key,
        x: MARGIN + column * COLUMN,
        y: MARGIN + row * ROW,
        initial: key === result.initial,
        deadlock: deadlocks.has(key),
      };
      position.set(key, node);
      nodes.push(node);
    });
  }

  const tallest = Math.max(...[...columns.values()].map((keys) => keys.length), 1);

  return {
    nodes,
    edges: result.edges.filter((edge) => position.has(edge.from) && position.has(edge.to)),
    width: MARGIN * 2 + (columns.size - 1) * COLUMN,
    height: MARGIN * 2 + (tallest - 1) * ROW,
  };
}

/** How many firings each marking is away from the initial one. */
function distances(result) {
  const out = new Map();
  for (const edge of result.edges) {
    if (!out.has(edge.from)) out.set(edge.from, []);
    out.get(edge.from).push(edge.to);
  }

  const distance = new Map([[result.initial, 0]]);
  const queue = [result.initial];

  while (queue.length) {
    const key = queue.shift();
    for (const next of out.get(key) ?? []) {
      if (distance.has(next)) continue;
      distance.set(next, distance.get(key) + 1);
      queue.push(next);
    }
  }

  // A marking the search reached but never expanded, and anything the edges do
  // not lead to, still has to go somewhere.
  for (const { key } of result.markings) {
    if (!distance.has(key)) distance.set(key, 0);
  }

  return distance;
}
