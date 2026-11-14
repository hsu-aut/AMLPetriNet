# Plan: net classes, Commoner's theorem and workflow net soundness

Covers the backlog entries "Workflow net soundness" and "Net classes and
Commoner's theorem". Status: planned, not built. Line numbers are from
2026-11-14 and refer to PetriNet.js 0.3.0 unless a path says otherwise.

**Goal:** Analyse names the subclass the net belongs to. Where a theorem covers
that subclass, it proves liveness and boundedness from the structure, whatever the
size of the state space. For a workflow net it decides soundness. A proven
statement is marked as proven. It never contradicts a complete search, and tests
check that.

**Not in scope:** new kinds of net. This plan only recognises subclasses of
place/transition nets (design section 1 still holds). No new runtime dependency.
`PtValidator` does not change.

## 1. What exists

- `src/net/structure.js` 27-34 sets the siphon and trap limits: 40 places, 60 sets,
  20000 steps. Above 40 places nothing is searched (110-113). The enumeration
  (122-141) finds every minimal siphon unless a cap stops it. The `sets` cap counts
  every set found, including non-minimal ones (131-132, filtered only at 148), so it
  can stop before 60 minimal siphons are found.
- `analyse.js` 132-135 computes all minimal siphons and traps but lists only 8 (37).
  PT20 (144-153) tests "siphon without a marked trap" with `maximalTrap`; its
  comment says the tool does not know whether the net is free-choice. The "more than
  40 places" text is hard-coded (166).
- Tarjan's algorithm exists only for the reachability graph (analyse.js 241-301).
  `notLive` (213-239) is exact on a complete search.
- `model.js` 86-96 adds up parallel arcs, and `readNet` accepts them, so two
  parallel arcs of weight 1 act as one arc of weight 2. "Ordinary" must be checked on
  the summed weights.
- `explore.js` 42 always starts from the drawn marking; a search from another
  marking needs a copy of the net. A search that finishes without omega is exact.
- `invariants.js` 41-73 (Farkas) has no cap and already runs on every analysis.
- A finding is `{rule, severity, element, message}` with optional extras `marking`,
  `cover`, `firings`. `Findings.wayTo` looks up `marking` in the main exploration
  only. Host findings lose their extras. Panel rows: mark, rule, message; actions
  come from the extras. The CSV has 4 columns.
- AMLPetriNet: ptmap passes any rule through (AnalyzeCommand.cs 213-217) but keeps
  only the known extras (AnalysisReport.cs 25-34). The text output hides information
  findings without `--all`.
- Checked by hand: the quality gate is not ordinary (an arc of weight 2); MPS500 and
  the `qualityCheck` and `resourceDeadlock` fixtures are asymmetric choice, not
  extended free-choice; `loop` is a state machine and a marked graph; `conflict` is a
  state machine, not strongly connected; the `deadlock` fixture is a workflow net
  (In to Out), drawn with one token in In, and has an arc of weight 2.

## 2. Definitions used

Murata (1989, Proc. IEEE, section VI), for ordinary nets, i.e. every summed weight is 1:

- **SM** (state machine): every transition has exactly one input and one output place.
- **MG** (marked graph): every place has exactly one input and one output transition.
- **FC** (free-choice): for every place p, either p has at most one output
  transition, or p is the only input of every transition it feeds.
- **EFC** (extended free-choice): places that share an output transition have the
  same output transitions.
- **AC** (asymmetric choice): if two places share an output transition, the output
  transitions of one include those of the other.

Hierarchy: SM, MG are inside FC, inside EFC, inside AC. Desel and Esparza (1995)
call EFC "free-choice"; the theorems below hold for EFC.

| Statement | Applies to | Source |
|---|---|---|
| Live iff strongly connected and holding a token | SM | Desel/Esparza ch. 3 |
| Live iff every circuit holds a token | MG | Murata VI-A |
| Live iff every minimal siphon contains a marked trap (Commoner) | EFC | Desel/Esparza ch. 4 |
| Siphon-trap property implies live (one direction only) | AC | Murata VI |
| Siphon-trap property implies no deadlock | ordinary nets without source transitions | standard |
| Every place in some semi-positive place invariant implies bounded | any net, weights allowed | Murata |
| Live and bounded iff covered by S-components (Hack) | live EFC | Desel/Esparza ch. 5 |
| Sound iff the short-circuited net is live and bounded | workflow nets | van der Aalst 1997 |

