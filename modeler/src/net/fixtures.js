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

/**
 * Two jobs and two resources, taken in opposite order: the textbook deadlock.
 *
 * Job one takes R1 and then wants R2, job two takes R2 and then wants R1. Each
 * job alone runs for ever; together they can both hold one resource and wait
 * for the other. The structure says so without being run: R1 and R2 form a
 * siphon that holds no trap, which is where the deadlock comes from.
 */
export const resourceDeadlock = {
  places: [
    { id: 'idle1', name: 'Idle 1', marking: 1 },
    { id: 'hold1', name: 'Holding R1', marking: 0 },
    { id: 'idle2', name: 'Idle 2', marking: 1 },
    { id: 'hold2', name: 'Holding R2', marking: 0 },
    { id: 'r1', name: 'R1', marking: 1 },
    { id: 'r2', name: 'R2', marking: 1 },
  ],
  transitions: [
    { id: 'take1', name: 'Job 1 takes R1' },
    { id: 'done1', name: 'Job 1 takes R2 and finishes' },
    { id: 'take2', name: 'Job 2 takes R2' },
    { id: 'done2', name: 'Job 2 takes R1 and finishes' },
  ],
  arcs: [
    { id: 'f1', source: 'idle1', target: 'take1' },
    { id: 'f2', source: 'r1', target: 'take1' },
    { id: 'f3', source: 'take1', target: 'hold1' },
    { id: 'f4', source: 'hold1', target: 'done1' },
    { id: 'f5', source: 'r2', target: 'done1' },
    { id: 'f6', source: 'done1', target: 'idle1' },
    { id: 'f7', source: 'done1', target: 'r1' },
    { id: 'f8', source: 'done1', target: 'r2' },
    { id: 'g1', source: 'idle2', target: 'take2' },
    { id: 'g2', source: 'r2', target: 'take2' },
    { id: 'g3', source: 'take2', target: 'hold2' },
    { id: 'g4', source: 'hold2', target: 'done2' },
    { id: 'g5', source: 'r1', target: 'done2' },
    { id: 'g6', source: 'done2', target: 'idle2' },
    { id: 'g7', source: 'done2', target: 'r1' },
    { id: 'g8', source: 'done2', target: 'r2' },
  ],
};
