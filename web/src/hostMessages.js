/**
 * The parts of the host protocol that are plain data, kept apart from the
 * bridge so that they can be tested without a browser.
 */

/**
 * The findings of a host message, as far as they can be read. The message comes
 * from outside the page, so a list that is not a list is an empty one, and an
 * entry that is not an object is left out rather than failing the others.
 */
export function hostFindingsOf(findings) {
  if (!Array.isArray(findings)) return [];

  return findings
    .filter((finding) => finding && typeof finding === 'object')
    .map((finding) => ({
      rule: typeof finding.rule === 'string' ? finding.rule : '',
      severity: finding.severity,
      element: typeof finding.element === 'string' && finding.element ? finding.element : null,
      message: typeof finding.message === 'string' ? finding.message : '',
    }));
}

/**
 * What the host is told after an analysis: how many findings of each severity
 * the analysis computed, and why it could not run if it could not. The host's
 * own findings are not counted; the host knows those.
 */
/** What the host is told when the analysis ended without an answer. */
export const ANALYSIS_ENDED = 'The analysis was stopped, or the net changed while it ran. Analyse again.';

export function analysedOf(computed, problem) {
  // null is an analysis that ended without an answer: stopped, or the net changed.
  if (computed === null) problem = problem || ANALYSIS_ENDED;
  const count = (severity) => (computed ?? []).filter((finding) => finding.severity === severity).length;
  return {
    type: 'analysed',
    errors: count('error'),
    warnings: count('warning'),
    information: count('information'),
    problem: typeof problem === 'string' && problem ? problem : null,
  };
}

/**
 * What the host is told about a selection: the id of the first element and how
 * many elements are selected. A label stands for the element it belongs to, so
 * clicking a place's name selects the place for the host as well, and a place
 * with its label in the selection counts once.
 */
export function selectionOf(elements) {
  const seen = new Set();
  const ids = [];
  for (const element of elements ?? []) {
    const target = element?.labelTarget ?? element;
    if (target && !seen.has(target.id)) {
      seen.add(target.id);
      ids.push(target.id);
    }
  }
  return { id: ids[0] ?? null, count: ids.length };
}
