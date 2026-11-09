# Plan: the second round, stages 6 to 13

> The design is `simulation-and-analysis-design.md`. Stages 1 to 5 built the fork,
> the analysis core, the simulation, the findings panel and the state space view.
> This plan covers what the author asked for after seeing them, in the order the
> work depends on itself: the core first, the views on it, the host last.

**Goal:** make the fork a Petri net tool rather than a Petri net editor: every
answer the core computes is reachable from the drawing, every statement it makes
can be played back, and the net can be taken apart the way the literature takes
nets apart.

## Global constraints

Unchanged from the first round, repeated because they bound every task here:

- Place/transition nets as in ISO/IEC 15909-1 stay the only net class. No
  capacities, inhibitor or reset arcs, no time, no colour.
- The document is never written to by anything in this plan. Simulation and
  analysis read.
- `modeler/` mentions AutomationML nowhere and imports nothing from outside
  itself.
- Everything runs in the browser bundle. No server, no new runtime dependency:
  the package's dependencies stay as they are.
- A partial result says it is partial. A statement about "every marking" is made
  only where the search was complete and nothing was abstracted into omega.
- English in code, comments and anything a user reads.
- Node tests with `node --test`, browser checks in `web/tools/verify-*.mjs`, the
  mapper's suite green, no build warnings.

## Stages

### 6. The way there

A finding that says "this net deadlocks" is half an answer; the other half is how
it gets there. The state space already holds it.

- `src/net/paths.js`: `pathTo(result, key)` (shortest firing sequence from the
  initial marking, BFS over the recorded edges), `pathToDeadlock(result)`,
  `matching(result, query)` (the markings that hold what a query asks for) and
  `parseQuery(text, net)` (`p1=2, p3` as a query a user typed).
- `analyse` carries the marking key on a deadlock finding, so a reader can ask
  for the way to it.
- `Simulation.loadRun(sequence)`: back to the initial marking, the sequence put
  on the undone stack, so the existing step forward and play walk it. A path is
  shown, not jumped to.
- The findings panel gets per-finding actions rather than one click target: show
  the element, play the way there.
- The state space panel gets a query line: a marking a user asks about is
  answered with reachable or not, in how many firings, and the way there is
  loaded into the simulation.
- Clicking a node plays the way to that marking instead of setting it, so the
  run has a history; omega nodes keep the refusal they have.

Proof: node tests on a net whose shortest ways are known by hand, and a browser
check that asks for the way to a deadlock and ends in it.

### 7. Invariants in the picture

The invariants are computed and then written as a sentence nobody can follow on a
net of thirty places.

- `src/highlight/Highlight.js`: a set of element ids with a weight each, drawn by
  the renderer as a ring (place) or a frame (transition) with the weight beside
  it. Cleared on any edit and on a new analysis.
- `analyse` carries the elements and weights on PT18 and PT19.
- A place invariant is shown in the drawing; a transition invariant is loaded
  into the simulation as a sequence, with `sequenceFor(net, invariant, marking)`
  in `src/net/invariants.js`: the firings in an order that is enabled from this
  marking, or nothing where no order is.

Proof: node tests on `sequenceFor`, a browser check that an invariant marks the
places it names and that the transition invariant returns the marking it started
from.

### 8. Back into the document

In the plugin the net comes out of an AML document, and the user works in two
windows. A click on the canvas should point at the element in the editor tree.

- `web/src/bridge.js`: `{ type: 'selected', id }` on a selection change, and
  `{ type: 'fired', id }` while a run is active.
- `PtMapper.Conversion`: `CaexToPtNet.FindElement(netIE, id)`, the net element id
  mapped back to the InternalElement it was read from, through the same
  normalisation and the same order the reader claims names in.
- The plugin selects that element in the editor tree through `Aml.Editor.API`
  (`ExpandObjectById`, `SelectObjectById`), behind a switch, and names the firing
  transition in its status line.

Proof: mapper tests for the lookup, including the renumbered copy, a plugin test
that the message is understood, and the browser check for the message.

### 9. Siphons and traps

Why a net deadlocks, without building the state space, and on nets where the
state space is too large.

- `src/net/structure.js`: `maximalSiphon(net, places)`, `siphons(net)`,
  `traps(net)`, minimal ones only, with a cap so a large net cannot hang the
  page.
- Findings PT20 (a siphon that is unmarked, or that a reachable marking can
  empty) and PT21 (a marked trap, which is the opposite: it can never empty).
- A siphon is shown in the drawing through the highlight of stage 7.

Proof: node tests on the two textbook nets (the dining philosophers' deadlock
siphon, a conservative net whose traps are its invariants).

### 10. Tokens that move, and steps

- `src/simulation/TokenFlow.js`: on a firing, a dot runs along each arc the
  firing used, in the time the run allows. Switched off above a speed and when
  the user turns it off.
- A speed control in the bar, and the run uses it.
- `maximalStep(net, marking)` in the core and a button that fires everything that
  can fire at once, which is the step semantics of concurrent nets. The firings
  are recorded one after another, so stepping back is unchanged.

Proof: node tests on `maximalStep` including a conflict, a browser check that the
button fires two transitions in one click and that the animation leaves nothing
behind.

### 11. Export

- `src/net/report.js`: `toDot(result, net)`, `markingsToCsv(result, net)`,
  `findingsToCsv(findings)`.
- A download from both panels, named after the net.

Proof: node tests on the three writers, a browser check that the download
arrives.

### 12. Editing comfort

- Align and distribute for a multi-selection, as a small bar that appears with
  it.
- `src/layout/arrange.js`: a layered layout for a net whose coordinates are
  useless, through `modeling.moveElements`, so it is one undo.
- The keyboard shortcuts for both, listed in the demo page.

Proof: node tests on the layout (no two nodes on one point, sources left of
targets), a browser check that aligning moves the elements and that one undo puts
them back.

### 13. PNML as other tools write it

- Pages: `<page>` nested in a net is read, its children flattened, and written
  back the way it came.
- Reference nodes: `<referencePlace>` and `<referenceTransition>` resolve to what
  they point at.
- `<toolspecific>` from another tool is kept through a round trip rather than
  dropped.

Proof: the PNML grammar check stays green, a round trip over a file with pages
and references, and a test that another tool's toolspecific section survives.

## Review focus

The inputs the stages above will meet and no task's own tests exercise:

- A query a user typed that names a place twice, or a place that is not in the
  net.
- A path asked for on a state space that was cut off at its limit: the way may
  not be in it although the marking is.
- A transition invariant that is not fireable from the marking the net is in.
- A siphon computation on a net of eighty places, which is where the enumeration
  stops being free.
- An animation running while the net is edited, or while the view is scrolled.
