/**
 * Steps and moving tokens, in a real browser.
 *
 * The step itself is computed and tested without a browser. What needs one is
 * the button and the animation: whether one click fires everything that can
 * fire at once, whether a conflict still lets only one of two transitions
 * through, whether the dots appear and, which matters more, whether they are
 * gone again afterwards and leave nothing on the canvas.
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
  const commandStack = modeler.get('commandStack');
  const modeling = modeler.get('modeling');
  const simulation = modeler.get('simulation');
  const tokenFlow = modeler.get('tokenFlow');

  const root = () => modeler.get('canvas').getRootElement();
  const place = (x, y, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y }, root());
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, y, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y }, root());
    modeling.updateLabel(shape, name);
    return shape;
  };

  const wait = (ms) => new Promise((done) => setTimeout(done, ms));
  const marking = () => Object.fromEntries([...(simulation.marking ?? [])]
    .map(([id, tokens]) => [modeler.get('elementRegistry').get(id)?.businessObject?.name ?? id, tokens]));
  const dots = () => document.querySelectorAll('.layer-ptn-token-flow circle').length;
  const click = (what) => document.querySelector(`.ptn-simulation-${what}`).click();

  // Two independent halves: nothing they do can get in each other's way, so a
  // step fires both.
  const a = place(120, 120, 'A', 1);
  const first = transition(240, 120, 'First');
  const b = place(360, 120, 'B');
  const c = place(120, 260, 'C', 1);
  const second = transition(240, 260, 'Second');
  const d = place(360, 260, 'D');
  modeling.connect(a, first, { type: 'ptn:Arc' });
  modeling.connect(first, b, { type: 'ptn:Arc' });
  modeling.connect(c, second, { type: 'ptn:Arc' });
  modeling.connect(second, d, { type: 'ptn:Arc' });

  const pnmlBefore = await modeler.savePNML();

  click('toggle');
  click('together');
  const step = {
    steps: simulation.steps.length,
    marking: marking(),
    dots: dots(),
    flying: tokenFlow.flying,
  };

  // The dots are gone when the firing is drawn, and they leave nothing behind.
  await wait(700);
  const settled = { dots: dots(), flying: tokenFlow.flying };

  // Stepping back walks out of the step one firing at a time.
  click('back');
  const afterOne = { steps: simulation.steps.length, marking: marking() };
  click('back');
  const afterBoth = { steps: simulation.steps.length, marking: marking() };

  // An undo while the dots are in flight: the run ends, because the net it was
  // running on is gone, and nothing of the animation is left behind on a canvas
  // that now shows something else.
  click('reset');
  click('together');
  const flying = { dots: dots(), running: simulation.active };
  commandStack.undo();
  const afterUndo = { dots: dots(), flying: tokenFlow.flying, running: simulation.active };
  commandStack.redo();
  await wait(600);
  const settledAfterUndo = dots();

  // Turned off, nothing moves. The undo above ended the run, so it is started
  // again rather than toggled blindly.
  if (!simulation.active) click('toggle');
  document.querySelector('.ptn-simulation-animate input').click();
  click('together');
  const switchedOff = { wanted: tokenFlow.wanted, dots: dots(), steps: simulation.steps.length };
  await wait(500);
  document.querySelector('.ptn-simulation-animate input').click();

  const pnmlAfter = await modeler.savePNML();
  simulation.stop();

  // A conflict: one token, two transitions that want it. A step is not a way
  // around the firing rule.
  await modeler.createNew();
  const token = place(120, 160, 'Token', 1);
  const left = transition(260, 100, 'Left');
  const right = transition(260, 220, 'Right');
  modeling.connect(token, left, { type: 'ptn:Arc' });
  modeling.connect(token, right, { type: 'ptn:Arc' });

  click('toggle');
  click('together');
  const conflict = { steps: simulation.steps.length, fired: [...simulation.steps] };
  const names = conflict.fired.map((id) => modeler.get('elementRegistry').get(id)?.businessObject?.name);

  // Nothing is enabled any more, so the step button says so rather than lying.
  const afterConflict = { disabled: document.querySelector('.ptn-simulation-together').disabled };
  simulation.stop();

  return {
    step,
    settled,
    afterOne,
    afterBoth,
    flying,
    afterUndo,
    settledAfterUndo,
    switchedOff,
    conflict: { ...conflict, names },
    afterConflict,
    documentUnchanged: pnmlBefore === pnmlAfter,
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(result.step.steps === 2, `one step fired ${result.step.steps} transitions, not the two that can fire`);
check(result.step.marking?.B === 1 && result.step.marking?.D === 1,
  `after the step the net is in ${JSON.stringify(result.step.marking)}`);
check(result.step.dots > 0, 'no token moved along the arcs');
check(result.step.flying === result.step.dots,
  `the service counts ${result.step.flying} dots, the canvas shows ${result.step.dots}`);

check(result.settled.dots === 0, `${result.settled.dots} dots stayed on the canvas`);
check(result.settled.flying === 0, 'the service still counts dots in flight');

// Which of the two firings is taken back depends on the order they went in,
// and that order is decided by chance: a conflict inside a step has no answer
// in the net, so the simulation picks one. What is pinned is that exactly one
// firing was taken back, which means one half is back at its start.
const half = result.afterOne.marking ?? {};
check(result.afterOne.steps === 1
  && (half.A === 1) !== (half.B === 1)
  && (half.C === 1) !== (half.D === 1)
  && (half.A === 1) !== (half.C === 1),
  `one step back left ${result.afterOne.steps} firings and ${JSON.stringify(half)}`);
check(result.afterBoth.steps === 0 && result.afterBoth.marking?.A === 1 && result.afterBoth.marking?.C === 1,
  `two steps back left ${JSON.stringify(result.afterBoth.marking)}`);

check(result.flying.dots > 0 && result.flying.running === true,
  `the step before the undo drew ${result.flying.dots} dots with the run ${result.flying.running ? 'on' : 'off'}`);
check(result.afterUndo.running === false, 'an undo left the run going on a net that had changed');
check(result.afterUndo.dots === 0 && result.afterUndo.flying === 0,
  `an undo during the animation left ${result.afterUndo.dots} dots on the canvas`);
check(result.settledAfterUndo === 0, 'dots came back after the animation was cancelled');

check(result.switchedOff.wanted === false, 'turning the animation off did not reach the service');
check(result.switchedOff.dots === 0, `${result.switchedOff.dots} dots were drawn although the animation is off`);
check(result.switchedOff.steps === 2, 'the step did not fire with the animation off');

check(result.conflict.steps === 1,
  `a step fired ${result.conflict.steps} of two transitions that want the same token: ${result.conflict.names}`);
check(result.afterConflict.disabled === true, 'the step button is still offered in a marking where nothing is enabled');

check(result.documentUnchanged, 'a step changed the document');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: one click fires a whole step, the tokens move along the arcs and the canvas is clean after');
}
