/**
 * Message bridge between the modeler and the WebView2 host.
 *
 * Mirrors the protocol the FPD plugin uses (Aml.Editor.Plugin.FPB/Bridge), so
 * the C# side stays recognisable across the two plugins. The payload here is
 * PNML rather than FPB JSON.
 *
 * Host -> page:
 *   { type: 'importPNML', pnml }   replace the diagram
 *   { type: 'setTheme', theme }    'light' | 'dark'
 *   { type: 'selectElement', id }  select and scroll to a model element
 *   { type: 'requestExport' }      ask for the current PNML on demand
 *   { type: 'requestSvg' }         ask for the diagram as an SVG picture
 *   { type: 'hostFindings', findings }
 *                                  what the host's own check of the net found,
 *                                  [{ rule, severity, element, message }] with
 *                                  severity 'error' | 'warning' | 'information'
 *                                  and element the PNML id; the page lists them
 *                                  with its own findings. They stay until the
 *                                  host sends the list again, an empty one
 *                                  clears them
 *   { type: 'simulate', active }   start (true) or end (false) a run, as the
 *                                  Simulate button does; the canvas takes the
 *                                  keyboard focus. Always answered by one
 *                                  'simulation' message
 *   { type: 'analyse' }            analyse the net and open the list on the
 *                                  canvas; answered by 'analysed'
 *
 * Page -> host:
 *   { type: 'ready', url }
 *   { type: 'imported', pnml }     import finished; pnml is the echo baseline
 *   { type: 'changed', pnml }      user edit
 *   { type: 'svg', svg }           answer to requestSvg
 *   { type: 'simulation', active, problem }
 *                                  a run started or ended; the model is not
 *                                  touched by a run, so nothing is to be done
 *                                  about it. problem is the modeler's reason
 *                                  when a start was refused or a run ended by
 *                                  itself, else null
 *   { type: 'analysed', errors, warnings, information, problem }
 *                                  answer to 'analyse': what the analysis
 *                                  computed (not the host's own findings), or
 *                                  why the net could not be analysed
 *   { type: 'selected', id, count } what the user clicked on the canvas, so the
 *                                  host can point at the same thing in its own
 *                                  view; id is null where nothing is selected
 *                                  and count says how many were
 *   { type: 'fired', ids }        the transitions that just fired, while a run
 *                                  is going
 *   { type: 'log', level, message }
 *   { type: 'error', message }
 */

import { analysedOf, hostFindingsOf, selectionOf } from './hostMessages.js';
import { exportSvg } from './svg.js';

const CHANGE_DEBOUNCE_MS = 300;

/** The modeler says when a simulation starts and stops; see its src/simulation. */
const SIMULATION_EVENT = 'simulation.changed';

/** Preserved before console is patched so the forwarder cannot recurse. */
const nativeConsole = {
  log: console.log.bind(console),
  info: console.info.bind(console),
  warn: console.warn.bind(console),
  error: console.error.bind(console),
  debug: (console.debug ?? console.log).bind(console),
};

function post(message) {
  try {
    window.chrome?.webview?.postMessage(message);
  } catch (e) {
    nativeConsole.error('postMessage failed', e);
  }
}

