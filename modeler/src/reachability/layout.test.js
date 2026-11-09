import assert from 'node:assert/strict';
import { test } from 'node:test';

import { conflict, deadlock, loop } from '../net/fixtures.js';
import { readNet } from '../net/model.js';
import { explore } from '../net/explore.js';
import { layoutStateSpace } from './layout.js';

const laid = (fixture) => layoutStateSpace(explore(readNet(fixture)));

test('the initial marking is the first node and is marked as such', () => {
  const { nodes } = laid(loop);

  assert.equal(nodes[0].initial, true);
  assert.equal(nodes.filter((node) => node.initial).length, 1);
});

test('markings stand in the column of the firings it takes to reach them', () => {
  const { nodes } = laid(deadlock);

  // In -> Step -> Mid, so two markings, one firing apart.
  assert.equal(nodes.length, 2);
  assert.ok(nodes[1].x > nodes[0].x, 'the successor should stand to the right');
  assert.equal(nodes[0].y, nodes[1].y, 'one marking per column, so both on one line');
});

test('markings the same number of firings away share a column', () => {
  const { nodes } = laid(conflict);

  const [start, left, right] = nodes;
  assert.equal(left.x, right.x, 'both successors are one firing away');
  assert.ok(left.x > start.x);
  assert.notEqual(left.y, right.y, 'and they must not sit on top of each other');
});

test('a marking in which nothing is enabled is marked as a dead end', () => {
  const { nodes } = laid(deadlock);

  assert.deepEqual(nodes.map((node) => node.deadlock), [false, true]);
});

test('every edge leads between two nodes that are on screen', () => {
  const { nodes, edges } = laid(conflict);
  const keys = new Set(nodes.map((node) => node.key));

  assert.equal(edges.length, 2);
  for (const edge of edges) {
    assert.ok(keys.has(edge.from) && keys.has(edge.to), 'an edge points at a marking that is not drawn');
    assert.ok(edge.transition, 'an edge does not say which transition it is');
  }
});

test('the size covers every node', () => {
  const { nodes, width, height } = laid(conflict);

  for (const node of nodes) {
    assert.ok(node.x <= width, `${node.x} is outside the width ${width}`);
    assert.ok(node.y <= height, `${node.y} is outside the height ${height}`);
  }
});

test('a net whose state space was cut off is still laid out', () => {
  const result = explore(readNet(loop), { markings: 1 });
  const { nodes, edges } = layoutStateSpace(result);

  assert.equal(nodes.length, 1);
  assert.deepEqual(edges, [], 'an edge to a marking that was never reached is not drawn');
});
