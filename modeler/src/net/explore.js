/**
 * The reachable markings of a net, and how they follow one another.
 *
 * A net with a cycle and no bound has infinitely many markings, so the search
 * does two things that an enumeration alone would not: it stops at a limit and
 * says that it stopped, and it recognises growth. Where a marking covers one
 * of its own ancestors and holds strictly more tokens in some place, that place
 * can grow without end; it is recorded as unbounded and the search carries
 * `omega` for it instead of counting on. That is the coverability graph of
 * Karp and Miller, and it keeps the result finite for every net.
 *
 * `omega` is a number, `Infinity`, so that comparing and adding need no special
 * case: anything plus omega is omega, and omega is enough for any arc weight.
 */
import { enabled, fire, initialMarking, markingKey } from './model.js';

export const OMEGA = Infinity;

const DEFAULT_LIMITS = {
  markings: 20000,
  milliseconds: 2000,
  // The clock, so that a test can make the time limit bite without waiting.
  now: () => Date.now(),
};

/**
 * @returns {{
 *   markings: {key: string, marking: Map<string, number>}[],
 *   edges: {from: string, to: string, transition: string}[],
 *   initial: string,
 *   deadlocks: string[],
 *   fired: string[],
 *   unbounded: string[],
 *   complete: boolean,
 *   stopped: 'markings' | 'time' | null,
 * }}
 */
export function explore(net, limits = {}) {
  const { markings: maxMarkings, milliseconds, now } = { ...DEFAULT_LIMITS, ...limits };
  const until = now() + milliseconds;

  const start = initialMarking(net);
  const initial = markingKey(start);

  const seen = new Map([[initial, start]]);
  const edges = [];
  const deadlocks = [];
  const fired = new Set();
  const unbounded = new Set();

  // Each entry carries the markings on the path to it, so that growth can be
  // seen against every ancestor, not only against the one before it.
  const queue = [{ key: initial, marking: start, ancestors: [] }];
  let stopped = null;

  while (queue.length) {
    if (now() > until) { stopped = 'time'; break; }

    const { key, marking, ancestors } = queue.shift();
    const ready = enabled(net, marking);
    if (ready.length === 0) deadlocks.push(key);

    for (const transition of ready) {
      fired.add(transition);

      let next = fire(net, marking, transition);
      const grown = growing(next, [...ancestors, marking]);
      for (const place of grown) {
        unbounded.add(place);
        next.set(place, OMEGA);
      }

      const nextKey = markingKey(next);
      edges.push({ from: key, to: nextKey, transition });

      if (!seen.has(nextKey)) {
        // Checked here rather than once per expansion: one marking can have as
        // many successors as the net has transitions, and a limit that is only
        // looked at between expansions is not a limit.
        if (seen.size >= maxMarkings) { stopped = 'markings'; break; }
        seen.set(nextKey, next);
        queue.push({ key: nextKey, marking: next, ancestors: [...ancestors, marking] });
      }
    }

    if (stopped) break;
  }

  return {
    markings: [...seen].map(([key, marking]) => ({ key, marking })),
    edges,
    initial,
    deadlocks,
    fired: [...fired],
    unbounded: [...unbounded],
    complete: stopped === null,
    stopped,
  };
}

/**
 * The places in which `marking` holds more than some ancestor while holding at
 * least as much everywhere else. Reaching such a marking again repeats the
 * same firings, so those places grow without end.
 */
function growing(marking, ancestors) {
  const grown = new Set();

  for (const ancestor of ancestors) {
    let covers = true;
    const more = [];

    for (const [place, tokens] of marking) {
      const before = ancestor.get(place) ?? 0;
      if (tokens < before) { covers = false; break; }
      if (tokens > before) more.push(place);
    }

    if (covers) for (const place of more) grown.add(place);
  }

  return [...grown];
}

/** The number of tokens a place holds at most, or `OMEGA` where it is unbounded. */
export function bounds(result) {
  const largest = new Map();

  for (const { marking } of result.markings) {
    for (const [place, tokens] of marking) {
      if (tokens > (largest.get(place) ?? 0)) largest.set(place, tokens);
    }
  }

  return largest;
}
