/**
 * Numbers in the modeler's PNML export, in a real browser.
 *
 * The ISO/IEC 15909-2 grammar types coordinates as xs:decimal, which has no
 * exponent, and node sizes as decimals with at most one digit after the point.
 * A node resized to an odd width and a label nudged by a float remainder used
 * to produce both violations.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('[pageerror]', e.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const pnml = await page.evaluate(async () => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const root = m.get('canvas').getRootElement();

  const place = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x: 150.333, y: 200 }, root);
  const transition = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x: 400, y: 200 }, root);
  modeling.updateLabel(place, 'Odd');
  modeling.resizeShape(place, { x: place.x, y: place.y, width: 50.25, height: 47.77 });
  modeling.connect(place, transition, { type: 'ptn:Arc' });

  // A label a float remainder away from its default position.
  const label = place.label;
  if (label) modeling.moveShape(label, { x: 5.551115123125783e-17, y: 0.1 + 0.2 - 0.3 });

  return m.savePNML();
});

await browser.close();
server.close();

const problems = [];
const numbers = [...pnml.matchAll(/\s(x|y)="([^"]*)"/g)].map((match) => match[2]);
for (const value of numbers) {
  if (!/^-?\d+(\.\d+)?$/.test(value)) problems.push(`'${value}' is not an xs:decimal`);
}
for (const match of pnml.matchAll(/<dimension x="([^"]*)" y="([^"]*)"/g)) {
  for (const value of [match[1], match[2]]) {
    if (!/^\d{1,4}(\.\d)?$/.test(value) || value.replace('.', '').length > 4) {
      problems.push(`dimension '${value}' has more than one decimal or four digits`);
    }
  }
}
if (!/<dimension /.test(pnml)) problems.push('no dimension was written');

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  console.log(pnml);
  process.exitCode = 1;
} else {
  console.log(`OK: ${numbers.length} coordinates are plain decimals, node sizes carry at most one decimal`);
}
