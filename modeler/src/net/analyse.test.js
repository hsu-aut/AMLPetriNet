import assert from 'node:assert/strict';
import { test } from 'node:test';

import { deadlock, loop, producer, qualityCheck, resourceDeadlock } from './fixtures.js';
import { enabled, fire, initialMarking, readNet } from './model.js';
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

test('a cut off search says so and claims nothing it cannot know', () => {
  const { findings, complete } = analyse(readNet(deadlock), { markings: 1 });

  assert.equal(complete, false);
  assert.match(of(findings, 'PT17')[0].message, /stopped at its limit on markings/);

  // Pair never fired, but with one marking searched that is an observation,
  // not the warning "can never fire" the full search is allowed to make.
  const notFired = of(findings, 'PT14');
  assert.deepEqual(notFired.map((f) => [f.element, f.severity]), [['pair', 'information']]);
  assert.match(notFired[0].message, /did not fire in the markings the search reached/);

  // Nothing is claimed about liveness from a search that stopped early.
  assert.deepEqual(of(findings, 'PT16'), []);
});

test('a net with an unbounded place does not claim to be fully searched', () => {
  const { findings, complete } = analyse(readNet(producer));

  assert.equal(complete, false, 'omega stands for markings rather than listing them');
  assert.match(of(findings, 'PT17')[0].message, /a deadlock it did not find can still exist/);
});

test('a transition that stops being available is reported as not live', () => {
  const { findings } = analyse(readNet(qualityCheck));

  const notLive = of(findings, 'PT16').map((f) => f.element).sort();
  // Each round consumes a result token, so every transition of the station
  // eventually has nothing left to do.
  assert.deepEqual(notLive, ['Approve', 'Discard', 'Reject', 'Release', 'Start'].sort());
});

test('a live net reports no transition as not live', () => {
  const { findings } = analyse(readNet(loop));

  assert.deepEqual(of(findings, 'PT16'), []);
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
    assert.match(finding.rule, /^PT(1[2-9]|2[01])$/);
    assert.ok(['error', 'warning', 'information'].includes(finding.severity), finding.severity);
    assert.match(finding.message, /\.$/, `message should be a sentence: ${finding.message}`);
  }
});

test('the deadlock net is told why it can get stuck, from the structure', () => {
  const { findings } = analyse(readNet(resourceDeadlock));

  const siphon = of(findings, 'PT20');
  assert.equal(siphon.length >= 1, true, 'no siphon was reported');
  // The two resources: marked now, and nothing inside them keeps them marked.
  const resources = siphon.find((finding) => /R1 and R2 ever run empty/.test(finding.message));
  assert.ok(resources, `expected the resources, got ${JSON.stringify(siphon.map((f) => f.message))}`);
  assert.deepEqual(resources.cover, [['r1', 1], ['r2', 1]]);
  assert.equal(resources.severity, 'information');
});

test('a set of places that can never hold a token is a warning', () => {
  // The same net with the resources taken out of the initial marking: nothing
  // can ever run.
  const empty = readNet({
    ...resourceDeadlock,
    places: resourceDeadlock.places.map((place) =>
      (place.id === 'r1' || place.id === 'r2' ? { ...place, marking: 0 } : place)),
  });

  // Several sets are unmarked siphons in this net, the two resources among
  // them; each of them is a warning, and the resources are the one asked about
  // here.
  const warnings = of(analyse(empty).findings, 'PT20').filter((f) => f.severity === 'warning');
  assert.ok(warnings.length > 0, 'an unmarked siphon was not reported as a warning');
  assert.match(warnings[0].message, /can never hold one/);
  assert.ok(warnings.some((f) => /^R1 and R2 hold no token/.test(f.message)),
    `expected the resources among ${JSON.stringify(warnings.map((f) => f.message))}`);
});

test('a loop reports its places as a trap that never runs empty', () => {
  const trap = of(analyse(readNet(loop)).findings, 'PT21');

  assert.equal(trap.length, 1);
  assert.match(trap[0].message, /Idle and Busy always hold at least one token/);
  assert.deepEqual(trap[0].cover, [['idle', 1], ['busy', 1]]);
});

test('a net with no siphon and no marked trap says nothing about either', () => {
  const findings = analyse(readNet(producer)).findings;

  assert.deepEqual(of(findings, 'PT20'), []);
  // Its one place is a trap, but an empty one, which is no statement at all.
  assert.deepEqual(of(findings, 'PT21'), []);
});
