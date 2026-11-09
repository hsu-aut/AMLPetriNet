/**
 * A place/transition net as plain data, and the firing rule on it.
 *
 * Nothing here knows about diagram-js, the DOM or any file format: a net is
 * read from plain objects whose identifiers are whatever the caller uses, so
 * that an answer can be pointed back at the element it came from.
 *
 * Markings are `Map` from place id to token count and are never changed in
 * place: `fire` and `unfire` return a new one, which is what lets a run be
 * recorded as a list of markings and stepped through in both directions.
 */

/** @typedef {Map<string, number>} Marking */

/**
 * Checks the input and fills in the defaults: a place without a marking holds
 * nothing, an arc without a weight carries one token.
 *
 * Throws on anything that would make the firing rule meaningless rather than
 * guessing: a weight that is not a positive whole number, a marking that is
 * not a non-negative whole number, an arc whose ends are not a place and a
 * transition, an arc end that is not an element of the net, or an identifier
 * used twice. A net that comes from a drawing can hold all of these.
 */
export function readNet(data) {
  const places = (data?.places ?? []).map((place) => ({
    id: String(place.id),
    name: place.name ?? '',
    marking: whole(place.marking ?? 0, `marking of place ${place.id}`, 0),
  }));
  const transitions = (data?.transitions ?? []).map((transition) => ({
    id: String(transition.id),
    name: transition.name ?? '',
  }));
  const arcs = (data?.arcs ?? []).map((arc) => ({
    id: String(arc.id),
    source: String(arc.source),
    target: String(arc.target),
    weight: whole(arc.weight ?? 1, `weight of arc ${arc.id}`, 1),
  }));

  const kind = new Map();
  for (const place of places) claim(kind, place.id, 'place');
  for (const transition of transitions) claim(kind, transition.id, 'transition');
  for (const arc of arcs) claim(kind, arc.id, 'arc');

  for (const arc of arcs) {
    const from = kind.get(arc.source);
    const to = kind.get(arc.target);
    if (!from) throw new Error(`arc ${arc.id} starts at ${arc.source}, which is not in the net`);
    if (!to) throw new Error(`arc ${arc.id} ends at ${arc.target}, which is not in the net`);
    if (!(from === 'place' && to === 'transition') && !(from === 'transition' && to === 'place')) {
      throw new Error(`arc ${arc.id} runs from a ${from} to a ${to}, not between a place and a transition`);
    }
  }

  return { places, transitions, arcs };
}

function claim(kind, id, what) {
  if (kind.has(id)) throw new Error(`id ${id} is used by two elements, a ${kind.get(id)} and a ${what}`);
  kind.set(id, what);
}

function whole(value, what, least) {
  const number = typeof value === 'number' || typeof value === 'string' ? Number(value) : NaN;
  if (!Number.isInteger(number) || number < least) {
    throw new Error(`the ${what} is ${value}; it has to be a whole number of at least ${least}`);
  }
  return number;
}

/** The marking the net was read with. */
export function initialMarking(net) {
  return new Map(net.places.map((place) => [place.id, place.marking]));
}

/**
 * What firing the transition takes from each place and gives to each place.
 *
 * The weights are summed per place rather than kept per arc: two arcs from the
 * same place to the same transition mean the transition needs both weights at
 * once. Checking them one at a time would let a place with one token enable a
 * transition that takes two, and the marking would go negative.
 */
function flowOf(net, transitionId) {
  const taking = new Map();
  const giving = new Map();

  for (const arc of net.arcs) {
    if (arc.target === transitionId) taking.set(arc.source, (taking.get(arc.source) ?? 0) + arc.weight);
    if (arc.source === transitionId) giving.set(arc.target, (giving.get(arc.target) ?? 0) + arc.weight);
  }

  return { taking, giving };
}

/**
 * Whether the transition can fire in this marking.
 *
 * A place that is both an input and an output of the same transition, a
 * self-loop, has to hold the input weight for the transition to fire; the
 * output then puts the tokens back, so the place ends up unchanged where both
 * weights are equal. That falls out of taking first and giving afterwards.
 */
