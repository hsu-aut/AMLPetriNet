using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PtMapper.Conversion;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// <c>ptmap explore</c>: the state space of one net as DOT or CSV, the text the state space view of
/// the modeler saves. The core writes the text; ptmap passes it on unchanged.
/// </summary>
public static class ExploreCommand
{
    private sealed class Answer
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("results")] public List<Result> Results { get; set; } = new();
    }

    private sealed class Result
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("problem")] public string? Problem { get; set; }
        [JsonPropertyName("complete")] public bool Complete { get; set; }
        [JsonPropertyName("stopped")] public string? Stopped { get; set; }
        [JsonPropertyName("markings")] public int Markings { get; set; }
        [JsonPropertyName("edges")] public int Edges { get; set; }
        [JsonPropertyName("deadlocks")] public int Deadlocks { get; set; }
        [JsonPropertyName("unbounded")] public List<string> Unbounded { get; set; } = new();
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, IAnalysisRunner runner)
    {
        var (options, problem) = ExploreOptions.Parse(args);
        if (options == null)
        {
            error.WriteLine(problem);
            error.WriteLine(ExploreOptions.UsageText);
            return ExitCodes.Usage;
        }

        if (options.Output != null && ReportOutput.SamePath(options.Output, options.File))
        {
            error.WriteLine("--output names the input file; the text would replace the net.");
            return ExitCodes.Usage;
        }

        List<NetSource> sources;
        try
        {
            sources = NetSources.Load(options.File, options.Hierarchy, error, out var fault);
            if (fault != null)
            {
                error.WriteLine(fault);
                return ExitCodes.Input;
            }
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Input;
        }

        if (sources.Count == 0)
        {
            error.WriteLine($"{options.File} contains no net.");
            return ExitCodes.Input;
        }

        NetSource source;
        if (options.NetId != null)
        {
            var found = sources.Where(s => s.Net.Id == options.NetId).ToList();
            if (found.Count == 0)
            {
                error.WriteLine($"{options.File} has no net '{options.NetId}'. It has: {string.Join(", ", sources.Select(s => s.Net.Id))}.");
                return ExitCodes.Input;
            }

            source = found[0];
        }
        else
        {
            source = sources[0];
            if (sources.Count > 1)
                error.WriteLine($"note: the file has {sources.Count} nets ({string.Join(", ", sources.Select(s => s.Net.Id))}); using '{source.Net.Id}'. --net picks another.");
        }

        foreach (var warning in source.Net.Warnings) error.WriteLine("note: " + warning);

        var structural = PtValidator.Validate(source.Net).Where(f => f.Severity == PtSeverity.Error).ToList();
        if (structural.Count > 0)
        {
            error.WriteLine($"The net breaks ISO/IEC 15909-1, so its state space was not written:");
            foreach (var finding in structural) error.WriteLine($"  {finding.Rule} {finding.ElementId ?? "-"} {finding.Message}");
            return ExitCodes.Errors;
        }

        Answer answer;
        try
        {
            var request = AnalysisRequest.Build(new[] { source.Net }, options.Markings, options.TimeLimitMilliseconds, "explore", options.Format);
            answer = JsonSerializer.Deserialize<Answer>(runner.Run(request, options.TimeoutSeconds))
                ?? throw new AnalysisFailedException("The search gave an empty answer.");
        }
        catch (AnalysisUnavailableException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Input;
        }
        catch (AnalysisFailedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Failure;
        }
        catch (JsonException ex)
        {
            error.WriteLine("The search gave an answer that is not JSON: " + ex.Message);
            return ExitCodes.Failure;
        }

        // An older bundle answers every request with an analysis and says nothing of its kind.
        if (answer.Kind != "explore")
        {
            error.WriteLine("The analysis bundle is too old for explore. Run 'npm run build' in web/.");
            return ExitCodes.Failure;
        }

        var result = answer.Results.FirstOrDefault(r => r.Key == "0");
        if (result == null || result.Problem != null)
        {
            error.WriteLine("The search gave no result for the net" + (result?.Problem != null ? ": " + result.Problem : "."));
            return ExitCodes.Failure;
        }

        var name = source.Net.Name ?? source.Net.Id;
        var state = result.Stopped != null ? $"stopped at the limit of {(result.Stopped == "markings" ? "markings" : "time")}" : "complete";
        var summary = $"{name} ({source.Net.Id}): {Count(result.Markings, "marking")}, {Count(result.Edges, "edge")}, {Count(result.Deadlocks, "deadlock")}; {state}";

        if (result.Stopped != null && !options.Partial)
        {
            error.WriteLine(summary);
            error.WriteLine($"Nothing was written. Raise --markings, or write what was found with --partial.");
            return ExitCodes.Warnings;
        }

        if (result.Stopped != null) error.WriteLine("note: the search stopped at its limit, so this is a part of the state space.");
        if (result.Unbounded.Count > 0)
            error.WriteLine($"note: {string.Join(", ", result.Unbounded)} grow without bound and are written as omega.");

        if (options.Output == null)
        {
            output.Write(result.Text);
            error.WriteLine(summary);
            return ExitCodes.Ok;
        }

        var failure = ReportOutput.Write(options.Output, result.Text);
        if (failure != null)
        {
            error.WriteLine(failure);
            return ExitCodes.Input;
        }

        output.WriteLine($"{options.Output}: {summary}");
        return ExitCodes.Ok;
    }

    private static string Count(int count, string word) =>
        string.Format(CultureInfo.InvariantCulture, "{0} {1}{2}", count, word, count == 1 ? "" : "s");
}
