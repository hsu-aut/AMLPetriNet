import { PnModeler } from 'petrinet-js';

export { connectBridge, installDiagnostics, post, showFatal } from './bridge.js';
export { exportSvg } from './svg.js';
export { convertModdleXmlToPnmlXml, convertPnmlXmlToModdleXml } from 'petrinet-js';

export { PnModeler };

export async function createPetriNetModeler(options) {
  const modeler = new PnModeler({
    container: options.container,
  });
  await modeler.createNew();
  return modeler;
}
