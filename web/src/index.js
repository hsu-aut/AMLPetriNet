import { PnModeler } from 'petrinetjs';

export { connectBridge, installDiagnostics, post, showFatal } from './bridge.js';
export { exportSvg } from './svg.js';
export { convertModdleXmlToPnmlXml, convertPnmlXmlToModdleXml } from 'petrinetjs';

export { PnModeler };

export async function createPetriNetModeler(options) {
  const modeler = new PnModeler({
    container: options.container,
    // The properties panel: left out with false, or { open: true | false }. Absent,
    // the modeler's own default holds: closed at first start, then as the user left it.
    ...(options.propertiesPanel === undefined ? {} : { propertiesPanel: options.propertiesPanel }),
  });
  await modeler.createNew();
  return modeler;
}
