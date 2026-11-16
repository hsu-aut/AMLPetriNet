# Plan: write the result of an analysis into the document

Idea 10 in `ideas.md`. Status: planned, **not built, waiting for the author's decision
on question 1** (it changes a rule of the design). Line numbers are from 2026-11-16.

**Goal:** One explicit command writes what an analysis found about a net into the AML
document, as attributes of its PT_Net element: the class, bounded (with the bound),
live, deadlock-free, sound (workflow nets only), the number of markings, and whether
the search was complete. Other tools and readers see it in the AML tree. Only two
things can write it: the plugin's Net menu and `ptmap analyze --write`.

**Not in scope:** findings (PT01 to PT25) in the document; results attached to places
or transitions; writing from the web application; any change to the ISO_PT library;
writing without being asked.

## 1. What exists

- Design `simulation-and-analysis-design.md`, section 5 says "The simulation never
  writes, and analysis never writes." D-4 covers the simulation. Section 1 says ISO_PT
  and the published examples stay valid, and PNML gets no tool-specific extensions.
  This plan changes only the analysis half of section 5 (question 1).
- `PtNetUpdater` writes only what the language owns (`PtWrite.IsLanguageOwned`); it
  leaves every other attribute alone. On the net element it writes only the display
  name and Identification, and checks every precondition before the first write.
- Tool concepts already sit outside the class model: `IsSilent` and `LabelOffset` are
  instance attributes that no class declares (`PtNames.cs`).
- `EnsureAttributeTypeLib` returns at once if the library already exists, so a
  document that carries v0.3 never gets a new type. Documents that borrow the library
  (MPS500) reference the published file.
- `PtRoundTripCheck.Run` sends AML through PNML and back and requires the document to
  be unchanged; today a stamp written during a session would fail `ptmap compare`.
- PNML carries one toolspecific item, the silent transition.
- Plugin: the Net menu is in `PetriNetPlugin.xaml`; `ExecuteAnalyse` and `OnAnalysed`
  are in `PetriNetPlugin.xaml.cs`; the page answers `analyse` with counts only
  (`web/src/hostMessages.js` `analysedOf`).
- Core: `analyse()` returns the findings, `complete`, the search result, `classes` and
  `workflow`. There is no verdict for live, deadlock-free or the bound; they exist only
  as sentences in PT13 to PT16 and PT23.
- Editor (memory `aml-editor-dienste`): every change to the open document goes into the
  UndoRedoService one at a time. In Aml.Engine 4.5.2, `ServiceLocator.UndoRedoService`
  has `BeginTransaction(doc, name, related)` and `EndTransaction(doc)`; changes between
  the two are treated as one operation. AMLOpcUa's `EditorServices.Quietly` is the
  precedent for using this service.

## 2. Decisions

**S-1: Instance attributes, not ISO_PT.** A property a tool derived for one net is not
part of ISO/IEC 15909-1; `IsSilent` and `LabelOffset` set the pattern. A type in ISO_PT
would be missing from every document that already carries v0.3 and from the published
file that borrowing documents reference. ISO_PT stays v0.3, existing documents stay
valid, older tools ignore an attribute they do not know. The stamp's own layout is
versioned by the CAEX `Version` of its container, value `1`.

**S-2: One container on PT_Net, named `AnalysisResult` (xs:string).** No
RefAttributeType; it and each child carry a one-sentence Description. It counts as
ours only if it has a `NetFingerprint` child; the tool never touches an
`AnalysisResult` without one.

| Child | Type | Value |
|---|---|---|
| NetClass | xs:string | the PT22 class names, comma-separated; `none` if in no class |
| Bounded | xs:string | `true`, `false` or `unknown` |
| Bound | xs:nonNegativeInteger | most tokens any place holds; only when Bounded is true and known |
| Live | xs:string | `true`, `false` or `unknown` |
| DeadlockFree | xs:string | `true`, `false` or `unknown` |
| Sound | xs:string | `true`, `false` or `unknown`; only for a workflow net |
| ProvenBy | xs:string | which verdicts rest on a theorem; absent if none |
| Markings | xs:nonNegativeInteger | how many markings the search found |
| SearchComplete | xs:boolean | `complete` of the core |
| Limits | xs:string | e.g. `20000 markings, 2000 ms` |
| NetFingerprint | xs:string | `sha256:<hex>`, see S-3 |
| AnalysedAt | xs:dateTime | UTC |
| AnalysedBy | xs:string | e.g. `AMLPetriNet 0.1.19, petrinetjs 0.7.0` |

The verdicts are strings, not xs:boolean, because `unknown` is a real answer after a
partial search.

**S-3: A stamp goes out of date by fingerprint.** `PtNetFingerprint.Of(PtNet)` is
SHA-256 over UTF-8 text: a header line, one line per place (id, marking), per
transition (id) and per arc (source, target, weight), sorted ordinally. It leaves out
names, layout, arc ids, the net id and `IsSilent`, none of which changes behaviour.
Not a PNML hash (it changes when a node moves, and needs byte-identical PNML from C#
and JS) and not the time alone (it cannot tell that another tool changed the net).

