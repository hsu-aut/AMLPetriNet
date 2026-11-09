import assert from 'node:assert/strict';
import { test } from 'node:test';

import { conflict, deadlock, loop, producer, qualityCheck } from './fixtures.js';
import { readNet } from './model.js';
import { bounds, explore, OMEGA } from './explore.js';

test('a loop has as many markings as it has states', () => {
  const result = explore(readNet(loop));

  // One token, two places: Idle or Busy.
  assert.equal(result.markings.length, 2);
  assert.equal(result.edges.length, 2);
  assert.deepEqual(result.deadlocks, []);
  assert.equal(result.complete, true);
  assert.deepEqual(result.unbounded, []);
});

test('a conflict branches into both successors', () => {
  const result = explore(readNet(conflict));

  assert.equal(result.markings.length, 3);
  assert.deepEqual(result.edges.map((edge) => edge.transition).sort(), ['toLeft', 'toRight']);
  // Both ends are stuck: the token is gone from the choice.
  assert.equal(result.deadlocks.length, 2);
});

test('a producer without a bound reports its place and stays finite', () => {
  const result = explore(readNet(producer));

  assert.deepEqual(result.unbounded, ['stock']);
  assert.equal(result.complete, true);
  assert.ok(result.markings.length <= 3, `expected a handful of markings, got ${result.markings.length}`);
  assert.equal(bounds(result).get('stock'), OMEGA);
});

test('the limit is reported as the reason it stopped', () => {
  const result = explore(readNet(producer), { markings: 1, milliseconds: 1000 });

  assert.equal(result.complete, false);
  assert.equal(result.stopped, 'markings');

  // A clock that jumps past the limit on the second look, so the test does not
  // depend on how fast the machine is.
  let tick = 0;
  const outOfTime = explore(readNet(qualityCheck), {
    markings: 100000,
    milliseconds: 10,
    now: () => (tick++ === 0 ? 0 : 1000),
  });
  assert.equal(outOfTime.stopped, 'time');
  assert.equal(outOfTime.complete, false);
});

test('a transition without an input place does not run away', () => {
  const net = readNet({
    places: [{ id: 'p' }, { id: 'q' }],
    transitions: [{ id: 'free' }, { id: 'move' }],
    arcs: [
      { id: '1', source: 'free', target: 'p' },
      { id: '2', source: 'p', target: 'move' },
      { id: '3', source: 'move', target: 'q' },
    ],
  });

  const result = explore(net, { markings: 500, milliseconds: 1000 });

  assert.ok(result.unbounded.includes('p') || result.unbounded.includes('q'),
    `expected growth to be seen, got ${JSON.stringify(result.unbounded)}`);
  assert.ok(result.markings.length < 500, `expected the search to settle, got ${result.markings.length}`);
});

test('a net that gets stuck names the marking it is stuck in', () => {
  const result = explore(readNet(deadlock));

  assert.equal(result.deadlocks.length, 1);
  const stuck = result.markings.find((entry) => entry.key === result.deadlocks[0]);
  assert.deepEqual(Object.fromEntries([...stuck.marking].filter(([, n]) => n)), { mid: 1 });
  assert.deepEqual(result.fired, ['step']);
});

test('the quality check net runs dry when the results are used up', () => {
  const result = explore(readNet(qualityCheck));
  const tokens = (key) => Object.fromEntries(
    [...result.markings.find((entry) => entry.key === key).marking].filter(([, n]) => n),
  );

  assert.equal(result.complete, true);
  assert.deepEqual(result.unbounded, []);
  assert.equal(result.fired.length, 5, 'every transition fires somewhere');

  // Each round consumes one result token, so the station returns to Idle with
  // one result fewer, and the last check finds none: Checking is where it
  // stops. That is a property of the net, not of the search.
  assert.deepEqual(result.deadlocks.map(tokens), [{ Checking: 1 }]);
  assert.ok(result.markings.some((entry) => {
    const held = tokens(entry.key);
    return held.Idle === 1 && held.Result_OK === undefined && held.Result_NOK === undefined;
  }), 'the station reaches Idle with both results used');
});
