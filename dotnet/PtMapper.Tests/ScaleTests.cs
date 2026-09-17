using System.Diagnostics;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;
using Xunit.Abstractions;

namespace PtMapper.Tests;

/// <summary>
/// A real Petri net is not three nodes. Several code paths pair every arc with
/// every node or every link, so this measures whether that matters at a size
/// somebody might actually draw. The assertions are deliberately loose: the
/// point is to catch an accidental quadratic blowup, not to pin down timings on
/// a particular machine.
/// </summary>
public class ScaleTests
{
    private readonly ITestOutputHelper _output;

    public ScaleTests(ITestOutputHelper output) => _output = output;

    /// <summary>A chain of place-transition pairs, plus a feedback arc every ten steps.</summary>
    private static PtNet Chain(int pairs)
    {
        var net = new PtNet { Id = "big", Name = "Big net" };

        for (var i = 0; i < pairs; i++)
        {
            net.Places.Add(new PtPlace
            {
                Id = $"p{i}",
                Name = $"Place {i}",
                InitialMarking = i % 3,
                Bounds = new PtBounds(i % 40 * 120, i / 40 * 120, 50, 50),
            });
            net.Transitions.Add(new PtTransition
            {
                Id = $"t{i}",
                Name = $"Transition {i}",
                Bounds = new PtBounds(i % 40 * 120 + 60, i / 40 * 120, 50, 50),
            });

            net.Arcs.Add(new PtArc { Id = $"a{i}in", SourceId = $"p{i}", TargetId = $"t{i}" });
            if (i > 0) net.Arcs.Add(new PtArc { Id = $"a{i}out", SourceId = $"t{i - 1}", TargetId = $"p{i}" });
        }

        return net;
    }

    [Theory]
    [InlineData(50)]
    [InlineData(500)]
    public void ARealisticNetGoesThroughTheWholeChainQuickly(int pairs)
    {
        var net = Chain(pairs);
        var elements = net.Places.Count + net.Transitions.Count + net.Arcs.Count;

        var watch = Stopwatch.StartNew();
        var pnml = Pnml.Write(net);
        var parsed = Pnml.Read(pnml);
        var doc = PtNetToCaex.Convert(parsed);
        var back = CaexToPtNet.ReadAll(doc).Single();
        var findings = PtValidator.Validate(back);
        watch.Stop();

        _output.WriteLine($"{elements} elements: {watch.ElapsedMilliseconds} ms, {findings.Count} findings");

        Assert.Equal(net.Places.Count, back.Places.Count);
        Assert.Equal(net.Arcs.Count, back.Arcs.Count);

        // Generous, but a quadratic path over 2000 elements would blow past it.
        Assert.True(watch.ElapsedMilliseconds < 15000,
            $"the chain took {watch.ElapsedMilliseconds} ms for {elements} elements");
    }

    [Fact]
    public void UpdatingALargeNetIsNotQuadraticEnoughToHurt()
    {
        var net = Chain(400);
        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "PetriNets");

        // One marking changed: the smallest possible edit on a large net, which
        // is the common case when someone is working.
        net.Places[0].InitialMarking = 42;

        var watch = Stopwatch.StartNew();
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, net);
        watch.Stop();

        _output.WriteLine($"update of {summary.Total} elements: {watch.ElapsedMilliseconds} ms");

        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Removed);
        Assert.True(watch.ElapsedMilliseconds < 15000,
            $"the update took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ValidationOfALargeNetStaysUsable()
    {
        var net = Chain(500);

        var watch = Stopwatch.StartNew();
        var findings = PtValidator.Validate(net);
        watch.Stop();

        _output.WriteLine($"validation: {watch.ElapsedMilliseconds} ms, {findings.Count} findings");
        Assert.True(watch.ElapsedMilliseconds < 5000, $"validation took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void DeletingALargeNetDoesNotDegenerate()
    {
        // Link cleanup scans the whole document for every interface that goes,
        // so a mass deletion is the case where that could turn quadratic.
        var net = Chain(300);
        var doc = Aml.Engine.CAEX.CAEXDocument.New_CAEXDocument();
        var ih = PtNetToCaex.AppendInto(doc, net, "PetriNets");

        var watch = Stopwatch.StartNew();
        var summary = PtNetUpdater.UpdateInPlace(doc, ih, new PtNet { Id = "big", Name = "Big net" });
        watch.Stop();

        _output.WriteLine($"deleting {summary.Removed} elements: {watch.ElapsedMilliseconds} ms");

        Assert.True(summary.Removed > 1000);
        Assert.True(watch.ElapsedMilliseconds < 20000,
            $"deleting {summary.Removed} elements took {watch.ElapsedMilliseconds} ms");
    }
}
