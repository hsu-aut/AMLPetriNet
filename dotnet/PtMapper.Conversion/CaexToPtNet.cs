using System.Xml.Linq;
using System.Globalization;
using Aml.Engine.CAEX;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// Reads place/transition nets back out of a CAEX document.
///
/// Deliberately tolerant: the document may have been edited by hand in the AML
/// Editor between two visits, so element types are recognised from either the
/// system unit class path or the role requirement, and a missing Identification
/// falls back to the element name.
/// </summary>
public static class CaexToPtNet
{
    /// <summary>All PT nets in the document, in document order.</summary>
    public static List<PtNet> ReadAll(CAEXDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return doc.CAEXFile.InstanceHierarchy
            .SelectMany(ih => ih.InternalElement)
            .Where(IsNet)
            .Select(ReadNet)
            .ToList();
    }

    /// <summary>The nets of one instance hierarchy.</summary>
    public static List<PtNet> Read(InstanceHierarchyType ih)
    {
        ArgumentNullException.ThrowIfNull(ih);
        return ih.InternalElement.Where(IsNet).Select(ReadNet).ToList();
    }

    /// <summary>Whether a hierarchy holds Petri net content at all.</summary>
    public static bool ContainsNet(InstanceHierarchyType ih) => ih.InternalElement.Any(IsNet);

    private static PtNet ReadNet(InternalElementType netIE)
    {
        // Identifiers stored in CAEX are not automatically usable as PNML ids.
        // A document written by another tool routinely carries the CAEX GUID,
        // braces and all, in Identification/id, and the modeler's parser refuses
        // anything that is not an XML name. Normalising here means such a file
        // opens and can be edited; PtWrite keeps the original in the document,
        // and PtNetUpdater matches through the same normalisation.
        var names = new HashSet<string>(StringComparer.Ordinal);
        string Id(string? raw) => PtIdText.ToDistinctXmlName(raw, names);

        // Identification is where the PNML net id and name live; the CAEX name
        // is only what the editor's tree displays. A net someone created by
        // hand has no Identification, and then the CAEX name is all there is.
        var net = new PtNet
        {
            Id = Id(IdentificationId(netIE) ?? netIE.Name ?? "net"),
            Name = IdentificationName(netIE),
        };

        // Interface id -> owning NODE id, so the arc links below resolve to nodes.
        // Arc interfaces are deliberately not in here: a link from one arc to
        // another would otherwise resolve an arc as an endpoint, and the modeler
        // then drops both arcs at import while reporting success.
        var interfaceOwner = new Dictionary<string, string>(PtCaex.Ids);

        // The name each child was claimed under. The arc pass below reuses it
        // rather than deriving it again: deriving would skip the numbering that
        // resolves a collision, and two arcs would leave here with one id.
        var claimedName = new Dictionary<XElement, string>();

        foreach (var child in netIE.InternalElement)
        {
            var raw = PnmlIdOf(child);
            if (raw == null) continue;
            var kind = ElementKindOf(child);
            if (kind is not (PtNames.Place or PtNames.Transition or PtNames.Arc)) continue;
            var id = Id(raw);
            claimedName[child.Node] = id;

            if (kind != PtNames.Arc)
            {
                foreach (var iface in child.ExternalInterface)
                    if (!string.IsNullOrEmpty(iface.ID)) interfaceOwner[iface.ID] = id;
            }

            switch (kind)
            {
                case PtNames.Place:
                    net.Places.Add(new PtPlace
                    {
                        Id = id,
                        Name = IdentificationName(child),
                        Bounds = ReadBounds(child),
                        LabelOffset = ReadLabelOffset(child),
                        InitialMarking = ReadInt(child, PtNames.Attributes.InitialMarking, 0),
                    });
                    break;

                case PtNames.Transition:
                    net.Transitions.Add(new PtTransition
                    {
                        Id = id,
                        Name = IdentificationName(child),
                        Bounds = ReadBounds(child),
                        LabelOffset = ReadLabelOffset(child),
                        IsSilent = string.Equals(ValueOf(child, PtNames.Attributes.IsSilent), "true", StringComparison.OrdinalIgnoreCase),
                    });
                    break;
            }
        }

        // Arcs come second: resolving their endpoints needs the node interfaces.
        foreach (var child in netIE.InternalElement.Where(e => ElementKind(e) == PtNames.Arc))
        {
            if (!claimedName.TryGetValue(child.Node, out var arcId)) continue;

            var arc = new PtArc
            {
                Id = arcId,
                Name = IdentificationName(child),
                Weight = Math.Max(1, ReadInt(child, PtNames.Attributes.Weight, 1)),
                LabelOffset = ReadLabelOffset(child),
            };

            var (sourceId, targetId) = ResolveEndpoints(netIE, child, interfaceOwner);
            if (sourceId == null || targetId == null)
            {
                // Said out loud and remembered: an updater that took silence for
                // "deleted in the diagram" would remove the element next.
                var missing = sourceId == null && targetId == null ? "source and target"
                    : sourceId == null ? "source" : "target";
                net.Warnings.Add($"Arc '{child.Name}' is not shown: its {missing} link does not resolve to a place or transition.");
                net.Unresolved.Add(arcId);
                continue;
            }
            arc.SourceId = sourceId;
            arc.TargetId = targetId;

            arc.Waypoints.AddRange(ReadWaypoints(child));
            net.Arcs.Add(arc);
        }

        return net;
    }