**S-components:** in an ordinary net, an S-component's places are the support of one
of the minimal invariants Farkas returns, when its support generates a strongly
connected subnet in which every adjacent transition has exactly one input and one
output place in the support. No new search is needed.

**Soundness of a free-choice workflow net:** closing the net with t*: o to i keeps it
free-choice, so it is sound iff the closed net has the siphon-trap property from [i]
and is covered by S-components.

## 3. Findings

| Rule | Name | Severity | Points at |
|---|---|---|---|
| PT22 | Net class | information | `cover` with the witness; `element` is the arc for "not ordinary" or the off-path node |
| PT23 | Liveness from the structure | information | `cover`: the siphon, the unmarked circuit, or the unreachable part |
| PT24 | Boundedness from the structure | information if bounded, warning if unbounded (as PT12) | `cover`: the places outside every S-component |
| PT25 | Workflow net soundness | information if sound or undecided, warning if not sound | `marking` (way there), `cover`: dead transitions and growing places |

- A finding resting on a theorem gets an extra field `proof`: `state-machine`,
  `marked-graph`, `commoner`, `siphon-trap`, `place-invariants`, `hack`,
  `free-choice-workflow`. The panel shows a proof tag with a tooltip naming the
  theorem and source. The message starts with "Proven from the structure (...)", so
  CSV, ptmap and any host see the difference without a new column.
- PT12 to PT21 keep their messages. Each net gets one PT22; a net without places or
  transitions gets none. PT22 to PT24 are added before the PT12 loop.

Messages are written for people who know automation, not Petri net theory, at most
three sentences, the theorem named in parentheses. Examples:

- PT22: "Net class: free-choice. Where two transitions compete for a token, each needs
  only that token, so a decision is never held up by tokens elsewhere. Liveness
  follows from the structure for such nets (PT23)."
- PT22: "Net class: not determined. The arc from Scrapped to Rework carries 2 tokens;
  the classes and their theorems are about nets in which every arc carries one. The
  search answers instead."
- PT23: "Proven from the structure (Commoner's theorem): the net is not live. R1 and R2
  can run empty and then stay empty, and nothing inside them keeps a token, so the
  transitions that need them stop for good. This holds however large the state space is."
- PT25: "Not a sound workflow net: from the marking (Mid 1) End can no longer be
  reached, and Pair can never fire. Started with one token in In, checked on all 2
  reachable markings."
- PT25: "Sound workflow net, proven from the structure: it is free-choice, and the net
  closed from End back to Start is live and bounded (Commoner's and Hack's theorems)."

## 4. Behaviour

- **The class is an information finding**, not a header field: the list is the only
  channel that reaches the panel, CSV, the bridge counts and ptmap without new
  protocol work. `analyse()` and `analyseData` also return `classes` (strings) and
  `workflow` (`{start, end, sound: true|false|null}` or null), both JSON-safe.
- **Theorem versus search.** When the search is complete, the two must agree.
  Search-live means: the search is whole, no PT14, no PT16. A disagreement is a bug
  and only the tests check for it. When the search is partial, the theorem is the
  only full answer, and the message says so.
- **Caps.** Each statement either has an answer or says it was not decided, naming
  the real limit (not the hard-coded 40). The 8 sets shown are never treated as the
  whole list. The structural check counts only minimal sets, or gets its own cap.
- **Workflow net.** Exactly one place without input arcs (i, not o), exactly one place
  without output arcs, and the net closed by t* strongly connected. A source
  transition breaks the last condition.
- **Nets that are not workflow nets** report nothing, except one hint: with exactly
  one start and one end place but a node off every path between them, PT22 names that
  node. Most nets here are cyclic and a line on each would be noise.
