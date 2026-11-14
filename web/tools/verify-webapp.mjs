/**
 * Drives the web mapper page in a real browser, against a running PtMapper.Web.
 *
 * The API can be checked with curl, and is. What cannot be checked that way is
 * the page: whether the modeler boots outside the WebView2 host, whether an AML
 * file dropped on it comes back as a diagram, and whether saving sends the
 * document it was opened from rather than a fresh one. That last point is the
 * whole reason this page exists, so it is asserted rather than assumed.
 *
 * Usage: node tools/verify-webapp.mjs [baseUrl]
 */
import { chromium } from 'playwright';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const base = process.argv[2] ?? 'http://127.0.0.1:5199';
const example = resolve(here, '../../examples/editor-test/QualityGate.aml');
const demo = resolve(here, '../../examples/paper/BridgingExample_MPS500_selfcontained.aml');

let failures = 0;

function check(name, ok, detail = '') {
  console.log(`${ok ? 'ok   ' : 'FAIL '} ${name}${detail ? ': ' + detail : ''}`);
  if (!ok) failures++;
}

const browser = await chromium.launch();
const page = await browser.newPage();

const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(e.message));
page.on('console', (m) => {
  if (m.type() === 'error') pageErrors.push(m.text());
});

// What the page hands back, caught where it is made rather than through the
// browser's download machinery: a blob download in headless Chromium reports
// itself as canceled, and the thing worth asserting is the content the page
// produced, not that Chromium wrote a file.
await page.addInitScript(() => {
  window.__downloads = [];
  const realCreate = URL.createObjectURL.bind(URL);
  URL.createObjectURL = (blob) => {
    const url = realCreate(blob);
    // Recorded synchronously: the page clicks the link in the same turn, and an
    // entry that only appears once blob.text() resolves is not there yet when
    // the click handler below looks for it.
    const entry = { url, name: null, body: null };
    window.__downloads.push(entry);
    blob.text().then((body) => { entry.body = body; });
    return url;
  };

  // The anchor click would still open a download; the name lives there.
  document.addEventListener('click', (event) => {
    const link = event.target.closest?.('a[download]');
    if (!link) return;
    event.preventDefault();
    const entry = window.__downloads.find((d) => d.url === link.href);
    if (entry) entry.name = link.download;
  }, true);
});

/**
 * The saved files, waiting for the one with this extension to have its content.
 * blob.text() resolves a turn later than the click, so reading straight after a
 * button press can catch an entry that is still empty.
 */
const saves = async (extension) => {
  await page.waitForFunction(
    (ext) => window.__downloads.some((d) => d.name?.endsWith(ext) && d.body !== null),
    extension, { timeout: 20000 });
  return page.evaluate(() => window.__downloads.filter((d) => d.name && d.body !== null));
};

await page.goto(base, { waitUntil: 'domcontentloaded' });

// ── boot ────────────────────────────────────────────────────────────────────

await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 20000 });
check('the modeler boots outside the WebView2 host', true);
check('no boot error is shown', !(await page.locator('#boot-error').isVisible()));
check('the buttons start disabled', await page.locator('#aml').isDisabled());

// ── opening an AML document ─────────────────────────────────────────────────

await page.setInputFiles('#file', example);
await page.waitForFunction(
  () => document.body.classList.contains('loaded'), null, { timeout: 20000 });

const status = await page.locator('#status').textContent();
check('the status line names the net', /place/.test(status), status.trim());

const shapes = await page.evaluate(() => {
  const registry = window.ptn.get('elementRegistry');
  const all = registry.getAll();
  const count = (suffix) => all.filter((e) => e.type?.endsWith(suffix)).length;
  return { places: count('Place'), transitions: count('Transition'), arcs: count('Arc') };
});
check('the places are on the canvas', shapes.places > 0, JSON.stringify(shapes));
check('the transitions are on the canvas', shapes.transitions > 0);
check('the arcs are on the canvas', shapes.arcs > 0);
check('the buttons are enabled once a net is loaded', !(await page.locator('#aml').isDisabled()));

// ── editing, then saving back into the same document ────────────────────────

const before = await readFile(example, 'utf8');

