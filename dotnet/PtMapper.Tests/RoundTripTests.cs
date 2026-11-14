using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// PNML in, CAEX out, PNML back. The second PNML must equal the first: this is
/// what protects a user's diagram from losing marking, weight or layout every
/// time the plugin syncs.
/// </summary>
public class RoundTripTests
{
    /// <summary>
    /// The export of the bundled modeler for a three-node net with a weighted
    /// arc and a two-bend arc. Captured from test/browser/roundtrip.mjs in PetriNet.js so both
    /// sides are checked against the same bytes.
    /// </summary>
    private const string SampledPnml =
        """
        <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="model_1" type="http://www.pnml.org/version-2009/grammar/ptnet"><name><text>Model 1</text></name><page id="model_1_page"><place id="Place_a"><name><text>Waiting</text><graphics><offset x="0.5" y="42"/></graphics></name><initialMarking><text>3</text></initialMarking><graphics><position x="150" y="200"/><dimension x="50" y="50"/></graphics></place><place id="Place_b"><name><text>Done</text><graphics><offset x="0.5" y="42"/></graphics></name><graphics><position x="500" y="320"/><dimension x="50" y="50"/></graphics></place><transition id="Transition_a"><name><text>Approve</text></name><graphics><position x="320" y="200"/><dimension x="50" y="50"/></graphics></transition><arc id="Arc_a" source="Place_a" target="Transition_a"><inscription><text>2</text></inscription></arc><arc id="Arc_b" source="Transition_a" target="Place_b"><graphics><position x="420" y="225"/><position x="420" y="345"/></graphics></arc></page></net></pnml>
        """;

    private static PtNet Sample() => Pnml.Read(SampledPnml);

    [Fact]
    public void PnmlReadsEveryElement()
    {
        var net = Sample();

        Assert.Equal("model_1", net.Id);
        Assert.Equal("Model 1", net.Name);
        Assert.Equal(2, net.Places.Count);
        Assert.Single(net.Transitions);
        Assert.Equal(2, net.Arcs.Count);
    }

    [Fact]
    public void PnmlPositionIsTheCentreAndBoundsAreTopLeft()
    {
        var waiting = Sample().Places.Single(p => p.Id == "Place_a");

        Assert.NotNull(waiting.Bounds);
        Assert.Equal(125, waiting.Bounds!.X);
        Assert.Equal(175, waiting.Bounds.Y);
        Assert.Equal(50, waiting.Bounds.Width);
        Assert.Equal(150, waiting.Bounds.Centre.X);
    }

    [Fact]
    public void MarkingAndWeightSurviveReading()
    {
        var net = Sample();

        Assert.Equal(3, net.Places.Single(p => p.Id == "Place_a").InitialMarking);
        Assert.Equal(0, net.Places.Single(p => p.Id == "Place_b").InitialMarking);
        Assert.Equal(2, net.Arcs.Single(a => a.Id == "Arc_a").Weight);
        // An arc without an inscription is weight 1 by definition.
        Assert.Equal(1, net.Arcs.Single(a => a.Id == "Arc_b").Weight);
    }

    [Fact]
    public void BendpointsSurviveReading()
    {
        var bent = Sample().Arcs.Single(a => a.Id == "Arc_b");

        Assert.Equal(2, bent.Waypoints.Count);
        Assert.Equal(new PtPoint(420, 225), bent.Waypoints[0]);
        Assert.Equal(new PtPoint(420, 345), bent.Waypoints[1]);
    }

