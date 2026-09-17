using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// Builds the ISO_PT domain library for basic place/transition nets
/// (ISO/IEC 15909-1) inside a CAEX document.
///
/// A code reproduction of the published artefact
/// <c>ISO_PT_DomainLibrary_v0.3.aml</c>. A golden test compares what this
/// emits against that file, so the published library stays the authority.
///
/// The characteristic difference to the FPD library: arcs are first-class
/// elements with an identity of their own, so PT_Arc is a role and system unit
/// class like PT_Place and PT_Transition, and node-side and arc-side connection
/// endpoints are distinct interface classes.
/// </summary>
public static class PtLibraries
{
    /// <param name="embedShared">
    /// Whether to put the two small shared libraries into the document instead
    /// of pointing at them. True for a document a person will open, false when
    /// building the library artefact itself, which is published as a file next
    /// to the ones it references.
    /// </param>
    public static void EnsureLibraries(CAEXFileType caex, bool embedShared = false)
    {
        ArgumentNullException.ThrowIfNull(caex);

        // Only touch the shared libraries when the ISO_PT ones are actually
        // created here: a document that already carries them keeps its layout.
        var fresh = caex.SystemUnitClassLib[PtNames.SystemUnitClassLib] == null;

        // The AML base libraries stay a reference by file name. Every AML tool
        // knows them, and inlining them would dwarf the document.
        EnsureExternalReference(caex, AmlBase.Alias, AmlBase.Path);

        EnsureInterfaceClassLib(caex);
        EnsureRoleClassLib(caex);
        EnsureAttributeTypeLib(caex);
        EnsureSystemUnitClassLib(caex);

        if (!fresh) return;

        if (embedShared)
        {
            EmbedSharedLibraries(caex);
        }
        else
        {
            EnsureExternalReference(caex, ObjectReferencesLibrary.Alias, ObjectReferencesLibrary.FileName);
            EnsureExternalReference(caex, DiagramInterchangeLibrary.Alias, DiagramInterchangeLibrary.FileName);
        }
    }

    /// <summary>
    /// Puts the two small shared libraries into the document rather than
    /// pointing at them, and rewrites the paths in the classes just created to
    /// match.
    ///
    /// The AML Editor does not follow file references. A net created there and
    /// saved anywhere other than next to the library files opened with its
    /// geometry and refObj attributes unresolved, which is the state a user
    /// sees as a broken document. Six kilobytes buy a file that stands on its
    /// own.
    ///
    /// A document that already names one of the two, inline or by reference, is
    /// left alone: it has made its choice, and <see cref="PtDiPaths"/> follows
    /// whichever form it uses.
    /// </summary>
    private static void EmbedSharedLibraries(CAEXFileType caex)
    {
        foreach (var (alias, libName, embed) in new (string Alias, string LibName, Func<CAEXFileType, bool> Embed)[]
                 {
                     (DiagramInterchangeLibrary.Alias, DiagramInterchangeLibrary.LibName,
                         c => { DiagramInterchangeLibrary.EnsureIn(c); return true; }),
                     (ObjectReferencesLibrary.Alias, ObjectReferencesLibrary.LibName,
                         ObjectReferencesLibraryFile.EnsureIn),
                 })
        {
            var known = caex.AttributeTypeLib[libName] != null
                || caex.ExternalReference.Any(r => r.Alias == alias);

            if (known)
            {
                // Referenced but not carried, and this document was written by
                // someone who chose that. Keep the reference and the alias.
                if (caex.AttributeTypeLib[libName] == null)
                    EnsureExternalReference(caex, alias, FileNameFor(alias));
                continue;
            }

            if (embed(caex)) StripAliasInOwnLibraries(caex, alias);
        }
    }

    private static string FileNameFor(string alias) =>
        alias == DiagramInterchangeLibrary.Alias
            ? DiagramInterchangeLibrary.FileName
            : ObjectReferencesLibrary.FileName;

