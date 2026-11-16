# Plan: `ptmap explore` and machine formats for `ptmap analyze`

Entries 8 and 9 of `ideas.md`. Status: planned, not built. Line numbers are from
2026-11-16.

**Goal.** `ptmap explore <file>` writes the state space as DOT or CSV, the same text the
state space view saves. `ptmap analyze` gains two more formats: SARIF 2.1.0, so that
GitHub shows the findings as code scanning alerts, and JUnit XML for CI dashboards.

## 1. What exists

- `PetriNet.js/src/net/report.js` has the writers `toDot` and `markingsToCsv`. They need
  no DOM. The view saves exactly these, on the result that came back over the wire
  (`ReachabilityView.js`).
- `PetriNet.js/net.js` exports `analyseData`, `analyse`, `RULES`, `readNet`, `OMEGA`,
  `findingsToCsv`, the classes and `soundness`. It does not export `explore`, `toDot` or
  `markingsToCsv`.
- `src/search/run.js` and `wire.js` are the internal message between worker and page.
- `explore.js` limits: 20000 markings, 2000 ms.
- Measured on Node 20: DOT and CSV from the direct result equal those from the JSON wire
  round trip on all fixtures; 16384 markings take 1.2 s to explore and 0.3 s to write;
  DOT is 4.7 MB, CSV 0.65 MB.
- ptmap: options in `AnalyzeOptions.cs` (format `text|json`); output at
  `AnalyzeCommand.cs`; `IAnalysisRunner.Run` takes and returns a string; the bundle has
  no request kind (`web/src/analysis.js`).
- Rule titles: PT01 to PT11 are in a private table (`PtConformanceReport.cs`); PT12 to
  PT25 are in `RULES`, which `net.js` exports and the bundle does not send.
- CI has `permissions: contents: read` and `PTMAP_REQUIRE_NODE=1`.
- GitHub's SARIF support: `physicalLocation.artifactLocation.uri`, `region.startLine` and
  `partialFingerprints` (`primaryLocationLineHash`) are required; each rule needs
  `shortDescription`, `fullDescription` and `help.text`, none empty; logical locations
  are not shown; at most 25000 results per run and 10 MB gzipped.

## 2. Decisions

1. **DOT and CSV come from the core, not from C#.** A copy would be a second writer for
   omega, DOT quoting and the firing distances, and "the same file as the view" would
   hold only as far as tests reach.
2. **PetriNet.js needs a release, not a new writer: 0.7.0** (the numbering follows the
   order of building and may move). `net.js` re-exports `explore`, `toDot` and
   `markingsToCsv`; `src/net/plain.js` gains `exploreData(data, limits, format)`
   returning `{ problem, complete, stopped, markings, edges, deadlocks, unbounded, text }`.
   It works like `analyseData`: `milliseconds: null` means no limit, an unreadable net
   gives `problem`, the answer survives JSON. ptmap imports no internal paths.
3. **One bundle and one runner.** The request gains `kind`: `analyse` (the default) or
   `explore`, and the answer echoes it. `IAnalysisRunner` is unchanged; the file keeps
   the name `ptmap-analysis.mjs`.
4. **SARIF and JUnit are written in C#** from `AnalyzeReport`, as `FindingsText` is. The
   rule titles come from the C# table (made public) and a new `rules` field in the
   analyse answer, filled from `RULES`.
5. **`--output <file>` for both commands.** UTF-8 without a BOM, written only when the
   command succeeds (analyze: exit 0, 4 or 5; explore: exit 0). A failed run leaves an
   existing file untouched. Reason: Windows PowerShell 5.1 writes UTF-16 with `>`, which
   breaks SARIF uploads. With `--output`, stdout gets one summary line.
6. **The format never changes the exit code.** `--fail-on` and `--require-complete` act on
   the exit code only; the reports show findings by severity.

## 3. `ptmap explore`

```
ptmap explore <in.pnml|in.aml> [hierarchy] [--net id] [--format dot|csv] [--output file]
              [--markings N] [--time-limit ms] [--timeout s] [--partial]
```

Defaults: `dot`, stdout, 20000 markings, no time limit, 120 s timeout.

- One net. `--net` picks by id; without it the first is used and a `note:` names the
  others, as `to-pnml` does. An unknown `--net` gives exit 2 and lists the ids.
- `PtValidator` runs first; on an error: exit 4, rule lines on stderr, nothing written,
  Node not started.
- The text is written unchanged: LF line ends, UTF-8 without a BOM, omega as it is.
- A summary line (`Quality gate (quality_gate): N markings, M edges, K deadlocks;
  complete`) goes to stderr, or to stdout with `--output`.
- Unbounded places: the coverability graph is finite and complete, so it is written, with
  a note naming the places shown as omega.
- Stopped at a limit: nothing is written and the exit code is 5; stderr names the limit
  and suggests `--markings` or `--partial`. With `--partial` the partial graph is written,
  the exit code is 0 and a note says so.
