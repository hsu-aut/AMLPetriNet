using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

public class LayoutTests
{
    private static PtNet Chain()
    {
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p1" });
        net.Transitions.Add(new PtTransition { Id = "t1" });
        net.Places.Add(new PtPlace { Id = "p2" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t1", TargetId = "p2" });
        return net;
    }

    [Fact]
    public void NodesWithoutLayoutGetPositions()
    {
        var net = Chain();

        var placed = PtLayout.ArrangeMissing(net);

        Assert.Equal(3, placed);
        Assert.All(net.Nodes, n => Assert.NotNull(n.Bounds));
    }

    [Fact]
    public void NoTwoNodesEndUpOnTopOfEachOther()
    {
        // The whole point: without this everything sat at the origin.
        var net = Chain();
        PtLayout.ArrangeMissing(net);

        var positions = net.Nodes.Select(n => (n.Bounds!.X, n.Bounds.Y)).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void FlowGoesLeftToRight()
    {
        var net = Chain();
        PtLayout.ArrangeMissing(net);

        var p1 = net.Places.Single(p => p.Id == "p1").Bounds!;
        var t1 = net.Transitions.Single().Bounds!;
        var p2 = net.Places.Single(p => p.Id == "p2").Bounds!;

        Assert.True(p1.X < t1.X, "the source place should sit left of the transition");
        Assert.True(t1.X < p2.X, "the target place should sit right of the transition");
    }

    [Fact]
    public void AnExistingLayoutIsNeverTouched()
    {
        var net = Chain();
        var pinned = new PtBounds(999, 888, 50, 50);
        net.Places[0].Bounds = pinned;

        PtLayout.ArrangeMissing(net);

        Assert.Equal(pinned, net.Places[0].Bounds);
    }

    [Fact]
    public void OnlyMissingNodesAreCounted()
    {
        var net = Chain();
        net.Places[0].Bounds = new PtBounds(0, 0, 50, 50);

        Assert.Equal(2, PtLayout.ArrangeMissing(net));
        Assert.Equal(0, PtLayout.ArrangeMissing(net));
    }

    [Fact]
    public void ACycleWithNoEntryPointStillTerminates()
    {
        // Every node has an incoming arc, so the layering has nowhere to start.
        var net = new PtNet { Id = "n" };
        net.Places.Add(new PtPlace { Id = "p" });
        net.Transitions.Add(new PtTransition { Id = "t" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p", TargetId = "t" });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t", TargetId = "p" });

        var placed = PtLayout.ArrangeMissing(net);

        Assert.Equal(2, placed);
        var positions = net.Nodes.Select(n => (n.Bounds!.X, n.Bounds.Y)).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void ASilentTransitionKeepsItsNarrowShape()
    {
        var net = new PtNet { Id = "n" };
        net.Transitions.Add(new PtTransition { Id = "t", IsSilent = true });

        PtLayout.ArrangeMissing(net);

        Assert.Equal(PtLayout.SilentTransitionWidth, net.Transitions[0].Bounds!.Width);
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        var first = Chain();
        var second = Chain();

        PtLayout.ArrangeMissing(first);
        PtLayout.ArrangeMissing(second);

        Assert.Equal(
            first.Nodes.Select(n => (n.Id, n.Bounds)),
            second.Nodes.Select(n => (n.Id, n.Bounds)));
    }

    [Fact]
    public void AnIsolatedNodeIsPlacedToo()
    {
        var net = Chain();
        net.Places.Add(new PtPlace { Id = "lonely" });

        PtLayout.ArrangeMissing(net);

        Assert.NotNull(net.Places.Single(p => p.Id == "lonely").Bounds);
    }

    [Fact]
    public void ALargeNetLaysOutQuickly()
    {
        var net = new PtNet { Id = "n" };
        for (var i = 0; i < 1000; i++)
        {
            net.Places.Add(new PtPlace { Id = $"p{i}" });
            net.Transitions.Add(new PtTransition { Id = $"t{i}" });
            net.Arcs.Add(new PtArc { Id = $"a{i}", SourceId = $"p{i}", TargetId = $"t{i}" });
            if (i > 0) net.Arcs.Add(new PtArc { Id = $"b{i}", SourceId = $"t{i - 1}", TargetId = $"p{i}" });
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        PtLayout.ArrangeMissing(net);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 5000, $"layout took {watch.ElapsedMilliseconds} ms");
    }
}
