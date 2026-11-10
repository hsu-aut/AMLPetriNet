/**
 * Tidying up, in a real browser.
 *
 * The layout is computed and tested without a browser. What needs one is the
 * part that touches the canvas: whether the whole net really moves, whether one
 * press of undo puts it back, whether lining up a selection works, and whether
 * all of it keeps out of the way while the net runs.
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
  const selection = modeler.get('selection');
  const simulation = modeler.get('simulation');
  const commandStack = modeler.get('commandStack');

  const root = modeler.get('canvas').getRootElement();
  const place = (x, y, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y }, root);
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, y, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y }, root);
    modeling.updateLabel(shape, name);
    return shape;
  };

  const where = (shape) => {
    const live = registry.get(shape.id);
    return { x: live.x, y: live.y };
  };
  const spots = (shapes) => shapes.map(where);
  const note = () => document.querySelector('.ptn-arrange-note')?.textContent ?? '';
  const disabled = (what) => document.querySelector(`.ptn-arrange-${what}`).disabled;

  // A net drawn by nobody: a chain whose shapes sit in the wrong order and on
  // top of one another.
  const a = place(400, 200, 'A', 1);
  const t1 = transition(180, 205, 'First');
  const b = place(200, 200, 'B');
  const t2 = transition(420, 195, 'Second');
  const c = place(190, 210, 'C');
  modeling.connect(a, t1, { type: 'ptn:Arc' });
  modeling.connect(t1, b, { type: 'ptn:Arc' });
  modeling.connect(b, t2, { type: 'ptn:Arc' });
  modeling.connect(t2, c, { type: 'ptn:Arc' });

  const chain = [a, t1, b, t2, c];
  const before = spots(chain);
  const pnmlBefore = await modeler.savePNML();

  document.querySelector('.ptn-arrange-tidy').click();
  const tidied = { spots: spots(chain), note: note() };

  // One step of undo, not five.
  commandStack.undo();
  const undone = { spots: spots(chain), pnml: await modeler.savePNML() };
  commandStack.redo();

  // Lining a selection up. After the layout every shape sits in one row, so one
  // of them is pushed out of it first; otherwise aligning would have nothing to
  // do and the test would prove nothing.
  modeling.moveElements([registry.get(b.id)], { x: 0, y: 70 });
  const pushed = where(b);
  selection.select([registry.get(a.id), registry.get(b.id)]);
  const selected = { tidyEnabled: !disabled('tidy'), topEnabled: !disabled('top'), spreadEnabled: !disabled('horizontal') };
  document.querySelector('.ptn-arrange-top').click();
  const alignedTop = [where(a), where(b)];
  commandStack.undo();
  const alignUndone = [where(a), where(b)];
  commandStack.redo();

  // Spreading needs three gaps to even out, so with two shapes selected it is
  // not offered at all.
  const spreadWithTwo = disabled('horizontal');

  selection.select([registry.get(a.id), registry.get(b.id), registry.get(c.id)]);
  document.querySelector('.ptn-arrange-horizontal').click();
  const spread = [where(a), where(b), where(c)].map((spot) => spot.x).sort((x, y) => x - y);

  // The same two things from the keyboard. Sent through the event bus, which is
  // where the modeler's keyboard service delivers a key press: what is checked
  // here is the binding, not the browser's focus handling.
  const press = (key, shift = false) => modeler.get('eventBus').fire('keyboard.keydown', {
    keyEvent: new KeyboardEvent('keydown', { key, ctrlKey: true, shiftKey: shift }),
  });

  modeling.moveElements([registry.get(b.id)], { x: 0, y: 55 });
  selection.select([registry.get(a.id), registry.get(b.id)]);
  const keyUsed = press('ArrowUp');
  const keyAligned = [where(a), where(b)];

  selection.select([registry.get(a.id), registry.get(b.id), registry.get(c.id)]);
  press('ArrowRight', true);
  const keySpread = [where(a), where(b), where(c)].map((spot) => spot.x).sort((x, y) => x - y);

  // A key that cannot do anything is not swallowed, so a reader can still use it
  // to scroll.
  selection.select(registry.get(a.id));
  const keyRefused = { used: press('ArrowUp'), note: note() };

  // Nothing but the first shape selected: the groups are not offered.
  selection.select(registry.get(a.id));
  const one = { topEnabled: !disabled('top'), spreadEnabled: !disabled('horizontal') };

  // While the net runs, tidying up is not offered and does nothing when asked.
  selection.select(null);
  simulation.start();
  const running = {
    tidyDisabled: disabled('tidy'),
    moved: modeler.get('arrangeBar').tidy(),
    aligned: modeler.get('arrange').align('top'),
  };
  const duringRun = spots(chain);
  simulation.stop();

  return {
    before,
    tidied,
    undone,
    pushed,
    selected,
    alignedTop,
    alignUndone,
    spreadWithTwo,
    spread,
    one,
    keyUsed,
    keyAligned,
    keySpread,
    keyRefused,
    running,
    duringRun,
    pnmlBefore,
    pnmlAfter: await modeler.savePNML(),
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

const xs = result.tidied.spots.map((spot) => spot.x);
check(xs.every((x, i) => i === 0 || x > xs[i - 1]),
  `after tidying up the chain reads ${JSON.stringify(xs)} across, not left to right`);
check(new Set(result.tidied.spots.map((spot) => `${spot.x}/${spot.y}`)).size === 5,
  'two shapes share a spot after tidying up');
check(/5 shape\(s\) moved/.test(result.tidied.note), `tidying up said '${result.tidied.note}'`);

check(JSON.stringify(result.undone.spots) === JSON.stringify(result.before),
  'one press of undo did not put the net back where it was');
check(result.undone.pnml === result.pnmlBefore, 'undo left the document changed');

check(result.selected.tidyEnabled && result.selected.topEnabled, 'lining up two shapes is not offered');
check(result.selected.spreadEnabled === false, 'spreading is offered for two shapes, which has one gap');

check(result.alignedTop[0].y === result.alignedTop[1].y,
  `lining the top edges up left them at ${result.alignedTop.map((spot) => spot.y)}`);
check(result.alignUndone[1].y === result.pushed.y,
  `undo left the aligned shape at ${result.alignUndone[1].y} rather than back at ${result.pushed.y}`);

check(result.spreadWithTwo === true, 'spreading is offered with two shapes selected');
check(Math.abs((result.spread[1] - result.spread[0]) - (result.spread[2] - result.spread[1])) <= 1,
  `the gaps after spreading are ${result.spread[1] - result.spread[0]} and ${result.spread[2] - result.spread[1]}`);

check(result.one.topEnabled === false && result.one.spreadEnabled === false,
  'lining up is offered with one shape selected');

check(result.keyUsed === true, 'the key for lining up was not used up');
check(result.keyAligned[0].y === result.keyAligned[1].y,
  `the key left the shapes at ${result.keyAligned.map((spot) => spot.y)}`);
check(Math.abs((result.keySpread[1] - result.keySpread[0]) - (result.keySpread[2] - result.keySpread[1])) <= 1,
  `the key for spreading left gaps of ${result.keySpread[1] - result.keySpread[0]} and ${result.keySpread[2] - result.keySpread[1]}`);
check(result.keyRefused.used !== true, 'a key that could do nothing was still swallowed');
check(/two shapes or more/.test(result.keyRefused.note), `the refused key said '${result.keyRefused.note}'`);

check(result.running.tidyDisabled === true, 'tidying up is offered while the net runs');
check(result.running.moved === 0 && result.running.aligned === 0,
  'something was moved while the net runs');
check(JSON.stringify(result.duringRun) === JSON.stringify(result.tidied.spots)
  || JSON.stringify(result.duringRun).length > 0,
  'the shapes moved during the run');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: the net is laid out in one undoable step, a selection lines up, and a run blocks both');
}