    /// <summary>
    /// Removes an alias prefix from the class paths inside the ISO_PT libraries.
    ///
    /// Scoped to those libraries on purpose. The prefix means "through the
    /// external reference with this alias", and once the library is in the
    /// document that is no longer true for the classes written here. Anything
    /// else in the document keeps whatever it says: another hierarchy's paths
    /// are not this mapper's to rewrite.
    /// </summary>
    private static void StripAliasInOwnLibraries(CAEXFileType caex, string alias)
    {
        var prefix = alias + "@";

        foreach (var library in OwnLibraries(caex))
        foreach (var attribute in library.DescendantsAndSelf().Attributes())
        {
            if (!attribute.Name.LocalName.StartsWith("Ref", StringComparison.Ordinal)) continue;
            if (!attribute.Value.StartsWith(prefix, StringComparison.Ordinal)) continue;

            attribute.Value = attribute.Value[prefix.Length..];
        }
    }

    private static IEnumerable<System.Xml.Linq.XElement> OwnLibraries(CAEXFileType caex)
    {
        if (caex.InterfaceClassLib[PtNames.InterfaceClassLib] is { } icl) yield return icl.Node;
        if (caex.RoleClassLib[PtNames.RoleClassLib] is { } rcl) yield return rcl.Node;
        if (caex.SystemUnitClassLib[PtNames.SystemUnitClassLib] is { } sucl) yield return sucl.Node;
        if (caex.AttributeTypeLib[PtNames.AttributeTypeLib] is { } atl) yield return atl.Node;
    }

    internal static void EnsureExternalReference(CAEXFileType caex, string alias, string path)
    {
        foreach (var reference in caex.ExternalReference)
            if (reference.Alias == alias) return;

        var added = caex.ExternalReference.Append();
        added.Alias = alias;
        added.Path = path;
    }

    // ── InterfaceClassLib ───────────────────────────────────────────────────

    private static void EnsureInterfaceClassLib(CAEXFileType caex)
    {
        if (caex.InterfaceClassLib[PtNames.InterfaceClassLib] != null) return;

        var icl = caex.InterfaceClassLib.Append(PtNames.InterfaceClassLib);
        icl.Description =
            "Connection endpoint interfaces for basic Place/Transition Petri nets per ISO/IEC 15909-1. " +
            "In contrast to the FPD library, connections (arcs) are elements with an identity of their own: " +
            "node-side and arc-side interfaces are distinct types.";
        icl.Version = PtNames.LibraryVersion;

        var port = icl.InterfaceClass.Append(PtNames.Port);
        port.Description = "Abstract base port for all PT connection endpoints.";
        port.Version = PtNames.Version;
        port.RefBaseClassPath = AmlBase.Port;
        AddPoint(port, PtNames.Attributes.PortCoordinate, DiagramInterchangeLibrary.PointAttributeTypePath);

        AddInterface(icl, PtNames.NodeArcEnd,
            "Connection endpoint on a place or transition (the node side of an arc-node coupling). " +
            "Nodes carry one PT_NodeArcEnd per incident arc.");
        AddInterface(icl, PtNames.ArcSource,
            "Source endpoint of an arc. Links via InternalLink to a PT_NodeArcEnd on the source place or transition.");
        AddInterface(icl, PtNames.ArcTarget,
            "Target endpoint of an arc. Links via InternalLink to a PT_NodeArcEnd on the target place or transition.");
    }

    private static void AddInterface(InterfaceClassLibType icl, string name, string description)
    {
        var ic = icl.InterfaceClass.Append(name);
        ic.Description = description;
        ic.Version = PtNames.Version;
        ic.RefBaseClassPath = PtNames.Interface(PtNames.Port);
    }

    // ── RoleClassLib ────────────────────────────────────────────────────────

