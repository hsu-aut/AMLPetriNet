using System.Globalization;
using Aml.Engine.CAEX;
using Aml.Engine.CAEX.Extensions;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// The CAEX writing primitives shared by <see cref="PtNetToCaex"/>, which builds
/// a hierarchy from scratch, and <see cref="PtNetUpdater"/>, which edits one that
/// already exists. Both have to produce the exact same shape, so the operations
/// live here once rather than being written twice.
/// </summary>
internal static class PtWrite
{
    /// <summary>
    /// Attributes the PT language owns on an element. Everything else is
    /// someone's own addition, and an update never touches it.
    /// </summary>
    internal static bool IsLanguageOwned(string? attributeName) =>
        attributeName != null
        && (attributeName == PtNames.Attributes.Identification
            || attributeName == PtNames.Attributes.ViewInformation
            || attributeName == PtNames.Attributes.LabelOffset
            || attributeName == PtNames.Attributes.InitialMarking
            || attributeName == PtNames.Attributes.Weight
            || attributeName == PtNames.Attributes.IsSilent
            || attributeName.StartsWith(PtNames.Attributes.WaypointPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Which of the language's attributes belong on this kind of element. A
    /// place has a marking and no weight, an arc the other way round.
    /// </summary>
    private static bool BelongsOn(string kind, string attributeName) => attributeName switch
    {
        PtNames.Attributes.InitialMarking => kind == PtNames.Place,
        PtNames.Attributes.Weight => kind == PtNames.Arc,
        PtNames.Attributes.IsSilent => kind == PtNames.Transition,
        PtNames.Attributes.ViewInformation => kind != PtNames.Arc,
        // An arc has a label too, its inscription, and its position is stored
        // the same way. Excluding arcs here removed that attribute on every
        // update and appended it again, moving it to the end of the element.
        _ => true,
    };

    /// <summary>
    /// Drops attributes of the language that do not belong on this kind of
    /// element. They appear when an element changed type by hand, or when an
    /// older version of the mapper wrote something a later one no longer does.
    /// A user's own attributes are never candidates: only names the language
    /// claims are considered at all.
    /// </summary>
    internal static void RemoveForeignLanguageAttributes(InternalElementType ie, string kind)
    {
        foreach (var attribute in ie.Attribute
                     .Where(a => IsLanguageOwned(a.Name) && !BelongsOn(kind, a.Name!))
                     .ToList())
        {
            attribute.Remove();
        }
    }

    // ── instantiation ───────────────────────────────────────────────────────

    internal static Dictionary<string, SystemUnitFamilyType> SystemUnitClasses(CAEXDocument doc)
    {
        var lib = doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]
            ?? throw new InvalidOperationException($"SystemUnitClassLib '{PtNames.SystemUnitClassLib}' is missing.");

        // The first class of a name wins. A library someone edited by hand can
        // carry a class twice, and ToDictionary threw on that halfway through
        // an update.
        var classes = new Dictionary<string, SystemUnitFamilyType>(StringComparer.Ordinal);
        foreach (var suc in lib.SystemUnitClass)
            if (!string.IsNullOrEmpty(suc.Name)) classes.TryAdd(suc.Name, suc);
        return classes;
    }

    internal static InternalElementType CreateInstance(Dictionary<string, SystemUnitFamilyType> classes, string className)
    {
        if (!classes.TryGetValue(className, out var suc))
            throw new InvalidOperationException($"SystemUnitClass '{className}' not found in library.");

        var ie = (InternalElementType)suc.CreateClassInstance(className);

        // CreateClassInstance only copies the first SupportedRoleClass into a
        // RoleRequirement; PT_Net has two, so the rest are added here.
        var present = new HashSet<string>(ie.RoleRequirements.Select(r => r.RefBaseRoleClassPath));
        foreach (var supported in suc.SupportedRoleClass)
        {
            if (!present.Contains(supported.RefRoleClassPath))
                ie.RoleRequirements.Append().RefBaseRoleClassPath = supported.RefRoleClassPath;
        }

        return ie;
    }

    /// <summary>
    /// asFirst defaults to true in Aml.Engine, which would reverse document
    /// order on every insert. Always append.
    /// </summary>
    internal static void Append(InternalElementType parent, InternalElementType child) => parent.Insert(child, false);

    internal static void Append(InstanceHierarchyType parent, InternalElementType child) => parent.Insert(child, false);

    // ── naming ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A node carries one endpoint per incident arc. A bipartite net has no
    /// self-loop, so the arc id alone identifies the endpoint on a node.
    /// </summary>
    internal static string EndpointName(PtArc arc, PtNode node) =>
        arc.SourceId == node.Id ? $"Out_{arc.Id}" : $"In_{arc.Id}";

    internal static string DisplayName(string? name, string fallbackId) =>
        string.IsNullOrWhiteSpace(name) ? fallbackId : name!;

    /// <summary>
    /// Sets the CAEX name, which is a display name: since ISO_PT v0.3 the
    /// language keeps its name in Identification/name.
    ///
    /// Another tool may have called an element something more telling than its
    /// PNML name, as BridgingExample_FPD_PT_v0.1.aml does with
    /// "Arc_Idle_to_Start" for the arc named "a1". A sync in which nobody
    /// renamed anything has no business replacing that, so the name is only
    /// written when it actually differs from what the document has stored.
    /// </summary>
    /// <param name="created">
    /// Whether this element was just instantiated. A fresh instance is named
    /// after its class, so there is no name of the document's to keep.
    /// </param>
    internal static void SetDisplayName(InternalElementType ie, string? name, string fallbackId, bool created)
    {
        if (!created)
        {
            var stored = ie.Attribute[PtNames.Attributes.Identification]?
                .Attribute[PtNames.Attributes.Name]?.Value;

            if (string.Equals(Trimmed(stored), Trimmed(name), StringComparison.Ordinal)) return;
        }

        ie.Name = DisplayName(name, fallbackId);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    // ── element content ─────────────────────────────────────────────────────

    /// <summary>Writes everything the language owns on a node.</summary>
    internal static void WriteNode(InternalElementType ie, PtNode node, bool created)
    {
        RemoveForeignLanguageAttributes(ie, node is PtPlace ? PtNames.Place : PtNames.Transition);
        SetDisplayName(ie, node.Name, node.Id, created);
        SetIdentification(ie, node.Id, node.Name);
        SetViewInformation(ie, node.Bounds);
        SetLabelOffset(ie, node.LabelOffset);

        switch (node)
        {
            case PtPlace place:
                SetValue(ie, PtNames.Attributes.InitialMarking,
                    place.InitialMarking.ToString(CultureInfo.InvariantCulture));
                break;

            case PtTransition transition:
                // Not part of ISO/IEC 15909-1; kept so an editor round trip does
                // not silently drop it. See Pnml.Write.
                if (transition.IsSilent)
                {
                    var silent = ie.Attribute[PtNames.Attributes.IsSilent] ?? ie.Attribute.Append(PtNames.Attributes.IsSilent);
                    silent.AttributeDataType = "xs:boolean";
                    silent.Value = "true";
                }
                else
                {
                    ie.Attribute[PtNames.Attributes.IsSilent]?.Remove();
                }
                break;
        }
    }

    /// <summary>Writes everything the language owns on an arc, except its interfaces.</summary>
    internal static void WriteArc(InternalElementType ie, PtArc arc, bool created)
    {
        RemoveForeignLanguageAttributes(ie, PtNames.Arc);

        // An arc that arrives without a name keeps the one it has. The modeler
        // never shows an arc's name and offers no way to edit it, so a missing
        // name cannot mean the user cleared it, only that it was lost on the
        // way. That happened once to every arc of the MPS500 example.
        var name = arc.Name;
        if (!created && string.IsNullOrWhiteSpace(name))
            name = ie.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Name]?.Value;

        SetDisplayName(ie, name, arc.Id, created);
        SetIdentification(ie, arc.Id, name);
        SetValue(ie, PtNames.Attributes.Weight, arc.Weight.ToString(CultureInfo.InvariantCulture));
        SetLabelOffset(ie, arc.LabelOffset);
        ReplaceWaypoints(ie, arc.Waypoints);
    }

    internal static void SetIdentification(InternalElementType ie, string id, string? name)
    {
        // Since ISO_PT v0.3 both PT_Element and PT_Net declare Identification, so
        // instantiation brings it along. The fallback covers an element created
        // against an older copy of the library, or by hand.
        var identification = ie.Attribute[PtNames.Attributes.Identification]
            ?? AppendTyped(ie, PtNames.Attributes.Identification, $"{PtNames.AttributeTypeLib}/PT_Identification");

        var idAttr = identification.Attribute[PtNames.Attributes.Id]
            ?? AppendString(identification, PtNames.Attributes.Id);

        // Do not overwrite an id that only differs by the normalisation the
        // reader applied. A document whose Identification/id is a braced CAEX
        // GUID keeps it; rewriting would break whatever else points at it.
        if (!PtIdText.SameAfterNormalisation(idAttr.Value, id)) idAttr.Value = id;

        var nameAttr = identification.Attribute[PtNames.Attributes.Name]
            ?? AppendString(identification, PtNames.Attributes.Name);
        nameAttr.Value = name ?? string.Empty;
    }

    internal static void SetViewInformation(InternalElementType ie, PtBounds? bounds)
    {
        if (bounds == null) return;

        var view = ie.Attribute[PtNames.Attributes.ViewInformation]
            ?? AppendTyped(ie, PtNames.Attributes.ViewInformation, PtDiPaths.Bounds(ie));

        var position = view.Attribute[PtNames.Attributes.Position]
            ?? AppendTyped(view, PtNames.Attributes.Position, PtDiPaths.Point(ie));

        SetChild(position, PtNames.Attributes.X, bounds.X);
        SetChild(position, PtNames.Attributes.Y, bounds.Y);
        SetChild(view, PtNames.Attributes.Width, bounds.Width);
        SetChild(view, PtNames.Attributes.Height, bounds.Height);
    }

    internal static void SetLabelOffset(InternalElementType ie, PtPoint? offset)
    {
        var existing = ie.Attribute[PtNames.Attributes.LabelOffset];
        if (offset == null)
        {
            // A label moved back to its default position must not leave the old
            // offset behind, or the next import would move it away again.
            existing?.Remove();
            return;
        }

        var attribute = existing
            ?? AppendTyped(ie, PtNames.Attributes.LabelOffset, PtDiPaths.Point(ie));
        SetChild(attribute, PtNames.Attributes.X, offset.X);
        SetChild(attribute, PtNames.Attributes.Y, offset.Y);
    }

    internal static void SetPortCoordinate(ExternalInterfaceType iface, PtPoint point)
    {
        var coordinate = iface.Attribute[PtNames.Attributes.PortCoordinate]
            ?? AppendTyped(iface, PtNames.Attributes.PortCoordinate, PtDiPaths.Point(iface));
        SetChild(coordinate, PtNames.Attributes.X, point.X);
        SetChild(coordinate, PtNames.Attributes.Y, point.Y);
    }

    /// <summary>
    /// The routing points are owned wholesale by the diagram, so the old set goes
    /// before the new one is written; otherwise a shortened polyline would keep
    /// its tail.
    /// </summary>
    internal static void ReplaceWaypoints(InternalElementType ie, IReadOnlyList<PtPoint> waypoints)
    {
        foreach (var stale in ie.Attribute
                     .Where(a => a.Name != null && a.Name.StartsWith(PtNames.Attributes.WaypointPrefix, StringComparison.Ordinal))
                     .ToList())
        {
            stale.Remove();
        }

        for (var i = 0; i < waypoints.Count; i++)
        {
            var waypoint = AppendTyped(ie,
                PtNames.Attributes.WaypointPrefix + (i + 1).ToString(CultureInfo.InvariantCulture),
                PtDiPaths.Waypoint(ie));
            var position = AppendTyped(waypoint, PtNames.Attributes.Position, PtDiPaths.Point(ie));
            SetChild(position, PtNames.Attributes.X, waypoints[i].X);
            SetChild(position, PtNames.Attributes.Y, waypoints[i].Y);
        }
    }

    // ── interfaces and links ────────────────────────────────────────────────

    internal static ExternalInterfaceType EnsureInterface(
        InternalElementType ie, string name, string id, string classPath)
    {
        var existing = ie.ExternalInterface.FirstOrDefault(i => i.ID == id)
            ?? ie.ExternalInterface.FirstOrDefault(i => i.Name == name);

        if (existing != null)
        {
            // Found by name: keep the id and the class the document gave it.
            // Reassigning the id would break whatever else points at the
            // interface, which is the one thing a sync must never do.
            existing.Name = name;
            if (string.IsNullOrEmpty(existing.ID)) existing.ID = id;
            if (string.IsNullOrEmpty(existing.RefBaseClassPath)) existing.RefBaseClassPath = classPath;
            return existing;
        }

        var added = ie.ExternalInterface.Append(name);
        added.ID = id;
        added.RefBaseClassPath = classPath;
        return added;
    }

    internal static void EnsureLink(
        InternalElementType parent, string name, string id,
        ExternalInterfaceType a, ExternalInterfaceType b)
    {
        var link = parent.InternalLink.FirstOrDefault(l => l.ID == id);
        if (link == null)
        {
            link = parent.InternalLink.Append(name);
            link.ID = id;
        }
        else
        {
            link.Name = name;
        }

        link.AInterface = a;
        link.BInterface = b;
    }

    // ── small helpers ───────────────────────────────────────────────────────

    internal static AttributeType AppendTyped(IObjectWithAttributes parent, string name, string attributeTypePath)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = attributeTypePath;
        return attribute;
    }

    internal static AttributeType AppendString(IObjectWithAttributes parent, string name)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
        return attribute;
    }

    internal static void SetChild(AttributeType parent, string name, double value)
    {
        var child = parent.Attribute[name] ?? parent.Attribute.Append(name);
        if (string.IsNullOrEmpty(child.AttributeDataType)) child.AttributeDataType = "xs:double";
        child.Value = value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    internal static void SetValue(InternalElementType ie, string name, string value)
    {
        var attribute = ie.Attribute[name] ?? ie.Attribute.Append(name);
        attribute.Value = value;
    }
}
