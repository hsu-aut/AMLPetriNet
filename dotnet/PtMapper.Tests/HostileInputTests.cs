using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// PNML that arrives from somewhere else. None of this has to be handled
/// gracefully in the sense of producing a good net; all of it has to be handled
/// without corrupting a document, hanging, or throwing something the plugin
/// cannot report.
/// </summary>
public class HostileInputTests
{
    private const string Head =
        """<pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">""";

    private const string Tail = "</net></pnml>";

    private static PtNet Read(string body) => Pnml.Read(Head + body + Tail);

    [Fact]
    public void DuplicateIdsDoNotProduceCollidingAmlElements()
    {
        // Two elements with the same PNML id derive the same CAEX id, and two
        // InternalElements with one id is not a valid document.
        var net = Read("""<place id="p"/><place id="p"/><transition id="t"/>""");
        Assert.Equal(2, net.Places.Count);

        var doc = PtNetToCaex.Convert(net);
        var ids = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single()
            .InternalElement.Select(e => e.ID).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ASelfLoopDoesNotLoseAnEndpoint()
    {
        // Impossible in a bipartite net, so this only ever arrives from a broken
        // file. It must still round-trip rather than silently lose the arc.
        var net = Read("""<place id="p"/><arc id="a" source="p" target="p"/>""");
        var doc = PtNetToCaex.Convert(net);

        var recovered = CaexToPtNet.ReadAll(doc).Single();
        var arc = Assert.Single(recovered.Arcs);
        Assert.Equal("p", arc.SourceId);
        Assert.Equal("p", arc.TargetId);
    }

    [Fact]
    public void NonNumericMarkingsAndWeightsFallBackToDefaults()
    {
        var net = Read("""
            <place id="p"><initialMarking><text>lots</text></initialMarking></place>
            <transition id="t"/>
            <arc id="a" source="p" target="t"><inscription><text>heavy</text></inscription></arc>
            """);

        Assert.Equal(0, net.Places.Single().InitialMarking);
        Assert.Equal(1, net.Arcs.Single().Weight);
    }

    [Fact]
    public void ANegativeMarkingInTheFileIsNotSilentlyKept()
    {
        var net = Read("""<place id="p"><initialMarking><text>-5</text></initialMarking></place>""");

        // Clamped on read, and the validator has nothing to complain about.
        Assert.Equal(0, net.Places.Single().InitialMarking);
        Assert.DoesNotContain(PtValidator.Validate(net), f => f.Rule == "PT06");
    }

    [Fact]
    public void IdsWithMarkupCharactersSurvive()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "a&b<c>d\"e", Name = "x & y" });

        var pnml = Pnml.Write(net);
        var back = Pnml.Read(pnml);

        Assert.Equal("a&b<c>d\"e", back.Places.Single().Id);
        Assert.Equal("x & y", back.Places.Single().Name);
    }

    [Fact]
    public void AnEntityExpansionAttackIsRefused()
    {
        // A billion laughs would otherwise be expanded while parsing an
        // untrusted file the user opened from somewhere.
        const string bomb =
            """
            <?xml version="1.0"?>
            <!DOCTYPE pnml [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
            ]>
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="&c;"/></pnml>
            """;

        Assert.ThrowsAny<Exception>(() => Pnml.Read(bomb));
    }

    [Fact]
    public void AnExternalEntityIsNotResolved()
    {
        const string xxe =
            """
            <?xml version="1.0"?>
            <!DOCTYPE pnml [<!ENTITY secret SYSTEM "file:///c:/windows/win.ini">]>
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="&secret;"/></pnml>
            """;

        Assert.ThrowsAny<Exception>(() => Pnml.Read(xxe));
    }

    [Fact]
    public void MalformedXmlIsReportedAsSuch()
    {
        var error = Assert.Throws<FormatException>(() => Pnml.Read("<pnml><net"));
        Assert.Contains("well-formed", error.Message);
    }

    [Fact]
    public void AnEmptyStringIsRejectedWithAClearMessage()
    {
        Assert.Throws<ArgumentException>(() => Pnml.Read(""));
    }

    [Fact]
    public void AnArcWithoutEndpointsIsDropped()
    {
        var net = Read("""<place id="p"/><arc id="a"/>""");
        Assert.Empty(net.Arcs);
    }

    [Fact]
    public void ExtremeCoordinatesDoNotBreakTheGeometry()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p", Bounds = new PtBounds(-1e6, -1e6, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t", Bounds = new PtBounds(1e6, 1e6, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var polyline = PtGeometry.Polyline(net, net.Arcs[0]);

        Assert.All(polyline, point =>
        {
            Assert.False(double.IsNaN(point.X) || double.IsInfinity(point.X));
            Assert.False(double.IsNaN(point.Y) || double.IsInfinity(point.Y));
        });
    }

    [Fact]
    public void ZeroSizedNodesDoNotDivideByZero()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p", Bounds = new PtBounds(0, 0, 0, 0) });
        net.Transitions.Add(new PtTransition { Id = "t", Bounds = new PtBounds(100, 0, 0, 0) });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var polyline = PtGeometry.Polyline(net, net.Arcs[0]);

        Assert.All(polyline, point =>
        {
            Assert.False(double.IsNaN(point.X) || double.IsInfinity(point.X));
            Assert.False(double.IsNaN(point.Y) || double.IsInfinity(point.Y));
        });
    }

    [Fact]
    public void ADocumentWithoutPtContentReadsAsNoNets()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = doc.CAEXFile.InstanceHierarchy.Append("Plant");
        var machine = ih.InternalElement.Append("Machine");
        machine.RefBaseSystemUnitPath = "OtherLib/Machine";

        Assert.Empty(CaexToPtNet.ReadAll(doc));
        Assert.False(CaexToPtNet.ContainsNet(ih));
    }

    [Fact]
    public void UpdatingADocumentThatAlreadyHasDuplicateIdsDoesNotThrow()
    {
        // The writer no longer produces duplicates, but a hand edit can, and an
        // update must survive meeting one.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });

        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "PetriNets");

        // Duplicate the place, id and all, the way a copy-paste in the editor would.
        var netIE = ih.InternalElement.Single();
        var original = netIE.InternalElement.First();
        var clone = netIE.InternalElement.Append("p copy");
        clone.RefBaseSystemUnitPath = original.RefBaseSystemUnitPath;
        var identification = clone.Attribute.Append(PtNames.Attributes.Identification);
        identification.Attribute.Append(PtNames.Attributes.Id).Value = "p";

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.True(summary.Total > 0);
    }

    [Fact]
    public void UpdatingWithANetThatHasDuplicateIdsDoesNotThrow()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var seed = new PtNet { Id = "n" };
        seed.Places.Add(new PtPlace { Id = "p" });
        var ih = PtNetToCaex.AppendInto(doc, seed, "PetriNets");

        var duplicated = new PtNet { Id = "n" };
        duplicated.Places.Add(new PtPlace { Id = "p", Name = "first" });
        duplicated.Places.Add(new PtPlace { Id = "p", Name = "second" });

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, duplicated);

        Assert.True(summary.Total > 0);
        var ids = ih.InternalElement.Single().InternalElement.Select(e => e.ID).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("p&1")]
    [InlineData("has space")]
    [InlineData("1starts-with-digit")]
    [InlineData("a<b")]
    [InlineData("q\"uote")]
    [InlineData("Ümlaut")]
    public void AnIdThatIsNotAValidXmlNameIsReported(string id)
    {
        // The modeler's parser refuses these outright and the whole import
        // fails, so the rule has to name them first.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = id });

        var finding = Assert.Single(PtValidator.Validate(net), f => f.Rule == "PT11");
        Assert.Equal(PtSeverity.Error, finding.Severity);
        Assert.Equal(id, finding.ElementId);
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("_underscore")]
    [InlineData("with-dash")]
    [InlineData("with.dot")]
    public void AValidIdIsNotReported(string id)
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = id });

        Assert.DoesNotContain(PtValidator.Validate(net), f => f.Rule == "PT11");
    }

    [Fact]
    public void TheHierarchyCarriesAnIdSoARenameCannotLoseIt()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p" });

        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "PetriNets");

        Assert.False(string.IsNullOrEmpty(ih.ID));

        // A rename must not change identity.
        var idBefore = ih.ID;
        ih.Name = "Renamed by the user";
        Assert.Equal(idBefore, doc.CAEXFile.InstanceHierarchy.Single().ID);
    }

    [Fact]
    public void TwoHierarchiesWithTheSameNameGetDifferentIds()
    {
        var net = new PtNet { Id = "n" };
        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();

        var first = PtNetToCaex.AppendInto(doc, net, "PetriNets");
        var second = PtNetToCaex.AppendInto(doc, net, "PetriNets");

        Assert.NotEqual(first.ID, second.ID);
    }

    [Fact]
    public void AnInvalidNetIdIsReportedToo()
    {
        // The net id becomes the model id in the modeler, which is also an
        // xs:ID. Checking only the elements would let the import die anyway.
        var net = new PtNet { Id = "my net", Name = "Spaces" };
        net.Places.Add(new PtPlace { Id = "p" });

        var finding = Assert.Single(PtValidator.Validate(net), f => f.Rule == "PT11");
        Assert.Equal("my net", finding.ElementId);
    }

    [Fact]
    public void ANetWithoutAnIdDoesNotProduceAnEmptyPnmlId()
    {
        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);
        var ih = doc.CAEXFile.InstanceHierarchy.Append("PetriNets");

        // A net element created by hand, with neither a name nor an Identification.
        var netIE = ih.InternalElement.Append("");
        netIE.RefBaseSystemUnitPath = PtNames.SystemUnit(PtNames.Net);

        var recovered = Assert.Single(CaexToPtNet.ReadAll(doc));

        Assert.False(string.IsNullOrWhiteSpace(recovered.Id));
        Assert.DoesNotContain("id=\"\"", Pnml.Write(recovered));
    }

    [Fact]
    public void TwoNetsInOneDocumentDoNotShareElementIds()
    {
        // Clicking "New Net" twice, or importing the same file twice, puts two
        // nets with identical PNML ids in one document. CAEX ids are unique per
        // document, not per hierarchy.
        var net = new PtNet { Id = "net_1", Name = "Petri net" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        PtNetToCaex.AppendInto(doc, net, "PetriNets");
        PtNetToCaex.AppendInto(doc, net, "PetriNets_2");

        var ids = doc.CAEXFile.InstanceHierarchy
            .SelectMany(ih => ih.InternalElement)
            .SelectMany(n => n.InternalElement.Select(e => e.ID).Prepend(n.ID))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryNetInAFileIsRead()
    {
        // PNML allows more than one <net> per document. Taking only the first
        // would drop the rest without a word.
        const string two =
            """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="first" type="http://www.pnml.org/version-2009/grammar/ptnet"><place id="p1"/></net>
              <net id="second" type="http://www.pnml.org/version-2009/grammar/ptnet"><place id="p2"/></net>
            </pnml>
            """;

        var nets = Pnml.ReadAll(two);

        Assert.Equal(2, nets.Count);
        Assert.Equal("first", nets[0].Id);
        Assert.Equal("second", nets[1].Id);
        Assert.Equal("first", Pnml.Read(two).Id);
    }

    [Fact]
    public void SeveralNetsGoIntoOneHierarchy()
    {
        var first = new PtNet { Id = "first", Name = "First" };
        first.Places.Add(new PtPlace { Id = "p1" });
        var second = new PtNet { Id = "second", Name = "Second" };
        second.Places.Add(new PtPlace { Id = "p2" });

        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, new[] { first, second }, "Both");

        var recovered = CaexToPtNet.Read(ih);
        Assert.Equal(2, recovered.Count);
        Assert.Equal(new[] { "first", "second" }, recovered.Select(n => n.Id));
    }

    [Fact]
    public void AnArcLinkedFromTheArcElementIsStillResolved()
    {
        // Both known documents put the arc-to-node links on the net element.
        // CAEX allows them anywhere, and a link placed on the arc itself must
        // not leave that arc looking unconnected.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var doc = PtNetToCaex.Convert(net);
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();
        var arcIE = netIE.InternalElement.Single(e =>
            e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value == "a");

        // Move the two links from the net element down onto the arc.
        foreach (var link in netIE.InternalLink.ToList())
        {
            var moved = arcIE.InternalLink.Append(link.Name);
            moved.ID = link.ID;
            moved.RefPartnerSideA = link.RefPartnerSideA;
            moved.RefPartnerSideB = link.RefPartnerSideB;
            link.Remove();
        }

        var recovered = CaexToPtNet.ReadAll(doc).Single();

        var arc = Assert.Single(recovered.Arcs);
        Assert.Equal("p", arc.SourceId);
        Assert.Equal("t", arc.TargetId);
    }

    [Fact]
    public void AnAttributeThatOnlyHasADefaultValueIsRead()
    {
        // The library declares DefaultValue for InitialMarking and Weight, and a
        // document may rely on it rather than writing the value out.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p", InitialMarking = 0 });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t", Weight = 1 });

        var doc = PtNetToCaex.Convert(net);
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();

        foreach (var (pnmlId, attribute, value) in new[] { ("p", PtNames.Attributes.InitialMarking, "5"), ("a", PtNames.Attributes.Weight, "3") })
        {
            var ie = netIE.InternalElement.Single(e =>
                e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value == pnmlId);
            var attr = ie.Attribute[attribute]!;
            attr.Value = null;
            attr.DefaultValue = value;
        }

        var recovered = CaexToPtNet.ReadAll(doc).Single();

        Assert.Equal(5, recovered.Places.Single().InitialMarking);
        Assert.Equal(3, recovered.Arcs.Single().Weight);
    }

    [Fact]
    public void ANestedNetElementDoesNotDerailTheMatching()
    {
        // Reader and updater have to agree on which elements get a name, or the
        // names after the odd one out shift and the wrong elements are paired.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "PetriNets");
        var netIE = ih.InternalElement.Single();

        // A second net element inside the first: not something the mapper
        // writes, but nothing forbids it either.
        var nested = netIE.InternalElement.Append("InnerNet");
        nested.RefBaseSystemUnitPath = PtNames.SystemUnit(PtNames.Net);

        var reread = CaexToPtNet.Read(ih).Single();
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, reread);

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);

        var after = CaexToPtNet.Read(ih).Single();
        Assert.Equal(new[] { "p" }, after.Places.Select(x => x.Id));
        Assert.Equal(new[] { "t" }, after.Transitions.Select(x => x.Id));
        Assert.Equal(new[] { "a" }, after.Arcs.Select(x => x.Id));
    }
}
