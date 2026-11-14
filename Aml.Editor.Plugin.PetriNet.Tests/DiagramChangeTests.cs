using Xunit;

namespace Aml.Editor.Plugin.PetriNet.Tests;

/// <summary>
/// Which changes of the canvas leave an unsaved copy behind. A wrong answer is
/// silent: the document is overwritten, or edits are lost, at the next update.
/// </summary>
public class DiagramChangeTests
{
    [Fact]
    public void AnEditDiffersFromTheBaseline() =>
        Assert.Equal(DiagramChange.Edited, DiagramChangeRules.Classify("b", "a", false, false));

    [Fact]
    public void AnEditWithoutAnyBaselineIsAnEdit() =>
        Assert.Equal(DiagramChange.Edited, DiagramChangeRules.Classify("a", null, false, false));

    [Fact]
    public void TheEchoOfAnImportChangesNothing() =>
        Assert.Equal(DiagramChange.ImportEcho, DiagramChangeRules.Classify("a", "a", false, false));

    [Fact]
    public void UndoBackToTheLoadedNetDropsTheCopy() =>
        Assert.Equal(DiagramChange.RevertedToLoaded, DiagramChangeRules.Classify("a", "a", true, false));

    [Fact]
    public void AfterARestoreTheBaselineIsTheUnsavedDiagramAndStaysUnsaved() =>
        Assert.Equal(DiagramChange.ImportEcho, DiagramChangeRules.Classify("a", "a", true, true));

    [Fact]
    public void ComparisonIsExact() =>
        Assert.Equal(DiagramChange.Edited, DiagramChangeRules.Classify("a ", "a", true, false));
}
