import AlignElementsModule from 'diagram-js/lib/features/align-elements';
import DistributeElementsModule from 'diagram-js/lib/features/distribute-elements';

import Arrange from './Arrange.js';
import ArrangeBar from './ArrangeBar.js';

export default {
  __depends__: [AlignElementsModule, DistributeElementsModule],
  __init__: ['arrange', 'arrangeBar'],
  arrange: ['type', Arrange],
  arrangeBar: ['type', ArrangeBar],
};
