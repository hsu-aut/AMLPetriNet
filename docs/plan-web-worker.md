# Plan: the search in a Web Worker

Backlog: "Search in a Web Worker". Design: `simulation-and-analysis-design.md`,
D-5. Status: planned, not built. Line numbers are from 2026-11-14, PetriNet.js
0.3.0.

## 1. What exists

Every caller of the search runs synchronously on the page thread:

| Caller | Work | Cost |
|---|---|---|
| `Findings.analyse()` (Findings.js 92-110), from the Analyse button and the bridge (bridge.js 246-251) | `analyse()`: explore, liveness, invariants, siphons, traps (analyse.js 39-186) | explore up to 20000 markings or 2000 ms (explore.js 19-24); Farkas (invariants.js 41-73) has no limit |
| `FindingsPanel._addActions` (199-233), on every `findings.changed` | "Way there": one BFS per PT15 finding; "Play": a DFS of up to 20000 steps per PT19 finding | deadlocks x edges |
| `ReachabilityView.show()` (137-152) | `explore()` | as above |
| `ReachabilityView.ask()` (165-194), the query line | `show()` first, then `wayTo`: one BFS per matching marking (paths.js 183-197) | matches x edges, the worst case |
| `_goTo()`, `save()` | one `pathTo`; DOT or CSV over the whole result | linear |
| Simulation `loadRun` | replays a given sequence | linear, no search |
| `ptmap analyze` (web/src/analysis.js) | the same analysis in Node | not a page; stays as it is |

Hosting and bundling:

- PetriNet.js ships sources without a build step; each host bundles them
  (web/build.mjs, test/browser/serve.mjs, demo/serve.mjs). build.mjs writes one ESM
  file with `splitting: false` and keeps minification off because of didi.
- Plugin: the page loads from `https://ptnjs.local` (PtWebView.cs). All of
  `dist/` is packed. There is no CSP: no meta tag and no header.
- Web app: Program.cs sets only `X-Content-Type-Options`; the csproj copies exactly
  `ptnjs.esm.js` and `ptnjs.css`. The headers of the proxy are not known.
- PtWebView.cs reloads the page on `RenderProcessUnresponsive`. A long synchronous
  analysis can trigger this and lose the page state.
- Findings are JSON-safe already. The state space is not: markings are `Map`s with
  `Infinity` for omega; keys write omega as `'omega'` (model.js 205-219).

## 2. Decisions

**W-1 What moves.** `analyse()` and `explore()` run in the worker. The worker also
computes the `way` of each PT15 finding and the `run` of each PT19 finding, so the
panel needs no state space; `findings.wayTo()` and `runFor()` read these fields and
stay synchronous. For the state space view the worker returns the space in compact
form. Drawing (up to 300 nodes), clicks, queries and saving stay on the page. The
simulation stays on the page.

**W-2 One BFS instead of many.** `paths.js` gets `shortestWays(result)`: one
breadth-first walk from the initial marking, cached per result. `pathTo`,
`pathToDeadlock` and `wayTo` use it. The answers do not change, because the order of
discovery is the same. The cost drops from matches x edges to edges. This fixes the
query line with or without a worker.

**W-3 An inline worker from a Blob, built into the package.**
`scripts/build-worker.mjs` bundles `src/search/worker.js` with esbuild (IIFE,
classic worker) into a string and writes `src/search/workerSource.js`
(`export default "..."`), which is committed. A test rebuilds the file and compares.
At run time: `new Worker(URL.createObjectURL(new Blob([source], { type:
'text/javascript' })))`. The bundle stays one file, hosts change no build step, and
nothing is fetched through the virtual host. About 50 KB, not minified.
Rejected: a separate `ptnjs.worker.js` (a second file, another copy step in the web
app, untested under the WebView2 virtual host, esbuild does not handle
`new URL(..., import.meta.url)`), and the bundle loading itself as a module worker
(it would pull diagram-js into the worker).

**W-4 CSP.** No host sets one today, so a Blob worker is allowed. A host whose
`worker-src` or `script-src` lacks `blob:` gets the fallback (W-6). The README states
`worker-src blob:` as the requirement.

**W-5 Stopping by termination.** A worker in the middle of a search cannot read
messages, so stopping means `worker.terminate()`; the next request starts a fresh
worker. One worker per kind (`analyse`, `explore`), kept between requests. A search
is stopped by `commandStack.changed`, `import.done`, `diagram.clear`, by the Stop
button and by `diagram.destroy` (which also revokes the Blob URL). Every message
carries a request id; answers with an old id are dropped.

