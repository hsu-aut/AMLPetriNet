using PtMapper.Conversion;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// The rules the reports name: PT01 to PT11 from the validator, the others from the titles the
/// analysis core sends with its answer. A rule without a title is listed under its id.
/// </summary>
public static class RuleCatalog
{
    public sealed record Rule(string Id, string Title, string Tag);

    /// <summary>Every known rule and every rule the report mentions, sorted by id.</summary>
    public static IReadOnlyList<Rule> For(AnalyzeReport report)
    {
        var titles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, what) in PtConformanceReport.RuleTexts) titles[id] = what;
        foreach (var (id, title) in report.Rules)
            if (!string.IsNullOrWhiteSpace(title)) titles[id] = title;
        foreach (var finding in report.Nets.SelectMany(n => n.Findings))
            if (!titles.ContainsKey(finding.Rule)) titles[finding.Rule] = finding.Rule;

        return titles.Select(t => new Rule(t.Key, t.Value, IsStructural(t.Key) ? "structure" : "behaviour")).ToList();
    }

    /// <summary>PT01 to PT11 look at the net as drawn; the others at what it does.</summary>
    public static bool IsStructural(string id) =>
        id.Length == 4 && id.StartsWith("PT", StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(2), out var number) && number <= 11;

    /// <summary>Where the finding is: hierarchy, net and element, as one path.</summary>
    public static string Location(NetReport net, string? element) =>
        string.Join('/', new[] { net.Hierarchy, net.Id, element }.Where(part => !string.IsNullOrEmpty(part)));
}
