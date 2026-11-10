/**
 * Saving the answers as files, in a real browser.
 *
 * The writers are tested without a browser. What needs one is the handover:
 * whether the buttons produce a real download with the right name and the
 * right content, and whether a host that wants to save the file itself gets it
 * instead of a download happening behind its back.
 */
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage({ acceptDownloads: true });
const pageErrors = [];
page.on('pageerror', (error) => pageErrors.push(error.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

// The net: In -> Step -> Mid -> Pair -> Out, which gets stuck in Mid.
const empty = await page.evaluate(async () => {
  const modeler = window.ptn;
  const factory = modeler.get('elementFactory');
  const modeling = modeler.get('modeling');
  const root = modeler.get('canvas').getRootElement();

  const place = (x, name, marking) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Place' }), { x, y: 160 }, root);
    modeling.updateLabel(shape, name);
    if (marking) modeling.updateProperty(shape, 'initialMarking', marking, { rerender: true });
    return shape;
  };
  const transition = (x, name) => {
    const shape = modeling.createShape(factory.createShape({ type: 'ptn:Transition' }), { x, y: 160 }, root);
    modeling.updateLabel(shape, name);
    return shape;
  };

  const inPlace = place(120, 'In', 1);
  const step = transition(240, 'Step');
  const mid = place(360, 'Mid');
  const pair = transition(480, 'Pair');
  const out = place(600, 'Out');
  modeling.connect(inPlace, step, { type: 'ptn:Arc' });
  modeling.connect(step, mid, { type: 'ptn:Arc' });
  const needsTwo = modeling.connect(mid, pair, { type: 'ptn:Arc' });
  modeling.updateLabel(needsTwo, '2');
  modeling.connect(pair, out, { type: 'ptn:Arc' });

  // Nothing analysed yet, so there is nothing to save and the panel says so.
  modeler.get('findingsPanel').open();
  const went = modeler.get('findingsPanel').save();
  return { went, note: document.querySelector('.ptn-findings-note')?.textContent ?? null };
});

/** Clicks what the selector names and returns the file that came out of it. */
async function saved(selector) {
  const [download] = await Promise.all([
    page.waitForEvent('download', { timeout: 10000 }),
    page.click(selector),
  ]);
  const path = await download.path();
  return { name: download.suggestedFilename(), text: await readFile(path, 'utf8') };
}

await page.click('.ptn-findings-button');
const findings = await saved('.ptn-findings-save');
const findingsNote = await page.textContent('.ptn-findings-note');

await page.click('.ptn-states-button');
const dot = await saved('.ptn-states-save-dot');
const markings = await saved('.ptn-states-save-csv');
const statesNote = await page.textContent('.ptn-states-note');

// A host that saves the file itself gets it, and nothing is downloaded.
const host = await page.evaluate(() => {
  const modeler = window.ptn;
  const taken = [];
  modeler.get('eventBus').on('export.file', (event) => {
    taken.push({ name: event.name, length: event.text.length });
    return true;
  });

  return {
    findings: modeler.get('findingsPanel').save(),
    states: modeler.get('reachabilityView').save('dot'),
    taken,
  };
});

let strayDownload = false;
page.on('download', () => { strayDownload = true; });
await page.waitForTimeout(300);

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };

check(empty.went === 'nothing', `saving nothing returned '${empty.went}'`);
check(/no findings to save/.test(empty.note ?? ''), `saving nothing said '${empty.note}'`);

// Named after the net, so that two nets do not both produce "findings.csv".
check(findings.name === 'model-1-findings.csv', `the findings came out as '${findings.name}'`);
check(findings.text.startsWith('rule,severity,element,message\n'),
  `the table starts with '${findings.text.split('\n')[0]}'`);
check(/PT14,warning,[^,]+,Pair can never fire/.test(findings.text), 'the dead transition is not in the table');
// No comma in this sentence, so no quotes around it: the quoting itself is
// checked where a message does hold one, in the writer's own tests.
check(/^PT15,warning,,No transition is enabled in the marking \(Mid 1\)\.$/m.test(findings.text),
  'the deadlock is not in the table');
check(/saved as model-1-findings.csv/.test(findingsNote ?? ''), `saving the findings said '${findingsNote}'`);

check(dot.name === 'model-1-state-space.dot', `the state space came out as '${dot.name}'`);
check(dot.text.startsWith('digraph state_space {'), `the DOT starts with '${dot.text.split('\n')[0]}'`);
check(/label="In 1", shape=doublecircle/.test(dot.text), 'the initial marking is not the double circle');
check(/label="Step"/.test(dot.text), 'the firing is not labelled in the DOT');

check(markings.name === 'model-1-markings.csv', `the markings came out as '${markings.name}'`);
check(markings.text.split('\n')[0] === 'marking,firings,deadlock,In,Mid,Out',
  `the table head is '${markings.text.split('\n')[0]}'`);
check(/^initial,0,no,1,0,0$/m.test(markings.text), 'the initial marking is not the first row');
check(/,1,yes,0,1,0$/m.test(markings.text), 'the marking the net is stuck in is not marked as a dead end');
check(/saved as model-1-markings.csv/.test(statesNote ?? ''), `saving the markings said '${statesNote}'`);

check(host.findings === 'host' && host.states === 'host',
  `a host that takes the file got '${host.findings}' and '${host.states}'`);
check(host.taken.length === 2, `the host was handed ${host.taken.length} files`);
check(host.taken[0]?.name === 'model-1-findings.csv' && host.taken[1]?.name === 'model-1-state-space.dot',
  `the host was handed ${JSON.stringify(host.taken.map((file) => file.name))}`);
check(host.taken.every((file) => file.length > 0), 'a file handed to the host was empty');
check(strayDownload === false, 'a file was downloaded although the host took it');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: the findings, the state space and the markings come out as files, or go to the host');
}
