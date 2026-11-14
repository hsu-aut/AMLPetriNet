# PNML conformance

Both PNML converters in this project were written here: the one in the modeler
bundle (`web/src/pnml/index.js`) and the one in the mapper
(`dotnet/PtMapper.Conversion/Pnml.cs`). Tested only against each other, they
could agree on something that is not PNML. This page records how they were
checked against the standard instead, and what came out.

Measured on 2026-09-13.

## Against the grammar

The reference is the RELAX NG grammar for place/transition nets that pnml.org
publishes for ISO/IEC 15909-2 (`ptnet.pntd` with `pnmlcoremodel.rng`,
`conventions.rng`, `anyElement.rng` and `pnmlextensions.rng`). The validator is
Jing 20220510, the reference implementation of RELAX NG, cross-checked with
libxml2 (lxml 6.0.2), which agreed on every file.

In bash, from the repository root (PowerShell passes the `*` through
unexpanded, so list the files there instead):

```bash
cd web
node tools/validate-pnml.mjs ../examples/interop/*.pnml ../examples/quality-gate.pnml ../examples/paper/MPS500_PT.pnml
```

Two properties of the grammar matter for running it:

* Its includes use absolute pnml.org URLs. The tool downloads the five files
  once and rewrites the includes to the local copies.
* It gives the `id` attribute conflicting ID types across patterns, which Jing
  refuses as a schema error. The tool passes `-i`, which switches ID checking
  off. Unique ids are then not checked by the grammar; rule PT01 of
  `PtValidator` checks them.

### Files the project writes

| File | Written by | Result |
|---|---|---|
| `examples/paper/MPS500_PT.pnml` | mapper | valid |
| PNML of `IH_PT_BehaviorView` in `BridgingExample_MPS500.aml` | mapper | valid |
| `examples/interop/*.pnml`, six files | mapper | valid |
| `examples/quality-gate.pnml` | mapper | valid |
| MPS500 net after import, one added transition, export | modeler | valid |
| `offsets-and-bends.pnml` and `silent.pnml` through the modeler | modeler | valid |

### The validator does reject things

Deliberately broken copies of `MPS500_PT.pnml`, to show the check is not
waving everything through:

| Change | Result |
|---|---|
| initial marking `-1` | invalid, must be an integer >= 0 |
| initial marking `viel` | invalid, must be an integer |
| arc inscription `0` | invalid, must be an integer >= 1 |
| net type `.../grammar/foo` | invalid, must be `ptnet` or `pnmlcoremodel` |
| unknown element `<bogus/>` on the page | invalid, element not allowed |

## Against files from other tools

Nineteen PNML files from public repositories of other tools, found through
GitHub code search for `ptnet` in `.pnml` files. They are not copied into this
repository, because their licences differ; the table names where each one is.

Each file went through both directions: the mapper (PNML to AML, AML to PNML,
then `ptmap roundtrip` on the AML, where `ptmap` is
`dotnet run --project dotnet/PtMapper.Tool --`) and the modeler in a browser (import, then
export). Semantically equal means the same places with names and markings, the
same transitions with names, and the same arcs between the same nodes with the
same weights, reference nodes resolved.

