/** The demo page: the modeler on its own, with PNML in and out. */
import { PnModeler } from '../index.js';

const status = document.getElementById('status');
const say = (text) => { status.textContent = text; };

const modeler = new PnModeler({ container: document.getElementById('canvas') });
await modeler.createNew();
say('empty net');

document.getElementById('new').addEventListener('click', async () => {
  await modeler.createNew();
  say('empty net');
});

document.getElementById('file').addEventListener('change', async (event) => {
  const file = event.target.files[0];
  if (!file) return;
  try {
    await modeler.importPNML(await file.text());
    say(file.name);
  } catch (error) {
    say(String(error?.message || error));
  }
  event.target.value = '';
});

document.getElementById('save').addEventListener('click', async () => {
  const pnml = await modeler.savePNML();
  const link = document.createElement('a');
  link.href = URL.createObjectURL(new Blob([pnml], { type: 'application/xml' }));
  link.download = 'net.pnml';
  link.click();
  URL.revokeObjectURL(link.href);
});
