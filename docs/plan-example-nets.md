# Plan: classic nets as examples and as tests

Idea 4 in `ideas.md`. Status: planned, not built. Line numbers are from 2026-11-16.
Paths starting with `PetriNet.js/` are in `C:\Dev\02_VDI3682\AML\PetriNet.js` (0.6.1);
the others are in AMLPetriNet.

**Goal:** a small library of classic place/transition nets. Each net has a PNML file with a
drawn layout, a two-sentence description, its source, and the findings Analyse must give.
Users open the nets from a menu; CI fails when the analysis says something else.

**Not in scope:** new analysis, new kinds of net, AML copies of the nets, a plugin menu.

## 1. What exists

- `PetriNet.js/src/net/fixtures.js` holds six nets without layout, for the core tests only
  (the package leaves the file out). `builders.js` builds a net in one line, also without
  layout.
- The modeler has no start screen; the demo has an empty canvas, Open and New.
- The web application has one example, the MPS500 AML document, from a header button and the
  start card (`dotnet/PtMapper.Web/wwwroot/index.html`); a PNML file opens through
  `openText`; a download is named `net.pnml`.
- PNML in Node: the reader needs `DOMParser` (`src/pnml/index.js`), so Node cannot read
  PNML. The reader keeps element ids and label offsets; `savePNML` writes one unindented line.
- `analyseData` returns `classes`, `workflow`, `unbounded`, `markings` and `findings`, all
  JSON-safe. Verdicts exist only in prose: PT13 gives its bound only in the sentence, PT23
  gives "live" or "not live" only in the sentence.
- A sound workflow net drawn at [i] gets PT16 for every transition (its only terminal
  component is [o]); nothing tests or suppresses this. The start place of every workflow net
  is a siphon without a trap and always gets a PT20 information.
- The layered auto layout lays cycles out as columns: right for workflow nets, not for rings.
- Pattern for generated files: `scripts/build-worker.mjs` plus a freshness test.

## 2. Decisions

1. **PetriNet.js owns the library, in a new folder `examples/`.** The nets are P/T data
   without AutomationML, the rules they test (PT12 to PT25) live in PetriNet.js, its CI runs
   before a tag and any host can use the nets. AMLPetriNet consumes them from the pinned tag
   and adds what only it can do: the web app menu, the comparison with `ptmap analyze` (a
   second PNML reader), the grammar check, PT01 to PT11. No text in `examples/` mentions
   AutomationML.
2. **The PNML file is the source.** Element ids are chosen by hand (`think1`, `fork1`); files
   are stored indented, otherwise as the modeler writes them.
3. **Metadata and expectations are JSON beside the nets.** `catalogue.json` is hand-written
   (order, title, description, source, claims); `expected.json` is generated (every finding,
   projected) and reviewed in the diff.
4. **Two levels of expectation.** Claims come from the literature and are never generated
   (class, bound, deadlock, liveness, soundness, the theorem). The expected findings catch
   every other change. Either mismatch fails CI.
5. **Two small additions to the core**, so that verdicts are fields and not only sentences:
   PT13 carries `bound` (a number) and PT23 carries `live` (true or false).
6. **No menu on the canvas.** The package exports the list; the demo and the web app build
   the menu.

## 3. The nets

Twelve nets in three groups (philosophers with three philosophers, so the drawing stays
readable). "Class" is the most specific class PT22 reports.

| id | Net | Class | Analyse must say | Source |
|---|---|---|---|---|
| `mutual-exclusion` | Two processes, one semaphore | AC | no warning; PT18 Crit 1 + Crit 2 + Mutex; PT24 bounded | Dijkstra 1968 (problem) |
| `producer-consumer-bounded` | Producer, buffer of two, consumer | MG | no warning; PT23 live (marked graph); PT24 bounded; PT13 Buffer 2 | Dijkstra 1968 (problem), Murata 1989 (after) |
| `producer-consumer-unbounded` | The same without the free slots | MG | PT12 Buffer; PT24 unbounded (Hack); PT23 live; PT17 | as above |
| `philosophers-deadlock` | Left fork, then right fork | AC | PT15; PT16 on all; PT20; PT24 bounded | Dijkstra 1971 (problem) |
| `philosophers-both-forks` | Both forks in one step | ordinary, no class | no warning; live and deadlock-free by search; PT24 bounded | Dijkstra 1971 (problem) |
| `readers-writers` | Two readers, two writers, two access tokens | not ordinary | no warning; PT22 names the arc of weight 2; PT24 bounded | Courtois et al. 1971 (problem), Murata 1989 (after) |
| `traffic-lights` | Crossing, two lights in turn | MG | no warning; PT23 live; "never both green"; safe | own |
| `machine-breakdown` | Idle, working, broken, repaired | SM | no warning; PT23 live (state machine); safe | own |
| `machine-not-returned` | Drill or mill; milling does not free the machine | EFC | PT15; PT23 not live (Commoner) | own; Desel/Esparza 1995 for the theorem |
| `chip-bin` | Drill or mill; chips go to a bin nobody empties | EFC | PT12 Chip bin; PT24 unbounded (Hack); PT23 live (Commoner) | own; Desel/Esparza 1995 |
| `workflow-sound` | Complaint: register, two contacts in parallel, pay or write, archive | FC | no warning; PT25 sound; PT15 information at Out | van der Aalst 1997; own net |
| `workflow-xor-into-and` | The same with an AND join after the XOR choice | FC | PT25 not sound; PT15 twice; PT14 Archive | van der Aalst 1997 |

