using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// Element identity is the precondition for updating a document in place rather
/// than replacing it, and for any reference a user adds by hand surviving a
/// sync. Aml.Engine reassigns ids on insert under some conditions, so "the id I
/// asked for is the id in the document" is worth asserting, not assuming.
/// </summary>
public class IdentityTests
{
    private static PtNet TwoNodeNet()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        return net;
    }

    [Fact]
    public void ElementIdsAreTheOnesTheMapperDerived()
    {
        var doc = PtNetToCaex.Convert(TwoNodeNet());
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();

        Assert.Equal(PtIds.Net("n"), netIE.ID);
        foreach (var (pnmlId, ie) in new[] { ("p1", Child(netIE, "P1")), ("t1", Child(netIE, "T1")), ("a1", Child(netIE, "a1")) })
            Assert.Equal(PtIds.Element(pnmlId), ie.ID);
    }

    [Fact]
    public void InterfaceAndLinkIdsAreTheOnesTheMapperDerived()
    {
        var doc = PtNetToCaex.Convert(TwoNodeNet());
        var netIE = doc.CAEXFile.InstanceHierarchy.Single().InternalElement.Single();

        var arc = Child(netIE, "a1");
        Assert.Equal(PtIds.Interface(arc.ID!, "Source"), arc.ExternalInterface.Single(i => i.Name == "Source").ID);
        Assert.Equal(PtIds.Interface(arc.ID!, "Target"), arc.ExternalInterface.Single(i => i.Name == "Target").ID);

        var place = Child(netIE, "P1");
        Assert.Equal(PtIds.Interface(place.ID!, "Out_a1"), place.ExternalInterface.Single().ID);

        Assert.Contains(netIE.InternalLink, l => l.ID == PtIds.Link(arc.ID!, "source"));
        Assert.Contains(netIE.InternalLink, l => l.ID == PtIds.Link(arc.ID!, "target"));
    }

    [Fact]
    public void DerivedIdsAreWellFormedUuids()
    {
        // .NET reads the first three fields of a byte[] Guid little-endian, so a
        // version nibble stamped at the wrong byte comes out somewhere else
        // entirely. Assert on the rendered form, which is what lands in the file.
        var text = PtIds.Element("p1");

        Assert.StartsWith("{", text);
        var guid = Guid.Parse(text);
        var fields = guid.ToString("D").Split('-');

        Assert.Equal('5', fields[2][0]);                    // version 5
        Assert.Contains(fields[3][0], new[] { '8', '9', 'a', 'b' }); // RFC 4122 variant
    }

    [Fact]
    public void DifferentKindsOfIdDoNotCollide()
    {
        // An element and its own interface derive from the same PNML id; only
        // the discriminator keeps them apart.
        var ids = new[]
        {
            PtIds.Element("x"), PtIds.Net("x"),
            PtIds.Interface("x", "Source"), PtIds.Interface("x", "Target"),
            PtIds.Link("x", "source"), PtIds.Link("x", "target"),
        };

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void ReplacingTheHierarchyTwiceKeepsTheSameIds()
    {
        // What the plugin's "Update InstanceHierarchy" does today: drop the
        // hierarchy, write a fresh one. If ids drifted on the second pass, every
        // update would break references into the net.
        var doc = CAEXDocument.New_CAEXDocument();
        var first = PtNetToCaex.AppendInto(doc, TwoNodeNet(), "PetriNets");
        var idsAfterFirst = Ids(first);

        first.Remove();
        var second = PtNetToCaex.AppendInto(doc, TwoNodeNet(), "PetriNets");

        Assert.Equal(idsAfterFirst, Ids(second));
        Assert.Single(doc.CAEXFile.InstanceHierarchy);
    }

    [Fact]
    public void ANetWithoutLayoutSurvivesTheRoundTrip()
    {
        // Foreign PNML routinely carries no graphics at all. Nothing may throw,
        // and the net must still come back complete; only the layout is absent.
        const string noLayout =
            """
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml"><net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet"><place id="p"><name><text>P</text></name></place><transition id="t"><name><text>T</text></name></transition><arc id="a" source="p" target="t"/></net></pnml>
            """;

        var recovered = CaexToPtNet.ReadAll(PtNetToCaex.Convert(Pnml.Read(noLayout))).Single();

        Assert.Single(recovered.Places);
        Assert.Single(recovered.Transitions);
        var arc = Assert.Single(recovered.Arcs);
        Assert.Equal("p", arc.SourceId);
        Assert.Equal("t", arc.TargetId);
        Assert.Null(recovered.Places[0].Bounds);
    }

    private static InternalElementType Child(InternalElementType parent, string name) =>
        parent.InternalElement.Single(e => e.Name == name);

    private static List<string> Ids(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement.Select(e => e.ID).Prepend(net.ID))
            .OrderBy(id => id)
            .ToList();
}
