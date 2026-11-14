# Plan: a properties panel

The design (`simulation-and-analysis-design.md`, 4.3) lists "a properties panel for
the selected element". This plan covers PetriNet.js and the follow-up in
AMLPetriNet. Status: planned, not built. Line numbers are from 2026-11-14, PetriNet.js
0.3.0. The version number follows the order of building (net classes 0.4.0, this panel
0.5.0, the Web Worker 0.6.0).

## What exists today

| Property | Edited today | In the panel |
|---|---|---|
| Name of a place or transition | double click or E, direct editing (`CustomLabelEditing.js` 20-22, `KeyboardBindings.js` 116-127) | the same command |
| Initial marking | context pad +1 / -1 (`CustomContextPadProvider.js` 103-123) | a number, quicker above five tokens |
| Silent | popup menu (`PopupMenuProvider.js` 20-27) | a check box, the same command |
| Arc weight | direct editing of the arc label; `^\d+$` lets 0 through (`UpdateLabelHandler.js` 21-25); empty gives 1 (83-92) | the same command, checked first |
| Arc name | not editable anywhere; in the model, in PNML and in AML | new |
| Net name | not editable anywhere; in PNML and names saved files | new |
| ID, counts | not shown | new, read only |

The panel repeats three ways of editing and adds four things the canvas cannot do.
A silent transition keeps its name and exports it; the name is not drawn, and direct
editing is cancelled for it. In the plugin, the AML Editor's attribute window shows
the same values for the document, which changes only on Update. The panel shows and
edits the canvas: two states of one net, not one state twice.

## Decisions

**P-1: Plain DOM. No Preact and no @bpmn-io/properties-panel.** No new runtime
dependency is a global rule (`plan-second-round.md`). The library is 147 KB plus 52 KB
of CSS and brings Preact, CodeMirror and a FEEL editor; the plugin bundle is 872 KB
and built unminified on purpose. It needs min-dash 5 while PetriNet.js uses 4, so the
bundle would carry two copies. PetriNet.js ships its sources, so with JSX every host
would need a JSX loader. Seven fields and no tables: about 350 lines, the size of the
findings panel. It will feel like FPB.JS through behaviour: a heading with the type
and name, a read-only ID with a Copy button, closed at first start and then as the user
left it, the same texts for an empty and a multiple selection.

**P-2: A card in a right-hand column, opened by a button in the top row.** A
"Properties" button joins "State space" and "Analyse" in one flex row at the top
right. Today these sit at fixed offsets (`simulation.css` 89-100, 245-256), and
"Analyse (1 error, 2 warnings)" runs under "State space"; the flex row fixes that.
Under the row, a right-hand column holds the properties card (auto height) and below it
the findings card (scrolls). Both can be open; a click on a finding selects the element
and the panel shows it. Widths: 280 px and 360 px; the column is at most the container
width minus 80 px so the palette stays reachable, and ends 50 px above the bottom. The
state space card stays at the bottom right. Touch: buttons and fields at least 32 px
high; no bottom sheet; no dark styles (the other cards have none).

**P-3: Each committed change is one command.**

| Field | Command |
|---|---|
| Name of a place or transition | `modeling.updateLabel` |
| Initial marking | `updateProperty(place, 'initialMarking', n, { rerender: true })` |
| Silent | `updateProperty(t, 'isSilent', b, { rerender: true })` |
| Weight | `updateLabel(arc, String(n))`; empty means 1 |
| Arc name, net name | `updateProperty(element, 'name', text or undefined)` |

A text field commits on Enter and on blur, not on every key; Escape restores the field
and returns the focus to the canvas; a check box commits on change. An unchanged value
produces no command. A field writes to the element it was built for, not to whatever
is selected when it commits. A commit never runs inside another command; it is
deferred and dropped if the element is gone. The panel rebuilds only when it shows
another element, otherwise it updates values in place and leaves a focused field alone.

**P-4: Input is checked before anything is written.** Fields use `type="text"` with
`inputmode="numeric"`. Marking: a whole number, 0 or more, empty means 0. Weight: a
whole number, 1 or more, empty means 1. Leading zeros are dropped. A wrong value is
never written; the field says why (`aria-invalid`, `aria-describedby`) and returns to
the stored value on blur. Direct editing gets the same rule: `UpdateLabelHandler.js`
will refuse 0.

**P-5: The ID is shown, not edited.** The plugin matches document elements by this id,
and a changed id deletes the element and creates a new one. Read only, selectable, with
a Copy button (`navigator.clipboard`; the plugin page is served over https).

**P-6: Read only while the net runs.** On `simulation.changed` the text fields become
read only and the check box disabled, with the note "The net is running. Stop the
simulation to edit it." The panel shows the document's initial marking. A double click
or E still opens the label editor during a run, and committing ends the run;
`CustomLabelEditing.js` will cancel direct editing while a run is active.

**P-7: An option for hosts.** `new PnModeler({ container, propertiesPanel })`: `false`
leaves out the button, the card and the key; `{ open: true | false }` sets the start
state, and the remembered state does not override it. Default: closed at first start,
then as the user left it, kept in `localStorage` under `petrinetjs.properties.open`
(errors ignored). Service `modeler.get('propertiesPanel')` with `open()`, `close()`,
`toggle()`, `isOpen` and the event `propertiesPanel.changed { open }`.

**P-8: Keyboard and accessibility.** PetriNet.js has no `aria-*` or `role` yet; the
panel follows FPB.JS feature 022 for its own controls; the canvas stays out of scope.
The card is a `<section aria-labelledby>` with a heading such as "Place: Idle". Every
field has a `<label for>`, with a per-modeler prefix on the ids. The button has
`aria-expanded`, `aria-controls` and a title naming the key. P on the canvas opens the
panel and focuses its first field. Typing in the panel triggers no canvas shortcut
(the diagram-js keyboard listens only on the SVG). Ctrl+Z in a field undoes the
typing. Fields show a `:focus-visible` outline; notes go to an `aria-live="polite"`
element; `[hidden]` needs its own `display: none` rule.

