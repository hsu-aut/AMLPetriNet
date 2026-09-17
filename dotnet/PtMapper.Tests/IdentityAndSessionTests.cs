using System.Xml.Linq;
using Aml.Engine.CAEX;
using Aml.Engine.CAEX.Extensions;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// How ids are formed and compared, what an update may leave half done, what a
/// hand-made element is called, and what comparing two saved documents has to
/// notice.
/// </summary>
public class IdentityAndSessionTests
{
    private static PtNet SmallNet()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });
        net.Places.Add(new PtPlace { Id = "p2", Name = "P2", Bounds = new PtBounds(200, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a1", Name = "Arc_P1_T1", SourceId = "p1", TargetId = "t1" });
        net.Arcs.Add(new PtArc { Id = "a2", Name = "Arc_T1_P2", SourceId = "t1", TargetId = "p2" });
        return net;
    }

    private static string XmlOf(CAEXDocument doc) => doc.CAEXFile.Node.ToString();

    private static CAEXDocument Copy(CAEXDocument doc)
    {
        using var stream = doc.SaveToStream(prettyPrint: true);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return CAEXDocument.LoadFromString(reader.ReadToEnd());
    }

    private static InternalElementType NetElement(InstanceHierarchyType ih) => ih.InternalElement.Single();

    private static InternalElementType ElementWithId(InstanceHierarchyType ih, string pnmlId) =>
        NetElement(ih).InternalElement.Single(e =>
            e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value == pnmlId);

    // ── ids the modeler accepts ─────────────────────────────────────────────

    [Theory]
    [InlineData("Prüfstation", "Pruefstation")]
    [InlineData("Größe", "Groesse")]
    [InlineData("ÄÖÜ", "AeOeUe")]
    [InlineData("Café", "Cafe")]
    [InlineData("{8CC172BC-0A57-4FE2-B37F-7FD540ADDCE8}", "_8CC172BC-0A57-4FE2-B37F-7FD540ADDCE8")]
    public void AnIdIsSpelledInTheLettersTheModelerAccepts(string raw, string expected)
    {
        Assert.False(PtIdText.IsXmlName(raw));
        Assert.Equal(expected, PtIdText.ToXmlName(raw));
        Assert.True(PtIdText.IsXmlName(expected));
    }

    [Fact]
    public void APlaceMadeByHandShowsItsNameAndOpensUnderAnAsciiId()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // What the AML Editor does when a user drags PT_Place into the net: an
        // instance of the class, with the empty Identification v0.3 brings.
        var placeClass = doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]!
            .SystemUnitClass[PtNames.Place]!;
        var byHand = (InternalElementType)((SystemUnitFamilyType)placeClass).CreateClassInstance("Prüfstation");
        NetElement(ih).Insert(byHand, false);
        Assert.NotNull(byHand.Attribute[PtNames.Attributes.Identification]);

        var place = CaexToPtNet.Read(ih).Single().Places.Single(p => p.Name == "Prüfstation");
        Assert.Equal("Pruefstation", place.Id);
    }

    // ── one id per element ──────────────────────────────────────────────────

    [Fact]
    public void APnmlFileThatUsesAnIdTwiceComesBackUnchangedFromAml()
    {
        // The shape of jbpt's model1.pnml: two arcs with one id.
        const string pnml = """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">
                <page id="pg">
                  <place id="p1"/><transition id="t1"/><place id="p2"/><place id="p3"/>
                  <arc id="x" source="t1" target="p2"/>
                  <arc id="x" source="t1" target="p3"/>
                  <arc id="a" source="p1" target="t1"/>
                </page>
              </net>
            </pnml>
            """;

        var nets = Pnml.ReadAll(pnml);
        var doc = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, nets, "Net");

        Assert.Contains(nets.Single().Warnings, w => w.Contains("'x'", StringComparison.Ordinal));
        Assert.Equal(new[] { "a", "x", "x_2" }, nets.Single().Arcs.Select(a => a.Id).Order());

        var report = PtRoundTripCheck.Run(doc, "Net");
        Assert.True(report.Passed, report.ToString());
    }

    [Fact]
    public void ACopiedElementGetsAnIdOfItsOwnAndTheUpdateSaysSo()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // A copy in the AML Editor: new CAEX ids, the same Identification/id.
        var copy = new XElement(ElementWithId(ih, "p2").Node);
        foreach (var id in copy.DescendantsAndSelf().Attributes("ID")) id.Value = Guid.NewGuid().ToString("B");
        copy.SetAttributeValue("Name", "P2 copy");
        NetElement(ih).Node.Add(copy);

        var shown = CaexToPtNet.Read(ih).Single();
        Assert.Equal(new[] { "p1", "p2", "p2_2" }, shown.Places.Select(p => p.Id).Order());

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, shown);
        Assert.Contains(summary.Notes, n => n.Contains("'p2_2'", StringComparison.Ordinal));

        var ids = NetElement(ih).InternalElement
            .Select(e => e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value)
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // Repaired once, quiet afterwards.
        var again = PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());
        Assert.DoesNotContain(again.Notes, n => n.StartsWith("Gave", StringComparison.Ordinal));
    }

    [Fact]
    public void ANodeWithADuplicateIdDoesNotTakeTheArcsOfTheFirst()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "{p}" });
        net.Places.Add(new PtPlace { Id = "{p}" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "{p}", TargetId = "t" });

        PtIdText.NormaliseIds(net);

        Assert.Equal(new[] { "p", "p_2" }, net.Places.Select(p => p.Id));
        Assert.Equal("p", net.Arcs.Single().SourceId);
    }

    // ── exact comparison, whole partner ids ─────────────────────────────────

    [Fact]
    public void ALinkThatNamesAnInterfaceInOtherLettersIsReportedAndKept()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        var arc = ElementWithId(ih, "a1");
        var source = arc.ExternalInterface.Single(i => (i.RefBaseClassPath ?? "").EndsWith("/" + PtNames.ArcSource, StringComparison.Ordinal));
        var link = NetElement(ih).InternalLink.Single(l => l.RefPartnerSideA == source.ID || l.RefPartnerSideB == source.ID);
        if (link.RefPartnerSideA == source.ID) link.RefPartnerSideA = source.ID!.ToUpperInvariant();
        else link.RefPartnerSideB = source.ID!.ToUpperInvariant();

        var net = CaexToPtNet.Read(ih).Single();
        Assert.DoesNotContain(net.Arcs, a => a.Id == "a1");
        Assert.Contains(net.Warnings, w => w.Contains("source", StringComparison.Ordinal));

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);
        Assert.Equal(0, summary.Removed);
        Assert.Contains(summary.Notes, n => n.StartsWith("Kept", StringComparison.Ordinal));
        Assert.NotNull(ElementWithId(ih, "a1"));
    }

    [Fact]
    public void AnInterfaceIdWithAColonIsReadAsAWhole()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        // CAEX ids are strings. Reading "a:b" as the CAEX 2.15 form
        // "OwnerID:InterfaceName" took "b" and lost the arc.
        var arc = ElementWithId(ih, "a1");
        var source = arc.ExternalInterface.Single(i => (i.RefBaseClassPath ?? "").EndsWith("/" + PtNames.ArcSource, StringComparison.Ordinal));
        var link = NetElement(ih).InternalLink.Single(l => l.RefPartnerSideA == source.ID || l.RefPartnerSideB == source.ID);
        const string colonId = "arc:source";
        if (link.RefPartnerSideA == source.ID) link.RefPartnerSideA = colonId;
        else link.RefPartnerSideB = colonId;
        source.ID = colonId;

        var net = CaexToPtNet.Read(ih).Single();
        Assert.Empty(net.Warnings);
        Assert.Equal("p1", net.Arcs.Single(a => a.Id == "a1").SourceId);
    }

    // ── nothing half written ────────────────────────────────────────────────

    [Fact]
    public void AnUpdateTheDocumentsLibraryCannotServeWritesNothing()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]!.SystemUnitClass[PtNames.Arc]!.Remove();
        var before = XmlOf(doc);

        var changed = SmallNet();
        changed.Places[0].Name = "Renamed";
        changed.Places.Add(new PtPlace { Id = "p3", Name = "P3" });
        changed.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "p3" });

        Assert.Throws<InvalidOperationException>(() => PtNetUpdater.UpdateInPlace(doc, ih, changed));
        Assert.Equal(before, XmlOf(doc));
    }

    [Fact]
    public void AnArcToANodeTheNetDoesNotHaveIsRefusedBeforeWriting()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var before = XmlOf(doc);

        var broken = SmallNet();
        broken.Places[0].Name = "Renamed";
        broken.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "ghost" });

        var error = Assert.Throws<ArgumentException>(() => PtNetUpdater.UpdateInPlace(doc, ih, broken));
        Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, XmlOf(doc));
    }

    [Fact]
    public void ALibraryThatCarriesAClassTwiceStillServesAnUpdate()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var lib = doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]!;
        var twin = new XElement(lib.SystemUnitClass[PtNames.Place]!.Node);
        foreach (var id in twin.DescendantsAndSelf().Attributes("ID")) id.Value = Guid.NewGuid().ToString("B");
        lib.Node.Add(twin);

        var grown = SmallNet();
        grown.Places.Add(new PtPlace { Id = "p3", Name = "P3" });
        grown.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "p3" });

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, grown);
        Assert.Equal(2, summary.Added);
    }

    // ── names ───────────────────────────────────────────────────────────────

    [Fact]
    public void AnArcThatComesBackWithoutANameKeepsTheOneItHad()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");

        var nameless = SmallNet();
        foreach (var arc in nameless.Arcs) arc.Name = null;
        PtNetUpdater.UpdateInPlace(doc, ih, nameless);

        var element = ElementWithId(ih, "a1");
        Assert.Equal("Arc_P1_T1", element.Name);
        Assert.Equal("Arc_P1_T1", CaexToPtNet.Read(ih).Single().Arcs.Single(a => a.Id == "a1").Name);
    }

    // ── arc labels ──────────────────────────────────────────────────────────

    [Fact]
    public void AnArcWithTheDefaultWeightKeepsAPlacedLabel()
    {
        // The Freiburg process editor writes the inscription 1 with an offset.
        // Leaving the weight implicit took the offset with it, and the label
        // position was lost on the way AML -> PNML -> AML.
        var net = SmallNet();
        net.Arcs[0].LabelOffset = new PtPoint(5, -10);

        var again = Pnml.Read(Pnml.Write(net)).Arcs.Single(a => a.Id == "a1");
        Assert.Equal(1, again.Weight);
        Assert.Equal(new PtPoint(5, -10), again.LabelOffset);

        // Without an offset the weight stays implicit.
        Assert.DoesNotContain("inscription", Pnml.Write(SmallNet()), StringComparison.Ordinal);
    }

    [Fact]
    public void AnArcWithBendsAndALabelSurvivesARoundTripInPlace()
    {
        // Label position and bends together: the label position was removed and
        // appended on every update, which moved it behind the waypoints.
        var net = SmallNet();
        net.Arcs[0].LabelOffset = new PtPoint(5, -10);
        net.Arcs[0].Waypoints.Add(new PtPoint(75, 60));

        var doc = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, net, "Net");

        var report = PtRoundTripCheck.Run(doc, "Net");
        Assert.True(report.Passed, report.ToString());
    }

    // ── what a session changed ──────────────────────────────────────────────

    [Fact]
    public void ComparingNoticesAChangeOutsideTheNet()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        var owner = ElementWithId(ih, "p1").Attribute.Append("Owner");
        owner.Value = "Quality";

        var renamed = Copy(doc);
        renamed.CAEXFile.InstanceHierarchy["Net"]!.Name = "Behaviour";
        var renamedReport = PtRoundTripCheck.Compare(doc, renamed);
        Assert.False(renamedReport.Checks.Single(c => c.Aspect == "everything else").Passed);

        var edited = Copy(doc);
        ElementWithId(edited.CAEXFile.InstanceHierarchy["Net"]!, "p1").Attribute["Owner"]!.Value = "Assembly";
        var editedReport = PtRoundTripCheck.Compare(doc, edited);
        var outside = editedReport.Checks.Single(c => c.Aspect == "everything else");
        Assert.False(outside.Passed);
        Assert.Contains("Assembly", outside.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparingASessionThatOnlyMovedANodePasses()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
        ElementWithId(doc.CAEXFile.InstanceHierarchy["Net"]!, "p1").Attribute.Append("Owner").Value = "Quality";

        var after = Copy(doc);
        var ih = after.CAEXFile.InstanceHierarchy["Net"]!;
        var moved = CaexToPtNet.Read(ih).Single();
        moved.Places[0].Bounds = new PtBounds(15, 10, 50, 50);
        PtNetUpdater.UpdateInPlace(after, ih, moved);

        var report = PtRoundTripCheck.Compare(doc, after);
        Assert.True(report.Passed, report.ToString());
        Assert.Contains("differs", report.Checks.Single(c => c.Aspect == "geometry").Detail, StringComparison.Ordinal);
    }

    // ── numbers PNML accepts ────────────────────────────────────────────────

    [Fact]
    public void ANodeSizeOffTheGridIsWrittenAsPnmlAllowsIt()
    {
        // The grammar allows one decimal in <dimension>. 50.25 made the file
        // invalid, although positions may carry any decimal.
        var net = SmallNet();
        net.Places[0].Bounds = new PtBounds(10.33, 20, 50.25, 12345);
        net.Places[0].LabelOffset = new PtPoint(5.551115123125783e-17, -3);

        var pnml = Pnml.Write(net);
        var dimension = XDocument.Parse(pnml).Descendants().First(e => e.Name.LocalName == "dimension");
        Assert.Equal("50.3", (string?)dimension.Attribute("x"));
        Assert.Equal("9999", (string?)dimension.Attribute("y"));
        Assert.DoesNotContain("e-", pnml, StringComparison.Ordinal);

        // The corner stays where it was; only the size moved onto the grid.
        var back = Pnml.Read(pnml).Places.Single(p => p.Id == "p1").Bounds!;
        Assert.Equal(10.33, back.X, 2);
        Assert.Equal(20, back.Y, 2);
        Assert.Equal(50.3, back.Width, 2);
    }
}
