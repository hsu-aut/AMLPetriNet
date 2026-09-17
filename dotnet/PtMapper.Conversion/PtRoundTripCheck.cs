using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>One thing that was checked, and how it came out.</summary>
public sealed record PtRoundTripCheckResult(string Aspect, bool Passed, string Detail)
{
    public override string ToString() => $"[{(Passed ? "ok" : "FAIL")}] {Aspect}: {Detail}";
}

/// <summary>What a round-trip check found.</summary>
public sealed record PtRoundTripReport(IReadOnlyList<PtRoundTripCheckResult> Checks)
{
    public bool Passed => Checks.All(c => c.Passed);

    public override string ToString() => string.Join(Environment.NewLine, Checks);
}

/// <summary>
/// Sends a net in a document out as PNML and back, and reports what survived.
///
/// This exists because a unit test on a fixture proves nothing about a file a
/// person edited in the AML Editor and saved. The same check runs in the test
/// suite against the MPS500 bridging example and on the command line against
/// whatever the editor wrote, so a statement about the round trip between an
/// editor and AML rests on the same measurements in both places.
///
/// What it does not do is judge the net. <see cref="PtValidator"/> does that,
/// and the check reports its verdict alongside its own.
/// </summary>
public static class PtRoundTripCheck
{
    public static PtRoundTripReport Run(CAEXDocument doc, string? hierarchyName = null)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var checks = new List<PtRoundTripCheckResult>();

        var hierarchy = Hierarchy(doc, hierarchyName);

        if (hierarchy == null)
        {
            checks.Add(new PtRoundTripCheckResult("hierarchy", false,
                hierarchyName == null
                    ? "the document holds no Petri net"
                    : $"'{hierarchyName}' is not an InstanceHierarchy of this document"));
            return new PtRoundTripReport(checks);
        }

        var nets = CaexToPtNet.Read(hierarchy);
        if (nets.Count != 1)
        {
            checks.Add(new PtRoundTripCheckResult("hierarchy", false,
                $"'{hierarchy.Name}' holds {nets.Count} nets; this check expects exactly one"));
            return new PtRoundTripReport(checks);
        }

        var net = nets[0];
        checks.Add(new PtRoundTripCheckResult("net", true,
            $"'{net.Name ?? net.Id}' in {hierarchy.Name}: {net.Places.Count} place(s), "
            + $"{net.Transitions.Count} transition(s), {net.Arcs.Count} arc(s)"));

        // ── AML -> PNML -> AML ──────────────────────────────────────────────
        //
        // The comparison is against the same file loaded a second time, so that
        // whatever Aml.Engine does while reading appears on both sides and only
        // the round trip's own writing is left.
        var before = Snapshot(doc);
        var identityBefore = Identity(hierarchy);
        var geometryBefore = Geometry(net).Concat(DockingPoints(hierarchy)).ToList();
        var couplingBefore = ForeignCoupling(doc, hierarchy);
        var referencesBefore = References(doc);
        var librariesBefore = Libraries(doc);

        var pnml = Pnml.Write(net);
        PtNetUpdater.UpdateInPlace(doc, hierarchy, Pnml.Read(pnml));

        var after = Snapshot(doc);
        checks.Add(Compare("aml -> pnml -> aml", before, after, "the document is unchanged"));

        var reread = CaexToPtNet.Read(hierarchy).Single();

        checks.Add(Compare("identity", identityBefore, Identity(hierarchy),
            "CAEX ids, Identification/id and names are the ones the document brought"));
        checks.Add(Compare("markings and weights",
            Markings(net), Markings(reread), "initial markings and arc weights are unchanged"));
        checks.Add(Compare("connections", Connections(net), Connections(reread),
            $"all {net.Arcs.Count} arc(s) still run between the same nodes, in the same direction"));
        checks.Add(Compare("geometry", geometryBefore, Geometry(reread).Concat(DockingPoints(hierarchy)).ToList(),
            geometryBefore.Count == 0
                ? "the net carries no graphical information, and none was added"
                : "bounds, waypoints and label offsets are unchanged"));
        checks.Add(Compare("links to other hierarchies", couplingBefore, ForeignCoupling(doc, hierarchy),
            couplingBefore.Count == 0
                ? "the net is not linked to another hierarchy"
                : $"{couplingBefore.Count} link(s) into another hierarchy survived"));
        checks.Add(Compare("external references", referencesBefore, References(doc),
            "the document still references the same library files"));
        checks.Add(Compare("class libraries", librariesBefore, Libraries(doc),
            librariesBefore.Count == 0
                ? "no class library was written into the document"
                : "the class libraries in the document are unchanged"));

