using System.Text.Encodings.Web;
using System.Text.Json;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// <c>ptmap analyze</c>: the structural rules PT01 to PT11 and the behavioural
/// findings PT12 to PT21 of a file, in one report. The structural rules run here;
/// the behavioural ones come from the analysis core of the modeler, through the
/// runner, so a file gives the same answer here as on the canvas.
/// </summary>
public static class AnalyzeCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Names stay readable: umlauts are not written as escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, IAnalysisRunner runner)
    {
        var (options, problem) = AnalyzeOptions.Parse(args);
        if (options == null)
        {
            error.WriteLine(problem);
            error.WriteLine(AnalyzeOptions.UsageText);
            return ExitCodes.Usage;
        }

        // The report must not replace the net it is about.
        if (options.Output != null && ReportOutput.SamePath(options.Output, options.File))
        {
            error.WriteLine("--output names the input file; the report would replace the net.");
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

        foreach (var source in sources)
        foreach (var warning in source.Net.Warnings)
            error.WriteLine("note: " + warning);

        // Nets with an error of structure are not analysed: the core would refuse
        // them, and the reason is better given in the words of the rule.
        var structural = sources.Select(s => PtValidator.Validate(s.Net)).ToList();
        var analysable = sources.Where((_, i) => structural[i].All(f => f.Severity != PtSeverity.Error)).ToList();

        CoreAnswer answer;
        try
        {
            answer = analysable.Count == 0
                ? new CoreAnswer()
                : Ask(runner, analysable.Select(s => s.Net).ToList(), options);
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

        var reports = new List<NetReport>();
        for (var i = 0; i < sources.Count; i++)
        {
            var index = analysable.IndexOf(sources[i]);
            var core = index >= 0 ? answer.Results.FirstOrDefault(r => r.Key == index.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
            reports.Add(Build(sources[i], structural[i], core, index >= 0));

            // A net that could be analysed and was not (no answer under its key, or
            // a reason from the core) must not pass as a net without findings.
            if (index >= 0 && !reports[i].Analysed)
            {
                error.WriteLine($"The analysis gave no result for the net '{sources[i].Net.Name ?? sources[i].Net.Id}'"
                    + (reports[i].Problem != null ? ": " + reports[i].Problem : "."));
                return ExitCodes.Failure;
            }
        }

        var counts = new Counts(reports.Sum(r => r.Counts.Error), reports.Sum(r => r.Counts.Warning), reports.Sum(r => r.Counts.Information));
        var exit = ExitCode(options, counts, reports);
        var report = new AnalyzeReport(options.File, "petrinetjs " + answer.Core,
            new ReportLimits(options.Markings, options.TimeLimitMilliseconds), reports, counts, exit)
        { Rules = answer.Rules ?? new() };

        var text = Render(report, options).Replace("\r\n", "\n");
        if (options.Output == null)
        {
            output.Write(text);
            return exit;
        }

        // Written only now, when the run has succeeded: a failed run leaves an old file as it was.
        // UTF-8 without a byte order mark, which a redirect in Windows PowerShell 5.1 does not give.
        var failure = ReportOutput.Write(options.Output, text);
        if (failure != null)
        {
            error.WriteLine(failure);
            return ExitCodes.Input;
        }

        output.WriteLine($"{options.Output}: {Plural(counts.Error, "error")}, {Plural(counts.Warning, "warning")}, {counts.Information} information.");
        return exit;
    }

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";

    /// <summary>The report in the format that was asked for, as the text that is written.</summary>
    private static string Render(AnalyzeReport report, AnalyzeOptions options)
    {
        switch (options.Format)
        {
            case "json": return JsonSerializer.Serialize(report, Json) + "\n";
            case "sarif":
                var version = typeof(AnalyzeCommand).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0";
                return FindingsSarif.Write(report, FindingsSarif.UriOf(options.File, Directory.GetCurrentDirectory()), options.All, version.Split('+')[0]);
            case "junit": return FindingsJUnit.Write(report);
            default:
                var writer = new StringWriter { NewLine = "\n" };
                FindingsText.Write(writer, report, options.All);
                return writer.ToString();
        }
    }

    private static CoreAnswer Ask(IAnalysisRunner runner, IReadOnlyList<PtNet> nets, AnalyzeOptions options)
    {
        var request = AnalysisRequest.Build(nets, options.Markings, options.TimeLimitMilliseconds);
        var text = runner.Run(request, options.TimeoutSeconds);

        try
        {
            return JsonSerializer.Deserialize<CoreAnswer>(text) ?? throw new AnalysisFailedException("The analysis gave an empty answer.");
        }
        catch (JsonException ex)
        {
            throw new AnalysisFailedException("The analysis gave an answer that is not JSON: " + ex.Message);
        }
    }

    private static NetReport Build(NetSource source, IReadOnlyList<PtFinding> structural, CoreNetAnswer? core, bool analysable)
    {
        var net = source.Net;
        var findings = new List<ReportedFinding>();

        // The computed findings come first within a severity, as in the panel.
        if (core != null)
        {
            foreach (var f in core.Findings)
            {
                findings.Add(new ReportedFinding(f.Rule, f.Severity, f.Element, f.Message,
                    InternalId(source, f.Element), f.Marking, f.Cover, f.Firings, f.Proof));
            }
        }

        foreach (var f in structural)
        {
            findings.Add(new ReportedFinding(f.Rule, f.Severity == PtSeverity.Error ? "error" : "warning",
                f.ElementId, f.Message, InternalId(source, f.ElementId)));
        }

        var ordered = findings.OrderBy(f => Rank(f.Severity)).ToList();
        var counts = new Counts(
            ordered.Count(f => f.Severity == "error"),
            ordered.Count(f => f.Severity == "warning"),
            ordered.Count(f => f.Severity == "information"));

        string? problem = core?.Problem;
        if (!analysable)
        {
            problem = $"The net breaks ISO/IEC 15909-1 ({structural.Count(f => f.Severity == PtSeverity.Error)} error(s)), so its behaviour was not analysed.";
        }

        var analysed = analysable && core != null && core.Problem == null;
        return new NetReport(net.Id, net.Name, source.Hierarchy, analysed, problem,
            analysed && core!.Complete, analysed ? core!.Stopped : null, analysed ? core!.Markings : 0,
            analysed ? core!.Unbounded : new List<string>(),
            analysed ? core!.Classes : new List<string>(),
            analysed && core!.Workflow is { } flow ? new WorkflowReport(flow.Start, flow.End, flow.Sound) : null,
            counts, ordered);
    }

    private static int Rank(string severity) => severity switch { "error" => 0, "warning" => 1, _ => 2 };

    private static string? InternalId(NetSource source, string? elementId) =>
        source.Element == null || string.IsNullOrEmpty(elementId)
            ? null
            : CaexToPtNet.FindElement(source.Element, elementId)?.ID;

    private static int ExitCode(AnalyzeOptions options, Counts counts, IReadOnlyList<NetReport> reports)
    {
        // "never" leaves only the failures of the tool itself: codes 1 to 3.
        if (options.FailOn == "never") return ExitCodes.Ok;
        if (counts.Error > 0) return ExitCodes.Errors;

        // Asked for by name, so it holds whatever --fail-on says about warnings.
        if (options.RequireComplete && reports.Any(r => r.Analysed && !r.Complete)) return ExitCodes.Warnings;

        return options.FailOn == "warning" && counts.Warning > 0 ? ExitCodes.Warnings : ExitCodes.Ok;
    }
}
