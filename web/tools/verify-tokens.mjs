/**
 * Tokens drawn as dots, in a real browser.
 *
 * The modeler writes a place's marking as a numeral. src/index.js draws up to
 * five tokens as dots and leaves larger markings as a number. This checks the
 * drawing for several markings, that the marking itself is unchanged in the
 * model and in PNML, that a changed marking is drawn again, and that the SVG
 * export carries the dots.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(e.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async () => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const registry = m.get('elementRegistry');
  const commandStack = m.get('commandStack');
  const root = m.get('canvas').getRootElement();

  const drawn = (place) => {
    const gfx = registry.getGraphics(place);
    return {
      dots: gfx.querySelectorAll('.ptn-tokens circle').length,
      text: [...gfx.querySelectorAll('text')].map((t) => t.textContent.trim()).filter(Boolean).join(' '),
    };
  };

  // The context pad's +/- entries pass rerender; without it the handler redraws nothing.
  const markings = [0, 1, 2, 3, 4, 5, 6, 12];
  const places = markings.map((marking, i) => {
    const place = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x: 100 + i * 80, y: 150 }, root);
    if (marking) modeling.updateProperty(place, 'initialMarking', marking, { rerender: true });
    return place;
  });

  const byMarking = Object.fromEntries(places.map((p, i) => [markings[i], drawn(p)]));

  modeling.updateProperty(places[1], 'initialMarking', 7, { rerender: true });
  const raised = drawn(places[1]);
  commandStack.undo();
  const undone = drawn(places[1]);

  const pnml = await m.savePNML();
  const svg = await window.ptnExportSvg(m);

  return {
    byMarking,
    raised,
    undone,
    stored: places.map((p) => p.businessObject.initialMarking ?? 0),
    pnmlMarkings: [...pnml.matchAll(/<initialMarking>\s*<text>(\d+)<\/text>/g)].map((x) => Number(x[1])),
    svgDots: (svg.match(/class="ptn-tokens"/g) || []).length,
  };
});

await browser.close();
server.close();

const problems = [];
for (const [marking, { dots, text }] of Object.entries(result.byMarking)) {
  const n = Number(marking);
  if (n >= 1 && n <= 5) {
    if (dots !== n) problems.push(`marking ${n} draws ${dots} dot(s)`);
    if (text) problems.push(`marking ${n} also writes '${text}'`);
  } else if (n === 0) {
    if (dots || text) problems.push(`marking 0 draws ${dots} dot(s) and '${text}'`);
  } else {
    if (dots) problems.push(`marking ${n} draws ${dots} dot(s)`);
    if (text !== String(n)) problems.push(`marking ${n} writes '${text}'`);
  }
}
if (result.raised.dots || result.raised.text !== '7') problems.push(`a marking raised to 7 draws ${result.raised.dots} dot(s) and '${result.raised.text}'`);
if (result.undone.dots !== 1 || result.undone.text) problems.push(`undo draws ${result.undone.dots} dot(s) and '${result.undone.text}'`);
if (result.stored.map(Number).join() !== '0,1,2,3,4,5,6,12') problems.push(`the model holds the markings ${result.stored.join()}`);
if (result.pnmlMarkings.join() !== '1,2,3,4,5,6,12') problems.push(`the PNML holds the markings ${result.pnmlMarkings.join()}`);
if (result.svgDots !== 5) problems.push(`the SVG export carries ${result.svgDots} token group(s), not 5`);
if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: one to five tokens are drawn as dots, more as a number; model, PNML and SVG agree');
}
