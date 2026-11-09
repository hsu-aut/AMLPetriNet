/**
 * The way from the initial marking to a marking, out of an exploration.
 *
 * The state space already holds every firing it took; what it does not hold is
 * the order to read them in. A breadth first walk over the recorded edges gives
 * the shortest firing sequence to any marking the search reached, which is what
 * makes a finding like "this net deadlocks" into something a reader can watch
 * happen.
 *
 * Shortest, not any: a path of three firings is a demonstration, a path of two
 * hundred is a log. Where several equally short ways exist the first the search
 * recorded is taken, so the answer is the same from one run to the next.
 *
 * Nothing here fires anything. A path is a list of transition ids, and whoever
 * wants to see it fires it.
 */
import { OMEGA } from './explore.js';
import { readKey } from './model.js';

/**
 * The shortest firing sequence from the initial marking to this one.
 *
 * @returns {{transitions: string[], markings: string[]} | null} null where the
 *   recorded edges do not lead there. That happens on a search that stopped at
 *   its limit: a marking can be in the result while the firings into it are
 *   not, because the marking that leads to it was never expanded.
 */
export function pathTo(result, key) {
  if (key === result.initial) return { transitions: [], markings: [result.initial] };

  const out = new Map();
  for (const edge of result.edges) {
    if (!out.has(edge.from)) out.set(edge.from, []);
    out.get(edge.from).push(edge);
  }

  const cameFrom = new Map();
  const queue = [result.initial];
  const seen = new Set([result.initial]);

  while (queue.length) {
    const from = queue.shift();
    for (const edge of out.get(from) ?? []) {
      if (seen.has(edge.to)) continue;
      seen.add(edge.to);
      cameFrom.set(edge.to, edge);
      if (edge.to === key) return walkBack(cameFrom, result.initial, key);
      queue.push(edge.to);
    }
  }

  return null;
}

function walkBack(cameFrom, initial, key) {
  const transitions = [];
  const markings = [key];

  let at = key;
  while (at !== initial) {
    const edge = cameFrom.get(at);
    if (!edge) return null;
    transitions.unshift(edge.transition);
    markings.unshift(edge.from);
    at = edge.from;
  }

  return { transitions, markings };
}

/**
 * The shortest way into a marking in which nothing is enabled.
 *
 * A deadlock holding omega is left out: it stands for a family of markings
 * rather than for one, so there is no run that ends in it.
 *
 * @returns {{key: string, transitions: string[], markings: string[]} | null}
 */
export function pathToDeadlock(result) {
  let best = null;

  for (const key of result.deadlocks) {
    if (readKey(key).some(([, held]) => held === OMEGA)) continue;
    const path = pathTo(result, key);
    if (!path) continue;
    if (!best || path.transitions.length < best.transitions.length) best = { key, ...path };
  }

  return best;
}

/**
 * What a user typed, as the token counts they are asking about.
 *
 * `Idle=1, Busy` asks for one token in Idle and one in Busy; a place without a
 * count is read as one token, which is what a user who writes a place name
 * means. A place is named by its id or by its name, and a name that two places
 * share is refused rather than guessed.
 *
 * Places the query does not name are free: the question is "a marking that
 * holds this", not "a marking that holds only this". A reader who wants the
 * second names the places they want empty with a zero.
 *
 * @returns {{wanted: Map<string, number>, problem: string | null}}
 */
export function parseQuery(text, net) {
  const wanted = new Map();
  const problem = (message) => ({ wanted: new Map(), problem: message });

  const byId = new Map(net.places.map((place) => [place.id, place.id]));
  const byName = new Map();
  for (const place of net.places) {
    if (!place.name) continue;
    const key = place.name.toLowerCase();
    byName.set(key, byName.has(key) ? null : place.id);
  }

  for (const part of String(text ?? '').split(/[,;]/)) {
    const piece = part.trim();
    if (!piece) continue;

    const [, label, , count] = /^(.*?)(\s*[=:]\s*(.*))?$/.exec(piece) ?? [];
    const named = (label ?? '').trim();
    if (!named) return problem(`"${piece}" does not name a place.`);

    const id = byId.get(named) ?? byName.get(named.toLowerCase());
    if (id === null) return problem(`Two places are called "${named}"; use their ids instead.`);
    if (id === undefined) return problem(`This net has no place called "${named}".`);
    if (wanted.has(id)) return problem(`"${named}" is in the query twice.`);

    const tokens = count === undefined ? 1 : Number(String(count).trim());
    if (!Number.isInteger(tokens) || tokens < 0) {
      return problem(`"${named}" is asked for ${String(count).trim()} tokens; that has to be a whole number of at least 0.`);
    }

    wanted.set(id, tokens);
  }

  if (wanted.size === 0) return problem('Name a place and how many tokens it should hold, as in "Idle = 1".');
  return { wanted, problem: null };
}

/**
 * The markings of an exploration that hold what the query asks for.
 *
 * Split in two, because the two are not the same statement: an exact marking is
 * one the net reaches, while a marking holding omega in a place the query names
 * stands for every marking that holds enough tokens there, so it is an answer
 * about a family rather than about one marking.
 *
 * @returns {{exact: string[], abstract: string[]}}
 */
export function matching(result, wanted) {
  const exact = [];
  const abstract = [];

  for (const { key, marking } of result.markings) {
    let fits = true;
    let omega = false;

    for (const [place, tokens] of wanted) {
      const held = marking.get(place) ?? 0;
      if (held === OMEGA) {
        omega = true;
        continue;
      }
      if (held !== tokens) { fits = false; break; }
    }

    if (!fits) continue;
    (omega ? abstract : exact).push(key);
  }

  return { exact, abstract };
}

/**
 * The query answered: the shortest way to a marking that holds what it asks
 * for, with `abstract` where the only answer stands for a family of markings.
 *
 * @returns {{key: string, transitions: string[], markings: string[], abstract: boolean} | null}
 */
export function wayTo(result, wanted) {
  const { exact, abstract } = matching(result, wanted);

  for (const [keys, isAbstract] of [[exact, false], [abstract, true]]) {
    let best = null;
    for (const key of keys) {
      const path = pathTo(result, key);
      if (!path) continue;
      if (!best || path.transitions.length < best.transitions.length) best = { key, ...path, abstract: isAbstract };
    }
    if (best) return best;
  }

  return null;
}
