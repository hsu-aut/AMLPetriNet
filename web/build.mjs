/**
 * Bundles the Petri net modeler plus the AML bridge into a single ESM file and
 * a single stylesheet that the WebView2 host loads from a virtual host mapping.
 * Everything goes into one file, with no async chunks and nothing fetched from
 * a CDN at runtime, so the host can serve the bundle offline from disk.
 */
import * as esbuild from 'esbuild';
import { cp, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dist = resolve(here, 'dist');
// The modeler is PetriNet.js (github.com/hsu-aut/PetriNet.js), installed from a
// tag like any other dependency; its stylesheet is bundled from there.
const pkg = resolve(here, 'node_modules', 'petrinetjs');

await rm(dist, { recursive: true, force: true });
await mkdir(dist, { recursive: true });

const shared = {
  bundle: true,
  target: 'es2022',
  logLevel: 'info',
  legalComments: 'eof',
  // Minification is OFF on purpose and must stay off.
  //
  // diagram-js resolves dependencies through didi, which falls back to reading
  // constructor *parameter names* whenever a component omits $inject. A
  // minifier renames those parameters and the injector dies at boot.
  //
  // Two separate failures were observed, and only the first is understood:
  //   1. The Petri net package's UpdatePropertyHandler declared no $inject
  //      (its sibling UpdateLabelHandler did). Fixed in the original and in
  //      PetriNet.js.
  //   2. With that patched, a minified build still dies with
  //      'No provider for "l"! (Resolving: keyboardMove -> distributeElements
  //      -> alignElements -> l)'. The cause was not tracked down: the $inject
  //      annotations do survive minification (all 118 of them), and every
  //      class registered as a type carries one, so the earlier guess that
  //      dead-code elimination strips them is wrong.
  //
  // The bundle is loaded from disk by the WebView2 host, so its size does not
  // matter; correctness does. Anyone turning this on must re-run
  // `npm run test:bridge`, which reproduces the failure in seconds.
  minify: false,
  sourcemap: process.env.PTN_DEBUG === '1' ? 'inline' : false,
};

await esbuild.build({
  ...shared,
  entryPoints: [resolve(here, 'src/index.js')],
  format: 'esm',
  splitting: false,
  outfile: resolve(dist, 'ptnjs.esm.js'),
});

// pn-js.css @imports diagram-js, bpmn-font and the modeler's own icon font.
// Bundling resolves all of it into one stylesheet with the fonts inlined, so
// the host needs a single <link> and no extra virtual host mappings.
await esbuild.build({
  ...shared,
  entryPoints: [resolve(pkg, 'assets/pn-js.css')],
  outfile: resolve(dist, 'ptnjs.css'),
  loader: {
    '.woff': 'dataurl',
    '.woff2': 'dataurl',
    '.ttf': 'dataurl',
    '.eot': 'dataurl',
    '.svg': 'dataurl',
    '.png': 'dataurl',
  },
});

await cp(resolve(here, 'index.html'), resolve(dist, 'index.html'));
console.log('dist/ ready');
