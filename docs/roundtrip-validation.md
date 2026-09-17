# Round-trip validation for the Petri net side

What was measured, on which model, and what comes out different. The page
records how far the round trip of a Petri net between an editor and AML holds,
and where it stops, in the same way as for the FPD.

All commands below run from the repository root. `ptmap` stands for
`dotnet run --project dotnet/PtMapper.Tool --`.

## The model

`examples/paper/BridgingExample_MPS500.aml`, InstanceHierarchy
`IH_PT_BehaviorView`: the behaviour of the MPS500 quality-check station. Six places (`Idle`, `Checking`,
`Approved`, `Rejected`, `Result_OK`, `Result_NOK`), five transitions (`Start`,
`Approve`, `Reject`, `Release`, `Discard`), twelve arcs, and five InternalLinks
that couple the net to the plant hierarchy `IH_MPS500_Plant`, three to the
controller and two to the sensors of the station. `Release` and `Discard` return
the station to `Idle` after a verdict, so the net is a loop; `Idle` holds the one
token.

The document references its libraries rather than carrying them:
`ISO_PT_DomainLibrary_v0.3.aml`, `OMG_DD_AttributeTypeLib_v0.1.aml`,
`VDI_FPD_DomainLibrary_v0.7.aml`, the object-reference library and the AML base
libraries.

## What was checked

`PaperRoundTripTests` in `dotnet/PtMapper.Tests`, and the same checks on the
command line through `ptmap roundtrip`, which is what gets run against a file
the AML Editor saved.

### AML to PNML to AML

The net is written out as PNML, parsed back from that text, and written into the
document it came from, which is what the plugin does on "Update
InstanceHierarchy".

The result is compared against the same file loaded a second time, so that
whatever Aml.Engine does while reading appears on both sides. **The document is
unchanged, XML for XML.** That is stricter than "equal apart from formatting":
there is no difference to describe.

Checked individually, so that a failure names itself:

| Aspect | Result |
|---|---|
| CAEX ids of all 24 elements | unchanged |
| `Identification/id` and `Identification/name` | unchanged |
| CAEX names | unchanged |
| Initial markings, arc weights | unchanged |
| Arc direction and endpoints | unchanged |
| The five links into the plant hierarchy | unchanged, including the `PlantSignalLink` interfaces they hang on |
| ExternalReferences | unchanged, ISO_PT v0.3 and OMG_DD among them |
| Class libraries | none written into the document |
| Arc encoding | still one InternalElement with `PT_ArcSource` and `PT_ArcTarget`, and two InternalLinks per connection, twenty-four in all |
| ISO/IEC 15909-1 rules | no findings |

### PNML to AML to PNML

The same net as PNML, converted into a fresh AML document, and read back out as
PNML: **the PNML comes back byte for byte.**

### The graphical half

This net carries no graphical information, and the round trip adds none. For
basic P/T nets the layout does not have to be stored: the net is bipartite and
its arcs are directed, so a layered layout algorithm can arrange the diagram
from the structure alone, and the diagram-interchange library is optional. The
measurement above therefore says nothing about bounds and waypoints, and a
separate test covers them: the same net laid out by `PtLayout`, given a label offset and an arc with
two bend points, survives the round trip with every coordinate intact.

## The differences that remain

### 1. PNML ids are normalised CAEX ids

This document stores the braced CAEX GUID in `Identification/id`, for example
`{7AE6F010-6E3C-43AC-AC23-1123CCA55159}`. A PNML id is an XML name, which must
not contain braces and must not begin with a digit, so the mapper normalises:

| In the AML | In the PNML |
|---|---|
| `{7AE6F010-6E3C-43AC-AC23-1123CCA55159}` | `_7AE6F010-6E3C-43AC-AC23-1123CCA55159` |
| `{D0985BA0-381A-42B3-BF7A-94788F9E28BE}` | `D0985BA0-381A-42B3-BF7A-94788F9E28BE` |

The underscore appears only where the GUID starts with a digit, which is why the
exported file shows both forms. The mapping is deterministic and reversible in
the sense that matters: the AML document keeps its own ids, and a value that
differs only by this normalisation is never written back over the original.

This is a property of the example document, not of the language. A net whose
`Identification/id` reads `Idle` and `Arc_Idle_to_Start` exports those ids
unchanged and the difference disappears.

### 2. A document built from PNML alone holds the net and nothing else

`MPS500_PT_export.aml` was generated from `MPS500_PT.pnml` and therefore has:

- newly derived CAEX ids, UUID v5 over the PNML id, because a new document has
  no ids to preserve;
- no links to the plant hierarchy, no FPD hierarchy, no plant hierarchy, because
  PNML has no way to express any of them;
- the ISO_PT libraries inlined rather than referenced, matching what
  `MPS500_FPD_export.aml` does on the FPD side;
- its elements grouped as places, then transitions, then arcs.

This is a reason to keep the net in AML rather than a shortcoming of the
mapper: the cross-language links survive the in-place direction, which is the
one a tool actually uses, and they cannot survive a detour through a format that
has no notion of them.

### 3. PNML defaults are left implicit

An initial marking of 0 and an arc weight of 1 are the PNML defaults and are not
written out. Reading restores them. The AML document states them explicitly, so
the two files differ in what they spell out, not in what they mean.

### 4. Formatting

`ptmap to-pnml` writes indented PNML because that file is meant to be read; the
bridge sends the same content without indentation, since PNML annotations are
mixed content. Both parse to the same net.

On the AML side, saving through Aml.Engine reformats the document, so a diff
against the file on disk shows indentation and attribute order that has nothing
to do with the round trip. Every measurement here is therefore taken against the
same document loaded a second time.

### 5. Opening the net in the editor and syncing stores the generated layout

