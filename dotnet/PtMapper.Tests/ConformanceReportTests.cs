using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

public class ConformanceReportTests
{
    private static PtNet Broken()
    {
        var net = new PtNet { Id = "n", Name = "Broken" };
        net.Places.Add(new PtPlace { Id = "p1" });
        net.Places.Add(new PtPlace { Id = "p2" });
        net.Arcs.Add(new PtArc { Id = "bad", SourceId = "p1", TargetId = "p2" });
        return net;
    }

    private static string Report(PtNet net, string? source = null) =>
        PtConformanceReport.Build(net, PtValidator.Validate(net), source);

    [Fact]
    public void ACleanNetReportsAsPassed()
    {
        var net = new PtNet { Id = "n", Name = "Fine" };
        net.Places.Add(new PtPlace { Id = "p1" });
        net.Transitions.Add(new PtTransition { Id = "t1" });
        net.Arcs.Add(new PtArc { Id = "a1", SourceId = "p1", TargetId = "t1" });
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "t1", TargetId = "p1" });

        var report = Report(net);

        Assert.Contains("**Passed.**", report);
        Assert.DoesNotContain("## Findings", report);
    }

    [Fact]
    public void AnErrorReportsAsFailed()
    {
        var report = Report(Broken());

        Assert.Contains("**Failed.**", report);
        Assert.Contains("## Findings", report);
        Assert.Contains("bipartite", report);
        Assert.Contains("`bad`", report);
    }

    [Fact]
    public void EveryRuleIsAccountedFor()
    {
        // A report that only lists what went wrong says nothing about coverage.
        var report = Report(Broken());

        // Derived, not listed: a hand-written list silently stopped at PT10
        // when PT11 was added, and the coverage claim went stale with it.
        var rules = PtConformanceReport.RuleIds;
        Assert.NotEmpty(rules);
        foreach (var rule in rules) Assert.Contains(rule, report);
    }

    [Fact]
    public void TheReportNamesTheNetAndTheLibrary()
    {
        var report = Report(Broken(), "somewhere.pnml");

        Assert.Contains("Broken", report);
        Assert.Contains("somewhere.pnml", report);
        Assert.Contains(PtNames.LibraryFileName, report);
        Assert.Contains("ISO/IEC 15909-1", report);
    }

    [Fact]
    public void APipeInANameDoesNotBreakTheTable()
    {
        var net = Broken();
        net.Places[0].Name = "a | b";
        net.Arcs[0].Name = "x | y";

        var report = Report(net);

        // Markdown tables are delimited by pipes, so a pipe in a name has to be
        // escaped or the row grows a column. The report has two tables with
        // different widths, so each row is compared against its own header.
        int? columns = null;
        foreach (var line in report.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                columns = null; // a blank line or a heading ends the table
                continue;
            }

            var delimiters = System.Text.RegularExpressions.Regex.Matches(line, @"(?<!\\)\|").Count;
            columns ??= delimiters;
            Assert.Equal(columns, delimiters);
        }
    }
}
