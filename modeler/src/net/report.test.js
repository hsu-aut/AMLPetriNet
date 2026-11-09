import assert from 'node:assert/strict';
import { test } from 'node:test';

import { deadlock, loop, producer } from './fixtures.js';
import { analyse } from './analyse.js';
import { explore } from './explore.js';
import { readNet } from './model.js';
import { findingsToCsv, markingsToCsv, toDot, writtenKey } from './report.js';

test('the state space of a loop is two nodes and two edges in DOT', () => {
  const net = readNet(loop);
  const dot = toDot(explore(net), net);

  assert.match(dot, /^digraph state_space \{/);
  assert.match(dot, /m0 \[label="Idle 1", shape=doublecircle\];/);
  assert.match(dot, /m1 \[label="Busy 1"\];/);
  assert.match(dot, /m0 -> m1 \[label="Start"\];/);
  assert.match(dot, /m1 -> m0 \[label="Stop"\];/);
  assert.match(dot, /\}\n$/);
  assert.equal((dot.match(/->/g) ?? []).length, 2);
});

test('a dead end is marked in the DOT as well', () => {
  const net = readNet(deadlock);
  const dot = toDot(explore(net), net);

  // The marking the net gets stuck in: one token in Mid and Pair wants two.
  assert.match(dot, /label="Mid 1", color="#d93025"/);
});

test('a name with a quote in it does not break the DOT', () => {
  const net = readNet({
    places: [{ id: 'p', name: 'The "big" store', marking: 1 }],
    transitions: [{ id: 't', name: 'A \\ B' }],
    arcs: [{ id: 'a', source: 'p', target: 't' }],
  });

  const dot = toDot(explore(net), net);
  assert.match(dot, /label="The \\"big\\" store 1"/);
  assert.match(dot, /label="A \\\\ B"/);
});

test('the markings come out as a table with a column per place', () => {
  const net = readNet(loop);
  const csv = markingsToCsv(explore(net), net);
  const rows = csv.trim().split('\n');

  assert.equal(rows[0], 'marking,firings,deadlock,Idle,Busy');
  assert.equal(rows[1], 'initial,0,no,1,0');
  assert.equal(rows[2], 'm1,1,no,0,1');
  assert.equal(rows.length, 3);
});

test('a place without a bound is written with the letter the literature uses', () => {
  const net = readNet(producer);
  const csv = markingsToCsv(explore(net), net);

  assert.match(csv, /ω/);
  assert.match(toDot(explore(net), net), /Stock ω/);
});

test('a findings table holds the rule, the severity, the element and the sentence', () => {
  const net = readNet(deadlock);
  const { findings } = analyse(net);
  const rows = findingsToCsv(findings).trim().split('\n');

  assert.equal(rows[0], 'rule,severity,element,message');
  assert.equal(rows.length, findings.length + 1);
  // Every message ends in a full stop, so every field holding one is quoted
  // only where it has a comma in it; one such row is checked by hand.
  const dead = rows.find((row) => row.startsWith('PT14'));
  assert.equal(dead, 'PT14,warning,pair,Pair can never fire: no reachable marking enables it.');
});

test('a field with a comma or a quote in it is quoted the way a spreadsheet reads it', () => {
  const csv = findingsToCsv([
    { rule: 'PT99', severity: 'information', element: 'p', message: 'Holds one, maybe two.' },
    { rule: 'PT99', severity: 'information', element: 'p', message: 'The "big" store.' },
  ]);

  assert.match(csv, /PT99,information,p,"Holds one, maybe two\."/);
  assert.match(csv, /PT99,information,p,"The ""big"" store\."/);
});

test('a marking key can be written out on its own', () => {
  const net = readNet(loop);
  const result = explore(net);

  assert.equal(writtenKey(result.initial, net), 'Idle 1');
});

test('an empty table is a header and nothing else', () => {
  assert.equal(findingsToCsv([]), 'rule,severity,element,message\n');
  assert.equal(findingsToCsv(null), 'rule,severity,element,message\n');
});
