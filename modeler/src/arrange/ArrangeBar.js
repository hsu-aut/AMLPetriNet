/**
 * Tidying up, as buttons on the canvas.
 *
 * Lining shapes up is a thing every drawing tool can do and nobody finds in a
 * menu, so it sits in the open: one button for the whole net and two groups for
 * a selection. The groups are greyed where they would do nothing, which is what
 * tells a reader that they want more than one shape selected, without a
 * sentence saying so.
 *
 * Nothing here is offered while the net runs. The rules refuse it anyway, and a
 * button that looks ready and then does nothing is worse than a grey one.
 */
const ALIGNMENTS = [
  ['left', 'L', 'Line the left edges up. Ctrl+Left'],
  ['center', 'C', 'Line the middles up, across'],
  ['right', 'R', 'Line the right edges up. Ctrl+Right'],
  ['top', 'T', 'Line the top edges up. Ctrl+Up'],
  ['middle', 'M', 'Line the middles up, down the page'],
  ['bottom', 'B', 'Line the bottom edges up. Ctrl+Down'],
];

const SPREADS = [
  ['horizontal', '⇔', 'Even out the gaps across (three shapes or more). Ctrl+Shift+Right'],
  ['vertical', '⇕', 'Even out the gaps down the page (three shapes or more). Ctrl+Shift+Down'],
];

/**
 * The arrow keys with Ctrl, which nothing else in the modeler uses: the plain
 * arrows move the selection and Shift makes that move further, but the module
 * that does it gives up as soon as Ctrl is held. Ctrl and Shift together spread
 * instead of aligning, which keeps the two families of the bar on one hand.
 */
const KEYS = {
  ArrowLeft: { align: 'left', spread: 'horizontal' },
  ArrowRight: { align: 'right', spread: 'horizontal' },
  ArrowUp: { align: 'top', spread: 'vertical' },
  ArrowDown: { align: 'bottom', spread: 'vertical' },
};

export default class ArrangeBar {
  static $inject = ['eventBus', 'canvas', 'arrange', 'injector'];

  constructor(eventBus, canvas, arrange, injector) {
    this._arrange = arrange;
    this._simulation = injector.get('simulation', false);

    this._container = document.createElement('div');
    this._container.className = 'ptn-arrange';

    this._tidy = document.createElement('button');
    this._tidy.type = 'button';
    this._tidy.className = 'ptn-arrange-tidy';
    this._tidy.textContent = 'Tidy up';
    this._tidy.title = 'Lay the whole net out by its flow. Ctrl+Shift+L';
    this._tidy.addEventListener('click', () => this.tidy());
    this._container.appendChild(this._tidy);

    this._buttons = [];
    this._container.appendChild(this._group('Align', ALIGNMENTS, (type) => this._arrange.align(type), 2));
    this._container.appendChild(this._group('Spread', SPREADS, (type) => this._arrange.spread(type), 3));

    this._note = document.createElement('span');
    this._note.className = 'ptn-arrange-note';
    this._container.appendChild(this._note);

    eventBus.on('canvas.init', () => {
      canvas.getContainer().appendChild(this._container);
      this._update();
    });

    eventBus.on(['selection.changed', 'simulation.changed', 'import.done'], () => this._update());

    eventBus.on('keyboard.keydown', (event) => this._key(event.keyEvent));
  }

  /**
   * The keys of the bar. Returns true where the key was used, which is how
   * diagram-js is told that nothing else should see it.
   */
  _key(key) {
    if (!key || !(key.ctrlKey || key.metaKey)) return undefined;

    if (key.key === 'L' || key.key === 'l') {
      this.tidy();
      return true;
    }

    const bound = KEYS[key.key];
    if (!bound) return undefined;
    if (this._simulation?.active) return undefined;

    const changed = key.shiftKey
      ? this._arrange.spread(bound.spread)
      : this._arrange.align(bound.align);

    // A key that did nothing is not used up: a reader who has one shape
    // selected should still be able to scroll with it.
    if (changed === 0) {
      this._say(`Select ${key.shiftKey ? 'three' : 'two'} shapes or more first.`);
      return undefined;
    }

    return true;
  }

  tidy() {
    if (this._simulation?.active) return 0;

    const moved = this._arrange.tidy();
    this._say(moved === 0 ? 'Nothing to tidy up.' : `${moved} shape(s) moved. Ctrl+Z puts them back.`);
    return moved;
  }

  _group(label, entries, run, least = 2) {
    const group = document.createElement('span');
    group.className = 'ptn-arrange-group';

    const name = document.createElement('span');
    name.className = 'ptn-arrange-label';
    name.textContent = label;
    group.appendChild(name);

    for (const [type, text, title] of entries) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = `ptn-arrange-${type}`;
      button.textContent = text;
      button.title = title;
      button.addEventListener('click', () => {
        const changed = run(type);
        this._say(changed === 0 ? `Select ${least === 3 ? 'three' : 'two'} shapes or more first.` : null);
      });
      group.appendChild(button);
      this._buttons.push({ button, least });
    }

    return group;
  }

  _say(message) {
    this._note.textContent = message ?? '';
  }

  _update() {
    const running = Boolean(this._simulation?.active);
    const selected = this._arrange.selected.length;

    this._container.classList.toggle('ptn-arrange-idle', selected < 2);
    this._tidy.disabled = running;
    for (const { button, least } of this._buttons) {
      button.disabled = running || selected < least;
    }
    if (selected >= 2) this._say(null);
  }
}
