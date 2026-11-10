/**
 * The state space beside the net.
 *
 * A node is a marking, an edge a firing. Clicking a node loads the firings that
 * lead to it into the simulation and walks them, which is the point of the
 * view: the graph says what can happen, the canvas says what it looks like, and
 * the run in between says how one becomes the other.
 *
 * The query line answers the other direction: a reader who has a marking in
 * mind asks whether the net reaches it, and gets the way there rather than a
 * yes.
 *
 * Drawn as SVG by hand rather than with a graph library: the picture is
 * circles, lines and short labels, and a library would be the largest
 * dependency of this package by far.
 */
import { explore, OMEGA } from '../net/explore.js';
import { netFromElements } from '../net/fromDiagram.js';
import { readNet } from '../net/model.js';
import { parseQuery, pathTo, wayTo } from '../net/paths.js';
import { markingsToCsv, toDot } from '../net/report.js';
import { nameFor, offerFile } from '../ui/download.js';
import { layoutStateSpace } from './layout.js';

const SVG = 'http://www.w3.org/2000/svg';
const RADIUS = 11;

/**
 * How much is worth drawing. Above this the picture says nothing a reader can
 * use, and drawing it blocks the page: twelve thousand markings take three
 * seconds and produce a quarter of a million SVG elements. The exploration
 * itself has its own, larger limit; what the numbers say stays available in
 * the findings.
 */
const MOST_NODES = 300;

/** Above this the firing labels are noise rather than information. */
const LABELS_UP_TO = 60;

export default class ReachabilityView {
  static $inject = ['eventBus', 'canvas', 'elementRegistry', 'simulation'];

  constructor(eventBus, canvas, elementRegistry, simulation) {
    this._eventBus = eventBus;
    this._canvas = canvas;
    this._elementRegistry = elementRegistry;
    this._simulation = simulation;
    this._result = null;
    this._net = null;

    this._container = document.createElement('div');
    this._container.className = 'ptn-states';
    this._container.hidden = true;

    const head = document.createElement('div');
    head.className = 'ptn-states-head';
    this._title = document.createElement('span');
    this._title.className = 'ptn-states-title';
    head.appendChild(this._title);

    for (const [kind, text, title] of [
      ['dot', '⤓ DOT', 'Save the state space for a graph tool, as Graphviz reads it'],
      ['csv', '⤓ CSV', 'Save the reachable markings as a table'],
    ]) {
      const save = document.createElement('button');
      save.type = 'button';
      save.className = `ptn-states-save ptn-states-save-${kind}`;
      save.textContent = text;
      save.title = title;
      save.addEventListener('click', () => this.save(kind));
      head.appendChild(save);
    }

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'ptn-states-close';
    close.textContent = '✕';
    close.title = 'Close the state space';
    close.addEventListener('click', () => this.close());
    head.appendChild(close);

    this._container.append(head, this._buildQuery());

    this._picture = document.createElement('div');
    this._picture.className = 'ptn-states-picture';
    this._container.appendChild(this._picture);

    this._button = document.createElement('button');
    this._button.type = 'button';
    this._button.className = 'ptn-states-button';
    this._button.textContent = 'State space';
    this._button.title = 'Every marking the net can reach, and the firings between them';
    this._button.addEventListener('click', () => this.show());

    eventBus.on('canvas.init', () => {
      canvas.getContainer().append(this._button, this._container);
    });

    // A changed net makes the graph wrong, and a wrong graph is worse than none.
    eventBus.on(['commandStack.changed', 'import.done'], () => this.close());

    // While the net runs, the marking it is in is the one to point out.
    eventBus.on('simulation.changed', () => this._markCurrent());
  }

  _buildQuery() {
    const line = document.createElement('form');
    line.className = 'ptn-states-query';

    this._input = document.createElement('input');
    this._input.type = 'text';
    this._input.className = 'ptn-states-query-input';
    this._input.placeholder = 'Can it reach, say, Idle = 1?';
    this._input.title = 'Places and token counts, as in "Idle = 1, Busy = 0". '
      + 'A place without a count is asked for one token; places the query leaves out are free.';
    line.appendChild(this._input);

    const find = document.createElement('button');
    find.type = 'submit';
    find.className = 'ptn-states-query-find';
    find.textContent = 'Reachable?';
    line.appendChild(find);

    line.addEventListener('submit', (event) => {
      event.preventDefault();
      this.ask(this._input.value);
    });

    return line;
  }

  /**
   * Explores the net on the canvas and draws its state space.
   * @param limits passed to the search; see explore(). A host with more time
   *   than a browser tab, or a test that wants the search cut off, says so here.
   */
  show(limits = {}) {
    try {
      const net = readNet(netFromElements(this._elementRegistry.getAll()));
      this._result = explore(net, limits);
      this._net = net;
      if (this._result.markings.length > MOST_NODES) this._tooBig();
      else this._draw();
    } catch (error) {
      this._result = null;
      this._net = null;
      this._title.textContent = 'The net could not be read';
      this._picture.replaceChildren(text(error.message));
    }

    this._container.hidden = false;
  }

