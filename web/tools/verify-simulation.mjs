/**
 * Firing the net on the canvas, in a real browser.
 *
 * What a test without a browser cannot see: whether the enabled transitions
 * are marked, whether a place shows the marking it holds now, whether stepping
 * back puts the drawing back, and, the one that matters most, whether a run
 * leaves the document alone. The simulation keeps its marking beside the model
 * on purpose, so that a save during a run writes the net as the user drew it.
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

  // Idle -> Start -> Busy -> Stop -> Idle, one token in Idle.
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

  const drawn = (shape) => {
    const gfx = registry.getGraphics(shape);
    return {
      dots: gfx.querySelectorAll('.ptn-tokens circle').length,
      marked: gfx.classList.contains('ptn-enabled'),
    };
  };
  const bar = () => ({
    text: document.querySelector('.ptn-simulation-status')?.textContent ?? null,
    toggle: document.querySelector('.ptn-simulation-toggle')?.textContent ?? null,
    stepDisabled: document.querySelector('.ptn-simulation-step')?.disabled ?? null,
    backDisabled: document.querySelector('.ptn-simulation-back')?.disabled ?? null,
  });

  const pnmlBefore = await modeler.savePNML();
  const before = { idle: drawn(idle), busy: drawn(busy), start: drawn(start), bar: bar() };

  simulation.start();
  const started = { idle: drawn(idle), start: drawn(start), stop: drawn(stop), bar: bar() };

  // A click on the canvas fires, which is how a user runs a net.
  registry.getGraphics(start).dispatchEvent(new MouseEvent('click', { bubbles: true }));
  const afterClick = { idle: drawn(idle), busy: drawn(busy), start: drawn(start), stop: drawn(stop), bar: bar() };

  simulation.stepBack();
  const afterBack = { idle: drawn(idle), busy: drawn(busy), bar: bar() };

  simulation.fire(start.id);
  simulation.fire(stop.id);
  const afterRound = {
    idle: drawn(idle),
    busy: drawn(busy),
    steps: simulation.steps.map((id) => registry.get(id).businessObject.name),
    bar: bar(),
  };

  // The document while the net runs: the marking the user typed, not the one
  // on the canvas.
  const pnmlDuring = await modeler.savePNML();
  const markingDuring = idle.businessObject.initialMarking;

  // Editing is refused by the rules, which is what every interactive feature
  // asks before it starts: dragging, drawing, deleting, pasting.
  const rules = modeler.get('rules');
  const refusedWhileRunning = {
    move: rules.allowed('elements.move', { shapes: [idle] }),
    create: rules.allowed('shape.create', { shape: idle, target: root }),
    remove: rules.allowed('elements.delete', { elements: [idle] }),
    connect: rules.allowed('connection.create', { source: idle, target: start }),
  };

  simulation.reset();
  const afterReset = { idle: drawn(idle), busy: drawn(busy), steps: simulation.steps };

  simulation.stop();
  const stopped = { idle: drawn(idle), busy: drawn(busy), start: drawn(start), bar: bar() };
  const pnmlAfter = await modeler.savePNML();
  const allowedAgain = rules.allowed('elements.move', { shapes: [idle] });

  // The editor has to work after a run, and after an edit that was refused:
  // a refusal that leaves the command stack half way through stops the canvas
  // from ever redrawing again, which looks like the editor freezing.
  modeling.updateLabel(busy, 'Working');
  const afterRun = {
    model: busy.businessObject.name,
    drawn: registry.getGraphics(busy.label ?? busy).textContent.trim(),
  };

  // A change that does not go through the rules, a host writing into the model
  // or an undo, ends the run rather than leaving a marking on a net that is
  // gone.
  simulation.start();
  modeling.updateLabel(busy, 'Busy again');
  const afterForeignEdit = { running: simulation.active, bar: bar() };

  // A net the core refuses: the simulation says why and stays off.
  modeling.updateProperty(idle, 'initialMarking', 'two', { rerender: true });
  const refused = { started: simulation.start(), problem: simulation.problem, active: simulation.active };
  modeling.updateProperty(idle, 'initialMarking', 1, { rerender: true });

  // An import while the net runs ends the run: the marking belongs to the net
  // that was replaced.
  simulation.start();
  const pnml = await modeler.savePNML();
  await modeler.importPNML(pnml);
  const afterImport = { running: simulation.active, bar: bar() };

  // Stepping back and then forward walks the same run rather than choosing
  // again, which matters where a marking offers several firings.
  simulation.start();
  simulation.fire(start.id);
  simulation.stepBack();
  simulation.fireOne();
  const reproduced = simulation.steps.map((id) => registry.get(id).businessObject.name);
  simulation.stop();

  return {
    before, started, afterClick, afterBack, afterRound, afterReset, stopped, refused,
    refusedWhileRunning, allowedAgain, afterRun, afterForeignEdit, afterImport, reproduced,
    markingDuring,
    documentUnchanged: pnmlBefore === pnmlDuring && pnmlBefore === pnmlAfter,
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(result.before.idle.dots === 1, `before: Idle shows ${result.before.idle.dots} token(s)`);
check(!result.before.start.marked, 'before: a transition is marked although the net does not run');
check(result.before.bar.toggle === 'Simulate', `before: the button says ${result.before.bar.toggle}`);

check(result.started.start.marked, 'start: Start is enabled and should be marked');
check(!result.started.stop.marked, 'start: Stop is not enabled and should not be marked');
check(result.started.bar.toggle === 'Stop', `start: the button says ${result.started.bar.toggle}`);
check(/1 enabled/.test(result.started.bar.text), `start: the bar says '${result.started.bar.text}'`);

check(result.afterClick.idle.dots === 0 && result.afterClick.busy.dots === 1,
  `click: the token did not move (Idle ${result.afterClick.idle.dots}, Busy ${result.afterClick.busy.dots})`);
check(result.afterClick.stop.marked && !result.afterClick.start.marked,
  'click: the enabled transition did not change');
check(/1 firing/.test(result.afterClick.bar.text), `click: the bar says '${result.afterClick.bar.text}'`);

check(result.afterBack.idle.dots === 1 && result.afterBack.busy.dots === 0,
  'back: stepping back did not restore the marking');
check(result.afterBack.bar.backDisabled === true, 'back: the button is still offered with no steps left');

check(result.afterRound.idle.dots === 1 && result.afterRound.busy.dots === 0,
  'round: a full round should come back to Idle');
check(result.afterRound.steps.join() === 'Start,Stop', `round: the steps are ${result.afterRound.steps}`);

check(result.afterReset.idle.dots === 1 && result.afterReset.steps.length === 0, 'reset: the run was not reset');

check(result.stopped.idle.dots === 1 && !result.stopped.start.marked, 'stop: the drawing was not put back');
check(result.stopped.bar.text === '', `stop: the bar still says '${result.stopped.bar.text}'`);

for (const [what, answer] of Object.entries(result.refusedWhileRunning)) {
  check(answer === false, `${what} is still allowed while the net runs (answer: ${answer})`);
}
check(result.allowedAgain !== false, 'editing is still refused after the run ended');
check(result.afterRun.model === 'Working' && result.afterRun.drawn === 'Working',
  `after the run the model says '${result.afterRun.model}' and the canvas '${result.afterRun.drawn}'`);
check(result.afterForeignEdit.running === false, 'a change from outside the rules left the run going');
check(/net changed/.test(result.afterForeignEdit.bar.text ?? ''),
  `the bar says '${result.afterForeignEdit.bar.text}' after the net changed`);
check(result.reproduced.join() === 'Start', `stepping back and forward gave ${result.reproduced}`);
check(result.afterImport.running === false, 'an import while the net ran left the run going');
check(result.markingDuring === 1, `the document held ${result.markingDuring} while the net ran`);
check(result.documentUnchanged, 'the PNML export changed although only the simulation ran');

check(result.refused.started === false && result.refused.active === false,
  'a net that cannot be read should not start');
check(/marking of place/.test(result.refused.problem ?? ''),
  `the refusal says '${result.refused.problem}'`);

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: the net fires on the canvas, steps back and resets, and the document is untouched');
}
