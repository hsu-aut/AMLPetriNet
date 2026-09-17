using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The MPS500 bridging example links Petri net nodes to plant signals, and the
/// ISO_PT library declares refObj on PT_Net. None of that is written by this
/// mapper: basic P/T nets need no reference framework, and the links there are
/// made by hand in the editor.
///
/// What the mapper does owe those users is that a sync leaves their work alone.
/// </summary>
public class ReferenceSurvivalTests
{
    private static PtNet Net()
    {
        var net = new PtNet { Id = "n", Name = "Behaviour" };
        net.Places.Add(new PtPlace { Id = "idle", Name = "Idle", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "start", Name = "Start", Bounds = new PtBounds(150, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "idle", TargetId = "start" });
        return net;
    }

    private static InternalElementType Element(InstanceHierarchyType ih, string pnmlId) =>
        ih.InternalElement.Single().InternalElement.Single(e =>
            e.Attribute[PtNames.Attributes.Identification]?.Attribute[PtNames.Attributes.Id]?.Value == pnmlId);

    [Fact]
    public void RefObjOnTheNetSurvivesASync()
    {
        // The library declares it; a user may point it at the process view.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, Net(), "PetriNets");

        var netIE = ih.InternalElement.Single();
        var refObj = netIE.Attribute[PtNames.Attributes.RefObj];
        Assert.NotNull(refObj);
        refObj!.Value = "{11111111-1111-1111-1111-111111111111}";

        PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.Equal("{11111111-1111-1111-1111-111111111111}",
            ih.InternalElement.Single().Attribute[PtNames.Attributes.RefObj]?.Value);
    }

    [Fact]
    public void AReferenceAttributeAddedToANodeSurvivesASync()
    {
        // Not declared on PT_Place, so a user adding one is adding their own
        // attribute; the updater must not sweep it up.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, Net(), "PetriNets");

        var place = Element(ih, "idle");
        var reference = place.Attribute.Append("refBaseObj");
        reference.AttributeDataType = "xs:IDREF";
        reference.RefAttributeType = "ObjectReferences@AutomationML_ObjectReferences_AttributeTypeLib/refBaseObj";
        reference.Value = "{22222222-2222-2222-2222-222222222222}";

        PtNetUpdater.UpdateInPlace(doc, ih, Net());

        var after = Element(ih, "idle").Attribute["refBaseObj"];
        Assert.NotNull(after);
        Assert.Equal("{22222222-2222-2222-2222-222222222222}", after!.Value);
        Assert.Equal("ObjectReferences@AutomationML_ObjectReferences_AttributeTypeLib/refBaseObj",
            after.RefAttributeType);
    }

    [Fact]
    public void ALinkFromAnotherHierarchyIntoTheNetSurvivesASync()
    {
        // The bridging case: a plant hierarchy links its signal to a transition
        // of the behaviour model. The link lives in the other hierarchy.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, Net(), "PetriNets");

        var plant = doc.CAEXFile.InstanceHierarchy.Append("Plant");
        var controller = plant.InternalElement.Append("Controller");
        controller.ID = "{33333333-3333-3333-3333-333333333333}";
        var signal = controller.ExternalInterface.Append("StartSignal");
        signal.ID = "{44444444-4444-4444-4444-444444444444}";

        var transition = Element(ih, "start");
        var hook = transition.ExternalInterface.Append("PlantSignal");
        hook.ID = "{55555555-5555-5555-5555-555555555555}";
        hook.RefBaseClassPath = "SomeLib/SignalPort";

        var link = controller.InternalLink.Append("Start_to_signal");
        link.ID = "{66666666-6666-6666-6666-666666666666}";
        link.RefPartnerSideA = signal.ID;
        link.RefPartnerSideB = hook.ID;

        var summary = PtNetUpdater.UpdateInPlace(doc, ih, Net());

        Assert.NotNull(Element(ih, "start").ExternalInterface.FirstOrDefault(i => i.Name == "PlantSignal"));
        Assert.Contains(controller.InternalLink, l => l.ID == "{66666666-6666-6666-6666-666666666666}");

        // Nothing was touched outside this net, so there is nothing to report.
        Assert.Empty(summary.Notes);
    }

    [Fact]
    public void DeletingANodeDoesNotLeaveALinkFromAnotherHierarchyDangling()
    {
        // The same bridging link, but now the user deletes the transition on the
        // canvas. The element goes, and the link that pointed into it must not
        // be left behind pointing at nothing.
        var doc = CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, Net(), "PetriNets");

        var plant = doc.CAEXFile.InstanceHierarchy.Append("Plant");
        var controller = plant.InternalElement.Append("Controller");
        var signal = controller.ExternalInterface.Append("StartSignal");
        signal.ID = "{44444444-4444-4444-4444-444444444444}";

        var transition = Element(ih, "start");
        var hook = transition.ExternalInterface.Append("PlantSignal");
        hook.ID = "{55555555-5555-5555-5555-555555555555}";
        hook.RefBaseClassPath = "SomeLib/SignalPort";

        var link = controller.InternalLink.Append("Start_to_signal");
        link.ID = "{66666666-6666-6666-6666-666666666666}";
        link.RefPartnerSideA = signal.ID;
        link.RefPartnerSideB = hook.ID;

        // The diagram no longer has the transition.
        var reduced = new PtNet { Id = "n", Name = "Behaviour" };
        reduced.Places.Add(new PtPlace { Id = "idle", Name = "Idle", Bounds = new PtBounds(0, 0, 50, 50) });

        PtNetUpdater.UpdateInPlace(doc, ih, reduced);

        var surviving = doc.CAEXFile.InstanceHierarchy
            .SelectMany(h => h.InternalElement)
            .SelectMany(e => e.InternalElement.Append(e))
            .SelectMany(e => e.ExternalInterface.Select(i => i.ID))
            .ToHashSet(StringComparer.Ordinal);

        var allLinks = doc.CAEXFile.InstanceHierarchy
            .SelectMany(h => h.InternalElement)
            .SelectMany(e => e.InternalElement.Append(e))
            .SelectMany(e => e.InternalLink);

        foreach (var remaining in allLinks)
        {
            Assert.Contains(remaining.RefPartnerSideA, surviving);
            Assert.Contains(remaining.RefPartnerSideB, surviving);
        }
    }
}
