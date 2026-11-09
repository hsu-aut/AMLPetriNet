/**
 * What is wrong with the net, as a list.
 *
 * The findings come from two places. The behavioural ones are computed here
 * from the drawing through the analysis core, which needs nothing but the
 * canvas. The others come from whoever hosts the modeler, which may check the
 * net against rules of its own, or against the document the net sits in. Both
 * kinds end up in one list in the order a reader wants them, errors first,
 * because a reader does not care which half of a program found a problem.
 */
import { analyse } from '../net/analyse.js';
import { netFromElements } from '../net/fromDiagram.js';
import { readNet } from '../net/model.js';

const ORDER = { error: 0, warning: 1, information: 2 };

export default class Findings {
  static $inject = ['eventBus', 'elementRegistry'];

  constructor(eventBus, elementRegistry) {
    this._eventBus = eventBus;
    this._elementRegistry = elementRegistry;

    this._own = [];
    this._fromHost = [];
    this._problem = null;
    this._stale = false;

    // An edited net makes the findings stale, which is worse than no findings:
    // a reader trusts a list that is on the screen. Saying so is not the same
    // as saying that the net is fine, hence the flag.
    eventBus.on(['commandStack.changed', 'import.done'], () => {
      if (!this._own.length && !this._fromHost.length) return;
      this.clear();
      this._stale = true;
      this._changed();
    });
  }

  /** The findings as they are now, errors first. */
  get all() {
    return [...this._own, ...this._fromHost].sort(
      (a, b) => (ORDER[a.severity] ?? 3) - (ORDER[b.severity] ?? 3),
    );
  }

  /** Whether the list was dropped because the net changed under it. */
  get stale() {
    return this._stale;
  }

  /** Why the net could not be analysed, as a sentence, or null. */
  get problem() {
    return this._problem;
  }

  /**
   * Reads the net from the canvas and analyses it.
   * @param limits passed to the analysis core; see explore().
   */
  analyse(limits = {}) {
    try {
      const net = readNet(netFromElements(this._elementRegistry.getAll()));
      this._own = analyse(net, limits).findings;
      this._problem = null;
      this._stale = false;
    } catch (error) {
      this._own = [];
      this._problem = error.message;
      this._stale = false;
    }

    this._changed();
    return this._own;
  }

  /**
   * Findings from the host, which knows about the document the net sits in.
   * They are kept apart from the computed ones so that analysing again does
   * not drop them.
   */
  setHostFindings(findings) {
    this._fromHost = (findings ?? []).map((finding) => ({
      rule: finding.rule ?? '',
      severity: finding.severity ?? 'information',
      element: finding.element ?? null,
      message: finding.message ?? '',
    }));
    this._changed();
  }

  clear() {
    this._own = [];
    this._fromHost = [];
    this._problem = null;
    this._stale = false;
    this._changed();
  }

  /** Whether the element a finding is about is on the canvas. */
  canShow(finding) {
    return Boolean(finding?.element && this._elementRegistry.get(finding.element));
  }

  /** Selects what a finding is about, so a reader can see it on the canvas. */
  show(finding) {
    const element = finding?.element && this._elementRegistry.get(finding.element);
    if (!element) return false;

    this._eventBus.fire('findings.show', { element, finding });
    return true;
  }

  _changed() {
    this._eventBus.fire('findings.changed', {
      findings: this.all,
      problem: this._problem,
      stale: this._stale,
    });
  }
}
