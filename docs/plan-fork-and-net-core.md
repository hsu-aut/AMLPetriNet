# Modeler fork and net core implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Petri net modeler into this repository as a fork that is a package of its own, and add an analysis core that answers questions about a net's behaviour, with no visible change to the tool yet.

**Architecture:** The npm package `@bptlab/openbpt-modeler-petri-net` is copied into `modeler/` beside `web/` and `dotnet/`, as a package with its own `package.json`, tests and demo page; the run-time patches and rendering changes in `web/src/index.js` move into it. `modeler/src/net/` holds the analysis core: plain data in, answers out, no diagram-js, no DOM, no AML. The bundle in `web/` takes the fork as a file dependency, so that moving `modeler/` into a repository of its own later costs a move, a publish and one line in `web/package.json`. The core is tested with `node --test`, which Node 20 carries.

**Tech Stack:** Node 20, esbuild, Playwright for the browser checks, diagram-js 15, `node --test` for the core.

**Spec:** `docs/simulation-and-analysis-design.md`

## Global Constraints

- The fork keeps its own `LICENSE` (MIT, Copyright 2024 Business Process Technology) and a README naming ptn-js by Maximilian König as its origin.
- `modeler/` imports nothing from the AML side of this repository, mentions AutomationML nowhere, and is usable by a project that has never heard of it. Everything the bundle uses from it goes through `modeler/index.js`.
- `modeler/src/net/` imports nothing at all beyond Node and its own files, not even diagram-js.
- `modeler/` runs on its own: `npm install && npm test` inside it passes without `web/`, and `npm run demo` serves a page with the modeler on it.
- Code and comments in English.
- Example AML and PNML files are not regenerated in these two stages; the suite must leave them byte for byte as they are.
- Commit after every task, on `main`, one line, imperative, with a scope. Never push.
- Each task ends green: `npm run build`, `npm test` in `web/`, `dotnet test dotnet/PtMapper.Tests`.

## Review Focus

Inputs the spec implies but names no behaviour for. Each has its test in the task that owns the code.

- An arc weight of zero or a negative weight: a transition must not become enabled by it, and the core must not loop forever. Owned by Task 5.
- A transition with no input place: it is enabled in every marking and makes the state space infinite. The exploration must stop at its limit and say so, not hang. Owned by Task 6.
- A self-loop, a place that is both input and output of the same transition: firing must leave the place unchanged, not consume twice. Owned by Task 5.
- Two elements with the same identifier: the core must reject the net rather than silently drop one. Owned by Task 5.
- A marking with more tokens than the limit of exploration: the result must be marked incomplete and still carry the findings it did reach. Owned by Task 7.

---

### Task 1: Make the modeler a package of its own

