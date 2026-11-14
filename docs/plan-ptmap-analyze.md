# Plan: `ptmap analyze`

Settles the open question in `simulation-and-analysis-design.md` section 8 and the
backlog entry "`ptmap analyze`". Status: planned, not built. Line numbers are
from 2026-11-14.

**Goal:** `ptmap analyze <file>` reads an AML or PNML file. It reports the
structural findings PT01 to PT11 and the behavioural findings PT12 to PT21, as
text or JSON, and exits with a code a CI job can act on.

**Decision:** ptmap runs the JavaScript core of PetriNet.js in a Node process
(option b). There is one implementation of the analysis. The runner sits behind
an interface, so an embedded engine (option c) can replace it if ptmap is ever
shipped as a binary.

## 1. What exists

ptmap:

- `dotnet/PtMapper.Tool/Program.cs:29-202` dispatches commands by position and
  parses no options. Exit codes: 1 usage, 2 missing input, 3 any exception, 4 a
  failed check.
- `validate` (`:127-148`) exits 4 on errors only. Read warnings go to stderr.
- `PtMapper.Tool.csproj` is a plain `Exe` for net8.0, no `PackAsTool`, no publish
  settings. It references only `PtMapper.Conversion` and builds without Node.
- ptmap is not distributed. CI releases only the plugin nupkg; the README runs it
  with `dotnet run --project dotnet/PtMapper.Tool --`. Building the repository
  already needs Node 20, and CI installs it.

C# findings: `PtSeverity` has `Error` and `Warning` only. The JSON shape in use
is `rule`, `severity` (lower case), `message`, `element` (`/api/validate`, the
bridge message). `CaexToPtNet.FindElement` maps a net element id back to its
InternalElement.

JS core (`PetriNet.js/src/net/`, about 1000 lines):

- `analyse.js` returns `{ findings, complete, result }`. A finding is `{ rule,
  severity, element, message }`, with extra `marking` (PT15), `cover` (PT18 to
  PT21), `firings` (PT19). Severities: `warning` and `information`.
- `model.js` `readNet` throws on duplicate ids, bad weights or markings and
  non-bipartite arcs.
- `explore.js` limits: 20000 markings and 2000 ms. The time limit makes answers
  depend on machine speed; `milliseconds: Infinity` disables it.
- `index.js` exports nothing from the core. `package.json` has no `bin` and no
  `exports` map. `src/pnml` needs `DOMParser`, which Node lacks.

Measured on Node 20.19: 16384 markings take 5.2 s, 1024 take 0.26 s. The quality
gate example and the MPS500 net each give exactly one PT15 warning.

Versions: AMLPetriNet `main` pins `petrinetjs#v0.1.0`; the branch
`findings-in-panel` uses `file:../../PetriNet.js` until v0.2.0 is pushed (B6).

## 2. The four ways

| | (a) Port to C# | (b) Node process | (c) Jint in process | (d) `petrinetjs analyze` |
|---|---|---|---|---|
| Effort | 800-1000 lines C#, parity harness | about 300 lines C#, 60 JS | (b) plus engine wiring, speed spike | CLI, PNML reader for Node, PT01-PT11 in JS |
| AML input | yes | yes | yes | no |
| Needs at run time | .NET | .NET, Node 20 | .NET | Node 20 |
| Implementations of the analysis | two | one | one | one (PT01-PT11 twice) |
| Same answers as the panel | as far as tests reach | same engine family (V8) | other engine, to check | V8 |
| New dependency | none | none | Jint (BSD-2-Clause) | a DOM for Node |

- (a) needs exact parity where C# differs from JS: map insertion order, ordinal
  sort, the marking key built with `JSON.stringify`, Farkas in doubles. The parity
  test needs JS answers, so CI needs Node anyway, and every new analysis lands
  twice. Rejected.
- (b) ptmap turns each net into plain data and starts `node` once per file on a
  bundled script, JSON in and out. The core is pinned by the same lock file as the
  canvas.
- (c) runs the same bundle in process. Costs: a new dependency and notice, an
  interpreter that would be much slower on the 5.2 s case, a second engine to
  check against V8. Its gain has no user today. Kept as the replacement runner.
- (d) cannot read AML and needs a DOM. Does not meet the requirement; cheap later.

## 3. Recommendation

(b), on three conditions:

1. PetriNet.js gets a public, JSON-safe entry for the core, with a new tag. ptmap
   imports no internal paths.
2. The CLI runs without a time limit by default, so one file gives one answer on
   any machine. The markings limit of 20000 stays.
3. The runner sits behind `IAnalysisRunner`.

## 4. Behaviour

```
ptmap analyze <in.pnml|in.aml> [hierarchy] [--format text|json]
              [--fail-on error|warning|never] [--markings N] [--time-limit ms] [--timeout s]
```

