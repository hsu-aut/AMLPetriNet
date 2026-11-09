# petrinet-js

A Petri net modeler for the browser: places, transitions, arcs with weights,
markings, PNML import and export. Built on
[diagram-js](https://github.com/bpmn-io/diagram-js).

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

## Use

```js
import { PnModeler } from 'petrinet-js';
import 'petrinet-js/assets/pn-js.css';

const modeler = new PnModeler({ container: document.querySelector('#canvas') });
await modeler.createNew();              // or: await modeler.importPNML(xml)
const pnml = await modeler.savePNML();
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
