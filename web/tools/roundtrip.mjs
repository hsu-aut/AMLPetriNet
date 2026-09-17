/**
 * Round-trip check for the PNML converter, run in a real browser.
 *
 * Builds a net, exports PNML, imports it back, exports again and compares. The
 * second export must be byte identical to the first: anything the converter
 * drops shows up here as a diff.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('[pageerror]', e.message));
page.on('console', (m) => {
  if (m.type() === 'error') console.error('[page]', m.text());
});

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async () => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const root = m.get('canvas').getRootElement();

  const p1 = factory.createShape({ type: 'ptn:Place' });
  const t1 = factory.createShape({ type: 'ptn:Transition' });
  const p2 = factory.createShape({ type: 'ptn:Place' });

  modeling.createShape(p1, { x: 150, y: 200 }, root);
  modeling.createShape(t1, { x: 320, y: 200 }, root);
  modeling.createShape(p2, { x: 500, y: 320 }, root);

  modeling.updateLabel(p1, 'Waiting');
  modeling.updateProperty(p1, 'initialMarking', 3);
  modeling.updateLabel(t1, 'Approve');
  modeling.updateLabel(p2, 'Done');

  const a1 = modeling.connect(p1, t1, { type: 'ptn:Arc' });
  const a2 = modeling.connect(t1, p2, { type: 'ptn:Arc' });
  modeling.updateProperty(a1, 'inscription', '2');
  modeling.updateWaypoints(a2, [
    { x: 345, y: 225 },
    { x: 420, y: 225 },
    { x: 420, y: 345 },
    { x: 500, y: 345 },
  ]);

  const nativeBefore = (await m.saveXML({ format: true })).xml;
  const first = await m.savePNML();

  await m.importPNML(first);
  const second = await m.savePNML();
  const nativeAfter = (await m.saveXML({ format: true })).xml;

  return { first, second, nativeBefore, nativeAfter };
});

await browser.close();
server.close();

console.log('=== PNML (first export) ===');
console.log(result.first);

if (result.first === result.second) {
  console.log('\nOK: re-export after import is identical');
} else {
  console.log('\nFAIL: re-export differs');
  console.log('--- second export ---');
  console.log(result.second);
  process.exitCode = 1;
}

console.log('\n=== native XML after round trip ===');
console.log(result.nativeAfter);