export function isEnabled(net, marking, transitionId) {
  const { taking } = flowOf(net, transitionId);
  return [...taking].every(([place, weight]) => (marking.get(place) ?? 0) >= weight);
}

/** The transitions that can fire, in the order the net lists them. */
export function enabled(net, marking) {
  return net.transitions
    .filter((transition) => isEnabled(net, marking, transition.id))
    .map((transition) => transition.id);
}

/**
 * A set of firings that can all happen at once.
 *
 * Concurrent nets are read in steps rather than in single firings: everything
 * that can happen together does. Together means together, so the tokens are
 * taken from the marking the step starts in: a transition that would need a
 * token another transition of the step produces belongs to the next step, not
 * to this one. Each transition fires at most once per step.
 *
 * The step is maximal but not the only maximal one: two transitions that want
 * the same token cannot both be in it, and which of them is depends on the
 * order they are tried in.
 *
 * Firing the result one transition after another in the order given is a legal
 * run, because every firing only adds tokens to what the others need. That is
 * what lets a step be recorded as single firings and stepped back one at a
 * time.
 *
 * @param order the transition ids to try, in the order to try them. The
 *   default is the order the net lists them, which makes the result the same
 *   from one run to the next; a caller that wants a conflict decided by chance
 *   hands in a shuffled order.
 * @returns the transition ids of the step, in the order they fire.
 */
export function maximalStep(net, marking, order = net.transitions.map((transition) => transition.id)) {
  const step = [];
  // What is left to take, counted down as the step is built up. The tokens the
  // step produces are deliberately not added here.
  const left = new Map(marking);

  for (const transition of order) {
    if (!net.transitions.some((candidate) => candidate.id === transition)) continue;

    const { taking } = flowOf(net, transition);
    if (![...taking].every(([place, weight]) => (left.get(place) ?? 0) >= weight)) continue;

    for (const [place, weight] of taking) left.set(place, (left.get(place) ?? 0) - weight);
    step.push(transition);
  }

  return step;
}

/** The marking after the transition fired. Throws when it could not fire. */
export function fire(net, marking, transitionId) {
  if (!net.transitions.some((transition) => transition.id === transitionId)) {
    throw new Error(`${transitionId} is not a transition of this net`);
  }
  if (!isEnabled(net, marking, transitionId)) {
    throw new Error(`transition ${transitionId} is not enabled in this marking`);
  }

  const { taking, giving } = flowOf(net, transitionId);
  const next = new Map(marking);
  for (const [place, weight] of taking) next.set(place, (next.get(place) ?? 0) - weight);
  for (const [place, weight] of giving) next.set(place, (next.get(place) ?? 0) + weight);
  return next;
}

/**
 * The marking the transition fired from, for stepping back through a run.
 *
 * Throws where this marking cannot have come from that firing, which is the
 * case when an output place holds less than the firing put into it.
 */
export function unfire(net, marking, transitionId) {
  if (!net.transitions.some((transition) => transition.id === transitionId)) {
    throw new Error(`${transitionId} is not a transition of this net`);
  }

  const { taking, giving } = flowOf(net, transitionId);
  const previous = new Map(marking);
  for (const [place, weight] of giving) {
    const left = (previous.get(place) ?? 0) - weight;
    if (left < 0) {
      throw new Error(`this marking cannot have come from firing ${transitionId}: ${place} holds too few tokens`);
    }
    previous.set(place, left);
  }
  for (const [place, weight] of taking) previous.set(place, (previous.get(place) ?? 0) + weight);
  return previous;
}

/**
 * A string that is equal for equal markings, whatever order the places were
 * put in, so that a marking can be a key of a map or a member of a set.
 */
export function markingKey(marking) {
  // JSON, not "place:tokens" joined by commas: an identifier that holds a comma
  // or a colon would otherwise make two different markings look like one.
  return JSON.stringify(
    [...marking.entries()]
      .filter(([, tokens]) => tokens !== 0)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([place, tokens]) => [place, tokens === Infinity ? 'omega' : tokens]),
  );
}

/** The places and counts a key was built from, for a message a reader sees. */
export function readKey(key) {
  return JSON.parse(key).map(([place, tokens]) => [place, tokens === 'omega' ? Infinity : tokens]);
}