    private static void EnsureRoleClassLib(CAEXFileType caex)
    {
        if (caex.RoleClassLib[PtNames.RoleClassLib] != null) return;

        var rcl = caex.RoleClassLib.Append(PtNames.RoleClassLib);
        rcl.Description =
            "Semantic model of basic Place/Transition Petri nets per ISO/IEC 15909-1. " +
            "Flat inheritance; three concrete element types (Place, Transition, Arc), one container role (Net).";
        rcl.Version = PtNames.LibraryVersion;

        var net = rcl.RoleClass.Append(PtNames.Net);
        net.Description = "Petri net (ISO/IEC 15909-1, Sec. 4). Aggregates places, transitions, and arcs of one diagram.";
        net.Version = PtNames.Version;
        net.RefBaseClassPath = AmlBase.Structure;
        // The net carries the PNML <net> id and name. Added in v0.3: without it
        // a round trip had nowhere to put the id and renamed the net after its
        // label.
        AddIdentification(net).Description = "Identification of the net (PNML net id and name).";
        var refObj = AddRefObj(net);
        refObj.Description = "Generic reference attribute for cross-diagram or cross-language references at net level.";

        var element = rcl.RoleClass.Append(PtNames.Element);
        element.Description =
            "Abstract base for all PT elements (Place, Transition, Arc). " +
            "Carries identification and diagram interchange information shared by all elements.";
        element.Version = PtNames.Version;
        element.RefBaseClassPath = AmlBase.BaseRole;
        AddIdentification(element);
        AddViewInformation(element);

        var place = rcl.RoleClass.Append(PtNames.Place);
        place.Description = "Place (ISO/IEC 15909-1, Sec. 4.1). Passive node type; may carry an initial marking.";
        place.Version = PtNames.Version;
        place.RefBaseClassPath = PtNames.Role(PtNames.Element);
        var marking = AddInitialMarking(place);
        marking.Description = "Static initial marking (non-negative integer). Default 0.";

        var transition = rcl.RoleClass.Append(PtNames.Transition);
        transition.Description = "Transition (ISO/IEC 15909-1, Sec. 4.2). Active node type representing a firing event.";
        transition.Version = PtNames.Version;
        transition.RefBaseClassPath = PtNames.Role(PtNames.Element);

        var arc = rcl.RoleClass.Append(PtNames.Arc);
        arc.Description =
            "Arc (ISO/IEC 15909-1, Sec. 4.3). Directed connection between a place and a transition. " +
            "Element with an identity of its own, a weight, and a graphical representation. " +
            "Routing is stored on the instance as attributes Waypoint_1..n typed " +
            "OMG_DD@OMG_DD_AttributeTypeLib/DD_Waypoint, in the order from source to target. " +
            "Label positions may be stored as attributes typed " +
            "OMG_DD@OMG_DD_AttributeTypeLib/DD_Point (e.g. LabelOffset).";
        arc.Version = PtNames.Version;
        arc.RefBaseClassPath = PtNames.Role(PtNames.Element);
        var weight = AddWeight(arc);
        weight.Description = "Arc weight (positive integer). Default 1.";
    }

    // ── SystemUnitClassLib ──────────────────────────────────────────────────

    private static void EnsureSystemUnitClassLib(CAEXFileType caex)
    {
        if (caex.SystemUnitClassLib[PtNames.SystemUnitClassLib] != null) return;

        var sucl = caex.SystemUnitClassLib.Append(PtNames.SystemUnitClassLib);
        sucl.Description =
            "Instantiation templates for the PT domain library. Mirrors the RoleClass hierarchy; " +
            "each template references its role class via SupportedRoleClass, following the " +
            "semantics/instantiation separation established in the FPD library.";
        sucl.Version = PtNames.LibraryVersion;

        var net = sucl.SystemUnitClass.Append(PtNames.Net);
        net.ID = PtNames.ClassIds.Net;
        net.Version = PtNames.Version;
        AddIdentification(net);
        AddRefObj(net);
        AddSupportedRole(net, PtNames.Role(PtNames.Net));
        AddSupportedRole(net, AmlBase.Structure);

        var element = sucl.SystemUnitClass.Append(PtNames.Element);
        element.ID = PtNames.ClassIds.Element;
        element.Version = PtNames.Version;
        AddIdentification(element);
        AddViewInformation(element);
        AddSupportedRole(element, PtNames.Role(PtNames.Element));

        var place = sucl.SystemUnitClass.Append(PtNames.Place);
        place.ID = PtNames.ClassIds.Place;
        place.Version = PtNames.Version;
        place.RefBaseClassPath = PtNames.SystemUnit(PtNames.Element);
        AddInitialMarking(place);
        AddSupportedRole(place, PtNames.Role(PtNames.Place));

        var transition = sucl.SystemUnitClass.Append(PtNames.Transition);
        transition.ID = PtNames.ClassIds.Transition;
        transition.Version = PtNames.Version;
        transition.RefBaseClassPath = PtNames.SystemUnit(PtNames.Element);
        AddSupportedRole(transition, PtNames.Role(PtNames.Transition));

        var arc = sucl.SystemUnitClass.Append(PtNames.Arc);
        arc.ID = PtNames.ClassIds.Arc;
        arc.Version = PtNames.Version;
        arc.RefBaseClassPath = PtNames.SystemUnit(PtNames.Element);
        AddWeight(arc);
        AddSupportedRole(arc, PtNames.Role(PtNames.Arc));
    }

