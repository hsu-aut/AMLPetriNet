/**
 * No editing while the net runs.
 *
 * This is how diagram-js is told no: a rule that answers false, which the
 * features ask before they start. Dragging a shape then does not move it,
 * the context pad offers nothing, the delete key does nothing, and no command
 * reaches the command stack.
 *
 * The obvious alternative, throwing from a command handler, breaks the editor:
 * the command stack counts the action it started and never finishes it, so
 * from then on nothing on the canvas is redrawn. Simulation.js has a backstop
 * for everything that does not ask the rules.
 */
import RuleProvider from 'diagram-js/lib/features/rules/RuleProvider';

/** Everything a user can do to a net, as diagram-js names it. */
const EDITING = [
  'connection.create',
  'connection.reconnect',
  'connection.updateWaypoints',
  'elements.align',
  'elements.create',
  'elements.delete',
  'elements.distribute',
  'elements.move',
  'shape.append',
  'shape.create',
  'shape.replace',
  'shape.resize',
  'element.copy',
  'element.paste',
  'elements.paste',
];

export default class SimulationRules extends RuleProvider {
  static $inject = ['eventBus', 'simulation'];

  constructor(eventBus, simulation) {
    super(eventBus);
    this._simulation = simulation;
  }

  init() {
    // Above the rules of the modeler itself, which run at the default
    // priority, so that this answer is the one that counts.
    for (const action of EDITING) {
      this.addRule(action, 2000, () => (this._simulation.active ? false : undefined));
    }
  }
}
