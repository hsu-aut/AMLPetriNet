import assert from 'node:assert/strict';
import { test } from 'node:test';

import { conflict, loop, qualityCheck } from './fixtures.js';
import {
  enabled,
  fire,
  initialMarking,
  markingKey,
  readNet,
  unfire,
} from './model.js';

/** A marking as a plain object, so that a test can write out what it expects. */
const tokens = (marking) => Object.fromEntries([...marking].filter(([, n]) => n !== 0));

test('a transition is enabled when every input place holds its arc weight', () => {
  const net = readNet({
    places: [{ id: 'a', marking: 2 }, { id: 'b', marking: 1 }],
    transitions: [{ id: 't' }],
    arcs: [
      { id: '1', source: 'a', target: 't', weight: 2 },
      { id: '2', source: 'b', target: 't', weight: 2 },
    ],
  });

  assert.deepEqual(enabled(net, initialMarking(net)), []);

  const enough = new Map([['a', 2], ['b', 2]]);
  assert.deepEqual(enabled(net, enough), ['t']);
});

test('firing moves tokens by the arc weights', () => {
  const net = readNet(loop);
  const after = fire(net, initialMarking(net), 'start');

  assert.deepEqual(tokens(after), { busy: 1 });
  assert.deepEqual(enabled(net, after), ['stop']);
});

test('firing one side of a conflict disables the other', () => {
  const net = readNet(conflict);
  const start = initialMarking(net);

  assert.deepEqual(enabled(net, start), ['toLeft', 'toRight']);
  assert.deepEqual(enabled(net, fire(net, start, 'toLeft')), []);
});

test('a self-loop leaves its place unchanged', () => {
  const net = readNet({
    places: [{ id: 'p', marking: 1 }],
    transitions: [{ id: 't' }],
    arcs: [
      { id: '1', source: 'p', target: 't' },
      { id: '2', source: 't', target: 'p' },
    ],
  });
  const start = initialMarking(net);

  assert.deepEqual(enabled(net, start), ['t']);
  assert.deepEqual(tokens(fire(net, start, 't')), { p: 1 });
});

test('two arcs from the same place need both their weights at once', () => {
  const net = readNet({
    places: [{ id: 'p', marking: 1 }],
    transitions: [{ id: 't' }],
    arcs: [
      { id: '1', source: 'p', target: 't' },
      { id: '2', source: 'p', target: 't' },
    ],
  });

  // One token, two arcs of weight one: the transition needs two.
  assert.deepEqual(enabled(net, initialMarking(net)), []);

  const two = new Map([['p', 2]]);
  assert.deepEqual(enabled(net, two), ['t']);
  assert.deepEqual(tokens(fire(net, two, 't')), {});
});

test('parallel arcs out of a transition add up as well', () => {
  const net = readNet({
    places: [{ id: 'p', marking: 1 }, { id: 'q' }],
    transitions: [{ id: 't' }],
    arcs: [
      { id: '1', source: 'p', target: 't' },
      { id: '2', source: 't', target: 'q' },
      { id: '3', source: 't', target: 'q', weight: 2 },
    ],
  });

  const after = fire(net, initialMarking(net), 't');
  assert.deepEqual(tokens(after), { q: 3 });
  assert.deepEqual(tokens(unfire(net, after, 't')), { p: 1 });
});

test('a marking that is not a number is rejected', () => {
  // null and undefined mean the place brought no marking, which is none.
  for (const marking of [true, [], {}, 'two']) {
    assert.throws(
      () => readNet({ places: [{ id: 'p', marking }], transitions: [], arcs: [] }),
      /marking of place p/,
      `marking ${JSON.stringify(marking)} should be refused`,
    );
  }
});

test('an identifier holding a comma or a colon keeps its own marking', () => {
  const odd = new Map([['a:1,b', 2]]);
  const plain = new Map([['a', 1], ['b', 2]]);

  assert.notEqual(markingKey(odd), markingKey(plain));
});

