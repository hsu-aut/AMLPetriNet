using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// A document's own conventions, which a sync has to leave alone.
///
/// Two of them cost real documents a large diff before this was fixed: a file
/// that references the ISO_PT libraries instead of carrying them had four class
/// libraries written into it, and elements another tool had given telling names
/// were renamed to their PNML ids.
/// </summary>
public class ForeignConventionTests
{
    private const string Alias = "ISO_PT_DomainLibrary";

    private static PtNet SmallNet()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", Name = "P1", Bounds = new PtBounds(0, 0, 40, 40) });
        net.Transitions.Add(new PtTransition { Id = "t1", Name = "T1", Bounds = new PtBounds(120, 0, 40, 60) });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1", Weight = 1 });
        return net;
    }

    /// <summary>
    /// A document shaped like BridgingExample_MPS500.aml: the libraries live in
    /// their own file and every class path names them through an alias.
    /// </summary>
    private static CAEXDocument ReferencingDocument()
    {
        var doc = PtNetToCaex.Convert(SmallNet());

        foreach (var name in new[]
                 {
                     PtNames.InterfaceClassLib, PtNames.RoleClassLib,
                     PtNames.SystemUnitClassLib, PtNames.AttributeTypeLib,
                 })
        {
            doc.CAEXFile.InterfaceClassLib[name]?.Remove();
            doc.CAEXFile.RoleClassLib[name]?.Remove();
            doc.CAEXFile.SystemUnitClassLib[name]?.Remove();
            doc.CAEXFile.AttributeTypeLib[name]?.Remove();
        }

        foreach (var attribute in PathAttributes(doc))
        {
            if (!attribute.Value.Contains('@') && attribute.Value.StartsWith("ISO_PT_", StringComparison.Ordinal))
                attribute.Value = Alias + "@" + attribute.Value;
        }

        var reference = doc.CAEXFile.ExternalReference.Append();
        reference.Alias = Alias;
        reference.Path = PtNames.LibraryFileName;

        return doc;
    }

    [Fact]
    public void SyncingAReferencingDocumentWritesNoLibrariesIntoIt()
    {
        var doc = ReferencingDocument();
        var ih = doc.CAEXFile.InstanceHierarchy.Single();

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        // The ISO_PT libraries are what this document keeps outside. The two
        // shared ones it carries are its own business and are left as they are.
        Assert.Null(doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]);
        Assert.Null(doc.CAEXFile.RoleClassLib[PtNames.RoleClassLib]);
        Assert.Null(doc.CAEXFile.InterfaceClassLib[PtNames.InterfaceClassLib]);
        Assert.Null(doc.CAEXFile.AttributeTypeLib[PtNames.AttributeTypeLib]);
    }

    [Fact]
    public void AnElementAddedToItFollowsTheSameClassPaths()
    {
        var doc = ReferencingDocument();
        var ih = doc.CAEXFile.InstanceHierarchy.Single();

        var net = CaexToPtNet.Read(ih).Single();
        net.Places.Add(new PtPlace { Id = "p2", Name = "P2", Bounds = new PtBounds(240, 0, 40, 40) });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t1", TargetId = "p2", Weight = 1 });

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);
        Assert.Equal(2, summary.Added);

        // Borrowed for the instantiation, given back afterwards.
        Assert.Empty(doc.CAEXFile.SystemUnitClassLib);

        // Every path still names the library through the document's alias, so
        // the new place is not described differently from the one beside it.
        // The alias itself starts with ISO_PT_, so what marks an unqualified
        // path is the missing '@', not the prefix.
        var unqualified = PathAttributes(doc)
            .Select(a => a.Value)
            .Where(v => !v.Contains('@') && v.StartsWith("ISO_PT_", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(unqualified);

        var place = ih.InternalElement.Single().InternalElement.Single(e => e.Name == "P2");
        Assert.Equal($"{Alias}@{PtNames.SystemUnitClassLib}/{PtNames.Place}", place.RefBaseSystemUnitPath);

        // And it still reads back as a place, which is the point of all of it.
        var again = CaexToPtNet.Read(ih).Single();
        Assert.Contains(again.Places, p => p.Id == "p2");
        Assert.Contains(again.Arcs, a => a.Id == "a2" && a.SourceId == "t1" && a.TargetId == "p2");
    }

    [Fact]
    public void ADocumentCarryingItsLibrariesKeepsThem()
    {
        // The other half of the rule: without an external reference the inlined
        // libraries are the only definition there is, so they have to stay.
        var doc = PtNetToCaex.Convert(SmallNet());
        var ih = doc.CAEXFile.InstanceHierarchy.Single();

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        Assert.NotNull(doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]);
        Assert.NotNull(doc.CAEXFile.RoleClassLib[PtNames.RoleClassLib]);
    }

    [Fact]
    public void ANameAnotherToolChoseSurvivesASyncThatRenamedNothing()
    {
        var doc = PtNetToCaex.Convert(SmallNet());
        var ih = doc.CAEXFile.InstanceHierarchy.Single();
        var netIE = ih.InternalElement.Single();

        // What BridgingExample_FPD_PT_v0.1.aml does: a CAEX name that says more
        // than the PNML name in Identification/name.
        var arc = netIE.InternalElement.Single(
            e => e.RefBaseSystemUnitPath?.EndsWith(PtNames.Arc, StringComparison.Ordinal) == true);
        arc.Name = "Arc_P1_to_T1";
        var place = netIE.InternalElement.Single(e => e.Name == "P1");
        place.Name = "Place_Idle";

        PtNetUpdater.UpdateInPlace(doc, ih, CaexToPtNet.Read(ih).Single());

        Assert.Equal("Arc_P1_to_T1", arc.Name);
        Assert.Equal("Place_Idle", place.Name);
    }

    [Fact]
    public void ARenameInTheCanvasStillReachesTheCaexName()
    {
        var doc = PtNetToCaex.Convert(SmallNet());
        var ih = doc.CAEXFile.InstanceHierarchy.Single();
        var netIE = ih.InternalElement.Single();

        var place = netIE.InternalElement.Single(e => e.Name == "P1");
        place.Name = "Place_Idle";

        var net = CaexToPtNet.Read(ih).Single();
        net.Places.Single(p => p.Id == "p1").Name = "Renamed";

        PtNetUpdater.UpdateInPlace(doc, ih, net);

        // Keeping a name is only right while nobody changed it. A rename the
        // user made in the canvas has to show up in the editor tree.
        Assert.Equal("Renamed", place.Name);
        Assert.Equal("Renamed", place.Attribute[PtNames.Attributes.Identification]
            .Attribute[PtNames.Attributes.Name].Value);
    }

    private static IEnumerable<XAttribute> PathAttributes(CAEXDocument doc) =>
        doc.CAEXFile.Node.DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName is "RefBaseSystemUnitPath" or "RefBaseClassPath"
                or "RefBaseRoleClassPath" or "RefRoleClassPath" or "RefAttributeType");
}
