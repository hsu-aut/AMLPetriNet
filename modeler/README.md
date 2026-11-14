# petrinet-js

A Petri net modeler for the browser: places, transitions, arcs with weights,
markings, PNML import and export, and a simulation that fires the net on the
canvas. Built on [diagram-js](https://github.com/bpmn-io/diagram-js).

This is a fork of [ptn-js](https://github.com/bptlab/openbpt-modeler-petri-net)
(`@bptlab/openbpt-modeler-petri-net`) by Maximilian König and the Business
Process Technology group at the Hasso Plattner Institute, MIT licensed. The
original is the Petri net modeler of the OpenBPT platform. Everything that made
it work is theirs; the changes below are this fork's.

## What this fork changes

- **Its own PNML reader and writer** (`src/pnml/`). The upstream converter
  builds XML with xmlbuilder2, which needs Node builtins and cannot be bundled
  for a browser, and it drops node sizes and every bend point. This one is DOM
  based and writes the graphics elements ISO/IEC 15909-2 provides for, so a
  round trip keeps the drawing.
- **Two defects fixed.** `UpdatePropertyHandler` declared no `$inject`, which
  kills a minified build at start-up. `UpdateLabelHandler` wrote the text "1",
  the default arc weight, whenever any label was cleared, so a place without a
  name was called "1". Both are fixed in the original as well
  ([#9](https://github.com/bptlab/openbpt-modeler-petri-net/pull/9),
  [#10](https://github.com/bptlab/openbpt-modeler-petri-net/pull/10)).
- **Transition names below the box** instead of inside it, where a name longer
  than the box was broken mid-word.
- **Markings as dots**, up to five, a number above that, which is how Petri nets
  are drawn.
- **Unused code removed**: the file and HTML helpers, which nothing imported and
  which were the only reason the package depended on jQuery.
- **An analysis core** (`src/net/`) that knows nothing about the canvas: the
  firing rule, the reachable markings within a limit, boundedness, deadlocks,
  dead transitions, liveness and place and transition invariants.
- **Its own simulation** (`src/simulation/`) in place of the token replay of the
  original, which wrote the simulated marking into the model, kept no history
  and took the undo stack for its own. This one holds the marking beside the
  model, so a document saved during a run is the net the user drew.
- **A findings list** (`src/findings/`) that says what the net does and leads to
  the element a finding is about. A host can add its own findings to it.
- **The state space** (`src/reachability/`) as a second view: every marking the
  net can reach, the firings between them, and a click that walks the net into
  one of them. A query line answers whether the net can reach a marking the
  reader has in mind, with the way there.
- **The way there, not just the fact.** A finding about a deadlock hands over
  the firings that lead into it, and they are loaded into the simulation to be
  stepped through or played (`src/net/paths.js`).
- **Invariants in the drawing** (`src/highlight/`): the places a conservation
  law covers are ringed, each with the weight it counts for, and a transition
  invariant is loaded as a firing sequence that returns the marking.
- **Siphons and traps** (`src/net/structure.js`): why a net can get stuck, from
  the structure, so the answer holds on nets whose state space is too large to
  build.
- **Steps and moving tokens**: one click fires everything that can fire at once,
  with a speed and with the tokens running along the arcs the firing used.
- **Answers as files** (`src/net/report.js`): the state space as DOT for a graph
  tool, the markings and the findings as tables.
- **Tidying up** (`src/arrange/`, `src/layout/`): a layered layout for the whole
  net in one undoable step, and aligning and spreading for a selection.
- **A PNML file from another tool comes back as it went in**: pages with their
  names and nesting, reference nodes, and the tool-specific sections of other
  tools are carried through, although the canvas itself has one plane and no
  pages.

## Use

```js
import { PnModeler } from 'petrinet-js';
import 'petrinet-js/assets/pn-js.css';

const modeler = new PnModeler({ container: document.querySelector('#canvas') });
await modeler.createNew();              // or: await modeler.importPNML(xml)
const pnml = await modeler.savePNML();
```

## Simulation

```js
const simulation = modeler.get('simulation');
simulation.start();            // reads the net from the canvas
simulation.fire(transitionId); // or click an enabled transition
simulation.stepBack();
simulation.stepForward();      // does the firing taken back again
simulation.reset();
simulation.stop();
```

The bar on the canvas does the same. While a simulation runs the net cannot be
edited: the rules refuse it. A change that does not pass the rules, an undo or
an import, ends the run rather than leaving a marking on a net that is gone.
`simulation.changed` says what the net is doing.

The state space view draws up to 300 markings; a larger one is reported in
numbers instead, and `findings.analyse()` answers for any size.

```js
modeler.get('findings').analyse();           // boundedness, deadlocks, invariants, siphons
modeler.get('findings').setHostFindings([]); // findings of your own, in the same list
modeler.get('findings').wayTo(finding);      // the firings that lead to what it is about
modeler.get('reachabilityView').show();      // the state space beside the net
modeler.get('reachabilityView').ask('p1 = 2, p2 = 0');  // can it reach that?
modeler.get('highlight').show([['p1', 2], ['p2', 1]]);  // ring these, with weights
modeler.get('arrange').tidy();               // lay the net out by its flow
modeler.get('simulation').fireStep();        // everything that can fire at once
```

A file the tool writes is offered on the event bus first, as `export.file` with
`{ name, text, type }`; a listener that returns `true` has saved it and no
download happens. The same holds for `selection.changed`, which a host can use
to point at the same element in its own view.

## Demo

```bash
npm install
npm run demo
```

Serves a page with the modeler, a file input for a `.pnml` file and a download
button, on http://127.0.0.1:5180.

## Tests

```bash
npm test
```

## License

MIT, see [LICENSE](LICENSE). The copyright of the original authors stands; this
fork keeps it.