**P-9: What each selection shows.** A place: Name, Initial marking, ID. A transition:
Name, Silent, ID; a silent one adds "A silent transition's name is not drawn." An arc:
Weight, Name (with the note "not drawn"), ID. A selected label: the element it belongs
to. Several elements: "3 elements selected: 2 places, 1 arc. Select one to edit it."
Nothing selected: the net's Name and ID and the numbers of places, transitions and arcs.

## Host side (AMLPetriNet)

- The message protocol does not change. A panel edit goes through the command stack, so
  the page sends `changed` like for any edit; the plugin marks the net pending and
  Update writes it. An undo back to the loaded net is recognised as before.
- The panel follows `selection.changed`, so a `selectElement` from the host shows the
  element in the panel too.
- Found while planning: the `selected` message drops labels (`bridge.js` 201), so
  clicking a place's name tells the host that nothing is selected. The fix maps a label
  to its element, in a separate commit.
- `createPetriNetModeler` passes `propertiesPanel` through (`web/src/index.js`). The web
  app gets the panel with the next bundle. The plugin README will say: edit on the
  canvas, then Update; the attribute window shows the document.

## Stages

Each stage leaves the modeler working and ends with one proof command (PetriNet.js
unless another folder is named).

1. **Values.** `src/properties/values.js`: `parseMarking`, `parseWeight`,
   `describe(selection)`, `netCounts(elements)`. No DOM. Tests beside it. Proof:
   `npm test`.
2. **Layout and display.** `src/ui/side.js` builds the top row and the right column;
   the findings and state space buttons move into the row; the findings card moves into
   the column. `src/properties/PropertiesPanel.js` shows every case, read only; the
   option, the service, the remembered state; `assets/properties.css`. Proof:
   `node test/browser/run.mjs properties findings states export`.
3. **Editing.** Commits and checks, one undo step each, no empty commands, the element
   captured at build; read only during a run; the weight rule in `UpdateLabelHandler.js`;
   no direct editing during a run. Proof: `node test/browser/run.mjs properties labels
   simulation`.
4. **Keyboard, accessibility, narrow canvas, docs.** The P key, Escape, labels, focus;
   checks at 1280 px and 600 px; README, CHANGELOG, version, CITATION.cff. Proof:
   `npm test && npm run test:browser`.
5. **AMLPetriNet** (on a branch, after the tag). Lock to the tag, pass the option
   through, map labels in `selected`, extend `verify-bridge.mjs`. Proof: `cd web && npm
   run build && npm test`, then `dotnet test Aml.Editor.Plugin.PetriNet.sln`.
6. **In the AML Editor.** Edit in a narrow plugin pane, Update, check that the
   attribute window agrees; release note; backlog ticked.

## Tests

- **Node:** the parse rules with "", "007", "0", "-1", "1.5", "1e3", " 2 "; counts when
  the input holds labels; the description of a mixed selection.
- **Browser** (`test/browser/verify-properties.mjs`, fields found by role and label):
  closed at start, opens with the button and with P, `aria-expanded` follows; a place's
  name is drawn as a label and marking 3 as three dots, one undo step each; -1, 1.5 and
  "" for the marking give a message and add nothing to the stack; a silent transition is
  drawn black with one undo step; weight 2 is drawn, 0 is refused, the arc name reaches
  the PNML; with nothing selected the net name reaches `savePNML()` and the counts are
  right; several selected shows the count line; a name typed for place A followed by a
  click on place B goes to A only; during a run the fields are read only, a direct edit
  is refused and the run goes on; `?properties=off` shows no button and no card; at 600
  px both cards are reachable and the palette is free.
- `verify-labels.mjs`: a typed 0 keeps the old weight.
- AMLPetriNet `verify-bridge.mjs`: an edit in the panel gives exactly one `changed`
  with the new value; a label click reports its element. Plugin tests are unchanged.

## Risks

- Input lost, or written to the wrong element, when the selection changes while a field
  holds text (FPB.JS had exactly this): the captured element, the commit on blur and a
  check cover it.
- A command started inside a command: `selection.changed` also fires while a delete
  runs, so commits are deferred and never run from that handler.
- A rebuild under the cursor must not reset a focused field.
- A narrow plugin pane leaves little room; the card closes with one click and stays closed.
- An element under the card after `scrollToElement`; the state space card overlaps the
  column on a short canvas.
- Two places to edit one value in the plugin; covered by a README line.

## Decided (2026-11-14)

The author left the questions below open and asked for the recommended answers:
1. Closed at first start, then as the user left it.
2. In the plugin: on, closed at first start.
3. The ID is not editable in the web app either.
4. A silent transition's name is editable, with the note.
5. During a run the panel shows the document, not the current tokens.
6. A 0 typed on the canvas is refused, as in the panel.
7. P opens the panel.
8. With several elements selected, only the count in this round.

## Open questions for the author

1. Open or closed at first start? Recommended: closed, then as the user left it.
2. In the plugin, on or off? Recommended: on, closed at first start.
3. Should the ID be editable in the web app? Recommended: no.
4. Silent transition's name editable with a note, or locked? Recommended: editable.
5. During a run, show a place's current tokens? Recommended: no.
6. Should a 0 typed on the canvas be refused? Recommended: yes.
7. Should P open the panel? Recommended: yes.
8. Several elements selected: only the count, or a common marking? Recommended: the count.