  close() {
    this._container.hidden = true;
  }

  /**
   * Answers a question about a marking: whether the net reaches it, and by
   * which firings. The way is loaded into the simulation rather than fired, so
   * the reader decides whether to watch it.
   *
   * @returns {string} what was said, which is what a test reads.
   */
  ask(question) {
    if (!this._result) this.show();
    if (!this._result) return this._note('The net could not be read, so nothing can be asked about it.');

    const { wanted, problem } = parseQuery(question, this._net);
    if (problem) return this._note(problem);

    const way = wayTo(this._result, wanted);
    if (!way) {
      return this._note(this._result.complete
        ? 'No reachable marking holds that.'
        : 'No marking the search reached holds that, and the search stopped at its limit, '
          + 'so this is not an answer about every marking.');
    }

    if (way.abstract) {
      return this._note('Only a marking that stands for a family of markings holds that, '
        + 'because a place in it grows without a bound. The net gets there, but not into one marking.');
    }

    if (!this._simulation.loadRun(way.transitions)) {
      return this._note('The net reaches that marking, but the run could not be loaded.');
    }

    this._markCurrent();
    return this._note(way.transitions.length === 0
      ? 'That is the marking the net starts in.'
      : `Reachable in ${count(way.transitions.length, 'firing')}: ${way.transitions.map(this._nameOf, this).join(', ')}. `
        + 'The way there is loaded; press ▶ or ▶▶ in the simulation bar.');
  }

  /**
   * Hands the state space over as a file: as DOT for a graph tool, as CSV for a
   * table. Returns where it went.
   */
  save(kind) {
    if (!this._result) {
      this._note('There is no state space to save yet.');
      return 'nothing';
    }

    const file = kind === 'dot'
      ? {
          name: nameFor(this._canvas, 'state-space.dot'),
          text: toDot(this._result, this._net),
          type: 'text/vnd.graphviz',
        }
      : {
          name: nameFor(this._canvas, 'markings.csv'),
          text: markingsToCsv(this._result, this._net),
          type: 'text/csv;charset=utf-8',
        };

    const went = offerFile(this._eventBus, file);
    this._note(went === 'failed'
      ? `${file.name} could not be saved.`
      : `${count(this._result.markings.length, 'marking')} saved as ${file.name}`
        + (this._result.complete ? '.' : ', which is what the search reached before it stopped.'));
    return went;
  }

  /** What is on screen, for a test or a host that wants to know. */
  get state() {
    if (!this._result) return null;
    return {
      markings: this._result.markings.length,
      edges: this._result.edges.length,
      complete: this._result.complete,
      deadlocks: this._result.deadlocks.length,
    };
  }

  /** The last thing the view said, for a test or a host that wants to know. */
  get said() {
    return this._picture.querySelector('.ptn-states-note')?.textContent ?? null;
  }

  /** Says how big it is rather than drawing something unreadable. */
  _tooBig() {
    const { markings, edges, complete } = this._result;
    this._title.textContent = `${count(markings.length, 'marking')}, too many to draw`;
    this._picture.replaceChildren(text(
      `The net reaches ${complete ? '' : 'at least '}${markings.length} markings with `
      + `${edges.length} firings between them, more than this view draws. `
      + 'Analyse gives the same net in numbers: bounds, dead transitions, deadlocks and invariants. '
      + 'The query line above still works.',
      'ptn-states-note',
    ));
  }

