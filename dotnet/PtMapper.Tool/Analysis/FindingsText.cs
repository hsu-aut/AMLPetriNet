namespace PtMapper.Tool.Analysis;

/// <summary>The report as text for a person: counts per net, errors and warnings listed, notes counted.</summary>
public static class FindingsText
{
    public static void Write(TextWriter output, AnalyzeReport report, bool all)
    {
        foreach (var net in report.Nets)
        {
            var state = !net.Analysed ? "not analysed"
                : net.Complete ? $"complete, {net.Markings} markings"
                : net.Stopped != null ? $"partial (stopped at {net.Stopped}), {net.Markings} markings"
                : $"partial (a place grows without bound), {net.Markings} markings";
            var where = net.Hierarchy != null ? $"{net.Hierarchy}: " : "";

            output.WriteLine($"{where}{net.Name ?? net.Id} ({net.Id}): {Plural(net.Counts.Error, "error")}, "
                + $"{Plural(net.Counts.Warning, "warning")}, {net.Counts.Information} information; {state}");

            if (net.Problem != null) output.WriteLine("  " + net.Problem);

            // What the structure says, in one line: the class, and for a workflow net the verdict.
            var facts = new List<string>();
            if (net.Classes.Count > 0) facts.Add("class: " + Class(net.Classes));
            if (net.Workflow != null)
                facts.Add("workflow net: " + (net.Workflow.Sound switch { true => "sound", false => "not sound", _ => "soundness not decided" }));
            if (facts.Count > 0) output.WriteLine("  " + string.Join("; ", facts));

            foreach (var finding in net.Findings)
            {
                if (!all && finding.Severity == "information") continue;
                output.WriteLine($"  {finding.Severity.ToUpperInvariant(),-12} {finding.Rule,-5} "
                    + $"{(finding.Element ?? "-"),-12} {finding.Message}");
            }

            if (!all && net.Counts.Information > 0)
                output.WriteLine($"  ({net.Counts.Information} information finding(s) not listed; --all lists them)");
        }
    }

    /// <summary>The most specific class, in words; the list is ordered that way.</summary>
    private static string Class(IReadOnlyList<string> classes) =>
        classes.Contains("state-machine") && classes.Contains("marked-graph")
            ? "state machine and marked graph"
            : classes[0].Replace('-', ' ');

    private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";
}