This is the one case where the two requirements pull apart. A round trip through
the mapper changes nothing. A round trip through the *editor* arranges the net
first, because a net without coordinates would otherwise be one pile at the
origin, and pressing "Update InstanceHierarchy" then writes what the user sees
into the document.

Measured through the real modeler, not only through the C# converters:
`web/tools/verify-paper-session.mjs` opens the document in the web mapper,
moves one node on the canvas, saves, and the result is compared with
`ptmap compare`. That path matters: the modeler's own PNML importer once
dropped every arc name, which no C#-only test could see, and a single Update
renamed every arc of this document to its GUID. Fixed on 2026-09-12; the
measurement below is from after the fix.

The script drives the web mapper in Chromium through Playwright, so it needs
`npm ci` in `web/`, the bundle built with `npm run build`, and the web mapper
running on the address the script expects:

```bash
dotnet run --project dotnet/PtMapper.Web --urls http://127.0.0.1:5199
```

Then, in a second terminal:

```bash
node web/tools/verify-paper-session.mjs examples/paper/BridgingExample_MPS500.aml session.aml --move Checking
dotnet run --project dotnet/PtMapper.Tool -- compare examples/paper/BridgingExample_MPS500.aml session.aml IH_PT_BehaviorView
```

```
[ok] elements: 6 place(s), 5 transition(s), 12 arc(s), as before
[ok] identity: CAEX ids, Identification/id and names are the ones the document had
[ok] markings and weights: initial markings and arc weights are unchanged
[ok] connections: the arcs still run between the same nodes, in the same direction
[ok] links to other hierarchies: the links into other hierarchies are unchanged
[ok] external references: the document still references the same library files
[ok] class libraries: the class libraries in the document are unchanged
[ok] everything else: nothing outside the net's own content changed: hierarchies,
     roles, attributes someone added and links between other elements are as they were
[ok] geometry: 0 graphical value(s) before, 41 after; the layout was generated on
     import, which a sync then stores
```

Forty-one attributes: eleven `ViewInformation`, one per node; twenty-four
`PortCoordinate`, two per arc, the points where the arcs meet their nodes; and
six `Waypoint_n` on the two arcs that close the loop, two on `Discard` to `Idle`,
which runs above the drawing, and four on `Release` to `Idle`, which runs below
it and leaves and enters from the side so that it does not cross the names
underneath the nodes. The layout routes both around the drawing instead of
straight across it. Nothing else moves, and the saved file then passes the
round-trip check in its own right, geometry included.

Which node is moved matters for that last number. The modeler redraws the arcs
of a node that is dragged, straight, so moving `Idle`, where both loops end,
straightens them and the session stores thirty-five attributes, without the
waypoints. `Checking` touches neither loop, so the measurement uses it.

Opening the document and saving it *without* syncing changes nothing at all.

### 6. Label positions are measured from a point the standard does not name

A node's label offset is measured from the node's centre, an arc's from the
middle of its polyline. The second one is a convention: PNML defines the offset
on an annotation but does not fix what it is relative to for an arc. The value
used here is the one the modeler itself places a label at when a file brings no
position, so the two agree, but another tool may well measure from somewhere
else and its labels would arrive shifted.

Worth stating in the library description at the next version, next to the
sentence that already fixes the order of the waypoints.

### 7. What the mapper does not carry

`IsSilent` on a transition is a modeler notion with no counterpart in
ISO/IEC 15909-1. It travels in a PNML `<toolspecific>` section and in a boolean
attribute on the AML side. This net has no silent transition, so the case does
not arise here.

An arc's *name* has no position of its own: the modeler renders an arc's
inscription, its weight, and never its name. The one label position an arc has
is therefore the inscription's.

## The files shipped with the example

In `examples/paper/`, next to the example document itself:

| File | What it is |
|---|---|
| `MPS500_PT.pnml` | the net of `IH_PT_BehaviorView` as PNML, written by `ptmap to-pnml` |
| `MPS500_PT_export.aml` | AML generated from that PNML, written by `ptmap to-aml` |

Both are regenerated and compared by `PaperRoundTripTests`, so the artefacts
cannot drift away from the code that produced them.

```bash
dotnet run --project dotnet/PtMapper.Tool -- to-pnml examples/paper/BridgingExample_MPS500.aml examples/paper/MPS500_PT.pnml IH_PT_BehaviorView
dotnet run --project dotnet/PtMapper.Tool -- to-aml examples/paper/MPS500_PT.pnml examples/paper/MPS500_PT_export.aml IH_PT_BehaviorView
```

## Checking a file the AML Editor saved

The run through the editor uses `BridgingExample_MPS500_selfcontained.aml`. The
AML Editor loads no document referenced through an ExternalReference, in any
form (relative path, absolute path, file URI, https URL), so the modular file
opens there with its library classes unresolved. The self-contained file
carries the VDI_FPD, ISO_PT, OMG_DD and ObjectReferences libraries inline; the writer
follows that form and writes unqualified diagram-interchange paths into it
(`PaperRoundTripTests.AnEditorSessionOnTheSelfContainedFileAddsTheLayoutAndNothingElse`).

```bash
dotnet run --project dotnet/PtMapper.Tool -- roundtrip <file.aml> IH_PT_BehaviorView
```

Exit code 0 and a list of `[ok]` lines mean the saved document still holds
everything the checks above cover. Exit code 4 names what changed.

To see what the editing session itself did, compare the file that went in with
the one that came out. The file that went in is the self-contained one: compared
with the modular file, every inlined library counts as a difference and the
check fails on external references, class libraries and everything else.

```bash
dotnet run --project dotnet/PtMapper.Tool -- compare <before.aml> <after.aml> IH_PT_BehaviorView
```
