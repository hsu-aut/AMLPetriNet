export default class UpdatePropertyHandler {
  // didi falls back to the constructor's parameter names wherever a component
  // omits this, and a minifier renames them, so the command stack dies while
  // registering handlers. Its sibling UpdateLabelHandler declares it too.
  static $inject = ["eventBus"];

  constructor(eventBus) {
    this._eventBus = eventBus;
  }

  execute(context) {
    const { element, propertyName, newValue } = context;
    const hints = context.hints || {};

    // Store old value for revert
    context.oldValue = element.businessObject[propertyName];

    element.businessObject[propertyName] = newValue;

    if (hints.rerender) {
      this._eventBus.fire("element.changed", { element });
    }
  }

  revert(context) {
    const { element, propertyName, oldValue } = context;
    const hints = context.hints || {};

    element.businessObject[propertyName] = oldValue;

    if (hints.rerender) {
      this._eventBus.fire("element.changed", { element });
    }
  }
}
