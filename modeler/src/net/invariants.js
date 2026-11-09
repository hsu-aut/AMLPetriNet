/**
 * Place and transition invariants, from the incidence matrix.
 *
 * A place invariant is a weighting of the places whose weighted token count is
 * the same in every reachable marking: it is a conservation law of the net, and
 * it proves boundedness without looking at a single marking. A transition
 * invariant is a count of firings that returns the net to the marking it
 * started from: a cycle in the behaviour.
 *
 * Both are the non-negative integer solutions of C^T * x = 0 and C * y = 0,
 * where C[p][t] is what firing t does to p. They are computed the way they are
 * taught, by the Farkas algorithm: start from the identity, and eliminate one
 * column at a time, keeping every row whose entry is already zero and adding,
 * for every pair of rows with opposite signs, the positive combination that
 * cancels the column. Rows whose support contains another row's support are
 * dropped, so the result is the minimal ones, which are the readable ones.
 *
 * The matrix of a drawn net is small, so the simple form is fast enough.
 */

/** C[p][t]: what firing t does to the tokens in p. */
function incidence(net) {
  const places = net.places.map((place) => place.id);
  const transitions = net.transitions.map((transition) => transition.id);
  const index = new Map(places.map((place, row) => [place, row]));

  const matrix = places.map(() => transitions.map(() => 0));
  transitions.forEach((transition, column) => {
    for (const arc of net.arcs) {
      if (arc.target === transition) matrix[index.get(arc.source)][column] -= arc.weight;
      if (arc.source === transition) matrix[index.get(arc.target)][column] += arc.weight;
    }
  });

  return { matrix, places, transitions };
}

/** Non-negative integer solutions of rows * x = 0, minimal in their support. */
function farkas(rows, width) {
  // Each row carries the equations it still has to satisfy and the combination
  // of original rows it was built from.
  let work = rows.map((row, i) => ({
    left: [...row],
    weights: rows.map((_, j) => (i === j ? 1 : 0)),
  }));

  for (let column = 0; column < width; column += 1) {
    const zero = work.filter((row) => row.left[column] === 0);
    const positive = work.filter((row) => row.left[column] > 0);
    const negative = work.filter((row) => row.left[column] < 0);

    const combined = [];
    for (const a of positive) {
      for (const b of negative) {
        const factor = lcm(Math.abs(a.left[column]), Math.abs(b.left[column]));
        const fa = factor / Math.abs(a.left[column]);
        const fb = factor / Math.abs(b.left[column]);
        combined.push({
          left: a.left.map((value, i) => value * fa + b.left[i] * fb),
          weights: a.weights.map((value, i) => value * fa + b.weights[i] * fb),
        });
      }
    }

    work = reduce([...zero, ...combined]);
  }

  return work
    .map((row) => normalise(row.weights))
    .filter((weights) => weights.some((value) => value !== 0));
}

/** Drops rows that are a multiple of another and rows whose support contains another's. */
function reduce(rows) {
  const simplified = rows.map((row) => ({ ...row, weights: normalise(row.weights) }));
  const support = (weights) => new Set(weights.flatMap((value, i) => (value !== 0 ? [i] : [])));

  return simplified.filter((row, i) => {
    const mine = support(row.weights);
    if (mine.size === 0) return false;

    return !simplified.some((other, j) => {
      if (i === j) return false;
      const theirs = support(other.weights);
      if (theirs.size === 0) return false;
      const contained = [...theirs].every((index) => mine.has(index));
      if (!contained) return false;
      // Equal supports: keep the first of the two.
      return theirs.size < mine.size || j < i;
    });
  });
}

function normalise(weights) {
  const divisor = weights.reduce((carry, value) => gcd(carry, Math.abs(value)), 0);
  return divisor > 1 ? weights.map((value) => value / divisor) : weights;
}

function gcd(a, b) {
  while (b) [a, b] = [b, a % b];
  return a;
}

function lcm(a, b) {
  return (a * b) / gcd(a, b);
}

/** Weightings of the places whose weighted token count never changes. */
export function placeInvariants(net) {
  const { matrix, places } = incidence(net);
  const width = net.transitions.length;

  return farkas(matrix, width).map((weights) => named(weights, places));
}

/** Counts of firings that bring the net back to the marking it started from. */
export function transitionInvariants(net) {
  const { matrix, transitions } = incidence(net);
  const columns = transitions.map((_, column) => matrix.map((row) => row[column]));

  return farkas(columns, net.places.length).map((weights) => named(weights, transitions));
}

function named(weights, ids) {
  return new Map(weights.flatMap((value, i) => (value !== 0 ? [[ids[i], value]] : [])));
}
