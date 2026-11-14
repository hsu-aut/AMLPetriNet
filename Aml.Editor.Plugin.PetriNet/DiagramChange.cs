namespace Aml.Editor.Plugin.PetriNet;

/// <summary>What a change reported by the canvas means for the unsaved copy.</summary>
public enum DiagramChange
{
    /// <summary>The user edited the net; the copy has to follow.</summary>
    Edited,

    /// <summary>The canvas repeated what was just imported; nothing is unsaved.</summary>
    ImportEcho,

    /// <summary>
    /// The edits were undone back to the loaded net. The unsaved copy is stale and
    /// must go, or a later update would write it over the document.
    /// </summary>
    RevertedToLoaded,
}

public static class DiagramChangeRules
{
    /// <summary>
    /// Pure, so that it can be tested without a window. Matching the baseline
    /// means "back to the loaded net" only while the baseline is that net. After
    /// a page reload restored unsaved edits, the baseline is the unsaved diagram
    /// itself, and matching it changes nothing.
    /// </summary>
    public static DiagramChange Classify(string pnml, string? baseline, bool hasPending, bool baselineIsPending)
    {
        if (!string.Equals(pnml, baseline, StringComparison.Ordinal)) return DiagramChange.Edited;
        return hasPending && !baselineIsPending ? DiagramChange.RevertedToLoaded : DiagramChange.ImportEcho;
    }
}
