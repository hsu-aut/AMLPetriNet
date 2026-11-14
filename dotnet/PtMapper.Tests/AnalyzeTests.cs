using System.Diagnostics;
using System.Text.Json;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using PtMapper.Tool.Analysis;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// ptmap analyze. The parts that are plain data are tested with a runner that
/// answers from the test; the parts that need the real analysis are tested end
/// to end and are skipped where Node is not installed, unless
/// PTMAP_REQUIRE_NODE=1, which CI sets so that a skip cannot hide a failure.
/// </summary>
public class AnalyzeTests
{
    // ── a runner that answers from the test ────────────────────────────────
    private sealed class FakeRunner(string answer) : IAnalysisRunner
    {
        public string? Request { get; private set; }
        public int Calls { get; private set; }
        public string Run(string requestJson, int timeoutSeconds) { Request = requestJson; Calls++; return answer; }
    }

    private sealed class ThrowingRunner(Exception exception) : IAnalysisRunner
    {
        public string Run(string requestJson, int timeoutSeconds) => throw exception;
    }

    private static string Answer(params string[] findings) =>
        $$"""{"core":"9.9.9","results":[{"key":"0","problem":null,"complete":true,"stopped":null,"markings":3,"unbounded":[],"findings":[{{string.Join(",", findings)}}]}]}""";

    private static string F(string rule, string severity, string? element = null, string message = "m") =>
        $$"""{"rule":"{{rule}}","severity":"{{severity}}","element":{{(element == null ? "null" : "\"" + element + "\"")}},"message":"{{message}}"}""";

    private static string PnmlOf(PtNet net)
    {
        var path = Path.Combine(Path.GetTempPath(), "ptmap-analyze-" + Guid.NewGuid().ToString("N") + ".pnml");
        File.WriteAllText(path, Pnml.Write(net));
        return path;
    }

    private static PtNet Loop()
    {
        var net = new PtNet { Id = "loop", Name = "Loop" };
        net.Places.Add(new PtPlace { Id = "p", Name = "Idle", InitialMarking = 1 });
        net.Transitions.Add(new PtTransition { Id = "t", Name = "Go" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });
        net.Arcs.Add(new PtArc { Id = "b", SourceId = "t", TargetId = "p" });
        return net;
    }

    private static (int Exit, string Out, string Err) Run(IAnalysisRunner runner, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = AnalyzeCommand.Run(args, output, error, runner);
        return (exit, output.ToString(), error.ToString());
    }

