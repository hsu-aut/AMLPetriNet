import assert from 'node:assert/strict';
import { test } from 'node:test';
import { analysedOf, hostFindingsOf, selectionOf } from './hostMessages.js';

test('a list that is not a list is an empty one', () => {
  assert.deepEqual(hostFindingsOf(undefined), []);
  assert.deepEqual(hostFindingsOf('PT05'), []);
  assert.deepEqual(hostFindingsOf({ rule: 'PT05' }), []);
});

test('an entry that is not an object is left out, the others stay', () => {
  const read = hostFindingsOf([null, 3, 'x', { rule: 'PT05', severity: 'warning', element: 'p1', message: 'm' }]);
  assert.deepEqual(read, [{ rule: 'PT05', severity: 'warning', element: 'p1', message: 'm' }]);
});

test('missing fields are read as empty, an empty element as none', () => {
  assert.deepEqual(hostFindingsOf([{ element: '' }]), [{ rule: '', severity: undefined, element: null, message: '' }]);
});

test('the counts of an analysis follow the severities', () => {
  const answer = analysedOf([
    { severity: 'warning' }, { severity: 'warning' },
    { severity: 'information' }, { severity: 'error' },
  ], null);
  assert.deepEqual(answer, { type: 'analysed', errors: 1, warnings: 2, information: 1, problem: null });
});

test('a net that could not be analysed carries the reason and no counts', () => {
  const answer = analysedOf([], 'The weight of an arc must be at least 1.');
  assert.equal(answer.problem, 'The weight of an arc must be at least 1.');
  assert.equal(answer.errors + answer.warnings + answer.information, 0);
});

test('nothing at all is read as an empty analysis', () => {
  assert.deepEqual(analysedOf(undefined, undefined), { type: 'analysed', errors: 0, warnings: 0, information: 0, problem: null });
});

test('a label is reported as the element it belongs to, once', () => {
  const place = { id: 'p' };
  const label = { id: 'p_label', labelTarget: place };
  assert.deepEqual(selectionOf([label]), { id: 'p', count: 1 });
  assert.deepEqual(selectionOf([place, label]), { id: 'p', count: 1 });
  assert.deepEqual(selectionOf([place, { id: 'q' }]), { id: 'p', count: 2 });
});

test('nothing selected is reported as no id and no count', () => {
  assert.deepEqual(selectionOf([]), { id: null, count: 0 });
  assert.deepEqual(selectionOf(undefined), { id: null, count: 0 });
});
