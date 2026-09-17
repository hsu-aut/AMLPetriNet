using Aml.Engine.CAEX;
using PtMapper.Conversion;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The MPS500 bridging example, as it actually is on disk. It is the document
/// this plugin exists for, it was written by another tool, and it stores braced
/// CAEX GUIDs in Identification/id. Everything here failed before that case was
/// handled.
///
/// The file is part of this repository, in examples/paper, so a missing file
/// fails these tests rather than skipping them.
/// </summary>
public class PaperExampleTests
{
    private static readonly string PaperExample = TestFiles.Paper("BridgingExample_MPS500.aml");

    private static CAEXDocument Load() => CAEXDocument.LoadFromFile(PaperExample);

    [Fact]
    public void ThePaperExampleIsReadable()
    {
        var nets = CaexToPtNet.ReadAll(Load());

        var net = Assert.Single(nets);
        Assert.Equal("QualityCheckControl", net.Name);
        Assert.Equal(6, net.Places.Count);
        Assert.Equal(5, net.Transitions.Count);
        Assert.Equal(12, net.Arcs.Count);
    }

    [Fact]
    public void EveryArcOfThePaperExampleResolves()
    {
        var net = CaexToPtNet.ReadAll(Load()).Single();

        Assert.All(net.Arcs, arc =>
        {
            Assert.NotNull(net.FindNode(arc.SourceId));
            Assert.NotNull(net.FindNode(arc.TargetId));
        });
    }

    [Fact]
    public void ThePaperExampleCanBeShownByTheModeler()
    {
        // The concrete failure that started this: every id had to be a valid XML
        // name before the canvas would accept the net.
        var net = CaexToPtNet.ReadAll(Load()).Single();

        Assert.True(PtIdText.IsXmlName(net.Id));
        Assert.All(net.Nodes, n => Assert.True(PtIdText.IsXmlName(n.Id), $"'{n.Id}'"));
        Assert.All(net.Arcs, a => Assert.True(PtIdText.IsXmlName(a.Id), $"'{a.Id}'"));
        Assert.DoesNotContain(PtValidator.Validate(net), f => f.Rule == "PT11");
    }

    [Fact]
    public void SyncingThePaperExampleKeepsItsIdentifiers()
    {
        var doc = Load();
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);
        var net = CaexToPtNet.Read(ih).Single();

        var idsBefore = StoredIds(ih);
        var caexBefore = CaexIds(ih);

        // What the user does: change something and write it back.
        net.Places[0].Name = "Renamed on the canvas";
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(idsBefore, StoredIds(ih));
        Assert.Equal(caexBefore, CaexIds(ih));
    }

    [Fact]
    public void SyncingThePaperExampleTwiceIsStable()
    {
        var doc = Load();
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());
        var once = ih.Node.ToString();

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        Assert.Equal(once, ih.Node.ToString());
    }

    [Fact]
    public void TheOtherHierarchiesAreNotTouched()
    {
        // The example holds an FPD process view and a plant hierarchy next to
        // the Petri net. A sync must not reach into them.
        var doc = Load();
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);

        // Not ReferenceEquals: Aml.Engine returns a new wrapper on every access,
        // so every hierarchy would count as "other".
        var others = doc.CAEXFile.InstanceHierarchy
            .Where(h => h.Name != ih.Name)
            .ToDictionary(h => h.Name, h => h.Node.ToString());

        Assert.NotEmpty(others);
        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        foreach (var (name, before) in others)
        {
            var after = doc.CAEXFile.InstanceHierarchy.First(h => h.Name == name).Node.ToString();
            Assert.Equal(before, after);
        }
    }

    private static List<string?> StoredIds(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement)
            .Select(e => e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    private static List<string?> CaexIds(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement.Select(e => (string?)e.ID).Prepend(net.ID))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
}
