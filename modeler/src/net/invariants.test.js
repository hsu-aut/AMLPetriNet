import assert from 'node:assert/strict';
import { test } from 'node:test';

import { conflict, loop, producer, qualityCheck } from './fixtures.js';
import { enabled, fire, initialMarking, readNet } from './model.js';
import { placeInvariants, sequenceFor, transitionInvariants } from './invariants.js';

/** An invariant as a plain object, so a test can write out what it expects. */
const plain = (invariant) => Object.fromEntries(invariant);

/** The weighted token count of a marking under an invariant. */
const weigh = (invariant, marking) =>
  [...invariant].reduce((sum, [place, weight]) => sum + weight * (marking.get(place) ?? 0), 0);

test('the loop conserves its one token', () => {
  const net = readNet(loop);
  const invariants = placeInvariants(net);

  assert.deepEqual(invariants.map(plain), [{ idle: 1, busy: 1 }]);
  assert.equal(weigh(invariants[0], initialMarking(net)), 1);
});

test('the loop has one firing sequence that returns the marking', () => {
  assert.deepEqual(transitionInvariants(readNet(loop)).map(plain), [{ start: 1, stop: 1 }]);
});

test('a choice conserves its token wherever it goes', () => {
  const net = readNet(conflict);
  const invariants = placeInvariants(net);

  // By hand: both transitions take from Choice, so a weighting that is to stay
  // constant has to give Left and Right the weight of Choice. That makes one
  // invariant over all three places, not one per branch.
  assert.deepEqual(invariants.map(plain), [{ p: 1, left: 1, right: 1 }]);
  assert.equal(weigh(invariants[0], initialMarking(net)), 1);
});

test('a net that only produces conserves nothing and repeats nothing', () => {
  const net = readNet(producer);

  assert.deepEqual(placeInvariants(net), []);
  assert.deepEqual(transitionInvariants(net), []);
});

test('the quality check net conserves the station and both results', () => {
  const net = readNet(qualityCheck);
  const invariants = placeInvariants(net);
  const marking = initialMarking(net);

  // Computed by hand: the station is in exactly one of its five states, and
  // each result token is either still there or has been consumed by its check.
  assert.ok(invariants.some((invariant) => JSON.stringify(plain(invariant)) === JSON.stringify({
    Idle: 1, Checking: 1, Approved: 1, Rejected: 1,
  })), `expected the station invariant, got ${JSON.stringify(invariants.map(plain))}`);

  // Exactly one, computed by hand: the station is in one of its four states.
  // A second invariant in the output, or a missing one, is a defect.
  assert.equal(invariants.length, 1);

  // A conservation law has to survive firing, so fire through the net and
  // weigh after every step.
  for (const invariant of invariants) {
    const start = weigh(invariant, marking);
    let current = marking;
    for (let step = 0; step < 20; step += 1) {
      const ready = enabled(net, current);
      if (ready.length === 0) break;
      current = fire(net, current, ready[0]);
      assert.equal(weigh(invariant, current), start,
        `the weighted token count changed after firing ${ready[0]}`);
    }
    for (const [, weight] of invariant) assert.ok(weight > 0, 'weights are positive');
  }
});

test('a weighting that is not an invariant does change when the net fires', () => {
  // The guard for the test above: if weighing were wrong, or if the net never
  // moved, the conservation check would pass for anything.
  const net = readNet(qualityCheck);
  const wrong = new Map([['Idle', 1], ['Checking', 2]]);
  const start = initialMarking(net);

  assert.notEqual(weigh(wrong, fire(net, start, 'Start')), weigh(wrong, start));
});

test('the quality check net repeats nothing while its results are consumed', () => {
  const invariants = transitionInvariants(readNet(qualityCheck));

  // By hand: Result_OK has one arc, into Approve, so any firing count that is
  // to return the marking has to fire Approve zero times; the same for Reject.
  // Without them nothing else can repeat either. The station only looks
  // cyclic: the two result places drain, which is what the state space shows
  // as the dead end in Checking.
  assert.deepEqual(invariants, []);
});

test('a station that puts its results back repeats both rounds', () => {
  // The same net with the two results returned at the end of each round, which
  // is the cyclic version of the station.
  const cyclic = readNet({
    ...qualityCheck,
    arcs: [
      ...qualityCheck.arcs,
      { id: 'e13', source: 'Release', target: 'Result_OK' },
      { id: 'e14', source: 'Discard', target: 'Result_NOK' },
    ],
  });

  const invariants = transitionInvariants(cyclic).map(plain);

  assert.ok(invariants.some((i) => JSON.stringify(i) === JSON.stringify({ Start: 1, Approve: 1, Release: 1 })),
    `expected the approve round, got ${JSON.stringify(invariants)}`);
  assert.ok(invariants.some((i) => JSON.stringify(i) === JSON.stringify({ Start: 1, Reject: 1, Discard: 1 })),
    `expected the reject round, got ${JSON.stringify(invariants)}`);
});

test('a transition invariant is handed back as an order that fires', () => {
  const net = readNet(loop);
  const [invariant] = transitionInvariants(net);

  const sequence = sequenceFor(net, invariant, initialMarking(net));
  assert.deepEqual(sequence, ['start', 'stop']);
});

test('firing the order of an invariant returns the marking it started from', () => {
  const cyclic = readNet({
    ...qualityCheck,
    arcs: [
      ...qualityCheck.arcs,
      { id: 'e13', source: 'Release', target: 'Result_OK' },
      { id: 'e14', source: 'Discard', target: 'Result_NOK' },
    ],
  });
  const start = initialMarking(cyclic);

  for (const invariant of transitionInvariants(cyclic)) {
    const sequence = sequenceFor(cyclic, invariant, start);
    assert.ok(sequence, `no order fires ${JSON.stringify(Object.fromEntries(invariant))}`);

    let marking = start;
    for (const transition of sequence) marking = fire(cyclic, marking, transition);
    assert.deepEqual([...marking].sort(), [...start].sort(),
      `firing ${sequence.join(', ')} did not return the marking`);
  }
});

test('the order is searched for, not guessed at in the order given', () => {
  // Hand-made rather than an invariant of a net: A empties the place, B needs it
  // and puts it back. A before B does not fire, B before A does, and A is named
  // first, so a search that takes the first enabled transition and keeps it
  // fails here.
  const net = readNet({
    places: [{ id: 'p', marking: 1 }],
    transitions: [{ id: 'A' }, { id: 'B' }],
    arcs: [
      { id: 'x1', source: 'p', target: 'A' },
      { id: 'x2', source: 'p', target: 'B' },
      { id: 'x3', source: 'B', target: 'p' },
    ],
  });

  const sequence = sequenceFor(net, new Map([['A', 1], ['B', 1]]), initialMarking(net));
  assert.deepEqual(sequence, ['B', 'A']);
});

test('firings no order can fire are refused rather than half done', () => {
  const net = readNet(loop);

  // Stop fires only after Start, so two firings of Stop are not a run.
  assert.equal(sequenceFor(net, new Map([['stop', 2]]), initialMarking(net)), null);
});