test('an arc weight of zero or less is rejected', () => {
  const net = { places: [{ id: 'p' }], transitions: [{ id: 't' }], arcs: [{ id: '1', source: 'p', target: 't' }] };

  assert.throws(() => readNet({ ...net, arcs: [{ ...net.arcs[0], weight: 0 }] }), /weight of arc 1 is 0/);
  assert.throws(() => readNet({ ...net, arcs: [{ ...net.arcs[0], weight: -2 }] }), /weight of arc 1 is -2/);
  assert.throws(() => readNet({ ...net, arcs: [{ ...net.arcs[0], weight: 1.5 }] }), /weight of arc 1 is 1.5/);
});

test('a negative or fractional marking is rejected', () => {
  assert.throws(
    () => readNet({ places: [{ id: 'p', marking: -1 }], transitions: [], arcs: [] }),
    /marking of place p is -1/,
  );
  assert.throws(
    () => readNet({ places: [{ id: 'p', marking: 0.5 }], transitions: [], arcs: [] }),
    /marking of place p is 0.5/,
  );
});

test('two elements with the same id are rejected', () => {
  assert.throws(
    () => readNet({ places: [{ id: 'x' }], transitions: [{ id: 'x' }], arcs: [] }),
    /id x is used by two elements/,
  );
});

test('an arc between two places is rejected', () => {
  assert.throws(
    () => readNet({
      places: [{ id: 'p' }, { id: 'q' }],
      transitions: [],
      arcs: [{ id: '1', source: 'p', target: 'q' }],
    }),
    /runs from a place to a place/,
  );
});

test('an arc end that is not in the net is rejected', () => {
  assert.throws(
    () => readNet({
      places: [{ id: 'p' }],
      transitions: [{ id: 't' }],
      arcs: [{ id: '1', source: 'p', target: 'gone' }],
    }),
    /arc 1 ends at gone, which is not in the net/,
  );
});

test('firing a transition that is not enabled is refused', () => {
  const net = readNet(loop);

  assert.throws(() => fire(net, initialMarking(net), 'stop'), /transition stop is not enabled/);
  assert.throws(() => fire(net, initialMarking(net), 'nothing'), /not a transition of this net/);
});

test('unfire returns the marking that firing came from', () => {
  const net = readNet(qualityCheck);
  const start = initialMarking(net);
  const checking = fire(net, start, 'Start');

  assert.deepEqual(tokens(unfire(net, checking, 'Start')), tokens(start));
});

test('unfire rejects a marking that cannot have come from this firing', () => {
  const net = readNet(loop);

  // stop puts a token into Idle, so a marking without one cannot follow it.
  const busyOnly = new Map([['idle', 0], ['busy', 1]]);
  assert.throws(() => unfire(net, busyOnly, 'stop'), /cannot have come from firing stop/);

  // The marking that does follow it goes back to the one it came from.
  assert.deepEqual(tokens(unfire(net, initialMarking(net), 'stop')), { busy: 1 });
});

test('equal markings have the same key whatever order they were built in', () => {
  const one = new Map([['a', 1], ['b', 2], ['c', 0]]);
  const other = new Map([['b', 2], ['c', 0], ['a', 1]]);

  assert.equal(markingKey(one), markingKey(other));
  assert.notEqual(markingKey(one), markingKey(new Map([['a', 2], ['b', 1]])));
});

test('the quality check net fires through one round', () => {
  const net = readNet(qualityCheck);
  let marking = initialMarking(net);

  assert.deepEqual(enabled(net, marking), ['Start']);
  for (const step of ['Start', 'Approve', 'Release']) {
    assert.ok(enabled(net, marking).includes(step), `${step} should be enabled`);
    marking = fire(net, marking, step);
  }

  // Back at the start, minus the result token the check consumed.
  assert.deepEqual(tokens(marking), { Idle: 1, Result_NOK: 1 });
});
