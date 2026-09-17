using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Aml.Editor.Plugin.PetriNet.Diagnostics;

/// <summary>
/// A handful of assertions run at plugin construction, so that the first thing
/// in the log says whether this build can work at all in this editor.
///
/// The failures worth catching here are the ones that otherwise surface much
/// later as something unhelpful: a serializer that cannot load shows up as an
/// exception on the first message from the canvas, and missing web assets show
/// up as a blank panel. Both are five-second fixes once named, and a long
/// puzzle otherwise.
/// </summary>
public static class StartupCheck
{
    public sealed record Result(string What, bool Ok, string Detail);

    public static List<Result> Run()
    {
        return new List<Result>
        {
            CheckJson(),
            CheckAssets(),
            CheckAssembly("Aml.Engine"),
            CheckAssembly("Microsoft.Web.WebView2.Wpf"),
        };
    }

    /// <summary>
    /// System.Text.Json is not packed with the plugin (the FPD plugin does the
    /// same and works, because the editor process already provides it). If that
    /// assumption ever breaks, every bridge message would fail; better to say so
    /// once at startup.
    /// </summary>
    private static Result CheckJson()
    {
        try
        {
            var round = JsonSerializer.Deserialize<Dictionary<string, string>>(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["type"] = "ready" }));

            var version = typeof(JsonSerializer).Assembly.GetName().Version?.ToString() ?? "?";
            return round?["type"] == "ready"
                ? new Result("System.Text.Json", true, "v" + version)
                : new Result("System.Text.Json", false, "round trip returned unexpected data");
        }
        catch (Exception ex)
        {
            return new Result("System.Text.Json", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Result CheckAssets()
    {
        try
        {
            var location = typeof(StartupCheck).Assembly.Location;
            var dir = Path.Combine(
                (string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location)) ?? AppContext.BaseDirectory,
                "ptnjs-assets");

            var missing = new[] { "index.html", "ptnjs.esm.js", "ptnjs.css" }
                .Where(f => !File.Exists(Path.Combine(dir, f)))
                .ToList();

            return missing.Count == 0
                ? new Result("Modeler assets", true, dir)
                : new Result("Modeler assets", false, "missing in " + dir + ": " + string.Join(", ", missing));
        }
        catch (Exception ex)
        {
            return new Result("Modeler assets", false, ex.Message);
        }
    }

    private static Result CheckAssembly(string name)
    {
        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

            if (loaded != null)
                return new Result(name, true, "v" + loaded.GetName().Version);

            // Not loaded yet is not a problem by itself; it will be pulled in on
            // first use. Only report what is known.
            var reference = Assembly.Load(new AssemblyName(name));
            return new Result(name, true, "v" + reference.GetName().Version + " (loaded on demand)");
        }
        catch (Exception ex)
        {
            return new Result(name, false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static string Format(IEnumerable<Result> results)
    {
        var lines = results
            .Select(r => $"  [{(r.Ok ? "ok" : "FAIL")}] {r.What}: {r.Detail}");
        return "Startup check:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
