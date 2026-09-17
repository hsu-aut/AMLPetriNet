using System.Globalization;
using System.Text;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// A validation run written out as a document someone can hand over: a
/// reviewer, a partner, an appendix. The plugin shows findings in a panel and
/// the CLI prints them, but neither leaves anything behind.
///
/// Markdown rather than a custom XML format, because the report is meant to be
/// read by people. It carries enough context to be meaningful on its own: which
/// net, when, against which library version, and what was checked even when
/// nothing was found.
/// </summary>
public static class PtConformanceReport
{
    /// <summary>The ids of the rules this report covers, for tests that check coverage.</summary>
    public static IReadOnlyList<string> RuleIds => Rules.Select(r => r.Id).ToList();

    /// <summary>The rules the validator applies, in the order they are reported.</summary>
    private static readonly (string Id, string What)[] Rules =
    {
        ("PT01", "Element identifiers are unique"),
        ("PT02", "Every element has an identifier"),
        ("PT03", "The net is bipartite: no arc joins two places or two transitions"),
        ("PT04", "Both endpoints of every arc are nodes of the net"),
        ("PT05", "No redundant parallel arcs between the same ordered pair"),
        ("PT06", "Initial markings are non-negative"),
        ("PT07", "Arc weights are positive"),
        ("PT08", "No node is left unconnected"),
        ("PT09", "No transition is permanently enabled for lack of an input place"),
        ("PT10", "No transition consumes without producing"),
        ("PT11", "Identifiers are valid XML names, as xs:ID requires"),
    };

    public static string Build(PtNet net, IReadOnlyList<PtFinding> findings, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(net);
        ArgumentNullException.ThrowIfNull(findings);

        var errors = findings.Count(f => f.Severity == PtSeverity.Error);
        var warnings = findings.Count - errors;

        var report = new StringBuilder();
        report.AppendLine("# Petri net conformance report");
        report.AppendLine();
        report.AppendLine($"- Net: {Escape(net.Name ?? net.Id)} (`{net.Id}`)");
        if (!string.IsNullOrWhiteSpace(source)) report.AppendLine($"- Source: {Escape(source!)}");
        report.AppendLine($"- Checked: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        report.AppendLine($"- Standard: ISO/IEC 15909-1 (structural rules)");
        report.AppendLine($"- Library: {PtNames.LibraryFileName}");
        report.AppendLine($"- Size: {net.Places.Count} places, {net.Transitions.Count} transitions, {net.Arcs.Count} arcs");
        report.AppendLine();

        report.AppendLine("## Result");
        report.AppendLine();
        report.AppendLine(errors == 0
            ? $"**Passed.** {warnings} warning(s), no errors."
            : $"**Failed.** {errors} error(s), {warnings} warning(s).");
        report.AppendLine();

        if (findings.Count > 0)
        {
            report.AppendLine("## Findings");
            report.AppendLine();
            report.AppendLine("| Severity | Rule | Element | Message |");
            report.AppendLine("|---|---|---|---|");
            foreach (var finding in findings.OrderByDescending(f => f.Severity == PtSeverity.Error).ThenBy(f => f.Rule))
            {
                report.AppendLine($"| {finding.Severity} | {finding.Rule} | "
                    + $"{(finding.ElementId is null ? "" : "`" + Escape(finding.ElementId) + "`")} | {Escape(finding.Message)} |");
            }
            report.AppendLine();
        }

        // Naming what was checked is the part that makes a clean report worth
        // anything: "no findings" alone says nothing about coverage.
        report.AppendLine("## Rules applied");
        report.AppendLine();
        report.AppendLine("| Rule | Check | Result |");
        report.AppendLine("|---|---|---|");
        foreach (var (id, what) in Rules)
        {
            var hits = findings.Count(f => f.Rule == id);
            report.AppendLine($"| {id} | {what} | {(hits == 0 ? "pass" : $"{hits} finding(s)")} |");
        }

        return report.ToString();
    }

    /// <summary>Keeps a name with a pipe or a backtick from breaking the table.</summary>
    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);
}
