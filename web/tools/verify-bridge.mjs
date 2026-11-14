/**
 * Exercises the WebView2 bridge protocol against a stand-in host.
 *
 * The other browser checks talk to the modeler directly and never touch
 * src/bridge.js, because window.chrome.webview does not exist outside WebView2.
 * That left the whole host-facing surface untested. This installs a stand-in for
 * that object before the page boots and drives the real message protocol the C#
 * side speaks.
 */
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const samplePnml = (await readFile(resolve(repoRoot, 'examples/quality-gate.pnml'), 'utf8')).trim();

const failures = [];
const check = (ok, what) => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}`);
  if (!ok) failures.push(what);
};

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('pageerror', (e) => console.error('[pageerror]', e.message));

// Stand in for the host object WebView2 injects. Must be installed before any
// page script runs, which is what addInitScript guarantees.
await page.addInitScript(() => {
  const listeners = [];
  window.__hostMessages = [];
  window.chrome = {
    webview: {
      postMessage: (message) => window.__hostMessages.push(message),
      addEventListener: (_type, handler) => listeners.push(handler),
    },
  };
  // What PostWebMessageAsJson does on the C# side: deliver an object as e.data.
  window.__hostSend = (message) => listeners.forEach((h) => h({ data: message }));
});

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const waitFor = (type, timeout = 5000) =>
  page.waitForFunction(
    (t) => window.__hostMessages.some((m) => m.type === t),
    type,
    { timeout },
  );

const messagesOfType = (type) =>
  page.evaluate((t) => window.__hostMessages.filter((m) => m.type === t), type);

const clearMessages = () => page.evaluate(() => (window.__hostMessages.length = 0));

// ── boot ────────────────────────────────────────────────────────────────────
await waitFor('ready');
const ready = (await messagesOfType('ready'))[0];
check(typeof ready.url === 'string' && ready.url.includes('index.html'), 'boot posts ready with the page url');

// ── import ──────────────────────────────────────────────────────────────────
await clearMessages();
await page.evaluate((pnml) => window.__hostSend({ type: 'importPNML', pnml }), samplePnml);
await waitFor('imported');

const imported = (await messagesOfType('imported'))[0];
check(typeof imported.pnml === 'string' && imported.pnml.includes('quality_gate'),
  'importPNML is answered with an echo baseline');

const placeCount = await page.evaluate(() =>
  window.ptn.get('elementRegistry').filter((e) => e.type === 'ptn:Place').length);
check(placeCount === 5, `the imported net really reached the canvas (${placeCount} places)`);

// An import must not look like a user edit. The bridge debounces changes by
// 300 ms, so wait past that window before judging.
await clearMessages();
await page.waitForTimeout(700);
const changesAfterImport = await messagesOfType('changed');
check(changesAfterImport.length === 0, 'an import raises no change events of its own');

// ── user edit ───────────────────────────────────────────────────────────────
await clearMessages();
await page.evaluate(() => {
  const registry = window.ptn.get('elementRegistry');
  const place = registry.filter((e) => e.type === 'ptn:Place')[0];
  window.ptn.get('modeling').updateProperty(place, 'initialMarking', 7);
});
await waitFor('changed');
const changed = (await messagesOfType('changed'))[0];
check(changed.pnml.includes('<text>7</text>'), 'a user edit is reported as changed, with the new value');

// ── explicit export request ─────────────────────────────────────────────────
await clearMessages();
await page.evaluate(() => window.__hostSend({ type: 'requestExport' }));
await waitFor('changed');
check((await messagesOfType('changed'))[0].pnml.includes('quality_gate'), 'requestExport returns the current net');

// ── select ──────────────────────────────────────────────────────────────────
await clearMessages();
await page.evaluate(() => window.__hostSend({ type: 'selectElement', id: 'p_waiting' }));
await page.waitForTimeout(200);
const selected = await page.evaluate(() =>
  window.ptn.get('selection').get().map((e) => e.id));
check(selected.includes('p_waiting'), 'selectElement selects the element by its PNML id');
check((await messagesOfType('error')).length === 0, 'selectElement does not raise an error');

// And the other way round: what the user selects on the canvas is reported, so
// the host can point at the same element in its own view.
const told = await messagesOfType('selected');
check(told.some((m) => m.id === 'p_waiting' && m.count === 1),
  `the selection was reported as ${JSON.stringify(told)}`);

await clearMessages();
await page.evaluate(() => window.ptn.get('selection').select(null));
await page.waitForTimeout(200);
check((await messagesOfType('selected')).some((m) => m.id === null && m.count === 0),
  'clearing the selection was not reported');

await clearMessages();
await page.evaluate(() => window.__hostSend({ type: 'selectElement', id: 'does_not_exist' }));
await page.waitForTimeout(200);
check((await messagesOfType('error')).length === 0, 'an unknown id is not treated as an error');

// ── two imports in quick succession ─────────────────────────────────────────
// A document switch right after a refresh sends two imports back to back. They
// must not interleave: the first one finishing would otherwise clear the
// suppression flag while the second is still running, and the second import's
// own command stack traffic would be reported as user edits.
await clearMessages();
await page.evaluate((pnml) => {
  window.__hostSend({ type: 'importPNML', pnml });
  window.__hostSend({ type: 'importPNML', pnml });
}, samplePnml);
await page.waitForTimeout(1500);

check((await messagesOfType('imported')).length === 2, 'both imports are acknowledged');
check((await messagesOfType('changed')).length === 0, 'back-to-back imports report no edits');
check((await messagesOfType('error')).length === 0, 'back-to-back imports raise no error');

// ── simulation ──────────────────────────────────────────────────────────────
// The simulation keeps the current marking beside the model and draws that, so
// firing is not an edit and the model never holds a half-finished run. The
// host is told that a run is on, and an export during a run is still the
// document the user drew.
await clearMessages();
const markingBefore = await page.evaluate(async () => (await window.ptn.savePNML()));

await page.evaluate(() => window.ptn.get('simulation').start());
await page.waitForTimeout(300);
check((await messagesOfType('simulation')).some((m) => m.active === true),
  'the host is told that a simulation started');

const fired = await page.evaluate(() => window.ptn.get('simulation').fireOne());
await page.waitForTimeout(700);

// Guard against a hollow assertion: if nothing fired, "no edit reported" and
// "the document is unchanged" would both pass for the wrong reason.
check(fired === true, 'a transition really fired (otherwise the next checks prove nothing)');
check(await page.evaluate(() => window.ptn.get('simulation').steps.length) === 1,
  'the firing is recorded as a step of the run');
check((await messagesOfType('changed')).length === 0, 'firing a transition reports no edit');

// What fired, by name, so the host can say so while a run is going.
const firings = await messagesOfType('fired');
check(firings.length === 1 && Array.isArray(firings[0].ids) && firings[0].ids.length === 1,
  `the firing was reported as ${JSON.stringify(firings)}`);

const markingDuring = await page.evaluate(async () => (await window.ptn.savePNML()));
check(markingDuring === markingBefore, 'the model is unchanged while the net runs');

// An export during a run is safe now, and the host gets the model.
await clearMessages();
await page.evaluate(() => window.__hostSend({ type: 'requestExport' }));
await page.waitForTimeout(500);
const during = await messagesOfType('changed');
check(during.length === 1, 'requestExport during a run answers with the net');
check(during[0]?.pnml === markingBefore, 'the export during a run is the model, not the run');
check((await messagesOfType('error')).length === 0, 'requestExport during a run raises no error');

await clearMessages();
await page.evaluate(() => window.ptn.get('simulation').stop());
await page.waitForTimeout(300);
check((await messagesOfType('simulation')).some((m) => m.active === false),
  'the host is told that the simulation stopped');
const afterRun = await page.evaluate(async () => (await window.ptn.savePNML()));
check(afterRun === markingBefore, 'the net after the run is the one before it');

// ── theme ───────────────────────────────────────────────────────────────────
await page.evaluate(() => window.__hostSend({ type: 'setTheme', theme: 'dark' }));
await page.waitForTimeout(100);
check(await page.evaluate(() => document.documentElement.getAttribute('data-theme')) === 'dark',
  'setTheme reaches the document');

// ── findings of the host ────────────────────────────────────────────────────
// The host checks the net by itself (the structural rules of ISO/IEC 15909-1)
// and hands the result to the page, which lists it with what the analysis
// finds, so that there is one list. They stay until the host sends again.
const hostRows = () => page.evaluate(() => [...document.querySelectorAll('.ptn-finding')].map((item) => ({
  rule: item.querySelector('.ptn-finding-rule')?.textContent ?? '',
  message: item.querySelector('.ptn-finding-message')?.textContent ?? '',
  clickable: item.classList.contains('ptn-findings-clickable'),
})));
const analyseButton = () => page.evaluate(() => document.querySelector('.ptn-findings-button').textContent);

await clearMessages();
await page.evaluate(() => window.__hostSend({
  type: 'hostFindings',
  findings: [
    { rule: 'PT05', severity: 'error', element: 'p_waiting', message: 'An arc joins two places.' },
    { rule: 'PT09', severity: 'warning', element: 'no_such_element', message: 'A place has no arc.' },
    { rule: 'PT10', severity: 'warning', message: 'A finding about the whole net.' },
  ],
}));
await page.waitForTimeout(200);

check((await messagesOfType('error')).length === 0, 'hostFindings raises no error');
check(await analyseButton() === 'Analyse (1 error, 2 warnings)',
  `the closed button carries the host's count: '${await analyseButton()}'`);

