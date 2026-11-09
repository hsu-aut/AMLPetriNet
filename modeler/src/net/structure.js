/**
 * Siphons and traps: what the net says about itself without being run.
 *
 * A **siphon** is a set of places that cannot refill once it is empty: every
 * transition that puts a token into the set also takes one out of it. An empty
 * siphon stays empty for ever, and every transition that needs a token from it
 * is dead from then on. That is where a deadlock comes from, and it is a
 * statement about the structure, so it holds on nets whose state space is far
 * too large to build.
 *
 * A **trap** is the mirror image: every transition that takes a token out of
 * the set also puts one back. A trap that holds a token can never lose all of
 * them.
 *
 * Both are computed from the two relations of a place: the transitions that
 * fill it and the transitions that drain it. The maximal siphon inside a set is
 * found the way it is taught: as long as some transition fills the set without
 * draining it, drop the places that transition fills. The minimal ones are
 * enumerated by taking the maximal siphon of the whole net and then of the net
 * without each of its places, recursively.
 *
 * The enumeration is exponential in the worst case, so it is capped on three
 * counts and says when a cap stopped it. A result that says `complete: false`
 * is a list of siphons, not the list of them.
 */

const DEFAULT_LIMITS = {
  /** Above this many places the enumeration is not attempted at all. */
  places: 40,
  /** Most sets reported. */
  sets: 60,
  /** Most recursive steps, for a net that is small but awkward. */
  steps: 20000,
};

/** For each place: the transitions that fill it, and those that drain it. */
function relations(net) {
  const fills = new Map(net.places.map((place) => [place.id, new Set()]));
  const drains = new Map(net.places.map((place) => [place.id, new Set()]));

  for (const arc of net.arcs) {
    if (fills.has(arc.target)) fills.get(arc.target).add(arc.source);
    if (drains.has(arc.source)) drains.get(arc.source).add(arc.target);
  }

  return { fills, drains };
}

/**
 * The largest subset of `places` that is a siphon under the two relations.
 *
 * Called with (fills, drains) this is the maximal siphon; called with the two
 * the other way round it is the maximal trap, because the two definitions are
 * each other read backwards.
 */
function largest(places, fills, drains) {
  const inside = new Set(places);

  for (let changed = true; changed;) {
    changed = false;

    // The transitions that take something out of the set, as it stands now.
    const taking = new Set();
    for (const place of inside) for (const transition of drains.get(place) ?? []) taking.add(transition);

    for (const place of [...inside]) {
      for (const transition of fills.get(place) ?? []) {
        if (taking.has(transition)) continue;
        // This transition fills the set without taking from it, so the set can
        // refill, and the place it fills cannot belong to a siphon.
        inside.delete(place);
        changed = true;
        break;
      }
    }
  }

  return inside;
}

/** The largest siphon inside these places, in the order the net lists them. */
export function maximalSiphon(net, places = net.places.map((place) => place.id)) {
  const { fills, drains } = relations(net);
  return ordered(net, largest(places, fills, drains));
}

/** The largest trap inside these places, in the order the net lists them. */
export function maximalTrap(net, places = net.places.map((place) => place.id)) {
  const { fills, drains } = relations(net);
  return ordered(net, largest(places, drains, fills));
}

/**
 * The minimal siphons of the net.
 *
 * @returns {{sets: string[][], complete: boolean, stopped: 'places' | 'sets' | 'steps' | null}}
 */
export function siphons(net, limits = {}) {
  const { fills, drains } = relations(net);
  return enumerate(net, fills, drains, { ...DEFAULT_LIMITS, ...limits });
}

/** The minimal traps of the net, as `siphons` reports siphons. */
export function traps(net, limits = {}) {
  const { fills, drains } = relations(net);
  return enumerate(net, drains, fills, { ...DEFAULT_LIMITS, ...limits });
}

function enumerate(net, fills, drains, limits) {
  const all = net.places.map((place) => place.id);
  if (all.length > limits.places) {
    return { sets: [], complete: false, stopped: 'places' };
  }

  const found = [];
  const seen = new Set();
  let steps = 0;
  let stopped = null;

  // Returns false when a cap stopped the walk, so that the caller can say so
  // rather than report a partial list as the whole one.
  const walk = (candidate) => {
    if ((steps += 1) > limits.steps) { stopped = 'steps'; return false; }

    const set = largest(candidate, fills, drains);
    if (set.size === 0) return true;

    const key = [...set].sort().join('\u0000');
    if (seen.has(key)) return true;
    seen.add(key);
    found.push(set);
    if (found.length > limits.sets) { stopped = 'sets'; return false; }

    for (const place of set) {
      const smaller = new Set(set);
      smaller.delete(place);
      if (!walk(smaller)) return false;
    }

    return true;
  };

  walk(new Set(all));

  return {
    // Smallest first: a siphon of two places says more than one of twenty, and
    // a caller that shows only some of them should show those.
    sets: minimal(found)
      .map((set) => ordered(net, set))
      .sort((a, b) => a.length - b.length || (a.join() < b.join() ? -1 : a.join() > b.join() ? 1 : 0)),
    complete: stopped === null,
    stopped,
  };
}

/** Drops every set that holds another one: those are not minimal. */
function minimal(sets) {
  return sets.filter((set, i) => !sets.some((other, j) => {
    if (i === j || other.size > set.size) return false;
    const contained = [...other].every((place) => set.has(place));
    if (!contained) return false;
    // Equal sets cannot happen, they are deduplicated, so this is strict.
    return true;
  }));
}

/** The places of a set in the order the net lists them, so results are stable. */
function ordered(net, set) {
  return net.places.map((place) => place.id).filter((id) => set.has(id));
}

/** Whether any place of the set holds a token in this marking. */
export function holdsToken(places, marking) {
  return places.some((place) => (marking.get(place) ?? 0) > 0);
}
