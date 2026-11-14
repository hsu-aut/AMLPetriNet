/**
 * The analysis as a command line process, for ptmap.
 *
 * Reads one JSON request on stdin and writes one JSON answer on stdout, both
 * UTF-8. ptmap turns each net of a file into plain data and starts this once per
 * file; the answer for each net is the one the canvas would give, from the same
 * core. Nothing else in this file knows about nets.
 *
 *   in:  { limits: { markings, milliseconds },
 *          nets: [{ key, places: [{ id, name, marking }], transitions: [{ id, name }],
 *                   arcs: [{ id, source, target, weight }] }] }
 *   out: { core, results: [{ key, problem, complete, stopped, markings, unbounded, findings }] }
 *
 * A `milliseconds` of null means no time limit.
 */
import { analyseData } from 'petrinetjs/net.js';

/** Injected by the build from the installed package. */
const CORE_VERSION = typeof __CORE_VERSION__ === 'undefined' ? 'unknown' : __CORE_VERSION__;

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString('utf8').replace(/^﻿/, '');
}

export function answer(request) {
  const limits = {};
  if (request?.limits && 'markings' in request.limits) limits.markings = request.limits.markings;
  if (request?.limits && 'milliseconds' in request.limits) limits.milliseconds = request.limits.milliseconds;

  return {
    core: CORE_VERSION,
    results: (request?.nets ?? []).map((net) => ({ key: net.key, ...analyseData(net, limits) })),
  };
}

const text = await readStdin();
let request;
try {
  request = JSON.parse(text);
} catch (error) {
  process.stderr.write(`ptmap-analysis: the request is not JSON: ${error.message}\n`);
  process.exit(2);
}

process.stdout.write(JSON.stringify(answer(request)));
