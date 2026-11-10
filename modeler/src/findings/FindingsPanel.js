/**
 * The findings, on the canvas, one line each.
 *
 * A finding is not only a sentence. One about an element can select it, one
 * about a marking can be played: the state space knows the firings that lead
 * there, so the panel offers them rather than leaving the reader to find the
 * way by hand. The buttons sit at the end of the row and say what they do; the
 * row itself keeps selecting the element it is about.
 */
import { findingsToCsv } from '../net/report.js';
import { nameFor, offerFile } from '../ui/download.js';

const SEVERITY_MARK = { error: '✖', warning: '⚠', information: 'ℹ' };

export default class FindingsPanel {
  static $inject = ['eventBus', 'canvas', 'findings', 'selection', 'injector'];

  constructor(eventBus, canvas, findings, selection, injector) {
    this._eventBus = eventBus;
    this._findings = findings;
    this._selection = selection;
    this._canvas = canvas;
    this._open = false;

    // Playing a way there needs the simulation, marking a set of elements needs
    // the highlight. Asked for lazily and optionally, so that a host can leave
    // either module out and still get the list.
    this._simulation = injector.get('simulation', false);
    this._highlight = injector.get('highlight', false);

    this._container = document.createElement('div');
    this._container.className = 'ptn-findings';
    this._container.hidden = true;

    const head = document.createElement('div');
    head.className = 'ptn-findings-head';
    this._title = document.createElement('span');
    this._title.className = 'ptn-findings-title';
    head.appendChild(this._title);

    const save = document.createElement('button');
    save.type = 'button';
    save.className = 'ptn-findings-save';
    save.textContent = '⤓ CSV';
    save.title = 'Save the findings as a table';
    save.addEventListener('click', () => this.save());
    head.appendChild(save);

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'ptn-findings-close';
    close.textContent = '✕';
    close.title = 'Close the findings';
    close.addEventListener('click', () => this.close());
    head.appendChild(close);

    this._note = document.createElement('p');
    this._note.className = 'ptn-findings-note';
    this._note.hidden = true;

    this._list = document.createElement('ul');
    this._list.className = 'ptn-findings-list';

    this._container.append(head, this._note, this._list);

    this._button = document.createElement('button');
    this._button.type = 'button';
    this._button.className = 'ptn-findings-button';
    this._button.textContent = 'Analyse';
    this._button.title = 'What the net does: boundedness, deadlocks, invariants';
    this._button.addEventListener('click', () => this.analyse());

    eventBus.on('canvas.init', () => {
      canvas.getContainer().append(this._button, this._container);
    });

    eventBus.on('findings.changed', (event) => this._update(event));

    eventBus.on('findings.show', (event) => {
      this._selection.select(event.element);
      // scrollToElement arrived in diagram-js 14; a host on an older one keeps
      // the selection and loses only the scrolling.
      this._canvas.scrollToElement?.(event.element);
    });
  }

  /** Analyses the net and opens the panel on the result. */
  analyse() {
    this._findings.analyse();
    this.open();
  }

  /** Hands the findings over as a table. Returns where they went. */
  save() {
    const findings = this._findings.all;
    if (findings.length === 0) {
      this._say('There are no findings to save. Analyse first.');
      return 'nothing';
    }

    const name = nameFor(this._canvas, 'findings.csv');
    const went = offerFile(this._eventBus, {
      name,
      text: findingsToCsv(findings),
      type: 'text/csv;charset=utf-8',
    });

    if (went === 'failed') this._say('The table could not be saved.');
    else this._say(`${findings.length} finding(s) saved as ${name}.`);
    return went;
  }

  open() {
    this._open = true;
    this._container.hidden = false;
  }

  close() {
    this._open = false;
    this._container.hidden = true;
  }

  _say(message) {
    this._note.textContent = message ?? '';
    this._note.hidden = !message;
  }