    // ── endpoint resolution ─────────────────────────────────────────────────

    /// <summary>
    /// Follows the two InternalLinks that couple an arc to its nodes. The links
    /// are the truth here rather than the interface names, because a user may
    /// have rewired them in the AML Editor.
    /// </summary>
    private static (string? Source, string? Target) ResolveEndpoints(
        InternalElementType netIE, InternalElementType arcIE, Dictionary<string, string> interfaceOwner)
    {
        string? source = null;
        string? target = null;

        var sourceInterfaces = arcIE.ExternalInterface
            .Where(i => PtCaex.IsClass(i.RefBaseClassPath, PtNames.ArcSource))
            .Select(i => i.ID!).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(PtCaex.Ids);
        var targetInterfaces = arcIE.ExternalInterface
            .Where(i => PtCaex.IsClass(i.RefBaseClassPath, PtNames.ArcTarget))
            .Select(i => i.ID!).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(PtCaex.Ids);

        foreach (var link in PtCaex.LinksBelow(netIE))
        {
            var a = PtCaex.PartnerId(link.RefPartnerSideA);
            var b = PtCaex.PartnerId(link.RefPartnerSideB);
            if (a == null || b == null) continue;

            // Whichever side is the arc's, the other side names the node.
            if (sourceInterfaces.Contains(a)) source ??= Owner(b);
            else if (sourceInterfaces.Contains(b)) source ??= Owner(a);
            else if (targetInterfaces.Contains(a)) target ??= Owner(b);
            else if (targetInterfaces.Contains(b)) target ??= Owner(a);
        }

        return (source, target);

        string? Owner(string interfaceId) =>
            interfaceOwner.TryGetValue(interfaceId, out var owner) ? owner : null;
    }

    // ── element typing ──────────────────────────────────────────────────────

    private static bool IsNet(InternalElementType ie) => ElementKind(ie) == PtNames.Net;

    /// <summary>Whether an element is the PT_Net container. Used by the updater.</summary>
    internal static bool IsPetriNet(InternalElementType ie) => IsNet(ie);

    /// <summary>
    /// The ids of the elements under this net that a read leaves out, in the
    /// id form the net uses. The updater consults this so that "absent from
    /// the diagram" is not mistaken for "deleted in the diagram".
    /// </summary>
    internal static HashSet<string> UnresolvedIn(InternalElementType netIE) => ReadNet(netIE).Unresolved;

    /// <summary>
    /// Which PT class an element belongs to, or null for anything that is not PT
    /// content. Exposed so the updater can tell its own elements from foreign
    /// ones sharing the hierarchy.
    /// </summary>
    internal static string? ElementKindOf(InternalElementType ie) => ElementKind(ie);

    /// <summary>
    /// The identifier an element is known by, before normalisation: its
    /// Identification/id, or the CAEX name when it has none.
    ///
    /// The fallback is not cosmetic. Documents written against earlier versions
    /// of the library carry no Identification at all, and an updater that only
    /// looked at Identification would recognise nothing in them and add a second
    /// copy of every element next to the first.
    /// </summary>
    internal static string? PnmlIdOf(InternalElementType ie) => IdentificationId(ie) ?? NonEmpty(ie.Name);