await page.evaluate(() => window.ptn.get('findingsPanel').open());
const hostList = await hostRows();
check(hostList.length === 3 && hostList[0].rule === 'PT05', `the host's findings are listed, errors first: ${JSON.stringify(hostList)}`);
check(hostList[0].clickable === true, 'a finding about an element on the canvas is not a link');
check(hostList[1].clickable === false, 'a finding about an element that is not on the canvas is a link');
check(hostList[2].clickable === false, 'a finding about the whole net is a link');

await page.evaluate(() => document.querySelector('.ptn-finding').click());
await page.waitForTimeout(200);
check(await page.evaluate(() => window.ptn.get('selection').get().map((e) => e.id).join()) === 'p_waiting',
  'a click on the finding selects the element by its PNML id');

// The page analyses by itself too, and the two lists become one.
await page.evaluate(() => window.ptn.get('findingsPanel').analyse());
const merged = await hostRows();
check(merged.some((row) => row.rule === 'PT05') && merged.some((row) => row.rule === 'PT13'),
  `the host's findings and the analysis share one list: ${JSON.stringify(merged.map((row) => row.rule))}`);

// An edit drops what the analysis computed and leaves the host's findings.
await page.evaluate(() => {
  const place = window.ptn.get('elementRegistry').filter((e) => e.type === 'ptn:Place')[0];
  window.ptn.get('modeling').updateProperty(place, 'initialMarking', 3);
});
await page.waitForTimeout(100);
const edited = (await hostRows()).map((row) => row.rule);
check(edited.includes('PT05') && !edited.includes('PT13'), `after an edit the list holds ${JSON.stringify(edited)}`);