const added = await page.evaluate(() => {
  const m = window.ptn;
  const factory = m.get('elementFactory');
  const modeling = m.get('modeling');
  const root = m.get('canvas').getRootElement();

  const place = factory.createShape({ type: 'ptn:Place' });
  modeling.createShape(place, { x: 700, y: 120 }, root);
  modeling.updateLabel(place, 'AddedInTheBrowser');
  return place.id;
});
check('a place can be drawn on the canvas', Boolean(added));

await page.locator('#aml').click();
await page.waitForFunction(
  () => /Saved into/.test(document.getElementById('status').textContent), null, { timeout: 20000 });

const saved = (await saves('.aml')).find((d) => d.name.endsWith('.aml'));
check('saving hands back an AML file', Boolean(saved), saved?.name);

if (saved) {
  // The point of /api/update: the answer is the document that was opened, with
  // the net updated, not a new document holding only the net.
  const keptLibraries = saved.body.includes('ISO_PT_SystemUnitClassLib');
  const grew = saved.body.length > before.length * 0.9;
  const carriesTheNewPlace = saved.body.includes('AddedInTheBrowser');

  check('the saved document still carries its libraries', keptLibraries);
  check('the saved document is the one that was opened', grew,
    `${before.length} bytes in, ${saved.body.length} out`);
  check('the edit reached the document', carriesTheNewPlace);
}

// ── validation ──────────────────────────────────────────────────────────────

await page.locator('#validate').click();
await page.waitForFunction(
  () => /finding|No findings/.test(document.getElementById('status').textContent),
  null, { timeout: 20000 });

const verdict = await page.locator('#status').textContent();
check('validation answers', /finding/.test(verdict), verdict.trim());

// A place with no arc is an ISO/IEC 15909-1 warning, so the one just drawn has
// to show up and the findings table has to appear with it.
const rows = await page.locator('#findings tbody tr').count();
check('the findings table lists the new dangling place', rows > 0, `${rows} row(s)`);

// The server's findings are also in the list on the canvas, where the analysis
// of the modeler is: one list, not two. The closed button carries their count.
const onCanvas = await page.evaluate(() => ({
  host: window.ptn.get('findings').host.length,
  button: document.querySelector('.ptn-findings-button').textContent,
}));
check('the server findings are in the list on the canvas', onCanvas.host === rows,
  `${onCanvas.host} on the canvas, ${rows} in the table`);
check('the closed Analyse button carries their count', /^Analyse \(/.test(onCanvas.button), onCanvas.button);

// ── PNML export ─────────────────────────────────────────────────────────────

await page.locator('#pnml').click();
await page.waitForFunction(
  () => /PNML exported/.test(document.getElementById('status').textContent),
  null, { timeout: 20000 });

const pnml = (await saves('.pnml')).find((d) => d.name.endsWith('.pnml'));
check('the PNML export hands back a file', Boolean(pnml), pnml?.name);
check('the exported PNML is a P/T net',
  Boolean(pnml?.body.includes('grammar/ptnet')) && Boolean(pnml?.body.includes('AddedInTheBrowser')));

// ── the example button, which is what a visitor presses first ───────────────

await page.locator('#example').click();
await page.waitForFunction(
  () => /place|error/.test(document.getElementById('status').textContent), null, { timeout: 30000 });

const demoStatus = (await page.locator('#status').textContent()).trim();
check('the example button loads a net', /place/.test(demoStatus), demoStatus.slice(0, 90));
check('the example is the MPS500 bridging document',
  /IH_PT_BehaviorView/.test(demoStatus) && /6 places, 5 transitions, 12 arcs/.test(demoStatus));

// ── a file the page should refuse ───────────────────────────────────────────

await page.evaluate(async () => {
  const file = new File(['this is not a model'], 'broken.aml', { type: 'application/xml' });
  const transfer = new DataTransfer();
  transfer.items.add(file);
  document.getElementById('file').files = transfer.files;
  document.getElementById('file').dispatchEvent(new Event('change', { bubbles: true }));
});
await page.waitForFunction(
  () => document.getElementById('status').className === 'error', null, { timeout: 20000 });
check('a file that is not a model is reported, not swallowed', true,
  (await page.locator('#status').textContent()).trim().slice(0, 80));

// ── no noise in the console ─────────────────────────────────────────────────

check('nothing was logged as a page error', pageErrors.length === 0, pageErrors.slice(0, 3).join(' | '));

await browser.close();

console.log(failures === 0 ? '\nall web app checks passed' : `\n${failures} check(s) failed`);
process.exitCode = failures === 0 ? 0 : 1;
