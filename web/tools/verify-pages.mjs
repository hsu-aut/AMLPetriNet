/**
 * A net from another tool, through the modeler and back out.
 *
 * The canvas has one plane and no pages, so a file with pages is flattened to
 * be drawn. Flattening is where a tool quietly reorganises somebody else's
 * document: the pages are gone, the reference nodes that let an arc reach
 * another page are gone, and whatever that tool wrote in its own section is
 * gone with them. This checks that none of it is.
 *
 * The fixture is examples/foreign-pages.pnml. Its grammar is checked separately
 * by tools/validate-pnml.mjs, which needs Java; this check needs a browser.
 */
import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { serve } from './serve.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const fixture = resolve(here, '..', '..', 'examples', 'foreign-pages.pnml');
const pnml = await readFile(fixture, 'utf8');

const { server, port } = await serve();
const browser = await chromium.launch();
const page = await browser.newPage();
const pageErrors = [];
page.on('pageerror', (error) => pageErrors.push(error.message));

await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });

const result = await page.evaluate(async (text) => {
  const modeler = window.ptn;
  await modeler.importPNML(text);

  const once = await modeler.savePNML();
  await modeler.importPNML(once);
  const twice = await modeler.savePNML();

  const registry = modeler.get('elementRegistry');
  const drawn = registry
    .filter((element) => ['ptn:Place', 'ptn:Transition', 'ptn:Arc'].includes(element.type) && !element.labelTarget)
    .map((element) => element.id);

  // Where each element ended up, read out of the export rather than matched
  // with a pattern: which page something is on is a question about the tree.
  const exported = new DOMParser().parseFromString(once, 'application/xml');
  const PNML = 'http://www.pnml.org/version-2009/grammar/pnml';
  const pageOf = {};
  const walk = (parent, path) => {
    for (const child of parent.children) {
      if (child.namespaceURI !== PNML) continue;
      if (child.localName === 'page') {
        const inside = path ? `${path}/${child.getAttribute('id')}` : child.getAttribute('id');
        pageOf[child.getAttribute('id')] = path;
        walk(child, inside);
        continue;
      }
      if (['place', 'transition', 'arc', 'referencePlace', 'referenceTransition'].includes(child.localName)) {
        pageOf[child.getAttribute('id')] = path;
      }
    }
  };
  walk(exported.getElementsByTagNameNS(PNML, 'net')[0], '');

  // The net as the analysis core reads it: the arc that went through the
  // reference has to reach the real place, or the net is not the net.
  const net = modeler.get('findings');
  net.analyse();

  return { once, twice, drawn, pageOf, findings: net.all.length };
}, pnml);

await browser.close();
server.close();

// Outside the project: dist/ is what the plugin package is built from, and a
// test must not put a file into a package.
const out = join(await mkdtemp(join(tmpdir(), 'ptn-pages-')), 'foreign-pages.out.pnml');
await writeFile(out, result.once, 'utf8');

const problems = [];
const check = (condition, message) => { if (!condition) problems.push(message); };
const has = (pattern, what) => check(pattern.test(result.once), `${what} is missing from the export`);

// Everything the file drew is on the canvas: three nodes and two arcs. The
// reference place is not an element of the net, so it is not drawn.
check(result.drawn.length === 5, `the canvas holds ${result.drawn.length} elements, not the five of the net`);
for (const id of ['raw', 'mill', 'done', 'a1', 'a2']) {
  check(result.drawn.includes(id), `${id} did not reach the canvas`);
}

// The pages, nested as they were, with their names.
has(/<page id="overview">/, 'the page overview');
has(/<page id="detail">/, 'the page detail');
has(/<name><text>Overview<\/text><\/name>/, "the overview page's name");
has(/<name><text>Detail<\/text><\/name>/, "the detail page's name");
// Who sits on which page. The order inside a page is not meaningful in PNML,
// so it is deliberately not asserted; the page is.
const on = (id) => result.pageOf[id];
check(on('detail') === 'overview', `the detail page sits on '${on('detail')}' rather than inside overview`);
for (const id of ['raw', 'mill', 'a1', 'a2']) {
  check(on(id) === 'overview', `${id} sits on '${on(id)}' rather than on overview`);
}
check(on('done') === 'overview/detail', `the place of the detail page sits on '${on('done')}'`);

// The reference node, and the arc that reaches the other page through it.
has(/<referencePlace id="done_ref" ref="done">/, 'the reference place');
has(/<name><text>Done, on the detail page<\/text><\/name>/, "the reference place's name");
has(/<arc id="a2" source="mill" target="done_ref"/, 'the arc through the reference');
check(on('done_ref') === 'overview', `the reference place sits on '${on('done_ref')}'`);

// The other tool's sections, on the net and on the place, unchanged.
has(/<toolspecific tool="WoPeD" version="1.0"><simulation runs="20"\/><\/toolspecific>/,
  "the net's tool-specific section");
has(/<toolspecific tool="WoPeD" version="1.0"><time unit="s">5<\/time><\/toolspecific>/,
  "the place's tool-specific section");

// And the net itself.
has(/<net id="plant"/, 'the net id');
has(/<initialMarking><text>1<\/text><\/initialMarking>/, 'the marking');
check(result.findings > 0, 'the net could not be analysed after the import');

check(result.once === result.twice, 'a second trip through the modeler changed the file again');

if (pageErrors.length) problems.push('page errors: ' + pageErrors.join('; '));

if (problems.length) {
  console.log(`FAIL: ${problems.join('; ')}\n      the export is in ${out}`);
  process.exitCode = 1;
} else {
  console.log('OK: pages, reference nodes and another tool\'s sections survive the modeler');
}
