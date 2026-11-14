# Backlog

What is open on the Petri net modeler (PetriNet.js) and on AMLPetriNet, in the
order the work is done. A box is ticked when the work is finished, tested and
committed. Nothing here is pushed, tagged or published by the work itself; the
commands for that are handed over at the end.

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

- [ ] B1. A bridge message from the host to the page carrying the structural
  findings (rule, severity, element as PNML id, message). The plugin sends it
  after each check of its own, the web application likewise.
- [ ] B2. An edit drops the behavioural findings as before; the host's findings
  stay until the host sends new ones. A finding about a deleted element is not a
  link.
- [ ] B3. With the panel closed, the Analyse button shows the host's number of
  errors and warnings.
- [ ] B4. The WPF table stays for now: it also carries read warnings and the
  conformance report.
- [ ] B5. Tests: node tests for Findings, `verify-findings.mjs` adapted, a bridge
  test for the message, a plugin test for the message.
- [ ] B6. PetriNet.js v0.2.0 (behaviour change, CHANGELOG). AMLPetriNet follows
  on a branch until the tag is pushed; the lock file is renewed only then.

## Block C: prepare the release

- [x] C1. Plugin 0.1.18 (csproj and Metadata.xml), built, nupkg in the plugin
  manager's source folder. Done 2026-11-14, commit 91a47ce.
- [ ] C2. A release note that sums up what the plugin gained since 0.1.15.
- [x] C3. Redeploy of aml.fpbjs.net/pt/ (Azure `pt-aml-mapper`). Done
  2026-11-14, checked against the live URL.

## Block D: browser tests move to PetriNet.js

- [ ] D1. The checks that only test the modeler (simulation, findings, states,
  paths, invariants, steps, export, arrange, labels, tokens, pages, roundtrip)
  move to PetriNet.js, with a test page of their own and into its CI. Bridge,
  interop, SVG and web application stay here.

## Not started, to be planned with Opus first

- Search in a Web Worker.
- Workflow net soundness.
- Net classes and Commoner's theorem.
- A properties panel.
- Simulate and Analyse in the plugin menu.
- `ptmap analyze`.
- CITATION.cff.

## Later, by the author

- Publishing `petrinetjs` on npm. Not soon; until then the package is installed
  from a tag of the GitHub repository.
