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
  name was called "1". Both are offered back to the original.
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
  net can reach, the firings between them, and a click to put the net into one
  of them.

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
modeler.get('findings').analyse();          // boundedness, deadlocks, invariants
modeler.get('findings').setHostFindings([]) // findings of your own, in the same list
modeler.get('reachabilityView').show();     // the state space beside the net
```

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
