# Backlog

What is open on the Petri net modeler (PetriNet.js) and on AMLPetriNet, in the
order the work is done. A box is ticked when the work is finished, tested and
committed. Publishing, tagging and deploying are done by the maintainer.

## Block A: tidy up AMLPetriNet

- [x] A1. README: a section for plugin users who do not build the plugin (take
  the nupkg from the GitHub release, add the folder as a source in the PlugIn
  Manager, install).
- [x] A2. A screenshot for the README: a real net (`examples/paper`) with the
  findings panel open, taken with Playwright, in `docs/`, about 150 KB at most.
- [x] A3. Pointers to AMLPetriNet in the READMEs of fpb-aml-editor-plugin
  (AMLFPB.js) and fpb-aml-mapper. Committed locally only.
- [x] A4. CI: a time limit for the PNML grammar step (`timeout-minutes: 5`) and
  for the job.

## Block B: one list of findings instead of two

The plugin shows the structural findings PT01 to PT11 in its WPF table, the
behavioural findings PT12 to PT21 are in the panel on the canvas.
`Findings.setHostFindings` was built for this and is not called. The goal is
that all findings appear in the panel.

- [x] B1. A bridge message from the host to the page carrying the structural
  findings (rule, severity, element as PNML id, message). The plugin sends it
  after each check of its own, the web application likewise.
- [x] B2. An edit drops the behavioural findings as before; the host's findings
  stay until the host sends new ones. A finding about a deleted element is not a
  link.
- [x] B3. With the panel closed, the Analyse button shows the host's number of
  errors and warnings.
- [x] B4. The WPF table stays for now: it also carries read warnings and the
  conformance report.
- [x] B5. Tests: node tests for Findings, `verify-findings.mjs` adapted, a bridge
  test for the message, a plugin test for the message.
- [ ] B6. (Built and committed in PetriNet.js as 0.2.0; the tag and the lock file wait for the push.) PetriNet.js v0.2.0 (behaviour change, CHANGELOG). AMLPetriNet follows
  on a branch until the tag is pushed; the lock file is renewed only then.

## Block C: prepare the release

- [x] C1. Plugin 0.1.18 (csproj and Metadata.xml), built, nupkg in the plugin
  manager's source folder. Done 2026-11-14, commit 91a47ce.
- [x] C2. A release note that sums up what the plugin gained since 0.1.15.
- [x] C3. Redeploy of aml.fpbjs.net/pt/ (Azure `pt-aml-mapper`). Done
  2026-11-14, checked against the live URL.

## Block D: browser tests move to PetriNet.js

- [x] D1. The checks that only test the modeler (simulation, findings, states,
  paths, invariants, steps, export, arrange, labels, tokens, pages, roundtrip)
  move to PetriNet.js, with a test page of their own and into its CI. Bridge,
  interop, SVG and web application stay here. The check of coordinate numbers
  (`verify-numbers`) moved with them, since it tests the modeler alone.

## Block E: the Net menu

- [x] E1. PetriNet.js 0.2.1: the play clock stops with the run (tagged v0.2.1).
- [x] E2. The page answers `simulate` and `analyse` (`web/src/bridge.js`, checks in
  `verify-bridge.mjs`).
- [x] E3. The plugin's Net menu has Simulate and Analyse (`NetMenuState`, tests).
  Merged into `main`; the lock file points at PetriNet.js v0.2.1.
- [ ] E5. Export SVG during a run shows the document's marking (decision 8a.2 of
  the menu plan); needs a change in PetriNet.js.
- [ ] E4. Tried in the real AML Editor; version, release note and tag.

## Block F: `ptmap analyze`

Plan and decisions: `plan-ptmap-analyze.md`.

- [x] F1. PetriNet.js 0.3.0: `net.js` and `analyseData` (tagged v0.3.0).
- [x] F2. The analysis bundle (`web/src/analysis.js`, `dist-node/`, `verify-analysis.mjs`).
- [x] F3. `ptmap analyze` with text and JSON (`dotnet/PtMapper.Tool/Analysis/`, tests
  in `AnalyzeTests.cs`). The lock file points at PetriNet.js v0.3.0.
- [x] F4. Review done, merged; CI runs with `PTMAP_REQUIRE_NODE`.

## Not started, to be planned first

- Search in a Web Worker.
- Workflow net soundness.
- Net classes and Commoner's theorem.
- A properties panel.
- CITATION.cff.

## Later, by the author

- Publishing `petrinetjs` on npm. Not soon; until then the package is installed
  from a tag of the GitHub repository.
