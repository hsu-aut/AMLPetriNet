using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// The language-agnostic diagram-interchange AttributeTypeLib shared by all
/// domain libraries built with the three-phase methodology (FPD, P/T Petri
/// nets, ...). Its three types take their structure from the OMG Diagram
/// Definition (DD) specification: <c>DD_Bounds</c> (DC::Bounds), <c>DD_Point</c>
/// (DC::Point) and <c>DD_Waypoint</c> (DI::Waypoint).
///
/// The FPD mapper, fpb-aml-mapper (https://github.com/hsu-aut/fpb-aml-mapper),
/// emits the same library. The two ship as independent repositories, so each
/// carries its own copy of this helper rather than depending on the other.
/// Keep the emitted library byte identical across both, it is the same
/// published artefact.
///
/// The library is a separate AML asset (<see cref="FileName"/>) referenced
/// through an ExternalReference with alias <see cref="Alias"/>; the paths below
/// are alias-qualified accordingly. <see cref="BuildDocument"/> produces that
/// asset, <see cref="EnsureIn"/> embeds a copy into a document (self-contained
/// variant).
/// </summary>
public static class DiagramInterchangeLibrary
{
    public const string LibName  = "OMG_DD_AttributeTypeLib";
    public const string Version  = "0.1.0";
    public const string FileName = "OMG_DD_AttributeTypeLib_v0.1.aml";
    public const string Alias    = "OMG_DD";

    public const string QualifiedLibName = Alias + "@" + LibName;

    public const string BoundsAttributeTypePath   = QualifiedLibName + "/DD_Bounds";
    public const string PointAttributeTypePath    = QualifiedLibName + "/DD_Point";
    public const string WaypointAttributeTypePath = QualifiedLibName + "/DD_Waypoint";

    /// <summary>Build the library as a standalone CAEX document.</summary>
    public static CAEXDocument BuildDocument()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var caex = doc.CAEXFile;
        caex.FileName = FileName;
        var sdi = caex.SourceDocumentInformation.FirstOrDefault() ?? caex.SourceDocumentInformation.Append();
        sdi.OriginName = "fpb-aml-mapper";
        sdi.OriginID = "omg-dd-attributetypelib";
        sdi.OriginVersion = Version;
        sdi.LastWritingDateTime = DateTime.UtcNow;
        EnsureIn(caex);
        return doc;
    }

    /// <summary>
    /// Append the library to <paramref name="caex"/> if it is not present.
    /// Inside the library the nested <c>position</c> attributes reference
    /// <c>DD_Point</c> by its document-internal path.
    /// </summary>
    public static AttributeTypeLibType EnsureIn(CAEXFileType caex)
    {
        var existing = caex.AttributeTypeLib[LibName];
        if (existing != null) return existing;

        var atl = caex.AttributeTypeLib.Append(LibName);
        atl.Description = "Language-agnostic diagram-interchange attribute types, structured after the OMG Diagram Definition (DD) specification: DD_Bounds (DC::Bounds), DD_Point (DC::Point), DD_Waypoint (DI::Waypoint). Referenced by the VDI_FPD and ISO_PT domain libraries.";
        atl.Version = Version;

        var pointPath = LibName + "/DD_Point";

        var bounds = atl.AttributeType.Append("DD_Bounds");
        bounds.AttributeDataType = "xs:string";
        bounds.Description = "A rectangular area defined by a top-left (x, y) location and a size (width, height) along the x-y axes (cf. DC::Bounds).";
        bounds.Version = Version;
        AddPoint(bounds, "position", pointPath);
        AddDouble(bounds, "width");
        AddDouble(bounds, "height");

        var wp = atl.AttributeType.Append("DD_Waypoint");
        wp.AttributeDataType = "xs:string";
        wp.Description = "A routing point along a connection path (cf. DI::Waypoint).";
        wp.Version = Version;
        AddPoint(wp, "position", pointPath);

        var pt = atl.AttributeType.Append("DD_Point");
        pt.AttributeDataType = "xs:string";
        pt.Description = "A two-dimensional point in a coordinate system (cf. DC::Point).";
        pt.Version = Version;
        AddDouble(pt, "x");
        AddDouble(pt, "y");

        return atl;
    }

    private static void AddPoint(IObjectWithAttributes parent, string name, string pointPath)
    {
        var attr = parent.Attribute.Append(name);
        attr.AttributeDataType = "xs:string";
        attr.RefAttributeType = pointPath;
        AddDouble(attr, "x");
        AddDouble(attr, "y");
    }

    private static void AddDouble(IObjectWithAttributes parent, string name)
    {
        var attr = parent.Attribute.Append(name);
        attr.AttributeDataType = "xs:double";
    }
}
