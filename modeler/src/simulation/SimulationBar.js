/**
 * The controls of the simulation, drawn on the canvas.
 *
 * Plain DOM rather than a framework: the whole bar is seven buttons and a line
 * of text, and the package this is forked from brings no framework a host
 * would have to load.
 */
const BUTTONS = [
  { name: 'toggle', text: 'Simulate', title: 'Run the net (Escape leaves)' },
  { name: 'back', text: '◀', title: 'One firing back' },
  { name: 'step', text: '▶', title: 'Fire one transition, or do the one taken back again' },
  { name: 'play', text: '▶▶', title: 'Fire until nothing is enabled' },
  { name: 'reset', text: '↺', title: 'Back to the initial marking' },
];

const STEP_MILLISECONDS = 700;

export default class SimulationBar {
  static $inject = ['eventBus', 'canvas', 'simulation'];

  constructor(eventBus, canvas, simulation) {
    this._simulation = simulation;
    this._playing = null;

    this._container = document.createElement('div');
    this._container.className = 'ptn-simulation';

    this._buttons = {};
    for (const { name, text, title } of BUTTONS) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = `ptn-simulation-${name}`;
      button.textContent = text;
      button.title = title;
      button.addEventListener('click', () => this._click(name));
      this._container.appendChild(button);
      this._buttons[name] = button;
    }

    this._status = document.createElement('span');
    this._status.className = 'ptn-simulation-status';
    this._container.appendChild(this._status);

    eventBus.on('canvas.init', () => {
      canvas.getContainer().appendChild(this._container);
      this._update({ active: false });
    });

    eventBus.on('simulation.changed', (event) => this._update(event));

    // Something the simulation refused to do, said once, until the next step.
    eventBus.on('simulation.said', (event) => {
      this._status.textContent = event.message;
    });

    // Escape leaves the simulation, as it leaves every other mode.
    eventBus.on('keyboard.keydown', (event) => {
      if (event.keyEvent?.key === 'Escape' && this._simulation.active) {
        this._stop();
        return true;
      }
      return undefined;
    });
  }

  _click(name) {
    const simulation = this._simulation;

    if (name === 'toggle') {
      if (simulation.active) this._stop();
      else simulation.start();
      return;
    }
    if (!simulation.active) return;

    this._pause();
    if (name === 'back') simulation.stepBack();
    if (name === 'step') simulation.fireOne();
    if (name === 'reset') simulation.reset();
    if (name === 'play') this._play();
  }

  _play() {
    this._playing = setInterval(() => {
      if (!this._simulation.fireOne()) this._pause();
    }, STEP_MILLISECONDS);
    this._update({ active: true, enabled: this._simulation.enabled, steps: this._simulation.steps });
  }

  _pause() {
    if (this._playing) clearInterval(this._playing);
    this._playing = null;
  }

  _stop() {
    this._pause();
    this._simulation.stop();
  }

  _update(event) {
    const { active, enabled = [], steps = [], undone = [], deadlocked, problem } = event;

    this._container.classList.toggle('ptn-simulation-active', Boolean(active));
    this._buttons.toggle.textContent = active ? 'Stop' : 'Simulate';
    for (const name of ['back', 'step', 'play', 'reset']) {
      this._buttons[name].disabled = !active;
    }
    this._buttons.back.disabled = !active || steps.length === 0;
    this._buttons.step.disabled = !active || (enabled.length === 0 && undone.length === 0);
    this._buttons.play.disabled = this._buttons.step.disabled;

    if (problem) this._status.textContent = problem;
    else if (!active) this._status.textContent = '';
    else if (deadlocked && undone.length === 0) this._status.textContent = `${count(steps)}, nothing is enabled`;
    else this._status.textContent = `${count(steps)}, ${enabled.length} enabled`;
  }
}

const count = (steps) => (steps.length === 1 ? '1 firing' : `${steps.length} firings`);
