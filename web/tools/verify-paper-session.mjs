/**
 * An editing session on a real document, through the real modeler.
 *
 * Opens an AML file in the web mapper page, moves one node on the canvas,
 * saves the document back, and writes the result next to the input so that
 * `ptmap compare` and `ptmap roundtrip` can say what the session changed.
 *
 * This is the measurement the C#-only round-trip tests cannot make: the net
 * has to pass through the modeler's own PNML importer and exporter, which is
 * where every arc name was once lost without any .NET test noticing.
 *
 * Usage: node web/tools/verify-paper-session.mjs <in.aml> <out.aml> [baseUrl] [--move <name>|--no-move]
 *
 * By default the first place is moved. --move picks a node by name, --no-move
 * saves without touching the canvas. Which node moves matters: the modeler
 * redraws the arcs of a node that is dragged, so moving the end of a routed
 * arc straightens it, and the numbers a session produces depend on the choice.
 */
import { chromium } from 'playwright';
import { writeFile } from 'node:fs/promises';
import { basename } from 'node:path';

const args = process.argv.slice(2);
const flag = (name) => {
  const i = args.indexOf(name);
  if (i < 0) return undefined;
  const value = args[i + 1];
  args.splice(i, value && !value.startsWith('--') ? 2 : 1);
  return value && !value.startsWith('--') ? value : true;
};
const noMove = flag('--no-move') === true;
const moveName = flag('--move');
const [input, output, base = 'http://127.0.0.1:5199'] = args;
if (!input || !output) {
  console.error('usage: node web/tools/verify-paper-session.mjs <in.aml> <out.aml> [baseUrl]');
  process.exit(2);
}

const browser = await chromium.launch();
const page = await browser.newPage();

const problems = [];
page.on('pageerror', (e) => problems.push('pageerror: ' + e.message));
page.on('console', (m) => {
  if (m.type() === 'error' || m.type() === 'warning') problems.push(`${m.type()}: ${m.text()}`);
});

// The saved document is caught where the page makes it; see verify-webapp.mjs.
await page.addInitScript(() => {
  window.__downloads = [];
  const realCreate = URL.createObjectURL.bind(URL);
  URL.createObjectURL = (blob) => {
    const url = realCreate(blob);
    const entry = { url, name: null, body: null };
    window.__downloads.push(entry);
    blob.text().then((body) => { entry.body = body; });
    return url;
  };
  document.addEventListener('click', (event) => {
    const link = event.target.closest?.('a[download]');
    if (!link) return;
    event.preventDefault();
    const entry = window.__downloads.find((d) => d.url === link.href);
    if (entry) entry.name = link.download;
  }, true);
});

await page.goto(base, { waitUntil: 'domcontentloaded' });
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 30000 });

await page.setInputFiles('#file', input);
await page.waitForFunction(() => document.body.classList.contains('loaded'), null, { timeout: 30000 });
const opened = (await page.locator('#status').textContent()).trim();
console.log('opened:', opened);

// What a user does: drag one node a little, or nothing at all.
const moved = noMove ? '(nothing)' : await page.evaluate((name) => {
  const m = window.ptn;
  const registry = m.get('elementRegistry');
  const node = registry.getAll().find((e) =>
    (e.type === 'ptn:Place' || e.type === 'ptn:Transition')
    && (name ? e.businessObject?.name === name : e.type === 'ptn:Place'));
  if (!node) return null;
  m.get('modeling').moveShape(node, { x: 15, y: 10 });
  return node.businessObject?.name ?? node.id;
}, typeof moveName === 'string' ? moveName : undefined);
console.log('moved:', moved);

await page.locator('#aml').click();
await page.waitForFunction(
  () => /Saved into|error/.test(document.getElementById('status').className + ' ' + document.getElementById('status').textContent),
  null, { timeout: 30000 });
console.log('saved:', (await page.locator('#status').textContent()).trim());

await page.waitForFunction(
  () => window.__downloads.some((d) => d.name?.endsWith('.aml') && d.body !== null), null, { timeout: 30000 });
const saved = await page.evaluate(() => window.__downloads.find((d) => d.name?.endsWith('.aml') && d.body !== null));

await browser.close();

await writeFile(output, saved.body, 'utf8');
console.log(`written: ${output} (${saved.body.length} bytes, from ${basename(input)})`);

if (problems.length) {
  console.log('page reported:');
  for (const p of problems) console.log('  ' + p);
}