- All nets of the file are analysed, as `validate` does; `[hierarchy]` picks one.
- `PtValidator.Validate` runs first. A net with a structural error is not
  analysed; the report says why in `problem`.
- Read warnings go to stderr as `note:`. They are not findings.
- `PtSeverity` stays unchanged; the command works with three strings.
- Defaults: `--format text`, `--fail-on warning`, `--markings 20000`, no time
  limit, `--timeout 120`.

Finding format: `rule` (PT01 to PT21), `severity` (`error`, `warning`,
`information`), `element` (the net element id, or null), `message` (unchanged),
and `marking`, `cover`, `firings` where the core gives them. Order as in the
panel: errors, warnings, information; within a severity computed before host.

Text:

```
Quality gate (quality_gate): 0 errors, 1 warning, 11 information; complete, 25 markings
  WARNING      PT15  -          No transition is enabled in the marking (Scrapped 1, Shipped 3).
  INFORMATION  PT13  p_waiting  Waiting holds at most 2 tokens.
```

JSON goes to stdout only, notes to stderr:

```json
{ "file": "examples/quality-gate.pnml", "core": "petrinetjs 0.3.0",
  "limits": { "markings": 20000, "milliseconds": null },
  "nets": [ { "id": "quality_gate", "name": "Quality gate", "hierarchy": null,
              "analysed": true, "problem": null, "complete": true, "stopped": null,
              "markings": 25, "unbounded": [], "findings": [] } ],
  "counts": { "error": 0, "warning": 1, "information": 11 }, "exit": 5 }
```

Exit codes (0 to 4 keep their meaning):

| Code | Meaning |
|---|---|
| 0 | nothing at or above `--fail-on` |
| 1 | usage |
| 2 | no file, no net, no such hierarchy; Node missing or older than 20; bundle missing |
| 3 | unexpected failure, or `--timeout` reached (the Node process is killed) |
| 4 | at least one error |
| 5 | at least one warning, no error |

With `--fail-on error`, 5 becomes 0. With `--fail-on never`, only 1 to 3 remain.

Protocol between ptmap and the script: one process per file, UTF-8 both ways, set
explicitly.

```
in:  { "limits": { "markings": 20000, "milliseconds": null },
       "nets": [ { "key": "0", "places": [{ "id", "name", "marking" }],
                   "transitions": [{ "id", "name" }],
                   "arcs": [{ "id", "source", "target", "weight" }] } ] }
out: { "core": "0.3.0", "results": [ { "key": "0", "problem", "complete", "stopped",
                                        "markings", "unbounded", "findings" } ] }
```

A `null` time limit means no limit; the script maps it to `Infinity`.

## 5. Stages

Each stage leaves ptmap working. `analyze` exists from stage 3. New core
functions go to PetriNet.js with a tag; AMLPetriNet follows. The stdin glue stays
in AMLPetriNet, because the protocol belongs to ptmap.

1. **PetriNet.js v0.3.0, a public entry for the core.** Precondition: v0.2.0 is
   pushed and tagged (B6). `net.js` at the package root re-exports `readNet`,
   `analyse`, `RULES`, `OMEGA`, `findingsToCsv` and a new `analyseData(data,
   limits)` (new `src/net/plain.js`): JSON-safe output (no `Map`, no `Infinity`), a
   net that cannot be read gives `problem` and no throw, `milliseconds: null`
   means no limit. `package.json` `files` gains `net.js`; no `exports` map, it
   would cut off deep imports. CHANGELOG, README section, tag. Proof: `cd
   PetriNet.js && npm test`.
2. **AMLPetriNet, the analysis bundle.** `web/package.json` pins `#v0.3.0`, lock
   renewed. `web/src/analysis.js` reads stdin, calls `analyseData` per net, writes
   the response, imports only `petrinetjs/net.js`. `web/build.mjs` gets a third
   build (`platform: 'node'`, ESM, `dist/ptmap-analysis.mjs`, version injected).
   `web/tools/verify-analysis.mjs` joins `npm test`. Proof: `cd web && npm ci &&
   npm run build && node tools/verify-analysis.mjs`; the quality gate gives one
   PT15 and the JSON has no `Infinity`.
3. **`ptmap analyze` with text output.** New files in
   `dotnet/PtMapper.Tool/Analysis/`: `AnalysisRequest.cs`, `IAnalysisRunner.cs`,
   `NodeAnalysisRunner.cs` (finds `node`, checks the version, kills on timeout),
   `AnalyzeCommand.cs`, `FindingsText.cs`. The csproj copies the bundle next to
   `ptmap.dll` if it exists; a missing bundle does not stop the build, `analyze`
   then exits 2 and names `npm run build`. `Program.cs` gets `case "analyze"`.
   Proof: `dotnet run --project dotnet/PtMapper.Tool -- analyze
   examples/quality-gate.pnml; echo $?` prints the PT15 line, then `5`.