        // ── PNML -> AML -> PNML ─────────────────────────────────────────────
        var fresh = CAEXDocument.New_CAEXDocument();
        var written = PtNetToCaex.AppendInto(fresh, Pnml.ReadAll(pnml), hierarchy.Name);
        var again = Pnml.Write(CaexToPtNet.Read(written).Single());
        checks.Add(Compare("pnml -> aml -> pnml", Lines(pnml), Lines(again), "the PNML comes back the same"));

        // ── the net itself ──────────────────────────────────────────────────
        //
        // The net as it was read, not as it came back: a verdict on the result
        // of the round trip would disagree with `ptmap validate` on the same
        // file and hide a problem the document already had.
        var findings = PtValidator.Validate(net);
        var errors = findings.Count(f => f.Severity == PtSeverity.Error);
        checks.Add(new PtRoundTripCheckResult("ISO/IEC 15909-1 rules", errors == 0,
            errors == 0
                ? $"{findings.Count} finding(s), none of them an error"
                : $"{errors} error(s): {string.Join("; ", findings.Where(f => f.Severity == PtSeverity.Error))}"));

        return new PtRoundTripReport(checks);
    }

    /// <summary>
    /// What an editing session changed, by comparing the file that went into the
    /// AML Editor with the one that came out.
    ///
    /// Opening a net that carries no layout and syncing it writes the arranged
    /// layout into the document, which is a change the user asked for without
    /// necessarily realising it. Naming it is better than leaving someone to
    /// diff two thousand lines of reformatted XML.
    /// </summary>
    public static PtRoundTripReport Compare(CAEXDocument before, CAEXDocument after, string? hierarchyName = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var checks = new List<PtRoundTripCheckResult>();

        var a = Hierarchy(before, hierarchyName);
        var b = Hierarchy(after, hierarchyName);
        if (a == null || b == null)
        {
            checks.Add(new PtRoundTripCheckResult("hierarchy", false,
                $"the net is missing from the {(a == null ? "first" : "second")} document"));
            return new PtRoundTripReport(checks);
        }

        var netA = CaexToPtNet.Read(a).Single();
        var netB = CaexToPtNet.Read(b).Single();

        checks.Add(Compare("elements",
            new[] { $"{netA.Places.Count}/{netA.Transitions.Count}/{netA.Arcs.Count}" },
            new[] { $"{netB.Places.Count}/{netB.Transitions.Count}/{netB.Arcs.Count}" },
            $"{netB.Places.Count} place(s), {netB.Transitions.Count} transition(s), {netB.Arcs.Count} arc(s), as before"));
        checks.Add(Compare("identity", Identity(a), Identity(b),
            "CAEX ids, Identification/id and names are the ones the document had"));
        checks.Add(Compare("markings and weights", Markings(netA), Markings(netB),
            "initial markings and arc weights are unchanged"));
        checks.Add(Compare("connections", Connections(netA), Connections(netB),
            "the arcs still run between the same nodes, in the same direction"));
        checks.Add(Compare("links to other hierarchies", ForeignCoupling(before, a), ForeignCoupling(after, b),
            "the links into other hierarchies are unchanged"));
        checks.Add(Compare("external references", References(before), References(after),
            "the document still references the same library files"));
        checks.Add(Compare("class libraries", Libraries(before), Libraries(after),
            "the class libraries in the document are unchanged"));
        checks.Add(Compare("everything else", Outside(before, a, after, b), Outside(after, b, before, a),
            "nothing outside the net's own content changed: hierarchies, roles, attributes "
            + "someone added and links between other elements are as they were"));

        // Layout is reported rather than demanded: a net that went in without
        // one and came back arranged is the expected outcome of a sync, not a
        // defect, and the count says how much was added.
        var geometryA = Geometry(netA).Concat(DockingPoints(a)).ToList();
        var geometryB = Geometry(netB).Concat(DockingPoints(b)).ToList();
        checks.Add(new PtRoundTripCheckResult("geometry", true,
            geometryA.SequenceEqual(geometryB, StringComparer.Ordinal)
                ? geometryA.Count == 0
                    ? "neither document carries graphical information"
                    : "bounds, waypoints and label offsets are unchanged"
                : $"{geometryA.Count} graphical value(s) before, {geometryB.Count} after"
                  + (geometryA.Count == 0
                      ? "; the layout was generated on import, which a sync then stores"
                      : "; the layout differs, which is what moving a node in the editor does")));

        return new PtRoundTripReport(checks);
    }

    private static InstanceHierarchyType? Hierarchy(CAEXDocument doc, string? name) =>
        name == null
            ? doc.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToPtNet.ContainsNet)
            : doc.CAEXFile.InstanceHierarchy[name];

    // ── comparison ──────────────────────────────────────────────────────────

    private static PtRoundTripCheckResult Compare(
        string aspect, IReadOnlyList<string> before, IReadOnlyList<string> after, string whenEqual)
    {
        if (before.SequenceEqual(after, StringComparer.Ordinal))
            return new PtRoundTripCheckResult(aspect, true, whenEqual);

        var added = after.Except(before, StringComparer.Ordinal).Take(3).ToList();
        var lost = before.Except(after, StringComparer.Ordinal).Take(3).ToList();

        // Counted on everything, listed up to three: a capped list that also
        // reported a capped count read as "only three changed".
        var lostCount = before.Except(after, StringComparer.Ordinal).Count();
        var addedCount = after.Except(before, StringComparer.Ordinal).Count();

        var detail = $"{lostCount} line(s) lost, {addedCount} added";
        if (lost.Count > 0) detail += Environment.NewLine + "      - " + string.Join(Environment.NewLine + "      - ", lost);
        if (added.Count > 0) detail += Environment.NewLine + "      + " + string.Join(Environment.NewLine + "      + ", added);

        return new PtRoundTripCheckResult(aspect, false, detail);
    }

    private static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').ToList();

    /// <summary>The whole document as XML, line by line, so a difference can be named.</summary>
    private static List<string> Snapshot(CAEXDocument doc) =>
        Lines(doc.CAEXFile.Node.ToString());

    private static List<string> Identity(InstanceHierarchyType ih) =>
        Elements(ih)
            .Select(e =>
            {
                var identification = e.Attribute[PtNames.Attributes.Identification];
                return $"{e.ID}|{identification?.Attribute[PtNames.Attributes.Id]?.Value}"
                    + $"|{identification?.Attribute[PtNames.Attributes.Name]?.Value}|{e.Name}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Which arc runs from where to where. Without this the check passed while
    /// an arc was retargeted or reversed: the counts, the ids and the weights
    /// all stay the same when only an endpoint moves.
    /// </summary>
    private static List<string> Connections(Models.PtNet net) =>
        net.Arcs.Select(a => $"{a.Id}:{a.SourceId}->{a.TargetId}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The docking points on the arcs' own interfaces, which live in the CAEX
    /// document rather than in the net model.
    ///
    /// They are geometry and a sync writes them: on the MPS500 example an
    /// editing session adds sixteen of them, two per arc, on top of the nine
    /// bounds on the nodes. Leaving them out made this check report nine when
    /// twenty-five attributes had been added, and the number went into the
    /// validation report.
    /// </summary>
    private static List<string> DockingPoints(InstanceHierarchyType ih) =>
        Elements(ih)
            .SelectMany(e => e.ExternalInterface)
            .Select(i => (Id: i.ID, Point: i.Attribute[PtNames.Attributes.PortCoordinate]))
            .Where(p => p.Point != null)
            .Select(p => $"{p.Id} port "
                + $"{p.Point!.Attribute[PtNames.Attributes.X]?.Value},{p.Point!.Attribute[PtNames.Attributes.Y]?.Value}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    private static List<string> Markings(Models.PtNet net) =>
        net.Places.Select(p => $"{p.Id}={p.InitialMarking}")
            .Concat(net.Arcs.Select(a => $"{a.Id}*{a.Weight}"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>Only what is really there: a net without layout yields nothing.</summary>
    private static List<string> Geometry(Models.PtNet net)
    {
        var lines = new List<string>();

        foreach (var node in net.Nodes)
        {
            if (node.Bounds != null) lines.Add($"{node.Id} bounds {node.Bounds}");
            if (node.LabelOffset != null) lines.Add($"{node.Id} label {node.LabelOffset}");
        }

        foreach (var arc in net.Arcs)
        {
            if (arc.LabelOffset != null) lines.Add($"{arc.Id} label {arc.LabelOffset}");

            // One line per waypoint, because the document holds one attribute
            // per waypoint. Counting a whole route as one value once reported
            // thirty-seven where the file had gained forty attributes.
            for (var i = 0; i < arc.Waypoints.Count; i++)
                lines.Add($"{arc.Id} waypoint {i + 1} {arc.Waypoints[i]}");
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>
    /// Links that join an element of this net to something outside the
    /// hierarchy, which is how the MPS500 example couples the net to the plant.
    /// </summary>
    private static List<string> ForeignCoupling(CAEXDocument doc, InstanceHierarchyType ih)
    {
        var ports = Elements(ih)
            .SelectMany(e => e.ExternalInterface.Select(i => (Owner: e.Name, Port: i)))
            .Where(p => !string.IsNullOrEmpty(p.Port.ID))
            .GroupBy(p => p.Port.ID!, PtCaex.Ids)
            .ToDictionary(g => g.Key, g => $"{g.First().Owner}.{g.First().Port.Name}", PtCaex.Ids);

        var coupling = new List<string>();
        foreach (var (link, _) in PtCaex.AllLinks(doc))
        {
            var a = PtCaex.PartnerId(link.RefPartnerSideA);
            var b = PtCaex.PartnerId(link.RefPartnerSideB);
            var mineA = a != null && ports.ContainsKey(a);
            var mineB = b != null && ports.ContainsKey(b);

            // Both sides ours is the net's own wiring; neither side ours is none
            // of our business. Exactly one side ours is a coupling.
            if (mineA == mineB) continue;

            coupling.Add($"{link.Name}: {(mineA ? ports[a!] : ports[b!])} <-> {(mineA ? b : a)}");
        }

        coupling.Sort(StringComparer.Ordinal);
        return coupling;
    }

    private static List<string> References(CAEXDocument doc) =>
        doc.CAEXFile.ExternalReference
            .Select(r => $"{r.Alias} -> {r.Path}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    private static List<string> Libraries(CAEXDocument doc) =>
        doc.CAEXFile.SystemUnitClassLib.Select(l => "SUC " + l.Name)
            .Concat(doc.CAEXFile.RoleClassLib.Select(l => "RC " + l.Name))
            .Concat(doc.CAEXFile.InterfaceClassLib.Select(l => "IC " + l.Name))
            .Concat(doc.CAEXFile.AttributeTypeLib.Select(l => "AT " + l.Name))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<InternalElementType> Elements(InstanceHierarchyType ih) =>
        ih.InternalElement.SelectMany(net => net.InternalElement.Prepend(net));

    /// <summary>
    /// The document without what the other checks already cover, one line per
    /// XML element, so that a change anywhere else fails the comparison.
    ///
    /// The checks above look at the net. Without this one, a session that
    /// renamed the hierarchy, dropped a role requirement from a plant element
    /// or changed an attribute someone added to a place still passed, and the
    /// claim that an editing session changes nothing but the net was not
    /// actually tested.
    ///
    /// Left out, because another check owns them: the attributes the language
    /// owns on net elements, the interfaces that join arcs to nodes, the links
    /// that use them, the CAEX name of a net element, and net elements that
    /// exist in only one of the two documents. Left out because they change on
    /// every save: the writing time and the schema location.
    /// </summary>
    private static List<string> Outside(
        CAEXDocument doc, InstanceHierarchyType ih, CAEXDocument otherDoc, InstanceHierarchyType otherIh)
    {
        var mine = NetElements(ih);
        var theirs = NetElements(otherIh);

        var common = mine.Keys.Intersect(theirs.Keys, PtCaex.Ids).ToHashSet(PtCaex.Ids);
        var netNodes = mine.Values.Select(e => e.Node).ToHashSet();

        // Arc wiring from both documents: a link to an arc end that only one of
        // them has is still wiring, not something else.
        var wiring = ArcEndIds(mine.Values).Concat(ArcEndIds(theirs.Values)).ToHashSet(PtCaex.Ids);

        var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var lines = new List<string>();

        void Walk(XElement element, string path)
        {
            var local = element.Name.LocalName;
            var parent = element.Parent;
            var onNetElement = parent != null && netNodes.Contains(parent);

            if (onNetElement && local == "Attribute" && PtWrite.IsLanguageOwned((string?)element.Attribute("Name")))
                return;
            if (onNetElement && local == "ExternalInterface" && IsArcEnd((string?)element.Attribute("RefBaseClassPath")))
                return;
            if (local == "InternalLink"
                && (wiring.Contains((string?)element.Attribute("RefPartnerSideA") ?? "")
                    || wiring.Contains((string?)element.Attribute("RefPartnerSideB") ?? "")))
                return;

            var isNetElement = netNodes.Contains(element);
            var id = (string?)element.Attribute("ID");
            if (isNetElement && (id == null || !common.Contains(id))) return;

            var name = (string?)element.Attribute("Name");
            var segment = local == "InternalElement" && id != null ? $"{local}[{id}]"
                : name != null ? $"{local}[{name}]"
                : local;
            var here = path + "/" + segment;

            var attributes = element.Attributes()
                .Where(x => !x.IsNamespaceDeclaration && x.Name.Namespace != xsi)
                .Where(x => !(local == "SourceDocumentInformation" && x.Name.LocalName == "LastWritingDateTime"))
                .Where(x => !(isNetElement && x.Name.LocalName == "Name"))
                .Select(x => $"{x.Name.LocalName}={x.Value}")
                .OrderBy(s => s, StringComparer.Ordinal);

            var text = element.HasElements ? "" : element.Value.Trim();
            lines.Add($"{here} {string.Join(" ", attributes)}{(text.Length > 0 ? " = " + text : "")}");

            foreach (var child in element.Elements()) Walk(child, here);
        }

        Walk(doc.CAEXFile.Node, "");

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>The net elements of a hierarchy by CAEX id: the nets and their places, transitions and arcs.</summary>
    private static Dictionary<string, InternalElementType> NetElements(InstanceHierarchyType ih)
    {
        var result = new Dictionary<string, InternalElementType>(PtCaex.Ids);
        foreach (var element in Elements(ih))
        {
            if (CaexToPtNet.ElementKindOf(element) == null || string.IsNullOrEmpty(element.ID)) continue;
            result.TryAdd(element.ID, element);
        }
        return result;
    }

    private static IEnumerable<string> ArcEndIds(IEnumerable<InternalElementType> elements) =>
        elements.SelectMany(e => e.ExternalInterface)
            .Where(i => IsArcEnd(i.RefBaseClassPath) && !string.IsNullOrEmpty(i.ID))
            .Select(i => i.ID!);

    private static bool IsArcEnd(string? classPath) =>
        PtCaex.IsClass(classPath, PtNames.NodeArcEnd)
        || PtCaex.IsClass(classPath, PtNames.ArcSource)
        || PtCaex.IsClass(classPath, PtNames.ArcTarget);
}