function formatArg(value) {
  if (value == null) return String(value);
  if (value instanceof Error) return value.stack || `${value.name}: ${value.message}`;
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

/**
 * Forwards every console call and unhandled failure to the host log. The host
 * gates log/debug behind its verbose setting; warn and error always pass.
 */
function installDiagnostics() {
  for (const level of ['log', 'info', 'warn', 'error', 'debug']) {
    console[level] = (...args) => {
      try {
        post({ type: 'log', level, message: args.map(formatArg).join(' ') });
      } catch {
        /* logging must never break the page */
      }
      try {
        nativeConsole[level](...args);
      } catch {
        /* ignore */
      }
    };
  }

  window.addEventListener('error', (e) => {
    post({
      type: 'log',
      level: 'error',
      message: 'window.error: ' + (e.error?.stack || e.message || String(e)),
    });
  });
  window.addEventListener('unhandledrejection', (e) => {
    post({
      type: 'log',
      level: 'error',
      message: 'unhandledRejection: ' + (e.reason?.stack || e.reason || '(no reason)'),
    });
  });
}

function showFatal(text) {
  try {
    const el = document.getElementById('boot-error');
    if (el) {
      el.textContent = text;
      el.style.display = 'flex';
    }
  } catch {
    /* ignore */
  }
  post({ type: 'error', message: text });
}

/**
 * Wires a modeler instance to the host. Returns nothing; the page keeps the
 * modeler on window for diagnostics.
 */
export function connectBridge(modeler) {
  const eventBus = modeler.get('eventBus');
  const canvas = modeler.get('canvas');
  const selection = modeler.get('selection');
  const elementRegistry = modeler.get('elementRegistry');

  // While an import runs the command stack fires a barrage of changes that are
  // not user edits. Suppress those, and hand the host our own export of the
  // imported model so it can tell import fallout from a real edit by content.
  let importing = false;
  let pending = null;

  // A running simulation writes nothing: the modeler keeps the current marking
  // beside the model and draws that, so an export during a run is the document
  // the user drew. Nothing has to be suppressed or refused; the flag is kept
  // only to tell the host when a run starts and ends.
  let simulating = false;

  const exportPnml = async () => {
    try {
      return await modeler.savePNML();
    } catch (e) {
      post({ type: 'error', message: 'PNML export failed: ' + (e?.stack || e) });
      return null;
    }
  };

  const scheduleChange = () => {
    if (importing) return;
    clearTimeout(pending);
    pending = setTimeout(async () => {
      const pnml = await exportPnml();
      if (pnml !== null) post({ type: 'changed', pnml });
    }, CHANGE_DEBOUNCE_MS);
  };

  eventBus.on('commandStack.changed', scheduleChange);

  /** Imports run one after another; see the importPNML handler below. */
  let importQueue = Promise.resolve();

  const runImport = async (pnml) => {
    importing = true;
    clearTimeout(pending);
    try {
      await modeler.importPNML(pnml);
    } catch (err) {
      post({ type: 'error', message: 'importPNML failed: ' + (err?.stack || err) });
    } finally {
      importing = false;
    }
    post({ type: 'imported', pnml: await exportPnml() });
  };

  // What the user is looking at. The host embeds the modeler beside its own
  // view of the same document, and a click on the canvas is the only place
  // where the two views meet.
  eventBus.on('selection.changed', (event) => {
    post({ type: 'selected', ...selectionOf(event.newSelection) });
  });

  // What fired, so a host can say so while a run is going. The model is not
  // touched by it; see the simulation message below.
  eventBus.on('simulation.fired', (event) => {
    const ids = event.transitions ?? [];
    if (ids.length) post({ type: 'fired', ids });
  });

  // Why the last start was refused or the last run ended by itself. Kept even
  // when `active` did not change, because a refused start leaves it false.
  let simulationProblem = null;

  eventBus.on(SIMULATION_EVENT, (event) => {
    const active = Boolean(event.active);
    simulationProblem = typeof event.problem === 'string' && event.problem ? event.problem : null;
    if (active === simulating) return;
    simulating = active;
    post({ type: 'simulation', active: simulating, problem: simulationProblem });
  });

  /** Starts or ends a run for the host, and always answers exactly once. */
  const simulate = (active) => {
    const simulation = modeler.get('simulation');
    const was = simulating;
    simulationProblem = null;

    if (active === true && !simulating) simulation.start();
    else if (active === false && simulating) simulation.stop();

    // A change was answered by the listener above. Anything else (a start the
    // modeler refused, a request that was already true) is answered here.
    if (simulating === was) post({ type: 'simulation', active: simulating, problem: simulationProblem });

    if (simulating) {
      // The menu click left the focus in the host; the keys of a run (a click
      // fires, Escape ends it) need it on the canvas.
      // The keyboard of diagram-js listens on the drawing itself, not on its
      // container, so the focus has to go to the drawing.
      canvas.focus();
    }
  };

  const analyse = async () => {
    const findings = modeler.get('findings');
    // The list is open before the answer is there, so that it can say it is working. The
    // search runs in a worker and the canvas stays usable; every request is answered once,
    // with null where the search was stopped or the net changed meanwhile.
    modeler.get('findingsPanel').open();
    const computed = await findings.analyse();
    post(analysedOf(computed, findings.problem));
  };

  window.chrome?.webview?.addEventListener('message', async (e) => {
    const msg = e.data;
    if (!msg || typeof msg !== 'object') return;

    try {
      if (msg.type === 'importPNML') {
        // Serialised deliberately. Two imports arriving close together (a
        // document switch right after a refresh, say) would otherwise interleave
        // at the await below: the first one's `finally` would clear the flag
        // while the second is still running, and the second import's own command
        // stack traffic would be reported to the host as user edits.
        importQueue = importQueue.then(() => runImport(msg.pnml));
        await importQueue;
      } else if (msg.type === 'requestExport') {
        // Safe during a simulation as well: the export is the model, and the
        // model does not hold the marking the canvas is showing.
        const pnml = await exportPnml();
        if (pnml !== null) post({ type: 'changed', pnml });
      } else if (msg.type === 'requestSvg') {
        post({ type: 'svg', svg: await exportSvg(modeler) });
      } else if (msg.type === 'hostFindings') {
        modeler.get('findings').setHostFindings(hostFindingsOf(msg.findings));
      } else if (msg.type === 'simulate') {
        await importQueue;
        simulate(msg.active);
      } else if (msg.type === 'analyse') {
        await importQueue;
        await analyse();
      } else if (msg.type === 'setTheme') {
        const theme = msg.theme === 'dark' ? 'dark' : 'light';
        document.documentElement.setAttribute('data-theme', theme);
      } else if (msg.type === 'selectElement') {
        const element = elementRegistry.get(msg.id);
        if (element) {
          selection.select(element);
          canvas.scrollToElement(element);
        } else {
          post({
            type: 'log',
            level: 'info',
            message: 'selectElement: id not in current diagram: ' + msg.id,
          });
        }
      }
    } catch (err) {
      post({ type: 'error', message: `handling ${msg.type} failed: ` + (err?.stack || err) });
    }
  });

  post({ type: 'ready', url: location.href });
}

export { installDiagnostics, post, showFatal };