    // ── the command line ────────────────────────────────────────────────────
    [Fact]
    public void TheDefaultsAreWhatThePlanSays()
    {
        var (options, error) = AnalyzeOptions.Parse(new[] { "net.pnml" });
        Assert.Null(error);
        Assert.Equal(("text", "warning", 20000, (int?)null, 120, false, false),
            (options!.Format, options.FailOn, options.Markings, options.TimeLimitMilliseconds, options.TimeoutSeconds, options.All, options.RequireComplete));
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--format", "xml")]
    [InlineData("--fail-on", "sometimes")]
    [InlineData("--markings", "0")]
    [InlineData("--markings", "many")]
    [InlineData("--time-limit", "0")]
    [InlineData("--timeout", "-3")]
    [InlineData("--timeout", "86401")]
    public void ABadOptionIsAUsageError(params string[] bad)
    {
        var (options, error) = AnalyzeOptions.Parse(new[] { "net.pnml" }.Concat(bad).ToArray());
        Assert.Null(options);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AMissingValueIsAUsageError() =>
        Assert.Null(AnalyzeOptions.Parse(new[] { "net.pnml", "--markings" }).Options);

    [Fact]
    public void ABadOptionGivesExitOne()
    {
        var runner = new FakeRunner(Answer());
        var (exit, _, err) = Run(runner, "net.pnml", "--markings", "0");
        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("ptmap analyze", err);
        Assert.Equal(0, runner.Calls);
    }

    // ── the request ─────────────────────────────────────────────────────────
    [Fact]
    public void TheRequestCarriesIdsNamesMarkingsAndWeights()
    {
        var net = Loop();
        net.Places[0].Name = null; // no name becomes an empty string
        net.Arcs[0].Weight = 3;

        using var json = JsonDocument.Parse(AnalysisRequest.Build(new[] { net }, 500, null));
        var root = json.RootElement;
        Assert.Equal(500, root.GetProperty("limits").GetProperty("markings").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("limits").GetProperty("milliseconds").ValueKind);

        var sent = root.GetProperty("nets")[0];
        Assert.Equal("0", sent.GetProperty("key").GetString());
        Assert.Equal("", sent.GetProperty("places")[0].GetProperty("name").GetString());
        Assert.Equal(1, sent.GetProperty("places")[0].GetProperty("marking").GetInt32());
        Assert.Equal(3, sent.GetProperty("arcs")[0].GetProperty("weight").GetInt32());
        Assert.Equal("t", sent.GetProperty("arcs")[0].GetProperty("target").GetString());
    }

    [Fact]
    public void ParallelArcsStayTwoArcs()
    {
        var net = Loop();
        net.Arcs.Add(new PtArc { Id = "a2", SourceId = "p", TargetId = "t" });
        using var json = JsonDocument.Parse(AnalysisRequest.Build(new[] { net }, 1, 5));
        Assert.Equal(3, json.RootElement.GetProperty("nets")[0].GetProperty("arcs").GetArrayLength());
        Assert.Equal(5, json.RootElement.GetProperty("limits").GetProperty("milliseconds").GetInt32());
    }

    [Fact]
    public void MarkupAndUmlautsInNamesSurviveTheRequest()
    {
        var net = Loop();
        net.Places[0].Name = "Prüf <a> & \"b\"";
        var text = AnalysisRequest.Build(new[] { net }, 1, null);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("Prüf <a> & \"b\"", json.RootElement.GetProperty("nets")[0].GetProperty("places")[0].GetProperty("name").GetString());
        Assert.False(text.StartsWith('﻿'));
    }

    // ── the exit codes ──────────────────────────────────────────────────────
    [Theory]
    [InlineData("warning", "information", "text", ExitCodes.Ok)]
    [InlineData("warning", "warning", "text", ExitCodes.Warnings)]
    [InlineData("error", "warning", "text", ExitCodes.Ok)]
    [InlineData("never", "warning", "text", ExitCodes.Ok)]
    [InlineData("warning", "warning", "json", ExitCodes.Warnings)]
    public void TheExitCodeFollowsTheSeverityAndTheSwitch(string failOn, string severity, string format, int expected)
    {
        var path = PnmlOf(Loop());
        try
        {
            var (exit, _, _) = Run(new FakeRunner(Answer(F("PT13", severity, "p"))), path, "--fail-on", failOn, "--format", format);
            Assert.Equal(expected, exit);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("warning", ExitCodes.Errors)]
    [InlineData("error", ExitCodes.Errors)]
    [InlineData("never", ExitCodes.Ok)]
    public void AnErrorFailsUnlessNothingMayFail(string failOn, int expected)
    {
        var path = PnmlOf(Loop());
        try { Assert.Equal(expected, Run(new FakeRunner(Answer(F("PT99", "error"))), path, "--fail-on", failOn).Exit); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void APartialResultFailsOnlyWithRequireComplete()
    {
        var partial = """{"core":"1","results":[{"key":"0","problem":null,"complete":false,"stopped":"markings","markings":5,"unbounded":[],"findings":[]}]}""";
        var path = PnmlOf(Loop());
        try
        {
            Assert.Equal(ExitCodes.Ok, Run(new FakeRunner(partial), path).Exit);
            Assert.Equal(ExitCodes.Warnings, Run(new FakeRunner(partial), path, "--require-complete").Exit);
            Assert.Equal(ExitCodes.Ok, Run(new FakeRunner(Answer()), path, "--require-complete").Exit);

            // Asked for by name, so it holds whatever --fail-on says about warnings,
            // except "never", which leaves only the failures of the tool.
            Assert.Equal(ExitCodes.Warnings, Run(new FakeRunner(partial), path, "--require-complete", "--fail-on", "error").Exit);
            Assert.Equal(ExitCodes.Ok, Run(new FakeRunner(partial), path, "--require-complete", "--fail-on", "never").Exit);
        }
        finally { File.Delete(path); }
    }

    // ── what ends up in the report ──────────────────────────────────────────
    [Fact]
    public void FindingsAreOrderedErrorsWarningsInformation()
    {
        var path = PnmlOf(Loop());
        try
        {
            var runner = new FakeRunner(Answer(F("PT13", "information"), F("PT14", "warning"), F("PT99", "error"), F("PT16", "information")));
            var (_, output, _) = Run(runner, path, "--format", "json");
            using var json = JsonDocument.Parse(output);
            var order = json.RootElement.GetProperty("nets")[0].GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("rule").GetString()).ToArray();
            Assert.Equal(new[] { "PT99", "PT14", "PT13", "PT16" }, order);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void ANetWithoutTransitionsReportsNoDeadlock()
    {
        // The core decides this, so the real one answers.
        var runner = RealRunner();
        var net = new PtNet { Id = "lonely", Name = "Lonely" };
        net.Places.Add(new PtPlace { Id = "p", Name = "P", InitialMarking = 1 });
        var path = PnmlOf(net);
        try
        {
            var (_, output, _) = Run(runner, path, "--format", "json");
            Assert.DoesNotContain("PT15", output);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ANetThatBreaksTheStandardIsNotAnalysedAndSaysWhy()
    {
        var net = Loop();
        net.Arcs.Add(new PtArc { Id = "bad", SourceId = "p", TargetId = "p" }); // place to place
        var path = PnmlOf(net);
        try
        {
            var runner = new FakeRunner(Answer());
            var (exit, output, _) = Run(runner, path, "--format", "json");
            Assert.Equal(ExitCodes.Errors, exit);
            Assert.Equal(0, runner.Calls); // nothing was left to analyse, so nobody was asked
            using var json = JsonDocument.Parse(output);
            var reported = json.RootElement.GetProperty("nets")[0];
            Assert.False(reported.GetProperty("analysed").GetBoolean());
            Assert.Contains("ISO/IEC 15909-1", reported.GetProperty("problem").GetString());
            Assert.Contains(reported.GetProperty("findings").EnumerateArray(), f => f.GetProperty("severity").GetString() == "error");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheTextCountsNotesAndListsThemWithAll()
    {
        var path = PnmlOf(Loop());
        try
        {
            var answer = Answer(F("PT14", "warning", "t", "Go can never fire"), F("PT13", "information", "p", "Idle holds at most one token"));
            var (_, brief, _) = Run(new FakeRunner(answer), path);
            Assert.Contains("1 warning", brief);
            Assert.Contains("Go can never fire", brief);
            Assert.DoesNotContain("holds at most", brief);
            Assert.Contains("--all", brief);

            var (_, full, _) = Run(new FakeRunner(answer), path, "--all");
            Assert.Contains("holds at most", full);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheJsonCarriesEveryFieldOfThePlan()
    {
        var path = PnmlOf(Loop());
        try
        {
            var (_, output, _) = Run(new FakeRunner(Answer(F("PT13", "information", "p"))), path, "--format", "json", "--markings", "77");
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            Assert.Equal(path, root.GetProperty("file").GetString());
            Assert.Equal("petrinetjs 9.9.9", root.GetProperty("core").GetString());
            Assert.Equal(77, root.GetProperty("limits").GetProperty("markings").GetInt32());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("limits").GetProperty("milliseconds").ValueKind);
            Assert.Equal(1, root.GetProperty("counts").GetProperty("information").GetInt32());
            Assert.Equal(0, root.GetProperty("exit").GetInt32());

            var net = root.GetProperty("nets")[0];
            foreach (var name in new[] { "id", "name", "hierarchy", "analysed", "problem", "complete", "stopped", "markings", "unbounded", "counts", "findings" })
                Assert.True(net.TryGetProperty(name, out _), $"the net lacks '{name}'");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadWarningsGoToTheErrorStreamAndNotIntoTheJson()
    {
        // A PNML file with an arc that has no id: the reader leaves it out and says so.
        var pnml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <pnml xmlns="http://www.pnml.org/version-2009/grammar/pnml">
              <net id="n" type="http://www.pnml.org/version-2009/grammar/ptnet">
                <page id="pg">
                  <place id="p"><initialMarking><text>1</text></initialMarking></place>
                  <transition id="t"/>
                  <arc id="a" source="p" target="t"/>
                  <arc id="" source="t" target="p"/>
                </page>
              </net>
            </pnml>
            """;
        var path = Path.Combine(Path.GetTempPath(), "ptmap-warn-" + Guid.NewGuid().ToString("N") + ".pnml");
        File.WriteAllText(path, pnml);
        try
        {
            var (_, output, err) = Run(new FakeRunner(Answer()), path, "--format", "json");
            Assert.Contains("note: ", err);
            Assert.DoesNotContain("note:", output);
            using var json = JsonDocument.Parse(output); // JSON from the first character
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        }
        finally { File.Delete(path); }
    }

    // ── what goes wrong around it ───────────────────────────────────────────
    [Fact]
    public void AMissingFileGivesExitTwo()
    {
        var (exit, _, err) = Run(new FakeRunner(Answer()), "/no/such/file.pnml");
        Assert.Equal(ExitCodes.Input, exit);
        Assert.Contains("no such file", err);
    }

    [Fact]
    public void AMissingHierarchyGivesExitTwo()
    {
        var aml = FindFile("examples/paper/BridgingExample_MPS500.aml");
        var (exit, _, err) = Run(new FakeRunner(Answer()), aml, "IH_DoesNotExist");
        Assert.Equal(ExitCodes.Input, exit);
        Assert.Contains("IH_DoesNotExist", err);
    }

    [Fact]
    public void ARunnerThatCannotRunGivesExitTwoWithItsSentence()
    {
        var path = PnmlOf(Loop());
        try
        {
            var (exit, _, err) = Run(new ThrowingRunner(new AnalysisUnavailableException("needs Node 20")), path);
            Assert.Equal(ExitCodes.Input, exit);
            Assert.Contains("needs Node 20", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ARunnerThatFailsGivesExitThree()
    {
        var path = PnmlOf(Loop());
        try
        {
            var (exit, _, err) = Run(new ThrowingRunner(new AnalysisFailedException("ran out of time")), path);
            Assert.Equal(ExitCodes.Failure, exit);
            Assert.Contains("ran out of time", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ANetThatGotNoAnswerIsAFailureAndNotAnEmptyReport()
    {
        var path = PnmlOf(Loop());
        try
        {
            var wrongKey = """{"core":"1","results":[{"key":"7","problem":null,"complete":true,"stopped":null,"markings":1,"unbounded":[],"findings":[]}]}""";
            var (exit, output, err) = Run(new FakeRunner(wrongKey), path, "--require-complete");
            Assert.Equal(ExitCodes.Failure, exit);
            Assert.Contains("no result", err);
            Assert.Equal("", output);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ANetTheCoreCouldNotReadIsAFailure()
    {
        var path = PnmlOf(Loop());
        try
        {
            var refused = """{"core":"1","results":[{"key":"0","problem":"the weight of arc a is 0","complete":false,"stopped":null,"markings":0,"unbounded":[],"findings":[]}]}""";
            var (exit, _, err) = Run(new FakeRunner(refused), path);
            Assert.Equal(ExitCodes.Failure, exit);
            Assert.Contains("the weight of arc a is 0", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnAnswerThatIsNotJsonGivesExitThree()
    {
        var path = PnmlOf(Loop());
        try { Assert.Equal(ExitCodes.Failure, Run(new FakeRunner("this is not json"), path).Exit); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnAmlFindingCarriesTheIdOfTheElementInTheDocument()
    {
        var aml = FindFile("examples/paper/BridgingExample_MPS500.aml");
        var document = Aml.Engine.CAEX.CAEXDocument.LoadFromFile(aml);
        var hierarchy = document.CAEXFile.InstanceHierarchy.First(CaexToPtNet.ContainsNet);
        var net = CaexToPtNet.Read(hierarchy)[0];
        var element = net.Transitions[0].Id;
        var expected = CaexToPtNet.FindElement(CaexToPtNet.NetElements(hierarchy)[0], element)!.ID;

        var (_, output, _) = Run(new FakeRunner(Answer(F("PT14", "warning", element))), aml, hierarchy.Name, "--format", "json");
        using var json = JsonDocument.Parse(output);
        var found = json.RootElement.GetProperty("nets")[0].GetProperty("findings")[0];
        Assert.Equal(expected, found.GetProperty("internalElementId").GetString());
        Assert.Equal(hierarchy.Name, json.RootElement.GetProperty("nets")[0].GetProperty("hierarchy").GetString());
    }

    // ── end to end, with the real analysis ──────────────────────────────────
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web")) ) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }

    private static string FindFile(string relative) => Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string BundlePath => Path.Combine(RepoRoot(), "web", "dist-node", "ptmap-analysis.mjs");

    private static bool NodeWorks()
    {
        try
        {
            var info = new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using var process = Process.Start(info);
            if (process == null) return false;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && int.TryParse(text.Trim().TrimStart('v').Split('.')[0], out var major) && major >= 20;
        }
        catch { return false; }
    }

    private static NodeAnalysisRunner RealRunner()
    {
        var required = Environment.GetEnvironmentVariable("PTMAP_REQUIRE_NODE") == "1";
        var ready = NodeWorks() && File.Exists(BundlePath);
        if (!ready && required) Assert.Fail("Node 20 and the analysis bundle are required here (PTMAP_REQUIRE_NODE=1).");
        Skip.IfNot(ready, "Node 20 or the analysis bundle (npm run build in web/) is not available.");
        return new NodeAnalysisRunner(BundlePath);
    }

    [SkippableFact]
    public void TheQualityGateGivesOneDeadlockWarning()
    {
        var runner = RealRunner();
        var (exit, output, _) = Run(runner, FindFile("examples/quality-gate.pnml"), "--format", "json");
        Assert.Equal(ExitCodes.Warnings, exit);

        using var json = JsonDocument.Parse(output);
        var findings = json.RootElement.GetProperty("nets")[0].GetProperty("findings").EnumerateArray().ToList();
        var deadlock = Assert.Single(findings, f => f.GetProperty("rule").GetString() == "PT15");
        Assert.True(deadlock.TryGetProperty("marking", out _));
        Assert.Equal(13, findings.Count(f => f.GetProperty("severity").GetString() == "information"));
        Assert.Contains(findings, f => f.GetProperty("rule").GetString() == "PT22");
        Assert.True(json.RootElement.GetProperty("nets")[0].GetProperty("complete").GetBoolean());
    }

    [SkippableFact]
    public void AmlAndPnmlOfTheSameNetGiveTheSameFindings()
    {
        var runner = RealRunner();
        var (_, aml, _) = Run(runner, FindFile("examples/quality-gate.aml"), "--format", "json");
        var (_, pnml, _) = Run(runner, FindFile("examples/quality-gate.pnml"), "--format", "json");

        static List<string> Rules(string text)
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.GetProperty("nets")[0].GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("rule").GetString() + "|" + f.GetProperty("element").GetString() + "|" + f.GetProperty("message").GetString()).OrderBy(x => x).ToList();
        }

        Assert.Equal(Rules(pnml), Rules(aml));
    }

    private static PtNet Workflow(bool sound)
    {
        // In -> A -> Mid -> B -> Out. In the unsound variant B needs two tokens in Mid, and A makes
        // only one: the net gets stuck in Mid.
        var net = new PtNet { Id = "wf", Name = "Workflow" };
        net.Places.Add(new PtPlace { Id = "i", Name = "In", InitialMarking = 1 });
        net.Places.Add(new PtPlace { Id = "m", Name = "Mid" });
        net.Places.Add(new PtPlace { Id = "o", Name = "Out" });
        net.Transitions.Add(new PtTransition { Id = "a", Name = "A" });
        net.Transitions.Add(new PtTransition { Id = "b", Name = "B" });
        net.Arcs.Add(new PtArc { Id = "x1", SourceId = "i", TargetId = "a" });
        net.Arcs.Add(new PtArc { Id = "x2", SourceId = "a", TargetId = "m" });
        net.Arcs.Add(new PtArc { Id = "x3", SourceId = "m", TargetId = "b" });
        net.Arcs.Add(new PtArc { Id = "x4", SourceId = "b", TargetId = "o" });
        if (!sound)
        {
            net.Arcs.First(x => x.Id == "x3").Weight = 2;
        }
        return net;
    }

    [SkippableFact]
    public void ASoundWorkflowNetGivesExitZeroAndIsReportedAsSound()
    {
        var runner = RealRunner();
        var path = PnmlOf(Workflow(sound: true));
        try
        {
            var (exit, output, _) = Run(runner, path, "--format", "json");
            using var json = JsonDocument.Parse(output);
            var net = json.RootElement.GetProperty("nets")[0];
            Assert.True(net.GetProperty("workflow").GetProperty("sound").GetBoolean());
            Assert.Equal("i", net.GetProperty("workflow").GetProperty("start").GetString());
            Assert.Contains("state-machine", net.GetProperty("classes").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(0, net.GetProperty("counts").GetProperty("warning").GetInt32());
            Assert.Equal(ExitCodes.Ok, exit);

            // The end of the process is information, not a deadlock warning.
            Assert.Contains(net.GetProperty("findings").EnumerateArray(),
                f => f.GetProperty("rule").GetString() == "PT15" && f.GetProperty("severity").GetString() == "information");

            var (_, text, _) = Run(runner, path);
            Assert.Contains("workflow net: sound", text);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void AnUnsoundWorkflowNetGivesExitFiveAndSaysSo()
    {
        var runner = RealRunner();
        var path = PnmlOf(Workflow(sound: false));
        try
        {
            var (exit, output, _) = Run(runner, path, "--format", "json");
            using var json = JsonDocument.Parse(output);
            var net = json.RootElement.GetProperty("nets")[0];
            Assert.False(net.GetProperty("workflow").GetProperty("sound").GetBoolean());
            var verdict = Assert.Single(net.GetProperty("findings").EnumerateArray(), f => f.GetProperty("rule").GetString() == "PT25");
            Assert.Equal("warning", verdict.GetProperty("severity").GetString());
            Assert.Equal(ExitCodes.Warnings, exit);

            var (_, text, _) = Run(runner, path);
            Assert.Contains("workflow net: not sound", text);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void AProvenStatementCarriesItsTheoremInTheJson()
    {
        var runner = RealRunner();
        var path = PnmlOf(Loop());
        try
        {
            var (_, output, _) = Run(runner, path, "--format", "json");
            using var json = JsonDocument.Parse(output);
            var live = Assert.Single(json.RootElement.GetProperty("nets")[0].GetProperty("findings").EnumerateArray(),
                f => f.GetProperty("rule").GetString() == "PT23");
            Assert.Equal("state-machine", live.GetProperty("proof").GetString());
            Assert.StartsWith("Proven from the structure", live.GetProperty("message").GetString());
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void ALiveLoopGivesExitZero()
    {
        var runner = RealRunner();
        var path = PnmlOf(Loop());
        try
        {
            var (exit, output, _) = Run(runner, path, "--format", "json");
            using var json = JsonDocument.Parse(output);
            Assert.Equal(0, json.RootElement.GetProperty("counts").GetProperty("warning").GetInt32());
            Assert.Equal(ExitCodes.Ok, exit);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void AGrowingNetWithTightLimitsIsPartialOrUnbounded()
    {
        var runner = RealRunner();
        var net = new PtNet { Id = "grow", Name = "Grow" };
        net.Places.Add(new PtPlace { Id = "stock", Name = "Stock" });
        net.Transitions.Add(new PtTransition { Id = "make", Name = "Make" });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "make", TargetId = "stock" });
        var path = PnmlOf(net);
        try
        {
            var (_, output, _) = Run(runner, path, "--format", "json", "--markings", "50");
            using var json = JsonDocument.Parse(output);
            var rules = json.RootElement.GetProperty("nets")[0].GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("rule").GetString()).ToList();
            Assert.Contains("PT12", rules);
            Assert.Contains("PT17", rules);
            Assert.False(json.RootElement.GetProperty("nets")[0].GetProperty("complete").GetBoolean());
            Assert.Contains("stock", json.RootElement.GetProperty("nets")[0].GetProperty("unbounded").EnumerateArray().Select(x => x.GetString()));

            // The partial result fails the run when asked to, although the search itself finished.
            Assert.Equal(ExitCodes.Warnings, Run(runner, path, "--require-complete", "--fail-on", "error").Exit);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void TheToolWritesUtf8WhateverTheConsoleCodePageIs()
    {
        RealRunner(); // skips where Node is missing
        var net = Loop();
        net.Places[0].Name = "Lager Ü";
        var path = PnmlOf(net);
        try
        {
            var tool = Path.Combine(AppContext.BaseDirectory, "ptmap.dll");
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add(tool);
            foreach (var a in new[] { "analyze", path, "--format", "json", "--all" }) info.ArgumentList.Add(a);

            using var process = Process.Start(info)!;
            using var bytes = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(bytes);
            process.WaitForExit();

            var raw = bytes.ToArray();
            Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "a byte order mark");
            var text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw); // throws on anything else
            Assert.Contains("Lager Ü", text);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void ATimeoutKillsTheProcess()
    {
        var runner = RealRunner();
        // A net too wide to finish in a second: 18 places that each hold a token and
        // each lose it independently, which is 2^18 reachable markings.
        var net = new PtNet { Id = "wide", Name = "Wide" };
        for (var i = 0; i < 18; i++)
        {
            net.Places.Add(new PtPlace { Id = $"p{i}", Name = $"P{i}", InitialMarking = 1 });
            net.Transitions.Add(new PtTransition { Id = $"t{i}", Name = $"T{i}" });
            net.Arcs.Add(new PtArc { Id = $"a{i}", SourceId = $"p{i}", TargetId = $"t{i}" });
        }

        var path = PnmlOf(net);
        try
        {
            var (exit, _, err) = Run(runner, path, "--markings", "100000000", "--timeout", "1");
            Assert.Equal(ExitCodes.Failure, exit);
            Assert.Contains("did not finish", err);

            Assert.NotNull(runner.LastProcessId);
            Thread.Sleep(500);
            // Its own process, by id: other programs may be running Node as well.
            var alive = Process.GetProcesses().Any(p => p.Id == runner.LastProcessId);
            Assert.False(alive, "the analysis process is still running after the timeout");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingBundleGivesExitTwoWithTheInstruction()
    {
        var path = PnmlOf(Loop());
        try
        {
            var (exit, _, err) = Run(new NodeAnalysisRunner(Path.Combine(Path.GetTempPath(), "no-such-bundle.mjs")), path);
            Assert.Equal(ExitCodes.Input, exit);
            Assert.Contains("npm run build", err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WithoutNodeOnThePathTheRunnerSaysSo()
    {
        var path = PnmlOf(Loop());
        var bundle = Path.GetTempFileName();
        try
        {
            var (exit, _, err) = Run(new NodeAnalysisRunner(bundle, "no-such-node-executable"), path);
            Assert.Equal(ExitCodes.Input, exit);
            Assert.Contains("Node.js", err);
        }
        finally { File.Delete(path); File.Delete(bundle); }
    }
}