- Difference from the view: ptmap has no time limit by default, so on a large net it can
  write more than the view; when the view's search completed, both give the same file.

| Code | Meaning |
|---|---|
| 0 | written |
| 1 | usage |
| 2 | no file, no net, no such hierarchy or `--net`; Node.js 20 or the bundle missing |
| 3 | the search failed, hit `--timeout`, or the bundle is too old (answer without `kind`) |
| 4 | the net breaks ISO/IEC 15909-1; nothing written |
| 5 | the search stopped at a limit; nothing written (without `--partial`) |

## 4. Protocol between ptmap and the bundle (additive)

```
in:  { "kind": "explore", "format": "dot" | "csv",
       "limits": { "markings": 20000, "milliseconds": null }, "nets": [ { "key": "0", ... } ] }
out: { "core": "0.7.0", "kind": "explore",
       "results": [ { "key": "0", "problem": null, "complete": true, "stopped": null,
                      "markings": 25, "edges": 41, "deadlocks": 1, "unbounded": [], "text": "digraph ..." } ] }
analyse answer: adds "kind": "analyse" and "rules": { "PT12": "Unbounded place", ... }
```

A request without `kind` is an analysis, so the current ptmap keeps working with a new
bundle. An unknown `kind` ends with status 2. ptmap sends exactly one net for explore.

## 5. SARIF 2.1.0 (`--format sarif`)

- One run per file, one result per finding, in the order of the report; all nets of the
  file in the same run.
- Rules: every known rule is listed (PT01 to PT11 with the texts of `PtConformanceReport`,
  PT12 to PT25 from `RULES` in the answer); a rule without a title gets its id.
  `shortDescription` and `fullDescription` are the title; `help.text` is the title plus a
  pointer to the README section "Checks", also `helpUri`. Tags: `petri-net` plus
  `structure` or `behaviour`. No `defaultConfiguration`, because some rules change
  severity from finding to finding; every result sets `level`.
- Levels: `error` is `error`, `warning` is `warning`, `information` is `note`. Information
  findings are included only with `--all`: they would stay open for ever as alerts.
- Location: the logical location is the element id, or the net id for a finding without
  an element (`fullyQualifiedName` is `[hierarchy/]net[/element]`, `kind` is `element`).
  GitHub requires a line, so the physical location is the file with `startLine: 1`; and
  because GitHub does not show logical locations, the message gets ` [fullyQualifiedName]`
  appended.
- URI: a relative path as given, with `/` separators, percent-encoded, with
  `uriBaseId: "%SRCROOT%"`; an absolute path inside the current directory is made
  relative, any other becomes `file:///`.
- Properties on each result: `net`, `hierarchy`, `element`, `internalElementId`
  (AML), `marking`, `cover`, `firings`, `proof`.
- Fingerprint: `primaryLocationLineHash` is the first 16 hex digits of the SHA-256 of
  `ptmap/1|rule|hierarchy|net|element|marking|cover (sorted)`, followed by `:n` for
  results with the same identity. It leaves out message, path, line and versions, so
  rewording or moving keeps the alert.
- `automationDetails.id` is set per file, so uploads for several files do not close each
  other's alerts.
- Tool version: `PtMapper.Tool.csproj` gets `<Version>`, the same number as the plugin.
- Deterministic: no times and no host names.

## 6. JUnit XML (`--format junit`)

- One `testsuite` per net and one `testcase` per rule, over the same rule list as SARIF,
  sorted by id; `classname` is `[hierarchy.]netId`.
- A test case fails when its rule has at least one error or warning finding: one
  `<failure>` with `type` the highest severity, `message` the first sentence, and a body
  with one line per finding in the style of `FindingsText`.
- Information findings go to `<system-out>` and never fail a test case.
- For a net that breaks the standard the behavioural test cases are `<skipped>` with the
  problem; if the bundle was not called, one skipped test case named `behaviour`.
- `errors` is always 0: a failure of the tool itself writes no file (exit 1 to 3).
- `XmlWriter`, UTF-8 without a BOM, no `time` or `timestamp`.

## 7. Stages (each leaves ptmap working)

1. **Bundle: `kind` and `rules`** (AMLPetriNet only). `web/src/analysis.js` dispatches on
   `kind`, echoes it and adds `rules`; `verify-analysis.mjs` checks the new fields. Proof:
   `cd web && npm run build && node tools/verify-analysis.mjs`.
2. **`--output`, the rule catalogue, SARIF.** `AnalyzeOptions` accepts `sarif`, `junit` and
   `--output`; new `RuleCatalog.cs`, `ReportOutput.cs`, `FindingsSarif.cs`;
   `PtConformanceReport` exposes its rule texts. Proof: `ptmap analyze
   examples/quality-gate.pnml --format sarif --output q.sarif` exits 5 and `q.sarif` holds
   one PT15 result.
3. **JUnit.** `FindingsJUnit.cs`. Proof: `dotnet test dotnet/PtMapper.Tests --filter
   "Sarif|JUnit"`.
