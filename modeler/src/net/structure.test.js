import assert from 'node:assert/strict';
import { test } from 'node:test';

import { loop, producer, resourceDeadlock } from './fixtures.js';
import { explore } from './explore.js';
import { initialMarking, readNet } from './model.js';
import { holdsToken, maximalSiphon, maximalTrap, siphons, traps } from './structure.js';

test('the two resources of the deadlock net are a siphon', () => {
  const net = readNet(resourceDeadlock);

  // By hand: both transitions that fill R1 or R2 are Done 1 and Done 2, and
  // both of them also take from one of the two, so the set cannot refill once
  // it is empty. Neither resource alone is a siphon: Done 1 fills R1 without
  // taking from R1.
  assert.deepEqual(maximalSiphon(net, ['r1', 'r2']), ['r1', 'r2']);
  assert.deepEqual(maximalSiphon(net, ['r1']), []);
  assert.deepEqual(maximalSiphon(net, ['r2']), []);
});

test('the two resources hold no trap, which is where the deadlock comes from', () => {
  const net = readNet(resourceDeadlock);

  // Take 1 takes R1 and puts nothing back into either resource, so R1 is in no
  // trap inside the set, and the same goes for R2 through Take 2.
  assert.deepEqual(maximalTrap(net, ['r1', 'r2']), []);
});

test('the deadlock net really deadlocks, so the structure was not crying wolf', () => {
  const result = explore(readNet(resourceDeadlock));

  assert.ok(result.deadlocks.length > 0, 'the net was expected to get stuck');
});

test('the minimal siphons of the deadlock net include the two resources', () => {
  const net = readNet(resourceDeadlock);
  const { sets, complete } = siphons(net);

  assert.equal(complete, true);
  assert.ok(sets.some((set) => set.join() === 'r1,r2'),
    `expected the resources among ${JSON.stringify(sets)}`);
  // Minimal means no reported set holds another one.
  for (const set of sets) {
    for (const other of sets) {
      if (set === other) continue;
      assert.ok(!other.every((place) => set.includes(place)),
        `${JSON.stringify(set)} holds ${JSON.stringify(other)}, so it is not minimal`);
    }
  }
});

test('the loop is its own siphon and its own trap', () => {
  const net = readNet(loop);

  // One token going round: the pair of places can neither empty nor fill.
  assert.deepEqual(siphons(net).sets, [['idle', 'busy']]);
  assert.deepEqual(traps(net).sets, [['idle', 'busy']]);
  assert.equal(holdsToken(['idle', 'busy'], initialMarking(net)), true);
});

test('a place that is only filled is in no siphon', () => {
  const net = readNet(producer);

  assert.deepEqual(siphons(net).sets, []);
  // It is a trap, because nothing takes from it at all, but an empty one.
  assert.deepEqual(traps(net).sets, [['stock']]);
  assert.equal(holdsToken(['stock'], initialMarking(net)), false);
});

test('a net too large for the enumeration says so rather than hanging', () => {
  // Eighty places in a chain: each place feeds the next transition. The cap is
  // on the number of places, so the walk is not even started.
  const places = [];
  const transitions = [];
  const arcs = [];
  for (let i = 0; i < 80; i += 1) {
    places.push({ id: `p${i}`, marking: i === 0 ? 1 : 0 });
    transitions.push({ id: `t${i}` });
    arcs.push({ id: `in${i}`, source: `p${i}`, target: `t${i}` });
    arcs.push({ id: `out${i}`, source: `t${i}`, target: `p${(i + 1) % 80}` });
  }

  const net = readNet({ places, transitions, arcs });
  const started = Date.now();
  const result = siphons(net);

  assert.equal(result.complete, false);
  assert.equal(result.stopped, 'places');
  assert.deepEqual(result.sets, []);
  assert.ok(Date.now() - started < 1000, 'the refusal took longer than a second');
});

test('a net the enumeration cannot finish reports what it found as partial', () => {
  // The cap on sets, met on a net that has many siphons: twelve places that
  // each form a cycle of their own with one shared transition.
  const places = [];
  const transitions = [{ id: 'shared' }];
  const arcs = [];
  for (let i = 0; i < 12; i += 1) {
    places.push({ id: `p${i}`, marking: 1 });
    arcs.push({ id: `a${i}`, source: `p${i}`, target: 'shared' });
    arcs.push({ id: `b${i}`, source: 'shared', target: `p${i}` });
  }

  const result = siphons(readNet({ places, transitions, arcs }), { sets: 3 });
  assert.equal(result.complete, false);
  assert.equal(result.stopped, 'sets');
});
