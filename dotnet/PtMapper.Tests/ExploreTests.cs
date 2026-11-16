using System.Text.Json;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using PtMapper.Tool.Analysis;
using Xunit;

namespace PtMapper.Tests;

/// <summary>ptmap explore. Parts that are plain data use a runner that answers from the test; the rest runs the real bundle.</summary>
public class ExploreTests
{
    private sealed class FakeRunner(string answer) : IAnalysisRunner
    {
        public string? Request { get; private set; }
        public int Calls { get; private set; }
        public string Run(string requestJson, int timeoutSeconds) { Request = requestJson; Calls++; return answer; }
    }

    private static string Answer(string text = "digraph state_space {\n}\n", string? stopped = null, string unbounded = "", string kind = "\"kind\":\"explore\",", string? problem = null) =>
        $$"""{"core":"9.9.9",{{kind}}"results":[{"key":"0","problem":{{(problem == null ? "null" : JsonSerializer.Serialize(problem))}},"complete":{{(stopped == null ? "true" : "false")}},"stopped":{{(stopped == null ? "null" : "\"" + stopped + "\"")}},"markings":2,"edges":1,"deadlocks":1,"unbounded":[{{unbounded}}],"text":{{JsonSerializer.Serialize(text)}}}]}""";

    private static PtNet Loop(string id = "loop")
    {
        var net = new PtNet { Id = id, Name = id.ToUpperInvariant() };
        net.Places.Add(new PtPlace { Id = "p", Name = "Idle", InitialMarking = 1 });
        net.Transitions.Add(new PtTransition { Id = "t", Name = "Go" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });
        return net;
    }

