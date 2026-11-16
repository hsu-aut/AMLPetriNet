# Ideas

What came up while building and reviewing, and is not on the backlog yet. Nothing
here is decided. Each entry says what it is for and roughly what it costs. An idea
that is taken up gets a plan in `docs/` first, then a block in `backlog.md`.

## The modeler (PetriNet.js)

1. **Take a marking of the state space as the new initial marking.** A node of the
   state space view, or the end of a run, becomes the marking the net is drawn with,
   as one undoable command. Today the only ways to get a marking onto the net are
   typing it and playing a run, which does not change the document. Small.
2. **Search further.** When a search stopped at its limit, a button that runs it
   again with ten times the limits, now that the page no longer freezes. Small, and
   it settles the question of the 2 s limit without changing the default.
3. **Say why.** For a finding such as "not live" or "not sound", a counter-run that
   can be played: the way into the marking from which the transition can never fire
   again. "Way there" exists for deadlocks only. Medium.
4. **Classic nets as examples and as tests**: producer and consumer, dining
   philosophers, readers and writers, a sound and an unsound workflow net, each with
   the findings it should give. They serve the start card of the web application,
   the documentation and the differential tests. Small to medium.
5. **Liveness of free-choice nets without siphons.** Live and bounded is decidable in
   polynomial time for them (the rank theorem), which would lift the limit of 40
   places for this class. Medium, and mathematical: it needs the same care as the
   proofs in `plan-net-classes.md`.
6. **Progress for the structural part of Analyse** (siphons, invariants): the bar
   stands still while it works, and the invariants have no cap. Small.
7. **One search for Analyse and the state space**, which explore the same net twice
   today. Small, and invisible except in time.

## The tools around it (AMLPetriNet)

8. **`ptmap explore`**: the state space as DOT or CSV from the command line, for
   papers and for CI. Small.
9. **Machine formats for `ptmap analyze`**: SARIF, which GitHub shows as code
   scanning alerts, and JUnit XML for CI dashboards. Small to medium.
10. **Write the result of an analysis into the document**, on request and not by
    itself: the class, boundedness and liveness as attributes of the PT_Net element,
    so that they are visible in the AML tree and to other tools. This is the one that
    touches the rule "an analysis never writes", so it needs the author's decision
    on behaviour first. Medium.
11. **The plugin menu: "State space" and "Properties"** beside Simulate and
    Analyse. Decided against for now in the menu plan; cheap to add.
12. **`petrinetjs analyze`** for PNML files without .NET (option d in the plan for
    `ptmap analyze`). Needs a DOM for Node. Medium.

## Not ours

- **FPB.JS**: its properties panel should be tried with the import-while-typing case
  (see `lessons-for-fpbjs.md`).
- **CITATION.cff**: the author's ORCID and the paper to cite.