4. **JSON output and tests in CI.** `--format json`. `PtMapper.Tests` references
   the Tool project; new `AnalyzeTests.cs`. CI sets `PTMAP_REQUIRE_NODE=1` so that
   tests needing Node fail instead of being skipped. Proof: `dotnet test
   dotnet/PtMapper.Tests --filter Analyze`; without Node the same tests skip.
5. **Documents.** README command table, design section 8 marked settled, backlog
   ticked. Proof: CI green; `ptmap` without arguments lists `analyze`.

## 6. Tests

- PetriNet.js (`node --test`): `analyseData` on every fixture survives a JSON
  round trip and equals `analyse`; the producer net has `unbounded: [stock]` and
  no `Infinity`; a duplicate id gives `problem`, no throw; `milliseconds: null`
  with a jumping clock still completes.
- AMLPetriNet, Node: `verify-analysis.mjs` sends the quality gate and the six
  interop fixtures through the bundle; names with `&`, `<`, quotes and umlauts
  come back unchanged.
- C#, pure: `AnalysisRequest` (ids, names with null as `""`, markings, weights,
  parallel arcs stay two); the exit code for every combination of counts and
  `--fail-on`; the order of findings; unknown option and `--markings 0` give 1.
- C#, end to end (SkippableFact, needs Node): quality gate exit 5 with one PT15
  carrying `marking` and eleven information findings; the MPS500 AML view and the
  MPS500 PNML give the same findings with the same ids; a live loop gives 0; a
  non-bipartite arc gives 4, PT03, `analysed: false`; a producer net with
  `--markings 50` gives PT12, PT17 and `complete: false`; `--timeout 1` on a large
  net gives 3 and leaves no Node process; a missing bundle gives 2 with the
  instruction; the JSON carries every field of section 4.

## 7. Risks

- Node missing or old: checked before the run, exit 2 with a sentence. Only
  `analyze` needs Node.
- Time-dependent answers: avoided by default; with `--time-limit` the output says
  `stopped: "time"`.
- Long runs: Farkas has no cap (`invariants.js:41-73`); `--timeout` kills the
  process. A cap would be a separate PetriNet.js change.
- Drift between ptmap and the canvas: both come from one `web/package-lock.json`;
  the JSON names the core version.
- Encoding on Windows: UTF-8 without BOM, covered by the markup fixture.
- Exit codes differ from `validate` (0 on warnings versus 5 here); the README says
  so.
- An empty net is reported as a deadlock, so `examples/interop/empty.pnml` exits 5
  (question 6).
- AML ids: findings carry the reader's normalised ids, not InternalElement IDs
  (question 5).
- Later switch to (c): measure Jint on the 16384-marking net first; run the
  end-to-end tests against both runners.

## 8a. Decided (2026-11-14)

The author left the questions below open and asked for the recommended answers:
1. A warning fails the run by default (exit 5); `--fail-on error` turns it off.
2. All warnings count alike, PT05, PT08, PT09 and PT10 included.
3. A partial result (PT17) fails only with `--require-complete`.
4. No time limit in ptmap, so the answer is reproducible; the panel keeps its limit.
5. For AML input a finding also carries the InternalElement ID.
6. An empty net, or one without transitions, reports no deadlock.
7. The text output only counts information findings; `--all` lists them.
8. `petrinetjs analyze` stays open.

Details settled while building: `--fail-on never` gives 0 whatever was found.
`--require-complete` fails with 5 on a partial result, which includes a net with
an unbounded place (`complete` is that of the core's `analyse()`), unless
`--fail-on never`. A net that could be analysed and got no result is a failure
(3), not an empty report. The deadlock rule for nets without transitions lives
in the core (PetriNet.js 0.3.0), so the canvas agrees.

## 8. Open questions for the author

1. Should a warning fail the run by default (exit 5), or only an error, as in
   `validate`?
2. Do the structural warnings PT05, PT08, PT09 and PT10 count toward the exit code?
3. Should a partial result (PT17) fail a CI run, for example with
   `--require-complete`?
4. ptmap runs without a time limit; the panel stops after 2 s. Is it acceptable
   that a large net gets a complete answer in ptmap and a partial one in the
   panel?
5. For AML input, should a finding also carry the InternalElement ID?
6. An empty net is reported as a deadlock. Keep that, or say nothing about
   deadlocks for a net without transitions?
7. Should the text output list information findings by default, or only count
   them, with `--all` to list them?
8. Is `petrinetjs analyze` (PNML only, option d) wanted later?
