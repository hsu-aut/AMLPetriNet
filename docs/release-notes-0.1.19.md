# Plugin 0.1.19

The plugin before this was 0.1.18. This version adds what the analysis can say
about the structure of a net, a properties panel, a search that no longer freezes
the canvas, and the Simulate and Analyse entries in the Net menu. Nothing here
writes into the AML document by itself; a run and an analysis only read.

## What is new since 0.1.18

- **Simulate and Analyse in the Net menu.** Simulate starts a run, the entry then
  reads Stop Simulation, and Escape ends it. Analyse opens the list on the canvas
  and the status line gives the count ("Analysis: 2 warnings, 9 notes"). Both are
  available when a net is shown.
- **The class of a net, and what follows from it (PT22 to PT24).** Analyse names
  the class a net belongs to (state machine, marked graph, free-choice and so on).
  Where a theorem covers it, liveness and boundedness are proven from the
  structure, whatever the size of the state space. A proven statement starts with
  "Proven from the structure" and carries a tag in the list; nothing else does. A
  net with an arc heavier than one is not in any class, and the search answers.
- **Soundness of workflow nets (PT25).** A net with one start place and one end
  place is checked from one token in the start place, however it is drawn. The end
  of such a net is no longer reported as a deadlock, and a net that is not sound is
  a warning.
- **A properties panel.** A button at the top right, or the key P, opens a card
  with the properties of the selected element: name and marking of a place,
  name and silent of a transition, weight and name of an arc, and the name of the
  net. Each change is one undoable step, a wrong value is refused with a reason,
  and the fields are read only while a net runs. The id is shown, not editable.
- **The search runs in a worker.** Analyse and the state space no longer freeze the
  canvas: the list says how far the search is and has a Stop button, and an edit or
  an import ends it. Where a worker cannot be started, the search runs on the page
  as before.
- **The buttons and cards on the right of the canvas** are laid out together and no
  longer lie on top of one another.
- **A picture of the net during a run** shows the marking that was typed, not the
  tokens of the run.

## Fixes

- Undo back to the net as loaded no longer leaves an unsaved copy behind that a
  later Update could write over the document.
- Tidy up takes the names of places along with them.
- A weight of 0 typed on the canvas is refused.
- The label of a place, when clicked, is reported to the editor as the place, so
  that the tree follows the click.

## Requirements

AutomationML Editor 6.4 or later and the WebView2 runtime. The steps to install
the package are in the [README](../README.md#install-the-plugin). The plugin is
switched on in the Plugins menu of the editor.

## Limits

Place/transition nets as in ISO/IEC 15909-1 only: no capacities, no inhibitor or
reset arcs, no time, no colour. The statements from the structure about liveness
need all minimal siphons and are not attempted above 40 places; state machines and
marked graphs are decided at any size.
