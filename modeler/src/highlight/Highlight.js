/**
 * Elements the tool wants to point at, drawn around them.
 *
 * An answer about a set of elements is unreadable as a sentence once the net is
 * larger than a textbook example: "the token count of Idle, Checking, Approved
 * and Rejected is the same in every reachable marking" is a fact about four
 * shapes, and a reader has to find all four. Marked in the drawing it is one
 * look.
 *
 * Each element carries a weight, because the answers that need this are
 * weighted: a place invariant counts a place twice as much as another one. A
 * weight of one is not written out; anything else is.
 *
 * Like the simulation, this never writes to the model. It holds a set of ids
 * and asks the graphics factory to redraw them.
 */
const CLASS = 'ptn-marked';

export default class Highlight {
  static $inject = ['eventBus', 'elementRegistry', 'graphicsFactory'];

  constructor(eventBus, elementRegistry, graphicsFactory) {
    this._eventBus = eventBus;
    this._elementRegistry = elementRegistry;
    this._graphicsFactory = graphicsFactory;

    this._weights = new Map();
    this._label = null;

    // A marked set belongs to the net it was computed on. Editing the net makes
    // it a decoration, so it goes.
    eventBus.on(['commandStack.changed', 'import.done', 'diagram.clear'], () => this.clear());

    // A new analysis replaces the answers the marks came from.
    eventBus.on('findings.changed', () => this.clear());
  }

  /**
   * Marks these elements and nothing else.
   *
   * @param entries iterable of [elementId, weight].
   * @param label what the set is, for whoever shows it in words.
   */
  show(entries, label = null) {
    const before = [...this._weights.keys()];
    this._weights = new Map([...entries].map(([id, weight]) => [id, Number(weight) || 1]));
    this._label = label;
    this._redraw([...before, ...this._weights.keys()]);
    return this._weights.size;
  }

  clear() {
    if (this._weights.size === 0) return;
    const before = [...this._weights.keys()];
    this._weights = new Map();
    this._label = null;
    this._redraw(before);
  }

  /** The weight an element is marked with, or undefined where it is not. */
  of(id) {
    return this._weights.get(id);
  }

  /** How many elements are marked. */
  get size() {
    return this._weights.size;
  }

  /** What the marked set is, as it was handed in, or null. */
  get label() {
    return this._label;
  }

  _redraw(ids) {
    for (const id of new Set(ids)) {
      const element = this._elementRegistry.get(id);
      if (!element) continue;

      const gfx = this._elementRegistry.getGraphics(element);
      if (!gfx) continue;

      // The class is for a host that wants to style a marked net; the drawing
      // itself comes from the renderer, which owns the colours.
      gfx.classList.toggle(CLASS, this._weights.has(id));
      this._graphicsFactory.update(element.waypoints ? 'connection' : 'shape', element, gfx);
    }

    this._eventBus.fire('highlight.changed', {
      elements: [...this._weights.keys()],
      label: this._label,
    });
  }
}
