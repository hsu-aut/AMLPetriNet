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
  const number = Number(value);
  if (!Number.isInteger(number) || number < least) {
    throw new Error(`the ${what} is ${value}; it has to be a whole number of at least ${least}`);
  }
  return number;
}

/** The marking the net was read with. */
export function initialMarking(net) {
  return new Map(net.places.map((place) => [place.id, place.marking]));
}

/** The arcs that bring tokens to a transition, and the ones that take them away. */
function arcsOf(net, transitionId) {
  return {
    taking: net.arcs.filter((arc) => arc.target === transitionId),
    giving: net.arcs.filter((arc) => arc.source === transitionId),
  };
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
  const { taking } = arcsOf(net, transitionId);
  return taking.every((arc) => (marking.get(arc.source) ?? 0) >= arc.weight);
}

/** The transitions that can fire, in the order the net lists them. */
export function enabled(net, marking) {
  return net.transitions
    .filter((transition) => isEnabled(net, marking, transition.id))
    .map((transition) => transition.id);
}

/** The marking after the transition fired. Throws when it could not fire. */
export function fire(net, marking, transitionId) {
  if (!net.transitions.some((transition) => transition.id === transitionId)) {
    throw new Error(`${transitionId} is not a transition of this net`);
  }
  if (!isEnabled(net, marking, transitionId)) {
    throw new Error(`transition ${transitionId} is not enabled in this marking`);
  }

  const { taking, giving } = arcsOf(net, transitionId);
  const next = new Map(marking);
  for (const arc of taking) next.set(arc.source, (next.get(arc.source) ?? 0) - arc.weight);
  for (const arc of giving) next.set(arc.target, (next.get(arc.target) ?? 0) + arc.weight);
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

  const { taking, giving } = arcsOf(net, transitionId);
  const previous = new Map(marking);
  for (const arc of giving) {
    const left = (previous.get(arc.target) ?? 0) - arc.weight;
    if (left < 0) {
      throw new Error(`this marking cannot have come from firing ${transitionId}: ${arc.target} holds too few tokens`);
    }
    previous.set(arc.target, left);
  }
  for (const arc of taking) previous.set(arc.source, (previous.get(arc.source) ?? 0) + arc.weight);
  return previous;
}

/**
 * A string that is equal for equal markings, whatever order the places were
 * put in, so that a marking can be a key of a map or a member of a set.
 */
export function markingKey(marking) {
  return [...marking.entries()]
    .filter(([, tokens]) => tokens !== 0)
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([place, tokens]) => `${place}:${tokens}`)
    .join(',');
}