  _update({ findings, problem, stale }) {
    this._list.replaceChildren();
    this._say(null);

    if (problem) {
      this._title.textContent = 'The net could not be read';
      this._list.appendChild(row({ severity: 'error', message: problem }));
      return;
    }

    this._title.textContent = title(findings);
    if (findings.length === 0 && this._open) {
      // "Nothing to report" would read as "the net is fine", which is not what
      // a dropped list means.
      this._list.appendChild(row({
        severity: 'information',
        message: stale
          ? 'The net changed, so the findings were dropped. Analyse again.'
          : 'Nothing to report.',
      }));
    }

    for (const finding of findings) {
      const item = row(finding);
      // Only a finding whose element is on the canvas looks like a link: one
      // that leads nowhere is worse than one that does not pretend to.
      if (finding.element && this._findings.canShow(finding)) {
        item.classList.add('ptn-findings-clickable');
        item.addEventListener('click', () => this._findings.show(finding));
      }
      this._addActions(item, finding);
      this._list.appendChild(item);
    }
  }

  /** The buttons a finding offers, if any. */
  _addActions(item, finding) {
    const actions = [];

    const way = this._simulation ? this._findings.wayTo(finding) : null;
    if (way) {
      actions.push({
        text: '▶ Way there',
        title: way.transitions.length === 1
          ? 'Load the firing that leads to this marking'
          : `Load the ${way.transitions.length} firings that lead to this marking`,
        run: () => this._playWay(way.transitions, 'lead there'),
      });
    }

    // A set of elements is marked in the drawing rather than read out of a
    // sentence. Only where every element of it is still on the canvas: half a
    // marked set says the wrong thing about the net.
    const cover = this._highlight && finding.cover?.length ? finding.cover : null;
    if (cover && cover.every(([id]) => this._findings.canShow({ element: id }))) {
      actions.push({
        text: '◎ Mark',
        title: 'Ring the elements this finding is about, in the drawing',
        run: () => this._mark(cover),
      });
    }

    // A firing sequence the finding stands for, where one is fireable at all.
    const firings = this._simulation && finding.firings?.length ? this._findings.runFor(finding) : null;
    if (firings) {
      actions.push({
        text: '▶ Play',
        title: `Load the ${firings.length} firings in an order the net can fire them in`,
        run: () => this._playWay(firings, 'return the marking'),
      });
    }

    if (actions.length === 0) return;

    const box = document.createElement('span');
    box.className = 'ptn-finding-actions';
    for (const action of actions) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'ptn-finding-action';
      button.textContent = action.text;
      button.title = action.title;
      button.addEventListener('click', (event) => {
        // The row below selects an element; a button is not that click.
        event.stopPropagation();
        action.run();
      });
      box.appendChild(button);
    }
    item.appendChild(box);
  }

  _playWay(transitions, what) {
    if (!this._simulation?.loadRun(transitions)) {
      this._say('The firings could not be loaded: the net is not the one that was analysed.');
      return;
    }

    this._say(transitions.length === 1
      ? `One firing ${what === 'lead there' ? 'leads there' : what}. Press ▶ in the simulation bar.`
      : `${transitions.length} firings ${what}. Press ▶ in the simulation bar, or ▶▶ to watch them.`);
  }

  /** Marks a set of elements, or unmarks it where it is already marked. */
  _mark(cover) {
    const ids = cover.map(([id]) => id);
    const marked = this._highlight.size === ids.length && ids.every((id) => this._highlight.of(id) !== undefined);

    if (marked) {
      this._highlight.clear();
      this._say(null);
      return;
    }

    const count = this._highlight.show(cover);
    const weighted = cover.some(([, weight]) => weight !== 1);
    this._say(`${count} element(s) ringed in the drawing`
      + (weighted ? ', each with the weight it counts for.' : '.')
      + ' Click Mark again to clear them.');
  }
}

function title(findings) {
  const errors = findings.filter((f) => f.severity === 'error').length;
  const warnings = findings.filter((f) => f.severity === 'warning').length;
  if (findings.length === 0) return 'Findings';
  if (errors === 0 && warnings === 0) return `Findings (${findings.length})`;
  return `Findings (${errors} error(s), ${warnings} warning(s), ${findings.length} in all)`;
}

function row(finding) {
  const item = document.createElement('li');
  item.className = `ptn-finding ptn-finding-${finding.severity}`;

  const mark = document.createElement('span');
  mark.className = 'ptn-finding-mark';
  mark.textContent = SEVERITY_MARK[finding.severity] ?? '';
  mark.title = finding.severity;

  const rule = document.createElement('span');
  rule.className = 'ptn-finding-rule';
  rule.textContent = finding.rule ?? '';

  const message = document.createElement('span');
  message.className = 'ptn-finding-message';
  message.textContent = finding.message;

  item.append(mark, rule, message);
  return item;
}