| Source (repository, path) | Grammar | Mapper | Modeler |
|---|---|---|---|
| ekkart/ePNK, `.../standard/samplePTnet.pnml` | invalid, `offset` without `x` | equal, round trip ok | equal |
| ekkart/ePNK, `.../experiments/samplePTnet-notokengraphics.pnml` | invalid, stray text | equal, round trip ok | equal |
| CvO-Theory/apt, `nets/pnml-iso/philo.pnml` | valid | equal, round trip ok | equal |
| frapu78/processeditor, `models/philo.pnml` | valid | equal, round trip ok | equal |
| utwente-fmt/ltsmin, `examples/Philosophers-5.pnml` | valid | equal, round trip ok | equal |
| nicolasAmat/SMPT, `nets/PDR_Performance/NTest/1.pnml` | valid | equal, round trip ok | equal |
| thorstink/Symmetri, `nets/PT1.pnml` | valid | equal, round trip ok | equal |
| Bogoe/pn2ccs, `examples/pnml/s03-sync-patterns.pnml` | valid | equal, round trip ok | equal |
| CharlesAttend/M1-S1-DAC, Tina 3.7.0 `nets/pnml_examples/hp.pnml` | valid | equal, round trip ok | equal |
| Mocahteam/E-LearningScape, `filteredNets/GearsEnigma.pnml` | valid | equal, round trip ok | equal |
| Mocahteam/E-LearningScape, `Assets/PNML/activable.pnml` | valid | equal, round trip ok | equal |
| vanrein/perpetuum, `demo/traffic_light.pnml` | invalid, `type` on `arc` | equal, round trip ok | equal |
| tamarit/pn_suite, `examples/other/khan_thesis_4_17.pnml` | invalid, foreign net type | equal, round trip ok after fix 1 | equal |
| ssoelvsten/bdd-benchmark, `benchmarks/mcnet/4-cycle.pnml` | invalid, id starts with a digit | equal, round trip ok | refused, see below |
| hlisdero/cargo-check-deadlock, `.../panic/net.pnml` | invalid, id with spaces | equal, round trip ok | refused, see below |
| jbpt/codebase, `jbpt-pm/examples/model1.pnml` | invalid, ids, duplicate id, dangling arc | one dangling arc dropped with a warning, round trip ok after fix 2 | refused, see below |
| culey24/petrinet, `samples/net3.pnml` | invalid, no namespace | refused | refused |
| iocchi/PetriNetPlans, `PNP/plantests/simple.pnml` (JARP 1.2) | invalid, no namespace | refused | refused |
| processquerying/PQL, `pnml/apql/4.pnml` | invalid, no namespace, `module` wrapper | refused | refused |

Every PNML the mapper wrote from these files, the invalid ones included, is
valid against the grammar. The mapper repairs what it can (ids that are not XML
names, duplicate ids) and says so.

### What it found

1. **An arc label position was lost for weight 1.** The mapper left the
   inscription out when the weight was 1, and the label offset with it. The
   Freiburg process editor writes `<inscription><text>1</text>` with an offset,
   so the position was lost on the way AML to PNML to AML. Fixed in
   `Pnml.Write`: the inscription is written when it has an offset. Test:
   `AnArcWithTheDefaultWeightKeepsAPlacedLabel`.
2. **A duplicate id went into AML twice.** jbpt's file carries the arc id
   `34e95c2c` twice. Both arcs were written with that Identification/id, and the
   next round trip renamed one of them. Fixed in `PtNetToCaex.AppendInto`: the
   later element gets a number before anything is written, with a warning.
   Test: `APnmlFileThatUsesAnIdTwiceComesBackUnchangedFromAml`.
3. **A node size with two decimals was invalid.** The grammar types
   `<dimension>` as a positive decimal with at most four digits, one after the
   point. The mapper wrote two decimals and the modeler wrote the number
   unrounded, so a node resized to 50.25 made the file invalid. The modeler
   also printed float remainders such as 5.55e-17 in exponent notation, which
   is not an xs:decimal. Both converters now write sizes with one decimal and
   coordinates with at most two; the centre follows the rounded size, so a node
   keeps its top-left corner. Found by reading the grammar, not by a file.
   Tests: `ANodeSizeOffTheGridIsWrittenAsPnmlAllowsIt`,
   `test/browser/verify-numbers.mjs` in PetriNet.js.

### What is refused, and why that stays

* **PNML without the namespace.** Three files are older, pre-standard PNML
  (JARP 1.2, PQL with a `module` wrapper and net type `PTNet`). The standard
  requires the namespace. The mapper names the missing namespace in its error
  message; the modeler only reports that it found no net.
* **The modeler refuses ids that are not XML names.** Its parser checks ids
  against an ASCII name pattern. In the plugin every net reaches the modeler
  through the mapper, which adjusts such ids first, and so does an AML document
  opened on the web page. A PNML file opened on the web page goes straight into
  the modeler and is refused there.

## What this does not show

* The grammar checks structure and value types. It does not check that ids are
  unique or that arcs connect a place and a transition; `PtValidator` does.
* Toolspecific content and graphics other than positions, dimensions and
  offsets (colours, fonts, line styles) are read past and not written back.
  This is a documented loss, not a conformance problem: the grammar allows
  writing less.
* No file written by PIPE, WoPeD or ProM was among the nineteen.
