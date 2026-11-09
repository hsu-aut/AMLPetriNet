/**
 * The state space beside the net.
 *
 * A node is a marking, an edge a firing. Clicking a node puts the net into
 * that marking, which is the point of the view: the graph says what can
 * happen, the canvas says what it looks like, and the two are the same model
 * seen twice.
 *
 * Drawn as SVG by hand rather than with a graph library: the picture is
 * circles, lines and short labels, and a library would be the largest
 * dependency of this package by far.
 */
import { analyse } from '../net/analyse.js';
import { explore } from '../net/explore.js';
import { netFromElements } from '../net/fromDiagram.js';
import { readNet } from '../net/model.js';
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
    this._elementRegistry = elementRegistry;
    this._simulation = simulation;
    this._result = null;

    this._container = document.createElement('div');
    this._container.className = 'ptn-states';
    this._container.hidden = true;

    const head = document.createElement('div');
    head.className = 'ptn-states-head';
    this._title = document.createElement('span');
    this._title.className = 'ptn-states-title';
    head.appendChild(this._title);

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'ptn-states-close';
    close.textContent = '✕';
    close.title = 'Close the state space';
    close.addEventListener('click', () => this.close());
    head.appendChild(close);

    this._picture = document.createElement('div');
    this._picture.className = 'ptn-states-picture';

    this._container.append(head, this._picture);

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

  /** Explores the net on the canvas and draws its state space. */
  show() {
    try {
      const net = readNet(netFromElements(this._elementRegistry.getAll()));
      this._result = explore(net);
      this._net = net;
      if (this._result.markings.length > MOST_NODES) this._tooBig();
      else this._draw();
    } catch (error) {
      this._result = null;
      this._title.textContent = 'The net could not be read';
      this._picture.replaceChildren(text(error.message));
    }

    this._container.hidden = false;
  }

  close() {
    this._container.hidden = true;
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

  /** Says how big it is rather than drawing something unreadable. */
  _tooBig() {
    const { markings, edges, complete } = this._result;
    this._title.textContent = `${count(markings.length, 'marking')}, too many to draw`;
    this._picture.replaceChildren(text(
      `The net reaches ${complete ? '' : 'at least '}${markings.length} markings with `
      + `${edges.length} firings between them, more than this view draws. `
      + 'Analyse gives the same net in numbers: bounds, dead transitions, deadlocks and invariants.',
      'ptn-states-note',
    ));
  }

  _draw() {
    const { nodes, edges, width, height } = layoutStateSpace(this._result);
    const name = new Map([...this._net.places, ...this._net.transitions].map((e) => [e.id, e.name || e.id]));

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

      const firing = name.get(edge.transition) ?? edge.transition;
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
      label.textContent = describe(node.key, name);
      group.appendChild(label);

      group.addEventListener('click', () => this._goTo(node.key));
      svg.appendChild(group);
    }

    this._picture.replaceChildren(svg);
    this._markCurrent();
  }

  /** Puts the net into the marking a node stands for. */
  _goTo(key) {
    const entry = this._result.markings.find((marking) => marking.key === key);
    if (!entry) return;

    if (!this._simulation.active) this._simulation.start();

    if (!this._simulation.setMarking(entry.marking)) {
      // A node holding omega stands for every marking that holds enough
      // tokens there, so the net cannot be put into it.
      this._note('This node stands for a family of markings, not for one, so the net cannot be put into it.');
      return;
    }

    this._note(null);
    this._markCurrent();
  }

  _note(message) {
    const existing = this._picture.querySelector('.ptn-states-note');
    if (existing) existing.remove();
    if (message) this._picture.prepend(text(message, 'ptn-states-note'));
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

function describe(key, name) {
  const held = JSON.parse(key);
  if (held.length === 0) return 'no tokens anywhere';
  return held
    .map(([place, tokens]) => `${name.get(place) ?? place}: ${tokens === 'omega' ? 'any number' : tokens}`)
    .join(', ');
}

function text(message, className = 'ptn-states-message') {
  const paragraph = document.createElement('p');
  paragraph.className = className;
  paragraph.textContent = message;
  return paragraph;
}