**S-4: The updater keeps the stamp in step with the net.** `UpdateInPlace` computes the
fingerprint of the incoming net: if our stamp matches it is left alone (moving or
renaming keeps it, the round trip stays byte-identical); if not, it is removed and the
summary says so: "Removed the analysis result of 'X': the net changed. Write it again
from the Net menu." A stamp that went out of date some other way is caught on reading.

**S-5: The core decides the verdicts; the page computes them; the host writes them
through the updater.** PetriNet.js 0.7.0 adds `verdicts` to `analyse()`, `runSearch`
and `analyseData`: `{ bounded, bound, live, deadlockFree, sound }`, each true, false or
null, with an optional `proof`. bounded is false if a place is omega or PT24 proves it
unbounded, true if the search is whole or PT24 proves it bounded. live is false if the
search is whole and finds PT14 or PT16, or PT23 proves it not live; true if the search
is whole and finds neither, or PT23 proves it live. deadlockFree is false with a PT15
warning, true if the search is whole and finds none or the net is proven live. sound
comes from `workflow.sound`. One implementation; the C# side only maps verdicts to
attributes. `Findings.summary` exposes the current answer.

**S-6: Writing happens only on request and checks what it writes about.** The menu
command always runs a fresh analysis and opens the list. The host refuses, with a
sentence, when the canvas has unsaved edits, when PtValidator finds an error, when the
document's net has unresolved elements, or when the answer has no summary (an old
bundle). The page sends the PNML it analysed; `WriteAnalysis` writes only if its
fingerprint equals that of the document's net, which also catches an edit during the
run. On load, net switch and Refresh an out-of-date stamp gives one information row.