    [Fact]
    public void PnmlWriteReadIsStable()
    {
        var once = Pnml.Write(Sample());
        var twice = Pnml.Write(Pnml.Read(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void CaexRoundTripPreservesTheNet()
    {
        var original = Sample();
        var doc = PtNetToCaex.Convert(original);

        var recovered = Assert.Single(CaexToPtNet.ReadAll(doc));

        Assert.Equal(
            original.Places.Select(p => (p.Id, p.Name, p.InitialMarking, p.Bounds)).OrderBy(p => p.Id),
            recovered.Places.Select(p => (p.Id, p.Name, p.InitialMarking, p.Bounds)).OrderBy(p => p.Id));

        Assert.Equal(
            original.Transitions.Select(t => (t.Id, t.Name, t.IsSilent, t.Bounds)).OrderBy(t => t.Id),
            recovered.Transitions.Select(t => (t.Id, t.Name, t.IsSilent, t.Bounds)).OrderBy(t => t.Id));

        Assert.Equal(
            original.Arcs.Select(a => (a.Id, a.SourceId, a.TargetId, a.Weight)).OrderBy(a => a.Id),
            recovered.Arcs.Select(a => (a.Id, a.SourceId, a.TargetId, a.Weight)).OrderBy(a => a.Id));
    }

    [Fact]
    public void CaexRoundTripPreservesBendpoints()
    {
        var doc = PtNetToCaex.Convert(Sample());
        var recovered = CaexToPtNet.ReadAll(doc).Single();

        var bent = recovered.Arcs.Single(a => a.Id == "Arc_b");
        Assert.Equal(new[] { new PtPoint(420, 225), new PtPoint(420, 345) }, bent.Waypoints);
    }

    [Fact]
    public void PnmlThroughCaexAndBackIsIdentical()
    {
        var before = Pnml.Write(Sample());
        var after = Pnml.Write(CaexToPtNet.ReadAll(PtNetToCaex.Convert(Pnml.Read(before))).Single());

        Assert.Equal(before, after);
    }

    [Fact]
    public void ArcsBecomeElementsNotLinks()
    {
        // The reified encoding is the point of this library: an arc is an
        // element with an identity, and the two InternalLinks per arc only
        // couple it to its nodes.
        var doc = PtNetToCaex.Convert(Sample());
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();

        Assert.Equal(5, netIE.InternalElement.Count());
        Assert.Equal(4, netIE.InternalLink.Count());
    }

    [Fact]
    public void ArcEndpointsAreDockedToTheShapeBorder()
    {
        var doc = PtNetToCaex.Convert(Sample());
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();

        var arc = netIE.InternalElement.Single(e =>
            e.Attribute["Identification"]?.Attribute["id"]?.Value == "Arc_a");
        var source = arc.ExternalInterface.Single(i => i.Name == "Source");
        var coordinate = source.Attribute["PortCoordinate"];

        // Straight run from the Waiting place (centre 150,200, radius 25)
        // towards the transition: the port sits on the circle, not in it.
        Assert.NotNull(coordinate);
        Assert.Equal("175", coordinate!.Attribute["x"]?.Value);
        Assert.Equal("200", coordinate.Attribute["y"]?.Value);
    }

    [Fact]
    public void SilentTransitionSurvivesTheRoundTrip()
    {
        var net = new PtNet { Id = "n1", Name = "Silent" };
        net.Transitions.Add(new PtTransition
        {
            Id = "t1",
            Name = "tau",
            IsSilent = true,
            Bounds = new PtBounds(0, 0, 20, 50),
        });

        var recovered = CaexToPtNet.ReadAll(PtNetToCaex.Convert(net)).Single();

        Assert.True(recovered.Transitions.Single().IsSilent);
        Assert.Contains("<silent>true</silent>", Pnml.Write(recovered));
    }

    [Fact]
    public void ForeignPnmlWithoutDimensionsGetsDefaultSizes()
    {
        const string foreign =
            """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet"><place id="p"><graphics><position x="100" y="100"/></graphics></place></net></pnml>
            """;

        var place = Pnml.Read(foreign).Places.Single();

        Assert.Equal(50, place.Bounds!.Width);
        Assert.Equal(75, place.Bounds.X);
    }

    [Fact]
    public void PagesAreFlattened()
    {
        const string paged =
            """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet"><page id="p1"><place id="a"/><page id="p2"><transition id="b"/></page></page></net></pnml>
            """;

        var net = Pnml.Read(paged);

        Assert.Single(net.Places);
        Assert.Single(net.Transitions);
    }

    [Fact]
    public void MapperIdsAreDeterministic()
    {
        var first = PtNetToCaex.Convert(Sample());
        var second = PtNetToCaex.Convert(Sample());

        Assert.Equal(
            first.CAEXFile.InstanceHierarchy.Single().InternalElement.Single().InternalElement.Select(e => e.ID),
            second.CAEXFile.InstanceHierarchy.Single().InternalElement.Single().InternalElement.Select(e => e.ID));
    }
}
