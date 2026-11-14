using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// Runs the analysis bundle (ptmap-analysis.mjs, built from web/src/analysis.js)
/// in a Node process, once per request. The core that answers is the one the
/// canvas uses, so the two cannot disagree.
/// </summary>
public sealed class NodeAnalysisRunner : IAnalysisRunner
{
    public const int MinimumNodeVersion = 20;

    /// <summary>The id of the analysis process the last run started, so that a test can check it is gone.</summary>
    public int? LastProcessId { get; private set; }

    private readonly string _bundle;
    private readonly string _node;

    public NodeAnalysisRunner(string? bundlePath = null, string node = "node")
    {
        _bundle = bundlePath ?? Path.Combine(AppContext.BaseDirectory, "ptmap-analysis.mjs");
        _node = node;
    }

    public string Run(string requestJson, int timeoutSeconds)
    {
        if (!File.Exists(_bundle))
        {
            throw new AnalysisUnavailableException(
                $"The analysis bundle '{_bundle}' is missing. Run 'npm install && npm run build' in web/, then build ptmap again.");
        }

        CheckNode();

        var process = Start(_bundle);
        LastProcessId = process.Id;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            process.StandardInput.Write(requestJson);
            process.StandardInput.Close();

            if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                Kill(process);
                throw new AnalysisFailedException($"The analysis did not finish within {timeoutSeconds} s and was stopped.");
            }

            process.WaitForExit(); // lets the output readers finish
            if (process.ExitCode != 0)
            {
                throw new AnalysisFailedException(
                    $"The analysis ended with status {process.ExitCode}: {errors.GetAwaiter().GetResult().Trim()}");
            }

            return output.GetAwaiter().GetResult();
        }
        finally
        {
            if (!process.HasExited) Kill(process);
            process.Dispose();
        }
    }

    private void CheckNode()
    {
        string version;
        try
        {
            using var process = Start("--version");
            version = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(TimeSpan.FromSeconds(20)))
            {
                Kill(process);
                throw new AnalysisUnavailableException("'node --version' did not answer.");
            }
        }
        catch (Win32Exception)
        {
            throw new AnalysisUnavailableException(
                $"'analyze' needs Node.js {MinimumNodeVersion} or later and '{_node}' was not found on the PATH.");
        }

        // "v20.19.0"
        var major = version.TrimStart('v').Split('.')[0];
        if (!int.TryParse(major, out var number) || number < MinimumNodeVersion)
        {
            throw new AnalysisUnavailableException(
                $"'analyze' needs Node.js {MinimumNodeVersion} or later; found '{version}'.");
        }
    }

    private Process Start(string argument)
    {
        var info = new ProcessStartInfo(_node)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Set explicitly: the console's code page is not UTF-8 on every machine.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new Win32Exception("the process did not start");
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* it ended meanwhile */ }
    }
}
