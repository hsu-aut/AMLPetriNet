# Plan: Simulate and Analyse in the plugin's Net menu

Design: `simulation-and-analysis-design.md`, section 5. Backlog: "Simulate and
Analyse in the plugin menu". D-4 still applies: a simulation never writes to the
document. Status: planned, not built. Line numbers are from 2026-11-14.

## 1. What exists

Page side (`web/src/bridge.js`):

- Lines 211-214 post `fired` with `ids`. Lines 216-221 post `simulation` with
  `active`. Neither can carry a reason.
- `simulation` is posted only when `active` changes, so a start the core refuses
  (`Simulation.start()` returns false and sets `problem`) is never reported.
- The message handler (223-264) has no branch that starts, stops or analyses.

Host side (`Aml.Editor.Plugin.PetriNet/Bridge/`):

- `PtWebView.cs` 57-58 declares `SimulationChanged`; the case at 324-331 raises
  it. `PetriNetPlugin.xaml.cs` `CreateBridge` (176-212) never subscribes to it.
- `BridgeMessage.cs` 10-32 has `Active`, `Ids`, `Count`, no reason, no counts.
- `PetriNetPlugin.xaml` 76-95 is the Net menu: New Net, Import PNML, separator,
  Export PNML, Export SVG, Save Conformance Report.
- `NewNetItem` and `ImportPnmlItem` are `RelayCommand`s with a CanExecute
  (xaml.cs 128-129). The new items follow that pattern.

Modeler (PetriNet.js 0.2.0):

- `src/simulation/Simulation.js`: `start()` 111-133, `stop(reason)` 139-147, the
  backstop that stops a run on import, undo or redo 65-68, `simulation.changed`
  with `active`, `problem`, `deadlocked` 351-359.
- `src/simulation/SimulationBar.js`: the play clock 142-158. `_stop()` pauses the
  clock, but `_update()` (172-190) does not. **Defect, checked:** when something
  other than the bar's button calls `simulation.stop()` during play, the interval
  keeps running. If a run starts again before the next tick, the old clock fires
  into the new run.
- `src/findings/Findings.js` `analyse()` 92-110; `FindingsPanel.js` `analyse()`
  88-92 analyses and opens the panel.

The web application does not use `bridge.js` and needs no change.

## 2. What the user sees

```
New Net
Import PNML...
-----------------
Simulate             (during a run: "Stop Simulation", with a check mark)
Analyse
-----------------
Export PNML...
Export SVG...
Save Conformance Report...
```

- Both entries are enabled only when a document is open, a net is bound and the
  canvas page has said `ready`.
- **Simulate** starts the run of the bar's Simulate button. The canvas gets
  keyboard focus, so a click fires a transition and Escape ends the run. Status:
  "Simulation running. The document is not changed by it." Stop gives
  "Simulation stopped." A run that ends by itself (import, undo, Refresh, net
  switch) gives "Simulation stopped: <reason>". A refused start gives "Cannot
  simulate: <reason>". A run started on the canvas is reflected the same way.
- Update, Export PNML and the conformance report stay available during a run and
  write the drawn net with its initial marking (D-4). Closing the document stops
  a run. A page reload starts with no run.
- **Analyse** runs the analysis and opens the panel on the canvas. Status:
  "Analysis: 2 warnings, 9 notes. The list is on the canvas." or "Cannot analyse:
  <reason>". Allowed during a run; it analyses the initial marking and does not
  stop the run.
- The WPF table and Findings button stay as they are (reading warnings,
  conformance report, backlog B4). Simulation touches no list.

## 3. Bridge messages

Host to page (new):

| type | fields | page does |
|---|---|---|
| `simulate` | `active: boolean` | waits for the import queue; `true` starts a run if none is on, then `canvas.focus()`; `false` stops it; a non-boolean changes nothing; answers once with `simulation` |
| `analyse` | none | waits for the import queue; `findings.analyse()`, then `findingsPanel.open()`; answers with `analysed` |

Page to host:

| type | fields | when |
|---|---|---|
| `simulation` (extended) | `active`, `problem: string or null` | on every change of `active`, now with the reason; once in answer to `simulate` if the state did not change (refused start, repeated request) |
| `analysed` (new) | `errors`, `warnings`, `information`: int; `problem` | in answer to `analyse`; counts cover PT12 to PT21, not the host's |

To avoid a double answer, `simulate` notes the state first; `start()` and `stop()`
fire `simulation.changed` synchronously and the existing listener posts; the
explicit answer is posted only if the state did not change. An old plugin ignores
`problem`; an old bundle ignores the new types. Both ship in one nupkg.

## 4. Changes per repository

PetriNet.js, version 0.2.1 (a patch: the clock fix and README, no new function):

- `SimulationBar._update()` pauses the clock when `active` is false.
- `test/browser/verify-simulation.mjs`: start, press play, stop from outside,
  start again, wait 1.5 s at "Slow", assert no firings happened.
- README host section: `findingsPanel.analyse()` and `open()`, `simulation.stop()`,
  `simulation.changed` carrying `problem`. CHANGELOG, `package.json`.
