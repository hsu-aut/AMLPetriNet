# Plan: take a marking, search further, say why

Ideas 1 to 3 in `ideas.md`. Status: planned, not built. Line numbers are from
2026-11-16 and refer to PetriNet.js 0.6.1 unless a path says otherwise.

## 1. What exists

- **Putting a marking onto the net** means typing a number: the properties panel or the
  context pad's +1 and -1, each one `updateProperty(place, 'initialMarking', n, { rerender:
  true })` per place. The simulation keeps its marking beside the model and stops itself on
  any `commandStack.changed`. A click on a state space node walks the run into that
  marking; omega nodes are refused. Nothing turns the marking of a run into the
  document's marking.
- **One command for many changes** exists as a pattern: `ptn.arrange` runs nested commands
  in `preExecute`. PNML writes no marking of 0, so 0 and no marking give the same document.
- **Limits.** `explore.js` sets 20000 markings and 2000 ms; neither is exported
  (`Findings.js` repeats the 20000). A search cut off gives PT17 and `answer.stopped`.
  `Findings.analyse(limits)` and `ReachabilityView.show(limits)` accept limits, but no
  button passes any. `netSearch.mode` says whether the search runs in a worker. Stop ends
  the worker and drops the partial result.
- **Ways into a marking.** `run.js` computes `way` in the worker for every finding that
  has a `marking`; the panel offers "Way there" for any finding with a way. PT15 carries
  `marking`. PT25 carries it when the net is drawn at [i]. PT16 carries nothing: `notLive`
  knows the terminal components but not the point where the way back is lost. PT14, on a
  whole search, means no reachable marking enables the transition, the initial marking
  included.
- **Host.** Every command reaches the host as one debounced `changed`. A stop with a
  reason arrives as `simulation` with `problem`. An analysis started in the panel posts
  nothing; only the host's own `analyse` request gets an `analysed` answer.

## 2. Decisions

**T-1 Taking a marking is an edit, not a simulation step.** D-4 stands: firing, stepping,
playing and jumping never write. The new action is a separate, named button that runs one
command through the command stack, the same as typing the numbers. It is the only path
from a run to the document.

**T-2 Where the button sits.** One button in the simulation bar, "Set as initial marking",
shown only while a run is active, after the reset button, set apart by a gap and a text
label. It takes whatever marking the net shows: a state space node, the end of a played
run, a "Way there" or "Why" run, any step. It is disabled when the marking equals the
document's. The state space view gets no button of its own; its note after a node click
says where the button is.

**T-3 What happens on the click.** `simulation.setAsInitial()` captures the marking, ends
the run with the reason "Its marking is now the initial marking of the net. Ctrl+Z takes
it back.", then calls `modeling.setInitialMarking(marking)`. Stopping first keeps the
backstop from writing "The net changed". The drawing does not jump. Focus goes to the
canvas so that Ctrl+Z works at once.

**T-4 The command.** `ptn.setInitialMarking`, handler `SetInitialMarkingHandler.js`,
registered in `CustomModeling.getHandlers`. `preExecute` runs one `updateProperty` per
place whose count changes: one undo step, one `commandStack.changed`, one `changed` to the
host. Places the marking does not name get 0. Input is checked before execute (a known
place, a whole number of at least 0, no omega); invalid input throws before anything is
written. No change means no command.

**T-5** Side effects are existing behaviour: computed findings become stale, the state
space closes, a running search stops, the properties panel shows the new number.

**S-1 Search further means ten times the limits of the search on screen.** `markings` and
`milliseconds` are multiplied by 10; a `milliseconds` of `null` stays `null`; the structure
limits stay. One step only. Analyse and State space keep their defaults, so nothing
changes without the click.

**S-2 Only with a worker** (`netSearch.mode === 'worker'`): inline, 20 s of frozen page
would trigger the plugin's reload on `RenderProcessUnresponsive`.

**S-3 Where.** Findings: a button on the PT17 row when `stopped` is `markings` or `time`
(not for omega, which a longer search does not change). State space: "Search further" in
the head beside Stop, under the same condition. Progress and Stop work as today; the busy
row says "at most 200 000".

**S-4 Stop and edits.** Stop during a further search brings back the list or state space it
started from, with the row "The further search was stopped; this is the earlier answer."
An edit or an import drops everything, as today. A run keeps going during the search.

**S-5 The host joins.** `findings.analyse()` called with no argument while a further search
runs returns that search's promise, so the plugin's Analyse menu does not throw away a
long search and still gets exactly one `analysed`.

**Y-1 Why is a way into a marking, computed in the worker.** The core attaches the marking
to the finding and `run.js` turns it into `way` with the BFS it already has. No new
message type; the inline fallback and ptmap run the same code.

**Y-2 PT16: the nearest point of no return.** A new `noReturn(result, transition)` in
`paths.js` finds the markings from which a firing of t can still be reached (a backward walk
over a reverse adjacency cached per result) and returns the first marking, in BFS order,
that lies outside that set. Whole searches only, like PT16 itself. It is never the initial
marking. Cost: transitions times edges.

**Y-3 PT25 keeps its marking** (first reason with a key, only when drawn at [i]); only its
button label changes.

**Y-4 PT14 has no run**, because the transition is dead in the initial marking already. Its
why is a place: an input place whose bound in the whole search is below the arc weight it
has to supply. The sentence gains "Mid never holds the 2 tokens it needs." or, where every
input reaches its weight alone, "Its input places never hold enough tokens at the same
time." `cover` lists those places so "Mark" rings them. Whole searches only.

**Y-5 Labels.** PT15 keeps "Way there". PT16 and PT25 get "Why".

## 3. What the user sees

| Where | Control | Text |
|---|---|---|
| Simulation bar, during a run | `Set as initial marking` | "Make the marking shown the net's initial marking. This edits the net and ends the run; Ctrl+Z takes it back." |
| State space note after a node click | none | "... press Set as initial marking to start the net here." |
| Findings, PT17 row | `Search further` | "Analyse again with ten times the limits: up to 200 000 markings and 20 s. The canvas stays usable." |
| Findings, still cut off after it | information row | "Ten times the usual limits were not enough either. The proofs from the structure (PT22 to PT25) hold at any size." |
| State space head | `Search further` | the same title, saying "search" |
| PT16 row | `Why` | "Load the 3 firings into a marking from which Pair can never fire again" |
| PT25 row | `Why` | "Load the 2 firings into the marking that shows the net is not sound" |
| PT14 row | `Mark` | the existing text |

Keyboard and accessibility as in the properties panel: every new control is a `<button
type="button">`; finding actions get an `aria-label` with the title text and the symbol in
an `aria-hidden` span (the existing actions too); `.ptn-findings-note` gets
`aria-live="polite"`; the bar and the actions get `:focus-visible` outlines; the bar
button gets its own `[hidden]` rule. No new key: a single key that edits the document
during a run is too easy to hit by mistake.

## 4. API and version: PetriNet.js 0.7.0 (the number follows the order of building)

A new minor version; nothing breaks.

- `modeling.setInitialMarking(marking)` takes a `Map` or `{ id: tokens }` and returns the
  number of places changed.
- `simulation.setAsInitial()` returns a boolean; getter `simulation.canSetAsInitial`.
- `findings.searchFurther()` returns `Promise<Finding[] | null>`; getters
  `findings.canSearchFurther` and `findings.further`; `findings.changed` carries `further`.
- `reachabilityView.searchFurther()`; getter `reachabilityView.canSearchFurther`.
- `furtherLimits(limits)` in `src/search/run.js`; `SEARCH_LIMITS` exported from
  `explore.js` and used in `Findings.js`. `paths.js` gains `noReturn`.
- PT16 gains `marking`; PT14 on a whole search gains `cover` and a second sentence; both
  reach `analyseData` and so ptmap.
- `workerSource.js` rebuilt with `npm run build:worker`.
- CHANGELOG notes the changed PT14 sentence and the PT16 and PT25 buttons; README gets
  sections "Taking a marking", "Searching further" and "Why"; design gets D-6 for T-1.

## 5. Host side (AMLPetriNet)

- No protocol change. Taking a marking gives one `simulation` message (active false, the
  reason as `problem`) and, 300 ms later, one `changed` with the new markings. The plugin
  marks the net pending and Update writes it; undo gives a `changed` back to the baseline,
  which `DiagramChangeRules` recognises.
- Search further and Why stay on the page and post no `analysed`; a menu Analyse during a
  further search joins it.
- ptmap: PT16 `marking` and PT14 `cover` pass through unchanged and the text output does
  not show them; the PT14 sentence changes in text and JSON.
- Web bundle: pin the new tag, renew the lock, rebuild; one README line.

## 6. Tests

- Node: `noReturn` on `qualityCheck` and `deadlock`, a fresh `explore` started at the
  returned marking never fires t, every earlier marking on the way can still reach a firing
  of t, null for every transition of `loop`; a differential test on the seeded generator
  (on every whole search `noReturn` is not null exactly when t is in `notLive`, with
  minimum case counts); PT16 has a `marking`, PT14 of `deadlock` has `cover [['mid', 1]]`
  and the new sentence, none on a cut-off search; `furtherLimits` (`{}` becomes 200 000
  markings and 20 000 ms, `null` stays `null`, structure limits kept); `Findings` with the
  fake search (`canSearchFurther` false inline and after a complete search, true after
  `stopped: 'markings'`; ten times the limits sent; Stop brings back the earlier list; an
  edit drops it; `analyse()` during a further search joins it).
- Browser: new `verify-initial.mjs` (fire twice, click the button, one undo step,
  `savePNML()` holds the new markings, the run is off, the drawing does not change, Ctrl+Z
  restores the old PNML byte for byte, a state space node followed by the button, hidden
  outside a run, disabled at the initial marking, at 600 px it does not cover the side
  row); `verify-search.mjs` (a cut-off analysis gives PT17 with the button, the click
  finishes complete, no button with `?worker=off`, Stop brings back the earlier list, the
  same for the state space); `verify-paths.mjs` (one "Way there" and two "Why");
  `verify-findings.mjs` (PT14's Mark rings Mid).
- AMLPetriNet: `verify-bridge.mjs` (taking a marking gives one `changed` and one
  `simulation` with the reason; undo gives a `changed` with the old; Search further posts no
  `analysed`; a host `analyse` during it gets exactly one); `verify-analysis.mjs` (PT16 has
  a `marking`); `AnalyzeTests` (the quality gate's PT16 findings carry a `marking`).

## 7. Stages

1. **Core.** `noReturn`, the PT16 marking, the PT14 cover and sentence, `SEARCH_LIMITS`,
   `furtherLimits`, the worker source. Proof: `npm test`.
2. **Taking a marking.** Handler, `modeling.setInitialMarking`, `simulation.setAsInitial`,
   the bar button, the state space note. Proof: `npm test && node test/browser/run.mjs
   initial simulation properties states`.
3. **Search further.** Findings, the panel row, the state space head, Stop bringing back
   the earlier answer, joining, and the memory measurement of section 8. Proof: `npm test &&
   node test/browser/run.mjs search findings states`.
4. **Why.** Labels, notes, aria labels, the live note. Proof: `npm test && node
   test/browser/run.mjs paths findings invariants export`.
5. **Docs and the version.** Proof: `npm test && npm run test:browser`. The maintainer tags.
6. **AMLPetriNet**, on a branch after the tag. Proof: `cd web && npm ci && npm run build &&
   npm test`, then `dotnet test Aml.Editor.Plugin.PetriNet.sln -c Release` and
   `PTMAP_REQUIRE_NODE=1 dotnet test dotnet/PtMapper.Tests --filter Analyze`.
7. **By hand in the AML Editor**: take a marking, Update, check the attribute window; run
   Search further on a large net while panning.

## 8. Risks

- Memory at 200 000 markings: `explore` keeps an ancestor array for every queued marking,
  and the worker shares the renderer process. Stage 3 measures the 2^18 net in Chromium; if
  the page dies, the factor drops to 5.
- Time: each exploration can take 20 s; a workflow net not drawn at [i] runs two, so up to
  40 s. Progress and Stop are shown throughout.
- `noReturn` costs transitions times edges; stage 3 measures it.
- `fromWire` on the page: 200 000 keys, about ten times today's tens of milliseconds.
- Bar width: the new button widens the bar during a run; checked at 600 px.
- Readers who rely on "the simulation never changes the document": the button text says it
  edits, the status line says the marking was taken, the docs get D-6.
- ptmap scripts that match the PT14 sentence break; the JSON gains two optional fields.

## 9. Decided (2026-11-16)

The author asked for the recommended answers where there is no preference:

1. Taking a marking ends the run; Simulate then starts from the new marking.
2. The button "Set as initial marking" sits in the bar and shows only during a run.
3. No confirmation; one Ctrl+Z takes it back.
4. Search further once (ten times); Analyse then starts over at the usual limits.
5. Stop during a further search brings back the earlier answer.
6. No Search further without a worker.
7. The siphon and trap limits are not multiplied.
8. PT14's sentence names the place that never gets enough tokens, and Mark rings it.
9. PT16's Why ends at the first marking from which the transition can never fire again.
10. PT25 for a net not drawn at [i] gets no Why.
