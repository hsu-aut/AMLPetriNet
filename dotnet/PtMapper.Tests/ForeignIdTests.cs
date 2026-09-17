using Aml.Engine.CAEX;
using Aml.Engine.CAEX.Extensions;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// Documents written by other tools store the CAEX GUID in Identification/id,
/// braces and all. The MPS500 bridging example does exactly that. Those are not
/// valid PNML identifiers and the modeler's parser refuses them, so they are
/// normalised for the diagram; the document keeps its own.
/// </summary>
public class ForeignIdTests
{
    private const string Guid1 = "{7AE6F010-6E3C-43AC-AC23-1123CCA55159}";
    private const string Guid2 = "{02E94E49-A878-4078-833C-10CD245AB789}";
    private const string ArcGuid = "{483B660D-FFF4-4B32-ABBE-0DE5EA30E910}";

    /// <summary>A hierarchy shaped like one another tool wrote.</summary>
    private static (CAEXDocument Doc, InstanceHierarchyType Ih) ForeignDocument()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);
        var ih = doc.CAEXFile.InstanceHierarchy.Append("IH_PT_BehaviorView");

        var sucs = doc.CAEXFile.SystemUnitClassLib[PtNames.SystemUnitClassLib]!;

        InternalElementType Add(InternalElementType? parent, string className, string caexId, string name)
        {
            var suc = sucs.SystemUnitClass[className]!;
            var ie = (InternalElementType)suc.CreateClassInstance(className);
            ie.ID = caexId;
            ie.Name = name;
            if (parent == null) ih.Insert(ie, false);
            else parent.Insert(ie, false);

            // The tool stored the CAEX id in Identification/id as well.
            var identification = ie.Attribute[PtNames.Attributes.Identification]!;
            identification.Attribute[PtNames.Attributes.Id]!.Value = caexId;
            identification.Attribute[PtNames.Attributes.Name]!.Value = name;
            return ie;
        }

        var net = Add(null, PtNames.Net, "{6C43F2EC-6204-4800-AE4F-E612A01C236E}", "QualityCheckControl");
        var idle = Add(net, PtNames.Place, Guid1, "Idle");
        // A transition, not a place: an arc between two places is not a P/T net.
        var checking = Add(net, PtNames.Transition, Guid2, "Start");
        var arc = Add(net, PtNames.Arc, ArcGuid, "Arc_Idle_to_Start");

        // Wire the arc the way the mapper does, so the reader can resolve it.
        var outIf = idle.ExternalInterface.Append("Out");
        outIf.ID = "{11111111-0000-0000-0000-000000000001}";
        outIf.RefBaseClassPath = PtNames.Interface(PtNames.NodeArcEnd);

        var inIf = checking.ExternalInterface.Append("In");
        inIf.ID = "{11111111-0000-0000-0000-000000000002}";
        inIf.RefBaseClassPath = PtNames.Interface(PtNames.NodeArcEnd);

        var source = arc.ExternalInterface.Append("Source");
        source.ID = "{22222222-0000-0000-0000-000000000001}";
        source.RefBaseClassPath = PtNames.Interface(PtNames.ArcSource);

        var target = arc.ExternalInterface.Append("Target");
        target.ID = "{22222222-0000-0000-0000-000000000002}";
        target.RefBaseClassPath = PtNames.Interface(PtNames.ArcTarget);

        var linkA = net.InternalLink.Append("arc_source");
        linkA.AInterface = source;
        linkA.BInterface = outIf;

        var linkB = net.InternalLink.Append("arc_target");
        linkB.AInterface = target;
        linkB.BInterface = inIf;

        return (doc, ih);
    }

    [Fact]
    public void ABracedGuidBecomesAValidNameOnTheDiagram()
    {
        var (_, ih) = ForeignDocument();

        var net = Assert.Single(CaexToPtNet.Read(ih));

        Assert.All(net.Nodes, n => Assert.True(PtIdText.IsXmlName(n.Id), $"'{n.Id}' is not an XML name"));
        Assert.All(net.Arcs, a => Assert.True(PtIdText.IsXmlName(a.Id), $"'{a.Id}' is not an XML name"));
        Assert.True(PtIdText.IsXmlName(net.Id));
    }

    [Fact]
    public void TheNormalisedIdStaysRecognisable()
    {
        var (_, ih) = ForeignDocument();
        var net = CaexToPtNet.Read(ih).Single();

        Assert.Contains(net.Places, p => p.Id == "_7AE6F010-6E3C-43AC-AC23-1123CCA55159");
    }

    [Fact]
    public void ArcEndpointsStillResolve()
    {
        var (_, ih) = ForeignDocument();
        var net = CaexToPtNet.Read(ih).Single();

        var arc = Assert.Single(net.Arcs);
        Assert.NotNull(net.FindNode(arc.SourceId));
        Assert.NotNull(net.FindNode(arc.TargetId));
    }

    [Fact]
    public void SuchANetPassesValidation()
    {
        // The whole point of the change: the document opens instead of being
        // refused for ids it never claimed were PNML ids. A two element net does
        // draw a warning about the transition having no output, which is true.
        var (_, ih) = ForeignDocument();

        var findings = PtValidator.Validate(CaexToPtNet.Read(ih).Single());

        Assert.DoesNotContain(findings, f => f.Severity == PtSeverity.Error);
        Assert.DoesNotContain(findings, f => f.Rule == "PT11");
    }

    [Fact]
    public void AnUpdateLeavesTheDocumentsOwnIdsAlone()
    {
        // The critical promise. Rewriting Identification/id would break whatever
        // else in the document points at it.
        var (doc, ih) = ForeignDocument();
        var net = CaexToPtNet.Read(ih).Single();

        net.Places[0].Name = "Renamed by the user";
        PtNetUpdater.UpdateInPlace(doc, ih, net);

        var storedIds = ih.InternalElement.Single().InternalElement
            .Select(e => e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value)
            .ToList();

        Assert.Contains(Guid1, storedIds);
        Assert.Contains(Guid2, storedIds);
        Assert.Contains(ArcGuid, storedIds);
    }

    [Fact]
    public void AnUpdateDoesNotDuplicateTheElements()
    {
        // If the updater matched on the raw id it would see every element as new
        // and delete the originals.
        var (doc, ih) = ForeignDocument();
        var net = CaexToPtNet.Read(ih).Single();

        var before = ih.InternalElement.Single().InternalElement.Count();
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.Equal(before, ih.InternalElement.Single().InternalElement.Count());
    }

    [Fact]
    public void CaexElementIdsAreUntouchedByAnUpdate()
    {
        var (doc, ih) = ForeignDocument();
        var net = CaexToPtNet.Read(ih).Single();

        PtNetUpdater.UpdateInPlace(doc, ih, net);

        var caexIds = ih.InternalElement.Single().InternalElement.Select(e => e.ID).ToList();
        Assert.Contains(Guid1, caexIds);
        Assert.Contains(ArcGuid, caexIds);
    }

    [Fact]
    public void NormalisingANetFromPnmlRewiresItsArcs()
    {
        // The PNML import path: the reader keeps what the file said, so the
        // plugin normalises before handing it to the canvas.
        var net = new PtNet { Id = "{net}" };
        net.Places.Add(new PtPlace { Id = "{p}" });
        net.Transitions.Add(new PtTransition { Id = "{t}" });
        net.Arcs.Add(new PtArc { Id = "{a}", SourceId = "{p}", TargetId = "{t}" });

        var changed = PtIdText.NormaliseIds(net);

        Assert.Equal(3, changed);
        var arc = net.Arcs.Single();
        Assert.NotNull(net.FindNode(arc.SourceId));
        Assert.NotNull(net.FindNode(arc.TargetId));
        Assert.All(net.Nodes, n => Assert.True(PtIdText.IsXmlName(n.Id)));
    }

    [Fact]
    public void AValidIdIsNeverRewritten()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p1" });
        net.Transitions.Add(new PtTransition { Id = "t1" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });

        Assert.Equal(0, PtIdText.NormaliseIds(net));
        Assert.Equal("p1", net.Places[0].Id);
    }

    [Theory]
    [InlineData("{7AE6F010-6E3C-43AC}", "_7AE6F010-6E3C-43AC")]
    [InlineData("has space", "hasspace")]
    [InlineData("1leading-digit", "_1leading-digit")]
    [InlineData("already_fine", "already_fine")]
    [InlineData("", "id")]
    [InlineData("{}", "id")]
    public void NormalisationIsPredictable(string input, string expected)
    {
        Assert.Equal(expected, PtIdText.ToXmlName(input));
    }

    [Fact]
    public void TwoIdsThatReduceToTheSameNameStayDistinct()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "{p}" });
        net.Places.Add(new PtPlace { Id = "(p)" });

        PtIdText.NormaliseIds(net);

        Assert.Equal(2, net.Places.Select(p => p.Id).Distinct().Count());
    }
}