    private static string FileOf(params PtNet[] nets)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ptmap-explore-").FullName, "net.pnml");
        File.WriteAllText(path, nets.Length == 1 ? Pnml.Write(nets[0]) : Pnml.Write(nets[0]).Replace("</pnml>", string.Concat(nets.Skip(1).Select(n => Pnml.Write(n).Split("<net", 2)[1].Insert(0, "<net").Split("</pnml>")[0])) + "</pnml>"));
        return path;
    }

    private static (int Exit, string Out, string Err) Run(IAnalysisRunner runner, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = ExploreCommand.Run(args, output, error, runner);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public void TheDefaultsAndTheBadOptions()
    {
        var options = ExploreOptions.Parse(new[] { "x.pnml" }).Options!;
        Assert.Equal(("dot", 20000, null, 120, false), (options.Format, options.Markings, options.TimeLimitMilliseconds, options.TimeoutSeconds, options.Partial));
        foreach (var bad in new[] { new[] { "x.pnml", "--format", "json" }, new[] { "x.pnml", "--net" }, new[] { "x.pnml", "--markings", "0" }, new[] { "--partial" }, new[] { "x.pnml", "--nope" } })
            Assert.NotNull(ExploreOptions.Parse(bad).Error);
    }

    [Fact]
    public void TheRequestCarriesKindFormatAndOneNet()
    {
        var runner = new FakeRunner(Answer());
        var (exit, text, _) = Run(runner, FileOf(Loop()), "--format", "csv", "--markings", "50");
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal("digraph state_space {\n}\n", text);

        var request = JsonDocument.Parse(runner.Request!).RootElement;
        Assert.Equal("explore", request.GetProperty("kind").GetString());
        Assert.Equal("csv", request.GetProperty("format").GetString());
        Assert.Equal(50, request.GetProperty("limits").GetProperty("markings").GetInt32());
        Assert.Equal(1, request.GetProperty("nets").GetArrayLength());
    }

    [Fact]
    public void TheTextIsWrittenUnchanged()
    {
        const string text = "digraph {\n  a [label=\"Prüf <x> & \\\"y\\\" ω\"];\n}\n";
        var file = FileOf(Loop());
        var target = Path.Combine(Path.GetDirectoryName(file)!, "out.dot");
        var (exit, stdout, _) = Run(new FakeRunner(Answer(text)), file, "--output", target);

        Assert.Equal(ExitCodes.Ok, exit);
        var bytes = File.ReadAllBytes(target);
        Assert.Equal(text, new System.Text.UTF8Encoding(false, true).GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("out.dot", stdout);
        Assert.Contains("2 markings", stdout);
    }

    [Fact]
    public void SeveralNetsTakeTheFirstWithANoteAndNetPicksAnother()
    {
        var file = FileOf(Loop("first"), Loop("second"));
        var (exit, _, err) = Run(new FakeRunner(Answer()), file);
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("using 'first'", err);

        var (pickExit, _, pickErr) = Run(new FakeRunner(Answer()), file, "--net", "second");
        Assert.Equal(ExitCodes.Ok, pickExit);
        Assert.Contains("SECOND (second)", pickErr);

        var (missing, _, missingErr) = Run(new FakeRunner(Answer()), file, "--net", "third");
        Assert.Equal(ExitCodes.Input, missing);
        Assert.Contains("first, second", missingErr);
    }

    [Fact]
    public void ARunThatFailsWritesNothingAndLeavesTheFile()
    {
        var file = FileOf(Loop());
        var target = Path.Combine(Path.GetDirectoryName(file)!, "out.dot");
        File.WriteAllText(target, "old");

        // 2: no such file. 3: an answer without kind, a problem, a throwing runner. 5: stopped at a limit.
        Assert.Equal(ExitCodes.Input, Run(new FakeRunner(Answer()), Path.Combine(Path.GetDirectoryName(file)!, "missing.pnml"), "--output", target).Exit);
        var (old, _, oldErr) = Run(new FakeRunner(Answer(kind: "")), file, "--output", target);
        Assert.Equal(ExitCodes.Failure, old);
        Assert.Contains("npm run build", oldErr);
        Assert.Equal(ExitCodes.Failure, Run(new FakeRunner(Answer(problem: "broken")), file, "--output", target).Exit);
        Assert.Equal(ExitCodes.Warnings, Run(new FakeRunner(Answer(stopped: "markings")), file, "--output", target).Exit);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void ANetThatBreaksTheStandardIsNotExploredAndNodeIsNotStarted()
    {
        var net = new PtNet { Id = "bad", Name = "Bad" };
        net.Places.Add(new PtPlace { Id = "p", Name = "P" });
        net.Places.Add(new PtPlace { Id = "q", Name = "Q" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "q" });
        var runner = new FakeRunner(Answer());
        var (exit, text, err) = Run(runner, FileOf(net));
        Assert.Equal(ExitCodes.Errors, exit);
        Assert.Equal(0, runner.Calls);
        Assert.Equal("", text);
        Assert.Contains("PT03", err);
    }

    [Fact]
    public void AStoppedSearchIsWrittenOnlyWithPartial()
    {
        var file = FileOf(Loop());
        var (without, text, err) = Run(new FakeRunner(Answer(stopped: "markings")), file);
        Assert.Equal(ExitCodes.Warnings, without);
        Assert.Equal("", text);
        Assert.Contains("--partial", err);

        var (with, partial, note) = Run(new FakeRunner(Answer("digraph {\n}\n", "markings")), file, "--partial");
        Assert.Equal(ExitCodes.Ok, with);
        Assert.StartsWith("digraph", partial);
        Assert.Contains("a part of the state space", note);
    }

    [Fact]
    public void UnboundedPlacesGetANote()
    {
        var (exit, _, err) = Run(new FakeRunner(Answer(unbounded: "\"p\"")), FileOf(Loop()));
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("p grow without bound", err);
    }

    [Fact]
    public void TheOutputMayNotBeTheInput()
    {
        var file = FileOf(Loop());
        var before = File.ReadAllText(file);
        Assert.Equal(ExitCodes.Usage, Run(new FakeRunner(Answer()), file, "--output", file).Exit);
        Assert.Equal(before, File.ReadAllText(file));
    }

    // ── the real bundle ────────────────────────────────────────────────────
    private static string QualityGate(string extension) => Path.Combine(AnalyzeTests.RepoRoot(), "examples", "quality-gate." + extension);

    [SkippableFact]
    public void TheQualityGateAsDotAndCsvMatchesTheAnalysis()
    {
        var runner = AnalyzeTests.RealRunner();
        var (exit, dot, _) = Run(runner, QualityGate("pnml"));
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("doublecircle", dot);

        var report = new StringWriter();
        AnalyzeCommand.Run(new[] { QualityGate("pnml"), "--format", "json" }, report, new StringWriter(), runner);
        var markings = JsonDocument.Parse(report.ToString()).RootElement.GetProperty("nets")[0].GetProperty("markings").GetInt32();
        Assert.Equal(markings, dot.Split('\n').Count(line => line.TrimStart().StartsWith("m") && !line.Contains("->") && line.Contains("[label=")));

        var (csvExit, csv, _) = Run(runner, QualityGate("pnml"), "--format", "csv");
        Assert.Equal(ExitCodes.Ok, csvExit);
        Assert.StartsWith("marking,firings,deadlock", csv);
        Assert.DoesNotContain('\r', csv);
    }

    [SkippableFact]
    public void AmlAndPnmlOfTheQualityGateGiveTheSameText()
    {
        var runner = AnalyzeTests.RealRunner();
        var pnml = Run(runner, QualityGate("pnml")).Out;
        var aml = Run(runner, QualityGate("aml")).Out;
        Assert.Equal(pnml, aml);
    }

    [SkippableFact]
    public void AStoppedSearchOnTheRealBundleGivesFiveAndWithPartialZero()
    {
        var runner = AnalyzeTests.RealRunner();
        Assert.Equal(ExitCodes.Warnings, Run(runner, QualityGate("pnml"), "--markings", "5").Exit);
        var (exit, text, _) = Run(runner, QualityGate("pnml"), "--markings", "5", "--partial");
        Assert.Equal(ExitCodes.Ok, exit);
        Assert.StartsWith("digraph", text);
    }
}
