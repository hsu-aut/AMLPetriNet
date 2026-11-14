# Fixes offered back to ptn-js

Two defects found while building on
[ptn-js](https://github.com/bptlab/openbpt-modeler-petri-net), each as a patch
against that repository and nothing else.

**Status: both merged.** Maximilian König merged them into the original on
2026-11-10, as [#9](https://github.com/bptlab/openbpt-modeler-petri-net/pull/9)
(commit `e309850`) and [#10](https://github.com/bptlab/openbpt-modeler-petri-net/pull/10)
(commit `21f0fd8`). No npm release carries them yet; the latest published
version is still 1.0.2. The patches stay here as the record of what was sent.

```bash
git clone https://github.com/bptlab/openbpt-modeler-petri-net
cd openbpt-modeler-petri-net
git am path/to/0001-*.patch path/to/0002-*.patch
```

## 0001: `UpdatePropertyHandler` declares no `$inject`

**Symptom.** A minified build of the modeler dies while the command stack
registers its handlers, with "No provider for ..." naming a single renamed
letter.

**Cause.** diagram-js resolves services through didi, which falls back to
reading a constructor's parameter names wherever a component does not declare
its dependencies. A minifier renames those parameters. `UpdatePropertyHandler`
is the only registered component without the annotation; its sibling
`UpdateLabelHandler` has it.

**Fix.** Declare `static $inject = ["eventBus"]`.

## 0002: clearing a name renames the element to "1"

**Symptom.** Draw a place, give it a name, clear the name. The place is then
called "1", and that name goes into the PNML export.

**Cause.** `UpdateLabelHandler.postExecute` calls `setText(element, "1")`
whenever a label becomes empty. The "1" is the default weight of an arc and is
meant for arcs, but the branch does not look at the element.

**Fix.** Fall back to "1" for an arc, and to no text for anything else.
