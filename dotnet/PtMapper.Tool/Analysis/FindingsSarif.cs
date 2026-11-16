using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// The report as SARIF 2.1.0, the format GitHub reads as code scanning alerts. GitHub shows a
/// location in a file and nothing else, so every result points at line 1 of the file and names
/// the element in the message and as a logical location.
/// </summary>
public static class FindingsSarif
{
    private const string Help = "https://github.com/hsu-aut/AMLPetriNet#checks";

    public static string Write(AnalyzeReport report, string fileUri, bool all, string toolVersion)
    {
        var rules = RuleCatalog.For(report);
        var index = rules.Select((rule, i) => (rule.Id, i)).ToDictionary(p => p.Id, p => p.i);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            json.WriteString("version", "2.1.0");
            json.WriteStartArray("runs");
            json.WriteStartObject();

            json.WriteStartObject("tool");
            json.WriteStartObject("driver");
            json.WriteString("name", "ptmap");
            json.WriteString("version", toolVersion);
            json.WriteString("informationUri", "https://github.com/hsu-aut/AMLPetriNet");
            json.WriteStartArray("rules");
            foreach (var rule in rules)
            {
                json.WriteStartObject();
                json.WriteString("id", rule.Id);
                json.WriteString("name", rule.Id);
                json.WriteStartObject("shortDescription"); json.WriteString("text", rule.Title); json.WriteEndObject();
                json.WriteStartObject("fullDescription"); json.WriteString("text", rule.Title); json.WriteEndObject();
                json.WriteStartObject("help");
                json.WriteString("text", $"{rule.Title}. The checks are described in the README of AMLPetriNet, section Checks.");
                json.WriteString("markdown", $"{rule.Title}. See [Checks]({Help}).");
                json.WriteEndObject();
                json.WriteString("helpUri", Help);
                json.WriteStartObject("properties");
                json.WriteStartArray("tags"); json.WriteStringValue("petri-net"); json.WriteStringValue(rule.Tag); json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteStartObject("automationDetails");
            json.WriteString("id", $"ptmap/{fileUri}/");
            json.WriteEndObject();

            json.WriteStartArray("results");
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var net in report.Nets)
            foreach (var finding in net.Findings)
            {
                if (!all && finding.Severity == "information") continue;

                var location = RuleCatalog.Location(net, finding.Element);
                json.WriteStartObject();
                json.WriteString("ruleId", finding.Rule);
                json.WriteNumber("ruleIndex", index[finding.Rule]);
                json.WriteString("level", finding.Severity switch { "error" => "error", "warning" => "warning", _ => "note" });
                json.WriteStartObject("message");
                json.WriteString("text", $"{finding.Message} [{location}]");
                json.WriteEndObject();

                json.WriteStartArray("locations");
                json.WriteStartObject();
                json.WriteStartObject("physicalLocation");
                json.WriteStartObject("artifactLocation");
                json.WriteString("uri", fileUri);
                if (!fileUri.StartsWith("file:", StringComparison.Ordinal)) json.WriteString("uriBaseId", "%SRCROOT%");
                json.WriteEndObject();
                json.WriteStartObject("region"); json.WriteNumber("startLine", 1); json.WriteEndObject();
                json.WriteEndObject();
                json.WriteStartArray("logicalLocations");
                json.WriteStartObject();
                json.WriteString("name", finding.Element ?? net.Id);
                json.WriteString("fullyQualifiedName", location);
                json.WriteString("kind", "element");
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndArray();

                var identity = Identity(net, finding);
                var twin = seen.TryGetValue(identity, out var n) ? n + 1 : 1;
                seen[identity] = twin;
                json.WriteStartObject("partialFingerprints");
                json.WriteString("primaryLocationLineHash", $"{Hash(identity)}:{twin}");
                json.WriteEndObject();

                json.WriteStartObject("properties");
                json.WriteString("net", net.Id);
                if (net.Hierarchy != null) json.WriteString("hierarchy", net.Hierarchy);
                if (finding.Element != null) json.WriteString("element", finding.Element);
                if (finding.InternalElementId != null) json.WriteString("internalElementId", finding.InternalElementId);
                if (finding.Marking != null) json.WriteString("marking", finding.Marking);
                if (finding.Cover is { } cover) { json.WritePropertyName("cover"); cover.WriteTo(json); }
                if (finding.Firings is { } firings) { json.WritePropertyName("firings"); firings.WriteTo(json); }
                if (finding.Proof != null) json.WriteString("proof", finding.Proof);
                json.WriteEndObject();

                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return new UTF8Encoding(false).GetString(stream.ToArray()) + "\n";
    }

    /// <summary>What makes a finding the same finding: not its wording, not where the file is, not a version.</summary>
    private static string Identity(NetReport net, ReportedFinding finding)
    {
        var cover = finding.Cover is { ValueKind: JsonValueKind.Array } array
            ? string.Join(",", array.EnumerateArray().Select(item => item.GetRawText()).OrderBy(text => text, StringComparer.Ordinal))
            : "";
        return string.Join("|", "ptmap/1", finding.Rule, net.Hierarchy ?? "", net.Id, finding.Element ?? "", finding.Marking ?? "", cover);
    }

    private static string Hash(string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..16];

    /// <summary>
    /// The file as GitHub wants it: relative to the repository, with forward slashes. A path inside the
    /// current directory is made relative; any other becomes a file URI.
    /// </summary>
    public static string UriOf(string path, string currentDirectory)
    {
        var full = Path.GetFullPath(path, currentDirectory);
        var root = Path.GetFullPath(currentDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

        if (full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            var relative = full[root.Length..].Replace(Path.DirectorySeparatorChar, '/');
            return string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
        }

        return new Uri(full).AbsoluteUri;
    }
}
