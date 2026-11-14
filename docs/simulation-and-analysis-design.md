# Design: simulation and analysis for the Petri net modeler

The modeler draws nets and writes them into AML documents. It cannot say anything
about their behaviour: whether a transition can ever fire, whether the net
deadlocks, how many tokens a place can hold. This design adds that, and brings
the editing comfort of a current modeler with it.

Status: stages 1 to 5 are built. The work that followed them, asked for after
the author had seen them, is planned and built as stages 6 to 13 in
`plan-second-round.md`: the way into a marking, invariants in the drawing, the
selection back into the AML tree, siphons and traps, steps with moving tokens,
export, editing comfort, and PNML as other tools write it. Decisions are listed
so that the reasons survive; stages are ordered so that work can stop after any
of them and leave a working tool.

## 1. Scope

In scope:

- **Simulation** on the canvas: which transitions can fire, firing them forwards
  and backwards, playing a run, resetting to the initial marking.
- **Analysis**: boundedness, dead transitions, deadlocks, liveness, reachable
  markings, place and transition invariants, reported as findings that point at
  an element.
- **Reachability graph** as a second view beside the net.
- **Editing comfort**: append an element instead of dragging it, multi-select,
  align, copy and paste, search, a properties panel, keyboard shortcuts.

Out of scope, on purpose:

- **Other net classes.** Place/transition nets as in ISO/IEC 15909-1 stay the
  only net class. Capacities, inhibitor and reset arcs, time and colour are not
  added. The `ISO_PT` domain library and the published example documents stay
  valid, and PNML stays the exchange format without tool-specific extensions.
- **Analysis on a server.** Everything runs in the browser bundle, see D-3.

## 2. Decisions

**D-1: Fork the modeler instead of patching it.**
The canvas today is `@bptlab/openbpt-modeler-petri-net` (ptn-js by Maximilian
König, MIT). Four of its behaviours are already patched at run time from
`web/src/index.js`, two of them to work around defects, and its PNML converter
is already replaced through a build alias. Simulation, palette and rendering
would add more of the same. The package is 69 files and about 6400 lines, and
upstream has published one dependency bump in the last year, so little is lost
by forking and much is gained in clarity.

The fork lives in `modeler/`, beside `web/` and `dotnet/`, as a package of its
own: its own `package.json`, LICENSE, README, tests and a demo page that runs
without the rest of this repository. It mentions AutomationML nowhere. The
bundle in `web/` takes it as a file dependency. Hosting it on its own later is a
move, a publish and one line in `web/package.json`.

Open: whether the fork becomes a separate public repository, under which account,
and under which name. The author expects to host it separately once it carries
simulation and analysis. Nothing in this design depends on the answer.
Recommended when the question comes up: a public fork on the author's own
account, named `PetriNet.js`, so the lineage stays visible on GitHub.

