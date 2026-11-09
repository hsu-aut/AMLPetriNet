import Simulation from './Simulation.js';
import SimulationBar from './SimulationBar.js';

export default {
  __init__: ['simulation', 'simulationBar'],
  simulation: ['type', Simulation],
  simulationBar: ['type', SimulationBar],
};
