using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

public class ValidatorTests
{
    private static PtNet Valid()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p1", InitialMarking = 1 });
        net.Places.Add(new PtPlace { Id = "p2" });
        net.Transitions.Add(new PtTransition { Id = "t1" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t1", TargetId = "p2" });
        return net;
    }

    private static IReadOnlyList<PtFinding> Rules(PtNet net, string rule) =>
        PtValidator.Validate(net).Where(f => f.Rule == rule).ToList();

    [Fact]
    public void AWellFormedNetHasNoFindings()
    {
        Assert.Empty(PtValidator.Validate(Valid()));
    }

    [Fact]
    public void DuplicateIdsAreAnError()
    {
        var net = Valid();
        net.Places.Add(new PtPlace { Id = "p1" });

        var finding = Assert.Single(Rules(net, "PT01"));
        Assert.Equal(PtSeverity.Error, finding.Severity);
    }

    [Fact]
    public void AnArcBetweenTwoPlacesIsAnError()
    {
        var net = Valid();
        net.Arcs.Add(new PtArc { Id = "a3", SourceId = "p1", TargetId = "p2" });

        var finding = Assert.Single(Rules(net, "PT03"));
        Assert.Equal(PtSeverity.Error, finding.Severity);
        Assert.Equal("a3", finding.ElementId);
        Assert.Contains("bipartite", finding.Message);
    }

    [Fact]
    public void AnArcBetweenTwoTransitionsIsAnError()
    {
        var net = Valid();
        net.Transitions.Add(new PtTransition { Id = "t2" });
        net.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "t2" });

        Assert.Single(Rules(net, "PT03"));
    }

    [Fact]
    public void AnArcPointingNowhereIsAnError()
    {
        var net = Valid();
        net.Arcs.Add(new PtArc { Id = "a3", SourceId = "ghost", TargetId = "t1" });

        var finding = Assert.Single(Rules(net, "PT04"));
        Assert.Contains("ghost", finding.Message);
    }

    [Fact]
    public void ParallelArcsAreReportedOnceAsAWarning()
    {
        var net = Valid();
        net.Arcs.Add(new PtArc { Id = "a1b", SourceId = "p1", TargetId = "t1" });

        var finding = Assert.Single(Rules(net, "PT05"));
        Assert.Equal(PtSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void AnArcInTheOtherDirectionIsNotParallel()
    {
        // p1 -> t1 and t1 -> p1 together are a read-arc pattern, entirely normal.
        var net = Valid();
        net.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "p1" });

        Assert.Empty(Rules(net, "PT05"));
    }

    [Fact]
    public void AnArcWeightBelowOneIsAnError()
    {
        var net = Valid();
        net.Arcs[0].Weight = 0;

        Assert.Single(Rules(net, "PT07"));
    }

    [Fact]
    public void ANegativeMarkingIsAnError()
    {
        var net = Valid();
        net.Places[0].InitialMarking = -1;

        Assert.Single(Rules(net, "PT06"));
    }

    [Fact]
    public void AnUnconnectedNodeIsAWarning()
    {
        var net = Valid();
        net.Places.Add(new PtPlace { Id = "lonely" });

        var finding = Assert.Single(Rules(net, "PT08"));
        Assert.Equal(PtSeverity.Warning, finding.Severity);
        Assert.Equal("lonely", finding.ElementId);
    }

    [Fact]
    public void ATransitionWithoutInputOrOutputIsAWarning()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p1" });
        net.Transitions.Add(new PtTransition { Id = "source" });
        net.Transitions.Add(new PtTransition { Id = "sink" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "source", TargetId = "p1" });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "p1", TargetId = "sink" });

        Assert.Equal("source", Assert.Single(Rules(net, "PT09")).ElementId);
        Assert.Equal("sink", Assert.Single(Rules(net, "PT10")).ElementId);
    }

    [Fact]
    public void AnUnconnectedNodeIsNotAlsoReportedAsSourceAndSink()
    {
        var net = new PtNet { Id = "n" };
        net.Transitions.Add(new PtTransition { Id = "t1" });

        var findings = PtValidator.Validate(net);

        Assert.Single(findings);
        Assert.Equal("PT08", findings[0].Rule);
    }

    [Fact]
    public void FindingsCarryTheIdTheModelerUses()
    {
        // The plugin turns a finding into a selectElement message, so the id has
        // to be the PNML id and not an AML one.
        var net = Valid();
        net.Arcs.Add(new PtArc { Id = "bad", SourceId = "p1", TargetId = "p2" });

        var finding = PtValidator.Validate(net).Single(f => f.Severity == PtSeverity.Error);
        Assert.Equal("bad", finding.ElementId);
        Assert.Contains(finding.ElementId!, Pnml.Write(net));
    }

    [Fact]
    public void TheSampleNetIsClean()
    {
        // The example shipped with the repository should not itself trip a rule.
        var pnml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "quality-gate.pnml"));

        Assert.DoesNotContain(PtValidator.Validate(Pnml.Read(pnml)), f => f.Severity == PtSeverity.Error);
    }
}