Every class is covered (SM, MG, FC, EFC, AC, not ordinary, ordinary with no class); Commoner
and Hack are shown both ways; pairs differ in one thing; every net has fewer than 1000
markings and is free of PT01 to PT11.

## 4. Format

`examples/catalogue.json`, by hand, one object per net: `id`, `file`, `group`, `title`,
`description` (two sentences), `source` (`relation` one of `problem`, `after`, `own`; `text`;
`doi`), and `claims`: `ordinary`, `classes`, `bound` (a number or `"unbounded"`), `deadlock`,
`live` (PT23's, else from a whole search; omitted for workflow nets), `sound` (workflow nets
only), `proofs` (the theorem a rule must name).

`examples/expected.json`, generated, one entry per id: `complete`, `stopped`, `markings`,
`unbounded`, `classes`, `workflow`, and the findings projected to `rule`, `severity`,
`element` and where present `bound`, `live`, `proof`, `marking`, `classes`, `cover` (sorted by
id); the sentence and `firings` are left out; sorted by JSON text. The projection is written
once in JS and once in C#. Every comparison uses 20000 markings and no time limit.

## 5. Files (PetriNet.js)

- Hand-written: `examples/*.pnml`, `examples/catalogue.json`.
- Generated: `examples/expected.json`, `examples/index.js` (`export const examples = [{ id,
  group, title, description, source, classes, pnml }]`), `examples/nets.json` (plain data, for
  the Node test, left out of the package), `examples/README.md`.
- `examples.js` at the root re-exports `examples`, as `net.js` does for the core.
- `scripts/build-examples.mjs`: opens each file in Chromium on the test page and writes it
  back indented; writes `nets.json`, `index.js`, `README.md`; `--expect` rewrites
  `expected.json` and prints which nets changed; `--pictures` writes SVGs (not checked).
- `examples/examples.test.js` (Node) and `test/browser/verify-examples.mjs`.
- `package.json`: `test` gains `examples/`; `files` gains `examples.js` and `examples`
  without `nets.json` and the test.
- `src/net/analyse.js`: `bound` on PT13 and `live` on PT23.
- `demo/index.html`, `demo/main.js`: a grouped "Examples" select; `test/browser/page.js`
  exposes `netFromElements`.

## 6. How the examples are offered

- Package: `import { examples } from 'petrinetjs/examples.js'`, then
  `modeler.importPNML(example.pnml)`; a README section "Examples".
- Demo: the select above, with the title and description in the status line.
- Web application: `web/src/index.js` re-exports `examples`; the header button "Load example"
  becomes an "Examples" select (the MPS500 document first, then the three groups; the tooltip
  of an option is its description); the start card lists the twelve nets by group; loading goes
  through `openText`; downloads are named after the example. No server change; about 100 KB
  more in the bundle.
- Plugin: nothing for now; Import PNML takes a downloaded file.

## 7. Layout

By hand, on the 10 px grid, written as centres in the PNML (or drawn in the demo and renamed).
Flow from left to right, cycles clockwise, a symmetric problem drawn symmetrically,
alternatives one above the other, labels above places and below transitions, a bend where an
arc would cross a node. Tidy up may draw a first version of a workflow net; for rings it gives
columns, so it is not used there.

The browser check asserts for every net: no two nodes closer than 20 px; no label over a node
or another label; no arc segment through a foreign node; everything within 1200 x 800 at
x, y >= 0. The author looks at each picture once.

