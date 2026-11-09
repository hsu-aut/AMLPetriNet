import assert from 'node:assert/strict';
import { test } from 'node:test';

import { netFromElements } from './fromDiagram.js';
import { enabled, initialMarking, readNet } from './model.js';

/** A shape as diagram-js holds it, as far as this reader looks at it. */
const shape = (id, type, bo = {}) => ({ id, type, businessObject: { id, $type: type, ...bo } });

const place = (id, marking) => shape(id, 'ptn:Place', { name: id, initialMarking: marking });
const transition = (id) => shape(id, 'ptn:Transition', { name: id });
const arc = (id, source, target, inscription) => ({
  ...shape(id, 'ptn:Arc', { inscription }),
  source,
  target,
});

test('a drawing becomes the net it shows', () => {
  const idle = place('idle', 1);
  const start = transition('start');
  const busy = place('busy');

  const net = readNet(netFromElements([
    idle, start, busy,
    arc('a1', idle, start),
    arc('a2', start, busy),
  ]));

  assert.deepEqual(net.places.map((p) => [p.id, p.marking]), [['idle', 1], ['busy', 0]]);
  assert.deepEqual(net.arcs.map((a) => a.weight), [1, 1]);
  assert.deepEqual(enabled(net, initialMarking(net)), ['start']);
});

test('markings and weights are read from the text the user typed', () => {
  const p = place('p', '3');
  const t = transition('t');

  const net = readNet(netFromElements([p, t, arc('a', p, t, '2')]));

  assert.equal(net.places[0].marking, 3);
  assert.equal(net.arcs[0].weight, 2);
});

test('an empty place holds nothing and an arc without an inscription carries one token', () => {
  const p = place('p', '');
  const t = transition('t');

  const net = readNet(netFromElements([p, t, arc('a', p, t, '')]));

  assert.equal(net.places[0].marking, 0);
  assert.equal(net.arcs[0].weight, 1);
});

test('a label does not become a second element', () => {
  const t = transition('t');
  const label = { id: 't_label', type: 'ptn:Transition', businessObject: t.businessObject, labelTarget: t };

  const net = netFromElements([t, label]);

  assert.equal(net.transitions.length, 1);
});

test('a connection that has only one end yet is not an arc', () => {
  const p = place('p', 1);
  const t = transition('t');
  const dangling = { ...shape('a', 'ptn:Arc'), source: p, target: undefined };

  assert.deepEqual(netFromElements([p, t, dangling]).arcs, []);
});

test('text that is not a number reaches the reader, which names the element', () => {
  const p = place('p', 'two');
  const t = transition('t');

  assert.throws(() => readNet(netFromElements([p, t, arc('a', p, t)])), /marking of place p is NaN/);
});

test('a silent transition is a transition like any other', () => {
  const t = shape('t', 'ptn:Transition', { isSilent: true });

  assert.deepEqual(netFromElements([t]).transitions, [{ id: 't', name: '' }]);
});
