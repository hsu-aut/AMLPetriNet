/**
 * What the state space says about a net, as findings.
 *
 * A finding reads like the structural ones a validator produces: a rule, a
 * severity, the element it is about and a sentence. Rule numbers continue at
 * PT12 so that behavioural and structural findings can sit in one list.
 *
 * Everything comes from a single exploration. Where that exploration hit its
 * limit, the findings are what was seen until then: `complete` says so, and
 * PT17 puts it in the list, because a reader who does not know that reads
 * "no deadlock" as a proof when it is only a report.
 */
import { bounds, explore, OMEGA } from './explore.js';

export const RULES = {
  PT12: 'Unbounded place',
  PT13: 'Bound of a place',
  PT14: 'Dead transition',
  PT15: 'Deadlock',
  PT16: 'Not live',
  PT17: 'Exploration cut off',
};

export function analyse(net, limits = {}) {
  const result = explore(net, limits);
  const findings = [];
  const add = (rule, severity, element, message) => findings.push({ rule, severity, element, message });

  const name = new Map([...net.places, ...net.transitions].map((e) => [e.id, e.name || e.id]));
  const largest = bounds(result);

  for (const place of net.places) {
    const most = largest.get(place.id) ?? 0;
    if (most === OMEGA) {
      add('PT12', 'warning', place.id,
        `${name.get(place.id)} can hold any number of tokens: there is no bound on it.`);
    } else {
      add('PT13', 'information', place.id, most === 0
        ? `${name.get(place.id)} never holds a token.`
        : `${name.get(place.id)} holds at most ${most === 1 ? 'one token' : `${most} tokens`}.`);
    }
  }

  for (const transition of net.transitions) {
    if (!result.fired.includes(transition.id)) {
      add('PT14', 'warning', transition.id,
        `${name.get(transition.id)} can never fire: no reachable marking enables it.`);
    }
  }

  for (const key of result.deadlocks) {
    add('PT15', 'warning', null, `No transition is enabled in the marking ${describe(key, name)}.`);
  }

  // A transition that fires but cannot fire again from some reachable marking
  // is not live. Dead transitions are already reported, so they are left out.
  for (const transition of liveness(net, result)) {
    if (result.fired.includes(transition)) {
      add('PT16', 'information', transition,
        `${name.get(transition)} is not live: from some reachable marking it can never fire again.`);
    }
  }

  if (!result.complete) {
    add('PT17', 'information', null, result.stopped === 'time'
      ? 'The search stopped at its time limit; the findings cover what it reached.'
      : 'The search stopped at its limit on markings; the findings cover what it reached.');
  }

  return { findings, complete: result.complete, result };
}

/** The transitions that are not enabled in any marking reachable from some marking. */
function liveness(net, result) {
  const reachable = successors(result);
  const notLive = new Set();

  for (const { key } of result.markings) {
    const from = reachable.get(key) ?? new Set([key]);
    const firing = new Set();
    for (const edge of result.edges) if (from.has(edge.from)) firing.add(edge.transition);
    for (const transition of net.transitions) {
      if (!firing.has(transition.id)) notLive.add(transition.id);
    }
  }

  return [...notLive];
}

/** For every marking, the markings reachable from it, itself included. */
function successors(result) {
  const next = new Map(result.markings.map(({ key }) => [key, new Set([key])]));

  // Small state spaces, so the simple closure is fast enough and easy to read.
  let changed = true;
  while (changed) {
    changed = false;
    for (const edge of result.edges) {
      const from = next.get(edge.from);
      const to = next.get(edge.to);
      if (!from || !to) continue;
      for (const key of to) {
        if (!from.has(key)) { from.add(key); changed = true; }
      }
    }
  }

  return next;
}

/** A marking key as a sentence a reader can follow. */
function describe(key, name) {
  if (key === '') return 'with no tokens anywhere';
  const parts = key.split(',').map((entry) => {
    const [place, tokens] = entry.split(':');
    return `${name.get(place) ?? place} ${tokens === String(OMEGA) ? 'unbounded' : tokens}`;
  });
  return `(${parts.join(', ')})`;
}
