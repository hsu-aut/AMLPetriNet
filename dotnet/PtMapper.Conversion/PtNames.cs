namespace PtMapper.Conversion;

/// <summary>
/// Names and class paths of the ISO_PT domain library, kept in one place so the
/// generator, the reader and the tests cannot drift apart.
///
/// The authoritative artefact is <c>libraries/ISO_PT_DomainLibrary_v0.3.aml</c>.
/// <see cref="PtLibraries"/> reproduces it in code, and a golden test compares
/// the two.
/// </summary>
public static class PtNames
{
    public const string Version = "1.0.0";
    public const string LibraryVersion = "0.3.0";
    public const string LibraryFileName = "ISO_PT_DomainLibrary_v0.3.aml";

    public const string InterfaceClassLib = "ISO_PT_InterfaceClassLib";
    public const string RoleClassLib = "ISO_PT_RoleClassLib";
    public const string SystemUnitClassLib = "ISO_PT_SystemUnitClassLib";
    public const string AttributeTypeLib = "ISO_PT_AttributeTypeLib";

    // Element type names. Role, system unit class and the concept share one
    // name each, as they do in the library file.
    public const string Net = "PT_Net";
    public const string Element = "PT_Element";
    public const string Place = "PT_Place";
    public const string Transition = "PT_Transition";
    public const string Arc = "PT_Arc";

    // Interface classes.
    public const string Port = "PT_Port";
    public const string NodeArcEnd = "PT_NodeArcEnd";
    public const string ArcSource = "PT_ArcSource";
    public const string ArcTarget = "PT_ArcTarget";

    public static string Role(string name) => $"{RoleClassLib}/{name}";
    public static string SystemUnit(string name) => $"{SystemUnitClassLib}/{name}";
    public static string Interface(string name) => $"{InterfaceClassLib}/{name}";

    /// <summary>Attribute names on the instances.</summary>
    public static class Attributes
    {
        public const string Identification = "Identification";
        public const string Id = "id";
        public const string Name = "name";
        public const string ViewInformation = "ViewInformation";
        public const string Position = "position";
        public const string Width = "width";
        public const string Height = "height";
        public const string X = "x";
        public const string Y = "y";
        public const string InitialMarking = "InitialMarking";
        public const string Weight = "Weight";
        public const string RefObj = "refObj";
        public const string PortCoordinate = "PortCoordinate";

        /// <summary>
        /// A transition the modeler can draw without a label. ISO/IEC 15909-1
        /// has no such concept, so this is a tool notion kept out of the class
        /// model and out of standard PNML (it travels in a toolspecific section).
        /// </summary>
        public const string IsSilent = "IsSilent";

        /// <summary>
        /// Where an element's label sits, relative to the element centre. An
        /// instance attribute typed OMG_DD/DD_Point, by the convention the
        /// PT_Arc description in ISO_PT v0.3 states. Not declared on the class:
        /// a dragged label is a user's edit, not part of the language.
        /// </summary>
        public const string LabelOffset = "LabelOffset";

        /// <summary>
        /// Prefix of the routing point attributes on an arc: Waypoint_1,
        /// Waypoint_2, ... each typed against OMG_DD/DD_Waypoint, ordered from
        /// source to target. Instance attributes by the same convention the FPD
        /// mapper uses, stated in the PT_Arc description in ISO_PT v0.3.
        ///
        /// The difference between the two languages is where they live: the FPD
        /// has no element to hang them on because its connections are
        /// InternalLinks, whereas a PT arc is an element in its own right. The
        /// ViewInformation rectangle PT_Arc inherits from PT_Element stays
        /// unused for arcs.
        /// </summary>
        public const string WaypointPrefix = "Waypoint_";
    }

    /// <summary>Fixed SystemUnitClass ids, taken from the library file.</summary>
    public static class ClassIds
    {
        public const string Net = "c1a8d000-0001-4000-8000-000000000001";
        public const string Element = "c1a8d000-0001-4000-8000-000000000002";
        public const string Place = "c1a8d000-0001-4000-8000-000000000003";
        public const string Transition = "c1a8d000-0001-4000-8000-000000000004";
        public const string Arc = "c1a8d000-0001-4000-8000-000000000005";
    }
}

/// <summary>The AutomationML base libraries, referenced but never emitted.</summary>
public static class AmlBase
{
    public const string Alias = "AutomationMLBaseLibrariesAMLEd22_11_0";

    /// <summary>
    /// A file name, resolved by AML tools through their library search path.
    /// Not a URL: the path is copied into every document the mapper writes, and
    /// a share link with an access token in it would travel with every file.
    /// The AutomationML Editor loads no referenced document in any form anyway;
    /// it takes the base libraries from its library manager.
    /// </summary>
    public const string Path = "AutomationML_Base_Libraries_AMLEd2_2.11.0.aml";

    public const string Port = Alias + "@AutomationMLInterfaceClassLib/AutomationMLBaseInterface/Port";
    public const string BaseRole = Alias + "@AutomationMLBaseRoleClassLib/AutomationMLBaseRole";
    public const string Structure = BaseRole + "/Structure";
}

/// <summary>
/// Rainer Drath's object reference attribute types, published as an official
/// AutomationML artefact and referenced here rather than reproduced.
/// </summary>
public static class ObjectReferencesLibrary
{
    public const string Alias = "ObjectReferences";
    public const string LibName = "AutomationML_ObjectReferences_AttributeTypeLib";
    public const string FileName = "AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml";

    public const string RefObj = Alias + "@" + LibName + "/refObj";
}
