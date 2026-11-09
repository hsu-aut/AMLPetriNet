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
 *     does not belong among the user's edits. Steps are kept here, and stepping
 *     back is a step of this module, not an undo.
 *  3. **A net that changes ends the run.** Editing is refused while a net runs
 *     (see SimulationRules), but undo, redo, an import and a host that writes
 *     into the model do not pass the rules. Whatever changed the net, the run
 *     stops: a marking on a net that no longer exists is worse than no marking.
 *
 * The net is read from the canvas on every start, so a net that was edited is
 * simulated as it is now.
 */
import { netFromElements } from '../net/fromDiagram.js';
import { enabled, fire, initialMarking, isEnabled, markingKey, maximalStep, readNet, unfire } from '../net/model.js';

const ENABLED_CLASS = 'ptn-enabled';
const RUNNING_CLASS = 'ptn-simulation-running';

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
    this._undone = [];
    this._problem = null;

    // Redrawing goes through the graphics factory, which does not touch the
    // command stack. The flag keeps the backstop below from reading our own
    // redraw as somebody editing the net.
    this._changing = false;

    // A click on an enabled transition fires it. The listener sits at a higher
    // priority than the selection, so that a running net does not also select.
    eventBus.on('element.click', 1500, (event) => {
      if (!this._active) return undefined;
      const { element } = event;
      if (element?.type !== 'ptn:Transition') return false;

      if (!this.fire(element.id)) {
        this._say(`${name(element)} cannot fire in this marking.`);
      }
      return false;
    });

    // The backstop for rule 3. Editing is refused by the rules, but undo, redo
    // and an import never ask them, and a host can write into the model.
    eventBus.on(['commandStack.changed', 'import.done', 'diagram.clear'], () => {
      if (!this._active || this._changing) return;
      this.stop('The net changed, so the run was stopped.');
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

  /** The marking as a key, for whoever wants to compare it with a state space. */
  get markingKey() {
    return this._active ? markingKey(this._marking) : null;
  }

  /** The transitions that can fire now. */
  get enabled() {
    return this._active ? enabled(this._net, this._marking) : [];
  }

  /** The firings that have happened, in order. */
  get steps() {
    return [...this._steps];
  }

  /** The firings that were taken back and can be done again, the latest last. */
  get undone() {
    return [...this._undone];
  }

  /**
   * Reads the net from the canvas and starts at its initial marking.
   * A net the core refuses leaves the simulation off and the reason in
   * `problem`.
   */
  start() {
    let net;
    try {
      net = readNet(netFromElements(this._elementRegistry.getAll()));
    } catch (error) {
      this._net = null;
      this._marking = null;
      this._steps = [];
      this._undone = [];
      this._problem = error.message;
      this._redraw();
      return false;
    }

    this._net = net;
    this._problem = null;
    this._active = true;
    this._marking = initialMarking(net);
    this._steps = [];
    this._undone = [];
    this._redraw();
    return true;
  }

  /**
   * Stops, and puts the drawing back to the marking the document holds.
   * @param reason why, for a run that ended by itself rather than by the user.
   */
  stop(reason = null) {
    this._active = false;
    this._net = null;
    this._marking = null;
    this._steps = [];
    this._undone = [];
    this._problem = reason;
    this._redraw();
  }

  /** Back to the initial marking without leaving the simulation. */
  reset() {
    if (!this._active) return;
    this._marking = initialMarking(this._net);
    this._steps = [];
    this._undone = [];
    this._redraw();
  }

  /** Fires the transition. Returns whether it could. */
  fire(transitionId) {
    if (!this._active || !isEnabled(this._net, this._marking, transitionId)) return false;

    this._marking = fire(this._net, this._marking, transitionId);
    this._steps.push(transitionId);
    // A firing chosen here starts a new run from this marking: what was taken
    // back cannot be put back on top of a different history.
    this._undone = [];
    this._redraw();
    this._fired([transitionId]);
    return true;
  }

  /**
   * Fires everything that can fire at once, which is how a concurrent net is
   * read: a step rather than a single firing. See `maximalStep`.
   *
   * The firings are recorded one after another, not as a block, so stepping
   * back walks out of the step one firing at a time. That is a deliberate
   * choice: a step is a legal run of its firings, and a reader who wants to see
   * what happened wants the firings.
   *
   * A conflict inside a step is decided by chance, as it is for a single
   * firing: the net does not say which of two transitions wanting the same
   * token goes first.
   *
   * @returns how many transitions fired.
   */
  fireStep() {
    if (!this._active) return 0;

    const order = shuffled(this._net.transitions.map((transition) => transition.id));
    const step = maximalStep(this._net, this._marking, order);
    if (step.length === 0) return 0;

    for (const transition of step) this._marking = fire(this._net, this._marking, transition);
    this._steps.push(...step);
    this._undone = [];
    this._redraw();
    this._fired(step);
    return step.length;
  }

  /** Takes back the last firing. Returns whether there was one. */
  stepBack() {
    if (!this._active || this._steps.length === 0) return false;

    const last = this._steps.pop();
    this._marking = unfire(this._net, this._marking, last);
    this._undone.push(last);
    this._redraw();
    return true;
  }

  /**
   * Does the firing that was taken back again, so that stepping back and
   * forward walks the same run instead of making a new one at every choice.
   */
  stepForward() {
    if (!this._active || this._undone.length === 0) return false;

    const next = this._undone[this._undone.length - 1];
    if (!isEnabled(this._net, this._marking, next)) return false;

    this._undone.pop();
    this._marking = fire(this._net, this._marking, next);
    this._steps.push(next);
    this._redraw();
    this._fired([next]);
    return true;
  }

  /**
   * Fires one transition: the one that was taken back, if there is one, and
   * otherwise one of those that can fire. Where several can, one is picked at
   * random, which is the usual reading of a conflict: the net does not say
   * which. The choice is recorded, so stepping back and forward repeats this
   * run rather than making a new one.
   */
  fireOne() {
    if (this.stepForward()) return true;

    const ready = this.enabled;
    if (ready.length === 0) return false;
    return this.fire(ready[Math.floor(Math.random() * ready.length)]);
  }

  /**
   * Puts the net into a marking that was computed rather than fired into.
   *
   * The state space view uses this to jump: a marking there is one the net can
   * reach, so showing it is honest, but the run that led to it is not known,
   * and the steps are cleared rather than invented.
   *
   * Refused for anything the net cannot be in: a place it does not have, a
   * count that is not a whole number, and omega, which stands for a family of
   * markings rather than for one.
   */
  setMarking(marking) {
    if (!this._active) return false;

    const places = new Set(this._net.places.map((place) => place.id));
    for (const [place, tokens] of marking) {
      if (!places.has(place)) return false;
      if (!Number.isInteger(tokens) || tokens < 0) return false;
    }

    this._marking = new Map(marking);
    this._steps = [];
    this._undone = [];
    this._redraw();
    return true;
  }

  /**
   * Puts a firing sequence on the net without firing it: back to the initial
   * marking, with the sequence ready to be stepped through or played.
   *
   * This is how a computed answer is shown. The state space knows that a
   * marking is reachable and by which firings; the user then watches that
   * happen, instead of being put into a marking whose history is unknown.
   * Starts the run where it is off.
   *
   * The sequence is fired through on a copy first, so a sequence this net
   * cannot fire is refused here rather than halfway through, in front of the
   * user.
   *
   * @param sequence transition ids in the order they fire.
   * @returns whether the net can fire the sequence from its initial marking.
   */
  loadRun(sequence) {
    if (!this._active && !this.start()) return false;

    let marking = initialMarking(this._net);
    for (const transition of sequence) {
      if (!isEnabled(this._net, marking, transition)) return false;
      marking = fire(this._net, marking, transition);
    }

    this._marking = initialMarking(this._net);
    this._steps = [];
    // Taken from the end, so the firing that comes first has to be last.
    this._undone = [...sequence].reverse();
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

  _say(message) {
    this._eventBus.fire('simulation.said', { message });
  }

  /**
   * Says what just fired, for whoever wants to show it happening. The marking
   * has already been redrawn: a firing is a fact by the time it is announced,
   * and an animation is decoration on top of it, so nothing waits for it.
   */
  _fired(transitions) {
    this._eventBus.fire('simulation.fired', { transitions, marking: this.marking });
  }

  _redraw() {
    this._changing = true;
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

    // The canvas says that a net is running, so that the palette and the
    // context pad can be greyed: refusing an edit is right, but an editor that
    // looks ready and then refuses is not.
    this._canvas.getContainer().classList.toggle(RUNNING_CLASS, this._active);

    this._eventBus.fire('simulation.changed', {
      active: this._active,
      marking: this.marking,
      enabled: [...ready],
      steps: this.steps,
      undone: this.undone,
      deadlocked: this._active && ready.size === 0,
      problem: this._problem,
    });

    this._changing = false;
  }
}

const name = (element) => element.businessObject?.name || 'The transition';

/** A copy in random order, so that a conflict inside a step is not always decided the same way. */
function shuffled(ids) {
  const copy = [...ids];
  for (let i = copy.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}
