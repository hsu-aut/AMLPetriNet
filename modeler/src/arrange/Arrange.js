/**
 * Tidying up: one net laid out, or a handful of shapes lined up.
 *
 * The layout itself is in `src/layout/arrange.js` and knows nothing about the
 * canvas. This is the part that knows the canvas: which shapes there are, what
 * the selection is, and how to move everything in one go.
 *
 * In one go matters. Moving thirty shapes with thirty commands is thirty times
 * undo, which is not what a user who regrets pressing the button wants. The
 * moves are wrapped in a command of this module's own, and the single moves
 * happen inside it, which is how diagram-js makes several commands into one
 * step: everything a handler does in `preExecute` belongs to the step it
 * started.
 */
import { arrange } from '../layout/arrange.js';
import ArrangeHandler from './ArrangeHandler.js';

const PLACE = 'ptn:Place';
const TRANSITION = 'ptn:Transition';
const ARC = 'ptn:Arc';

export default class Arrange {
  static $inject = [
    'eventBus', 'canvas', 'commandStack', 'elementRegistry', 'selection',
    'alignElements', 'distributeElements',
  ];

  constructor(eventBus, canvas, commandStack, elementRegistry, selection, alignElements, distributeElements) {
    this._eventBus = eventBus;
    this._canvas = canvas;
    this._commandStack = commandStack;
    this._elementRegistry = elementRegistry;
    this._selection = selection;
    this._alignElements = alignElements;
    this._distributeElements = distributeElements;

    commandStack.registerHandler('ptn.arrange', ArrangeHandler);
  }

  /** The shapes of the net, labels and arcs left out. */
  get shapes() {
    return this._elementRegistry.filter((element) =>
      !element.labelTarget && (element.type === PLACE || element.type === TRANSITION));
  }

  /** The shapes of the selection, in the same reading. */
  get selected() {
    return this._selection.get().filter((element) =>
      !element.labelTarget && !element.waypoints && (element.type === PLACE || element.type === TRANSITION));
  }

  /**
   * Lays the whole net out and brings it into view.
   * @returns how many shapes moved.
   */
  tidy() {
    const shapes = this.shapes;
    if (shapes.length === 0) return 0;

    const arcs = this._elementRegistry
      .filter((element) => element.type === ARC && element.source && element.target)
      .map((element) => ({ source: element.source.id, target: element.target.id }));

    const at = arrange(
      shapes.map((shape) => ({ id: shape.id, width: shape.width, height: shape.height })),
      arcs,
    );

    const moves = shapes
      .map((shape) => ({
        shape,
        delta: { x: at.get(shape.id).x - shape.x, y: at.get(shape.id).y - shape.y },
      }))
      .filter(({ delta }) => delta.x !== 0 || delta.y !== 0);

    if (moves.length === 0) return 0;

    this._commandStack.execute('ptn.arrange', { moves });
    // The net is somewhere else now, so looking at where it was is no use.
    this._canvas.zoom('fit-viewport', 'auto');
    return moves.length;
  }

  /** Lines the selected shapes up. See AlignElements for the names. */
  align(type) {
    const shapes = this.selected;
    if (shapes.length < 2) return 0;

    this._alignElements.trigger(shapes, type);
    return shapes.length;
  }

  /** Spreads the gaps between the selected shapes evenly. */
  spread(orientation) {
    const shapes = this.selected;
    // Two shapes have one gap, and one gap is even already.
    if (shapes.length < 3) return 0;

    this._distributeElements.trigger(shapes, orientation);
    return shapes.length;
  }
}
