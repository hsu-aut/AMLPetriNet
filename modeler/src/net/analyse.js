/**
 * What the state space says about a net, as findings.
 *
 * A finding reads like the structural ones a validator produces: a rule, a
 * severity, the element it is about and a sentence. Rule numbers continue at
 * PT12 so that behavioural and structural findings can sit in one list.
 *
 * Everything but the invariants comes from a single exploration, and the
 * exploration has two ways of being less than the whole truth: it stops at its
 * limit, and it abstracts a place that grows without bound into omega. Both
 * turn "never" and "at most" into statements about what was searched. The
 * findings say which of the two they are, and PT17 states it once more in the
 * list, because a reader takes "no deadlock" for a proof otherwise.
 */
import { readKey } from './model.js';
import { bounds, explore, OMEGA } from './explore.js';
import { placeInvariants, transitionInvariants } from './invariants.js';

export const RULES = {
  PT12: 'Unbounded place',
  PT13: 'Bound of a place',
  PT14: 'Dead transition',
  PT15: 'Deadlock',
  PT16: 'Not live',
  PT17: 'Partial result',
  PT18: 'Place invariant',
  PT19: 'Transition invariant',
};

export function analyse(net, limits = {}) {
  const result = explore(net, limits);
  const findings = [];
  const add = (rule, severity, element, message) => findings.push({ rule, severity, element, message });

  const name = new Map([...net.places, ...net.transitions].map((e) => [e.id, e.name || e.id]));
  const largest = bounds(result);

  // Whether a statement about every reachable marking can be made at all.
  const whole = result.complete && result.unbounded.length === 0;
  const searched = result.complete
    ? 'in the markings the search covered'
    : 'in the markings the search reached before it stopped';

  for (const place of net.places) {
    const most = largest.get(place.id) ?? 0;
    if (most === OMEGA) {
      add('PT12', 'warning', place.id,
        `${name.get(place.id)} can hold any number of tokens: there is no bound on it.`);
    } else if (!whole) {
      add('PT13', 'information', place.id,
        `${name.get(place.id)} holds ${tokens(most)} at most ${searched}.`);
    } else {
      add('PT13', 'information', place.id, most === 0
        ? `${name.get(place.id)} never holds a token.`
        : `${name.get(place.id)} holds at most ${tokens(most)}.`);
    }
  }

  for (const transition of net.transitions) {
    if (result.fired.includes(transition.id)) continue;
    if (whole) {
      add('PT14', 'warning', transition.id,
        `${name.get(transition.id)} can never fire: no reachable marking enables it.`);
    } else {
      // Without the whole state space this is not "never", and a warning that
      // may be wrong is worse than an observation that is right.
      add('PT14', 'information', transition.id,
        `${name.get(transition.id)} did not fire ${searched}.`);
    }
  }

  // A marking in which nothing is enabled was reached and tried, so a deadlock
  // stays a deadlock even where the rest is partial. The exception is a
  // marking holding omega, which stands for a family of markings rather than
  // for one.
  for (const key of result.deadlocks) {
    const abstract = readKey(key).some(([, held]) => held === OMEGA);
    add('PT15', abstract ? 'information' : 'warning', null, abstract
      ? `No transition is enabled in the marking ${describe(key, name)}, which stands for a family of markings.`
      : `No transition is enabled in the marking ${describe(key, name)}.`);
  }

  // Liveness needs the whole picture: from a marking that was never expanded,
  // or from one that omega stands for, nothing follows.
  if (whole) {
    for (const transition of notLive(net, result)) {
      if (result.fired.includes(transition)) {
        add('PT16', 'information', transition,
          `${name.get(transition)} is not live: from some reachable marking it can never fire again.`);
      }
    }
  }

  // Invariants come from the matrix, not from the search, so they hold whatever
  // the search did.
  for (const invariant of placeInvariants(net)) {
    add('PT18', 'information', null,
      `The token count of ${weighted(invariant, name)} is the same in every reachable marking.`);
  }
  for (const invariant of transitionInvariants(net)) {
    add('PT19', 'information', null,
      `Firing ${weighted(invariant, name, 'once', 'times')} returns the net to the marking it started from.`);
  }

  if (!result.complete) {
    add('PT17', 'information', null, result.stopped === 'time'
      ? 'The search stopped at its time limit, so the findings above cover the markings it reached, not all of them.'
      : 'The search stopped at its limit on markings, so the findings above cover the markings it reached, not all of them.');
  } else if (result.unbounded.length !== 0) {
    add('PT17', 'information', null,
      'Some places have no bound, so the search stands for their markings instead of listing them, '
      + 'and a deadlock it did not find can still exist.');
  }

  return { findings, complete: whole, result };
}

