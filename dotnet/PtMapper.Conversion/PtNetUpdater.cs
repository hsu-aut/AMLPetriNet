using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>What an update changed, for logging and for the confirmation dialog.</summary>
public sealed record PtUpdateSummary(int Added, int Updated, int Removed)
{
    /// <summary>
    /// Things the caller should tell the user about, beyond the bare counts.
    /// Currently: links from outside the net that had to go because the element
    /// they pointed at was deleted on the canvas.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public int Total => Added + Updated + Removed;

    public override string ToString() => $"{Added} added, {Updated} updated, {Removed} removed";
}

/// <summary>
/// Brings an existing PT instance hierarchy in line with a net, instead of
/// replacing it.
///
/// The difference matters as soon as anyone works on the document: a user may
/// have added attributes to an element, linked a place to a station in another
/// hierarchy, or pointed a refObj at an arc. Replacing the hierarchy throws all
/// of that away on every sync. This matches elements by their PNML id, writes
/// only what the language owns, and leaves everything else alone.
///
/// What the language owns is listed in <see cref="PtWrite.IsLanguageOwned"/>;
/// only those attributes are written, and only those are ever removed. Foreign
/// InternalLinks are kept as long as both of their endpoints survive; only links
/// between an arc and its own nodes belong to the mapper.
/// </summary>
public static class PtNetUpdater
{
    /// <summary>
    /// Update a net of <paramref name="ih"/> to match <paramref name="net"/>,
    /// creating the net element if the hierarchy has none yet.
    /// </summary>
    /// <param name="netIndex">
    /// Which net of the hierarchy to update, for the case that it holds several.
    /// An index past the end appends a new net rather than overwriting the wrong
    /// one.
    /// </param>
    public static PtUpdateSummary UpdateInPlace(CAEXDocument doc, InstanceHierarchyType ih, PtNet net, int netIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(ih);
        ArgumentNullException.ThrowIfNull(net);

        var notes = new List<string>();

        // ── preconditions ───────────────────────────────────────────────────
        //
        // Everything that can make the update fail is checked before the first
        // write. An update writes element by element, so a failure halfway left
        // a document that was part old, part new, and the next Ctrl+S saved
        // exactly that.
        notes.AddRange(PtIdText.MakeIdsDistinct(net));
        CheckArcEnds(net);
        CheckCarriedClasses(doc);

        // How this document names its classes, read before anything is written.
        var loan = PtLibraryLoan.Open(doc);
        try
        {
            return Update(doc, ih, net, netIndex, notes, loan);
        }
        finally
        {
            // Hand the borrowed class definitions back to a document that keeps
            // its libraries outside, whether the update went through or not.
            loan.Close();
        }
    }

