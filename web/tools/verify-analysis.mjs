/**
 * The analysis bundle that ptmap starts, driven the way ptmap drives it: one
 * JSON request on stdin, one JSON answer on stdout.
 *
 * The C# side is tested in dotnet/PtMapper.Tests; this checks the other half of
 * the wire without a browser or .NET, so that a change to the protocol or to the
 * core that breaks it is found here first.
 */
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const bundle = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'dist-node', 'ptmap-analysis.mjs');
const failures = [];
const check = (ok, what) => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}`);
  if (!ok) failures.push(what);
};

if (!existsSync(bundle)) {
  console.log('FAIL  the analysis bundle is missing; run npm run build');
  process.exit(1);
}

function run(input, raw = false) {
  const result = spawnSync('node', [bundle], {
    input: raw ? input : JSON.stringify(input),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  return { ...result, json: result.status === 0 ? JSON.parse(result.stdout) : null };
}

const idle = (name = 'Idle') => ({
  key: 'n',
  places: [{ id: 'p', name, marking: 1 }],
  transitions: [{ id: 't', name: 'Go' }],
  arcs: [{ id: 'a', source: 'p', target: 't' }],
});

// A net that deadlocks after one step.
let { json, status } = run({ limits: { markings: 20000, milliseconds: null }, nets: [idle()] });
check(status === 0, 'a request is answered');
check(/^\d+\.\d+\.\d+/.test(json?.core ?? ''), `the answer names the core version (${json?.core})`);
check(json?.results?.length === 1 && json.results[0].key === 'n', 'each net is answered under its key');
check(json?.results?.[0]?.problem === null && json.results[0].complete === true, 'the net was analysed completely');
check(json?.results?.[0]?.findings?.some((f) => f.rule === 'PT15' && typeof f.marking === 'string'),
  'the deadlock is found and carries its marking');

// Names with markup characters and umlauts come back as they went in.
const awkward = 'Prüf <station> & "co" äß';
({ json } = run({ nets: [idle(awkward)] }));
const text = JSON.stringify(json);
check(json?.results?.[0]?.findings?.some((f) => f.message.includes(awkward)),
  'a name with markup characters and umlauts is returned unchanged');
check(!text.includes('\ufffd') && !text.includes('�'), 'no replacement characters in the answer');

// A byte order mark in front of the request does not break it.
({ json, status } = run('﻿' + JSON.stringify({ nets: [idle()] }), true));
check(status === 0 && json?.results?.length === 1, 'a request with a byte order mark is read');

// A net the core cannot read gives a reason, not a crash.
({ json } = run({ nets: [{ key: 'bad', places: [{ id: 'x' }], transitions: [{ id: 'x' }], arcs: [] }] }));
check(typeof json?.results?.[0]?.problem === 'string' && json.results[0].findings.length === 0,
  'a net that cannot be read is answered with a reason');

// No Infinity anywhere, even for a net that grows without bound.
const producer = {
  key: 'grow',
  places: [{ id: 'stock', name: 'Stock', marking: 0 }],
  transitions: [{ id: 'make', name: 'Make' }],
  arcs: [{ id: 'a', source: 'make', target: 'stock' }],
};
({ json } = run({ limits: { markings: 20000, milliseconds: null }, nets: [producer] }));
check(json?.results?.[0]?.unbounded?.includes('stock'), 'an unbounded place is named');
check(json?.results?.[0]?.findings?.some((f) => f.rule === 'PT12'), 'PT12 is reported for it');

// The limit on markings is honoured.
const wide = {
  key: 'wide',
  places: Array.from({ length: 12 }, (_, i) => ({ id: `p${i}`, name: `P${i}`, marking: 1 })),
  transitions: Array.from({ length: 12 }, (_, i) => ({ id: `t${i}`, name: `T${i}` })),
  arcs: Array.from({ length: 12 }, (_, i) => ({ id: `a${i}`, source: `p${i}`, target: `t${i}` })),
};
({ json } = run({ limits: { markings: 50, milliseconds: null }, nets: [wide] }));
check(json?.results?.[0]?.complete === false && json.results[0].stopped === 'markings', 'the limit on markings stops the search');
check(json?.results?.[0]?.findings?.some((f) => f.rule === 'PT17'), 'and the partial result is reported');

// What the structure says: the class, a proof marked as such, and the verdict on a workflow net.
const sequence = (marking) => ({
  key: 'wf',
  places: [{ id: 'i', name: 'In', marking }, { id: 'o', name: 'Out', marking: 0 }],
  transitions: [{ id: 't', name: 'Go' }],
  arcs: [{ id: 'a', source: 'i', target: 't' }, { id: 'b', source: 't', target: 'o' }],
});
({ json } = run({ nets: [sequence(1)] }));
check(json?.results?.[0]?.classes?.includes('state-machine'), 'the class of a net comes back');
check(json?.results?.[0]?.workflow?.sound === true && json.results[0].workflow.start === 'i', 'a workflow net is found and found sound');
check(json?.results?.[0]?.findings?.filter((f) => f.proof).every((f) => /^Proven from the structure/.test(f.message)),
  'a proven finding says so in its sentence');
({ json } = run({ nets: [sequence(0)] }));
check(/Started with one token in In/.test(json?.results?.[0]?.findings?.find((f) => f.rule === 'PT25')?.message ?? ''),
  'a workflow net drawn without its token is checked from one and says so');

// Several nets in one request, answered in order.
({ json } = run({ nets: [{ ...idle(), key: 'one' }, { ...idle('Other'), key: 'two' }] }));
check(json?.results?.map((r) => r.key).join() === 'one,two', 'several nets are answered in order');

// Nothing to do, and rubbish.
({ json } = run({ nets: [] }));
check(json?.results?.length === 0, 'a request without nets gives no results');
const rubbish = run('this is not json', true);
check(rubbish.status === 2 && /not JSON/.test(rubbish.stderr), 'a request that is not JSON ends with status 2 and says why');

// A transition that can never fire again carries the marking from which it cannot (PetriNet.js 0.7).
const a = (id, source, target) => ({ id, source, target });
({ json } = run({
  limits: { markings: 20000, milliseconds: null },
  nets: [{
    key: 'loss',
    places: [{ id: 'p', name: 'P', marking: 1 }, { id: 'q', name: 'Q', marking: 0 }, { id: 'z', name: 'Z', marking: 0 }],
    transitions: ['t', 'u', 'x', 'y'].map((id) => ({ id, name: id.toUpperCase() })),
    arcs: [a('1', 'p', 't'), a('2', 't', 'q'), a('3', 'q', 'u'), a('4', 'u', 'p'), a('5', 'q', 'x'), a('6', 'x', 'z'), a('7', 'z', 'y'), a('8', 'y', 'z')],
  }],
}));
const lost = json?.results?.[0]?.findings?.filter((f) => f.rule === 'PT16') ?? [];
check(lost.length >= 2, `a transition that can never fire again is found (${lost.length})`);
check(lost.every((f) => typeof f.marking === 'string' && f.marking.length > 0), 'each carries the marking it is lost in');

// The kind is echoed, the titles of the rules come with the answer, and an unknown kind is refused.
({ json } = run({ nets: [idle()] }));
check(json?.kind === 'analyse', 'a request without a kind is an analysis and says so');
check(json?.rules?.PT15 === 'Deadlock', `the answer names the rules (PT15 is ${JSON.stringify(json?.rules?.PT15)})`);
({ json } = run({ kind: 'analyse', nets: [idle()] }));
check(json?.kind === 'analyse', 'an analysis asked for by name is answered');
const unknown = run({ kind: 'nonsense', nets: [idle()] });
check(unknown.status === 2, `an unknown kind ends with status 2, not ${unknown.status}`);

if (failures.length) {
  console.log(`\n${failures.length} analysis check(s) failed`);
  process.exitCode = 1;
} else {
  console.log('\nall analysis checks passed');
}
