/**
 * The net behind a drawing.
 *
 * The analysis core works on plain data; the canvas works on shapes. This is
 * the one place that knows both, and it is written against a list of elements
 * rather than against the element registry, so that it can be tested without a
 * browser.
 *
 * The identifiers are the canvas identifiers, so every answer about the net can
 * be pointed back at the shape it came from.
 */

const PLACE = 'ptn:Place';
const TRANSITION = 'ptn:Transition';
const ARC = 'ptn:Arc';

const typeOf = (element) => element?.type ?? element?.businessObject?.$type;
const idOf = (element) => element?.id ?? element?.businessObject?.id;

/**
 * @param elements every shape and connection on the canvas, labels included;
 *   labels and the root are ignored.
 * @returns the net as the analysis core reads it.
 */
export function netFromElements(elements) {
  const places = [];
  const transitions = [];
  const arcs = [];

  for (const element of elements) {
    // A label carries the type of what it labels, so it would be counted twice.
    if (element.labelTarget) continue;

    const bo = element.businessObject ?? {};
    switch (typeOf(element)) {
      case PLACE:
        places.push({ id: idOf(element), name: bo.name ?? '', marking: whole(bo.initialMarking) });
        break;
      case TRANSITION:
        transitions.push({ id: idOf(element), name: bo.name ?? '' });
        break;
      case ARC: {
        // A connection that is being drawn has no target yet; it is not an arc
        // of the net until it has both ends.
        const source = idOf(element.source);
        const target = idOf(element.target);
        if (!source || !target) break;
        arcs.push({ id: idOf(element), source, target, weight: positive(bo.inscription) });
        break;
      }
      default:
        break;
    }
  }

  return { places, transitions, arcs };
}

/**
 * The marking and the weight come from text the user typed, so they can be
 * anything. An empty place holds nothing and an arc without an inscription
 * carries one token, which is what the drawing shows; everything else that is
 * not a whole number is left to `readNet` to refuse, with a message that names
 * the element.
 */
function whole(value) {
  if (value === undefined || value === null || value === '') return 0;
  return typeof value === 'number' || typeof value === 'string' ? Number(value) : value;
}

function positive(value) {
  if (value === undefined || value === null || value === '') return 1;
  return typeof value === 'number' || typeof value === 'string' ? Number(value) : value;
}