## 8. Tests

- Node (`examples/examples.test.js`, in `npm test`): per net `analyseData` has no `problem`,
  at most 1000 markings, meets every claim and gives the projection in `expected.json`; ids
  are unique and files exist; each description has exactly two sentences; `index.js` and
  `README.md` are fresh; every class is the most specific class of at least one net; each of
  `state-machine`, `marked-graph`, `commoner`, `hack` and `place-invariants` is a proof at least
  once; one sound and one unsound workflow net.
- Browser (`verify-examples.mjs`): per net `importPNML` then `savePNML` returns the file
  unchanged apart from whitespace; the canvas reads the net in `nets.json`; `findings.analyse()`
  (the worker path) gives the projection; the layout rules hold.
- AMLPetriNet (`ExampleLibraryTests.cs`, one SkippableTheory per net, reading
  `web/node_modules/petrinetjs/examples/`): `ptmap analyze --format json` gives no PT01 to
  PT11, the projection, the same `classes` and `workflow`, exit 5 exactly when a warning is
  expected; the same net written to AML gives the same projection; skipped without Node unless
  `PTMAP_REQUIRE_NODE=1`. `verify-analysis.mjs` checks that `bound` comes through. The CI
  grammar step also validates the examples. `verify-webapp.mjs` loads `philosophers-deadlock`
  from the select, finds 12 places and one PT15 warning.

## 9. Stages

PetriNet.js, on a branch; the maintainer tags.

1. **Verdicts as fields** (`bound` on PT13, `live` on PT23) with tests. Proof: `npm test`.
2. **The frame, with three nets** (`philosophers-deadlock`, `producer-consumer-bounded`,
   `workflow-sound`): catalogue, generator, `index.js`, `examples.js`, both tests. Proof: `npm
   test && node test/browser/run.mjs examples`.
3. **The other nine nets** and the coverage assertions.
4. **Demo, documents, the version.** Demo select, pictures, README, CHANGELOG, `package.json`,
   `CITATION.cff`. Proof: `npm test && npm run test:browser && npm pack --dry-run` lists
   `examples/*.pnml` and `expected.json`, not `nets.json`.

AMLPetriNet, on a branch until the tag is pushed.

5. **Follow the core.** Pin the tag, pass `bound` and `live` through `AnalysisReport.cs`, add
   `ExampleLibraryTests.cs` and the grammar step.
6. **Web application.** The select, the start card, download names, `verify-webapp.mjs`, the
   README.

## 10. License and sources

The nets are drawn here; no figure is copied. A problem or an idea is not protected. A net
that follows a printed one says `after` and cites it. The files are MIT with the package.
Sources, by work: Dijkstra 1968, "Cooperating sequential processes" (Genuys (ed.),
Programming Languages, Academic Press); Dijkstra 1971, Acta Informatica 1, doi
10.1007/BF00289519; Courtois, Heymans, Parnas 1971, CACM 14(10), doi 10.1145/362759.362813;
Murata 1989, Proc. IEEE 77(4), doi 10.1109/5.24143; Desel, Esparza 1995, Free Choice Petri
Nets, Cambridge University Press; van der Aalst 1997, "Verification of workflow nets", ICATPN,
LNCS 1248. Page and figure numbers are left out until the author has checked them.

## 11. Risks

- `expected.json` regenerated without reading the diff: the claims guard against it.
- A citation wrong in detail: only works are named; the author checks them before the tag.
- A change to the PNML writer changes every file: the browser check fails and names the
  generator; intended.
- The generator needs Chromium; it runs only when a net changes.
- Two current behaviours become visible: PT16 on every transition of a sound workflow net, and
  the PT20 information on every workflow net's start place.
- A deliberate change to the analysis now also needs `--expect` and a reviewed diff in the same
  commit; that is the point.
- Hand-chosen ids must be valid XML names (PT11); the AMLPetriNet test catches a slip.

## 12. Decided (2026-11-16)

The author asked for the recommended answers where there is no preference:

1. No Examples button on the canvas; the hosts build the menu.
2. The web app's start card shows all twelve nets by group, kept short.
3. Analyse does not run after an example is loaded.
4. A "New Net from Example" submenu in the plugin comes later, after the web app.
5. The unsound workflow net is XOR into AND only; AND into XOR stays a unit test.
6. Philosophers with a solution: both forks in one step.
7. PT16 is dropped for a workflow net drawn at [i], as PT23 already is: a separate change
   before stage 2.
8. Titles and descriptions in English only.