    /// <summary>
    /// Which PT class an element belongs to, taken from the system unit class
    /// path and falling back to the role requirement. Returns null for anything
    /// that is not PT content, so foreign elements in the same hierarchy are
    /// simply skipped.
    /// </summary>
    private static string? ElementKind(InternalElementType ie)
    {
        foreach (var candidate in new[] { PtNames.Net, PtNames.Place, PtNames.Transition, PtNames.Arc })
        {
            if (PtCaex.IsClass(ie.RefBaseSystemUnitPath, candidate)) return candidate;
            if (ie.RoleRequirements.Any(r => PtCaex.IsClass(r.RefBaseRoleClassPath, candidate))) return candidate;
        }
        return null;
    }

    // ── attribute reading ───────────────────────────────────────────────────

    private static string? IdentificationId(InternalElementType ie) =>
        NonEmpty(ie.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value);

    /// <summary>
    /// The element's label. When the mapper wrote the element, Identification/name
    /// is authoritative even if it is empty: a PNML element without a name must
    /// not come back carrying its id as a label.
    ///
    /// An element a user added by hand in the AML Editor has no Identification/id,
    /// and then the CAEX name is its label. Since ISO_PT v0.3 the class brings an
    /// Identification along, with id and name empty, so its mere presence says
    /// nothing about who wrote the element. Whether the id is filled in does: the
    /// mapper always writes one.
    /// </summary>
    private static string? IdentificationName(InternalElementType ie)
    {
        var name = NonEmpty(ie.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Name]?.Value);
        if (name != null) return name;
        return IdentificationId(ie) != null ? null : NonEmpty(ie.Name);
    }

    private static PtBounds? ReadBounds(InternalElementType ie)
    {
        var view = ie.Attribute[PtNames.Attributes.ViewInformation];
        if (view == null) return null;

        var position = view.Attribute[PtNames.Attributes.Position];
        if (position == null) return null;

        var x = ReadDouble(position.Attribute[PtNames.Attributes.X]?.Value);
        var y = ReadDouble(position.Attribute[PtNames.Attributes.Y]?.Value);
        var width = ReadDouble(view.Attribute[PtNames.Attributes.Width]?.Value);
        var height = ReadDouble(view.Attribute[PtNames.Attributes.Height]?.Value);

        if (x == null || y == null) return null;
        return new PtBounds(x.Value, y.Value, width ?? 50, height ?? 50);
    }

    private static PtPoint? ReadLabelOffset(InternalElementType ie)
    {
        var offset = ie.Attribute[PtNames.Attributes.LabelOffset];
        if (offset == null) return null;

        var x = ReadDouble(offset.Attribute[PtNames.Attributes.X]?.Value);
        var y = ReadDouble(offset.Attribute[PtNames.Attributes.Y]?.Value);
        return x == null || y == null ? null : new PtPoint(x.Value, y.Value);
    }

    private static IEnumerable<PtPoint> ReadWaypoints(InternalElementType ie)
    {
        return ie.Attribute
            .Where(a => a.Name != null && a.Name.StartsWith(PtNames.Attributes.WaypointPrefix, StringComparison.Ordinal))
            .Select(a => (Index: WaypointIndex(a.Name!), Attribute: a))
            .Where(entry => entry.Index > 0)
            .OrderBy(entry => entry.Index)
            .Select(entry =>
            {
                var position = entry.Attribute.Attribute[PtNames.Attributes.Position];
                var x = ReadDouble(position?.Attribute[PtNames.Attributes.X]?.Value);
                var y = ReadDouble(position?.Attribute[PtNames.Attributes.Y]?.Value);
                return x == null || y == null ? null : new PtPoint(x.Value, y.Value);
            })
            .Where(point => point != null)
            .Select(point => point!);
    }

    private static int WaypointIndex(string attributeName)
    {
        var suffix = attributeName[PtNames.Attributes.WaypointPrefix.Length..];
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : 0;
    }

    /// <summary>
    /// An attribute's value, falling back to its declared default.
    ///
    /// The library gives InitialMarking and Weight a DefaultValue, and a
    /// document is free to rely on it instead of writing the value out. Reading
    /// only Value would silently turn a declared default of 5 into 0.
    /// </summary>
    private static string? ValueOf(InternalElementType ie, string name)
    {
        var attribute = ie.Attribute[name];
        if (attribute == null) return null;
        return NonEmpty(attribute.Value) ?? NonEmpty(attribute.DefaultValue);
    }

    private static int ReadInt(InternalElementType ie, string name, int fallback) =>
        int.TryParse(ValueOf(ie, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static double? ReadDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