- **Soundness is checked from [i]** (one token in i). If the drawn marking is [i], the
  main exploration is reused; otherwise a second exploration runs on a copy with
  marking [i], and the message says so. `marking` is attached only when the drawn
  marking is [i]. A partial search can still prove "not sound": a marking in which
  nothing is enabled and which is not [o] (without omega), a marking with a token in o
  and tokens elsewhere, or omega. "Sound" needs a complete search or the free-choice
  proof. All reasons go into one PT25.
- **Workflow net drawn at [i]:** [o] is the proper end, not a deadlock, so PT15 at
  exactly [o] becomes information ("the process ends here, as it should"). PT23 about
  the open net is dropped, because a terminating net is never live; liveness of the
  closed net is what PT25 reports. PT16 stays.

## 5. Size

| Check | Cost | Limit |
|---|---|---|
| Classes, workflow structure, SM and MG theorems | linear in the arcs | none |
| Commoner, AC, siphon-trap, free-choice soundness | minimal siphons | `limits.structure` (40 places, 60 sets, 20000 steps) |
| Invariant cover, Hack | Farkas, already run | none (existing risk) |
| Soundness by search | one or two explorations | 20000 markings; 2 s in the panel, none in ptmap |

Liveness of free-choice systems is co-NP-complete, so the cap reflects the problem.

## 6. Files (PetriNet.js)

- New `src/net/graph.js`: Tarjan for any graph, moved from `analyse.js`, and a
  cycle-among-a-subset helper.
- New `src/net/classes.js`: `ordinary`, `netClasses(net)` with flags and a witness,
  `workflowNet(net)`.
- New `src/net/theorems.js`: `livenessFromStructure`, `boundednessFromStructure`.
- New `src/net/workflow.js`: `shortCircuit(net, wf)`, `soundness(net, wf, limits, result)`.
- `analyse.js`: RULES gains PT22 to PT25 and the findings are emitted. `plain.js` and
  `net.js` gain `netClasses` and `soundness`.
- `FindingsPanel.js`: the proof tag and the rule name from `RULES` as tooltip;
  `assets/simulation.css`: one style.

## 7. Stages

PetriNet.js (each ends with `cd PetriNet.js && npm test`):

1. **Classes (PT22).** `graph.js`, `classes.js`, fixtures for each class and for
   counter-examples.
2. **Liveness from the structure (PT23).** `theorems.js` and the differential test.
3. **Boundedness from the structure (PT24).** Invariant cover and Hack.
4. **Workflow soundness (PT25).** With the PT15 change at [o] and dropping PT23 for
   workflow nets.
5. **Panel, entry, documents, 0.4.0 (or the next free minor).** `verify-classes.mjs`;
   `verify-findings.mjs` expects one more warning (the `deadlock` net is an unsound
   workflow net); README section; CHANGELOG, package.json and CITATION.cff. Proof:
   `npm test && node test/browser/run.mjs`. The maintainer tags.

AMLPetriNet (on a branch until the tag is pushed):

6. **Follow the new core.** Pin the tag. `verify-analysis.mjs`: a workflow net
   returns PT25 and `classes`. C#: pass `proof` through in `AnalysisReport.cs`, add
   `classes` and `workflow` to `NetReport`, and show the class and the soundness
   verdict in the net line of `FindingsText.cs`. `AnalyzeTests`: the information count
   of the quality gate changes (PT22 "not ordinary" and PT24 by invariants; worked out
   by hand). New end-to-end tests: an unsound workflow PNML exits 5; a sound one exits
   0. Update README, design section 4.2, the backlog. Proof: `cd web && npm ci &&
   npm run build && npm test`, then `PTMAP_REQUIRE_NODE=1 dotnet test
   dotnet/PtMapper.Tests --filter Analyze`.

`PtValidator` and `PtSeverity` stay unchanged: they check conformance to ISO/IEC
15909-1, and a net class is not a violation. PT09 (a source transition) still warns
on its own.

## 8. Tests

