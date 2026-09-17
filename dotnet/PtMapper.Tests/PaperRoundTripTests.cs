using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The round trip between an editor and AML for Petri nets, on the MPS500
/// bridging example in examples/paper: the quality-check station of the Festo
/// MPS500, hierarchy IH_PT_BehaviorView of BridgingExample_MPS500.aml, six
/// places, five transitions, twelve arcs. Release and Discard close the loop
/// back to Idle.
///
/// The round trip is validated on an example model, so what has to hold is not
/// that some net survives but that *this* net survives, with everything the
/// document carries around it: the CAEX ids, the identification, the weights and
/// markings, the links from the net into the plant hierarchy, and the external
/// references to the library artefacts.
///
/// These tests compare the whole document, not a selection of properties. The
/// property-by-property assertions exist so that a failure says what broke.
/// </summary>
public class PaperRoundTripTests
{
    private static readonly string Document = TestFiles.Paper("BridgingExample_MPS500.aml");
    private const string Hierarchy = "IH_PT_BehaviorView";

    /// <summary>The same document with its libraries inline, the file the editor run opens.</summary>
    private static readonly string SelfContained = TestFiles.Paper("BridgingExample_MPS500_selfcontained.aml");

    /// <summary>The two files shipped next to the example, written by ptmap.</summary>
    private static readonly string ShippedPnml = TestFiles.Paper("MPS500_PT.pnml");
    private static readonly string ShippedAml = TestFiles.Paper("MPS500_PT_export.aml");

    // ── AML -> PNML -> AML ──────────────────────────────────────────────────

    [Fact]
    public void AmlToPnmlAndBackLeavesTheDocumentUnchanged()
    {
        // Both sides are loaded the same way, so anything Aml.Engine does while
        // reading cancels out and what is left is the round trip's own doing.
        // This is a stricter test than "equal apart from formatting": the two
        // trees have to be the same XML.
        var before = CAEXDocument.LoadFromFile(Document).CAEXFile.Node.ToString();

        var doc = CAEXDocument.LoadFromFile(Document);
        RoundTrip(doc);

        Assert.Equal(before, doc.CAEXFile.Node.ToString());
    }