**W-6 Fallback.** The same `runSearch()` runs on the page when the config says
`search: { worker: false }`; when `Worker`, `Blob` or `URL.createObjectURL` is
missing (Node, `node --test`); when the constructor throws; or when an `error` event
arrives before the worker's `ready` message. One `console.warn`, which the bridge
forwards to the host log. It stays inline for the life of the modeler. A
`limits.now` clock cannot be cloned, so such a request also runs inline. Before an
inline run the service waits one timer tick so the busy row is drawn first.

**W-7 Limits unchanged.** 20000 markings, 2000 ms and the structure limits stay.
Answers stay identical to 0.3.0.

**W-8 Joining.** If `analyse()` is called while an analysis of the same net with the
same limits runs, it returns the running promise.

## 3. Messages

Page to worker: `{ type: 'analyse' | 'explore', id, net, limits }`. `net` is the
output of `readNet()`, read on the page first so an unreadable net is answered at
once. `limits` has `markings`, `milliseconds` (a number, or `null` for none) and
`structure`.

Worker to page:

| type | fields |
|---|---|
| `ready` | once, when the worker has loaded |
| `progress` | `id, phase: 'explore' or 'structure', markings, edges`, at most every 100 ms |
| `result` | `id, answer` |
| `error` | `id, message`: a defect in the core, not an unreadable net |

`answer` for `analyse`: `{ complete, stopped, markings, unbounded, findings }`, each
finding as today plus `way: string[] | null` and `run: string[] | null`. `answer` for
`explore`: `{ keys, initial, edges, deadlocks, fired, unbounded, complete, stopped }`
with `edges` as flat triples of indices (from, to, transition).

Everything is JSON-safe: no Map, Set, Infinity or function; markings travel as keys
(omega as `'omega'`); indices avoid copying long key strings per edge. On the page,
`fromWire()` rebuilds the shape that layout.js, paths.js and report.js read.

Progress needs one hook in the core: `limits.progress`, used only where the search
runs. explore calls it every 500 new markings and once at the end; analyse calls it
before the structural part. `analyseData` and ptmap do not change.

## 4. What the user sees

- **Analyse:** the panel opens at once with the row "Analysing: 12 345 markings so
  far, at most 20 000" and a Stop button, then the findings as today. The canvas can
  be panned, zoomed, selected and edited meanwhile. The Analyse button reads
  "Analysing..." and is disabled.
- **Edit or import during a run:** the run is stopped and the stale note appears.
- **Stop:** "The analysis was stopped. Analyse again." No partial list.
- **State space:** the view opens with "Searching: n markings" and a Stop button. A
  query typed during the search is answered when it ends.
- **Simulation:** a run keeps going during an analysis.
- **Without a worker:** as in 0.3.0, except that the busy row is drawn before the
  freeze. The busy row has its own class, `ptn-findings-busy`.

## 5. API changes and who depends on them

PetriNet.js, version **0.4.0** (public methods change their return types, which
breaks hosts; under 0.x that is a new minor version, with a "Breaking" line in the
CHANGELOG):

- New service `search` (`src/search/`): `run(kind, net, limits)` returns a promise;
  also `cancel(kind)`, `idle()`, `mode` and the event `search.progress`.
- `findings.analyse(limits)` returns `Promise<Finding[] | null>` (null when stopped
  or cancelled). New `findings.busy`; `findings.changed` carries `busy`.
- `findingsPanel.analyse()` returns a promise. `reachabilityView.show(limits)`
  returns `Promise<state | null>`, `ask(text)` returns `Promise<string>`. New
  `reachabilityView.busy`.
- Unchanged: `wayTo`, `runFor`, `save`, `state`, `said`, `setHostFindings`, net.js,
  `analyseData`. net.js must not import `src/search/` (entry.test.js checks this).

Dependents: the PetriNet.js tests and browser checks (verify-findings, -states,
-paths, -invariants, -export, -pages) await the promise or wait for `search.idle()`.
AMLPetriNet `web/src/bridge.js` 246-251 opens the panel, then awaits
`findings.analyse()`; a `null` result maps to the problem "The net changed while it
was analysed. Analyse again." (in hostMessages.js, with a node test); each request
still gets exactly one `analysed`. verify-bridge.mjs replaces fixed waits by
waiting for `analysed`. The plugin needs no protocol change; a stopped run arrives
as "Cannot analyse: ...". The web app's page, verify-webapp.mjs, ptmap and
dist-node do not change.

## 6. Tests

