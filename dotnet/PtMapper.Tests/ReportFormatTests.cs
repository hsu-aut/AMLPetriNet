using System.Text.Json;
using System.Xml.Linq;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using PtMapper.Tool.Analysis;
using Xunit;

namespace PtMapper.Tests;

/// <summary>ptmap analyze --format sarif and junit, and --output. The analysis itself is answered by the test.</summary>
public class ReportFormatTests
{
    private sealed class FakeRunner(string answer) : IAnalysisRunner
    {
        public string Run(string requestJson, int timeoutSeconds) => answer;
    }

    private static string Answer(params string[] findings) =>
        $$"""{"core":"9.9.9","kind":"analyse","rules":{"PT15":"Deadlock","PT16":"Transition that can never fire again"},"results":[{"key":"0","problem":null,"complete":true,"stopped":null,"markings":3,"unbounded":[],"findings":[{{string.Join(",", findings)}}]}]}""";

    private static string F(string rule, string severity, string? element, string message, string? marking = null, string? cover = null) =>
        $$"""{"rule":"{{rule}}","severity":"{{severity}}","element":{{(element == null ? "null" : "\"" + element + "\"")}},"message":{{JsonSerializer.Serialize(message)}}{{(marking == null ? "" : ",\"marking\":\"" + marking + "\"")}}{{(cover == null ? "" : ",\"cover\":" + cover)}}}""";

    private static string Pnml(string name = "Loop")
    {
        var net = new PtNet { Id = "loop", Name = name };
        net.Places.Add(new PtPlace { Id = "p", Name = "Idle", InitialMarking = 1 });
        net.Transitions.Add(new PtTransition { Id = "t", Name = "Go" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });
        net.Arcs.Add(new PtArc { Id = "b", SourceId = "t", TargetId = "p" });
        var directory = Directory.CreateTempSubdirectory("ptmap-format-").FullName;
        var path = Path.Combine(directory, "net.pnml");
        File.WriteAllText(path, PtMapper.Conversion.Pnml.Write(net));
        return path;
    }

    private static (int Exit, string Out, string Err) Run(string answer, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = AnalyzeCommand.Run(args, output, error, new FakeRunner(answer));
        return (exit, output.ToString(), error.ToString());
    }

    private static readonly string Findings = Answer(
        F("PT15", "warning", "p", "Idle holds a token in a deadlock.", "p=1"),
        F("PT16", "information", "t", "Go can never fire again.", "p=0"),
        F("PT15", "error", "p", "Second twin.", "p=1"));

    // ── options ────────────────────────────────────────────────────────────
    [Fact]
    public void TheFormatsAndTheOutputAreAccepted()
    {
        foreach (var format in new[] { "text", "json", "sarif", "junit" })
            Assert.NotNull(AnalyzeOptions.Parse(new[] { "x.pnml", "--format", format }).Options);
        Assert.Equal("out.sarif", AnalyzeOptions.Parse(new[] { "x.pnml", "--output", "out.sarif" }).Options!.Output);
        Assert.NotNull(AnalyzeOptions.Parse(new[] { "x.pnml", "--format", "xml" }).Error);
        Assert.NotNull(AnalyzeOptions.Parse(new[] { "x.pnml", "--output" }).Error);
    }

