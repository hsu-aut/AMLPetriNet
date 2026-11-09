import assert from 'node:assert/strict';
import { test } from 'node:test';

import { conflict, deadlock, loop, producer, qualityCheck } from './fixtures.js';
import { explore } from './explore.js';
import { markingKey, readNet } from './model.js';
import { matching, parseQuery, pathTo, pathToDeadlock, wayTo } from './paths.js';

test('the way to the initial marking is no firing at all', () => {
  const net = readNet(loop);
  const result = explore(net);

  const path = pathTo(result, result.initial);
  assert.deepEqual(path.transitions, []);
  assert.deepEqual(path.markings, [result.initial]);
});

test('the way to the other marking of a loop is one firing', () => {
  const net = readNet(loop);
  const result = explore(net);
  const busy = markingKey(new Map([['idle', 0], ['busy', 1]]));

  const path = pathTo(result, busy);
  assert.deepEqual(path.transitions, ['start']);
  // The markings on the way include both ends, so a player can check each step.
  assert.deepEqual(path.markings, [result.initial, busy]);
});

test('a marking the edges do not lead to has no way to it', () => {
  const net = readNet(loop);
  const result = explore(net);

  assert.equal(pathTo(result, markingKey(new Map([['idle', 7]]))), null);
});

test('the way into a deadlock is the firing sequence that gets stuck', () => {
  const net = readNet(deadlock);
  const result = explore(net);

  const path = pathToDeadlock(result);
  // One token in, one firing of Step, and Pair wants two tokens in Mid.
  assert.deepEqual(path.transitions, ['step']);
  assert.ok(result.deadlocks.includes(path.key));
});

test('a net with two deadlocks gives the shortest way to one of them', () => {
  const net = readNet(conflict);
  const result = explore(net);

  const path = pathToDeadlock(result);
  assert.equal(path.transitions.length, 1);
  assert.ok(['toLeft', 'toRight'].includes(path.transitions[0]));
});

test('a net that does not deadlock has no way into one', () => {
  assert.equal(pathToDeadlock(explore(readNet(loop))), null);
});

test('a deadlock that stands for a family of markings is not offered as a way', () => {
  // The producer fills its place without end, so the search abstracts the place
  // into omega. Nothing there is a marking a run can end in.
  const result = explore(readNet(producer));
  assert.equal(pathToDeadlock(result), null);
});

test('a query names places by id, by name and without a count', () => {
  const net = readNet(qualityCheck);

  assert.deepEqual(parseQuery('Approved = 1', net).wanted, new Map([['Approved', 1]]));
  // The fixture's names equal its ids, so the lower case form proves that a
  // name is matched without regard to case.
  assert.deepEqual(parseQuery('approved', net).wanted, new Map([['Approved', 1]]));
  assert.deepEqual(parseQuery('Idle=0, Checking: 1', net).wanted, new Map([['Idle', 0], ['Checking', 1]]));
});

test('a query that cannot be meant is refused with the reason', () => {
  const net = readNet(qualityCheck);

  assert.match(parseQuery('Nowhere = 1', net).problem, /no place called "Nowhere"/);
  assert.match(parseQuery('Idle = 1, Idle = 2', net).problem, /twice/);
  assert.match(parseQuery('Idle = two', net).problem, /whole number/);
  assert.match(parseQuery('Idle = -1', net).problem, /whole number/);
  assert.match(parseQuery('   ', net).problem, /Name a place/);
});

test('two places of one name are refused rather than guessed', () => {
  const net = readNet({
    places: [{ id: 'p1', name: 'Store', marking: 1 }, { id: 'p2', name: 'Store', marking: 0 }],
    transitions: [],
    arcs: [],
  });

  assert.match(parseQuery('Store = 1', net).problem, /Two places are called "Store"/);
});

test('a query is answered with the way to a marking that holds it', () => {
  const net = readNet(qualityCheck);
  const result = explore(net);

  const way = wayTo(result, parseQuery('Approved = 1', net).wanted);
  assert.deepEqual(way.transitions, ['Start', 'Approve']);
  assert.equal(way.abstract, false);
});

test('places the query leaves out are free', () => {
  const net = readNet(qualityCheck);
  const result = explore(net);

  // Asking only about Checking finds the marking after Start although that
  // marking also holds the two result tokens.
  const way = wayTo(result, parseQuery('Checking = 1', net).wanted);
  assert.deepEqual(way.transitions, ['Start']);
});

test('a marking the net cannot reach is answered with nothing', () => {
  const net = readNet(qualityCheck);
  const result = explore(net);

  assert.equal(wayTo(result, parseQuery('Approved = 1, Rejected = 1', net).wanted), null);
});

test('a marking only an omega node holds is reported as a family, not as one', () => {
  const net = readNet(producer);
  const result = explore(net);
  const wanted = parseQuery('Stock = 3', net).wanted;

  const { exact, abstract } = matching(result, wanted);
  assert.deepEqual(exact, []);
  assert.equal(abstract.length, 1);

  const way = wayTo(result, wanted);
  assert.equal(way.abstract, true);
});
