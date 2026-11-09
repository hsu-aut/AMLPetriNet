/**
 * One command that holds many moves.
 *
 * Everything happens in preExecute: the single moves are commands of their own,
 * and a command another handler starts there belongs to the step that started
 * it. So one press of undo puts the whole layout back, and the handler itself
 * has nothing left to do.
 */
export default class ArrangeHandler {
  static $inject = ['modeling'];

  constructor(modeling) {
    this._modeling = modeling;
  }

  preExecute(context) {
    for (const { shape, delta } of context.moves) {
      this._modeling.moveShape(shape, delta);
    }
  }

  execute() {}

  revert() {}
}
