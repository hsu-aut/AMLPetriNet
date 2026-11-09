/**
 * The findings, on the canvas, one line each.
 *
 * A finding that names an element is a button: it selects the element and
 * scrolls to it, because a list of problems is only useful if the reader can
 * get to the thing the problem is about.
 */
const SEVERITY_MARK = { error: '✖', warning: '⚠', information: 'ℹ' };

export default class FindingsPanel {
  static $inject = ['eventBus', 'canvas', 'findings', 'selection'];

  constructor(eventBus, canvas, findings, selection) {
    this._findings = findings;
    this._selection = selection;
    this._canvas = canvas;
    this._open = false;

    this._container = document.createElement('div');
    this._container.className = 'ptn-findings';
    this._container.hidden = true;

    const head = document.createElement('div');
    head.className = 'ptn-findings-head';
    this._title = document.createElement('span');
    this._title.className = 'ptn-findings-title';
    head.appendChild(this._title);

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'ptn-findings-close';
    close.textContent = '✕';
    close.title = 'Close the findings';
    close.addEventListener('click', () => this.close());
    head.appendChild(close);

    this._list = document.createElement('ul');
    this._list.className = 'ptn-findings-list';

    this._container.append(head, this._list);

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

  open() {
    this._open = true;
    this._container.hidden = false;
  }

  close() {
    this._open = false;
    this._container.hidden = true;
  }

  _update({ findings, problem, stale }) {
    this._list.replaceChildren();

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
      this._list.appendChild(item);
    }
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
