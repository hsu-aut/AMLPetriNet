/**
 * PNML reader and writer, in place of the upstream `pnml-moddle-converter`.
 *
 * Two reasons this exists rather than using the package:
 *
 *  1. The upstream converter builds XML with xmlbuilder2, which pulls in the
 *     Node builtins `url` and `events`. It cannot be bundled for a browser
 *     without polyfills.
 *  2. It is lossy in the direction that matters here: node graphics carry only
 *     <position> and never <dimension>, and arcs get no <graphics> at all, so
 *     every bendpoint is dropped. A
 *     consumer that maps layout onto its own format needs both, so losing them
 *     breaks round trips.
 *
 * This implementation is DOM based (DOMParser/XMLSerializer, both available in
 * the WebView2 host) and writes the graphics elements ISO/IEC 15909-2 already
 * provides for, so the result stays standard PNML.
 *
 * BaseViewer imports it directly, so importPNML() and savePNML() use it.
 *
 * Conventions, chosen to stay standard and to survive a round trip:
 *
 *  - <position> on a node is its CENTRE, per PNML practice, and <dimension>
 *    carries width/height. Reading back: bounds = centre - dimension / 2.
 *    Foreign PNML without <dimension> falls back to the modeler's default
 *    element size.
 *  - <graphics> on an arc holds only the INTERMEDIATE points (the bendpoints).
 *    The two endpoints are docking points the modeler recomputes from the node
 *    geometry, so writing them would be redundant and would show up as stray
 *    bends in other tools.
 *  - A label <offset> is relative to the node centre. Label width/height is
 *    derived from the text by the renderer and is deliberately not carried.
 */

const PNML_NS = 'http://www.pnml.org/version-2009/grammar/pnml';
const PTNET_TYPE = 'http://www.pnml.org/version-2009/grammar/ptnet';

const PTN_NS = 'http://bpt-lab.org/schemas/ptn';
const PTN_DI_NS = 'http://bpt-lab.org/schemas/ptnDi';
const DC_NS = 'https://www.omg.org/spec/BPMN/20100501/DC.xsd';

const DEFAULT_SIZE = { width: 50, height: 50 };
const DEFAULT_SILENT_SIZE = { width: 20, height: 50 };
/** The box the modeler lays a node label out in (diagram-js external label). */
const DEFAULT_LABEL_SIZE = { width: 90, height: 20 };

// ── shared DOM helpers ──────────────────────────────────────────────────────

function parse(xml, what) {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  const failure = doc.getElementsByTagName('parsererror')[0];
  if (failure) {
    throw new Error(`${what} is not well-formed XML: ${parserMessage(failure)}`);
  }
  return doc;
}

/**
 * The one useful line out of a parsererror element.
 *
 * Browsers render the failure as a small HTML document, so textContent starts
 * with chrome like "This page contains the following errors:" and ends with an
 * invitation to check the source. A user reading a message in a dialog wants
 * the line and column, not that.
 */
function parserMessage(failure) {
  const lines = failure.textContent
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line && !/^(this page contains|below is a rendering)/i.test(line));

  return lines[0] || 'the file could not be parsed';
}

function serialize(doc) {
  return '<?xml version="1.0" encoding="UTF-8"?>\n' + new XMLSerializer().serializeToString(doc);
}

/** Children of `parent` in namespace `ns` with local name `name`. */
function kids(parent, ns, name) {
  return Array.from(parent.childNodes).filter(
    (n) => n.nodeType === 1 && n.namespaceURI === ns && n.localName === name,
  );
}

function kid(parent, ns, name) {
  return kids(parent, ns, name)[0] ?? null;
}

/** Text content of a single child element, or undefined when absent/blank. */
function childText(parent, ns, name) {
  const el = kid(parent, ns, name);
  if (!el) return undefined;
  const text = el.textContent.trim();
  return text === '' ? undefined : text;
}

function num(value, fallback = 0) {
  const n = Number.parseFloat(value);
  return Number.isFinite(n) ? n : fallback;
}

// ── native modeler XML -> PNML ──────────────────────────────────────────────

/**
 * Reads the DI plane into lookup tables keyed by the id of the model element
 * they describe, so nodes and arcs can be emitted with their geometry inline.
 */
