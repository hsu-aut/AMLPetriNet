# AMLPetriNet

Place/transition Petri nets (ISO/IEC 15909) in AutomationML.

- Plugin for the AutomationML Editor to view and edit nets inside AML documents
- Mapper between PNML and CAEX 3.0, based on the `ISO_PT` domain library
- Command line tool and web application

Online: https://aml.fpbjs.net/pt/

The counterpart for the Formalised Process Description (VDI/VDE 3682) is
[AMLFPB.js](https://github.com/hsu-aut/AMLFPB.js).

## Repository

| Path | Content |
|---|---|
| `Aml.Editor.Plugin.PetriNet/` | AutomationML Editor plugin |
| `dotnet/PtMapper.Conversion/` | Mapper library |
| `dotnet/PtMapper.Tool/` | Command line tool `ptmap` |
| `dotnet/PtMapper.Web/` | Web application |
| `dotnet/PtMapper.Tests/` | Mapper tests |
| `modeler/` | The Petri net modeler as a package of its own: canvas, PNML reader and writer, simulation, analysis core, demo page |
| `web/` | The bundle for plugin and web application, host bridge, browser tests |
| `libraries/` | `ISO_PT` domain library and the libraries it references |
| `examples/` | Example documents, including the MPS500 quality check in `examples/paper/` |
| `docs/` | Round-trip validation and PNML conformance |

## Build

Requires the .NET 8 SDK and Node.js 20. The plugin needs Windows, the
AutomationML Editor 6.4 or later and the WebView2 runtime.

```bash
cd web
npm ci
npm run build
cd ..
dotnet build Aml.Editor.Plugin.PetriNet.sln
```

The modeler bundle is built first because the plugin and the web application
include it. `npm ci` in `web` also installs what `modeler/` needs. The plugin package ends up in
`build/Plugins/Aml.Editor.Plugin.PetriNet/<configuration>/`.

## Plugin

Add the package folder as a source in the PlugIn Manager of the AutomationML
Editor and install the plugin from there. It opens as the tab "AMLPetriNet".

- The canvas shows a net of the open document; a list switches between nets.
- **Simulate** fires the net on the canvas: enabled transitions are marked, a
  click fires one, and the run can be stepped back or played. The document is
  not touched by a run; the marking it holds is the one the user typed.
- **Update** writes the net into the document. Elements are updated in place,
  so attributes and links added by others are kept.
- **Refresh** reloads the net from the document.
- The **Net** menu creates a new net, imports and exports PNML and SVG, and
  saves a conformance report.

Do not copy `Aml.Editor.Plugin.Contract.dll` into an installed plugin folder.
The editor ignores the plugin without an error message if it finds a second
copy.

## Web application

```bash
dotnet run --project dotnet/PtMapper.Web
```

| Endpoint | Function |
|---|---|
| `POST /api/to-pnml` | AML in, PNML out (`?hierarchy=` selects the hierarchy) |
| `POST /api/update` | `{aml, pnml, hierarchy}` in, the same AML document with the updated net out |
| `POST /api/to-aml` | PNML in, new AML document out |
| `POST /api/validate` | AML or PNML in, findings as JSON out |
| `GET /api/library` | `ISO_PT` domain library |
| `GET /api/health` | Status and library version |

Requests are limited to 8 MB and 60 per minute per client. The page uses
relative paths, so the app can run under a path behind a reverse proxy. In that
case set `PT_PROXY_KEY` on both sides; the proxy sends it in `X-Proxy-Key`
together with the client address in `X-Client-Address`.

## Command line

```bash
dotnet run --project dotnet/PtMapper.Tool -- <command>
```

| Command | Function |
|---|---|
| `to-aml <in.pnml> <out.aml> [hierarchy]` | PNML to a new AML document |
| `to-pnml <in.aml> <out.pnml> [hierarchy]` | Net of an AML document to PNML |
| `roundtrip <in.aml> [hierarchy]` | Round trip in both directions with a report of the differences |
| `compare <before.aml> <after.aml> [hierarchy]` | Differences between two versions of a document |
| `validate <in.pnml or in.aml>` | Structural checks, non-zero exit code on errors |
| `report <in> <out.md>` | Checks as a Markdown report |
| `library <out.aml>` | Writes the `ISO_PT` domain library |
| `selfcontained <in.aml> <out.aml>` | Embeds the referenced libraries into a document |

## Representation in AML

```
InstanceHierarchy
  PT_Net
    PT_Place, PT_Transition   one PT_NodeArcEnd interface per connected arc
    PT_Arc                   PT_ArcSource and PT_ArcTarget interfaces
    InternalLink             two per arc, from arc interface to node interface
```

Layout uses the `OMG_DD_AttributeTypeLib`: `ViewInformation` on nodes,
`Waypoint_1..n` on arcs, `PortCoordinate` on arc interfaces and `LabelOffset`
for moved labels. Nodes without a position are arranged when a net is loaded.

## Checks

| Rule | Check |
|---|---|
| PT01 | Identifiers are unique |
| PT02 | Every element has an identifier |
| PT03 | Arcs connect a place and a transition |
| PT04 | Both ends of an arc are nodes of the net |
| PT05 | No parallel arcs between the same nodes (warning) |
| PT06 | Initial markings are not negative |
| PT07 | Arc weights are positive |
| PT08 | No unconnected nodes (warning) |
| PT09 | No transition without an input place (warning) |
| PT10 | No transition without an output place (warning) |
| PT11 | Identifiers are valid XML names |

## Tests

```bash
dotnet test dotnet/PtMapper.Tests
cd web && npm test
```

`npm test` runs the analysis core in Node and the modeler in Chromium. The core
can be run on its own with `npm test` in `modeler/`, and `npm run demo` there
opens the modeler without the rest of this repository.

`web/tools/validate-pnml.mjs` checks PNML files against the ISO/IEC 15909-2
grammar and needs Java. Results are described in
[docs/roundtrip-validation.md](docs/roundtrip-validation.md) and
[docs/pnml-conformance.md](docs/pnml-conformance.md).

## License

MIT, see [LICENSE](LICENSE).

`modeler/` is a fork of
[ptn-js](https://github.com/bptlab/openbpt-modeler-petri-net) (MIT) by Maximilian
König and the Business Process Technology group at the Hasso Plattner Institute,
based on [diagram-js](https://github.com/bpmn-io/diagram-js). What the fork
changes is listed in [modeler/README.md](modeler/README.md); fixes that belong
to the original are prepared in [docs/upstream/](docs/upstream/). All
third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