4. **PetriNet.js 0.7.0** (precondition: v0.6.1 tagged, done). `net.js` exports,
   `exploreData`, tests, CHANGELOG, README. Proof: `cd PetriNet.js && npm test`.
5. **Bundle `explore`.** Pin the tag, renew the lock, `analysis.js` calls `exploreData`.
   Proof: `cd web && npm ci && npm run build && node tools/verify-analysis.mjs`.
6. **`ptmap explore`.** New `ExploreOptions.cs` and `ExploreCommand.cs`; `AnalyzeCommand.Load`
   moves to `NetSources.cs`; `AnalysisRequest.Build` gains `kind` and `format`;
   `Program.cs` gets `case "explore"` and the usage text. Proof: `ptmap explore
   examples/quality-gate.pnml --format csv` prints the table and exits 0.
7. **Documents.** README (command table, `explore`, the formats, PT12 to PT25 in
   "Checks", an example GitHub workflow with `security-events: write`, `upload-sarif`,
   `--fail-on never`), backlog, ideas 8 and 9 removed. The repository's own CI does not
   upload SARIF: the quality gate has a deadlock on purpose and would keep a permanent
   alert.

## 8. Tests

- PetriNet.js (`node --test`): `entry.test.js` for the new exports without `document`;
  `plain.test.js` for every fixture, complete and with `markings: 2`, that `exploreData(...)
  .text` equals `toDot` or `markingsToCsv` of `fromWire(JSON round trip of
  toWire(explore))`, that the answer survives JSON, a duplicate id gives `problem`, the
  producer net gives omega with `complete: true`, `milliseconds: null` with a jumping
  clock completes, an unknown format throws.
- `verify-analysis.mjs`: `kind` is echoed, `rules.PT15` is `Deadlock`, a request without
  `kind` is analysed, an unknown `kind` gives status 2, DOT and CSV start and end as
  expected, umlauts and markup survive, the wide net with `markings: 50` gives
  `stopped: "markings"` and still returns text.
- C# with the fake runner: defaults and bad options; the request carries `kind`, `format`
  and one net; `--net` and an unknown id; the note for several nets; the text byte for
  byte (LF, no BOM, omega, Ue) on stdout and to a file; no file created or changed on
  exits 2, 3, 4 and 5; a stopped search gives 5 and with `--partial` 0 and the file; an
  answer without `kind` gives 3 and names `npm run build`. SARIF: levels, information only
  with `--all`, relative URI with `%SRCROOT%`, AML `fullyQualifiedName` and
  `internalElementId`, fingerprints (same for same input and reworded message, different
  for a changed id, `:1` and `:2` for twins), every field GitHub requires present and not
  empty, validation against the vendored SARIF schema with the test-only package
  `JsonSchema.Net` (MIT; add it to `THIRD-PARTY-NOTICES.md`). JUnit: counts add up, one
  test case per net and rule, a failure for a warning, information in `system-out`,
  skipped cases for a broken net, markup escaped, no BOM. The exit-code theory also runs
  with `sarif` and `junit`.
- C# end to end (SkippableFact): the quality gate as DOT has a `doublecircle` node and
  its number of markings equals `analyze --format json`; AML and PNML of the quality gate
  give the same text; the wide net with `--timeout 1` gives exit 3 and leaves no Node
  process; `--markings 50` gives 5 without `--partial` and 0 with it; SARIF has one PT15
  result with the rule text from the core; JUnit has one failure; `ptmap.dll` as a process
  writes UTF-8 without a BOM.

## 9. Risks

- Version mismatch: a new bundle with an old ptmap works (`kind` defaults to `analyse`);
  an old bundle with a new ptmap is caught by the missing `kind`: exit 3 with the rebuild
  instruction.
- Line 1 for every result: alerts show in the check summary and not beside the changed
  lines of a pull request.
- Size: 16384 markings give 4.7 MB of DOT in 1.5 s; `--markings` in the millions can
  exhaust Node's heap (exit 3 with Node's message).
- "Same file as the view" holds for the same net data in the same order of places and
  transitions, and differs when the view stopped at its 2 s limit.
- JUnit has no official schema; it is tested against the common subset.
- The vendored SARIF schema is test-only and needs an entry in the notices.

## 10. Decided (2026-11-16)

The author asked for the recommended answers to the questions that concern only the
behaviour of `ptmap` (the author's earlier instruction: recommendations apply when there
is no preference):

1. A file with several nets: `explore` takes the first with a note.
2. A search stopped at a limit writes nothing and exits 5 unless `--partial`.
3. No `--format json` for `explore` for now.
4. SARIF leaves information findings out unless `--all`; they come in as `note`.
5. SARIF uses line 1 plus the logical location; the real line is a follow-up.
6. SARIF and JUnit show findings by severity, independent of the exit-code switches.
7. The version of ptmap in SARIF is the version of the plugin.
