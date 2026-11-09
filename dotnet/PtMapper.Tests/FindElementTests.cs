using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The way back from the diagram into the document.
///
/// The canvas knows an element by the id the reader derived for it, which is not
/// always the id the document carries: a GUID is normalised into an XML name, and
/// two elements that reduce to the same name are numbered apart. A click on the
/// canvas that is to select the element in the editor tree therefore has to walk
/// the same path the reader walked, and these tests pin that it does, including
/// for the cases where the two ids differ.
/// </summary>
public class FindElementTests
{
    private static PtNet ThreeNodeNet()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(100, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        return net;
    }

    private static InternalElementType NetElement(CAEXDocument doc) =>
        CaexToPtNet.NetElements(doc.CAEXFile.InstanceHierarchy.Single()).Single();

    [Fact]
    public void EveryElementOfTheDiagramIsFoundInTheDocument()
    {
        var doc = PtNetToCaex.Convert(ThreeNodeNet());
        var netIE = NetElement(doc);
        var net = CaexToPtNet.Read(doc.CAEXFile.InstanceHierarchy.Single()).Single();

        var ids = net.Places.Select(p => p.Id)
            .Concat(net.Transitions.Select(t => t.Id))
            .Concat(net.Arcs.Select(a => a.Id))
            .ToList();

        var found = ids.Select(id => CaexToPtNet.FindElement(netIE, id)).ToList();

        Assert.Equal(3, ids.Count);
        Assert.All(found, element => Assert.NotNull(element));
        // Three ids, three different elements: a lookup that always returned the
        // first child would pass the check above.
        Assert.Equal(3, found.Select(element => element!.ID).Distinct().Count());
    }

    [Fact]
    public void AnIdTheDocumentDoesNotHaveIsNotGuessedAt()
    {
        var netIE = NetElement(PtNetToCaex.Convert(ThreeNodeNet()));

        Assert.Null(CaexToPtNet.FindElement(netIE, "nothing_like_this"));
        Assert.Null(CaexToPtNet.FindElement(netIE, ""));
        Assert.Null(CaexToPtNet.FindElement(netIE, null));
    }

    [Fact]
    public void TheCopyOfAnElementIsToldApartFromTheOriginal()
    {
        // What a user does in the editor: copy a place. Both carry the same
        // identification, so the reader numbers the second one; the lookup has
        // to pair the numbered id with the second element, not the first.
        var doc = PtNetToCaex.Convert(ThreeNodeNet());
        var netIE = NetElement(doc);

        var original = netIE.InternalElement.First(e => e.Name == "P1");
        var copy = (InternalElementType)original.Copy();
        netIE.Insert(copy, false);
        var second = netIE.InternalElement.Last();

        var net = CaexToPtNet.Read(doc.CAEXFile.InstanceHierarchy.Single()).Single();
        var ids = net.Places.Select(p => p.Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.NotEqual(ids[0], ids[1]);

        // Compared by CAEX id, not by reference: Aml.Engine hands out a new
        // wrapper object for the same element on every access, so two reads of
        // one element are never the same instance.
        Assert.NotEqual(original.ID, second.ID);
        Assert.Equal(original.ID, CaexToPtNet.FindElement(netIE, ids[0])?.ID);
        Assert.Equal(second.ID, CaexToPtNet.FindElement(netIE, ids[1])?.ID);
        Assert.NotNull(copy);
    }

    [Fact]
    public void AnElementWhoseIdentificationIsAGuidIsFoundUnderItsNormalisedId()
    {
        // A document written by another tool: the identification is a CAEX GUID
        // with braces, which is no XML name, so the diagram shows a normalised
        // form of it.
        var net = new PtNet { Id = "{3F2504E0-4F89-11D3-9A0C-0305E82C3301}", Name = "Net" };
        net.Places.Add(new PtPlace
        {
            Id = "{8A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9}",
            Name = "Store",
            Bounds = new PtBounds(0, 0, 50, 50),
        });

        var doc = PtNetToCaex.Convert(net);
        var netIE = NetElement(doc);
        var read = CaexToPtNet.Read(doc.CAEXFile.InstanceHierarchy.Single()).Single();
        var id = read.Places.Single().Id;

        Assert.DoesNotContain("{", id);
        Assert.NotNull(CaexToPtNet.FindElement(netIE, id));
        // And the id the document carries does not find it, which is the whole
        // reason this method exists.
        Assert.Null(CaexToPtNet.FindElement(netIE, "{8A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9}"));
    }

    [Fact]
    public void TheNetsOfAHierarchyComeOutInTheOrderTheirNetsDo()
    {
        var second = ThreeNodeNet();
        second.Id = "n2";
        second.Name = "Second";

        var doc = CAEXDocument.New_CAEXDocument();
        var hierarchy = PtNetToCaex.AppendInto(doc, new[] { ThreeNodeNet(), second });

        var elements = CaexToPtNet.NetElements(hierarchy);
        var nets = CaexToPtNet.Read(hierarchy);

        Assert.Equal(nets.Count, elements.Count);
        for (var i = 0; i < nets.Count; i += 1)
        {
            // The element at an index holds the net at the same index, which is
            // what lets the plugin take the net the user picked and ask that
            // element for its children.
            Assert.NotNull(CaexToPtNet.FindElement(elements[i], nets[i].Places[0].Id));
        }
    }
}
