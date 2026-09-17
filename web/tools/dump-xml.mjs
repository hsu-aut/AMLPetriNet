/**
 * Builds a small net in a real browser and prints the modeler's native XML.
 * Used to write the PNML serializer against the actual serialization rather
 * than against an assumption about the moddle schema.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('console', (m) => console.error('[page]', m.type(), m.text()));
page.on('pageerror', (e) => console.error('[pageerror]', e.message));
page.on('response', (r) => {
  if (r.status() >= 400) console.error('[http]', r.status(), r.url());
});

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const xml = await page.evaluate(async () => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const canvas = m.get('canvas');
  const root = canvas.getRootElement();

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

  // A bendpoint on the second arc: this is exactly what the upstream PNML
  // export throws away.
  modeling.updateWaypoints(a2, [
    { x: 345, y: 225 },
    { x: 420, y: 225 },
    { x: 420, y: 345 },
    { x: 500, y: 345 },
  ]);

  return (await m.saveXML({ format: true })).xml;
});

console.log(xml);
await browser.close();
server.close();
