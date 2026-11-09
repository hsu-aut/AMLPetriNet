import { assign, omit } from "min-dash";
import BaseViewer from "./BaseViewer";

// diagram-js modules
import ZoomScrollModule from "diagram-js/lib/navigation/zoomscroll";
import MoveCanvasModule from "diagram-js/lib/navigation/movecanvas";

import BendPointsModule from "diagram-js/lib/features/bendpoints";
import ConnectModule from "diagram-js/lib/features/connect";
import ConnectionPreviewModule from "diagram-js/lib/features/connection-preview";
import ContextPadModule from "diagram-js/lib/features/context-pad";
import CreateModule from "diagram-js/lib/features/create";
import EditorActionsModule from "./common/editor-actions";
import GridSnappingModule from "diagram-js/lib/features/grid-snapping";
import KeyboardMoveSelectionModule from "diagram-js/lib/features/keyboard-move-selection";
import LassoToolModule from "diagram-js/lib/features/lasso-tool";
import ModelingModule from "diagram-js/lib/features/modeling";
import MoveModule from "diagram-js/lib/features/move";
import OutlineModule from "diagram-js/lib/features/outline";
import PaletteModule from "diagram-js/lib/features/palette";
import ResizeModule from "diagram-js/lib/features/resize";
import RulesModule from "diagram-js/lib/features/rules";
import SelectionModule from "diagram-js/lib/features/selection";
import SnappingModule from "diagram-js/lib/features/snapping";
import TranslateModule from "diagram-js/lib/i18n/translate";

// Custom common Modules
import SearchPadModule from "./common/search";
import KeyboardModule from "./common/keyboard";
import CopyPasteModule from "./common/copy-paste";

// Modeler-specific modules
import CustomPaletteModule from "./palette";
import CustomCoreModule from "./core";
import CustomModelingModule from "./modeling";
import CustomRulesModule from "./rules";
import CustomAutoPlaceModule from "./auto-place";
import CustomPopupMenuModule from "./popup-menu";

// Firing the net on the canvas. This replaces the token replay of the package
// this is forked from, which wrote the simulated marking into the model, had
// no history and took the undo stack for its own.
import SimulationModule from "./simulation";

import emptyDiagram from "./util/emptyDiagram";

export default class CustomModeler extends BaseViewer {
  constructor(options) {
    const { additionalModules = [] } = options;

    const builtinModules = [
      ConnectModule,
      ConnectionPreviewModule,
      ContextPadModule,
      CreateModule,
      LassoToolModule,
      ModelingModule,
      MoveCanvasModule,
      MoveModule,
      OutlineModule,
      PaletteModule,
      RulesModule,
      SelectionModule,
      ZoomScrollModule,
      EditorActionsModule,
      KeyboardModule,
      CopyPasteModule,
      GridSnappingModule,
      BendPointsModule,
      ResizeModule,
      SnappingModule,
      KeyboardMoveSelectionModule,
      TranslateModule,
      SearchPadModule,
    ];

    const customModules = [
      CustomPaletteModule,
      CustomCoreModule,
      CustomModelingModule,
      CustomAutoPlaceModule,
      CustomRulesModule,
      CustomPopupMenuModule,
      SimulationModule,
    ];

    const modules = [...builtinModules, ...customModules, ...additionalModules];

    const diagramOptions = assign(
      // Take the options object except additionalModules
      omit(options, ["additionalModules"]),
      // Add modules as property
      { modules },
    );

    super(diagramOptions);
  }

  createNew() {
    return this.importXML(emptyDiagram);
  }
}
