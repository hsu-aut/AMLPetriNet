import {
  MODELER_PREFIX,
  MODELER_DI_PREFIX,
  MODELER_NAMESPACE,
} from "../../util/constants";

const modelSchema = {
  name: "Place Transition nets",
  uri: MODELER_NAMESPACE,
  prefix: MODELER_PREFIX,
  xml: {
    tagAlias: "lowerCase",
  },
  types: [
    {
      name: "Schema",
      isAbstract: true,
      properties: [
        {
          name: "id",
          isAttr: true,
          type: "String",
          isId: true,
        },
        {
          name: "name",
          type: "String",
        },
      ],
    },
    {
      name: "ModelElement",
      isAbstract: true,
      superClass: ["Schema"],
      properties: [
        // What a PNML file said about this element that the modeler has no use
        // for but must not lose: which page it was on, and the tool-specific
        // sections other tools wrote. Both are carried through unread and put
        // back on export, so a file from another tool survives a visit here.
        // See src/pnml/index.js.
        {
          name: "pnmlPage",
          isAttr: true,
          type: "String",
        },
        {
          name: "pnmlTool",
          type: "String",
        },
      ],
    },
    {
      name: "Node",
      isAbstract: true,
      superClass: ["ModelElement"],
    },
    {
      name: "Place",
      superClass: ["Node"],
      properties: [
        {
          name: "initialMarking",
          type: "Integer",
          default: 0,
        },
      ],
    },
    {
      name: "Transition",
      superClass: ["Node"],
      properties: [
        {
          name: "isSilent",
          type: "Boolean",
          isAttr: true,
          default: false,
        },
      ],
    },
    {
      name: "Connection",
      isAbstract: true,
      superClass: ["ModelElement"],
    },
    {
      name: "BinaryConnection",
      isAbstract: true,
      superClass: ["Connection"],
      properties: [
        {
          name: "source",
          isAttr: true,
          type: "Node",
          isReference: true,
        },
        {
          name: "target",
          isAttr: true,
          type: "Node",
          isReference: true,
        },
      ],
    },
    {
      name: "Arc",
      superClass: ["BinaryConnection"],
      properties: [
        {
          name: "inscription",
          type: "String",
        },
        // An arc may not cross a page boundary in PNML, so a file reaches a
        // node on another page through a reference node. The import resolves
        // those to the real node; these two remember what the arc said, so the
        // export can say it again.
        {
          name: "pnmlSourceRef",
          isAttr: true,
          type: "String",
        },
        {
          name: "pnmlTargetRef",
          isAttr: true,
          type: "String",
        },
      ],
    },
    {
      name: "Model",
      superClass: ["Schema"],
      properties: [
        {
          name: "modelElements",
          type: "ModelElement",
          isMany: true,
        },
        // The page tree of the file this net came from, its reference nodes and
        // the net's own tool-specific sections. Carried through unread; see
        // src/pnml/index.js.
        {
          name: "pnmlPages",
          type: "String",
        },
        {
          name: "pnmlRefs",
          type: "String",
        },
        {
          name: "pnmlTool",
          type: "String",
        },
      ],
    },
    {
      name: "Definitions",
      superClass: ["Schema"],
      properties: [
        {
          name: "targetNamespace",
          isAttr: true,
          type: "String",
        },
        {
          name: "expressionLanguage",
          default: "http://www.w3.org/1999/XPath",
          isAttr: true,
          type: "String",
        },
        {
          name: "typeLanguage",
          default: "http://www.w3.org/2001/XMLSchema",
          isAttr: true,
          type: "String",
        },
        {
          name: "model",
          type: "Model",
        },
        {
          name: "diagram",
          type: `${MODELER_DI_PREFIX}:Diagram`,
        },
        {
          name: "exporter",
          isAttr: true,
          type: "String",
        },
        {
          name: "exporterVersion",
          isAttr: true,
          type: "String",
        },
      ],
    },
  ],
};

export default modelSchema;
