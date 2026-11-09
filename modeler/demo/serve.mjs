/**
 * Builds the demo page and serves it, so that the modeler can be tried without
 * anything else in this repository.
 *
 *   node demo/serve.mjs [port]
 *
 * The bundle is not minified, for the same reason the consumers of this package
 * do not minify it: diagram-js resolves services through didi, which falls back
 * to constructor parameter names wherever a component omits $inject, and a
 * minifier renames those.
 */
import * as esbuild from 'esbuild';
import { cp } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(here, '..', '.demo');
const port = Number(process.argv[2] ?? 5180);

await esbuild.build({
  entryPoints: [resolve(here, 'main.js')],
  outfile: resolve(out, 'demo.js'),
  bundle: true,
  format: 'esm',
  target: 'es2022',
  minify: false,
  logLevel: 'info',
});

await esbuild.build({
  entryPoints: [resolve(here, '..', 'assets', 'pn-js.css')],
  outfile: resolve(out, 'demo.css'),
  bundle: true,
  loader: { '.woff': 'dataurl', '.woff2': 'dataurl', '.ttf': 'dataurl', '.eot': 'dataurl', '.svg': 'dataurl', '.png': 'dataurl' },
  logLevel: 'info',
});

await cp(resolve(here, 'index.html'), resolve(out, 'index.html'));

const context = await esbuild.context({ outdir: out });
const served = await context.serve({ servedir: out, host: '127.0.0.1', port });
// esbuild answers with the addresses it listens on, not with a single host.
console.log(`demo on http://${served.hosts?.[0] ?? '127.0.0.1'}:${served.port}`);