    [Fact]
    public void TheNetItselfComesBackElementForElement()
    {
        var doc = CAEXDocument.LoadFromFile(Document);
        var ih = doc.CAEXFile.InstanceHierarchy[Hierarchy];
        var net = CaexToPtNet.Read(ih).Single();

        Assert.Equal(6, net.Places.Count);
        Assert.Equal(5, net.Transitions.Count);
        Assert.Equal(12, net.Arcs.Count);

        var namesBefore = net.Nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var markingBefore = net.Places.ToDictionary(p => p.Id, p => p.InitialMarking);
        var weightBefore = net.Arcs.ToDictionary(a => a.Id, a => a.Weight);
        var wiringBefore = net.Arcs
            .Select(a => $"{a.SourceId}->{a.TargetId}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        RoundTrip(doc);

        var after = CaexToPtNet.Read(doc.CAEXFile.InstanceHierarchy[Hierarchy]).Single();

        Assert.Equal(6, after.Places.Count);
        Assert.Equal(5, after.Transitions.Count);
        Assert.Equal(12, after.Arcs.Count);
        Assert.Equal(namesBefore, after.Nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(markingBefore, after.Places.ToDictionary(p => p.Id, p => p.InitialMarking));
        Assert.Equal(weightBefore, after.Arcs.ToDictionary(a => a.Id, a => a.Weight));
        Assert.Equal(wiringBefore, after.Arcs
            .Select(a => $"{a.SourceId}->{a.TargetId}")
            .OrderBy(s => s, StringComparer.Ordinal));

        // Result_OK and Result_NOK are the net's two input places.
        Assert.Contains(after.Places, p => p.Name == "Result_OK");
        Assert.Contains(after.Places, p => p.Name == "Result_NOK");
    }

    [Fact]
    public void TheCaexIdentityIsTheOneTheDocumentBroughtAlong()
    {
        var doc = CAEXDocument.LoadFromFile(Document);
        var before = Identity(doc);

        RoundTrip(doc);

        // CAEX ids, Identification/id and the CAEX names, per element. A tool
        // that reassigned any of these would break every reference into the net
        // from outside, and the ids here are braced GUIDs, which is exactly the
        // case that made the plugin refuse the document once.
        Assert.Equal(before, Identity(doc));
    }

    [Fact]
    public void TheLinksIntoThePlantHierarchySurvive()
    {
        var doc = CAEXDocument.LoadFromFile(Document);
        var before = PlantWiring(doc);

        // Five signal couplings: Start, Approved, Rejected to the controller,
        // Result_OK and Result_NOK to the station's sensors.
        Assert.Equal(5, before.Count);

        RoundTrip(doc);

        Assert.Equal(before, PlantWiring(doc));

        // The interfaces those links hang on are not PT interfaces, and the
        // sync must not mistake them for endpoints it owns.
        var plantPorts = PtElements(doc)
            .SelectMany(e => e.ExternalInterface)
            .Count(i => i.Name == "PlantSignalLink");
        Assert.Equal(5, plantPorts);
    }

    [Fact]
    public void TheLibraryReferencesAndTheAmlStructureAreUntouched()
    {
        var doc = CAEXDocument.LoadFromFile(Document);
        RoundTrip(doc);

        // The document references its libraries; a sync must not inline them,
        // or the modular document turns into a different file than the one
        // published with its separate library artefacts.
        Assert.Empty(doc.CAEXFile.SystemUnitClassLib);
        Assert.Empty(doc.CAEXFile.RoleClassLib);
        Assert.Empty(doc.CAEXFile.InterfaceClassLib);
        Assert.Empty(doc.CAEXFile.AttributeTypeLib);

        var references = doc.CAEXFile.ExternalReference.ToDictionary(r => r.Alias, r => r.Path);
        Assert.Equal("ISO_PT_DomainLibrary_v0.3.aml", references["ISO_PT_DomainLibrary"]);
        Assert.Equal("OMG_DD_AttributeTypeLib_v0.1.aml", references["OMG_DD"]);

        // The arc encoding of the ISO_PT library: an arc is an element of its
        // own with two interfaces, and each connection is two InternalLinks.
        var netIE = doc.CAEXFile.InstanceHierarchy[Hierarchy].InternalElement.Single();
        var arcs = netIE.InternalElement
            .Where(e => e.RefBaseSystemUnitPath?.EndsWith("/" + PtNames.Arc, StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(12, arcs.Count);
        Assert.All(arcs, arc =>
        {
            Assert.Single(arc.ExternalInterface, i =>
                i.RefBaseClassPath?.EndsWith("/" + PtNames.ArcSource, StringComparison.Ordinal) == true);
            Assert.Single(arc.ExternalInterface, i =>
                i.RefBaseClassPath?.EndsWith("/" + PtNames.ArcTarget, StringComparison.Ordinal) == true);
        });
        Assert.Equal(24, netIE.InternalLink.Count());
    }

    // ── PNML -> AML -> PNML ─────────────────────────────────────────────────

    [Fact]
    public void PnmlToAmlAndBackYieldsTheSamePnml()
    {
        var ih = CAEXDocument.LoadFromFile(Document).CAEXFile.InstanceHierarchy[Hierarchy];
        var pnml = Pnml.Write(CaexToPtNet.Read(ih).Single());

        var fresh = CAEXDocument.New_CAEXDocument();
        var written = PtNetToCaex.AppendInto(fresh, Pnml.ReadAll(pnml), Hierarchy);

        Assert.Equal(pnml, Pnml.Write(CaexToPtNet.Read(written).Single()));
    }

    [Fact]
    public void ThePnmlIsAPlaceTransitionNetAnyReaderCanTake()
    {
        var ih = CAEXDocument.LoadFromFile(Document).CAEXFile.InstanceHierarchy[Hierarchy];
        var pnml = XDocument.Parse(Pnml.Write(CaexToPtNet.Read(ih).Single()));

        var ns = XNamespace.Get("http://www.pnml.org/version-2009/grammar/pnml");
        var net = Assert.Single(pnml.Root!.Elements(ns + "net"));

        Assert.Equal("http://www.pnml.org/version-2009/grammar/ptnet", (string?)net.Attribute("type"));

        var page = Assert.Single(net.Elements(ns + "page"));
        Assert.Equal(6, page.Elements(ns + "place").Count());
        Assert.Equal(5, page.Elements(ns + "transition").Count());
        Assert.Equal(12, page.Elements(ns + "arc").Count());

        // Every arc has to resolve, or a reader has a dangling reference.
        var ids = page.Elements().Select(e => (string?)e.Attribute("id")).ToHashSet();
        foreach (var arc in page.Elements(ns + "arc"))
        {
            Assert.Contains((string?)arc.Attribute("source"), ids);
            Assert.Contains((string?)arc.Attribute("target"), ids);
        }
    }

    // ── the graphical half, which this document does not carry ──────────────

    [Fact]
    public void GeometryAddedToTheNetSurvivesTheRoundTrip()
    {
        // Layout is optional for basic P/T nets, since a layered layout can
        // arrange one from the arcs alone, and this document indeed carries
        // none on its PT side. That makes the round trip
        // above say nothing about the diagram-interchange half, so the same net
        // is laid out, given a bend and a label offset, and sent round again.
        var source = CAEXDocument.LoadFromFile(Document);
        var net = CaexToPtNet.Read(source.CAEXFile.InstanceHierarchy[Hierarchy]).Single();

        var nodes = net.Nodes.ToList();
        Assert.All(nodes, n => Assert.Null(n.Bounds));
        Assert.Equal(nodes.Count, PtLayout.ArrangeMissing(net));

        var labelled = nodes[0];
        var bent = net.Arcs[0];
        labelled.LabelOffset = new PtPoint(12, -18);
        bent.Waypoints.Add(new PtPoint(250, 300));
        bent.Waypoints.Add(new PtPoint(310, 340));

        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, new[] { net }, Hierarchy);
        var before = doc.CAEXFile.Node.ToString();

        var readBack = CaexToPtNet.Read(ih).Single();
        PtNetUpdater.UpdateInPlace(doc, ih, Pnml.Read(Pnml.Write(readBack)));

        Assert.Equal(before, doc.CAEXFile.Node.ToString());

        var after = CaexToPtNet.Read(ih).Single();
        Assert.Equal(
            nodes.Select(n => $"{n.Id}:{n.Bounds}"),
            after.Nodes.Select(n => $"{n.Id}:{n.Bounds}"));
        Assert.Equal(new PtPoint(12, -18), after.FindNode(labelled.Id)!.LabelOffset);
        Assert.Equal(bent.Waypoints, after.Arcs.Single(a => a.Id == bent.Id).Waypoints);
    }

    // ── what an editing session does to the file ────────────────────────────

    [Fact]
    public void AnEditorSessionAddsTheLayoutAndNothingElse()
    {
        // The plugin's path between opening a document and pressing "Update
        // InstanceHierarchy": read, normalise the ids for the canvas, arrange a
        // net that has no layout, through PNML, write back.
        var before = CAEXDocument.LoadFromFile(Document);
        var after = CAEXDocument.LoadFromFile(Document);

        var ih = after.CAEXFile.InstanceHierarchy[Hierarchy];
        var net = CaexToPtNet.Read(ih).Single();
        PtIdText.NormaliseIds(net);
        Assert.Equal(11, PtLayout.ArrangeMissing(net));

        var summary = PtNetUpdater.UpdateInPlace(after, ih, Pnml.Read(Pnml.Write(net)));
        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);

        // Everything except the layout has to come out of that unchanged. The
        // layout itself is the point: a net that arrives without one is arranged
        // for the canvas, and a sync then stores what the user sees.
        var report = PtRoundTripCheck.Compare(before, after, Hierarchy);
        Assert.True(report.Passed, report.ToString());

        var laidOut = CaexToPtNet.Read(ih).Single();
        Assert.All(laidOut.Nodes, n => Assert.NotNull(n.Bounds));

        // And the saved file passes the round-trip check in its own right,
        // geometry included, which is what the editor run has to confirm.
        Assert.True(PtRoundTripCheck.Run(after, Hierarchy).Passed);
    }

    // ── the self-contained file, which is what the editor run opens ─────────

    /// <summary>
    /// The AML Editor loads no document referenced through an ExternalReference,
    /// in any form (relative, absolute, file URI, https), so the run through the
    /// editor uses the self-contained file. Its libraries are inline, and the
    /// writer has to follow that form: an OMG_DD-qualified type path in this file
    /// resolves to nothing, because the file declares no OMG_DD alias. Arranging
    /// the net gives the two loop-closing arcs bend points, which is the case
    /// that once wrote such paths.
    /// </summary>
    [Fact]
    public void AnEditorSessionOnTheSelfContainedFileAddsTheLayoutAndNothingElse()
    {
        var before = CAEXDocument.LoadFromFile(SelfContained);
        var after = CAEXDocument.LoadFromFile(SelfContained);

        var ih = after.CAEXFile.InstanceHierarchy[Hierarchy];
        var net = CaexToPtNet.Read(ih).Single();
        PtIdText.NormaliseIds(net);
        Assert.Equal(11, PtLayout.ArrangeMissing(net));
        Assert.Contains(net.Arcs, a => a.Waypoints.Count > 0);

        var summary = PtNetUpdater.UpdateInPlace(after, ih, Pnml.Read(Pnml.Write(net)));
        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);

        // No type path may use an alias the file does not declare.
        var aliases = after.CAEXFile.ExternalReference.Select(r => r.Alias).ToHashSet(StringComparer.Ordinal);
        var dangling = after.CAEXFile.Node.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName.StartsWith("Ref", StringComparison.Ordinal))
            .Select(a => a.Value)
            .Where(v => v.IndexOf('@') > 0 && !aliases.Contains(v[..v.IndexOf('@')]))
            .Distinct()
            .ToList();
        Assert.Empty(dangling);

        // Compared with the self-contained file it came from, not with the
        // modular one: against that, every inlined library counts as a change.
        var report = PtRoundTripCheck.Compare(before, after, Hierarchy);
        Assert.True(report.Passed, report.ToString());
        Assert.Contains("0 graphical value(s) before, 41 after", report.ToString());

        Assert.True(PtRoundTripCheck.Run(after, Hierarchy).Passed);
    }

