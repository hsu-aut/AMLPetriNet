/**
 * The way to a marking, in a real browser.
 *
 * The paths themselves are tested without a browser. What needs one is the
 * offer: whether a finding about a deadlock hands the user the firings that
 * lead into it, whether playing them ends there, and whether the query line
 * answers a marking a reader types with the way to it.
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
  const simulation = modeler.get('simulation');
  const root = modeler.get('canvas').getRootElement();

  // In -> Step -> Mid -> Pair -> Out, where Pair needs two tokens in Mid and
  // only one ever arrives: one firing of Step and the net is stuck.
  const place = (x, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y: 160 }, root);
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y: 160 }, root);
    modeling.updateLabel(shape, name);
    return shape;
  };

  const inPlace = place(120, 'In', 1);
  const step = transition(240, 'Step');
  const mid = place(360, 'Mid');
  const pair = transition(480, 'Pair');
  const out = place(600, 'Out');
  modeling.connect(inPlace, step, { type: 'ptn:Arc' });
  modeling.connect(step, mid, { type: 'ptn:Arc' });
  const needsTwo = modeling.connect(mid, pair, { type: 'ptn:Arc' });
  modeling.updateLabel(needsTwo, '2');
  modeling.connect(pair, out, { type: 'ptn:Arc' });

  const marking = () => {
    const held = simulation.marking;
    return held ? Object.fromEntries([...held].map(([id, tokens]) => [name(id), tokens])) : null;
  };
  const name = (id) => modeler.get('elementRegistry').get(id)?.businessObject?.name ?? id;
  const note = () => document.querySelector('.ptn-findings-note')?.textContent ?? null;
  const stateNote = () => document.querySelector('.ptn-states-note')?.textContent ?? null;
  const status = () => document.querySelector('.ptn-simulation-status')?.textContent ?? '';

  const pnmlBefore = await modeler.savePNML();

  // The deadlock finding offers the way into the deadlock.
  document.querySelector('.ptn-findings-button').click();
  const deadlockRow = [...document.querySelectorAll('.ptn-finding')]
    .find((item) => item.querySelector('.ptn-finding-rule')?.textContent === 'PT15');
  const offered = deadlockRow?.querySelector('.ptn-finding-action')?.textContent ?? null;

  // Other findings offer other actions; what belongs to a marking is the way
  // into it, and only the deadlock is about a marking here.
  const waysOffered = [...document.querySelectorAll('.ptn-finding-action')]
    .filter((button) => button.textContent.includes('Way there')).length;

  [...deadlockRow?.querySelectorAll('.ptn-finding-action') ?? []]
    .find((button) => button.textContent.includes('Way there'))?.click();
  const loaded = { note: note(), marking: marking(), running: simulation.active, steps: simulation.steps.length };

  // Playing the loaded way ends in the deadlock, and in the deadlock the bar
  // says so.
  document.querySelector('.ptn-simulation-step').click();
  const walked = { marking: marking(), steps: simulation.steps, status: status() };

  // Stepping back walks the loaded way out again rather than inventing one.
  document.querySelector('.ptn-simulation-back').click();
  const back = { marking: marking(), steps: simulation.steps.length };

  simulation.stop();

  // The query line answers a marking a reader types.
  document.querySelector('.ptn-states-button').click();
  const ask = (text) => {
    const line = document.querySelector('.ptn-states-query');
    line.querySelector('.ptn-states-query-input').value = text;
    line.requestSubmit();
    return stateNote();
  };

  const reachable = ask('Mid = 1');
  const afterAsk = { marking: marking(), running: simulation.active };
  const unreachable = ask('Out = 1');
  const unknownPlace = ask('Nowhere = 1');
  const nonsense = ask('Mid = two');
  const initial = ask('In = 1');

  simulation.stop();
  const pnmlAfter = await modeler.savePNML();

  // A finding whose element is gone must not keep an action that leads nowhere.
  modeling.updateLabel(out, 'Finished');
  const afterEdit = [...document.querySelectorAll('.ptn-finding-action')].length;

  return {
    offered,
    waysOffered,
    loaded,
    walked,
    back,
    reachable,
    afterAsk,
    unreachable,
    unknownPlace,
    nonsense,
    initial,
    afterEdit,
    documentUnchanged: pnmlBefore === pnmlAfter,
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(result.offered === '▶ Way there', `the deadlock finding offers '${result.offered}'`);
check(result.waysOffered === 1, `${result.waysOffered} findings offer a way there; only the deadlock should`);

check(/One firing leads there/.test(result.loaded.note ?? ''), `loading the way said '${result.loaded.note}'`);
check(result.loaded.running === true, 'loading the way did not start the run');
check(result.loaded.steps === 0, 'loading the way fired it instead of loading it');
check(result.loaded.marking?.In === 1 && result.loaded.marking?.Mid === 0,
  `after loading, the net is in ${JSON.stringify(result.loaded.marking)} rather than in the initial marking`);

check(result.walked.marking?.Mid === 1 && result.walked.marking?.In === 0,
  `walking the way ended in ${JSON.stringify(result.walked.marking)}`);
check(result.walked.steps.length === 1, `walking the way took ${result.walked.steps.length} firings`);
check(/nothing is enabled/.test(result.walked.status), `the bar says '${result.walked.status}'`);

check(result.back.steps === 0 && result.back.marking?.In === 1,
  `stepping back left the net in ${JSON.stringify(result.back.marking)} after ${result.back.steps} firings`);

check(/Reachable in 1 firing: Step/.test(result.reachable ?? ''), `the query was answered with '${result.reachable}'`);
check(result.afterAsk.running === true && result.afterAsk.marking?.In === 1,
  'the answered query did not load the way from the initial marking');
check(/No reachable marking holds that/.test(result.unreachable ?? ''),
  `a marking the net cannot reach was answered with '${result.unreachable}'`);
check(/no place called "Nowhere"/.test(result.unknownPlace ?? ''),
  `an unknown place was answered with '${result.unknownPlace}'`);
check(/whole number/.test(result.nonsense ?? ''), `a count that is not a number was answered with '${result.nonsense}'`);
check(/marking the net starts in/.test(result.initial ?? ''),
  `the initial marking was answered with '${result.initial}'`);

check(result.afterEdit === 0, `editing the net left ${result.afterEdit} action(s) from the old analysis on screen`);
check(result.documentUnchanged, 'playing a way changed the document');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: a finding hands over the way into the marking it is about, and the query line answers one');
}