**S-7: Undo is one step.** The plugin wraps the write in `BeginTransaction(doc, "Write
analysis result", null)` with `EndTransaction` in a finally block. Without the service
(ptmap, tests, web app) the write runs plainly. `Aml.Engine.Services.dll` is never
packed (memory: a second copy breaks the editor's type check).

**S-8: The stamp never goes into PNML.** It is not part of `PtNet`. A toolspecific
section is ruled out: section 1 forbids tool-specific extensions, PNML is the canvas's
wire format so a stamp in it would ride along with every edit and come back stale, and
a PNML reader can analyse for itself. Import PNML and `ptmap to-aml` never write one.

## 3. What the user sees

- Net menu: Simulate, Analyse, **Write Analysis Result**, separator, Export. Enabled
  like Analyse; allowed during a run (it uses the initial marking).
- Status after a write: "Analysis result written to 'Quality gate': not ordinary,
  bounded (3), not live, deadlock found; 25 markings, complete. Ctrl+Z in the editor
  removes it." Refusals: "Write the diagram back with Update first: ...", "Cannot
  write the result: <problem>.", "Nothing written: the analysis was stopped or the net
  changed. Write again." No confirmation dialog.
- `ptmap analyze <in.aml> [hierarchy] [options] --write <out.aml>`: the output path is
  explicit and may be the input file; every analysed net is stamped; on exit 1 to 3
  nothing is written; PNML input with `--write` exits 1. The JSON gets `written` and,
  per net, `stored`: `current`, `out-of-date` or `none`.

## 4. Changes per repository

- **PetriNet.js 0.7.0:** `src/net/verdicts.js` (pure); `analyse.js`, `run.js` and
  `plain.js` pass `verdicts` on; `Findings.summary`; regenerate `workerSource.js`; the
  differential test checks that no verdict contradicts the findings or the search.
- **Conversion:** `PtNetFingerprint.cs`, `PtAnalysisStamp.cs`; `PtNames.Attributes`;
  `PtWrite` (write in place, remove, `IsToolOwned`; `IsLanguageOwned` unchanged);
  `PtNetUpdater.WriteAnalysis` and the stale-stamp removal in `Update`;
  `PtRoundTripCheck` leaves our container out of "everything else". `PtLibraries` and
  the golden tests are not touched.
- **ptmap:** `--write`, `Verdicts` in `CoreNetAnswer`, `stored` and `written` in the
  report, a `TimeProvider` for deterministic tests.
- **Page:** `bridge.js` `analyse` takes `summary: true`, exports the PNML before
  analysing, posts `analysed` with `summary` and `pnml`; `build.mjs` defines
  `__CORE_VERSION__` for the browser build too.
- **Plugin:** `BridgeMessage.cs` and `PtWebView.cs` (`Analyse(bool summary)`, a new
  event); new `Bridge/EditorUndo.cs`; `NetMenuState`; the menu item; `OnAnalysisSummary`;
  the out-of-date row.
- **Docs:** design section 5, README, backlog, release note.

## 5. Tests

- xunit, Conversion: the fingerprint is unchanged by layout, names and arc ids and
  changes with a marking, a weight, an arc end or a node, equal across the three
  routes for MPS500 with GUID ids; `WriteAnalysis` writes every child with type and
  Description, a second write leaves one container in the same position, refuses a
  foreign container, a different fingerprint and unresolved elements without writing,
  and adds no library or alias to MPS500; `UpdateInPlace` keeps the stamp
  byte-identical for the same net, on a move or a rename, removes it with a note on a
  changed marking, and leaves a foreign container; `PtRoundTripCheck.Run` passes on a
  stamped document; the PNML export is the same with and without the stamp; the golden
  library tests stay unchanged.
- xunit, ptmap: with a fake runner `--write` writes the stamps, the exit codes are
  unchanged, the file is deterministic with a fixed clock; with Node the quality gate
  gives `DeadlockFree=false`, `SearchComplete=true`, `Markings=25` and a sound workflow
  net gives `Sound=true`; the written file passes `ptmap roundtrip`.
- xunit, plugin: the JSON of `analyse` with and without the flag, parsing `analysed`
  with a summary, `NetMenuState`, `EditorUndo` with `UndoRedoService.Register()`
  (one undo restores the document byte-identical, an exception still ends the
  transaction).
- Node and Playwright (`verify-bridge.mjs`): `summaryOf`; `analyse {summary:true}` is
  answered once with a summary and a `pnml` equal to `savePNML()`; an edit during the
  run gives a null summary with a problem.
- By hand in the editor: the menu item and status texts, one Ctrl+Z removes the stamp,
  Ctrl+Z on the canvas does not reach the document, the tree shows the attribute and
  its Descriptions, an Update with a changed marking removes the stamp, save, reopen
  and the out-of-date row.

## 6. Stages

1. **Conversion:** fingerprint, stamp, updater, round-trip check. No caller yet. Proof:
   `dotnet test dotnet/PtMapper.Tests -c Release`.
2. **PetriNet.js 0.7.0:** verdicts and `Findings.summary`. Proof: `cd PetriNet.js &&
   npm test && node test/browser/run.mjs`. The maintainer tags.
3. **`ptmap analyze --write`** (lock on `file:` until the tag). Proof: `cd web && npm
   run build && node tools/verify-analysis.mjs`, then `ptmap analyze
   examples/quality-gate.aml --write $TEMP/qg.aml` (exit 5), `ptmap roundtrip
   $TEMP/qg.aml` (exit 0), and `PTMAP_REQUIRE_NODE=1 dotnet test dotnet/PtMapper.Tests
   --filter Analyze`.
4. **Page bridge.** Proof: `cd web && npm run build && node --test src/ && node
   tools/verify-bridge.mjs`.
5. **Host:** menu, `EditorUndo`, out-of-date row. Proof: `dotnet test
   Aml.Editor.Plugin.PetriNet.Tests -c Release`, then the by-hand list with exactly the
   files of the package.
6. **Docs and release.**

## 7. Risks

- A design rule changes (section 5). Nothing is built before question 1 is answered.
- The transaction API is untried in editor 6.4.3.5. The fallback is to write under
  `Pause` (no undo); the status line then says to delete the attribute in the tree.
- Ctrl+Z on the canvas may reach the editor and undo the stamp (WebView2 for WPF passes
  Ctrl keys up). Check by hand; key containment is a backlog item if it does.
- A wrong verdict in the core now lasts in files; the differential test is extended.
- A stamp depends on the limits; `Limits` and `SearchComplete` record which applied.
- The fingerprint could differ between routes where ids are normalised; covered by the
  MPS500 test.
- Old stamps from older plugins or other tools go stale unseen by readers that cannot
  recompute the hash; the Descriptions name `NetFingerprint` and `AnalysedAt`.
- A workflow net drawn at its start reads `Live=false`: true, but it may alarm a reader;
  the Description points to Sound.
- The existing Update is still many undo steps; wrapping it in `EditorUndo` is a
  separate item.

## 8a. Decided (2026-11-16)

The author said "good" to all of it, including that one explicit command may write the
analysis summary into the document (question 1). The recommended answers hold for questions
2 to 12. The change to design section 5 is: "An analysis never writes by itself; one command
writes its summary."

## 8. Open questions for the author (visible behaviour only)

1. May one explicit command write the analysis summary into the document, which
   changes design section 5? Recommended: yes, only from the Net menu and `ptmap
   analyze --write`, never automatically.
2. Should the entry be "Write Analysis Result" below Analyse, running a fresh analysis
   and opening the list? Recommended: yes.
3. A confirmation dialog before writing? Recommended: no (one undo step, the status line
   says what was written).
4. With unsaved canvas edits, refuse with a sentence, or offer "Update and write"?
   Recommended: refuse.
5. When an Update changes the net, remove the stamp with a note, or keep it marked out
   of date? Recommended: removed.
6. An out-of-date stamp found on opening: one information row and a status note?
   Recommended: yes.
7. Write with the panel's limits (recorded in the stamp) or with no time limit like
   ptmap? Recommended: the panel's limits.
8. A workflow net drawn at its start: `Live=false` with Sound carrying the verdict, or
   `Live=unknown`? Recommended: `false`.
9. Write a partial result, with `SearchComplete=false` and `unknown` verdicts?
   Recommended: yes.
10. Does "Save the document after Update" also apply to this write? Recommended: yes.
11. A "Remove Analysis Result" entry? Recommended: no; delete it in the tree, or Ctrl+Z.
12. Should `ptmap --write` need an explicit output path, which may be the input file?
    Recommended: yes.
