namespace PtMapper.Tool.Analysis;

/// <summary>What the command line of <c>ptmap explore</c> asked for.</summary>
public sealed record ExploreOptions(
    string File,
    string? Hierarchy,
    string? NetId,
    string Format,
    string? Output,
    int Markings,
    int? TimeLimitMilliseconds,
    int TimeoutSeconds,
    bool Partial)
{
    public const string UsageText = """
        ptmap explore <in.pnml|in.aml> [hierarchy] [--net id] [--format dot|csv] [--output file]
                      [--markings N] [--time-limit ms] [--timeout s] [--partial]
        """;

    /// <summary>Reads the arguments after "explore". An error is returned as text, never thrown.</summary>
    public static (ExploreOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        var format = "dot";
        string? output = null;
        string? netId = null;
        var markings = 20000;
        int? timeLimit = null;
        var timeout = 120;
        var partial = false;

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
                case "--partial": partial = true; break;
                case "--net":
                    netId = Value();
                    if (string.IsNullOrEmpty(netId) || netId.StartsWith("--", StringComparison.Ordinal)) return (null, "--net takes the id of a net.");
                    break;
                case "--format":
                    format = Value() ?? "";
                    if (format is not ("dot" or "csv")) return (null, $"--format takes dot or csv, not '{format}'.");
                    break;
                case "--output":
                    output = Value();
                    if (string.IsNullOrWhiteSpace(output) || output.StartsWith("--", StringComparison.Ordinal)) return (null, "--output takes the name of a file.");
                    break;
                case "--markings":
                    if (!int.TryParse(Value(), out markings) || markings < 1) return (null, "--markings takes a whole number of at least 1.");
                    break;
                case "--time-limit":
                    if (!int.TryParse(Value(), out var ms) || ms < 1) return (null, "--time-limit takes a whole number of milliseconds, at least 1.");
                    timeLimit = ms;
                    break;
                case "--timeout":
                    if (!int.TryParse(Value(), out timeout) || timeout < 1 || timeout > 86400) return (null, "--timeout takes a whole number of seconds, from 1 to 86400.");
                    break;
                default:
                    return (null, $"Unknown option '{arg}'.");
            }
        }

        if (positional.Count is < 1 or > 2) return (null, "explore takes a file and, for an AML file, optionally the name of a hierarchy.");

        return (new ExploreOptions(positional[0], positional.Count > 1 ? positional[1] : null, netId,
            format, output, markings, timeLimit, timeout, partial), null);
    }
}
