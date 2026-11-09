import assert from 'node:assert/strict';
import { test } from 'node:test';

import { deadlock, loop, producer, qualityCheck } from './fixtures.js';
import { readNet } from './model.js';
import { analyse } from './analyse.js';

const rules = (findings) => findings.map((finding) => finding.rule);
const of = (findings, rule) => findings.filter((finding) => finding.rule === rule);

test('a loop has no warning and a bound for every place', () => {
  const { findings, complete } = analyse(readNet(loop));

  assert.equal(complete, true);
  assert.deepEqual(findings.filter((f) => f.severity === 'warning'), []);
  assert.deepEqual(of(findings, 'PT13').map((f) => [f.element, f.message]),
    [['idle', 'Idle holds at most one token.'], ['busy', 'Busy holds at most one token.']]);
});

test('a deadlocking net names the marking it is stuck in', () => {
  const { findings } = analyse(readNet(deadlock));

  const stuck = of(findings, 'PT15');
  assert.equal(stuck.length, 1);
  assert.match(stuck[0].message, /Mid 1/);
  assert.equal(stuck[0].severity, 'warning');
});

test('a place that is never marked says so', () => {
  const { findings } = analyse(readNet(deadlock));

  const out = of(findings, 'PT13').find((f) => f.element === 'out');
  assert.equal(out.message, 'Out never holds a token.');
});

test('a transition that can never fire is reported as dead', () => {
  const { findings } = analyse(readNet(deadlock));

  const dead = of(findings, 'PT14');
  assert.deepEqual(dead.map((f) => f.element), ['pair']);
  assert.match(dead[0].message, /can never fire/);
});

test('an unbounded place is reported as one, bounded places carry their bound', () => {
  const { findings } = analyse(readNet(producer));

  assert.deepEqual(of(findings, 'PT12').map((f) => f.element), ['stock']);
  assert.deepEqual(of(findings, 'PT13'), []);
});

test('the quality check net reports its dead end and no unbounded place', () => {
  const { findings, complete } = analyse(readNet(qualityCheck));

  assert.equal(complete, true);
  assert.deepEqual(of(findings, 'PT12'), []);
  assert.deepEqual(of(findings, 'PT14'), [], 'every transition fires somewhere');
  assert.equal(of(findings, 'PT15').length, 1);
  assert.match(of(findings, 'PT15')[0].message, /Checking 1/);
});

test('a cut off search still reports what it reached, and says that it stopped', () => {
  const { findings, complete } = analyse(readNet(producer), { markings: 1 });

  assert.equal(complete, false);
  assert.ok(rules(findings).includes('PT17'));
  assert.match(of(findings, 'PT17')[0].message, /stopped at its limit on markings/);
  // The places are still described, from the one marking that was reached.
  assert.ok(rules(findings).includes('PT13') || rules(findings).includes('PT12'));
});

test('the invariants of a net are reported', () => {
  const { findings } = analyse(readNet(loop));

  const conservation = of(findings, 'PT18');
  assert.equal(conservation.length, 1);
  assert.equal(conservation[0].message,
    'The token count of Idle and Busy is the same in every reachable marking.');

  const cycle = of(findings, 'PT19');
  assert.equal(cycle.length, 1);
  assert.equal(cycle[0].message,
    'Firing Start once and Stop once returns the net to the marking it started from.');
});

test('every finding names a rule, a severity and a sentence', () => {
  const { findings } = analyse(readNet(qualityCheck));

  for (const finding of findings) {
    assert.match(finding.rule, /^PT1[2-9]$/);
    assert.ok(['error', 'warning', 'information'].includes(finding.severity), finding.severity);
    assert.match(finding.message, /\.$/, `message should be a sentence: ${finding.message}`);
  }
});
