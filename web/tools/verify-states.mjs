/**
 * The state space view, in a real browser.
 *
 * The layout is tested without a browser; what needs one is the picture and
 * what a click on it does: a node has to put the net into that marking, and
 * the view has to follow a running simulation rather than drift away from it.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
const pageErrors = [];
page.on('pageerror', (error) => pageErrors.push(error.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async () => {
  const modeler = window.ptn;
  const factory = modeler.get('elementFactory');
  const modeling = modeler.get('modeling');
  const registry = modeler.get('elementRegistry');
  const simulation = modeler.get('simulation');
  const root = modeler.get('canvas').getRootElement();

  const place = (x, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y: 420 }, root);
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y: 500 }, root);
    modeling.updateLabel(shape, name);
    return shape;
  };

  // Idle -> Start -> Busy -> Stop -> Idle: two markings, two firings, a cycle.
  const idle = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x: 120, y: 160 }, root);
  const start = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x: 260, y: 160 }, root);
  const busy = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x: 400, y: 160 }, root);
  const stop = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x: 400, y: 300 }, root);
  modeling.updateLabel(idle, 'Idle');
  modeling.updateLabel(start, 'Start');
  modeling.updateLabel(busy, 'Busy');
  modeling.updateLabel(stop, 'Stop');
  modeling.updateProperty(idle, 'initialMarking', 1, { rerender: true });
  modeling.connect(idle, start, { type: 'ptn:Arc' });
  modeling.connect(start, busy, { type: 'ptn:Arc' });
  modeling.connect(busy, stop, { type: 'ptn:Arc' });
  modeling.connect(stop, idle, { type: 'ptn:Arc' });

  const view = modeler.get('reachabilityView');
  const panel = () => document.querySelector('.ptn-states');
  const nodes = () => [...document.querySelectorAll('.ptn-states-node')];
  const dotsOf = (shape) => registry.getGraphics(shape).querySelectorAll('.ptn-tokens circle').length;

  const before = { hidden: panel().hidden, nodes: nodes().length };

  document.querySelector('.ptn-states-button').click();
  const shown = {
    hidden: panel().hidden,
    title: document.querySelector('.ptn-states-title')?.textContent ?? '',
    nodes: nodes().length,
    edges: document.querySelectorAll('.ptn-states-edge').length,
    labels: [...document.querySelectorAll('.ptn-states-edge-label')].map((t) => t.textContent).sort(),
    initial: nodes().filter((node) => node.classList.contains('ptn-states-initial')).length,
    tooltips: nodes().map((node) => node.querySelector('title')?.textContent),
    state: view.state,
  };

  // Clicking the second marking puts the net into it, starting the simulation.
  const other = nodes().find((node) => !node.classList.contains('ptn-states-initial'));
  // An SVG group has no click(), so the event is dispatched as a click from a
  // mouse would arrive.
  other.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  const afterClick = {
    running: simulation.active,
    idle: dotsOf(idle),
    busy: dotsOf(busy),
    current: nodes().filter((node) => node.classList.contains('ptn-states-current')).length,
    currentIsTheOneClicked: other.classList.contains('ptn-states-current'),
  };

  // Firing on the canvas moves the mark in the graph with it.
  simulation.fire(stop.id);
  const afterFire = {
    idle: dotsOf(idle),
    currentIsInitial: nodes()
      .find((node) => node.classList.contains('ptn-states-current'))
      ?.classList.contains('ptn-states-initial'),
  };

  simulation.stop();

  // A changed net closes the view rather than leaving a graph of a net that
  // no longer exists.
  modeling.updateLabel(busy, 'Working');
  const afterEdit = panel().hidden;

  // A net the core cannot read says so instead of drawing nothing.
  modeling.updateProperty(idle, 'initialMarking', 'two', { rerender: true });
  document.querySelector('.ptn-states-button').click();
  const refused = {
    hidden: panel().hidden,
    title: document.querySelector('.ptn-states-title')?.textContent ?? '',
    message: document.querySelector('.ptn-states-message')?.textContent ?? '',
    state: view.state,
  };

  // A node that stands for a family of markings, which is what omega means in
  // a coverability graph, cannot be a marking the net is put into.
  modeling.updateProperty(idle, 'initialMarking', 1, { rerender: true });
  const free = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x: 260, y: 320 }, root);
  modeling.updateLabel(free, 'Produce');
  modeling.connect(free, busy, { type: 'ptn:Arc' });
  document.querySelector('.ptn-states-button').click();
  const omegaNode = nodes().find((node) => /any number/.test(node.querySelector('title')?.textContent ?? ''));
  omegaNode?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  const omega = {
    found: Boolean(omegaNode),
    note: document.querySelector('.ptn-states-note')?.textContent ?? '',
    marking: simulation.active ? [...simulation.marking.values()] : null,
  };
  simulation.stop();

  // A state space with more markings than the view draws says how big it is
  // rather than drawing something unreadable, and it says it quickly.
  modeling.removeElements([free]);
  const many = [];
  for (let i = 0; i < 9; i += 1) {
    const from = place(120 + i * 60, `P${i}`, 1);
    const via = transition(150 + i * 60, `T${i}`);
    const to = place(180 + i * 60, `Q${i}`);
    modeling.connect(from, via, { type: 'ptn:Arc' });
    modeling.connect(via, to, { type: 'ptn:Arc' });
    many.push(from, via, to);
  }
  const drawingStarted = Date.now();
  document.querySelector('.ptn-states-button').click();
  const big = {
    milliseconds: Date.now() - drawingStarted,
    title: document.querySelector('.ptn-states-title')?.textContent ?? '',
    note: document.querySelector('.ptn-states-note')?.textContent ?? '',
    nodes: nodes().length,
  };

  // The same net with the search cut off: what is on screen is then some of the
  // markings, not all of them, and the view says so instead of presenting a
  // part as the whole.
  modeler.get('reachabilityView').show({ markings: 4 });
  const cut = {
    title: document.querySelector('.ptn-states-title')?.textContent ?? '',
    nodes: nodes().length,
    complete: modeler.get('reachabilityView').state?.complete,
    answer: modeler.get('reachabilityView').ask('P0 = 0'),
  };

  return { before, shown, afterClick, afterFire, afterEdit, refused, omega, big, cut };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(result.before.hidden === true, 'the state space is open before it was asked for');
check(result.before.nodes === 0, 'the state space holds nodes before it was asked for');

check(result.shown.hidden === false, 'the view stayed closed');
check(result.shown.nodes === 2, `the cycle has two markings, the view drew ${result.shown.nodes}`);
check(result.shown.edges === 2, `the cycle has two firings, the view drew ${result.shown.edges}`);
check(result.shown.labels.join() === 'Start,Stop', `the firings are labelled ${result.shown.labels}`);
check(result.shown.initial === 1, 'exactly one node is the initial marking');
check(/2 markings, 2 firings/.test(result.shown.title), `the title says '${result.shown.title}'`);
check(result.shown.tooltips.some((text) => /Idle: 1/.test(text ?? '')), `the tooltips read ${result.shown.tooltips}`);
check(result.shown.state?.complete === true, 'the state space says it is incomplete');

check(result.afterClick.running === true, 'clicking a marking did not start the simulation');
check(result.afterClick.idle === 0 && result.afterClick.busy === 1,
  `the net is in ${result.afterClick.idle}/${result.afterClick.busy} rather than in the marking clicked`);
check(result.afterClick.current === 1 && result.afterClick.currentIsTheOneClicked,
  'the marking the net is in is not the one marked in the graph');

check(result.afterFire.idle === 1, 'firing on the canvas did not move the net');
check(result.afterFire.currentIsInitial === true, 'the graph did not follow the net back to the initial marking');

check(result.afterEdit === true, 'editing the net left the old state space on screen');

check(result.refused.hidden === false, 'a net that cannot be read closed the view silently');
check(/could not be read/.test(result.refused.title), `the refusal says '${result.refused.title}'`);
check(/marking of place/.test(result.refused.message), `the reason reads '${result.refused.message}'`);
check(result.refused.state === null, 'the view kept the old state space after a refusal');

check(result.omega.found === true, 'the unbounded net produced no node standing for a family of markings');
check(/family of markings/.test(result.omega.note), `clicking it said '${result.omega.note}'`);
check(result.omega.marking === null || result.omega.marking.every((n) => Number.isFinite(n)),
  `the net was put into the marking ${result.omega.marking}`);

check(result.big.nodes === 0, `a state space too large to draw still drew ${result.big.nodes} node(s)`);
check(/too many to draw/.test(result.big.title), `the title says '${result.big.title}'`);
check(/markings with/.test(result.big.note), `the note says '${result.big.note}'`);
check(result.big.milliseconds < 1500, `saying so took ${result.big.milliseconds} ms`);

check(result.cut.complete === false, 'a search that was cut off reported itself as complete');
check(/cut off at the limit/.test(result.cut.title), `the cut off search says '${result.cut.title}'`);
check(result.cut.nodes === 4, `the cut off search drew ${result.cut.nodes} markings, not the four it kept`);
check(/not an answer about every marking|Reachable in/.test(result.cut.answer ?? ''),
  `the query on a cut off search answered '${result.cut.answer}'`);

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: the state space is drawn, and a marking in it puts the net into that marking');
}
