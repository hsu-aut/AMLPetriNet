import RulesModule from 'diagram-js/lib/features/rules';

import Simulation from './Simulation.js';
import SimulationBar from './SimulationBar.js';
import SimulationRules from './SimulationRules.js';
import TokenFlow from './TokenFlow.js';

export default {
  __depends__: [RulesModule],
  __init__: ['simulation', 'simulationBar', 'simulationRules', 'tokenFlow'],
  simulation: ['type', Simulation],
  simulationBar: ['type', SimulationBar],
  simulationRules: ['type', SimulationRules],
  tokenFlow: ['type', TokenFlow],
};