function readDiagram(definitions) {
  const shapes = new Map();
  const edges = new Map();

  const diagram = kid(definitions, PTN_DI_NS, 'diagram');
  const plane = diagram && kid(diagram, PTN_DI_NS, 'plane');
  if (!plane) return { shapes, edges };

  for (const shape of kids(plane, PTN_DI_NS, 'diagramShape')) {
    const ref = shape.getAttribute('modelElement');
    if (!ref) continue;
    const bounds = kid(shape, DC_NS, 'Bounds');
    if (!bounds) continue;

    const entry = {
      x: num(bounds.getAttribute('x')),
      y: num(bounds.getAttribute('y')),
      width: num(bounds.getAttribute('width')),
      height: num(bounds.getAttribute('height')),
    };

    const label = kid(shape, PTN_DI_NS, 'diagramLabel');
    const labelBounds = label && kid(label, DC_NS, 'Bounds');
    if (labelBounds) {
      entry.label = {
        x: num(labelBounds.getAttribute('x')),
        y: num(labelBounds.getAttribute('y')),
        width: num(labelBounds.getAttribute('width')),
        height: num(labelBounds.getAttribute('height')),
      };
    }
    shapes.set(ref, entry);
  }

  for (const edge of kids(plane, PTN_DI_NS, 'diagramEdge')) {
    const ref = edge.getAttribute('modelElement');
    if (!ref) continue;

    const entry = {
      waypoints: kids(edge, PTN_DI_NS, 'waypoint').map((w) => ({
        x: num(w.getAttribute('x')),
        y: num(w.getAttribute('y')),
      })),
    };

    // An arc carries a label too: the modeler renders its inscription, the
    // weight, as an external label the user can drag.
    const label = kid(edge, PTN_DI_NS, 'diagramLabel');
    const labelBounds = label && kid(label, DC_NS, 'Bounds');
    if (labelBounds) {
      entry.label = {
        x: num(labelBounds.getAttribute('x')),
        y: num(labelBounds.getAttribute('y')),
        width: num(labelBounds.getAttribute('width')),
        height: num(labelBounds.getAttribute('height')),
      };
    }

    edges.set(ref, entry);
  }

  return { shapes, edges };
}

