using Aml.Engine.CAEX;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// Writes a place/transition net into a CAEX 3.0 document against the ISO_PT
/// domain library.
///
/// Shape of the result:
///
///   InstanceHierarchy
///     PT_Net                      the net
///       PT_Place / PT_Transition  one per node, with a PT_NodeArcEnd
///                                 interface per incident arc
///       PT_Arc                    one per arc, with PT_ArcSource and
///                                 PT_ArcTarget interfaces
///       InternalLink              arc endpoint to node endpoint, two per arc
///
/// This is the reified encoding: an arc is an element of its own, not an
/// InternalLink between two node ports the way an FPD flow is. The links here
/// only couple an arc to its nodes, they do not carry the arc's identity.
///
/// Use <see cref="PtNetUpdater"/> to refresh a hierarchy that already exists:
/// this class always builds from scratch.
/// </summary>
public static class PtNetToCaex
{
    public const string DefaultHierarchyName = "PetriNets";

    /// <summary>Convert a net into a fresh CAEX document.</summary>
    public static CAEXDocument Convert(PtNet net, string? hierarchyName = null)
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var caex = doc.CAEXFile;
        caex.FileName = "pt-export.aml";

        var sdi = caex.SourceDocumentInformation.FirstOrDefault() ?? caex.SourceDocumentInformation.Append();
        sdi.OriginName = "pt-aml-mapper";
        sdi.OriginID = "iso-pt-aml-mapper";
        sdi.OriginVersion = PtNames.LibraryVersion;
        sdi.LastWritingDateTime = DateTime.UtcNow;

        AppendInto(doc, net, hierarchyName);
        return doc;
    }

    /// <summary>
    /// Append the net as a new InstanceHierarchy in an existing document,
    /// creating the ISO_PT libraries first if they are not there yet.
    /// </summary>
    public static InstanceHierarchyType AppendInto(CAEXDocument doc, PtNet net, string? hierarchyName = null)
    {
        ArgumentNullException.ThrowIfNull(net);
        return AppendInto(doc, new[] { net }, hierarchyName);
    }

    /// <summary>
    /// Append several nets as one new InstanceHierarchy. A PNML document may
    /// hold more than one net, and dropping all but the first would lose a
    /// user's work without telling them.
    /// </summary>
    public static InstanceHierarchyType AppendInto(CAEXDocument doc, IEnumerable<PtNet> nets, string? hierarchyName = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(nets);

        // A document that references the ISO_PT library rather than carrying it
        // gets the definitions on loan and keeps its own class paths.
        var loan = PtLibraryLoan.Open(doc);
        PtLibraries.EnsureLibraries(doc.CAEXFile, embedShared: !loan.IsBorrowing);
        PtDiPaths.Refresh(doc);

        var name = string.IsNullOrWhiteSpace(hierarchyName) ? DefaultHierarchyName : hierarchyName!;
        var ih = doc.CAEXFile.InstanceHierarchy.Append(name);


        // Stamped so a consumer can find this hierarchy again after a rename.
        // Distinct, because two hierarchies may legitimately share a base name.
        var taken = new HashSet<string>(
            doc.CAEXFile.InstanceHierarchy.Select(h => h.ID ?? "").Where(id => id.Length > 0),
            StringComparer.Ordinal);
        var candidate = PtIds.Hierarchy(name);
        for (var ordinal = 2; !taken.Add(candidate); ordinal++)
            candidate = PtIds.Hierarchy($"{name}#{ordinal}");
        ih.ID = candidate;

        foreach (var net in nets)
        {
            // Before anything of the net is written: see MakeIdsDistinct.
            net.Warnings.AddRange(PtIdText.MakeIdsDistinct(net));
            WriteNet(doc, ih, net);
        }

        loan.Close();
        return ih;
    }

    private static void WriteNet(CAEXDocument doc, InstanceHierarchyType ih, PtNet net)
    {
        var classes = PtWrite.SystemUnitClasses(doc);

        // Ids are unique per document, not per hierarchy: the same net imported
        // twice would otherwise derive the same ids both times.
        var ids = new CaexIdSpace(doc);

        var netIE = PtWrite.CreateInstance(classes, PtNames.Net);
        netIE.ID = PtIds.DistinctNet(net.Id, ids);
        PtWrite.SetDisplayName(netIE, net.Name, net.Id, created: true);
        PtWrite.Append(ih, netIE);
        PtWrite.SetIdentification(netIE, net.Id, net.Name);

        // Which arcs touch a node, so each node gets exactly the endpoints it needs.
        var endpoints = new Dictionary<string, ExternalInterfaceType>(StringComparer.Ordinal);

        foreach (var node in net.Nodes)
        {
            var ie = PtWrite.CreateInstance(classes, node is PtPlace ? PtNames.Place : PtNames.Transition);
            ie.ID = PtIds.DistinctElement(node.Id, ids);
            PtWrite.Append(netIE, ie);
            PtWrite.WriteNode(ie, node, created: true);

            foreach (var arc in net.Arcs.Where(a => a.SourceId == node.Id || a.TargetId == node.Id))
            {
                var key = EndpointKey(node.Id, arc.Id);
                if (endpoints.ContainsKey(key)) continue;

                var interfaceName = PtWrite.EndpointName(arc, node);
                endpoints[key] = PtWrite.EnsureInterface(ie, interfaceName,
                    PtIds.Interface(ie.ID!, interfaceName), PtNames.Interface(PtNames.NodeArcEnd));
            }
        }

        foreach (var arc in net.Arcs)
        {
            var ie = PtWrite.CreateInstance(classes, PtNames.Arc);
            ie.ID = PtIds.DistinctElement(arc.Id, ids);
            PtWrite.Append(netIE, ie);
            PtWrite.WriteArc(ie, arc, created: true);

            var polyline = PtGeometry.Polyline(net, arc);

            var sourceEnd = PtWrite.EnsureInterface(ie, "Source",
                PtIds.Interface(ie.ID!, "Source"), PtNames.Interface(PtNames.ArcSource));
            if (polyline.Count > 0) PtWrite.SetPortCoordinate(sourceEnd, polyline[0]);

            var targetEnd = PtWrite.EnsureInterface(ie, "Target",
                PtIds.Interface(ie.ID!, "Target"), PtNames.Interface(PtNames.ArcTarget));
            if (polyline.Count > 1) PtWrite.SetPortCoordinate(targetEnd, polyline[^1]);

            if (endpoints.TryGetValue(EndpointKey(arc.SourceId, arc.Id), out var sourceNodeEnd))
                PtWrite.EnsureLink(netIE, $"{ie.Name}_source", PtIds.Link(ie.ID!, "source"), sourceEnd, sourceNodeEnd);

            if (endpoints.TryGetValue(EndpointKey(arc.TargetId, arc.Id), out var targetNodeEnd))
                PtWrite.EnsureLink(netIE, $"{ie.Name}_target", PtIds.Link(ie.ID!, "target"), targetEnd, targetNodeEnd);
        }
    }

    internal static string EndpointKey(string nodeId, string arcId) => $"{nodeId}|{arcId}";
}