- Not folded into the untagged 0.2.0, whose CHANGELOG is written.

AMLPetriNet, page (`web/`): `src/bridge.js` (protocol comment, `problem` on
`simulation`, branches `simulate` and `analyse`, exported helper `analysedOf`),
new `src/bridge.test.js`, `package.json` pins `#v0.2.1` and `test` runs
`node --test src/` first, `tools/verify-bridge.mjs` gets a section.

AMLPetriNet, plugin:

- `BridgeMessage.cs`: `Problem`, `Errors`, `Warnings`, `Information`; constants
  `Simulate`, `Analyse`, `Analysed`; `SimulateMessage.Create(bool)`,
  `AnalyseMessage.Create()`.
- `PtWebView.cs`: `SetSimulation(bool)` and `Analyse()` like `SelectElement`
  (return false when not ready, not buffered); `SimulationChanged` becomes
  `Action<bool, string?>`; event `Analysed`.
- `NetMenuState.cs` (new, pure, like `DiagramChange.cs`): enabled flags, header,
  check mark and status texts.
- `PetriNetPlugin.xaml(.cs)`: two items and a separator, the new tooltip
  ("Create, import, simulate, analyse or export a Petri net."), `_simulating`,
  subscriptions in `CreateBridge`, reset in `OnModelerReady`, a stop in
  `DocumentUnLoaded`, `Modeler.Focus()` after `simulate`.

Docs: README (Net menu line), design section 5 marked built, backlog ticked,
release note. Version and Metadata are the maintainer's.

## 5. Tests

- xunit: `simulation` with and without `problem`; `analysed` with counts; exact
  JSON of outgoing `simulate` and `analyse`; pinned constants; `NetMenuState` for
  no document, page not ready, idle, running, refused start, automatic stop.
- node --test: `analysedOf` (host findings excluded, severities counted, problem
  passed on), `hostFindingsOf` (malformed input).
- Playwright, `verify-bridge.mjs`:
  1. `simulate true` gives exactly one `simulation` with `active: true`; a second
     gives one answer and no error.
  2. A firing posts no `changed`; the exported PNML is the one from before.
  3. `simulate false` gives `active: false`; `savePNML()` is byte-identical to the
     PNML before the run (D-4).
  4. A marking of -1, then `simulate true`: `active: false` with a `problem`.
  5. A run, then `importPNML`: `active: false` with the modeler's reason.
  6. Play, then `simulate false` and `simulate true`; after 1.5 s no firings
     (needs 0.2.1).
  7. `analyse` gives counts equal to the PT12 to PT21 rows; the panel is open; the
     host's PT05 is still listed.
  8. `analyse` during a run leaves the run active.
- By hand in the editor: focus and Escape after the menu click, header and check
  mark, status texts, Update during a run leaves the document's marking alone.

## 6. Stages

0. B6: v0.2.0 tagged, `web/package.json` pinned to it, lock renewed. Proof:
   `cd web && npm ci && npm test`. Everything else waits for this; CI cannot
   install the `file:` dependency.
1. PetriNet.js 0.2.1. Proof: `npm test && node test/browser/run.mjs simulation`;
   the maintainer tags.
2. Page bridge. Proof: `cd web && npm run build && node --test src/ && node tools/verify-bridge.mjs`.
3. Host wire and `NetMenuState` with tests, no UI. Proof: `dotnet test Aml.Editor.Plugin.PetriNet.Tests -c Release`.
4. Menu. Proof: build plus tests, then the manual list in the editor.
5. Docs and release. Proof: the CI sequence.

Work on a new branch from `findings-in-panel`, or from `main` once merged.

## 7. Risks

- B6 blocks everything (stage 0).
- Keyboard focus stays in WPF after a menu click; mitigated by `Modeler.Focus()`
  and `canvas.focus()`, checkable only by hand.
- Play clock: fixed in 0.2.1; without it Stop followed by Simulate can auto-play.
- Long analysis runs synchronously on the page thread, up to the exploration limit
  (D-5); the canvas freezes meanwhile. The Web Worker item on the backlog is the
  real fix.
- Double or missing answers to `simulate`; covered by check 1.
- Menu state drift after a crash or reload is cosmetic; `_simulating` is reset in
  `OnModelerReady`.
- D-4: the host gains no write path. `_pendingPnml` changes only on `changed`, and
  a run never posts `changed`.

## 8. Open questions for the author

1. Analyse during a run: from the document's initial marking (planned), or from
   the marking the run has reached?
2. Export SVG during a run draws the run's marking and the highlights. Should a
   picture always show the document's marking?
3. Refresh, a net switch, New Net or Import PNML during a run end it with a status
   note and no question. Right, or should the plugin ask first?
4. Design section 5 says the WPF table shows both kinds of finding. Block B made
   the canvas panel the one list. Should PT12 to PT21 also go into the WPF table
   and the conformance report, or stay on the canvas (planned)?
5. Should the status line also report an analysis started from the canvas button,
   or only one started from the menu (planned)?
6. Does the menu also want "State space", or are Simulate and Analyse enough?
