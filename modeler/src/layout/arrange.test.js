import assert from 'node:assert/strict';
import { test } from 'node:test';

import { arrange } from './arrange.js';

/** A net as the layout takes it: nodes with a size, arcs with two ends. */
const net = (ids, arcs, size = { width: 50, height: 50 }) => ({
  nodes: ids.map((id) => ({ id, ...size })),
  arcs: arcs.map(([source, target], i) => ({ id: `a${i}`, source, target })),
});

test('a chain comes out as a row, in the direction of the flow', () => {
  const { nodes, arcs } = net(['p1', 't1', 'p2', 't2', 'p3'], [
    ['p1', 't1'], ['t1', 'p2'], ['p2', 't2'], ['t2', 'p3'],
  ]);

  const at = arrange(nodes, arcs);
  const x = (id) => at.get(id).x;

  assert.ok(x('p1') < x('t1'), 'the first place is not left of the first transition');
  assert.ok(x('t1') < x('p2') && x('p2') < x('t2') && x('t2') < x('p3'),
    `the chain came out as ${JSON.stringify([...at])}`);
  // One row: a chain has nothing to put beside anything.
  assert.equal(new Set([...at.values()].map((point) => point.y)).size, 1);
});

test('nothing lands on top of anything', () => {
  // Two chains that meet, which is where a careless layout puts two elements on
  // one spot.
  const { nodes, arcs } = net(['a', 'b', 'join', 'c', 'd'], [
    ['a', 'join'], ['b', 'join'], ['join', 'c'], ['join', 'd'],
  ]);

  const at = arrange(nodes, arcs);
  const spots = [...at.values()].map((point) => `${point.x}/${point.y}`);

  assert.equal(new Set(spots).size, spots.length, `two elements share a spot: ${spots}`);
});

test('a cycle is laid out rather than refused', () => {
  const { nodes, arcs } = net(['idle', 'start', 'busy', 'stop'], [
    ['idle', 'start'], ['start', 'busy'], ['busy', 'stop'], ['stop', 'idle'],
  ]);

  const at = arrange(nodes, arcs);

  assert.equal(at.size, 4);
  // The arc that closes the cycle is the one laid out backwards; the other
  // three read left to right.
  assert.ok(at.get('idle').x < at.get('start').x);
  assert.ok(at.get('start').x < at.get('busy').x);
  assert.ok(at.get('busy').x < at.get('stop').x);
});

test('an element nothing connects to still gets a place', () => {
  const { nodes, arcs } = net(['alone', 'p', 't'], [['p', 't']]);

  const at = arrange(nodes, arcs);
  assert.equal(at.size, 3);
  assert.ok(Number.isFinite(at.get('alone').x) && Number.isFinite(at.get('alone').y));
});

test('the same net is laid out the same way twice', () => {
  const { nodes, arcs } = net(['a', 'b', 'c', 'join', 'd'], [
    ['a', 'join'], ['b', 'join'], ['c', 'join'], ['join', 'd'],
  ]);

  assert.deepEqual([...arrange(nodes, arcs)], [...arrange(nodes, arcs)]);
});

test('the position is the corner of the shape, not its middle', () => {
  const { nodes, arcs } = net(['p', 't'], [['p', 't']], { width: 60, height: 40 });

  const at = arrange(nodes, arcs, { margin: 100, column: 200, row: 100 });

  // Middle at 100/100 for the first column, so the corner is half a shape up
  // and to the left of it.
  assert.deepEqual(at.get('p'), { x: 70, y: 80 });
  assert.deepEqual(at.get('t'), { x: 270, y: 80 });
});

test('an arc whose end is not a node is ignored rather than breaking the layout', () => {
  const { nodes } = net(['p', 't'], []);
  const arcs = [{ id: 'a', source: 'p', target: 'gone' }, { id: 'b', source: 'p', target: 't' }];

  const at = arrange(nodes, arcs);
  assert.ok(at.get('p').x < at.get('t').x);
});

test('a big net is laid out in no time', () => {
  const ids = [];
  const arcs = [];
  for (let i = 0; i < 200; i += 1) {
    ids.push(`p${i}`, `t${i}`);
    arcs.push([`p${i}`, `t${i}`], [`t${i}`, `p${(i + 1) % 200}`]);
  }

  const { nodes, arcs: edges } = net(ids, arcs);
  const started = Date.now();
  const at = arrange(nodes, edges);

  assert.equal(at.size, 400);
  assert.ok(Date.now() - started < 3000, `the layout took ${Date.now() - started} ms`);
});
