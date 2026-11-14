namespace PtMapper.Tool.Analysis;

/// <summary>
/// Runs the analysis on a request and returns the answer, both as JSON text. Kept
/// behind an interface so that the process that runs it can be replaced, by an
/// embedded engine for instance, without touching the command or the format.
/// </summary>
public interface IAnalysisRunner
{
    /// <exception cref="AnalysisUnavailableException">the analysis cannot run here (no Node, no bundle)</exception>
    /// <exception cref="AnalysisFailedException">it ran and failed, or ran out of time</exception>
    string Run(string requestJson, int timeoutSeconds);
}

/// <summary>The analysis cannot run on this machine. A problem with the input of the user's setup, exit code 2.</summary>
public sealed class AnalysisUnavailableException(string message) : Exception(message);

/// <summary>The analysis ran and did not finish properly. Exit code 3.</summary>
public sealed class AnalysisFailedException(string message) : Exception(message);