  _draw() {
    const { nodes, edges, width, height } = layoutStateSpace(this._result);

    this._title.textContent = this._result.complete
      ? `${count(nodes.length, 'marking')}, ${count(edges.length, 'firing')}`
      : `${count(nodes.length, 'marking')}, cut off at the limit`;

    const svg = document.createElementNS(SVG, 'svg');
    svg.setAttribute('viewBox', `0 0 ${width + RADIUS * 2} ${height + RADIUS * 2}`);
    svg.setAttribute('width', String(width + RADIUS * 2));
    svg.setAttribute('height', String(height + RADIUS * 2));

    const at = new Map(nodes.map((node) => [node.key, node]));

    const labelled = nodes.length <= LABELS_UP_TO;

    for (const edge of edges) {
      const from = at.get(edge.from);
      const to = at.get(edge.to);
      const line = document.createElementNS(SVG, 'path');
      line.setAttribute('class', 'ptn-states-edge');
      line.setAttribute('d', path(from, to));
      svg.appendChild(line);

      const firing = this._nameOf(edge.transition);
      const hint = document.createElementNS(SVG, 'title');
      hint.textContent = firing;
      line.appendChild(hint);

      // Above a few dozen markings the labels lie on top of each other, so
      // they are left to the pointer.
      if (!labelled) continue;
      const label = document.createElementNS(SVG, 'text');
      label.setAttribute('class', 'ptn-states-edge-label');
      label.setAttribute('x', String((from.x + to.x) / 2));
      label.setAttribute('y', String((from.y + to.y) / 2 - 4));
      label.textContent = firing;
      svg.appendChild(label);
    }

    for (const node of nodes) {
      const group = document.createElementNS(SVG, 'g');
      group.setAttribute('class', 'ptn-states-node'
        + (node.initial ? ' ptn-states-initial' : '')
        + (node.deadlock ? ' ptn-states-deadlock' : ''));
      group.dataset.key = node.key;

      const circle = document.createElementNS(SVG, 'circle');
      circle.setAttribute('cx', String(node.x));
      circle.setAttribute('cy', String(node.y));
      circle.setAttribute('r', String(RADIUS));
      group.appendChild(circle);

      const label = document.createElementNS(SVG, 'title');
      label.textContent = this._describe(node.key);
      group.appendChild(label);

      group.addEventListener('click', () => this._goTo(node.key));
      svg.appendChild(group);
    }

    this._picture.replaceChildren(svg);
    this._markCurrent();
  }

  /**
   * Walks the net into the marking a node stands for.
   *
   * The firings that lead there are loaded and done, so the net arrives with a
   * history: stepping back from here walks the same way out again.
   *
   * The second branch is a safety net rather than a case: a marking the search
   * kept was always reached by a firing it recorded, so the way to it exists,
   * and no limit changes that. It stays because a node whose way is unknown
   * should put the net into the marking rather than do nothing at all, should a
   * later search ever produce one.
   */
  _goTo(key) {
    const entry = this._result.markings.find((marking) => marking.key === key);
    if (!entry) return;

    // A node holding omega stands for every marking that holds enough tokens
    // there, so there is no marking to go to. Checked before the way is looked
    // up: the firings that lead to such a node are real, but they do not end in
    // it, and walking them while saying "to here" would be a lie.
    if ([...entry.marking.values()].some((held) => held === OMEGA)) {
      return this._note('This node stands for a family of markings, not for one, so the net cannot be put into it.');
    }

    const way = pathTo(this._result, key);
    if (way && this._simulation.loadRun(way.transitions)) {
      while (this._simulation.stepForward()) { /* walk the loaded way to its end */ }
      this._note(way.transitions.length === 0
        ? null
        : `${count(way.transitions.length, 'firing')} to here: ${way.transitions.map(this._nameOf, this).join(', ')}. `
          + 'Step back with ◀ to walk it the other way.');
      this._markCurrent();
      return;
    }

    if (!this._simulation.active) this._simulation.start();

    if (!this._simulation.setMarking(entry.marking)) {
      this._note('The net cannot be put into that marking.');
      return;
    }

    this._note('The net is in that marking. The search did not record a way into it, so there are no steps to walk.');
    this._markCurrent();
  }

  _note(message) {
    const existing = this._picture.querySelector('.ptn-states-note');
    if (existing) existing.remove();
    if (message) this._picture.prepend(text(message, 'ptn-states-note'));
    return message;
  }

  _nameOf(id) {
    if (!this._net) return id;
    const element = [...this._net.places, ...this._net.transitions].find((e) => e.id === id);
    return element?.name || id;
  }

  _describe(key) {
    const held = JSON.parse(key);
    if (held.length === 0) return 'no tokens anywhere';
    return held
      .map(([place, tokens]) => `${this._nameOf(place)}: ${tokens === 'omega' ? 'any number' : tokens}`)
      .join(', ');
  }

  _markCurrent() {
    const current = this._simulation.markingKey;
    for (const node of this._picture.querySelectorAll('.ptn-states-node')) {
      node.classList.toggle('ptn-states-current', node.dataset.key === current);
    }
  }
}

const count = (n, what) => `${n} ${what}${n === 1 ? '' : 's'}`;

function path(from, to) {
  if (from === to) {
    // A firing that leads back to the same marking, drawn as a loop above it.
    return `M ${from.x - 6} ${from.y - RADIUS} C ${from.x - 26} ${from.y - 42}, `
      + `${from.x + 26} ${from.y - 42}, ${from.x + 6} ${from.y - RADIUS}`;
  }
  return `M ${from.x} ${from.y} L ${to.x} ${to.y}`;
}

function text(message, className = 'ptn-states-message') {
  const paragraph = document.createElement('p');
  paragraph.className = className;
  paragraph.textContent = message;
  return paragraph;
}