    [Fact]
    public void TheRoundTripCheckPassesOnTheSelfContainedFile()
    {
        var report = PtRoundTripCheck.Run(CAEXDocument.LoadFromFile(SelfContained), Hierarchy);

        Assert.True(report.Passed, report.ToString());
    }

    [Fact]
    public void TheSelfContainedFileHoldsTheSameNetAsTheModularOne()
    {
        static string NetOf(string path) =>
            Pnml.Write(CaexToPtNet.Read(CAEXDocument.LoadFromFile(path).CAEXFile.InstanceHierarchy[Hierarchy]).Single());

        Assert.Equal(NetOf(Document), NetOf(SelfContained));
    }

    // ── the check the command line runs, and the shipped artefacts ──────────

    [Fact]
    public void TheRoundTripCheckPasses()
    {
        // Same code path as "ptmap roundtrip", which is what gets run against
        // the file the AML Editor saved. A green unit test and a green command
        // line then mean the same thing.
        var report = PtRoundTripCheck.Run(CAEXDocument.LoadFromFile(Document), Hierarchy);

        Assert.True(report.Passed, report.ToString());
        Assert.Equal(11, report.Checks.Count);
    }

    [Fact]
    public void TheShippedPnmlIsWhatTheMapperProduces()
    {
        // The shipped file has to be the one the code writes, or the artefact
        // and the code that is said to produce it drift apart.
        var ih = CAEXDocument.LoadFromFile(Document).CAEXFile.InstanceHierarchy[Hierarchy];
        var expected = Pnml.Write(CaexToPtNet.Read(ih).Single(), indent: true);

        Assert.Equal(Normalise(expected), Normalise(File.ReadAllText(ShippedPnml)));
    }

