/**
 * The answers as files: a state space for a graph tool, numbers for a table.
 *
 * A tool that computes something and only draws it makes its reader retype it.
 * A paper wants the markings in a table, a poster wants the reachability graph
 * drawn by a tool that lays graphs out properly, and a report wants the
 * findings beside the ones from elsewhere. All three are a few lines of text.
 *
 * Writers only: nothing here reads a file, and nothing here talks to a
 * browser. Whoever calls them decides what happens to the text.
 */
import { OMEGA } from './explore.js';
import { readKey } from './model.js';

/** A marking that holds nothing anywhere, in the places it is written out. */
const EMPTY = 'empty';

/**
 * The state space as DOT, the language of Graphviz.
 *
 * A node is a marking and carries the places that hold something; the initial
 * marking is a double circle, a marking in which nothing is enabled is red.
 * Node names are generated rather than taken from the marking, because a
 * marking key holds quotes and brackets.
 */
export function toDot(result, net) {
  const name = names(net);
  const node = new Map(result.markings.map(({ key }, i) => [key, `m${i}`]));
  const deadlocks = new Set(result.deadlocks);

  const lines = [
    'digraph state_space {',
    '  rankdir=LR;',
    '  node [shape=circle, fontname="Arial", fontsize=9];',
    '  edge [fontname="Arial", fontsize=8];',
  ];

  for (const { key, marking } of result.markings) {
    const attributes = [`label="${quoted(written(marking, name))}"`];
    if (key === result.initial) attributes.push('shape=doublecircle');
    if (deadlocks.has(key)) attributes.push('color="#d93025"', 'fontcolor="#d93025"');
    lines.push(`  ${node.get(key)} [${attributes.join(', ')}];`);
  }

  for (const edge of result.edges) {
    const from = node.get(edge.from);
    const to = node.get(edge.to);
    if (!from || !to) continue;
    lines.push(`  ${from} -> ${to} [label="${quoted(name.get(edge.transition) ?? edge.transition)}"];`);
  }

  lines.push('}', '');
  return lines.join('\n');
}

/**
 * The reachable markings as a table: one row per marking, one column per place,
 * plus how many firings it takes to get there and whether it is a dead end.
 */
export function markingsToCsv(result, net) {
  const name = names(net);
  const away = distances(result);

  const head = ['marking', 'firings', 'deadlock', ...net.places.map((place) => name.get(place.id) ?? place.id)];
  const deadlocks = new Set(result.deadlocks);
  const rows = result.markings.map(({ key, marking }, i) => [
    key === result.initial ? 'initial' : `m${i}`,
    away.has(key) ? String(away.get(key)) : '',
    deadlocks.has(key) ? 'yes' : 'no',
    ...net.places.map((place) => tokensOf(marking.get(place.id) ?? 0)),
  ]);

  return csv([head, ...rows]);
}

/** The findings as a table, in the order they were reported. */
export function findingsToCsv(findings) {
  const head = ['rule', 'severity', 'element', 'message'];
  const rows = (findings ?? []).map((finding) => [
    finding.rule ?? '',
    finding.severity ?? '',
    finding.element ?? '',
    finding.message ?? '',
  ]);

  return csv([head, ...rows]);
}

/** The names of places and transitions, falling back to their ids. */
function names(net) {
  return new Map([...net.places, ...net.transitions].map((element) => [element.id, element.name || element.id]));
}

/** A marking as a reader writes it: the places that hold something. */
function written(marking, name) {
  const held = [...marking]
    .filter(([, tokens]) => tokens !== 0)
    .map(([place, tokens]) => `${name.get(place) ?? place} ${tokensOf(tokens)}`);

  return held.length === 0 ? EMPTY : held.join(', ');
}

/** A token count, with omega written as the letter the literature uses. */
const tokensOf = (tokens) => (tokens === OMEGA ? 'ω' : String(tokens));

/** How many firings each marking is away from the initial one. */
function distances(result) {
  const out = new Map();
  for (const edge of result.edges) {
    if (!out.has(edge.from)) out.set(edge.from, []);
    out.get(edge.from).push(edge.to);
  }

  const away = new Map([[result.initial, 0]]);
  const queue = [result.initial];

  while (queue.length) {
    const key = queue.shift();
    for (const next of out.get(key) ?? []) {
      if (away.has(next)) continue;
      away.set(next, away.get(key) + 1);
      queue.push(next);
    }
  }

  return away;
}

/**
 * Rows as comma separated text.
 *
 * A field is quoted where it holds a comma, a quote or a line break, and a
 * quote inside it is doubled, which is what every spreadsheet reads. The
 * separator is the comma of the format's name; a reader whose spreadsheet
 * wants semicolons says so in its import dialogue.
 */
function csv(rows) {
  return `${rows.map((row) => row.map(field).join(',')).join('\n')}\n`;
}

function field(value) {
  const text = String(value ?? '');
  return /[",\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function quoted(text) {
  return String(text).replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}

/** The marking a key stands for, written out, for a caller that only has the key. */
export function writtenKey(key, net) {
  return written(new Map(readKey(key)), names(net));
}
