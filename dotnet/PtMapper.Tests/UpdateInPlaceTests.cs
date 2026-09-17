using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The point of updating in place rather than replacing is that a user's own
/// work on the document survives a sync. Most of these tests are about what must
/// NOT change.
/// </summary>
public class UpdateInPlaceTests
{
    private static PtNet Net(int marking = 1)
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", InitialMarking = marking, Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        return net;
    }

    private static (CAEXDocument Doc, InstanceHierarchyType Ih) Seeded()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, Net(), "PetriNets");
        return (doc, ih);
    }

    private static InternalElementType NetElement(InstanceHierarchyType ih) => ih.InternalElement.Single();

    /// <summary>The document as text, for asserting that nothing at all moved.</summary>
    private static string Snapshot(CAEXDocument doc) => doc.CAEXFile.Node.ToString();

    private static InternalElementType Element(InstanceHierarchyType ih, string pnmlId) =>
        NetElement(ih).InternalElement.Single(e =>
            e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value == pnmlId);

    [Fact]
    public void UpdatingWithoutChangesTouchesNothing()
    {
        var (doc, ih) = Seeded();
        var before = Snapshot(doc);

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(before, Snapshot(doc));
    }

    [Fact]
    public void AUsersOwnAttributeSurvives()
    {
        var (doc, ih) = Seeded();
        var place = Element(ih, "p1");
        var mine = place.Attribute.Append("PlantSection");
        mine.AttributeDataType = "xs:string";
        mine.Value = "Assembly";

        PtNetUpdater.UpdateInPlace(doc, ih, Net(marking: 9));

        var after = Element(ih, "p1");
        Assert.Equal("Assembly", after.Attribute["PlantSection"]?.Value);
        Assert.Equal("9", after.Attribute[PtNames.Attributes.InitialMarking]?.Value);
    }

    [Fact]
    public void AUsersOwnInterfaceAndLinkSurvive()
    {
        // The bridging case of the MPS500 example: someone links a place to a station
        // in another hierarchy. That interface is not a PT_NodeArcEnd and the
        // link is not one of ours, so neither may be swept up.
        var (doc, ih) = Seeded();
        var place = Element(ih, "p1");

        var mine = place.ExternalInterface.Append("PlantRef");
        mine.ID = "{11111111-1111-1111-1111-111111111111}";
        mine.RefBaseClassPath = "SomeOtherLib/SomeInterface";

        var transition = Element(ih, "t1");
        var other = transition.ExternalInterface.Append("PlantRef");
        other.ID = "{22222222-2222-2222-2222-222222222222}";
        other.RefBaseClassPath = "SomeOtherLib/SomeInterface";

        var link = NetElement(ih).InternalLink.Append("bridging");
        link.ID = "{33333333-3333-3333-3333-333333333333}";
        link.AInterface = mine;
        link.BInterface = other;

        PtNetUpdater.UpdateInPlace(doc, ih, Net(marking: 4));

        Assert.NotNull(Element(ih, "p1").ExternalInterface.FirstOrDefault(i => i.Name == "PlantRef"));
        Assert.Contains(NetElement(ih).InternalLink, l => l.Name == "bridging");
    }

    [Fact]
    public void AForeignElementInTheHierarchyIsLeftAlone()
    {
        var (doc, ih) = Seeded();
        var foreign = NetElement(ih).InternalElement.Append("SomeMachine");
        foreign.RefBaseSystemUnitPath = "OtherLib/Machine";

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.Equal(0, summary.Removed);
        Assert.Contains(NetElement(ih).InternalElement, e => e.Name == "SomeMachine");
    }

    [Fact]
    public void ElementIdsStayStableAcrossUpdates()
    {
        var (doc, ih) = Seeded();
        var idBefore = Element(ih, "p1").ID;

        PtNetUpdater.UpdateInPlace(doc, ih, Net(marking: 3));

        Assert.Equal(idBefore, Element(ih, "p1").ID);
    }

    [Fact]
    public void AddingANodeAddsOnlyThatNode()
    {
        var (doc, ih) = Seeded();
        var grown = Net();
        grown.Places.Add(new PtPlace { Id = "p2", Name = "P2", Bounds = new PtBounds(200, 0, 50, 50) });
        grown.Arcs.Add(new PtArc { Id = "a2", SourceId = "t1", TargetId = "p2" });

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, grown);

        Assert.Equal(2, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(5, NetElement(ih).InternalElement.Count());
    }

    [Fact]
    public void RemovingAnArcRemovesItsLinksAndEndpoints()
    {
        var (doc, ih) = Seeded();
        var reduced = new PtNet { Id = "n", Name = "Net" };
        reduced.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        reduced.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, reduced);

        Assert.Equal(1, summary.Removed);
        Assert.Empty(NetElement(ih).InternalLink);
        Assert.Empty(Element(ih, "p1").ExternalInterface);
        Assert.Empty(Element(ih, "t1").ExternalInterface);
    }

    [Fact]
    public void NoDanglingLinkIsLeftBehind()
    {
        var (doc, ih) = Seeded();
        PtNetUpdater.UpdateInPlace(doc, ih, new PtNet { Id = "n", Name = "Net" });

        var interfaceIds = NetElement(ih).InternalElement
            .SelectMany(e => e.ExternalInterface.Select(i => i.ID))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var link in NetElement(ih).InternalLink)
        {
            Assert.Contains(link.RefPartnerSideA, interfaceIds);
            Assert.Contains(link.RefPartnerSideB, interfaceIds);
        }
    }

    [Fact]
    public void RenamingAndMovingIsPickedUp()
    {
        var (doc, ih) = Seeded();
        var changed = Net();
        changed.Places[0].Name = "Renamed";
        changed.Places[0].Bounds = new PtBounds(400, 300, 50, 50);

        PtNetUpdater.UpdateInPlace(doc, ih, changed);

        var place = Element(ih, "p1");
        Assert.Equal("Renamed", place.Name);
        Assert.Equal("Renamed", place.Attribute[PtNames.Attributes.Identification]!.Attribute[PtNames.Attributes.Name]!.Value);
        Assert.Equal("400", place.Attribute[PtNames.Attributes.ViewInformation]!
            .Attribute[PtNames.Attributes.Position]!.Attribute[PtNames.Attributes.X]!.Value);
    }

    [Fact]
    public void AShortenedPolylineDoesNotKeepItsTail()
    {
        var (doc, ih) = Seeded();
        var bent = Net();
        bent.Arcs[0].Waypoints.AddRange(new[] { new PtPoint(10, 10), new PtPoint(20, 20), new PtPoint(30, 30) });
        PtNetUpdater.UpdateInPlace(doc, ih, bent);

        var straightened = Net();
        straightened.Arcs[0].Waypoints.Add(new PtPoint(10, 10));
        PtNetUpdater.UpdateInPlace(doc, ih, straightened);

        var waypoints = Element(ih, "a1").Attribute
            .Where(a => a.Name.StartsWith(PtNames.Attributes.WaypointPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Single(waypoints);
    }

    [Fact]
    public void ALabelMovedBackLosesItsOffset()
    {
        var (doc, ih) = Seeded();
        var moved = Net();
        moved.Places[0].LabelOffset = new PtPoint(5, 40);
        PtNetUpdater.UpdateInPlace(doc, ih, moved);
        Assert.NotNull(Element(ih, "p1").Attribute[PtNames.Attributes.LabelOffset]);

        PtNetUpdater.UpdateInPlace(doc, ih, Net());
        Assert.Null(Element(ih, "p1").Attribute[PtNames.Attributes.LabelOffset]);
    }

    [Fact]
    public void ATransitionThatStopsBeingSilentLosesTheFlag()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        var silent = Net();
        silent.Transitions[0].IsSilent = true;
        var ih = PtNetToCaex.AppendInto(doc, silent, "PetriNets");
        Assert.NotNull(Element(ih, "t1").Attribute[PtNames.Attributes.IsSilent]);

        PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.Null(Element(ih, "t1").Attribute[PtNames.Attributes.IsSilent]);
    }

    [Fact]
    public void UpdatingAnEmptyHierarchyCreatesTheNet()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);
        var ih = doc.CAEXFile.InstanceHierarchy.Append("PetriNets");

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.Equal(4, summary.Added);
        Assert.Single(CaexToPtNet.Read(ih));
    }

    [Fact]
    public void AnUpdatedHierarchyReadsBackAsTheSameNet()
    {
        // The end to end property: whatever the update did, the document must
        // still describe exactly the net that went in.
        var (doc, ih) = Seeded();

        var target = new PtNet { Id = "n", Name = "Grown" };
        target.Places.Add(new PtPlace { Id = "p1", Name = "Renamed", InitialMarking = 5, Bounds = new PtBounds(10, 10, 50, 50) });
        target.Places.Add(new PtPlace { Id = "p2", Name = "P2", Bounds = new PtBounds(300, 10, 50, 50) });
        target.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(150, 10, 50, 50) });
        target.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1", Weight = 3 });
        var bent = new PtArc { Id = "a2", SourceId = "t1", TargetId = "p2" };
        bent.Waypoints.Add(new PtPoint(250, 80));
        target.Arcs.Add(bent);

        PtNetUpdater.UpdateInPlace(doc, ih, target);

        Assert.Equal(Pnml.Write(target), Pnml.Write(CaexToPtNet.Read(ih).Single()));
    }

    [Fact]
    public void RepeatedUpdatesConverge()
    {
        var (doc, ih) = Seeded();
        PtNetUpdater.UpdateInPlace(doc, ih, Net(marking: 2));
        var once = Snapshot(doc);

        PtNetUpdater.UpdateInPlace(doc, ih, Net(marking: 2));

        Assert.Equal(once, Snapshot(doc));
    }

    [Fact]
    public void AnArcCarriesNoEmptyBoundsRectangle()
    {
        // PT_Arc inherits ViewInformation from PT_Element, but a polyline is not
        // a rectangle and the library says so in the PT_Arc description. Writing
        // an empty one would be noise in every document.
        var (_, ih) = Seeded();

        Assert.Null(Element(ih, "a1").Attribute[PtNames.Attributes.ViewInformation]);
        Assert.NotNull(Element(ih, "p1").Attribute[PtNames.Attributes.ViewInformation]);
    }

    [Fact]
    public void AttributesOfTheLanguageThatDoNotFitTheTypeAreCleanedUp()
    {
        // A hand edit, or an older mapper, can leave a marking on a transition
        // or a weight on a place. Those names belong to the language, so the
        // update is allowed to drop them; a user's own attribute is not touched.
        var (doc, ih) = Seeded();

        var transition = Element(ih, "t1");
        transition.Attribute.Append(PtNames.Attributes.InitialMarking).Value = "7";
        transition.Attribute.Append("MyOwnField").Value = "keep me";

        PtNetUpdater.UpdateInPlace(doc, ih, Net());

        var after = Element(ih, "t1");
        Assert.Null(after.Attribute[PtNames.Attributes.InitialMarking]);
        Assert.Equal("keep me", after.Attribute["MyOwnField"]?.Value);
    }

    [Fact]
    public void APlaceThatBecameATransitionIsRebuilt()
    {
        var (doc, ih) = Seeded();
        var changed = new PtNet { Id = "n", Name = "Net" };
        // Same id, different kind: only a hand edit can do this, but it must not
        // leave a place carrying transition semantics.
        changed.Transitions.Add(new PtTransition { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        changed.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });

        PtNetUpdater.UpdateInPlace(doc, ih, changed);

        var rebuilt = Element(ih, "p1");
        Assert.EndsWith(PtNames.Transition, rebuilt.RefBaseSystemUnitPath);
        Assert.Null(rebuilt.Attribute[PtNames.Attributes.InitialMarking]);
    }
}
