/**
 * The diagram as a standalone SVG file.
 *
 * The modeler's own saveSVG sizes the picture to the bounding box of the
 * geometry, which cuts the outer half of a stroke and leaves labels flush with
 * the edge. A margin is added around it, so the file can be dropped into a
 * document as it is. Styles are written onto the elements, so no stylesheet
 * has to travel with the file.
 */
export async function exportSvg(modeler, margin = 10) {
  const result = await modeler.saveSVG();
  const svg = typeof result === 'string' ? result : result.svg;

  return svg.replace(
    /width="([\d.-]+)"\s+height="([\d.-]+)"\s+viewBox="([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+)"/,
    (match, width, height, x, y, boxWidth, boxHeight) =>
      `width="${Number(width) + 2 * margin}" height="${Number(height) + 2 * margin}" `
      + `viewBox="${Number(x) - margin} ${Number(y) - margin} `
      + `${Number(boxWidth) + 2 * margin} ${Number(boxHeight) + 2 * margin}"`);
}