**Files:**
- Create: `modeler/package.json`, `modeler/index.js`, `modeler/README.md`, `modeler/LICENSE`, `modeler/.gitignore`
- Create: `modeler/src/` (the package's `lib/`), `modeler/assets/` (its `assets/`)
- Create: `modeler/demo/index.html`, `modeler/demo/serve.mjs`
- Move: `web/src/pnml/` to `modeler/src/pnml/` (PNML is the modeler's format, not an AML concern)
- Modify: `web/src/index.js`, `web/build.mjs`, `web/package.json`, `THIRD-PARTY-NOTICES.md`
- Delete: `modeler/src/util/FileUtil.js`, `modeler/src/util/HtmlUtil.js` (imported by nothing; FileUtil is the only user of jquery)

**Interfaces:**
- Produces: `modeler/index.js` exports `PnModeler` (the default export of `src/CustomModeler.js`), `CustomRenderer`, `UpdatePropertyHandler`, `UpdateLabelHandler`, `convertModdleXmlToPnmlXml`, `convertPnmlXmlToModdleXml`. The package name is `petrinet-js`; it is referenced in exactly one place, the dependency entry in `web/package.json`, so renaming it later is one line.
- Consumes: nothing of this repository.

- [ ] **Step 1: Copy the package in**

`lib/*` goes to `modeler/src/*`, `assets/*` to `modeler/assets/*`, `LICENSE` to `modeler/LICENSE`. The package's own one-line `index.js` is not copied; the new `modeler/index.js` replaces it.

- [ ] **Step 2: Write `modeler/package.json`**

`name: petrinet-js`, `version: 0.1.0`, `type: module`, `main: index.js`, `license: MIT`, `files: [index.js, src, assets]`, and as dependencies what the fork imports, at the versions `npm ls` reports in `web/` today: `diagram-js`, `diagram-js-direct-editing`, `ids`, `inherits`, `min-dash`, `min-dom`, `moddle`, `moddle-xml`, `object-refs`, `tiny-svg`, `bpmn-font`. Not `jquery`, not `pnml-moddle-converter`. Scripts: `test` (the core, from Task 5; until then it prints that there is nothing to run yet) and `demo`.

- [ ] **Step 3: Write `modeler/README.md`**

First paragraph: a fork of ptn-js by Maximilian König and the Business Process Technology group (https://github.com/bptlab/openbpt-modeler-petri-net), MIT. Then what this fork changes: its own PNML converter that keeps sizes, bend points and label positions, two defects fixed, transition names drawn below the box, markings drawn as dots. Then how to run the demo. AutomationML is not mentioned.

- [ ] **Step 4: Move the PNML converter into the fork**

`web/src/pnml/` becomes `modeler/src/pnml/`. In `modeler/src/BaseViewer.js`, import it from `./pnml/index.js` instead of `pnml-moddle-converter`. The `alias` block in `web/build.mjs` goes; its comment, which explains that the upstream converter is Node-only and lossy, moves into the fork's README.

- [ ] **Step 5: Point the bundle at the package**

`web/package.json` drops `@bptlab/openbpt-modeler-petri-net` and the dependencies that moved, and adds `"petrinet-js": "file:../modeler"`. `web/src/index.js` imports the modeler and the two PNML functions from `petrinet-js`. In `web/build.mjs`, the stylesheet entry point becomes the package's `assets/pn-js.css`.

- [ ] **Step 6: Give the fork a demo that needs nothing else**

`modeler/demo/index.html` loads the modeler on an empty canvas with a file input for a `.pnml` file and a download button. `modeler/demo/serve.mjs` builds it with esbuild and serves it, so that `npm run demo` in `modeler/` shows a working Petri net editor without `web/`, without .NET and without AML.

- [ ] **Step 7: Run the suite**

Run: `cd web && npm install && npm run build && npm test`
Expected: `dist/ ready`, then all seven checks pass, `verify-bridge` among them, which is the one that catches a broken injector.

- [ ] **Step 8: Run the demo once**

Run: `cd modeler && npm install && npm run demo`
Expected: the page serves, a place and a transition can be drawn, a `.pnml` file opens.

- [ ] **Step 9: Correct the notices**

`THIRD-PARTY-NOTICES.md` lists the modeler as a bundled npm package. The entry becomes a fork in `modeler/`, keeping version, licence and the copyright line, and the dependencies that moved stay listed because the bundle still carries them.

- [ ] **Step 10: Commit**

```bash
git add modeler web/src web/build.mjs web/package.json web/package-lock.json THIRD-PARTY-NOTICES.md
git commit -m "refactor(modeler): fork the Petri net modeler as a package of its own"
```

---

### Task 2: Fold the run-time patches into the fork

**Files:**
- Modify: `modeler/src/modeling/UpdatePropertyHandler.js`, `modeler/src/modeling/UpdateLabelHandler.js`, `web/src/index.js`
- Test: `web/tools/verify-labels.mjs` (exists, must stay green)

**Interfaces:**
- Consumes: Task 1's `modeler/index.js`.
- Produces: nothing new; `web/src/index.js` loses the two patch blocks and keeps `createPetriNetModeler`.

- [ ] **Step 1: Run the label check to see it pass today**

Run: `cd web && npm run test:labels`
Expected: PASS. It passes because of the patch in `src/index.js`; this records the behaviour the fork must keep.

- [ ] **Step 2: Declare `$inject` in the fork**

`UpdatePropertyHandler` gets `static $inject = ['eventBus']` in the class, as its sibling `UpdateLabelHandler` has. Remove the patch block from `web/src/index.js`.

- [ ] **Step 3: Scope the label default to arcs in the fork**

In `UpdateLabelHandler.postExecute`, the fallback text that is the default arc weight is applied only when the element is an arc; a place or a transition whose label was cleared keeps no name. Remove the patch block from `web/src/index.js`. Keep a comment naming the behaviour, because it looks like a bug when read without it.

- [ ] **Step 4: Run the checks**

Run: `cd web && npm run build && npm test`
Expected: all seven pass, `verify-labels` among them.

- [ ] **Step 5: Commit**

```bash
git add modeler/src/modeling web/src/index.js
git commit -m "fix(modeler): declare the missing injection and limit the label default to arcs"
```

---

### Task 3: Move the rendering changes into the fork

**Files:**
- Modify: `modeler/src/draw/CustomRenderer.js`, `web/src/index.js`
- Test: `web/tools/verify-tokens.mjs`, `web/tools/verify-svg.mjs` (exist, must stay green)

**Interfaces:**
- Produces: `CustomRenderer` draws transition names below the box and markings as dots; `web/src/index.js` keeps only `createPetriNetModeler` and the exports.

- [ ] **Step 1: Move the two drawing changes**

`nameTransitionsBelow` becomes part of `drawTransition`, `drawTokensAsDots` becomes `renderMarking` in `modeler/src/draw/CustomRenderer.js`, with the comments that say why. `MOST_DOTS` stays a constant. `web/src/index.js` keeps neither function nor the calls in `createPetriNetModeler`.

- [ ] **Step 2: Run the checks**

Run: `cd web && npm run build && npm test`
Expected: all seven pass, `verify-tokens` and `verify-svg` among them, with no change to their assertions.

- [ ] **Step 3: Commit**

```bash
git add modeler/src/draw web/src/index.js
git commit -m "refactor(modeler): draw transition names and tokens in the renderer"
```

---

### Task 4: Prepare the two fixes for upstream

**Files:**
- Create: `docs/upstream/0001-declare-inject-on-UpdatePropertyHandler.patch`, `docs/upstream/0002-keep-a-cleared-node-name-empty.patch`, `docs/upstream/README.md`

**Interfaces:**
- Consumes: the fork's versions of the two files from Tasks 2 and 3.

- [ ] **Step 1: Write the patches against the published package**

Each patch changes one file of `bptlab/openbpt-modeler-petri-net` and nothing else. They carry no reference to this repository or to AML.

- [ ] **Step 2: Check that they apply**

Run: `git clone --depth 1 https://github.com/bptlab/openbpt-modeler-petri-net <tmp> && cd <tmp> && git apply --check <repo>/docs/upstream/*.patch`
Expected: no output, which means both apply.

- [ ] **Step 3: Write `docs/upstream/README.md`**

One paragraph per patch: the symptom, the cause, the fix, and how to reproduce it.

- [ ] **Step 4: Commit**

```bash
git add docs/upstream
git commit -m "docs: prepare the two modeler fixes for upstream"
```

---

### Task 5: The net model, enabling and firing

**Files:**
- Create: `modeler/src/net/model.js`, `modeler/src/net/model.test.js`, `modeler/src/net/fixtures.js`
- Modify: `web/package.json`

**Interfaces:**
- Produces:
  - `readNet(data)` returns `{ places, transitions, arcs }` after checking it, or throws `Error` naming the first problem. `data` is `{ places: [{ id, name, marking }], transitions: [{ id, name }], arcs: [{ id, source, target, weight }] }`. A missing `marking` is 0, a missing `weight` is 1.
  - `initialMarking(net)` returns a `Map` from place id to token count.
  - `enabled(net, marking)` returns the ids of the transitions that can fire, in the order the transitions appear.
  - `fire(net, marking, transitionId)` returns a new marking; throws when the transition is not enabled.
  - `unfire(net, marking, transitionId)` returns the marking before that firing; throws when it could not have fired into this marking.
  - `markingKey(marking)` returns a string that is equal for equal markings, for use as a map key.
  - `fixtures.js` exports `loop`, `conflict`, `producer`, `deadlock`, `qualityCheck`, the five nets of the spec, as plain data.

- [ ] **Step 1: Write the failing tests**

```js
test('a transition is enabled when every input place holds its arc weight', ...)
test('firing moves tokens by the arc weights', ...)
test('a self-loop leaves its place unchanged', ...)       // Review Focus
test('an arc weight of zero or less is rejected', ...)    // Review Focus
test('two elements with the same id are rejected', ...)   // Review Focus
test('unfire returns the marking that firing came from', ...)
test('unfire rejects a marking that cannot have come from this firing', ...)
test('equal markings have the same key regardless of insertion order', ...)
```

Each uses a fixture or a net written in the test, with the expected markings written out.

- [ ] **Step 2: Run them to see them fail**

Run: `cd modeler && node --test src/net/`
Expected: FAIL, `readNet is not a function` or similar.

- [ ] **Step 3: Implement `modeler/src/net/model.js`**

Markings are `Map<string, number>` and are never mutated in place: `fire` and `unfire` return new maps.

- [ ] **Step 4: Run them to see them pass**

Run: `cd modeler && node --test src/net/`
Expected: PASS for every test.

- [ ] **Step 5: Add the script**

`modeler/package.json` gets `"test": "node --test src/net/"`. `web/package.json` gets `"test:net": "npm --prefix ../modeler test"` and runs it first, before the browser checks, so one command in `web/` still proves everything.

- [ ] **Step 6: Commit**

```bash
git add modeler/src/net modeler/package.json web/package.json
git commit -m "feat(net): add the net model with enabling and firing"
```

---

### Task 6: Exploring the state space

**Files:**
- Create: `modeler/src/net/explore.js`, `modeler/src/net/explore.test.js`

**Interfaces:**
- Consumes: Task 5's `readNet`, `initialMarking`, `enabled`, `fire`, `markingKey`.
- Produces: `explore(net, limits = { markings: 20000, milliseconds: 2000 })` returns
  `{ markings: [{ key, marking }], edges: [{ from, to, transition }], complete: boolean, stopped: 'markings' | 'time' | null, unbounded: [placeId] }`.
  A place is reported in `unbounded` when a reachable marking covers an earlier one on the path and holds strictly more tokens there, which is the coverability criterion; the exploration then records omega for that place instead of growing.

- [ ] **Step 1: Write the failing tests**

```js
test('a loop has as many markings as it has states', ...)          // loop fixture
test('a conflict branches into both successors', ...)              // conflict fixture
test('a producer without a bound reports its place as unbounded and stays finite', ...) // producer fixture
test('a transition without an input place stops at the limit', ...)  // Review Focus
test('the limit is reported as the reason it stopped', ...)
```

- [ ] **Step 2: Run them to see them fail**

Run: `cd modeler && node --test src/net/explore.test.js`
Expected: FAIL.

- [ ] **Step 3: Implement `modeler/src/net/explore.js`**

Breadth first from the initial marking, markings kept in a map by `markingKey`, the limits checked on every step so that an unbounded net returns rather than hangs.

- [ ] **Step 4: Run them to see them pass**

Run: `cd modeler && node --test src/net/`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add modeler/src/net/explore.js modeler/src/net/explore.test.js
git commit -m "feat(net): explore the state space within limits"
```

---

### Task 7: The findings

**Files:**
- Create: `modeler/src/net/analyse.js`, `modeler/src/net/analyse.test.js`

**Interfaces:**
- Consumes: Tasks 5 and 6.
- Produces: `analyse(net, limits)` returns `{ findings: [{ rule, severity, element, message }], complete: boolean }`.
  `rule` is one of `PT12` unbounded place, `PT13` bound of a place, `PT14` dead transition, `PT15` deadlock, `PT16` not live, `PT17` exploration cut off. `severity` is `error`, `warning` or `information`, as the design's table says. `element` is the id of the place or transition the finding belongs to, or `null` for a finding about the net.

- [ ] **Step 1: Write the failing tests**

```js
test('the quality check net has no warning', ...)                  // qualityCheck fixture
test('a deadlocking net reports the marking that deadlocks', ...)  // deadlock fixture
test('a transition that can never fire is reported as dead', ...)
test('an unbounded place is reported, and bounded places carry their bound', ...)
test('a cut off exploration still reports the findings it reached', ...) // Review Focus
```

- [ ] **Step 2: Run them to see them fail**

Run: `cd modeler && node --test src/net/analyse.test.js`
Expected: FAIL.

- [ ] **Step 3: Implement `modeler/src/net/analyse.js`**

One exploration, then the findings read off it. Rule numbers continue the structural rules PT01 to PT11 of `PtValidator`, so that one findings list can hold both.

- [ ] **Step 4: Run them to see them pass**

Run: `cd modeler && node --test src/net/`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add modeler/src/net/analyse.js modeler/src/net/analyse.test.js
git commit -m "feat(net): report behavioural findings"
```

---

### Task 8: Invariants

**Files:**
- Create: `modeler/src/net/invariants.js`, `modeler/src/net/invariants.test.js`
- Modify: `modeler/src/net/analyse.js`, `modeler/src/net/analyse.test.js`

**Interfaces:**
- Consumes: Task 5's net.
- Produces: `placeInvariants(net)` and `transitionInvariants(net)`, each returning an array of `Map` from element id to a positive integer weight, the minimal non-negative solutions of the incidence matrix. `analyse` adds them as findings `PT18` place invariant and `PT19` transition invariant.

- [ ] **Step 1: Write the failing tests**

```js
test('the loop has one place invariant that holds its token count', ...)
test('the quality check net has the invariants computed by hand', ...)
test('a net without an invariant returns none', ...)
```

The expected invariants are written out in the test, computed by hand from the incidence matrix.

- [ ] **Step 2: Run them to see them fail**

Run: `cd modeler && node --test src/net/invariants.test.js`
Expected: FAIL.

- [ ] **Step 3: Implement `modeler/src/net/invariants.js`**

Fourier-Motzkin elimination over the incidence matrix with integer arithmetic, minimal support solutions only, so that the result stays readable for a reader of the findings.

- [ ] **Step 4: Run the whole core**

Run: `cd modeler && node --test src/net/`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add modeler/src/net
git commit -m "feat(net): compute place and transition invariants"
```

---

### Task 9: The core runs in CI

**Files:**
- Modify: `.github/workflows/ci.yml`, `README.md`

**Interfaces:**
- Consumes: the `test:net` script from Task 5.

- [ ] **Step 1: Run the core in the workflow**

`npm test` in `web/` already runs it after Task 5, so the workflow needs no new step; check that the run shows the core tests and that the step name still describes what it does.

- [ ] **Step 2: Describe the core in the README**

The repository table gains `modeler/src/net/` as the analysis core and `modeler/src/` as the forked modeler, and the modeler paragraph in the licence section says fork instead of package.

- [ ] **Step 3: Run everything once more**

Run: `cd web && npm run build && npm test`, then `dotnet test dotnet/PtMapper.Tests -c Release`, then `node tools/validate-pnml.mjs ../examples/interop/*.pnml ../examples/quality-gate.pnml ../examples/paper/MPS500_PT.pnml`
Expected: core tests pass, seven browser checks pass, 226 mapper tests pass, eight PNML files valid.

- [ ] **Step 4: Check that nothing else moved**

Run: `git status --short`
Expected: no changed example file, no changed AML or PNML.

- [ ] **Step 5: Commit**

```bash
git add README.md .github/workflows/ci.yml
git commit -m "docs: describe the modeler fork and the analysis core"
```
