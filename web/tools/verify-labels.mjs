/**
 * Clearing a label, in a real browser.
 *
 * The modeler set the text "1" whenever a label was emptied, which is the
 * default weight of an arc and nonsense as the name of a place. The fork
 * scopes that default to arcs (UpdateLabelHandler.postExecute). This checks all three element types, and that
 * undo brings a cleared name back.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('[pageerror]', e.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async () => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const commandStack = m.get('commandStack');
  const root = m.get('canvas').getRootElement();

  const place = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x: 150, y: 200 }, root);
  const transition = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x: 320, y: 200 }, root);
  const arc = modeling.connect(place, transition, { type: 'ptn:Arc' });

  modeling.updateLabel(place, 'Waiting');
  modeling.updateLabel(transition, 'Start');
  modeling.updateLabel(arc, '3');

  modeling.updateLabel(place, '');
  modeling.updateLabel(transition, '');
  modeling.updateLabel(arc, '');

  const cleared = {
    place: place.businessObject.name,
    transition: transition.businessObject.name,
    arc: arc.businessObject.inscription,
    pnml: await m.savePNML(),
  };

  commandStack.undo();
  commandStack.undo();
  commandStack.undo();

  return {
    ...cleared,
    undone: {
      place: place.businessObject.name,
      transition: transition.businessObject.name,
      arc: arc.businessObject.inscription,
    },
  };
});

await browser.close();
server.close();

const problems = [];
if (result.place) problems.push(`a cleared place is called '${result.place}'`);
if (result.transition) problems.push(`a cleared transition is called '${result.transition}'`);
if (String(result.arc) !== '1') problems.push(`a cleared arc has the inscription '${result.arc}', not 1`);
if (/<text>1<\/text>\s*<\/name>/.test(result.pnml)) problems.push('the export names a node "1"');
if (result.undone.place !== 'Waiting') problems.push(`undo gave the place '${result.undone.place}'`);
if (result.undone.transition !== 'Start') problems.push(`undo gave the transition '${result.undone.transition}'`);
if (String(result.undone.arc) !== '3') problems.push(`undo gave the arc '${result.undone.arc}'`);

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: a cleared node name stays empty, a cleared arc falls back to weight 1, undo restores both');
}
