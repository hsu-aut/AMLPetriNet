import { PnModeler } from 'petrinet-js';

export { connectBridge, installDiagnostics, post, showFatal } from './bridge.js';
export { exportSvg } from './svg.js';
export { convertModdleXmlToPnmlXml, convertPnmlXmlToModdleXml } from 'petrinet-js';

export { PnModeler };

export async function createPetriNetModeler(options) {
  const modeler = new PnModeler({
    container: options.container,
  });
  nameTransitionsBelow(modeler);
  drawTokensAsDots(modeler);
  await modeler.createNew();
  return modeler;
}

/**
 * Draws a transition's name underneath it instead of inside the box.
 *
 * The modeler writes the name into the 50 pixel box and breaks any word that
 * does not fit, so "Approve" came out as "Appr" over "ove". Names beside the
 * transition are how Petri nets are usually drawn in the literature,
 * and how this modeler already draws place names. Only the drawing changes:
 * the name stays the transition's own attribute and no label element is added,
 * so nothing about the net or its PNML is different.
 */
function nameTransitionsBelow(modeler) {
  const renderer = modeler.get('customRenderer');
  const textRenderer = modeler.get('textRenderer');
  const drawTransition = renderer.drawTransition;

  renderer.drawTransition = function drawTransitionNamedBelow(parentGfx, element) {
    // The original draws the box and then the name inside it; keep the box.
    this.renderEmbeddedLabel = () => undefined;
    try {
      drawTransition.call(this, parentGfx, element);
    } finally {
      delete this.renderEmbeddedLabel;
    }

    const name = element.businessObject?.name;
    if (!element.businessObject?.isSilent && name) {
      // The width and style a place's name gets. Text layout only uses the
      // box's size, never its position, so the text is moved into place.
      const text = this.renderLabel(parentGfx, name, {
        box: { width: 90, height: 30 },
        align: 'center-top',
        padding: 0,
        style: { ...textRenderer.getExternalStyle(), fill: 'black' },
      });
      text.setAttribute('transform', `translate(${element.width / 2 - 45}, ${element.height + 2})`);
    }
    return parentGfx;
  };
}

/** Most tokens a place shows as dots; from one more on it shows the number. */
const MOST_DOTS = 5;

/**
 * Draws a place's tokens as black dots instead of a numeral.
 *
 * The modeler writes the marking as text, so a place holding one token showed
 * a "1". Petri nets are drawn with dots, and with a number only once the dots
 * would no longer be countable at a glance. Only the drawing changes: the
 * marking stays the place's attribute, and PNML, AML and token replay see the
 * same number as before. A marking that is not a whole number is left as text.
 */
function drawTokensAsDots(modeler) {
  const renderer = modeler.get('customRenderer');
  const renderMarking = renderer.renderMarking;

  renderer.renderMarking = function renderMarkingAsDots(parentGfx, element, marking) {
    const count = Number(marking);
    if (!Number.isInteger(count) || count < 1 || count > MOST_DOTS) {
      return renderMarking.call(this, parentGfx, element, marking);
    }

    // Positions as fractions of the place's radius, like the faces of a die.
    const layouts = {
      1: [[0, 0]],
      2: [[-0.35, 0], [0.35, 0]],
      3: [[0, -0.35], [-0.35, 0.25], [0.35, 0.25]],
      4: [[-0.3, -0.3], [0.3, -0.3], [-0.3, 0.3], [0.3, 0.3]],
      5: [[-0.38, -0.38], [0.38, -0.38], [0, 0], [-0.38, 0.38], [0.38, 0.38]],
    };
    const radius = Math.min(element.width, element.height) / 2;
    const dot = radius * (count === 1 ? 0.28 : 0.2);
    const svg = parentGfx.ownerDocument.createElementNS('http://www.w3.org/2000/svg', 'g');
    svg.setAttribute('class', 'ptn-tokens');
    for (const [dx, dy] of layouts[count]) {
      const circle = parentGfx.ownerDocument.createElementNS('http://www.w3.org/2000/svg', 'circle');
      circle.setAttribute('cx', element.width / 2 + dx * radius);
      circle.setAttribute('cy', element.height / 2 + dy * radius);
      circle.setAttribute('r', dot);
      circle.setAttribute('fill', 'black');
      svg.appendChild(circle);
    }
    parentGfx.appendChild(svg);
    return svg;
  };
}