- **Classes:** one net per class and one counter-example per boundary. `loop` is SM
  and MG; `conflict` is SM and FC, not MG; a fork/join cycle is MG, not SM; a
  two-place shared choice (p1 and p2 both feed t1 and t2) is EFC, not FC;
  `resourceDeadlock` and `qualityCheck` are AC, not EFC; dining philosophers taking
  both forks at once is not AC; the quality gate and two parallel weight-1 arcs are
  not ordinary.
- **Theorems with known answers:** `loop` is live, and with no token it is not;
  `conflict` is not live; a fork/join cycle with a token is live, a circuit without a
  token is not; `producer` has no siphon, so Commoner says live and Hack says
  unbounded, matching PT12; `resourceDeadlock`: the structure stays silent and the
  search finds the deadlock.
- **Workflow nets** (van der Aalst patterns): sound are sequence, AND split/join, XOR
  split/join; not sound are AND split into XOR join (two tokens in o), XOR split into
  AND join (stuck, dead join), a dead transition, a loop that makes tokens; a
  non-free-choice sound net (structure silent, search says sound); the `deadlock`
  fixture is not sound by search because its weight 2 rules out the structural proof;
  a drawn marking other than [i] is still checked, with no `marking` attached; the
  near-miss hint, and a cyclic net with no PT25.
- **Differential test** (a contradiction is a failure): a seeded generator of ordinary
  nets (2 to 5 places, 1 to 5 transitions, markings 0 to 2) and of workflow nets built
  from series, parallel and choice blocks with random extra arcs. Where both sides
  are complete it asserts: the class hierarchy holds; every theorem verdict equals the
  search verdict; "bounded by invariants" implies no omega; soundness by search equals
  "closed net live and bounded"; for free-choice workflow nets the structural verdict
  equals the search verdict. Minimum counts of exercised cases stop it passing
  vacuously.
- **Existing tests that change:** `analyse.test.js` (the rule pattern must reach
  PT25), `plain.test.js` (new fields survive JSON), `entry.test.js`.

## 9. Risks

- **A theorem applied outside its preconditions** (a false proof) is the worst case;
  the differential test and the ordinary check on summed weights guard against it.
- Two meanings of "free-choice" (Murata versus Desel/Esparza): the message names the
  class and the theorem.
- More "not decided" answers than expected because the `sets` cap counts non-minimal
  sets; see section 4.
- Two explorations when the drawn marking is not [i], up to twice the panel's 2 s.
- Farkas without a cap runs once more on the closed net.
- Visible changes: more information rows; PT15 at [o] is no longer a warning, so ptmap
  goes from exit 5 to 0 for sound workflow nets; an unsound net with a livelock and no
  deadlock now exits 5. These go in the CHANGELOG.

## 9a. Decided (2026-11-14)

The author left the questions below open and asked for the recommended answers:
1. The class is one information row in the list.
2. Soundness is checked from one token in the start place even if the net is drawn
   otherwise, and the message says so; "Way there" only when the drawn marking is [i].
3. The proper end [o] of a workflow net drawn at [i] is information, not a warning.
4. "Not sound" is a warning and counts toward ptmap exit 5.
5. Nets that are not workflow nets get nothing except the near-miss hint.
6. "Proven not live" (PT23) is information, like PT16.
7. PT23 about the open workflow net is dropped in favour of PT25.
8. The boundedness proof from place invariants is also given for nets with weights
   above 1.
9. The CSV gets no column for proof versus search; the message says it.

## 10. Open questions for the author

1. Class as one information row in the list (recommended), or a line in the panel
   heading?
2. Soundness checked from one token in the start place even when the net is drawn
   otherwise? Recommended: yes, and say so.
3. Should the proper end [o] still be a deadlock warning? Recommended: no.
4. Should "not sound" count toward ptmap exit 5? Recommended: yes.
5. For nets that are not workflow nets, say nothing except the near-miss hint?
   Recommended: yes.
6. Should "proven not live" be information like PT16 (recommended), or a warning?
7. Should PT23 about the open workflow net be dropped in favour of PT25?
   Recommended: yes.
8. Should the boundedness proof from place invariants also be given for nets with
   weights above 1? Recommended: yes.
9. Should the CSV get a column for proof versus search? Recommended: no.
