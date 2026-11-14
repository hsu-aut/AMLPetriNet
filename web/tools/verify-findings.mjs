/**
 * The findings panel, in a real browser.
 *
 * The analysis itself is tested without a browser; what needs one is the list:
 * whether it shows what the core found, whether a row leads to the element it
 * is about, whether it is cleared when the net changes under it, and whether
 * findings handed in by the host survive an analysis.
 */
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
const pageErrors = [];
page.on('pageerror', (error) => pageErrors.push(error.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async () => {
  const modeler = window.ptn;
  const factory = modeler.get('elementFactory');
  const modeling = modeler.get('modeling');
  const findings = modeler.get('findings');
  const selection = modeler.get('selection');
  const root = modeler.get('canvas').getRootElement();

  // In -> Step -> Mid -> Pair -> Out, where Pair needs two tokens and only one
  // ever arrives: the net gets stuck in Mid and Pair never fires.
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

  const rows = () => [...document.querySelectorAll('.ptn-finding')].map((item) => ({
    rule: item.querySelector('.ptn-finding-rule')?.textContent ?? '',
    severity: [...item.classList].find((name) => name.startsWith('ptn-finding-') && name !== 'ptn-finding-mark')
      ?.replace('ptn-finding-', '') ?? '',
    message: item.querySelector('.ptn-finding-message')?.textContent ?? '',
    clickable: item.classList.contains('ptn-findings-clickable'),
  }));
  const panelHidden = () => document.querySelector('.ptn-findings')?.hidden;

  const before = { hidden: panelHidden(), rows: rows().length };

  const pnmlBefore = await modeler.savePNML();
  document.querySelector('.ptn-findings-button').click();
  const analysed = {
    hidden: panelHidden(),
    title: document.querySelector('.ptn-findings-title')?.textContent ?? '',
    rows: rows(),
  };
  const pnmlAfter = await modeler.savePNML();

  // A row that names an element selects it.
  selection.select(null);
  const deadRow = [...document.querySelectorAll('.ptn-finding')]
    .find((item) => item.querySelector('.ptn-finding-rule')?.textContent === 'PT14');
  deadRow?.click();
  const selected = selection.get().map((element) => element.businessObject.name);

  // Findings from the host sit in the same list and survive an analysis.
  findings.setHostFindings([
    { rule: 'PT02', severity: 'error', element: mid.id, message: 'The place has no identifier.' },
  ]);
  const withHost = rows();
  findings.analyse();
  const afterSecond = rows();

  // Editing the net drops what the analysis computed rather than leaving a
  // stale list on screen. What the host found stays, because only the host can
  // say whether the edit cured it.
  modeling.updateLabel(out, 'Finished');
  const afterEdit = rows();

  // With the list closed the button says what the host found.
  document.querySelector('.ptn-findings-close').click();
  const button = () => document.querySelector('.ptn-findings-button');
  const closed = { text: button().textContent, error: button().classList.contains('ptn-findings-button-error') };

  // A finding about an element that is gone is no longer a link.
  const linkBefore = rows().find((row) => row.rule === 'PT02')?.clickable;
  modeling.removeShape(mid);
  const linkAfter = rows().find((row) => row.rule === 'PT02')?.clickable;

  // The host empties its list; the button goes back to its plain name.
  findings.setHostFindings([]);
  const cleared = { text: button().textContent, error: button().classList.contains('ptn-findings-button-error') };

  return {
    before,
    analysed,
    selected,
    withHost,
    afterSecond,
    afterEdit,
    closed,
    linkBefore,
    linkAfter,
    cleared,
    documentUnchanged: pnmlBefore === pnmlAfter,
  };
});

await browser.close();
server.close();

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };
const ruleOf = (rows, rule) => rows.filter((row) => row.rule === rule);

check(result.before.hidden === true, 'the panel is open before anything was analysed');
check(result.before.rows === 0, 'the panel holds findings before anything was analysed');

check(result.analysed.hidden === false, 'the panel stayed closed after Analyse');
check(result.analysed.rows.length > 0, 'the analysis reported nothing at all');
check(/Findings \(0 error\(s\), 2 warning\(s\)/.test(result.analysed.title),
  `the title says '${result.analysed.title}'`);

const dead = ruleOf(result.analysed.rows, 'PT14');
check(dead.length === 1 && dead[0].severity === 'warning', 'the transition that never fires is not reported as a warning');
check(/Pair can never fire/.test(dead[0]?.message ?? ''), `the dead transition reads '${dead[0]?.message}'`);
check(dead[0]?.clickable === true, 'the row about an element is not clickable');

const stuck = ruleOf(result.analysed.rows, 'PT15');
check(stuck.length === 1 && /Mid 1/.test(stuck[0].message), `the deadlock reads '${stuck[0]?.message}'`);
check(stuck[0]?.clickable === false, 'a finding about the whole net should not pretend to lead somewhere');

check(ruleOf(result.analysed.rows, 'PT13').length === 3, 'every place should carry its bound');
check(result.documentUnchanged, 'analysing changed the document');

check(result.selected.join() === 'Pair', `the row selected ${result.selected.join() || 'nothing'}`);

check(ruleOf(result.withHost, 'PT02').length === 1, 'a finding from the host does not show');
check(result.withHost[0]?.rule === 'PT02', `the error is not first; the list starts with ${result.withHost[0]?.rule}`);
check(ruleOf(result.afterSecond, 'PT02').length === 1, 'analysing again dropped the findings from the host');

// What the analysis computed is gone, what the host found is still there.
check(ruleOf(result.afterEdit, 'PT02').length === 1,
  `the host's finding did not survive an edit: ${JSON.stringify(result.afterEdit)}`);
check(!result.afterEdit.some((row) => /^PT1\d$|^PT2\d$/.test(row.rule)),
  `editing the net left ${result.afterEdit.filter((row) => /^PT1|^PT2/.test(row.rule)).length} stale finding(s) of the analysis on screen`);
// And it says that it dropped them, not that the net is fine.
check(result.afterEdit.some((row) => row.rule === '' && /net changed.*Analyse again/.test(row.message)),
  `after an edit the panel does not say what it dropped: ${JSON.stringify(result.afterEdit)}`);

check(result.closed.text === 'Analyse (1 error)', `the closed button reads '${result.closed.text}'`);
check(result.closed.error === true, 'the closed button does not look like an error');
check(result.linkBefore === true, 'the host finding about an existing element is not a link');
check(result.linkAfter === false, 'a finding about a deleted element is still a link');
check(result.cleared.text === 'Analyse' && result.cleared.error === false,
  `the button after the host cleared its list reads '${result.cleared.text}'`);

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log('FAIL: ' + problems.join('; '));
  process.exitCode = 1;
} else {
  console.log('OK: the findings list shows what the net does and leads to the element it is about');
}
