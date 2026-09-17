/**
 * The SVG export, in a real browser.
 *
 * The picture has to stand on its own: every label present, the styles on the
 * elements rather than in a stylesheet, and a margin around the drawing so a
 * stroke at the edge is not cut in half.
 */
import { chromium } from 'playwright';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { serve } from './serve.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const pnml = await readFile(resolve(here, '../../examples/paper/MPS500_PT.pnml'), 'utf8');

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('[pageerror]', e.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const { svg, raw, labels } = await page.evaluate(async (text) => {
  await window.ptn.importPNML(text);
  // Arranged by the mapper in the real flow; here the file carries no layout,
  // so put the nodes somewhere before measuring.
  const modeling = window.ptn.get('modeling');
  const registry = window.ptn.get('elementRegistry');
  registry.getAll()
    .filter((e) => e.type === 'ptn:Place' || e.type === 'ptn:Transition')
    .forEach((node, i) => modeling.moveShape(node, { x: i * 120, y: (i % 3) * 90 }));

  return {
    svg: await window.ptnExportSvg(window.ptn),
    raw: (await window.ptn.saveSVG()).svg,
    // Only what the canvas draws: an arc's name is never rendered, and the
    // net's own name is not an element on it.
    labels: registry.getAll()
      .filter((e) => (e.type === 'ptn:Place' || e.type === 'ptn:Transition') && e.businessObject?.name)
      .map((e) => e.businessObject.name),
  };
}, pnml);

await browser.close();
server.close();

const problems = [];
const box = svg.match(/width="([\d.-]+)" height="([\d.-]+)" viewBox="([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+)"/);
if (!box) {
  problems.push('no width, height and viewBox on the svg element');
} else {
  const [, width, height, , , boxWidth, boxHeight] = box.map(Number);
  if (width !== boxWidth || height !== boxHeight) problems.push('size and viewBox disagree');

  // The margin is what the modeler's own export does not add.
  const plain = raw.match(/width="([\d.-]+)"\s+height="([\d.-]+)"/);
  if (!plain) problems.push('the modeler export has no size');
  else if (width - Number(plain[1]) !== 20 || height - Number(plain[2]) !== 20) {
    problems.push(`margin is ${width - Number(plain[1])}/${height - Number(plain[2])}, expected 20/20`);
  }
}
for (const label of labels) {
  if (!svg.includes(`>${label}<`)) problems.push(`label '${label}' is missing`);
}
if (!/font-family/.test(svg)) problems.push('no font on the labels');
if (/class="djs-label"[^>]*>\s*<\/text>/.test(svg)) problems.push('an empty label element');

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log(`OK: the picture carries all ${labels.length} labels, its own styles and a margin`);
}