    // ── SARIF ──────────────────────────────────────────────────────────────
    private static JsonElement Sarif(string answer, params string[] extra)
    {
        var file = Pnml();
        var directory = Path.GetDirectoryName(file)!;
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory);
            var (_, text, _) = Run(answer, new[] { "net.pnml", "--format", "sarif" }.Concat(extra).ToArray());
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }

    [Fact]
    public void SarifCarriesWhatGitHubRequires()
    {
        var root = Sarif(Findings, "--all");
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var run = root.GetProperty("runs")[0];
        Assert.Equal("ptmap", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(run.GetProperty("tool").GetProperty("driver").GetProperty("version").GetString()));

        foreach (var rule in run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("shortDescription").GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("fullDescription").GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("help").GetProperty("text").GetString()));
        }

        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            var location = result.GetProperty("locations")[0].GetProperty("physicalLocation");
            Assert.Equal("net.pnml", location.GetProperty("artifactLocation").GetProperty("uri").GetString());
            Assert.Equal("%SRCROOT%", location.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString());
            Assert.Equal(1, location.GetProperty("region").GetProperty("startLine").GetInt32());
            Assert.Matches("^[0-9a-f]{16}:[0-9]+$", result.GetProperty("partialFingerprints").GetProperty("primaryLocationLineHash").GetString());
        }
    }

    [Fact]
    public void SarifLevelsAndInformationOnlyWithAll()
    {
        var without = Sarif(Findings).GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToList();
        Assert.DoesNotContain(without, r => r.GetProperty("level").GetString() == "note");
        Assert.Equal(2, without.Count);

        var with = Sarif(Findings, "--all").GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(3, with.Count);
        Assert.Contains(with, r => r.GetProperty("level").GetString() == "note");
        Assert.Contains(with, r => r.GetProperty("level").GetString() == "error");
    }

    [Fact]
    public void SarifNamesTheElementAndTheRuleTitleFromTheCore()
    {
        var run = Sarif(Findings, "--all").GetProperty("runs")[0];
        var result = run.GetProperty("results")[0];
        Assert.Contains("[loop/p]", result.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal("loop/p", result.GetProperty("locations")[0].GetProperty("logicalLocations")[0].GetProperty("fullyQualifiedName").GetString());
        var titles = run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("shortDescription").GetProperty("text").GetString());
        Assert.Equal("Deadlock", titles["PT15"]);
        Assert.Equal("Element identifiers are unique", titles["PT01"]);
    }

    [Fact]
    public void FingerprintsAreStableAndTwinsAreCounted()
    {
        string[] Prints(string answer) => Sarif(answer, "--all").GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("partialFingerprints").GetProperty("primaryLocationLineHash").GetString()!).ToArray();

        var twins = Answer(F("PT15", "warning", "p", "One.", "p=1"), F("PT15", "warning", "p", "Two.", "p=1"));
        var prints = Prints(twins);
        Assert.Equal(prints[0].Split(':')[0], prints[1].Split(':')[0]);
        Assert.Equal(new[] { "1", "2" }, prints.Select(p => p.Split(':')[1]).ToArray());

        // Another wording is the same finding; another element is not.
        var reworded = Answer(F("PT15", "warning", "p", "Reworded.", "p=1"));
        Assert.Equal(prints[0], Prints(reworded)[0]);
        Assert.NotEqual(prints[0], Prints(Answer(F("PT15", "warning", "q", "One.", "p=1")))[0]);
    }

    [Fact]
    public void ACoverIsReadInAnyOrder()
    {
        string Print(string cover) => Sarif(Answer(F("PT14", "warning", "t", "m", null, cover)), "--all")
            .GetProperty("runs")[0].GetProperty("results")[0].GetProperty("partialFingerprints").GetProperty("primaryLocationLineHash").GetString()!;
        Assert.Equal(Print("""[["a",1],["b",1]]"""), Print("""[["b",1],["a",1]]"""));
    }

    [Fact]
    public void AFileOutsideTheCurrentDirectoryIsAFileUri()
    {
        Assert.Equal("a%20b/net.pnml", FindingsSarif.UriOf("a b/net.pnml", Directory.GetCurrentDirectory()));
        var other = Path.Combine(Path.GetTempPath(), "elsewhere", "net.pnml");
        Assert.StartsWith("file:///", FindingsSarif.UriOf(other, Path.Combine(Path.GetTempPath(), "here")));
    }

    // ── JUnit ──────────────────────────────────────────────────────────────
    [Fact]
    public void JUnitHasOneTestCasePerRuleAndFailsOnWarnings()
    {
        var file = Pnml();
        var (exit, text, _) = Run(Findings, file, "--format", "junit");
        Assert.Equal(ExitCodes.Errors, exit);
        Assert.NotEqual((char)0xFEFF, text[0]);

        var root = XDocument.Parse(text).Root!;
        var suite = Assert.Single(root.Elements("testsuite"));
        var cases = suite.Elements("testcase").ToList();
        Assert.Equal(int.Parse(suite.Attribute("tests")!.Value), cases.Count);
        Assert.Equal(cases.Count, cases.Select(c => c.Attribute("name")!.Value).Distinct().Count());

        var deadlock = cases.Single(c => c.Attribute("name")!.Value.StartsWith("PT15 "));
        var failure = Assert.Single(deadlock.Elements("failure"));
        Assert.Equal("error", failure.Attribute("type")!.Value);
        Assert.Contains("Idle holds a token", failure.Value);

        // Information never fails a test case: it goes to system-out.
        var never = cases.Single(c => c.Attribute("name")!.Value.StartsWith("PT16 "));
        Assert.Empty(never.Elements("failure"));
        Assert.Contains("Go can never fire again.", never.Element("system-out")!.Value);

        Assert.Equal("0", root.Attribute("errors")!.Value);
        Assert.Equal(root.Attribute("failures")!.Value, cases.Count(c => c.Elements("failure").Any()).ToString());
    }

    [Fact]
    public void JUnitEscapesMarkupAndSkipsWhatWasNotAnalysed()
    {
        var file = Pnml();
        var (_, text, _) = Run(Answer(F("PT15", "warning", "p", "<Station> & \"co\" Prüf")), file, "--format", "junit");
        var failure = XDocument.Parse(text).Descendants("failure").Single();
        Assert.Contains("<Station> & \"co\" Prüf", failure.Value);

        // A net that breaks the standard: its behavioural test cases are skipped, and the structural ones are not.
        var broken = new PtNet { Id = "bad", Name = "Bad" };
        broken.Places.Add(new PtPlace { Id = "p", Name = "P" });
        broken.Places.Add(new PtPlace { Id = "q", Name = "Q" });
        broken.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "q" });
        var path = Path.Combine(Directory.CreateTempSubdirectory("ptmap-format-").FullName, "bad.pnml");
        File.WriteAllText(path, PtMapper.Conversion.Pnml.Write(broken));
        var (exit, bad, _) = Run("{}", path, "--format", "junit");
        Assert.Equal(ExitCodes.Errors, exit);
        var cases = XDocument.Parse(bad).Descendants("testcase").ToList();
        Assert.Contains(cases, c => c.Attribute("name")!.Value == "behaviour" && c.Elements("skipped").Any());
        Assert.Contains(cases, c => c.Attribute("name")!.Value.StartsWith("PT03 ") && c.Elements("failure").Any());
    }

    // ── --output and the exit code ─────────────────────────────────────────
    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("sarif")]
    [InlineData("junit")]
    public void TheFormatNeverChangesTheExitCode(string format)
    {
        var file = Pnml();
        var (exit, _, _) = Run(Findings, file, "--format", format);
        Assert.Equal(ExitCodes.Errors, exit);
        Assert.Equal(ExitCodes.Ok, Run(Findings, file, "--format", format, "--fail-on", "never").Exit);
    }

    [Fact]
    public void OutputWritesTheFileWithoutAByteOrderMarkAndSaysSo()
    {
        var file = Pnml();
        var target = Path.Combine(Path.GetDirectoryName(file)!, "report.sarif");
        var (exit, stdout, _) = Run(Findings, file, "--format", "sarif", "--output", target);
        Assert.Equal(ExitCodes.Errors, exit);

        var bytes = File.ReadAllBytes(target);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Single(stdout.Split((char)10, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("report.sarif", stdout);
        JsonDocument.Parse(bytes);
    }

    [Fact]
    public void AFailedRunLeavesTheFileAlone()
    {
        var file = Pnml();
        var target = Path.Combine(Path.GetDirectoryName(file)!, "report.sarif");
        File.WriteAllText(target, "old");

        Assert.Equal(ExitCodes.Input, Run(Findings, Path.Combine(Path.GetDirectoryName(file)!, "missing.pnml"), "--output", target).Exit);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void TheOutputMayNotBeTheInput()
    {
        var file = Pnml();
        var before = File.ReadAllText(file);
        Assert.Equal(ExitCodes.Usage, Run(Findings, file, "--format", "sarif", "--output", file).Exit);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void ABundleThatSendsNoRulesStillGivesAReport()
    {
        var answer = Findings.Replace("\"rules\":{\"PT15\":\"Deadlock\",\"PT16\":\"Transition that can never fire again\"}", "\"rules\":null");
        var (exit, text, _) = Run(answer, Pnml(), "--format", "sarif");
        Assert.Equal(ExitCodes.Errors, exit);
        var rules = JsonDocument.Parse(text).RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules");
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("id").GetString() == "PT15" && r.GetProperty("shortDescription").GetProperty("text").GetString() == "PT15");
    }

    [Fact]
    public void AWarningOnlyFailsAsAWarning()
    {
        var (_, text, _) = Run(Answer(F("PT15", "warning", "p", "Idle holds a token.")), Pnml(), "--format", "junit");
        Assert.Equal("warning", XDocument.Parse(text).Descendants("failure").Single().Attribute("type")!.Value);
    }

    [Fact]
    public void AReportIsWrittenWithLineFeedsOnly()
    {
        var file = Pnml();
        var target = Path.Combine(Path.GetDirectoryName(file)!, "r.sarif");
        Run(Findings, file, "--format", "sarif", "--output", target);
        Assert.DoesNotContain((char)13, File.ReadAllText(target));
    }

    [Fact]
    public void AFailureOfTheToolLeavesTheFileAlone()
    {
        var file = Pnml();
        var target = Path.Combine(Path.GetDirectoryName(file)!, "r.sarif");
        File.WriteAllText(target, "old");
        var output = new StringWriter();
        var exit = AnalyzeCommand.Run(new[] { file, "--output", target }, output, new StringWriter(), new ThrowingRunner());
        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Equal("old", File.ReadAllText(target));
    }

    private sealed class ThrowingRunner : IAnalysisRunner
    {
        public string Run(string requestJson, int timeoutSeconds) => throw new AnalysisFailedException("boom");
    }
}