    /// <summary>
    /// Every arc has to name two nodes of the net. An arc that does not would be
    /// written without links, and every later read would report it as broken.
    /// </summary>
    private static void CheckArcEnds(PtNet net)
    {
        var nodes = net.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var arc in net.Arcs)
        {
            if (!nodes.Contains(arc.SourceId) || !nodes.Contains(arc.TargetId))
                throw new ArgumentException(
                    $"Arc '{arc.Id}' runs from '{arc.SourceId}' to '{arc.TargetId}', and the net has no node "
                    + $"'{(nodes.Contains(arc.SourceId) ? arc.TargetId : arc.SourceId)}'. Nothing was written.",
                    nameof(net));
        }
    }

    /// <summary>
    /// A document that carries the ISO_PT classes has to carry all of the ones
    /// an update may instantiate. The mapper's own definitions are only used
    /// for a document that brings none.
    /// </summary>
    private static void CheckCarriedClasses(CAEXDocument doc)
    {
        var lib = doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib];
        if (lib == null) return;

        var present = lib.SystemUnitClass.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var missing = new[] { PtNames.Net, PtNames.Place, PtNames.Transition, PtNames.Arc }
            .Where(c => !present.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The SystemUnitClassLib '{PtNames.SystemUnitClassLib}' in this document lacks "
                + $"{string.Join(", ", missing)}. Nothing was written.");
    }

    private static PtUpdateSummary Update(
        CAEXDocument doc, InstanceHierarchyType ih, PtNet net, int netIndex, List<string> notes, PtLibraryLoan loan)
    {
        PtDiPaths.Refresh(doc);

        // The libraries are only needed to instantiate something new, and
        // creating them writes four class libraries into the document. A sync
        // that changes an attribute has no business doing that to a file that
        // referenced its libraries rather than carrying them, so this is put off
        // until an element actually has to be created.
        Dictionary<string, SystemUnitFamilyType>? classCache = null;

        Dictionary<string, SystemUnitFamilyType> Classes()
        {
            if (classCache != null) return classCache;
            PtLibraries.EnsureLibraries(doc.CAEXFile, embedShared: !loan.IsBorrowing);
            return classCache = PtWrite.SystemUnitClasses(doc);
        }

        var added = 0;
        var updated = 0;

        var nets = ih.InternalElement.Where(CaexToPtNet.IsPetriNet).ToList();
        var netIE = netIndex >= 0 && netIndex < nets.Count ? nets[netIndex] : null;
        var netIsNew = netIE == null;
        if (netIE == null)
        {
            netIE = PtWrite.CreateInstance(Classes(), PtNames.Net);
            netIE.ID = PtIds.DistinctNet(net.Id, new CaexIdSpace(doc));
            PtWrite.Append(ih, netIE);
            added++;
        }
        else
        {
            updated++;
        }
        PtWrite.SetDisplayName(netIE, net.Name, net.Id, netIsNew);
        PtWrite.SetIdentification(netIE, net.Id, net.Name);

        // Existing PT children by their PNML id. Anything without one, or of a
        // foreign type, is not ours and stays untouched.
        var existing = new Dictionary<string, (InternalElementType Element, string? Kind, string? Id)>(StringComparer.Ordinal);

        // Elements whose id another element already carries, which happens when
        // someone copies an element in the AML Editor. The reader numbers the
        // copy, the canvas shows it under that number, and writing it back gives
        // the copy an id of its own. Said out loud, because it changes an id.
        var renumbered = new Dictionary<string, string>(StringComparer.Ordinal);

        // The incoming net carries normalised ids, so the lookup keys have to be
        // normalised the same way, in the same order. CaexToPtNet claims the net
        // first and then walks the children in document order; anything else and
        // two elements that reduce to one name would be paired up differently
        // here than they were there.
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        PtIdText.ToDistinctXmlName(CaexToPtNet.PnmlIdOf(netIE) ?? netIE.Name, claimed);

        foreach (var child in netIE.InternalElement)
        {
            var kind = CaexToPtNet.ElementKindOf(child);
            var raw = CaexToPtNet.PnmlIdOf(child);
            if (raw == null) continue;

            // Exactly the kinds CaexToPtNet claims a name for. A nested PT_Net
            // is skipped there, and claiming one here would shift every name
            // after it and pair the wrong elements up.
            if (kind is not (PtNames.Place or PtNames.Transition or PtNames.Arc)) continue;

            var childId = PtIdText.ToDistinctXmlName(raw, claimed);
            existing[childId] = (child, kind, childId);
            if (childId != PtIdText.ToXmlName(raw)) renumbered[childId] = raw;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new Dictionary<string, ExternalInterfaceType>(StringComparer.Ordinal);

        // How the document already connects its arcs. Reusing that is what keeps
        // a sync from rewriting interfaces that work: another tool names them
        // NodeArcEnd_1 rather than Out_<arc>, and renaming them, or worse
        // reassigning their ids, would break anything else pointing at them.
        var wiring = ReadWiring(netIE, existing);

        // Not everything the document holds made it into the diagram: an arc
        // whose link is missing, or whose endpoint the reader could not name,
        // was never shown, so its absence from the incoming net is not a
        // deletion. Read before anything below removes an interface, or the
        // arcs the diagram legitimately dropped would look unresolved too.
        var unresolved = CaexToPtNet.UnresolvedIn(netIE);

        // Ids in use anywhere in the document, so a newly created element cannot
        // collide with one that stays, nor with another hierarchy's. Same
        // allocation the writer uses.
        var ids = new CaexIdSpace(doc);

        // ── nodes ───────────────────────────────────────────────────────────
        foreach (var node in net.Nodes)
        {
            var wanted = node is PtPlace ? PtNames.Place : PtNames.Transition;
            var ie = Reuse(doc, notes, netIE, existing, ids, node.Id, wanted, Classes, wiring, ref added, ref updated, out var nodeIsNew);
            seen.Add(node.Id);
            if (!nodeIsNew) NoteRenumbered(notes, renumbered, ie, node.Id);

            PtWrite.WriteNode(ie, node, nodeIsNew);

            // One endpoint per incident arc, and no leftovers from arcs that are gone.
            var incident = net.Arcs.Where(a => a.SourceId == node.Id || a.TargetId == node.Id).ToList();
            var wantedNames = new HashSet<string>(incident.Select(a => PtWrite.EndpointName(a, node)), StringComparer.Ordinal);

            var keep = new HashSet<string>(StringComparer.Ordinal);

            foreach (var arc in incident)
            {
                var key = PtNetToCaex.EndpointKey(node.Id, arc.Id);
                var name = PtWrite.EndpointName(arc, node);

                var existingEndpoint = wiring.NodeEndpoint(node.Id, arc.Id);
                var endpoint = existingEndpoint ?? PtWrite.EnsureInterface(
                    ie, name, PtIds.Interface(ie.ID!, name), PtNames.Interface(PtNames.NodeArcEnd));

                endpoints[key] = endpoint;
                if (endpoint.Name != null) keep.Add(endpoint.Name);
            }

            // Only the endpoints this diagram no longer needs go; the ones that
            // were reused keep whatever they are called.
            RemoveStaleInterfaces(doc, netIE, ie, keep, notes);
        }

        // ── arcs ────────────────────────────────────────────────────────────
        foreach (var arc in net.Arcs)
        {
            var ie = Reuse(doc, notes, netIE, existing, ids, arc.Id, PtNames.Arc, Classes, wiring, ref added, ref updated, out var arcIsNew);
            seen.Add(arc.Id);
            if (!arcIsNew) NoteRenumbered(notes, renumbered, ie, arc.Id);

            PtWrite.WriteArc(ie, arc, arcIsNew);

            var polyline = PtGeometry.Polyline(net, arc);

            var sourceEnd = wiring.ArcEnd(arc.Id, PtNames.ArcSource)
                ?? PtWrite.EnsureInterface(ie, "Source",
                    PtIds.Interface(ie.ID!, "Source"), PtNames.Interface(PtNames.ArcSource));
            if (polyline.Count > 0) PtWrite.SetPortCoordinate(sourceEnd, polyline[0]);

            var targetEnd = wiring.ArcEnd(arc.Id, PtNames.ArcTarget)
                ?? PtWrite.EnsureInterface(ie, "Target",
                    PtIds.Interface(ie.ID!, "Target"), PtNames.Interface(PtNames.ArcTarget));
            if (polyline.Count > 1) PtWrite.SetPortCoordinate(targetEnd, polyline[^1]);

            if (endpoints.TryGetValue(PtNetToCaex.EndpointKey(arc.SourceId, arc.Id), out var sourceNodeEnd))
            {
                if (wiring.Link(sourceEnd.ID, sourceNodeEnd.ID) == null)
                    PtWrite.EnsureLink(netIE, $"{ie.Name}_source", PtIds.Link(ie.ID!, "source"), sourceEnd, sourceNodeEnd);
            }

            if (endpoints.TryGetValue(PtNetToCaex.EndpointKey(arc.TargetId, arc.Id), out var targetNodeEnd))
            {
                if (wiring.Link(targetEnd.ID, targetNodeEnd.ID) == null)
                    PtWrite.EnsureLink(netIE, $"{ie.Name}_target", PtIds.Link(ie.ID!, "target"), targetEnd, targetNodeEnd);
            }
        }

        // ── what the diagram no longer has ──────────────────────────────────
        var removed = 0;
        foreach (var (id, entry) in existing)
        {
            if (seen.Contains(id)) continue;
            if (unresolved.Contains(id))
            {
                notes.Add($"Kept '{entry.Element.Name}': the diagram could not show it "
                    + "(its links do not resolve), so it was not deleted.");
                continue;
            }
            RemoveElement(doc, netIE, entry.Element, notes);
            removed++;
        }

        return new PtUpdateSummary(added, updated, removed) { Notes = notes };
    }

    private static void NoteRenumbered(
        List<string> notes, Dictionary<string, string> renumbered, InternalElementType ie, string id)
    {
        if (!renumbered.TryGetValue(id, out var raw)) return;
        notes.Add($"Gave '{ie.Name}' the id '{id}': its id '{raw}' is also carried by another element, "
            + "most likely the one it was copied from.");
    }

    /// <summary>
    /// The arc wiring a document already has: which interface plays which role
    /// on an arc, which interface on a node an arc is attached to, and which
    /// link joins the two.
    /// </summary>
    private sealed class Wiring
    {
        private readonly Dictionary<string, ExternalInterfaceType> _arcEnds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExternalInterfaceType> _nodeEndpoints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InternalLinkType> _links = new(StringComparer.Ordinal);

        internal void AddArcEnd(string arcId, string role, ExternalInterfaceType iface) =>
            _arcEnds.TryAdd($"{arcId}|{role}", iface);

        internal void AddNodeEndpoint(string nodeId, string arcId, ExternalInterfaceType iface) =>
            _nodeEndpoints.TryAdd(PtNetToCaex.EndpointKey(nodeId, arcId), iface);

        internal void AddLink(string? a, string? b, InternalLinkType link)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return;
            _links.TryAdd(LinkKey(a, b), link);
        }

        /// <summary>
        /// Drop everything that was read from an element that has since been
        /// removed. Without this, a place replaced by a transition under the
        /// same id leaves its endpoints in here, the arcs reuse interfaces that
        /// are no longer in the document, and every arc touching that node ends
        /// up unlinked.
        /// </summary>
        internal void Forget(string pnmlId)
        {
            foreach (var key in _arcEnds.Keys.Where(k => k.StartsWith(pnmlId + "|", StringComparison.Ordinal)).ToList())
                _arcEnds.Remove(key);

            foreach (var key in _nodeEndpoints.Keys
                         .Where(k => k.StartsWith(pnmlId + "|", StringComparison.Ordinal)
                                     || k.EndsWith("|" + pnmlId, StringComparison.Ordinal)).ToList())
                _nodeEndpoints.Remove(key);

            // The links that joined those interfaces are gone with the element.
            foreach (var (key, link) in _links.ToList())
                if (link.Node.Parent == null) _links.Remove(key);
        }

        internal ExternalInterfaceType? ArcEnd(string arcId, string role) =>
            _arcEnds.GetValueOrDefault($"{arcId}|{role}");

        internal ExternalInterfaceType? NodeEndpoint(string nodeId, string arcId) =>
            _nodeEndpoints.GetValueOrDefault(PtNetToCaex.EndpointKey(nodeId, arcId));

        internal InternalLinkType? Link(string? a, string? b) =>
            string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) ? null : _links.GetValueOrDefault(LinkKey(a, b));

        /// <summary>Order-independent: a link joins two interfaces, it has no direction.</summary>
        private static string LinkKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
    }

    private static Wiring ReadWiring(
        InternalElementType netIE,
        Dictionary<string, (InternalElementType Element, string? Kind, string? Id)> existing)
    {
        var wiring = new Wiring();

        // Interface id -> the element that owns it, and the id that element goes by.
        var owner = new Dictionary<string, (string Id, string Kind)>(PtCaex.Ids);
        foreach (var (id, entry) in existing)
        foreach (var iface in entry.Element.ExternalInterface)
        {
            if (!string.IsNullOrEmpty(iface.ID)) owner[iface.ID] = (id, entry.Kind ?? "");
        }

        foreach (var (arcPnmlId, entry) in existing.Where(e => e.Value.Kind == PtNames.Arc))
        {
            foreach (var iface in entry.Element.ExternalInterface)
            {
                var role = PtCaex.IsClass(iface.RefBaseClassPath, PtNames.ArcSource) ? PtNames.ArcSource
                    : PtCaex.IsClass(iface.RefBaseClassPath, PtNames.ArcTarget) ? PtNames.ArcTarget
                    : null;
                if (role == null) continue;

                wiring.AddArcEnd(arcPnmlId, role, iface);

                // Follow the link to whatever node interface it reaches. The
                // same set the reader walks: CAEX allows a link on any element,
                // and one parked on the arc would otherwise look like no link
                // at all here while the reader resolved the arc happily.
                foreach (var link in PtCaex.LinksBelow(netIE))
                {
                    var a = PtCaex.PartnerId(link.RefPartnerSideA);
                    var b = PtCaex.PartnerId(link.RefPartnerSideB);
                    if (a == null || b == null || string.IsNullOrEmpty(iface.ID)) continue;
                    var other = PtCaex.Ids.Equals(a, iface.ID) ? b
                        : PtCaex.Ids.Equals(b, iface.ID) ? a
                        : null;
                    if (other == null) continue;

                    wiring.AddLink(a, b, link);
                    if (owner.TryGetValue(other, out var node) && node.Kind != PtNames.Arc)
                    {
                        var nodeIface = existing[node.Id].Element.ExternalInterface
                            .FirstOrDefault(i => PtCaex.Ids.Equals(i.ID, other));
                        if (nodeIface != null) wiring.AddNodeEndpoint(node.Id, arcPnmlId, nodeIface);
                    }
                }
            }
        }

        return wiring;
    }

    /// <summary>
    /// Reuse the element that carries this PNML id, or create one. An element
    /// whose type changed (a place became a transition, which the modeler cannot
    /// do but a hand edit can) is replaced rather than reinterpreted.
    /// </summary>
    private static InternalElementType Reuse(
        CAEXDocument doc,
        List<string> notes,
        InternalElementType netIE,
        Dictionary<string, (InternalElementType Element, string? Kind, string? Id)> existing,
        IIdSpace ids,
        string pnmlId,
        string wantedKind,
        Func<Dictionary<string, SystemUnitFamilyType>> classes,
        Wiring wiring,
        ref int added,
        ref int updated,
        out bool created)
    {
        created = true;

        if (existing.TryGetValue(pnmlId, out var entry))
        {
            if (entry.Kind == wantedKind)
            {
                updated++;
                created = false;
                return entry.Element;
            }

            RemoveElement(doc, netIE, entry.Element, notes);
            existing.Remove(pnmlId);
            wiring.Forget(pnmlId);
            // The replaced element's id is free again, but deriving it a second
            // time would now collide with the claim above, so the new element
            // simply takes the next free one.
        }

        var ie = PtWrite.CreateInstance(classes(), wantedKind);
        ie.ID = PtIds.DistinctElement(pnmlId, ids);
        PtWrite.Append(netIE, ie);
        added++;
        return ie;
    }

    /// <summary>
    /// Drops endpoint interfaces of the given class that the diagram no longer
    /// calls for, together with any link that used them. Interfaces of other
    /// classes are someone else's and are left alone.
    /// </summary>
    private static void RemoveStaleInterfaces(
        CAEXDocument doc, InternalElementType netIE, InternalElementType ie,
        HashSet<string> wantedNames, List<string> notes)
    {
        foreach (var iface in ie.ExternalInterface
                     .Where(i => PtCaex.IsClass(i.RefBaseClassPath, PtNames.NodeArcEnd) && !wantedNames.Contains(i.Name))
                     .ToList())
        {
            RemoveLinksTouching(doc, netIE, iface.ID, ie.Name, notes);
            iface.Remove();
        }
    }

    /// <summary>
    /// Removes an element and every link that pointed at one of its interfaces,
    /// so no dangling InternalLink is left behind.
    /// </summary>
    private static void RemoveElement(
        CAEXDocument doc, InternalElementType netIE, InternalElementType ie, List<string> notes)
    {
        foreach (var id in ie.ExternalInterface.Select(i => i.ID).ToList())
            RemoveLinksTouching(doc, netIE, id, ie.Name, notes);

        ie.Remove();
    }

    /// <summary>
    /// Drops every link that used this interface, wherever in the document it
    /// lives.
    ///
    /// Scanning the whole document rather than just this net is deliberate. The
    /// MPS500 bridging example links a plant signal to a transition
    /// of the behaviour model, and that link hangs off the plant element.
    /// Leaving it behind would leave a reference to a deleted element in the
    /// document, which is worse than touching a hierarchy this plugin does not
    /// own. Every such removal is reported, so it does not happen silently.
    /// </summary>
    private static void RemoveLinksTouching(
        CAEXDocument doc, InternalElementType netIE, string? interfaceId, string? elementName, List<string> notes)
    {
        if (string.IsNullOrEmpty(interfaceId)) return;

        // A document written before interface ids were salted, or one edited by
        // hand, can carry the same interface id twice. A link outside this net
        // that names such an id may well belong to the other owner, so it is
        // left alone and reported rather than taken on a string match.
        var ambiguous = doc.CAEXFile.Node.DescendantsAndSelf()
            .Attributes("ID")
            .Count(a => PtCaex.Ids.Equals(a.Value, interfaceId)) > 1;

        foreach (var (link, owner) in PtCaex.AllLinks(doc).ToList())
        {
            if (!PtCaex.Ids.Equals(PtCaex.PartnerId(link.RefPartnerSideA), interfaceId)
                && !PtCaex.Ids.Equals(PtCaex.PartnerId(link.RefPartnerSideB), interfaceId))
                continue;

            // Aml.Engine hands out a fresh wrapper on every access, so wrappers
            // cannot be compared by reference. The XElement behind them can.
            var insideThisNet = ReferenceEquals(owner.Node, netIE.Node)
                || owner.Node.Ancestors().Any(a => ReferenceEquals(a, netIE.Node));

            if (!insideThisNet)
            {
                if (ambiguous)
                {
                    notes.Add($"Left the link '{link.Name}' on '{owner.Name}' alone: it names the interface id "
                        + $"'{interfaceId}', which more than one element in this document carries.");
                    continue;
                }

                notes.Add($"Removed the link '{link.Name}' on '{owner.Name}': it pointed at "
                    + $"'{elementName}', which the diagram no longer has.");
            }

            link.Remove();
        }
    }

}
