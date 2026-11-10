/**
 * Invariants in the picture, in a real browser.
 *
 * The invariants themselves are computed and tested without a browser. What
 * needs one is what a reader does with them: ringing the places a conservation
 * law covers, with the weight each of them counts for, and firing the sequence
 * a transition invariant stands for to see the marking come back.
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
  const highlight = modeler.get('highlight');

  const root = () => modeler.get('canvas').getRootElement();
  const place = (x, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y: 160 }, root());
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y: 160 }, root());
    modeling.updateLabel(shape, name);
    return shape;
  };

  const rings = () => document.querySelectorAll('.ptn-marked-ring').length;
  const marked = () => document.querySelectorAll('.ptn-marked').length;
  const note = () => document.querySelector('.ptn-findings-note')?.textContent ?? null;
  const rowFor = (rule) => [...document.querySelectorAll('.ptn-finding')]
    .find((item) => item.querySelector('.ptn-finding-rule')?.textContent === rule);
  const actionOn = (rule, text) => [...rowFor(rule)?.querySelectorAll('.ptn-finding-action') ?? []]
    .find((button) => button.textContent.includes(text));
  const marking = () => Object.fromEntries([...(simulation.marking ?? [])]);

  // Idle -> Start -> Busy -> Stop -> Idle: one token going round, one place
  // invariant over both places and one firing sequence that returns it.
  const idle = place(120, 'Idle', 1);
  const start = transition(240, 'Start');
  const busy = place(360, 'Busy');
  const stop = transition(480, 'Stop');
  modeling.connect(idle, start, { type: 'ptn:Arc' });
  modeling.connect(start, busy, { type: 'ptn:Arc' });
  modeling.connect(busy, stop, { type: 'ptn:Arc' });
  modeling.connect(stop, idle, { type: 'ptn:Arc' });

  const pnmlBefore = await modeler.savePNML();
  document.querySelector('.ptn-findings-button').click();

  const before = { rings: rings(), marked: marked() };

  actionOn('PT18', 'Mark')?.click();
  const after = { rings: rings(), marked: marked(), note: note(), size: highlight.size };

  // The same button clears it again, so a reader is not stuck with the rings.
  actionOn('PT18', 'Mark')?.click();
  const cleared = { rings: rings(), marked: marked(), note: note() };

  // The transition invariant is loaded as firings and really returns the marking.
  // The marking to compare against is the one the loaded run starts in, which
  // is the initial marking: before the click there is no run and no marking.
  actionOn('PT19', 'Play')?.click();
  const loaded = { note: note(), steps: simulation.steps.length, running: simulation.active };
  const startMarking = JSON.stringify(marking());
  document.querySelector('.ptn-simulation-step').click();
  const halfway = JSON.stringify(marking());
  document.querySelector('.ptn-simulation-step').click();
  const returned = { marking: JSON.stringify(marking()), steps: simulation.steps.length };
  const pnmlAfter = await modeler.savePNML();
  simulation.stop();

  // A weighted invariant: the transition takes one token from In and puts two
  // into Out, so In counts twice as much as Out.
  await modeler.createNew();
  const inPlace = place(120, 'In', 1);
  const double = transition(240, 'Double');
  const outPlace = place(360, 'Out');
  modeling.connect(inPlace, double, { type: 'ptn:Arc' });
  const twice = modeling.connect(double, outPlace, { type: 'ptn:Arc' });
  modeling.updateLabel(twice, '2');

  document.querySelector('.ptn-findings-button').click();
  actionOn('PT18', 'Mark')?.click();
  const weighted = {
    rings: rings(),
    note: note(),
    // The weight is drawn beside the place it counts for; In counts twice.
    weights: [...document.querySelectorAll('.djs-element')]
      .filter((element) => element.querySelector('.ptn-marked-ring'))
      .map((element) => ({
        id: element.getAttribute('data-element-id'),
        texts: [...element.querySelectorAll('text')].map((text) => text.textContent),
      })),
  };

  // An invariant whose firings cannot be fired from the marking the net is in:
  // the same cycle with no token in it. The set is still worth marking, so Mark
  // stays; Play does not, because there is nothing to play.
  await modeler.createNew();
  const empty = place(120, 'Empty');
  const go = transition(240, 'Go');
  const back = place(360, 'Back');
  const ret = transition(480, 'Return');
  modeling.connect(empty, go, { type: 'ptn:Arc' });
  modeling.connect(go, back, { type: 'ptn:Arc' });
  modeling.connect(back, ret, { type: 'ptn:Arc' });
  modeling.connect(ret, empty, { type: 'ptn:Arc' });

  document.querySelector('.ptn-findings-button').click();
  const unfireable = {
    hasRow: Boolean(rowFor('PT19')),
    buttons: [...rowFor('PT19')?.querySelectorAll('.ptn-finding-action') ?? []].map((b) => b.textContent),
  };

  // Editing the net drops the rings: they belong to the net they were computed on.
  modeling.updateLabel(outPlace, 'Finished');
  const afterEdit = { rings: rings(), marked: marked(), size: highlight.size };

  return {
    before,
    after,
    cleared,
    loaded,
    startMarking,
    halfway,
    returned,
    weighted,
    unfireable,
    afterEdit,
    inId: inPlace.id,
    outId: outPlace.id,
    documentUnchanged: pnmlBefore === pnmlAfter,
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(result.before.rings === 0, `${result.before.rings} rings were drawn before anything was marked`);

check(result.after.rings === 2, `the place invariant ringed ${result.after.rings} elements, not the two places`);
check(result.after.marked === 2, `${result.after.marked} elements carry the class for a marked element`);
check(result.after.size === 2, `the service holds ${result.after.size} marked elements`);
check(/2 element\(s\) ringed/.test(result.after.note ?? ''), `marking said '${result.after.note}'`);

check(result.cleared.rings === 0, `clicking Mark again left ${result.cleared.rings} rings`);
check(result.cleared.marked === 0, 'clicking Mark again left the class on the elements');

check(result.loaded.running === true, 'playing an invariant did not start the run');
check(result.loaded.steps === 0, 'playing an invariant fired it instead of loading it');
check(/2 firings return the marking/.test(result.loaded.note ?? ''), `playing said '${result.loaded.note}'`);
check(result.halfway !== result.startMarking, 'the first firing of the invariant changed nothing');
check(result.returned.marking === result.startMarking,
  `after the invariant the net is in ${result.returned.marking}, not back in ${result.startMarking}`);
check(result.returned.steps === 2, `the invariant took ${result.returned.steps} firings, not two`);
check(result.documentUnchanged, 'playing an invariant changed the document');

check(result.weighted.rings === 2, `the weighted invariant ringed ${result.weighted.rings} elements`);
const inRing = result.weighted.weights.find((element) => element.id === result.inId);
const outRing = result.weighted.weights.find((element) => element.id === result.outId);
check(inRing?.texts.includes('2'), `the place that counts twice shows ${JSON.stringify(inRing?.texts)}`);
check(outRing !== undefined && !outRing.texts.includes('2'),
  `the place that counts once shows ${JSON.stringify(outRing?.texts)}`);
check(/each with the weight it counts for/.test(result.weighted.note ?? ''),
  `the weighted invariant said '${result.weighted.note}'`);

check(result.unfireable.hasRow === true, 'the empty cycle reported no transition invariant at all');
check(result.unfireable.buttons.includes('◎ Mark'),
  `an invariant that cannot be fired offers ${JSON.stringify(result.unfireable.buttons)}`);
check(!result.unfireable.buttons.includes('▶ Play'),
  'an invariant that cannot be fired from this marking still offers to play it');

check(result.afterEdit.rings === 0, `editing the net left ${result.afterEdit.rings} rings on screen`);
check(result.afterEdit.size === 0, 'editing the net left the marked set in the service');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: an invariant is ringed in the drawing and its firings return the marking');
}