node --test (add `src/search/` to the test script): `fromWire(toWire(r))` equals
`explore()` on all fixtures and survives a JSON round trip, omega included; the
findings of `runSearch` equal those of `analyse()`, `way` equals `pathTo`, `run`
equals `sequenceFor`; `shortestWays` equals the old per-key BFS, also on a search
cut off at its limit; `progress` is called, ends with the final count and changes no
result; the Search service with a fake Worker class (result, progress, error, cancel,
joining, and every fallback); workerSource.js equals a fresh build.

Browser, new `verify-search.mjs`: `page.on('worker')` sees a worker and `search.mode`
is `'worker'`; on a net with more than 20000 markings a 20 ms page timer never
misses more than 250 ms during the search, and the same check with
`search: { worker: false }` misses more than 1000 ms (so the check measures
something); worker and inline give the same findings and state space numbers; an
edit or Stop during the search resolves to `null`, shows the stale note and causes
no page error; `page.route` adds `Content-Security-Policy: worker-src 'none'` and the
result is inline mode with the same findings.

AMLPetriNet: a case in hostMessages.test.js for the stop text; in verify-bridge.mjs
an import during `analyse` gives exactly one `analysed` with a problem, and
`page.on('worker')` fires in the built bundle.

By hand: in the AML Editor, analyse a large net from the menu; the canvas must pan
while it runs (tests the Blob worker under the virtual host). `curl -sI
https://aml.fpbjs.net/pt/` shows no CSP that blocks `blob:`.

## 7. Stages

Stages 1 to 3 are in PetriNet.js, on a branch, and end with a tag; AMLPetriNet follows.

1. **Core.** The `progress` hook, `shortestWays`, `src/search/run.js` and `wire.js`.
   No UI change. Proof: `npm test`.
2. **Worker and service.** `worker.js`, `scripts/build-worker.mjs`, `workerSource.js`,
   `Search.js`, module wiring. Panels still call the core directly. Proof:
   `npm test && node test/browser/run.mjs search`.
3. **Async services.** Busy rows, Stop, README, CHANGELOG, version 0.4.0. Proof:
   `npm test && node test/browser/run.mjs`. The maintainer tags v0.4.0.
4. **AMLPetriNet.** Pin `#v0.4.0`, renew the lock, bridge, checks, status line.
   Proof: `cd web && npm ci && npm run build && npm test`, then `dotnet test
   Aml.Editor.Plugin.PetriNet.Tests -c Release`.
5. **Docs and release.** Note on D-5, backlog ticked, the risk in
   `plan-plugin-menu.md` closed, a check in the editor, the web app redeployed.

## 8. Risks

- Stage 4 waits for the tag; CI cannot install a `file:` dependency.
- workerSource.js can be forgotten when the worker changes; the freshness test fails
  then. An esbuild update changes the file as well.
- Blob workers under a WebView2 virtual host are untested; the manual check is in
  stage 5, and the fallback keeps today's behaviour if they fail.
- A CSP on the proxy would quietly cause the fallback; the curl check covers it.
- The worker shares the renderer process, so running out of memory takes the page
  down; the markings limit stays for this reason.
- Stopping by termination loses the partial state space.
- `fromWire` and the exports of 20000 markings still run on the page (tens of
  milliseconds; verify-search measures it).
- Browser checks with fixed waits become flaky; they must wait for events.
- Analyse and State space explore the same net twice, as today; a shared cache is
  left for later.

## 8a. Decided (2026-11-14)

The author left the questions below open and asked for the recommended answers:
1. Findings list and state space have a Stop button while they run.
2. Stop and edits drop the partial result; no partial list.
3. The 2 s limit stays in 0.4.0; raising it is decided separately.
4. Progress is "12 345 markings so far, at most 20 000", without a percentage bar.
5. The plugin's status line says "Analysing the net..." right after the menu click.
6. If no worker can be started: fall back with a line in the host log only.
7. Analyse is disabled while it runs; State space stays usable in parallel.

## 9. Open questions for the author

1. Should the findings list and the state space have a Stop button while they run?
   Recommended: yes.
2. Stop and edits drop the partial result. Should Stop show what was found so far,
   marked partial (PT17)? Recommended: no, partial lists are easy to misread.
3. The page no longer freezes. Keep the 2 s limit, or raise it? Recommended: keep it
   in 0.4.0 so that answers stay the same.
4. Show progress without a percentage bar, since the total is unknown? Recommended:
   yes.
5. Should the status line say "Analysing the net..." after the menu click?
   Recommended: yes.
6. If no worker can be started: fall back with a host log line, or also a note in
   the panel? Recommended: log line only.
7. Disable Analyse while it runs, but keep State space usable in parallel?
   Recommended: yes.
