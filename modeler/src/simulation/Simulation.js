/**
 * Firing the net on the canvas.
 *
 * The rules of this module, in order of importance:
 *
 *  1. **The document is never written to.** The current marking lives here, in
 *     a map beside the model, and the renderer is told to draw it. A place's
 *     `initialMarking` is what the user typed and what a save writes out, in
 *     simulation and after it. The package this is forked from wrote the
 *     simulated marking into the model, which is how a run ends up in a saved
 *     file.
 *  2. **The run is not part of the undo stack.** Firing is not an edit, so it
 *     does not belong among the user's edits. Steps are kept here and stepping
 *     back is a step of this module, not an undo.
 *  3. **Nothing can be edited while a net runs**, because a changed net under a
 *     running marking is neither the old net nor the new one.
 *
 * The net is read from the canvas on every start, so a net that was edited is
 * simulated as it is now.
 */
import { netFromElements } from '../net/fromDiagram.js';
import { enabled, fire, initialMarking, isEnabled, markingKey, readNet, unfire } from '../net/model.js';

const ENABLED_CLASS = 'ptn-enabled';

export default class Simulation {
  static $inject = ['eventBus', 'elementRegistry', 'canvas', 'graphicsFactory'];

  constructor(eventBus, elementRegistry, canvas, graphicsFactory) {
    this._eventBus = eventBus;
    this._elementRegistry = elementRegistry;
    this._canvas = canvas;
    this._graphicsFactory = graphicsFactory;

    this._active = false;
    this._net = null;
    this._marking = null;
    this._steps = [];
    this._problem = null;

    // A click on an enabled transition fires it. The listener sits at a higher
    // priority than the selection, so that a running net does not also select.
    eventBus.on('element.click', 1500, (event) => {
      if (!this._active) return undefined;
      const { element } = event;
      if (element?.type !== 'ptn:Transition') return false;
      this.fire(element.id);
      return false;
    });

    // An edit while the simulation runs would leave the marking on a net that
    // no longer exists, so editing is refused for as long as it runs.
    eventBus.on('commandStack.preExecute', 2000, () => {
      if (this._active) throw new Error('the net cannot be edited while the simulation runs');
    });
  }

  /** Whether the net is running. */
  get active() {
    return this._active;
  }

  /** What the simulation could not do, as a sentence, or null. */
  get problem() {
    return this._problem;
  }

  /** The marking as it is now, place id to token count. */
  get marking() {
    return this._marking ? new Map(this._marking) : null;
  }

  /** The transitions that can fire now. */
  get enabled() {
    return this._active ? enabled(this._net, this._marking) : [];
  }

  /** How many firings have happened, and which. */
  get steps() {
    return [...this._steps];
  }

  /**
   * Reads the net from the canvas and starts at its initial marking.
   * A net the core refuses leaves the simulation off and the reason in
   * `problem`.
   */
  start() {
    try {
      this._net = readNet(netFromElements(this._elementRegistry.getAll()));
      this._problem = null;
    } catch (error) {
      this._net = null;
      this._problem = error.message;
      // The canvas says that a net is running, so the palette and the context
    // pad can be greyed: refusing an edit is right, but an editor that looks
    // ready and then refuses is not.
    this._canvas.getContainer().classList.toggle('ptn-simulation-running', this._active);

    this._eventBus.fire('simulation.changed', { active: false, problem: this._problem });
      return false;
    }

    this._active = true;
    this._marking = initialMarking(this._net);
    this._steps = [];
    this._redraw();
    return true;
  }

  /** Stops, and puts the drawing back to the marking the document holds. */
  stop() {
    this._active = false;
    this._marking = null;
    this._steps = [];
    this._net = null;
    this._redraw();
  }

  /** Back to the initial marking without leaving the simulation. */
  reset() {
    if (!this._active) return;
    this._marking = initialMarking(this._net);
    this._steps = [];
    this._redraw();
  }

  /** Fires the transition. Returns whether it could. */
  fire(transitionId) {
    if (!this._active || !isEnabled(this._net, this._marking, transitionId)) return false;

    this._marking = fire(this._net, this._marking, transitionId);
    this._steps.push(transitionId);
    this._redraw();
    return true;
  }

  /** Takes back the last firing. Returns whether there was one. */
  stepBack() {
    if (!this._active || this._steps.length === 0) return false;

    const last = this._steps.pop();
    this._marking = unfire(this._net, this._marking, last);
    this._redraw();
    return true;
  }

  /**
   * Fires one transition of the ones that can fire. Where several can, one is
   * picked at random, which is the usual reading of a conflict: the net does
   * not say which. The choice is recorded in the steps, so stepping back and
   * forward repeats this run rather than making a new one.
   */
  fireOne() {
    const ready = this.enabled;
    if (ready.length === 0) return false;
    return this.fire(ready[Math.floor(Math.random() * ready.length)]);
  }

  /** The marking as a key, for whoever wants to compare it with a state space. */
  get markingKey() {
    return this._active ? markingKey(this._marking) : null;
  }

  /**
   * Puts the net into a marking that was computed, rather than fired into.
   *
   * The state space view uses this to jump: a marking there is one the net can
   * reach, so showing it is honest, but the run that led to it is not known,
   * and the steps are cleared rather than invented.
   */
  setMarking(marking) {
    if (!this._active) return false;

    this._marking = new Map(marking);
    this._steps = [];
    this._redraw();
    return true;
  }

  /** Whether the renderer should draw the transition as one that can fire. */
  isReady(transitionId) {
    return this._active && isEnabled(this._net, this._marking, transitionId);
  }

  /** The token count the renderer should draw for a place. */
  markingOf(placeId) {
    return this._active ? this._marking.get(placeId) ?? 0 : undefined;
  }

  _redraw() {
    const ready = new Set(this.enabled);

    for (const element of this._elementRegistry.getAll()) {
      if (element.labelTarget) continue;
      if (element.type !== 'ptn:Place' && element.type !== 'ptn:Transition') continue;

      const gfx = this._elementRegistry.getGraphics(element);
      if (!gfx) continue;

      // The class is for a host that wants to style a running net; the colour
      // itself comes from the renderer, which owns the drawing.
      if (element.type === 'ptn:Transition') gfx.classList.toggle(ENABLED_CLASS, ready.has(element.id));
      this._graphicsFactory.update('shape', element, gfx);
    }

    // The canvas says that a net is running, so the palette and the context
    // pad can be greyed: refusing an edit is right, but an editor that looks
    // ready and then refuses is not.
    this._canvas.getContainer().classList.toggle('ptn-simulation-running', this._active);

    this._eventBus.fire('simulation.changed', {
      active: this._active,
      marking: this.marking,
      enabled: [...ready],
      steps: this.steps,
      deadlocked: this._active && ready.size === 0,
      problem: this._problem,
    });
  }
}
