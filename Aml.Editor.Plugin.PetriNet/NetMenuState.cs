namespace Aml.Editor.Plugin.PetriNet;

/// <summary>What the Net menu shows for Simulate and Analyse.</summary>
public sealed record NetMenuItems(bool CanSimulate, bool CanAnalyse, string SimulateHeader, bool SimulateChecked);

/// <summary>
/// The wording and the state of the Simulate and Analyse entries, kept apart
/// from the window so that they can be tested. A run never writes to the
/// document, and the texts say so.
/// </summary>
public static class NetMenuState
{
    public static NetMenuItems For(bool documentOpen, bool netBound, bool pageReady, bool simulating)
    {
        var usable = documentOpen && netBound && pageReady;
        return new NetMenuItems(
            CanSimulate: usable,
            CanAnalyse: usable,
            SimulateHeader: simulating ? "Stop Simulation" : "Simulate",
            SimulateChecked: simulating);
    }

    /// <summary>The status line after the page reported the state of a run.</summary>
    public static string SimulationStatus(bool wasRunning, bool active, string? problem)
    {
        if (active) return "Simulation running. The document is not changed by it.";

        var reason = string.IsNullOrWhiteSpace(problem) ? null : problem.Trim();
        if (wasRunning) return reason == null ? "Simulation stopped." : "Simulation stopped: " + reason;
        return reason == null ? "Simulation stopped." : "Cannot simulate: " + reason;
    }

    /// <summary>The status line from the click on Analyse until the page answers. The search runs in a worker and can take a while.</summary>
    public const string AnalysisRunning = "Analysing the net… The canvas stays usable; the list shows how far it is.";

    /// <summary>The status line after an analysis asked for from the menu.</summary>
    public static string AnalysisStatus(int errors, int warnings, int information, string? problem)
    {
        if (!string.IsNullOrWhiteSpace(problem)) return "Cannot analyse: " + problem.Trim();

        var parts = new List<string>();
        if (errors > 0) parts.Add(errors == 1 ? "1 error" : $"{errors} errors");
        if (warnings > 0) parts.Add(warnings == 1 ? "1 warning" : $"{warnings} warnings");
        if (information > 0) parts.Add(information == 1 ? "1 note" : $"{information} notes");

        return parts.Count == 0
            ? "Analysis: nothing found. The list is on the canvas."
            : "Analysis: " + string.Join(", ", parts) + ". The list is on the canvas.";
    }
}
