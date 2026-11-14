namespace PtMapper.Tool.Analysis;

/// <summary>The exit codes of <c>ptmap analyze</c>. 0 to 4 mean what they mean for the other commands.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Input = 2;
    public const int Failure = 3;
    public const int Errors = 4;
    public const int Warnings = 5;
}

/// <summary>What the command line of <c>ptmap analyze</c> asked for.</summary>
public sealed record AnalyzeOptions(
    string File,
    string? Hierarchy,
    string Format,
    string FailOn,
    int Markings,
    int? TimeLimitMilliseconds,
    int TimeoutSeconds,
    bool All,
    bool RequireComplete)
{
    public const string UsageText = """
        ptmap analyze <in.pnml|in.aml> [hierarchy] [--format text|json] [--fail-on error|warning|never]
                      [--markings N] [--time-limit ms] [--timeout s] [--all] [--require-complete]
        """;

    /// <summary>Reads the arguments after "analyze". An error is returned as text, never thrown.</summary>
    public static (AnalyzeOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        var format = "text";
        var failOn = "warning";
        var markings = 20000;
        int? timeLimit = null;
        var timeout = 120;
        var all = false;
        var requireComplete = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            string? Value() => i + 1 < args.Count ? args[++i] : null;

            switch (arg)
            {
                case "--all": all = true; break;
                case "--require-complete": requireComplete = true; break;
                case "--format":
                    format = Value() ?? "";
                    if (format is not ("text" or "json")) return (null, $"--format takes text or json, not '{format}'.");
                    break;
                case "--fail-on":
                    failOn = Value() ?? "";
                    if (failOn is not ("error" or "warning" or "never")) return (null, $"--fail-on takes error, warning or never, not '{failOn}'.");
                    break;
                case "--markings":
                    if (!int.TryParse(Value(), out markings) || markings < 1) return (null, "--markings takes a whole number of at least 1.");
                    break;
                case "--time-limit":
                    if (!int.TryParse(Value(), out var ms) || ms < 1) return (null, "--time-limit takes a whole number of milliseconds, at least 1.");
                    timeLimit = ms;
                    break;
                case "--timeout":
                    if (!int.TryParse(Value(), out timeout) || timeout < 1) return (null, "--timeout takes a whole number of seconds, at least 1.");
                    break;
                default:
                    return (null, $"Unknown option '{arg}'.");
            }
        }

        if (positional.Count is < 1 or > 2) return (null, "analyze takes a file and, for an AML file, optionally the name of a hierarchy.");

        return (new AnalyzeOptions(positional[0], positional.Count > 1 ? positional[1] : null,
            format, failOn, markings, timeLimit, timeout, all, requireComplete), null);
    }
}
