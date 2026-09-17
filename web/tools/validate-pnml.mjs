/**
 * Validates PNML files against the official ISO/IEC 15909-2 grammar for
 * place/transition nets, with Jing, the reference RELAX NG validator.
 *
 * Both PNML converters of this project, the one in the modeler bundle and the
 * one in PtMapper, were written here. Checked only against each other they can
 * agree on something that is not PNML at all, so this checks them against the
 * grammar published at pnml.org instead.
 *
 * Usage: node tools/validate-pnml.mjs <file.pnml>...
 *
 * Needs Java on the PATH. The grammar and the two jars are downloaded once into
 * web/.cache/pnml and reused. Exit code 1 if any file is invalid.
 *
 * Two things about the grammar itself:
 *   - Its absolute include hrefs point at pnml.org, so the local copies are
 *     rewritten to include each other instead.
 *   - It declares the id attribute with conflicting ID types across patterns,
 *     which Jing refuses as a schema error unless ID checking is switched off
 *     with -i. Uniqueness of ids is then not checked by the grammar; PtValidator
 *     rule PT01 checks it.
 */
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve, delimiter } from 'node:path';
import { fileURLToPath } from 'node:url';

const GRAMMAR = 'http://www.pnml.org/version-2009/grammar/';
const GRAMMAR_FILES = ['ptnet.pntd', 'pnmlcoremodel.rng', 'conventions.rng', 'anyElement.rng', 'pnmlextensions.rng'];
const JARS = {
  'jing.jar': 'https://repo1.maven.org/maven2/org/relaxng/jing/20220510/jing-20220510.jar',
  'xercesImpl.jar': 'https://repo1.maven.org/maven2/xerces/xercesImpl/2.12.2/xercesImpl-2.12.2.jar',
};

const files = process.argv.slice(2);
if (files.length === 0) {
  console.error('usage: node tools/validate-pnml.mjs <file.pnml>...');
  process.exit(2);
}

const cache = resolve(dirname(fileURLToPath(import.meta.url)), '..', '.cache', 'pnml');
await mkdir(cache, { recursive: true });

async function fetchOnce(url, target, transform) {
  if (existsSync(target)) return;
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: ${response.status} ${response.statusText}`);
  if (transform) {
    await writeFile(target, transform(await response.text()), 'utf8');
  } else {
    await writeFile(target, Buffer.from(await response.arrayBuffer()));
  }
}

for (const name of GRAMMAR_FILES) {
  await fetchOnce(GRAMMAR + name, join(cache, name),
    (text) => text.replaceAll(`href="${GRAMMAR}`, 'href="'));
}
for (const [name, url] of Object.entries(JARS)) {
  await fetchOnce(url, join(cache, name));
}

const classpath = Object.keys(JARS).map((name) => join(cache, name)).join(delimiter);
let failed = 0;

for (const file of files) {
  const path = resolve(file);
  try {
    execFileSync('java', ['-cp', classpath, 'com.thaiopensource.relaxng.util.Driver', '-i',
      join(cache, 'ptnet.pntd'), path], { stdio: 'pipe' });
    console.log(`valid    ${file}`);
  } catch (error) {
    failed++;
    const output = `${error.stdout ?? ''}${error.stderr ?? ''}`.trim() || String(error.message);
    console.log(`INVALID  ${file}`);
    for (const line of output.split(/\r?\n/).slice(0, 5)) {
      console.log('         ' + line.replace(/^.*?:(\d+):\d+: error: /, 'line $1: '));
    }
  }
}

console.log(failed === 0
  ? `\nall ${files.length} file(s) are valid P/T net PNML`
  : `\n${failed} of ${files.length} file(s) are not valid P/T net PNML`);
process.exitCode = failed === 0 ? 0 : 1;