const tokens = (count) => (count === 1 ? 'one token' : `${count} tokens`);

/**
 * The transitions that are not live: from some reachable marking there is no
 * way on that ever enables them again.
 *
 * A transition is live exactly where it fires inside every terminal strongly
 * connected component of the reachability graph, a component nothing leaves.
 * Whatever the net does it ends up in one of them and stays there, so a
 * transition missing from one of them can be lost for good. Tarjan's algorithm
 * finds them in linear time; the closure over all markings this replaces took
 * a minute on a state space of twenty thousand, which in a browser is a frozen
 * page.
 */
function notLive(net, result) {
  const { of } = stronglyConnected(result);

  const leaves = new Set(of.values());
  for (const edge of result.edges) {
    const from = of.get(edge.from);
    const to = of.get(edge.to);
    if (from !== undefined && to !== undefined && from !== to) leaves.delete(from);
  }

  const firing = new Map([...leaves].map((leaf) => [leaf, new Set()]));
  for (const edge of result.edges) {
    const from = of.get(edge.from);
    if (from !== undefined && from === of.get(edge.to) && firing.has(from)) {
      firing.get(from).add(edge.transition);
    }
  }

  const missing = new Set();
  for (const fired of firing.values()) {
    for (const transition of net.transitions) {
      if (!fired.has(transition.id)) missing.add(transition.id);
    }
  }

  return [...missing];
}

/** Tarjan's strongly connected components, iterative so that a deep graph does not overflow the stack. */
function stronglyConnected(result) {
  const out = new Map(result.markings.map(({ key }) => [key, []]));
  for (const edge of result.edges) out.get(edge.from)?.push(edge.to);

  const index = new Map();
  const low = new Map();
  const onStack = new Set();
  const stack = [];
  const of = new Map();
  let count = 0;
  let next = 0;

  for (const { key: root } of result.markings) {
    if (index.has(root)) continue;

    const work = [{ key: root, step: 0 }];
    while (work.length) {
      const frame = work[work.length - 1];
      const { key } = frame;

      if (frame.step === 0) {
        index.set(key, next);
        low.set(key, next);
        next += 1;
        stack.push(key);
        onStack.add(key);
      }

      const neighbours = out.get(key) ?? [];
      if (frame.step < neighbours.length) {
        const neighbour = neighbours[frame.step];
        frame.step += 1;
        if (!index.has(neighbour)) {
          work.push({ key: neighbour, step: 0 });
        } else if (onStack.has(neighbour)) {
          low.set(key, Math.min(low.get(key), index.get(neighbour)));
        }
        continue;
      }

      work.pop();
      if (work.length) {
        const parent = work[work.length - 1].key;
        low.set(parent, Math.min(low.get(parent), low.get(key)));
      }

      if (low.get(key) === index.get(key)) {
        let member;
        do {
          member = stack.pop();
          onStack.delete(member);
          of.set(member, count);
        } while (member !== key);
        count += 1;
      }
    }
  }

  return { of, count };
}

/** An invariant as a sentence: the elements it covers with their weights. */
function weighted(invariant, name, one = '', many = '') {
  return [...invariant]
    .map(([id, weight]) => {
      const label = name.get(id) ?? id;
      if (!one) return weight === 1 ? label : `${weight} times ${label}`;
      return weight === 1 ? `${label} ${one}` : `${label} ${weight} ${many}`;
    })
    .reduce((text, part, i, all) => {
      if (i === 0) return part;
      return i === all.length - 1 ? `${text} and ${part}` : `${text}, ${part}`;
    }, '');
}

/** A marking key as a sentence a reader can follow. */
function describe(key, name) {
  const held = readKey(key);
  if (held.length === 0) return 'with no tokens anywhere';
  const parts = held.map(([place, count]) =>
    `${name.get(place) ?? place} ${count === OMEGA ? 'unbounded' : count}`);
  return `(${parts.join(', ')})`;
}