The two defects we work around were reported upstream as pull requests,
independent of the fork, and both were merged on 2026-11-10 (#9 and #10):

- `UpdatePropertyHandler` declares no `$inject`, so a minified build dies at
  start-up.
- `UpdateLabelHandler` writes the text "1", the default arc weight, whenever any
  label is cleared, so a place without a name is called "1".

**D-2: The analysis core knows nothing about the canvas.**
A module `modeler/src/net/` takes a net as plain data (places with initial markings,
transitions, arcs with weights) and answers questions about it. It has no
dependency on diagram-js, the DOM or AML, and is tested without a browser. The
modeler, the panels and the graph view are built on it. The command line tool
can later use it as well (`ptmap analyze`), and nothing in the design prevents a
second front end.

**D-3: Panels and the graph view live in the browser bundle, not in WPF.**
The plugin hosts the modeler in WebView2 and the web application serves the same
bundle. Anything written in the bundle appears in both, in the same shape, from
one implementation. The alternative, WPF panels in the plugin, would mean
building each view twice and leaving the web application behind.

**D-4: The simulation replaces the one in the package.**
The package ships a token replay that fires a transition on click. It has no
history, no automatic run, no conflict handling and it writes into
`initialMarking`, which is the document's data. The simulation here keeps the
current marking beside the model, never in it, so that leaving simulation mode
restores the document untouched and a running simulation can never be saved into
an AML document by accident.

**D-5: State space exploration is bounded and says so.**
A net with a cycle and no bound has an infinite state space. Exploration stops at
a configurable limit (markings and time) and reports that it stopped, rather than
freezing the browser. Where a place is unbounded, the coverability graph marks it
with omega instead of enumerating. Every analysis result carries whether it is
complete or cut off.

## 3. Architecture

```
modeler/            the fork, a package of its own, no AutomationML in it
  src/              canvas, rendering, rules, palette, context pad
  src/pnml/         PNML reader and writer (moved here from web/src)
  src/net/          analysis core, no UI: model, simulation, state space, invariants
  src/ui/           panels and views: simulation bar, findings, graph
  demo/             a page that shows the modeler on its own
web/
  src/bridge.js     messages to and from the plugin host (exists)
  src/svg.js        picture export (exists)
  src/index.js      what the host and the page import
```

Dependencies point downwards only: `ui` uses `net` and the canvas, the canvas
uses neither, and `net` uses nothing at all. The AML side of the repository uses
the package, never the other way round.

### 3.1 The analysis core

Input is a net as plain data, with identifiers that match the canvas elements so
that a finding can point back at a shape:

```js
{ places: [{ id, name, marking }],
  transitions: [{ id, name }],
  arcs: [{ id, source, target, weight }] }
```

Functions, all pure:

- `enabled(net, marking)`: the transitions that can fire.
- `fire(net, marking, transitionId)`: the marking after firing, or an error when
  the transition is not enabled.
- `unfire(net, marking, transitionId)`: the marking before firing, for stepping
  backwards through a recorded run.
- `explore(net, limits)`: reachable markings and the firing relation between
  them, plus `complete` and the reason it stopped. Unbounded places are reported
  as such.
- `analyse(net, limits)`: the findings listed in section 4.2, from one
  exploration plus the invariants.
- `invariants(net)`: place and transition invariants over the incidence matrix,
  as non-negative integer solutions.

### 3.2 The simulation

A service in `modeler/src/ui/` holds the current marking, the recorded run and the mode. It
listens to the canvas for clicks on transitions and asks the core what is
enabled. Firing is a step in its own history, not in the modeler's undo stack, so
that undo never mixes editing with simulation.

Leaving simulation mode drops the current marking and redraws the initial one.
Editing is blocked while simulating, which the canvas shows by greying the
palette, so that the net cannot change under a running simulation.

### 3.3 The views

- **Simulation bar**: mode switch, step back, step forward, play and pause with a
  speed, reset, and the number of steps taken.
- **Findings panel**: the analysis results, grouped by severity, each row naming
  its element. Clicking a row selects the element on the canvas, as the
  conformance findings already do today.
- **Reachability view**: the state space as a graph beside the net. A node is a
  marking, an edge a firing. Selecting a node sets the net to that marking.
  Nodes that deadlock are marked.

## 4. Behaviour

### 4.1 Simulation

- Enabled transitions are highlighted. A transition that cannot fire is not
  clickable.
- Where several transitions are enabled, the user picks one by clicking. The
  automatic run picks one at random; the choice is recorded, so stepping back and
  forward reproduces it.
- Tokens are drawn as dots up to five, as a number above that, as today.
- The initial marking is the one in the document. Reset returns to it.
- A net whose marking cannot change any more is reported as a deadlock in the bar
  and as a finding.

### 4.2 Analysis

| Finding | Severity | Meaning |
|---|---|---|
| Unbounded place | warning | the place can hold any number of tokens |
| Bounded by n | information | the largest number of tokens the place holds |
| Dead transition | warning | the transition fires in no reachable marking |
| Deadlock | warning | a reachable marking in which nothing is enabled |
| Not live | information | a transition that cannot fire again from some marking |
| Place invariant | information | a weighted sum of tokens that never changes |
| Transition invariant | information | a firing sequence that returns the marking |
| Exploration cut off | information | the limit was reached, results are partial |

The eleven structural rules that exist today (PT01 to PT11) stay where they are
and keep their place in the findings list; the behavioural findings join them.

### 4.3 Editing comfort

Append an element from a node and connect it in one step, multi-select with a
rubber band, align and distribute, copy and paste, search by name, a properties
panel for the selected element, and the keyboard shortcuts for all of it. This
follows what the FPD modeler already offers, so that both tools feel alike.

## 5. Integration

- **Plugin**: the Net menu gains "Simulate" and "Analyse". The findings table in
  the plugin shows both kinds of finding. The canvas tab carries the simulation
  bar itself, so the plugin needs no new WPF beyond the menu entries.
- **Web application**: the page carries the same bar and panels. The API does not
  change; analysis runs in the browser.
- **Document**: unchanged. The simulation never writes, and analysis never
  writes. An AML document saved during or after a simulation holds the initial
  marking it always held, which a test asserts.

## 6. Testing

Each stage ends with a command that proves it. The existing suites stay green
throughout: 226 mapper tests, the browser checks in `web/tools/`, and the PNML
grammar check.

- The core is tested without a browser, with nets whose answers are known by
  hand: a loop, a conflict, an unbounded producer, a deadlocking net, the MPS500
  quality check. Invariants are checked against hand-computed solutions.
- The canvas parts are tested in Chromium as the existing checks are, by driving
  the real modeler.
- One check asserts that a document that went through a simulation is byte for
  byte the document that came in.

## 7. Stages

Each stage leaves the tool working and shippable.

1. **Fork in place.** The package is replaced by `modeler/`, the run-time patches
   become ordinary code, the PNML converter moves into it, the alias disappears,
   unused parts are removed. No new function. Proof: the whole suite green, the
   demo page running on its own, the plugin package built and installed once by
   hand.
2. **Analysis core.** `modeler/src/net/` with its own tests. No UI. Proof: the core
   tests, including the known answers for the five nets.
3. **Simulation.** Bar, highlighting, stepping, play, reset, deadlock report.
   Proof: a browser check that fires a recorded sequence and compares markings,
   and the document-unchanged check.
4. **Findings panel.** Behavioural findings beside the structural ones, clickable.
   Proof: a browser check that asserts the findings of the known nets and the
   selection on click.
5. **Reachability view.** Second view, selecting a marking, deadlock marking.
   Proof: a browser check on a net with a known state space.

6 to 13. **The second round**, in `plan-second-round.md`: the way into a marking
   and a query line for one, invariants in the drawing, the canvas selection back
   into the editor tree, siphons and traps, steps with moving tokens, the answers
   as files, tidying up, and a PNML file from another tool coming back as it went
   in. All built; each stage carries its own node tests and its own browser
   check.

## 8. Open questions

- Where the fork finally lives (see D-1), and its name.
- Whether `Findings.setHostFindings` is worth keeping: the plugin and the web
  application both show the findings of their own validator in their own table,
  so nothing in production hands findings to the modeler. The way is tested and
  documented; using it would mean one list instead of two.
- Whether the command line tool gets `ptmap analyze` from the same core, which
  would mean a second implementation in C# or running the JavaScript core in the
  tool. Not decided; nothing in stages 1 to 5 depends on it.
- When `modeler/` moves into its own repository. Expected, not scheduled; the
  layout above keeps the cost at a move and a publish.
- Whether the reachability view belongs beside the net or in a separate tab when
  the plugin window is narrow.
