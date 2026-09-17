using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// Regression tests for defects found in the mapper, each written to fail on
/// the code before the fix.
/// </summary>
public class RegressionTests
{
    private static PtNet SmallNet(string suffix = "")
    {
        var net = new PtNet { Id = "n" + suffix, Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });
        net.Places.Add(new PtPlace { Id = "p2", Name = "P2", Bounds = new PtBounds(200, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a1", Name = "Arc_P1_T1", SourceId = "p1", TargetId = "t1" });
        net.Arcs.Add(new PtArc { Id = "a2", Name = "Arc_T1_P2", SourceId = "t1", TargetId = "p2" });
        return net;
    }

    private static List<string> AllIds(CAEXDocument doc) =>
        doc.CAEXFile.Node.DescendantsAndSelf().Attributes("ID").Select(a => a.Value).ToList();

    // ── critical: interface and link ids per document ───────────────────────

    [Fact]
    public void ImportingTheSameNetTwiceLeavesNoDuplicateIdAnywhere()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, SmallNet(), "First");
        PtNetToCaex.AppendInto(doc, SmallNet(), "Second");

        // Every ID attribute in the document, not only the InternalElements:
        // the interfaces and links were the ones that collided.
        var ids = AllIds(doc);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RemovingAnArcInOneCopyLeavesTheOtherCopyWired()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var first = PtNetToCaex.AppendInto(doc, SmallNet(), "First");
        var second = PtNetToCaex.AppendInto(doc, SmallNet(), "Second");

        var reduced = SmallNet();
        reduced.Arcs.RemoveAll(a => a.Id == "a1");
        var summary = PtNetUpdater.UpdateInPlace(doc, second, reduced);

        Assert.Equal(1, summary.Removed);

        // The first hierarchy was never touched and reads back complete.
        var untouched = CaexToPtNet.Read(first).Single();
        Assert.Equal(2, untouched.Arcs.Count);
        Assert.Equal(4, first.InternalElement.Single().InternalLink.Count());
        Assert.DoesNotContain(summary.Notes, n => n.Contains("First", StringComparison.Ordinal));
    }

    // ── high: the reader says what it left out, the updater keeps it ────────

    [Fact]
    public void AnArcWhoseLinkIsMissingIsReportedAndSurvivesTheNextUpdate()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var netIE = ih.InternalElement.Single();

        // Someone deleted one link in the editor.
        netIE.InternalLink.First(l => l.Name!.EndsWith("_target", StringComparison.Ordinal)).Remove();

        var read = CaexToPtNet.Read(ih).Single();
        Assert.Single(read.Arcs);
        Assert.Single(read.Unresolved);
        Assert.Contains(read.Warnings, w => w.Contains("does not resolve", StringComparison.Ordinal));