// A new import does not clear them either: the host does that, when it has
// loaded another net, by sending an empty list.
await page.evaluate((pnml) => window.__hostSend({ type: 'importPNML', pnml }), samplePnml);
await page.waitForTimeout(700);
check((await hostRows()).some((row) => row.rule === 'PT05'), 'an import dropped the findings of the host');

await page.evaluate(() => window.__hostSend({ type: 'hostFindings', findings: [] }));
await page.waitForTimeout(100);
check((await hostRows()).every((row) => row.rule === ''), 'an empty list did not clear the findings of the host');

// Whatever else arrives is read as far as it can be and never takes the page down.
await clearMessages();
await page.evaluate(() => {
  window.__hostSend({ type: 'hostFindings', findings: 'not a list' });
  window.__hostSend({ type: 'hostFindings', findings: [null, 7, 'x', { rule: 5, severity: 'fatal', message: {} }] });
});
await page.waitForTimeout(200);
// The note that the analysis was dropped is a row of its own and is not a finding.
const odd = (await hostRows()).filter((row) => !/Analyse again/.test(row.message));
check(odd.length === 1 && odd[0].rule === '', `a malformed finding was read as ${JSON.stringify(odd)}`);
check((await messagesOfType('error')).length === 0, 'malformed host findings raise no error');
await page.evaluate(() => window.__hostSend({ type: 'hostFindings', findings: [] }));

// ── malformed input from the host ───────────────────────────────────────────
await clearMessages();
await page.evaluate(() => {
  window.__hostSend({ type: 'importPNML', pnml: '<not-pnml>' });
});
await page.waitForTimeout(500);
check((await messagesOfType('error')).length > 0, 'a broken import is reported as an error');
check(await page.evaluate(() => window.ptn != null), 'a broken import does not take the modeler down');

await browser.close();
server.close();

if (failures.length) {
  console.log(`\n${failures.length} bridge check(s) failed`);
  process.exitCode = 1;
} else {
  console.log('\nall bridge checks passed');
}
