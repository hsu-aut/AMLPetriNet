/**
 * Nets whose behaviour is known by hand, used by the tests of this folder.
 *
 * Each one is small enough to check with a pencil, and each shows one thing:
 * a cycle, a choice, growth without a bound, a net that gets stuck, and one
 * that is written after a station of a production plant.
 */

/** Two places and two transitions in a cycle: one token goes round for ever. */
export const loop = {
  places: [
    { id: 'idle', name: 'Idle', marking: 1 },
    { id: 'busy', name: 'Busy', marking: 0 },
  ],
  transitions: [
    { id: 'start', name: 'Start' },
    { id: 'stop', name: 'Stop' },
  ],
  arcs: [
    { id: 'a1', source: 'idle', target: 'start' },
    { id: 'a2', source: 'start', target: 'busy' },
    { id: 'a3', source: 'busy', target: 'stop' },
    { id: 'a4', source: 'stop', target: 'idle' },
  ],
};

/** One token, two transitions that want it: firing one disables the other. */
export const conflict = {
  places: [
    { id: 'p', name: 'Choice', marking: 1 },
    { id: 'left', name: 'Left', marking: 0 },
    { id: 'right', name: 'Right', marking: 0 },
  ],
  transitions: [
    { id: 'toLeft', name: 'To left' },
    { id: 'toRight', name: 'To right' },
  ],
  arcs: [
    { id: 'b1', source: 'p', target: 'toLeft' },
    { id: 'b2', source: 'toLeft', target: 'left' },
    { id: 'b3', source: 'p', target: 'toRight' },
    { id: 'b4', source: 'toRight', target: 'right' },
  ],
};

/** A transition with no input place: it fires for ever and fills its place. */
export const producer = {
  places: [{ id: 'stock', name: 'Stock', marking: 0 }],
  transitions: [{ id: 'produce', name: 'Produce' }],
  arcs: [{ id: 'c1', source: 'produce', target: 'stock' }],
};

/**
 * Two tokens are needed to go on, only one ever arrives: the net reaches a
 * marking in which nothing is enabled.
 */
export const deadlock = {
  places: [
    { id: 'in', name: 'In', marking: 1 },
    { id: 'mid', name: 'Mid', marking: 0 },
    { id: 'out', name: 'Out', marking: 0 },
  ],
  transitions: [
    { id: 'step', name: 'Step' },
    { id: 'pair', name: 'Pair' },
  ],
  arcs: [
    { id: 'd1', source: 'in', target: 'step' },
    { id: 'd2', source: 'step', target: 'mid' },
    { id: 'd3', source: 'mid', target: 'pair', weight: 2 },
    { id: 'd4', source: 'pair', target: 'out' },
  ],
};

/**
 * Written after the behaviour of a quality check station: idle, check, then release or
 * discard, and back to idle. Six places, five transitions, twelve arcs, one
 * token in Idle. Two further tokens stand for the two possible results of the
 * check, which the station reads from its sensors.
 */
export const qualityCheck = {
  places: [
    { id: 'Idle', name: 'Idle', marking: 1 },
    { id: 'Checking', name: 'Checking', marking: 0 },
    { id: 'Approved', name: 'Approved', marking: 0 },
    { id: 'Rejected', name: 'Rejected', marking: 0 },
    { id: 'Result_OK', name: 'Result_OK', marking: 1 },
    { id: 'Result_NOK', name: 'Result_NOK', marking: 1 },
  ],
  transitions: [
    { id: 'Start', name: 'Start' },
    { id: 'Approve', name: 'Approve' },
    { id: 'Reject', name: 'Reject' },
    { id: 'Release', name: 'Release' },
    { id: 'Discard', name: 'Discard' },
  ],
  arcs: [
    { id: 'e1', source: 'Idle', target: 'Start' },
    { id: 'e2', source: 'Start', target: 'Checking' },
    { id: 'e3', source: 'Checking', target: 'Approve' },
    { id: 'e4', source: 'Result_OK', target: 'Approve' },
    { id: 'e5', source: 'Approve', target: 'Approved' },
    { id: 'e6', source: 'Checking', target: 'Reject' },
    { id: 'e7', source: 'Result_NOK', target: 'Reject' },
    { id: 'e8', source: 'Reject', target: 'Rejected' },
    { id: 'e9', source: 'Approved', target: 'Release' },
    { id: 'e10', source: 'Release', target: 'Idle' },
    { id: 'e11', source: 'Rejected', target: 'Discard' },
    { id: 'e12', source: 'Discard', target: 'Idle' },
  ],
};