        // What the diagram sends back lacks that arc, and that must not delete it.
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Pnml.Read(Pnml.Write(read)));

        Assert.Equal(0, summary.Removed);
        Assert.Equal(5, netIE.InternalElement.Count());
        Assert.Contains(summary.Notes, n => n.StartsWith("Kept ", StringComparison.Ordinal));
    }

    [Fact]
    public void ALinkBetweenTwoArcsDoesNotMakeAnArcAnEndpoint()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var netIE = ih.InternalElement.Single();

        // Rewire a1's source link onto a2's Target interface.
        var a2Target = netIE.InternalElement.First(e => e.Name == "Arc_T1_P2").ExternalInterface.First(i => i.Name == "Target");
        var link = netIE.InternalLink.First(l => l.Name!.EndsWith("_source", StringComparison.Ordinal));
        link.BInterface = a2Target;

        var read = CaexToPtNet.Read(ih).Single();

        // a1 is unresolved now, a2 is fine; neither has an arc as an endpoint.
        Assert.All(read.Arcs, a =>
        {
            Assert.NotNull(read.FindNode(a.SourceId));
            Assert.NotNull(read.FindNode(a.TargetId));
        });
        Assert.Contains("a1", read.Unresolved);
    }

    // ── medium, same area: two arcs reducing to one name ────────────────────

    [Fact]
    public void TwoArcsWithTheSameStoredIdLeaveTheReaderWithDistinctIds()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var netIE = ih.InternalElement.Single();

        // A copy-pasted arc keeps its Identification/id.
        var a2 = netIE.InternalElement.First(e => e.Name == "Arc_T1_P2");
        a2.Attribute[PtNames.Attributes.Identification]!.Attribute[PtNames.Attributes.Id]!.Value = "a1";

        var read = CaexToPtNet.Read(ih).Single();
        var ids = read.Arcs.Select(a => a.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.True(PtIdText.IsXmlName(id)));

        // And the updater pairs them the same way: nothing added, nothing removed.
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Pnml.Read(Pnml.Write(read)));
        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
    }

    // ── high: layout of a fed cycle ─────────────────────────────────────────

    [Fact]
    public void AFedCycleIsLaidOutWithinTheNodeCount()
    {
        var net = new PtNet { Id = "loop" };
        net.Transitions.Add(new PtTransition { Id = "entry" });
        for (var i = 0; i < 10; i++)
        {
            net.Places.Add(new PtPlace { Id = $"p{i}" });
            net.Transitions.Add(new PtTransition { Id = $"t{i}" });
            net.Arcs.Add(new PtArc { Id = $"pt{i}", SourceId = $"p{i}", TargetId = $"t{i}" });
            net.Arcs.Add(new PtArc { Id = $"tp{i}", SourceId = $"t{i}", TargetId = $"p{(i + 1) % 10}" });
        }
        net.Arcs.Add(new PtArc { Id = "in", SourceId = "entry", TargetId = "p0" });

        PtLayout.ArrangeMissing(net);

        var width = net.Nodes.Max(n => n.Bounds!.X) - net.Nodes.Min(n => n.Bounds!.X);
        Assert.True(width <= 140 * net.Nodes.Count(), $"net is {width} px wide");
    }

    [Fact]
    public void TheLayoutOrdersAColumnSoThatArcsDoNotCross()
    {
        // The MPS500 quality-check net, minus the coordinates: two input places feed the
        // two branch transitions, and ordering a column by id put four arcs
        // across each other. The ordering pass has to get this to zero.
        var net = new PtNet { Id = "quality" };
        foreach (var id in new[] { "Idle", "Checking", "Approved", "Rejected", "Result_OK", "Result_NOK" })
            net.Places.Add(new PtPlace { Id = id, Name = id });
        foreach (var id in new[] { "Start", "Approve", "Reject" })
            net.Transitions.Add(new PtTransition { Id = id, Name = id });

        void Arc(string from, string to) =>
            net.Arcs.Add(new PtArc { Id = $"{from}_{to}", SourceId = from, TargetId = to });

        Arc("Idle", "Start"); Arc("Start", "Checking");
        Arc("Checking", "Approve"); Arc("Checking", "Reject");
        Arc("Result_OK", "Approve"); Arc("Result_NOK", "Reject");
        Arc("Approve", "Approved"); Arc("Reject", "Rejected");

        PtLayout.ArrangeMissing(net);

        Assert.Equal(0, Crossings(net));
    }

    [Fact]
    public void TheMainPathOfTheQualityCheckRunsInOneLine()
    {
        // Every column used to fill from the top, so Idle and Start sat a row
        // above Checking and the arc between them ran diagonally. The column of
        // three stays put, the others line up with it, and the two verdict
        // branches sit symmetrically above and below the main path.
        var net = new PtNet { Id = "quality" };
        foreach (var id in new[] { "Idle", "Checking", "Approved", "Rejected", "Result_OK", "Result_NOK" })
            net.Places.Add(new PtPlace { Id = id, Name = id, InitialMarking = id == "Idle" ? 1 : 0 });
        foreach (var id in new[] { "Start", "Approve", "Reject", "Release", "Discard" })
            net.Transitions.Add(new PtTransition { Id = id, Name = id });

        void Arc(string from, string to) =>
            net.Arcs.Add(new PtArc { Id = $"{from}_{to}", SourceId = from, TargetId = to });

        Arc("Idle", "Start"); Arc("Start", "Checking");
        Arc("Checking", "Approve"); Arc("Checking", "Reject");
        Arc("Result_OK", "Approve"); Arc("Result_NOK", "Reject");
        Arc("Approve", "Approved"); Arc("Reject", "Rejected");
        Arc("Approved", "Release"); Arc("Release", "Idle");
        Arc("Rejected", "Discard"); Arc("Discard", "Idle");

        PtLayout.ArrangeMissing(net);
        double Y(string id) => net.FindNode(id)!.Bounds!.Centre.Y;

        Assert.Equal(Y("Checking"), Y("Idle"));
        Assert.Equal(Y("Checking"), Y("Start"));

        var above = Y("Checking") - Y("Reject");
        var below = Y("Approve") - Y("Checking");
        Assert.True(above > 0 && below > 0, "one branch above the main path, one below");
        Assert.Equal(above, below);
        Assert.Equal(Y("Reject"), Y("Discard"));
        Assert.Equal(Y("Approve"), Y("Release"));
    }

    [Fact]
    public void ALoopBackToTheStartIsLaidOutFromWhereTheTokenIs()
    {
        // The quality-check net once Release and Discard return to Idle: every place
        // has something feeding it except the two inputs, so "start where nothing
        // feeds" began the loop at Result_NOK and put all eleven nodes in one row,
        // with two arcs straight through four nodes each. The token says where a
        // Petri net begins.
        var net = new PtNet { Id = "quality" };
        foreach (var id in new[] { "Idle", "Checking", "Approved", "Rejected", "Result_OK", "Result_NOK" })
            net.Places.Add(new PtPlace { Id = id, Name = id, InitialMarking = id == "Idle" ? 1 : 0 });
        foreach (var id in new[] { "Start", "Approve", "Reject", "Release", "Discard" })
            net.Transitions.Add(new PtTransition { Id = id, Name = id });

        void Arc(string from, string to) =>
            net.Arcs.Add(new PtArc { Id = $"{from}_{to}", SourceId = from, TargetId = to });

        Arc("Idle", "Start"); Arc("Start", "Checking");
        Arc("Checking", "Approve"); Arc("Checking", "Reject");
        Arc("Result_OK", "Approve"); Arc("Result_NOK", "Reject");
        Arc("Approve", "Approved"); Arc("Reject", "Rejected");
        Arc("Approved", "Release"); Arc("Release", "Idle");
        Arc("Rejected", "Discard"); Arc("Discard", "Idle");

        PtLayout.ArrangeMissing(net);

        // Idle is where the drawing starts.
        Assert.Equal(net.Nodes.Min(n => n.Bounds!.X), net.FindNode("Idle")!.Bounds!.X);

        // The two arcs that return to it are routed around, the rest are not.
        var routed = net.Arcs.Where(a => a.Waypoints.Count > 0).Select(a => a.Id).OrderBy(i => i).ToList();
        Assert.Equal(new[] { "Discard_Idle", "Release_Idle" }, routed);

        // Nothing crosses among the straight arcs, and none runs through a node.
        var straight = new PtNet { Id = "straight" };
        straight.Places.AddRange(net.Places);
        straight.Transitions.AddRange(net.Transitions);
        straight.Arcs.AddRange(net.Arcs.Where(a => a.Waypoints.Count == 0));
        Assert.Equal(0, Crossings(straight));
        Assert.Equal(0, ThroughNodes(straight));

        // A route that runs below the drawing meets a place from the side, not
        // through the name printed underneath it.
        var below = net.Arcs.Single(a => a.Waypoints.Count > 0
            && a.Waypoints.Max(w => w.Y) > net.Nodes.Max(n => n.Bounds!.Y));
        var idle = net.FindNode("Idle")!.Bounds!;
        Assert.True(below.Waypoints[^1].Y == idle.Centre.Y, "the lower route should enter Idle from the side");
    }

    /// <summary>How many straight arcs pass through a node that is not one of their ends.</summary>
    private static int ThroughNodes(PtNet net)
    {
        var hits = 0;
        foreach (var arc in net.Arcs)
        {
            var a = net.FindNode(arc.SourceId)!.Bounds!.Centre;
            var b = net.FindNode(arc.TargetId)!.Bounds!.Centre;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;

            foreach (var node in net.Nodes)
            {
                if (node.Id == arc.SourceId || node.Id == arc.TargetId) continue;
                var c = node.Bounds!.Centre;
                var t = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / lengthSquared;
                if (t <= 0 || t >= 1) continue;
                var distance = Math.Abs(dx * (c.Y - a.Y) - dy * (c.X - a.X)) / Math.Sqrt(lengthSquared);
                if (distance < PtLayout.NodeSize / 2) hits++;
            }
        }
        return hits;
    }

    /// <summary>How many pairs of arcs cross, drawn as straight lines between node centres.</summary>
    private static int Crossings(PtNet net)
    {
        (double X, double Y) Centre(string id)
        {
            var b = net.FindNode(id)!.Bounds!;
            return (b.X + b.Width / 2, b.Y + b.Height / 2);
        }

        static double Side((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var segments = net.Arcs.Select(a => (From: Centre(a.SourceId), To: Centre(a.TargetId))).ToList();

        var crossings = 0;
        for (var i = 0; i < segments.Count; i++)
        for (var j = i + 1; j < segments.Count; j++)
        {
            var (p1, p2) = segments[i];
            var (p3, p4) = segments[j];
            var d1 = Side(p3, p4, p1);
            var d2 = Side(p3, p4, p2);
            var d3 = Side(p1, p2, p3);
            var d4 = Side(p1, p2, p4);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                crossings++;
        }

        return crossings;
    }

    [Fact]
    public void ADocumentMissingOneIsoPtLibraryGetsItBack()
    {
        // Three of the four is not a shape anyone means to be in. Borrowing the
        // set and taking it away again would leave the document as broken as it
        // was found, so the missing one is added and stays.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        doc.CAEXFile.InterfaceClassLib[PtNames.InterfaceClassLib]!.Remove();

        var net = CaexToPtNet.Read(ih).Single();
        net.Places.Add(new PtPlace { Id = "p3", Name = "P3", Bounds = new PtBounds(300, 0, 50, 50) });
        PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.NotNull(doc.CAEXFile.InterfaceClassLib[PtNames.InterfaceClassLib]);
        Assert.NotNull(doc.CAEXFile.RoleClassLib[PtNames.RoleClassLib]);
        Assert.NotNull(doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]);
        Assert.NotNull(doc.CAEXFile.AttributeTypeLib[PtNames.AttributeTypeLib]);
    }

    // ── a new document has to open in the editor ────────────────────────────

    [Fact]
    public void ANewlyCreatedDocumentResolvesEveryClassPathOnItsOwn()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // The AML Editor does not follow file references, so a document the
        // plugin creates has to carry what it names. The only reference left is
        // the AML base library, which is a URL every AML tool knows.
        var aliases = doc.CAEXFile.ExternalReference.Select(r => r.Alias).ToList();
        Assert.Equal(new[] { AmlBase.Alias }, aliases);

        Assert.NotNull(doc.CAEXFile.AttributeTypeLib[DiagramInterchangeLibrary.LibName]);
        Assert.NotNull(doc.CAEXFile.AttributeTypeLib[ObjectReferencesLibrary.LibName]);

        // And no path names an alias the document does not declare.
        foreach (var path in doc.CAEXFile.Node.DescendantsAndSelf().Attributes()
                     .Where(a => a.Name.LocalName.StartsWith("Ref", StringComparison.Ordinal))
                     .Select(a => a.Value))
        {
            var at = path.IndexOf('@');
            if (at > 0) Assert.Contains(path[..at], aliases);
        }
    }

    [Fact]
    public void TheLibraryArtefactStillPointsAtItsNeighbours()
    {
        // The published library file is a different case: it lives next to the
        // ones it references, and embedding them would make it something other
        // than the file published in libraries/.
        var doc = CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);

        var aliases = doc.CAEXFile.ExternalReference.Select(r => r.Alias).ToList();
        Assert.Contains(DiagramInterchangeLibrary.Alias, aliases);
        Assert.Contains(ObjectReferencesLibrary.Alias, aliases);
        Assert.Null(doc.CAEXFile.AttributeTypeLib[DiagramInterchangeLibrary.LibName]);
    }

    // ── the library's promise about label positions ─────────────────────────

    [Fact]
    public void AnArcsLabelPositionSurvivesPnmlAndCaex()
    {
        // The published library says of PT_Arc that label positions may be
        // stored as DD_Point attributes. An arc has exactly one label, its
        // inscription, because that is what the modeler renders next to it.
        var net = SmallNet();
        var arc = net.Arcs[0];
        arc.Weight = 3;
        arc.LabelOffset = new PtPoint(12.5, -30);

        // Through PNML and back.
        var again = Pnml.Read(Pnml.Write(net)).Arcs.Single(a => a.Id == arc.Id);
        Assert.Equal(new PtPoint(12.5, -30), again.LabelOffset);

        // And through the document.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "Net");
        var stored = CaexToPtNet.Read(ih).Single().Arcs.Single(a => a.Id == arc.Id);
        Assert.Equal(new PtPoint(12.5, -30), stored.LabelOffset);

        // The attribute is typed, so it resolves against the library.
        var element = ih.InternalElement.Single().InternalElement
            .Single(e => e.Attribute[PtNames.Attributes.Identification]?
                .Attribute[PtNames.Attributes.Id]?.Value == arc.Id);
        var offset = element.Attribute[PtNames.Attributes.LabelOffset];
        Assert.NotNull(offset);
        Assert.EndsWith("/DD_Point", offset!.RefAttributeType, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRoundTripCheckCountsTheDockingPointsAsGeometry()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        var report = PtRoundTripCheck.Run(doc, "Net");
        var geometry = report.Checks.Single(c => c.Aspect == "geometry");

        // Three nodes with bounds and two arcs with two docking points each.
        // Counting only the bounds reported nine where twenty-five attributes
        // had been written, and that number went into the validation report.
        Assert.True(geometry.Passed, report.ToString());
        Assert.Contains("port", string.Join(" ", PortAttributes(ih)), StringComparison.Ordinal);
        Assert.Equal(4, PortAttributes(ih).Count);
    }

    private static List<string> PortAttributes(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement)
            .SelectMany(e => e.ExternalInterface)
            .Where(i => i.Attribute[PtNames.Attributes.PortCoordinate] != null)
            .Select(i => "port " + i.ID)
            .ToList();

    // ── high: reference nodes across pages ──────────────────────────────────

    [Fact]
    public void ArcsThroughReferenceNodesReachTheConcreteNode()
    {
        const string pnml = """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">
                <page id="p1">
                  <place id="a"/><transition id="t"/>
                  <arc id="x1" source="a" target="t"/>
                  <arc id="x2" source="t" target="rp"/>
                </page>
                <page id="p2">
                  <referencePlace id="rp" ref="rp2"/>
                  <referencePlace id="rp2" ref="b"/>
                  <place id="b"/>
                  <arc id="x3" source="rp" target="ghost"/>
                </page>
              </net>
            </pnml>
            """;

        var net = Pnml.Read(pnml);

        Assert.Equal(2, net.Places.Count);
        Assert.Equal("b", net.Arcs.Single(a => a.Id == "x2").TargetId);
        Assert.DoesNotContain(net.Arcs, a => a.Id == "x3");
        Assert.Contains("x3", net.Unresolved);
        Assert.Contains(net.Warnings, w => w.Contains("x3", StringComparison.Ordinal));
    }

    // ── high: diagram-interchange paths follow the document ─────────────────

    [Fact]
    public void ASelfContainedDocumentGetsUnqualifiedDiagramTypePaths()
    {
        var fresh = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(fresh, SmallNet(), "Net");

        // Inlined the way the editor-test files are, then opened like the
        // plugin opens them.
        var (xml, _) = PtSelfContained.Inline(XmlOf(fresh), PtSelfContained.ReadLibraries(LibraryDir()));
        var doc = CAEXDocument.LoadFromString(xml);
        var ih = doc.CAEXFile.InstanceHierarchy["Net"];

        // An edit that writes new geometry: a bent arc and a moved label.
        var edited = CaexToPtNet.Read(ih).Single();
        edited.Arcs[0].Waypoints.Add(new PtPoint(60, 90));
        edited.Places[0].LabelOffset = new PtPoint(0, 40);
        PtNetUpdater.UpdateInPlace(doc, ih, edited);

        var aliases = doc.CAEXFile.ExternalReference.Select(r => r.Alias).ToHashSet(StringComparer.Ordinal);
        foreach (var path in doc.CAEXFile.Node.Descendants().Attributes("RefAttributeType").Select(a => a.Value))
        {
            var at = path.IndexOf('@');
            if (at > 0) Assert.Contains(path[..at], aliases);
        }

        Assert.True(PtRoundTripCheck.Run(doc, "Net").Passed);
    }

    [Fact]
    public void ADocumentReferencingTheDiagramLibraryUnderItsOwnAliasKeepsThatAlias()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // The author kept the library outside and called the reference "DD".
        // The mapper embeds it for a document of its own making, so the shape
        // has to be built here: take the library out, point at it, and write
        // every path that named it through the alias.
        doc.CAEXFile.AttributeTypeLib[DiagramInterchangeLibrary.LibName]?.Remove();
        var reference = doc.CAEXFile.ExternalReference.Append();
        reference.Alias = "DD";
        reference.Path = DiagramInterchangeLibrary.FileName;

        foreach (var attribute in doc.CAEXFile.Node.DescendantsAndSelf().Attributes("RefAttributeType").ToList())
        {
            if (attribute.Value.StartsWith(DiagramInterchangeLibrary.LibName + "/", StringComparison.Ordinal))
                attribute.Value = "DD@" + attribute.Value;
        }

        var edited = CaexToPtNet.Read(ih).Single();
        edited.Arcs[0].Waypoints.Add(new PtPoint(60, 90));
        PtNetUpdater.UpdateInPlace(doc, ih, edited);

        var paths = doc.CAEXFile.Node.Descendants().Attributes("RefAttributeType")
            .Select(a => a.Value).Where(v => v.Contains(DiagramInterchangeLibrary.LibName, StringComparison.Ordinal));
        Assert.All(paths, p => Assert.StartsWith("DD@", p));
    }

    // ── low, ids: an interface found by name keeps its id ───────────────────

    [Fact]
    public void AnInterfaceFoundByNameKeepsTheIdTheDocumentGaveIt()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var p1 = ih.InternalElement.Single().InternalElement.First(e => e.Name == "P1");

        // Another tool's id on the endpoint, and the link that depends on it.
        var endpoint = p1.ExternalInterface.Single();
        endpoint.ID = "{11111111-2222-3333-4444-555555555555}";
        var link = ih.InternalElement.Single().InternalLink.First(l => l.Name!.EndsWith("_source", StringComparison.Ordinal));
        link.BInterface = endpoint;

        var net = CaexToPtNet.Read(ih).Single();
        PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.Equal("{11111111-2222-3333-4444-555555555555}", p1.ExternalInterface.Single().ID);
    }

    // ── further regressions ─────────────────────────────────────────────────

    [Fact]
    public void TurningAPlaceIntoATransitionKeepsItsArcsWired()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // p2 becomes a transition under the same id, as it would if the user
        // deleted it on the canvas and drew a transition with the same name.
        var changed = SmallNet();
        changed.Places.RemoveAll(p => p.Id == "p2");
        changed.Transitions.Add(new PtTransition { Id = "p2", Name = "P2", Bounds = new PtBounds(200, 0, 50, 50) });
        changed.Arcs.RemoveAll(a => a.Id == "a2");
        changed.Arcs.Add(new PtArc { Id = "a2", Name = "Arc_T1_P2", SourceId = "p1", TargetId = "p2" });

        PtNetUpdater.UpdateInPlace(doc, ih, changed);

        // The replaced element took its interfaces with it; the arc has to be
        // wired to the new one rather than to what the stale wiring remembered.
        var reread = CaexToPtNet.Read(ih).Single();
        var arc = Assert.Single(reread.Arcs, a => a.Id == "a2");
        Assert.Equal("p1", arc.SourceId);
        Assert.Equal("p2", arc.TargetId);
    }

    [Fact]
    public void ANetWithDuplicateNodeIdsIsLaidOutInsteadOfThrowing()
    {
        // Invalid PNML, which the validator reports; arranging it must still
        // work, or the user never gets as far as the message.
        var net = new PtNet { Id = "dup" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        PtLayout.ArrangeMissing(net);

        Assert.All(net.Nodes, n => Assert.NotNull(n.Bounds));
        Assert.Contains(PtValidator.Validate(net), f => f.Rule == "PT01");
    }

    [Fact]
    public void NonFiniteCoordinatesNeverReachTheModelOrTheFile()
    {
        const string pnml = """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">
                <page id="p">
                  <place id="a"><graphics><position x="NaN" y="1e400"/><dimension x="50" y="50"/></graphics></place>
                  <transition id="t"><graphics><position x="10" y="20"/></graphics></transition>
                  <arc id="x" source="a" target="t"/>
                </page>
              </net>
            </pnml>
            """;

        var net = Pnml.Read(pnml);
        var place = net.Places.Single();

        Assert.True(double.IsFinite(place.Bounds!.X));
        Assert.True(double.IsFinite(place.Bounds!.Y));
        Assert.DoesNotContain("NaN", Pnml.Write(net), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRoundTripCheckNoticesARetargetedArc()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        var before = CAEXDocument.LoadFromString(XmlOf(doc));

        // Same arcs, same ids, same weights: only an endpoint moves.
        var retargeted = SmallNet();
        retargeted.Arcs.Single(a => a.Id == "a2").TargetId = "p1";
        PtNetUpdater.UpdateInPlace(doc, ih, retargeted);

        var report = PtRoundTripCheck.Compare(before, doc, "Net");
        var connections = report.Checks.Single(c => c.Aspect == "connections");

        Assert.False(connections.Passed, report.ToString());
    }

    [Fact]
    public void APnmlFileDeclaringItsOwnEncodingIsReadFromTheBytes()
    {
        // Latin-1 bytes with a matching declaration. Decoding as UTF-8 first,
        // which the string overload forces a caller to do, turns the umlaut
        // into a replacement character.
        const string text = """
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">
                <page id="p"><place id="a"><name><text>Prüfung</text></name></place></page>
              </net>
            </pnml>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(text));
        var net = Pnml.ReadAll(stream).Single();

        Assert.Equal("Prüfung", net.Places.Single().Name);
    }

    private static string XmlOf(CAEXDocument doc)
    {
        using var stream = doc.SaveToStream(prettyPrint: true);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LibraryDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "libraries", PtNames.LibraryFileName)))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new InvalidOperationException("libraries folder not found"), "libraries");
    }
}