    [Fact]
    public void TheShippedAmlIsWhatTheMapperProduces()
    {
        // Saved to a file rather than compared in memory: SaveToFile adds the
        // schema location, so only two saved documents are comparable.
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".aml");
        try
        {
            var fresh = CAEXDocument.New_CAEXDocument();
            fresh.CAEXFile.FileName = Path.GetFileName(ShippedAml);
            PtNetToCaex.AppendInto(fresh, Pnml.ReadAll(File.ReadAllText(ShippedPnml)), Hierarchy);
            fresh.SaveToFile(temp, prettyPrint: true);

            // The writing time is the one thing that cannot match.
            Assert.Equal(
                WithoutWritingTime(Normalise(File.ReadAllText(ShippedAml))),
                WithoutWritingTime(Normalise(File.ReadAllText(temp))).Replace(
                    Path.GetFileName(temp), Path.GetFileName(ShippedAml)));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void WhatPnmlCannotCarryIsNamedRatherThanAssumed()
    {
        // The limit of the round trip. A document built from PNML alone holds
        // the net and nothing else: PNML has no notion of a link into a plant
        // hierarchy, so the five couplings of the bridging example are not in
        // it. That is a property of the format, and the reason to keep the net
        // in AML next to the plant. Only the in-place direction keeps them, which
        // TheLinksIntoThePlantHierarchySurvive covers.
        var doc = CAEXDocument.LoadFromFile(ShippedAml);

        Assert.Single(doc.CAEXFile.InstanceHierarchy);
        Assert.DoesNotContain(
            doc.CAEXFile.InstanceHierarchy.Single().InternalElement
                .SelectMany(net => net.InternalElement.Prepend(net))
                .SelectMany(e => e.ExternalInterface),
            i => i.Name == "PlantSignalLink");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// AML to PNML and back, the way the plugin does it: the net leaves as PNML
    /// text, comes back as a model parsed from that text, and is written into
    /// the document it came from.
    /// </summary>
    private static void RoundTrip(CAEXDocument doc)
    {
        var ih = doc.CAEXFile.InstanceHierarchy[Hierarchy];
        var pnml = Pnml.Write(CaexToPtNet.Read(ih).Single());
        PtNetUpdater.UpdateInPlace(doc, ih, Pnml.Read(pnml));
    }

    private static IEnumerable<InternalElementType> PtElements(CAEXDocument doc) =>
        doc.CAEXFile.InstanceHierarchy[Hierarchy].InternalElement
            .SelectMany(net => net.InternalElement.Prepend(net));

    /// <summary>CAEX id, stored PNML id and CAEX name of every PT element.</summary>
    private static List<string> Identity(CAEXDocument doc) =>
        PtElements(doc)
            .Select(e =>
            {
                var id = e.Attribute[PtNames.Attributes.Identification]?
                    .Attribute[PtNames.Attributes.Id]?.Value;
                var name = e.Attribute[PtNames.Attributes.Identification]?
                    .Attribute[PtNames.Attributes.Name]?.Value;
                return $"{e.ID}|{id}|{name}|{e.Name}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every link that joins a PT element to something outside the net's
    /// hierarchy, written so the two sides can be compared by name.
    /// </summary>
    private static List<string> PlantWiring(CAEXDocument doc)
    {
        var ports = PtElements(doc)
            .SelectMany(e => e.ExternalInterface.Select(i => (Owner: e.Name, Port: i)))
            .Where(p => p.Port.ID != null)
            .ToDictionary(p => p.Port.ID!, p => $"{p.Owner}.{p.Port.Name}", StringComparer.OrdinalIgnoreCase);

        var wiring = new List<string>();
        foreach (var link in AllLinks(doc))
        {
            var a = SideId(link.RefPartnerSideA);
            var b = SideId(link.RefPartnerSideB);
            var hasA = a != null && ports.ContainsKey(a);
            var hasB = b != null && ports.ContainsKey(b);

            // One side ours, the other not: that is a coupling into the plant.
            if (hasA == hasB) continue;

            var mine = hasA ? ports[a!] : ports[b!];
            var theirs = hasA ? b : a;
            wiring.Add($"{link.Name}: {mine} <-> {theirs}");
        }

        wiring.Sort(StringComparer.Ordinal);
        return wiring;
    }

    /// <summary>Every InternalLink in the document, wherever it is parked.</summary>
    private static IEnumerable<InternalLinkType> AllLinks(CAEXDocument doc)
    {
        IEnumerable<InternalElementType> Descend(IEnumerable<InternalElementType> elements)
        {
            foreach (var element in elements)
            {
                yield return element;
                foreach (var nested in Descend(element.InternalElement)) yield return nested;
            }
        }

        foreach (var ih in doc.CAEXFile.InstanceHierarchy)
        foreach (var element in Descend(ih.InternalElement))
        foreach (var link in element.InternalLink)
            yield return link;
    }

    /// <summary>Line endings only: git may check the file out either way.</summary>
    private static string Normalise(string text) =>
        text.ReplaceLineEndings(LineFeed).TrimEnd();

    private const string LineFeed = "\n";

    /// <summary>
    /// The document without its writing timestamp, which differs by definition
    /// between the shipped file and a fresh run, and without the schema
    /// location: Aml.Engine adds that one, together with a copy of the XSD next
    /// to the file, depending on state that is not the document's, so two runs
    /// of the same code can differ in it. The content is what this test guards.
    /// </summary>
    private static string WithoutWritingTime(string xml)
    {
        var withoutTime = System.Text.RegularExpressions.Regex.Replace(
            xml, "LastWritingDateTime=\"[^\"]*\"", "LastWritingDateTime=\"\"");
        return System.Text.RegularExpressions.Regex.Replace(
            withoutTime, " xsi:schemaLocation=\"[^\"]*\"", "");
    }

    private static string? SideId(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var colon = value.IndexOf(':');
        return colon < 0 ? value : value[..colon];
    }
}