function centreOf(bounds) {
  return { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
}

/**
 * The point an arc's label is placed relative to: the middle of the polyline's
 * middle segment.
 *
 * Taken from the modeler's own getWaypointsMid, because the two have to agree.
 * A label position only means something next to the point it is measured from,
 * and if the mapper measured from, say, the arc's centre of gravity, every
 * label would land somewhere else on the way back.
 */
function waypointsMid(waypoints) {
  if (!waypoints || waypoints.length === 0) return { x: 0, y: 0 };
  if (waypoints.length === 1) return { x: waypoints[0].x, y: waypoints[0].y };

  const mid = waypoints.length / 2 - 1;
  const first = waypoints[Math.floor(mid)];
  const second = waypoints[Math.ceil(mid + 0.01)];

  return {
    x: first.x + (second.x - first.x) / 2,
    y: first.y + (second.y - first.y) / 2,
  };
}

/**
 * A coordinate as PNML spells it: an xs:decimal, at most two decimals.
 *
 * String() is not enough. Float arithmetic leaves remainders such as
 * 5.551115123125783e-17, String() prints those in exponent notation, and an
 * exponent is not an xs:decimal, so the file stops being valid PNML. Two
 * decimals is what PtMapper writes too, rounded away from zero like .NET does,
 * so both sides spell a coordinate the same way.
 */
function coordinate(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return '0';
  const rounded = Math.sign(n) * Math.round(Math.abs(n) * 100) / 100;
  return String(rounded === 0 ? 0 : rounded);
}

/**
 * A node size as PNML allows it. The grammar types <dimension> as a positive
 * decimal with at most four digits, one of them after the point, so 50.25 is
 * invalid and 12000 is too.
 */
function roundSize(value) {
  const n = Number(value);
  if (!Number.isFinite(n) || n <= 0) return 0.1;
  const rounded = Math.round(n * 10) / 10;
  if (rounded >= 1000) return Math.min(Math.round(rounded), 9999);
  return Math.max(rounded, 0.1);
}

/** <name><text>..</text>[<graphics><offset/></graphics>]</name> */
function appendName(doc, parent, text, offset) {
  if (text === undefined) return;
  const name = doc.createElementNS(PNML_NS, 'name');
  const t = doc.createElementNS(PNML_NS, 'text');
  t.textContent = text;
  name.appendChild(t);
  if (offset) {
    const graphics = doc.createElementNS(PNML_NS, 'graphics');
    const off = doc.createElementNS(PNML_NS, 'offset');
    off.setAttribute('x', coordinate(offset.x));
    off.setAttribute('y', coordinate(offset.y));
    graphics.appendChild(off);
    name.appendChild(graphics);
  }
  parent.appendChild(name);
}

/** <graphics><position/><dimension/></graphics> for a node. */
function appendNodeGraphics(doc, parent, bounds) {
  if (!bounds) return;

  // The centre follows the size that is written, so that reading it back puts
  // the top-left corner where it was and only a size off the grid moves.
  const width = roundSize(bounds.width);
  const height = roundSize(bounds.height);
  const centre = { x: bounds.x + width / 2, y: bounds.y + height / 2 };
  const graphics = doc.createElementNS(PNML_NS, 'graphics');

  const position = doc.createElementNS(PNML_NS, 'position');
  position.setAttribute('x', coordinate(centre.x));
  position.setAttribute('y', coordinate(centre.y));
  graphics.appendChild(position);

  const dimension = doc.createElementNS(PNML_NS, 'dimension');
  dimension.setAttribute('x', String(width));
  dimension.setAttribute('y', String(height));
  graphics.appendChild(dimension);

  parent.appendChild(graphics);
}

/**
 * A PNML annotation such as initialMarking or inscription, optionally with the
 * offset its label was dragged to.
 */
function appendAnnotation(doc, parent, tag, text, offset) {
  if (text === undefined) return;
  const el = doc.createElementNS(PNML_NS, tag);
  const t = doc.createElementNS(PNML_NS, 'text');
  t.textContent = text;
  el.appendChild(t);

  if (offset) {
    const graphics = doc.createElementNS(PNML_NS, 'graphics');
    const point = doc.createElementNS(PNML_NS, 'offset');
    point.setAttribute('x', coordinate(offset.x));
    point.setAttribute('y', coordinate(offset.y));
    graphics.appendChild(point);
    el.appendChild(graphics);
  }

  parent.appendChild(el);
}

export function convertModdleXmlToPnmlXml(moddleXml) {
  const definitions = parse(moddleXml, 'modeler XML').documentElement;
  const model = kid(definitions, PTN_NS, 'model');
  if (!model) {
    throw new Error('modeler XML has no ptn:model element');
  }
  const { shapes, edges } = readDiagram(definitions);

  const doc = document.implementation.createDocument(PNML_NS, 'pnml', null);
  const pnml = doc.documentElement;

  const netId = model.getAttribute('id') || 'net';
  const net = doc.createElementNS(PNML_NS, 'net');
  net.setAttribute('id', netId);
  net.setAttribute('type', PTNET_TYPE);
  appendName(doc, net, childText(model, PTN_NS, 'name'));
  pnml.appendChild(net);

  const page = doc.createElementNS(PNML_NS, 'page');
  page.setAttribute('id', `${netId}_page`);
  net.appendChild(page);

  const labelOffset = (bounds) => {
    if (!bounds || !bounds.label) return undefined;
    const nodeCentre = centreOf(bounds);
    const textCentre = centreOf(bounds.label);
    return { x: textCentre.x - nodeCentre.x, y: textCentre.y - nodeCentre.y };
  };

  for (const source of [...kids(model, PTN_NS, 'place'), ...kids(model, PTN_NS, 'transition')]) {
    const id = source.getAttribute('id');
    const bounds = shapes.get(id);
    const node = doc.createElementNS(PNML_NS, source.localName);
    node.setAttribute('id', id);

    appendName(doc, node, childText(source, PTN_NS, 'name'), labelOffset(bounds));

    if (source.localName === 'place') {
      const marking = childText(source, PTN_NS, 'initialMarking');
      // A marking of 0 is the default and carries no information.
      if (marking !== undefined && num(marking) !== 0) {
        appendAnnotation(doc, node, 'initialMarking', marking);
      }
    } else if (source.getAttribute('isSilent') === 'true') {
      // PNML has no notion of a silent transition. Keep it in the tool-specific
      // section so a round trip through this converter preserves it, while
      // foreign readers can ignore it.
      const toolspecific = doc.createElementNS(PNML_NS, 'toolspecific');
      toolspecific.setAttribute('tool', 'ptn-js');
      toolspecific.setAttribute('version', '1.0');
      const silent = doc.createElementNS(PNML_NS, 'silent');
      silent.textContent = 'true';
      toolspecific.appendChild(silent);
      node.appendChild(toolspecific);
    }

    appendNodeGraphics(doc, node, bounds);
    page.appendChild(node);
  }

  for (const source of kids(model, PTN_NS, 'arc')) {
    const id = source.getAttribute('id');
    const from = source.getAttribute('source');
    const to = source.getAttribute('target');
    if (!id || !from || !to) {
      // An arc with a missing end is not a P/T arc. Writing source="" would
      // make the .NET reader drop it silently and the next update delete the
      // element from the document, so it is better to say so here.
      console.warn(`arc ${id ?? '(no id)'} has no ${from ? 'target' : 'source'}; not exported`);
      continue;
    }
    const arc = doc.createElementNS(PNML_NS, 'arc');
    arc.setAttribute('id', id);
    arc.setAttribute('source', from);
    arc.setAttribute('target', to);

    appendName(doc, arc, childText(source, PTN_NS, 'name'));

    // The inscription is what the modeler draws next to an arc, so its offset
    // is the arc's label position. Measured from the middle of the polyline,
    // the same point the modeler places it at when a file brings none.
    const routing = edges.get(id) ?? {};
    const waypoints = routing.waypoints ?? [];
    const inscriptionOffset = routing.label
      ? (() => {
          const anchor = waypointsMid(waypoints);
          const text = centreOf(routing.label);
          return { x: text.x - anchor.x, y: text.y - anchor.y };
        })()
      : undefined;

    appendAnnotation(doc, arc, 'inscription',
      childText(source, PTN_NS, 'inscription'), inscriptionOffset);

    // Only the bendpoints: the first and last waypoint are docking points the
    // modeler recomputes from the node geometry on import.
    const bends = waypoints.slice(1, -1);
    if (bends.length) {
      const graphics = doc.createElementNS(PNML_NS, 'graphics');
      for (const point of bends) {
        const position = doc.createElementNS(PNML_NS, 'position');
        position.setAttribute('x', coordinate(point.x));
        position.setAttribute('y', coordinate(point.y));
        graphics.appendChild(position);
      }
      arc.appendChild(graphics);
    }

    page.appendChild(arc);
  }

  return serialize(doc);
}

// ── PNML -> native modeler XML ──────────────────────────────────────────────

/**
 * Nodes and arcs of a net, with pages flattened.
 *
 * ISO/IEC 15909-2 forbids an arc from crossing a page boundary; a page reaches
 * a node on another page through a referencePlace/referenceTransition whose
 * `ref` names the real node, possibly through another reference. Flattening
 * makes the pages disappear, so the references are resolved to the concrete
 * node and the arcs are rewritten to point at it. An arc whose end cannot be
 * resolved is left out with a warning rather than imported half connected.
 */
function collectPageContent(net) {
  const places = [];
  const transitions = [];
  const arcs = [];
  const references = new Map();

  const walk = (parent) => {
    places.push(...kids(parent, PNML_NS, 'place'));
    transitions.push(...kids(parent, PNML_NS, 'transition'));
    arcs.push(...kids(parent, PNML_NS, 'arc'));
    for (const tag of ['referencePlace', 'referenceTransition']) {
      for (const ref of kids(parent, PNML_NS, tag)) {
        const id = ref.getAttribute('id');
        const target = ref.getAttribute('ref');
        if (id && target) references.set(id, target);
      }
    }
    for (const page of kids(parent, PNML_NS, 'page')) walk(page);
  };
  walk(net);

  const nodeIds = new Set([...places, ...transitions].map((n) => n.getAttribute('id')));
  const resolve = (id) => {
    // A chain of references ends at a node, or nowhere; the visited set stops
    // a cycle of references from spinning.
    const visited = new Set();
    let current = id;
    while (current && !nodeIds.has(current) && references.has(current) && !visited.has(current)) {
      visited.add(current);
      current = references.get(current);
    }
    return nodeIds.has(current) ? current : undefined;
  };

  const resolvedArcs = [];
  for (const arc of arcs) {
    const id = arc.getAttribute('id');
    const from = resolve(arc.getAttribute('source'));
    const to = resolve(arc.getAttribute('target'));
    if (!from || !to) {
      console.warn(`arc ${id ?? '(no id)'} points at a node this net does not have; not imported`);
      continue;
    }
    if (from !== arc.getAttribute('source')) arc.setAttribute('source', from);
    if (to !== arc.getAttribute('target')) arc.setAttribute('target', to);
    resolvedArcs.push(arc);
  }

  return { places, transitions, arcs: resolvedArcs };
}

/** PNML annotation value: <tag><text>value</text></tag>, or the tag's own text. */
function annotationValue(parent, tag) {
  const el = kid(parent, PNML_NS, tag);
  if (!el) return undefined;
  const text = kid(el, PNML_NS, 'text');
  const value = (text ?? el).textContent.trim();
  return value === '' ? undefined : value;
}

function readGraphics(node) {
  const graphics = kid(node, PNML_NS, 'graphics');
  if (!graphics) return {};

  const position = kid(graphics, PNML_NS, 'position');
  const dimension = kid(graphics, PNML_NS, 'dimension');

  // A dimension of zero or less is not a size. The C# reader falls back to the
  // default in that case and the two must agree, or the same file opens with
  // different node sizes here and in the mapper; a negative one would reach the
  // SVG as a negative height and the renderer drops the shape.
  const width = dimension ? num(dimension.getAttribute('x')) : 0;
  const height = dimension ? num(dimension.getAttribute('y')) : 0;

  return {
    centre: position
      ? { x: num(position.getAttribute('x')), y: num(position.getAttribute('y')) }
      : undefined,
    size: width > 0 && height > 0 ? { width, height } : undefined,
  };
}

/** The offset on any PNML annotation: <tag><graphics><offset x y/></graphics></tag>. */
function readAnnotationOffset(node, tag) {
  const annotation = kid(node, PNML_NS, tag);
  const graphics = annotation && kid(annotation, PNML_NS, 'graphics');
  const offset = graphics && kid(graphics, PNML_NS, 'offset');
  if (!offset) return undefined;
  return { x: num(offset.getAttribute('x')), y: num(offset.getAttribute('y')) };
}

function readNameOffset(node) {
  const name = kid(node, PNML_NS, 'name');
  const graphics = name && kid(name, PNML_NS, 'graphics');
  const offset = graphics && kid(graphics, PNML_NS, 'offset');
  if (!offset) return undefined;
  return { x: num(offset.getAttribute('x')), y: num(offset.getAttribute('y')) };
}

const ORIGIN_BOX = { x: 0, y: 0, width: 0, height: 0, shape: 'rect' };

/**
 * Where the line from `box`'s centre towards `target` leaves the shape.
 *
 * Places render as circles and transitions as rectangles, so the two cases are
 * solved separately rather than approximating both with a bounding box.
 */
function dockingPoint(box, target) {
  if (!box) return { x: 0, y: 0 };

  const centre = centreOf(box);
  const dx = target.x - centre.x;
  const dy = target.y - centre.y;
  if (dx === 0 && dy === 0) return centre;

  const a = box.width / 2;
  const b = box.height / 2;

  const scale =
    box.shape === 'circle'
      ? // Ellipse: solve (dx*s/a)^2 + (dy*s/b)^2 = 1 for s.
        1 / Math.sqrt((dx / a) ** 2 + (dy / b) ** 2)
      : // Rectangle: shrink to whichever edge is hit first.
        Math.min(dx === 0 ? Infinity : a / Math.abs(dx), dy === 0 ? Infinity : b / Math.abs(dy));

  return { x: round(centre.x + dx * scale), y: round(centre.y + dy * scale) };
}

/** Keeps serialized coordinates short without visibly moving anything. */
function round(value) {
  return Math.round(value * 100) / 100;
}

function isSilent(node) {
  for (const tool of kids(node, PNML_NS, 'toolspecific')) {
    const silent = kid(tool, PNML_NS, 'silent');
    if (silent && silent.textContent.trim() === 'true') return true;
  }
  return false;
}

export function convertPnmlXmlToModdleXml(pnmlXml) {
  const pnml = parse(pnmlXml, 'PNML').documentElement;
  const net = kid(pnml, PNML_NS, 'net');
  if (!net) {
    throw new Error('PNML document contains no <net>');
  }

  const { places, transitions, arcs } = collectPageContent(net);
  const modelId = net.getAttribute('id') || 'model_1';

  const doc = document.implementation.createDocument(PTN_NS, 'ptn:definitions', null);
  const definitions = doc.documentElement;
  definitions.setAttribute('xmlns:ptn', PTN_NS);
  definitions.setAttribute('xmlns:ptnDi', PTN_DI_NS);
  definitions.setAttribute('xmlns:dc', DC_NS);

  const model = doc.createElementNS(PTN_NS, 'ptn:model');
  model.setAttribute('id', modelId);
  const netName = annotationValue(net, 'name');
  if (netName !== undefined) {
    const name = doc.createElementNS(PTN_NS, 'ptn:name');
    name.textContent = netName;
    model.appendChild(name);
  }
  definitions.appendChild(model);

  // The diagram-interchange ids have to be unique across the whole file, and
  // "<id>_di" is a perfectly legal PNML id that a foreign file may already use
  // for something else. A prefix that no id in the file starts with keeps them
  // apart without renaming anything.
  const taken = new Set([...places, ...transitions, ...arcs].map((e) => e.getAttribute('id')));
  taken.add(modelId);
  let diPrefix = 'di_';
  while ([...taken].some((id) => id?.startsWith(diPrefix))) diPrefix = '_' + diPrefix;
  const diId = (id) => `${diPrefix}${id}`;

  const diagram = doc.createElementNS(PTN_DI_NS, 'ptnDi:diagram');
  diagram.setAttribute('id', diId(modelId));
  const plane = doc.createElementNS(PTN_DI_NS, 'ptnDi:plane');
  plane.setAttribute('id', diId(modelId + '_plane'));
  plane.setAttribute('modelElement', modelId);
  diagram.appendChild(plane);
  definitions.appendChild(diagram);

  /** id -> bounds, needed to rebuild the arc docking points below. */
  const bounds = new Map();

  const addNode = (source, tag) => {
    const id = source.getAttribute('id');
    if (!id) return;

    const node = doc.createElementNS(PTN_NS, `ptn:${tag}`);
    node.setAttribute('id', id);

    const label = annotationValue(source, 'name');
    if (label !== undefined) {
      const name = doc.createElementNS(PTN_NS, 'ptn:name');
      name.textContent = label;
      node.appendChild(name);
    }

    const silent = tag === 'transition' && isSilent(source);
    if (silent) {
      node.setAttribute('isSilent', 'true');
    }

    if (tag === 'place') {
      const marking = annotationValue(source, 'initialMarking');
      if (marking !== undefined) {
        const el = doc.createElementNS(PTN_NS, 'ptn:initialMarking');
        el.textContent = String(Math.trunc(num(marking)));
        node.appendChild(el);
      }
    }
    model.appendChild(node);

    // Foreign PNML routinely omits <dimension>, so fall back to the size the
    // modeler would have given the element itself.
    const { centre, size } = readGraphics(source);
    const fallback = silent ? DEFAULT_SILENT_SIZE : DEFAULT_SIZE;
    const box = {
      width: size?.width || fallback.width,
      height: size?.height || fallback.height,
      shape: tag === 'place' ? 'circle' : 'rect',
    };
    box.x = (centre?.x ?? 0) - box.width / 2;
    box.y = (centre?.y ?? 0) - box.height / 2;
    bounds.set(id, box);

    const shape = doc.createElementNS(PTN_DI_NS, 'ptnDi:diagramShape');
    shape.setAttribute('id', diId(id));
    shape.setAttribute('modelElement', id);
    const dcBounds = doc.createElementNS(DC_NS, 'dc:Bounds');
    dcBounds.setAttribute('x', String(box.x));
    dcBounds.setAttribute('y', String(box.y));
    dcBounds.setAttribute('width', String(box.width));
    dcBounds.setAttribute('height', String(box.height));
    shape.appendChild(dcBounds);

    // Only the label's placement is carried over, as an offset from the node
    // centre. The modeler treats the bounds' x/y as the box's top-left corner
    // and lays the text out at DEFAULT_LABEL_SIZE, so the box has to be that
    // size and centred on the point: a 0x0 box would put the intended centre
    // at the top edge, and every session that touched the node would sink the
    // label another half height.
    const offset = readNameOffset(source);
    if (offset && label !== undefined) {
      const diLabel = doc.createElementNS(PTN_DI_NS, 'ptnDi:diagramLabel');
      const labelBounds = doc.createElementNS(DC_NS, 'dc:Bounds');
      const nodeCentre = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
      labelBounds.setAttribute('x', String(nodeCentre.x + offset.x - DEFAULT_LABEL_SIZE.width / 2));
      labelBounds.setAttribute('y', String(nodeCentre.y + offset.y - DEFAULT_LABEL_SIZE.height / 2));
      labelBounds.setAttribute('width', String(DEFAULT_LABEL_SIZE.width));
      labelBounds.setAttribute('height', String(DEFAULT_LABEL_SIZE.height));
      diLabel.appendChild(labelBounds);
      shape.appendChild(diLabel);
    }

    plane.appendChild(shape);
  };

  for (const place of places) addNode(place, 'place');
  for (const transition of transitions) addNode(transition, 'transition');

  for (const source of arcs) {
    const id = source.getAttribute('id');
    const from = source.getAttribute('source');
    const to = source.getAttribute('target');
    if (!id || !from || !to) continue;

    const arc = doc.createElementNS(PTN_NS, 'ptn:arc');
    arc.setAttribute('id', id);
    arc.setAttribute('source', from);
    arc.setAttribute('target', to);

    // An arc carries a name like any other element (the moddle Arc type
    // inherits it, and the export writes it). Dropping it here once renamed
    // every arc of the MPS500 bridging example to its GUID after a single Update.
    const label = annotationValue(source, 'name');
    if (label !== undefined) {
      const name = doc.createElementNS(PTN_NS, 'ptn:name');
      name.textContent = label;
      arc.appendChild(name);
    }

    const inscription = annotationValue(source, 'inscription');
    if (inscription !== undefined) {
      const el = doc.createElementNS(PTN_NS, 'ptn:inscription');
      el.textContent = inscription;
      arc.appendChild(el);
    }
    model.appendChild(arc);

    const graphics = kid(source, PNML_NS, 'graphics');
    const bends = graphics
      ? kids(graphics, PNML_NS, 'position').map((p) => ({
          x: num(p.getAttribute('x')),
          y: num(p.getAttribute('y')),
        }))
      : [];

    // Endpoints are not carried in PNML. The modeler does not run its docking
    // layout on import either, so anchoring at the node centres would draw the
    // arrow head inside the shape. Crop them here instead.
    const fromBox = bounds.get(from);
    const toBox = bounds.get(to);
    const start = dockingPoint(fromBox, bends[0] ?? centreOf(toBox ?? ORIGIN_BOX));
    const end = dockingPoint(toBox, bends[bends.length - 1] ?? centreOf(fromBox ?? ORIGIN_BOX));

    const edge = doc.createElementNS(PTN_DI_NS, 'ptnDi:diagramEdge');
    edge.setAttribute('id', diId(id));
    edge.setAttribute('modelElement', id);

    const polyline = [start, ...bends, end];
    for (const point of polyline) {
      const waypoint = doc.createElementNS(PTN_DI_NS, 'ptnDi:waypoint');
      waypoint.setAttribute('x', String(point.x));
      waypoint.setAttribute('y', String(point.y));
      edge.appendChild(waypoint);
    }

    // Where the weight label sits, if the file says. The box is the size the
    // modeler lays the text out in and is centred on the point, the same way a
    // node's label is: bounds x and y are its top-left corner.
    const inscriptionAt = readAnnotationOffset(source, 'inscription');
    if (inscriptionAt && inscription !== undefined) {
      const anchor = waypointsMid(polyline);
      const diLabel = doc.createElementNS(PTN_DI_NS, 'ptnDi:diagramLabel');
      const labelBounds = doc.createElementNS(DC_NS, 'dc:Bounds');
      labelBounds.setAttribute('x', String(anchor.x + inscriptionAt.x - DEFAULT_LABEL_SIZE.width / 2));
      labelBounds.setAttribute('y', String(anchor.y + inscriptionAt.y - DEFAULT_LABEL_SIZE.height / 2));
      labelBounds.setAttribute('width', String(DEFAULT_LABEL_SIZE.width));
      labelBounds.setAttribute('height', String(DEFAULT_LABEL_SIZE.height));
      diLabel.appendChild(labelBounds);
      edge.appendChild(diLabel);
    }

    plane.appendChild(edge);
  }

  return serialize(doc);
}
