/**
 * Tokens moving along the arcs a firing used.
 *
 * A firing is instant: the marking before and the marking after are both drawn,
 * and the step in between is invisible. On a net of any size that makes a run
 * hard to follow, because the only way to see what happened is to compare two
 * pictures. A dot that runs from each input place into the transition and on
 * into each output place shows it.
 *
 * Decoration, and treated as such:
 *
 *  - It runs on its own layer, never on the elements. Nothing it draws is part
 *    of the diagram, the model or an export.
 *  - It is behind the firing, not in front of it: the marking is already
 *    redrawn when the dots appear, so a dropped frame or a cancelled animation
 *    cannot leave a wrong marking on screen.
 *  - It gets out of the way: it is off where the run is faster than the
 *    animation, where too many arcs would be moving at once, where the reader
 *    turned it off, and where the system asks for reduced motion.
 */
import { append as svgAppend, create as svgCreate, remove as svgRemove } from 'tiny-svg';

/** How long a firing takes to draw, and what the run has to leave for it. */
const DURATION = 420;
const NEEDS = DURATION + 120;

/** Above this many arcs in one firing the dots are a swarm rather than a picture. */
const MOST_ARCS = 16;

const RADIUS = 4;

export default class TokenFlow {
  static $inject = ['eventBus', 'canvas', 'elementRegistry'];

  constructor(eventBus, canvas, elementRegistry) {
    this._canvas = canvas;
    this._elementRegistry = elementRegistry;

    this._wanted = true;
    this._speed = Infinity;
    this._running = null;
    this._dots = [];

    eventBus.on('simulation.fired', (event) => this.show(event.transitions));

    // A run that ends, a net that is replaced: whatever is in flight belongs to
    // a marking that is no longer on screen.
    eventBus.on('simulation.changed', (event) => { if (!event.active) this.stop(); });
    eventBus.on(['import.done', 'diagram.clear', 'commandStack.changed'], () => this.stop());

    // The player says how much time there is between firings.
    eventBus.on('simulation.speed', (event) => { this._speed = event.milliseconds ?? Infinity; });
  }

  /** Whether the reader wants to see the dots. */
  get wanted() {
    return this._wanted;
  }

  set wanted(wanted) {
    this._wanted = Boolean(wanted);
    if (!this._wanted) this.stop();
  }

  /** How many dots are in flight, for a test or a host that wants to know. */
  get flying() {
    return this._dots.length;
  }

  /**
   * Draws these firings, if there is time and room for it.
   * @returns how many dots were started.
   */
  show(transitions) {
    if (!this._wanted || this._speed < NEEDS || reducedMotion()) return 0;

    // One firing at a time: a second firing arriving while the first is drawn
    // belongs to a newer marking, so the older dots are dropped rather than
    // left to finish on a marking that has moved on.
    this.stop();

    const legs = [];
    for (const transition of transitions) {
      const shape = this._elementRegistry.get(transition);
      if (!shape) continue;

      const middle = { x: shape.x + shape.width / 2, y: shape.y + shape.height / 2 };
      for (const arc of [...(shape.incoming ?? []), ...(shape.outgoing ?? [])]) {
        const points = arc.waypoints?.map((point) => ({ x: point.x, y: point.y }));
        if (!points || points.length < 2) continue;
        // Into the transition on the first half of the time, out of it on the
        // second, so that a token is seen arriving before it leaves.
        const incoming = arc.target === shape;
        legs.push({
          points: incoming ? [...points, middle] : [middle, ...points],
          from: incoming ? 0 : 0.5,
          to: incoming ? 0.5 : 1,
        });
      }
    }

    if (legs.length === 0 || legs.length > MOST_ARCS) return 0;

    const layer = this._canvas.getLayer('ptn-token-flow');
    for (const leg of legs) {
      const dot = svgCreate('circle', { cx: leg.points[0].x, cy: leg.points[0].y, r: RADIUS, fill: 'black' });
      svgAppend(layer, dot);
      this._dots.push({ dot, leg, length: lengths(leg.points) });
    }

    const started = now();
    const tick = () => {
      const passed = (now() - started) / DURATION;
      if (passed >= 1) {
        this.stop();
        return;
      }

      for (const { dot, leg, length } of this._dots) {
        // Each dot moves on its own share of the time and waits at its start
        // or its end outside it.
        const share = (passed - leg.from) / (leg.to - leg.from);
        const at = along(leg.points, length, Math.min(1, Math.max(0, share)));
        dot.setAttribute('cx', String(at.x));
        dot.setAttribute('cy', String(at.y));
        dot.setAttribute('opacity', share < 0 || share > 1 ? '0' : '1');
      }

      this._running = frame(tick);
    };

    this._running = frame(tick);
    return this._dots.length;
  }

  /** Takes every dot off the canvas. */
  stop() {
    if (this._running !== null) cancel(this._running);
    this._running = null;
    for (const { dot } of this._dots) svgRemove(dot);
    this._dots = [];
  }
}

/** The length of each leg of a polyline, and the whole. */
function lengths(points) {
  const legs = [];
  let whole = 0;

  for (let i = 1; i < points.length; i += 1) {
    const leg = Math.hypot(points[i].x - points[i - 1].x, points[i].y - points[i - 1].y);
    legs.push(leg);
    whole += leg;
  }

  return { legs, whole };
}

/** The point a fraction of the way along a polyline. */
function along(points, length, fraction) {
  if (length.whole === 0) return points[0];

  let left = length.whole * fraction;
  for (let i = 0; i < length.legs.length; i += 1) {
    if (left > length.legs[i]) {
      left -= length.legs[i];
      continue;
    }
    const share = length.legs[i] === 0 ? 0 : left / length.legs[i];
    return {
      x: points[i].x + (points[i + 1].x - points[i].x) * share,
      y: points[i].y + (points[i + 1].y - points[i].y) * share,
    };
  }

  return points[points.length - 1];
}

const now = () => (typeof performance === 'undefined' ? Date.now() : performance.now());

const frame = (callback) =>
  (typeof requestAnimationFrame === 'undefined' ? setTimeout(callback, 16) : requestAnimationFrame(callback));

const cancel = (handle) =>
  (typeof cancelAnimationFrame === 'undefined' ? clearTimeout(handle) : cancelAnimationFrame(handle));

/** Whether the system asks for as little movement as possible. */
function reducedMotion() {
  try {
    return Boolean(window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches);
  } catch {
    return false;
  }
}
