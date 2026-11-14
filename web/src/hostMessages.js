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
export function analysedOf(computed, problem) {
  const count = (severity) => (computed ?? []).filter((finding) => finding.severity === severity).length;
  return {
    type: 'analysed',
    errors: count('error'),
    warnings: count('warning'),
    information: count('information'),
    problem: typeof problem === 'string' && problem ? problem : null,
  };
}
