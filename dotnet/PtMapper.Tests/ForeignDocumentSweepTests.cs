using Aml.Engine.CAEX;
using PtMapper.Conversion;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The Petri net documents in this repository that this mapper did not write.
///
/// Reading them is the easy half. Writing back is where a wrong assumption
/// destroys someone's file, so each one is loaded, synced with what was read,
/// and checked for having come out the way it went in.
/// </summary>
public class ForeignDocumentSweepTests
{
    /// <summary>Documents with real net content, written by other tools.</summary>
    public static TheoryData<string> Documents() => new()
    {
        TestFiles.Paper("BridgingExample_MPS500.aml"),
        TestFiles.Paper("BridgingExample_MPS500_selfcontained.aml"),
    };

    /// <summary>Files that contain PT vocabulary but no net: libraries, and other languages.</summary>
    public static TheoryData<string> NotNets() => new()
    {
        TestFiles.Library("ISO_PT_DomainLibrary_v0.3.aml"),
        TestFiles.Library("OMG_DD_AttributeTypeLib_v0.1.aml"),
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void ItReadsAsANetTheModelerCouldShow(string path)
    {
        var net = Assert.Single(CaexToPtNet.ReadAll(CAEXDocument.LoadFromFile(path)));

        Assert.NotEmpty(net.Places);
        Assert.NotEmpty(net.Arcs);

        // Every id has to be a name the modeler's parser accepts, or the canvas
        // stays blank. This is the case the braced CAEX GUIDs broke.
        Assert.True(PtIdText.IsXmlName(net.Id), $"net id '{net.Id}'");
        Assert.All(net.Nodes, n => Assert.True(PtIdText.IsXmlName(n.Id), $"node id '{n.Id}'"));
        Assert.All(net.Arcs, a => Assert.True(PtIdText.IsXmlName(a.Id), $"arc id '{a.Id}'"));

        // An arc whose endpoints do not resolve would draw into nothing.
        Assert.All(net.Arcs, a =>
        {
            Assert.NotNull(net.FindNode(a.SourceId));
            Assert.NotNull(net.FindNode(a.TargetId));
        });
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void SyncingItChangesNothingItShouldNot(string path)
    {
        var doc = CAEXDocument.LoadFromFile(path);
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);
        var net = CaexToPtNet.Read(ih).Single();

        var identificationBefore = IdentificationsByCaexId(ih);
        var caexBefore = CaexIds(ih);
        var elementsBefore = ElementCount(ih);
        var foreignBefore = OtherHierarchies(doc, ih);

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(elementsBefore, ElementCount(ih));
        Assert.Equal(caexBefore, CaexIds(ih));
        Assert.Equal(foreignBefore, OtherHierarchies(doc, ih));

        // An id that was there must survive untouched. A document written
        // against an earlier library carries no Identification at all, and
        // filling that in with the name the element already had is a repair,
        // not a change of meaning.
        var identificationAfter = IdentificationsByCaexId(ih);
        foreach (var (caexId, before) in identificationBefore)
        {
            if (before.Length == 0) continue;
            Assert.Equal(before, identificationAfter[caexId]);
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void SyncingItTwiceSettles(string path)
    {
        var doc = CAEXDocument.LoadFromFile(path);
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());
        var once = ih.Node.ToString();

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        Assert.Equal(once, ih.Node.ToString());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void WhatComesBackOutIsWhatWentIn(string path)
    {
        // Read, write back, read again: the net has to describe the same thing.
        var doc = CAEXDocument.LoadFromFile(path);
        var ih = doc.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);

        var before = Pnml.Write(CaexToPtNet.Read(ih).Single());
        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());
        var after = Pnml.Write(CaexToPtNet.Read(ih).Single());

        Assert.Equal(before, after);
    }

    [Theory]
    [MemberData(nameof(NotNets))]
    public void AFileWithoutANetIsNotMistakenForOne(string path)
    {
        // Libraries mention every PT class by name, and an FPD document is a
        // different language in the same file format. Neither is a net.
        Assert.Empty(CaexToPtNet.ReadAll(CAEXDocument.LoadFromFile(path)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static int ElementCount(InstanceHierarchyType ih) =>
        ih.InternalElement.Sum(net => net.InternalElement.Count() + 1);

    /// <summary>Stored PNML id per element, keyed by the CAEX id, which never moves.</summary>
    private static Dictionary<string, string> IdentificationsByCaexId(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement.Prepend(net))
            .ToDictionary(
                e => e.ID ?? "",
                e => e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value ?? "");

    private static List<string> CaexIds(InstanceHierarchyType ih) =>
        ih.InternalElement
            .SelectMany(net => net.InternalElement.Select(e => e.ID ?? "").Prepend(net.ID ?? ""))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Not compared by reference: Aml.Engine hands out a new wrapper on every
    /// access, so reference equality never holds.
    /// </summary>
    private static Dictionary<string, string> OtherHierarchies(CAEXDocument doc, InstanceHierarchyType ih) =>
        doc.CAEXFile.InstanceHierarchy
            .Where(h => h.Name != ih.Name)
            .ToDictionary(h => h.Name, h => h.Node.ToString());
}
