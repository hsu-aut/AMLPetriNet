using Xunit;

namespace Aml.Editor.Plugin.PetriNet.Tests;

public class NetMenuStateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WithoutADocumentAndAReadyPageNothingIsOffered(bool document, bool ready)
    {
        var items = NetMenuState.For(document, ready, simulating: false);
        Assert.False(items.CanSimulate);
        Assert.False(items.CanAnalyse);
    }

    [Fact]
    public void WithADocumentAndAReadyPageBothAreOffered()
    {
        var items = NetMenuState.For(true, true, simulating: false);
        Assert.True(items.CanSimulate && items.CanAnalyse);
        Assert.Equal("Simulate", items.SimulateHeader);
        Assert.False(items.SimulateChecked);
    }

    [Fact]
    public void DuringARunTheEntryOffersToStopIt()
    {
        var items = NetMenuState.For(true, true, simulating: true);
        Assert.Equal("Stop Simulation", items.SimulateHeader);
        Assert.True(items.SimulateChecked);
        Assert.True(items.CanAnalyse);
    }

    [Fact]
    public void ARunSaysThatTheDocumentIsLeftAlone() =>
        Assert.Contains("not changed", NetMenuState.SimulationStatus(false, true, null));

    [Fact]
    public void ARunThatEndedAsksNoReason() =>
        Assert.Equal("Simulation stopped.", NetMenuState.SimulationStatus(true, false, null));

    [Fact]
    public void ARunThatEndedByItselfSaysWhy() =>
        Assert.Equal("Simulation stopped: The net was replaced.",
            NetMenuState.SimulationStatus(true, false, "The net was replaced."));

    [Fact]
    public void ARefusedStartSaysWhy() =>
        Assert.Equal("Cannot simulate: A marking must be a whole number.",
            NetMenuState.SimulationStatus(false, false, "A marking must be a whole number."));

    [Fact]
    public void ABlankReasonIsNoReason() =>
        Assert.Equal("Simulation stopped.", NetMenuState.SimulationStatus(true, false, "  "));

    [Fact]
    public void AnAnalysisCountsWhatItFound() =>
        Assert.Equal("Analysis: 2 warnings, 9 notes. The list is on the canvas.",
            NetMenuState.AnalysisStatus(0, 2, 9, null));

    [Fact]
    public void OneIsNotPlural() =>
        Assert.Equal("Analysis: 1 error, 1 warning, 1 note. The list is on the canvas.",
            NetMenuState.AnalysisStatus(1, 1, 1, null));

    [Fact]
    public void AnAnalysisThatFoundNothingSaysSo() =>
        Assert.Equal("Analysis: nothing found. The list is on the canvas.", NetMenuState.AnalysisStatus(0, 0, 0, null));

    [Fact]
    public void ANetThatCannotBeReadSaysWhy() =>
        Assert.Equal("Cannot analyse: The weight of an arc must be at least 1.",
            NetMenuState.AnalysisStatus(0, 0, 0, "The weight of an arc must be at least 1."));
}
