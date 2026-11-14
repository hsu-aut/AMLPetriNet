# Plugin 0.1.18

The plugin before this was 0.1.15, which draws and edits nets. Versions 0.1.16 to
0.1.18 add behaviour: the plugin can now run a net, say what it does and show
why. Nothing here writes into the AML document by itself; a run and an analysis
only read.

## What is new since 0.1.15

- **Simulate.** Enabled transitions are marked, a click fires one. A run can be
  stepped back and forward, played at a chosen speed and reset. One click can
  fire everything that is enabled at once, with the tokens shown moving along the
  arcs. While a run is on, the net cannot be edited. The marking of a run is kept
  beside the model, so a document saved during a run holds the marking that was
  typed, not the one on the screen.
- **Analyse.** A list of what the net does: how many tokens a place can hold,
  transitions that can never fire, deadlocks, liveness, place and transition
  invariants, siphons and traps. A finding leads to its element, rings the set
  of elements it covers, or hands over the firings that lead into the marking it
  names, ready to play in the simulation. Siphons and traps are not searched in
  nets of more than 40 places, and the list says so.
- **State space.** Every marking the net can reach and the firings between them.
  A click walks the net into that marking. A query line answers whether a given
  marking can be reached, and the way there is loaded into the simulation. The
  view draws up to 300 markings; a larger state space is reported in numbers.
- **One list of findings.** The structural check of the plugin (rules PT01 to
  PT11) now appears in the same list as the behavioural findings. They stay when
  the net is edited until the check runs again; the findings of the analysis are
  dropped, and the list says so. With the list closed, the Analyse button shows
  how many errors and warnings the check found. The table below the canvas stays,
  because it also carries what reading the document left off the canvas and the
  conformance report.
- **The canvas follows the tree.** A click on an element on the canvas selects
  the same element in the editor's tree, and the transition that fires is named
  in the status line. A setting turns it off.
- **Tidy up.** One undoable step lays the net out by its flow. A selection can be
  lined up and spread evenly (Ctrl+Arrow, Ctrl+Shift+Arrow), the whole net laid
  out with Ctrl+Shift+L.
- **Files.** The state space is saved as DOT for a graph tool, the markings and
  the findings as CSV, named after the net.
- **PNML from other tools.** Pages with their names and nesting, reference
  nodes and the tool-specific sections of other tools are kept through an import
  and an export. The canvas draws one plane, as before.

## Changes underneath

- The modeler is now [PetriNet.js](https://github.com/hsu-aut/PetriNet.js), a
  repository of its own, a fork of the Petri net modeler of the OpenBPT platform
  (ptn-js). The bundle of this repository takes it from a tag.
- Parallel arcs between the same two nodes are summed instead of the last one
  winning, and a partial result of the analysis says that it is partial.
- Editing during a simulation is refused through the rules of the modeler. A
  refusal through the command stack had broken it for the rest of the session.
- The web application at https://aml.fpbjs.net/pt/ runs the same modeler and has
  the same functions.

## Requirements

AutomationML Editor 6.4 or later and the WebView2 runtime. The steps to install
the package are in the [README](../README.md#install-the-plugin).

## Limits

Place/transition nets as in ISO/IEC 15909-1 only: no capacities, no inhibitor or
reset arcs, no time, no colour. A net whose state space is too large to build can
still be analysed for boundedness and structure, but not for every marking.
