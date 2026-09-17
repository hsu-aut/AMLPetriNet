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

// ── token replay ────────────────────────────────────────────────────────────
// The simulator moves tokens by writing initialMarking through the command
// stack, so without special handling a simulation would look like the user
// editing every marking, and the host could save a mid-simulation state.
await clearMessages();
const markingBefore = await page.evaluate(async () => (await window.ptn.savePNML()));

await page.evaluate(() => window.ptn.get('toggleSimulationMode').toggleMode(true));
await page.evaluate(() => {
  const registry = window.ptn.get('elementRegistry');
  const enabled = registry.filter((e) => e.type === 'ptn:Transition')[0];
  window.ptn.get('eventBus').fire('tokenSimulation.fire', { element: enabled });
});
await page.waitForTimeout(700);

// Guard against a hollow assertion: if firing changed nothing, "no edit
// reported" would pass for the wrong reason.
const markingDuring = await page.evaluate(async () => (await window.ptn.savePNML()));
check(markingDuring !== markingBefore, 'firing really moved tokens (otherwise the next check proves nothing)');
check((await messagesOfType('changed')).length === 0, 'firing a transition in replay mode reports no edit');

// The host asking for an export during a replay must be refused for the same
// reason: the answer would be the simulator's marking, not the model's.
await clearMessages();
await page.evaluate(() => window.__hostSend({ type: 'requestExport' }));
await page.waitForTimeout(500);
check((await messagesOfType('changed')).length === 0, 'requestExport during a replay reports no net');
check((await messagesOfType('error')).length > 0, 'requestExport during a replay says why');

await clearMessages();
await page.evaluate(() => window.ptn.get('toggleSimulationMode').toggleMode(false));
await page.waitForTimeout(700);
const afterReplay = await messagesOfType('changed');
check(afterReplay.length > 0, 'leaving replay mode reports the restored net');
check(afterReplay.at(-1).pnml === markingBefore, 'the restored net equals the one before the simulation');

// ── theme ───────────────────────────────────────────────────────────────────
await page.evaluate(() => window.__hostSend({ type: 'setTheme', theme: 'dark' }));
await page.waitForTimeout(100);
check(await page.evaluate(() => document.documentElement.getAttribute('data-theme')) === 'dark',
  'setTheme reaches the document');

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