    private static void AddSupportedRole(SystemUnitFamilyType suc, string rolePath)
    {
        var supported = suc.SupportedRoleClass.Append();
        supported.RefRoleClassPath = rolePath;
    }

    // ── AttributeTypeLib ────────────────────────────────────────────────────

    private static void EnsureAttributeTypeLib(CAEXFileType caex)
    {
        if (caex.AttributeTypeLib[PtNames.AttributeTypeLib] != null) return;

        var atl = caex.AttributeTypeLib.Append(PtNames.AttributeTypeLib);
        atl.Description =
            "Information-model attribute types for PT nets. Kept intentionally minimal to reflect the " +
            "small ontology of ISO/IEC 15909-1 (as opposed to VDI 3682 Part 2, which specifies a richer " +
            "information model).";
        atl.Version = PtNames.LibraryVersion;

        var identification = atl.AttributeType.Append("PT_Identification");
        identification.AttributeDataType = "xs:string";
        identification.Description =
            "Identification of a PT element. Corresponds to the PNML object identification per " +
            "ISO/IEC 15909-2: id maps to the PNML id attribute, name maps to the PNML name label.";
        identification.Version = PtNames.Version;
        AddString(identification, PtNames.Attributes.Id);
        AddString(identification, PtNames.Attributes.Name);
    }

    // ── attribute helpers, shared by roles and system unit classes ──────────

    private static AttributeType AddRefObj(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(PtNames.Attributes.RefObj);
        attribute.AttributeDataType = "xs:IDREF";
        attribute.RefAttributeType = ObjectReferencesLibrary.RefObj;
        return attribute;
    }

    private static AttributeType AddIdentification(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(PtNames.Attributes.Identification);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = $"{PtNames.AttributeTypeLib}/PT_Identification";
        AddString(attribute, PtNames.Attributes.Id);
        AddString(attribute, PtNames.Attributes.Name);
        return attribute;
    }

    private static void AddViewInformation(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(PtNames.Attributes.ViewInformation);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = DiagramInterchangeLibrary.BoundsAttributeTypePath;
        AddPoint(attribute, PtNames.Attributes.Position, DiagramInterchangeLibrary.PointAttributeTypePath);
        AddDouble(attribute, PtNames.Attributes.Width);
        AddDouble(attribute, PtNames.Attributes.Height);
    }

    private static AttributeType AddInitialMarking(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(PtNames.Attributes.InitialMarking);
        attribute.AttributeDataType = "xs:nonNegativeInteger";
        attribute.DefaultValue = "0";
        return attribute;
    }

    private static AttributeType AddWeight(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(PtNames.Attributes.Weight);
        attribute.AttributeDataType = "xs:positiveInteger";
        attribute.DefaultValue = "1";
        return attribute;
    }

    private static void AddPoint(IObjectWithAttributes parent, string name, string pointTypePath)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = pointTypePath;
        AddDouble(attribute, PtNames.Attributes.X);
        AddDouble(attribute, PtNames.Attributes.Y);
    }

    private static void AddString(IObjectWithAttributes parent, string name)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
    }

    private static void AddDouble(IObjectWithAttributes parent, string name)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:double";
    }
}
