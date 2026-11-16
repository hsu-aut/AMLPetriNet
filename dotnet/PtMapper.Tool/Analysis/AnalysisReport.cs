using System.Text.Json;
using System.Text.Json.Serialization;

namespace PtMapper.Tool.Analysis;

// What the bundle answers. Only what ptmap needs is read; the rest is passed on.

public sealed class CoreAnswer
{
    [JsonPropertyName("core")] public string Core { get; set; } = "unknown";
    [JsonPropertyName("results")] public List<CoreNetAnswer> Results { get; set; } = new();

    /// <summary>The titles of the rules the core knows, by id. Empty from a bundle that predates them.</summary>
    [JsonPropertyName("rules")] public Dictionary<string, string>? Rules { get; set; } = new();
}

public sealed class CoreNetAnswer
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("problem")] public string? Problem { get; set; }
    [JsonPropertyName("complete")] public bool Complete { get; set; }
    [JsonPropertyName("stopped")] public string? Stopped { get; set; }
    [JsonPropertyName("markings")] public int Markings { get; set; }
    [JsonPropertyName("unbounded")] public List<string> Unbounded { get; set; } = new();
    [JsonPropertyName("classes")] public List<string> Classes { get; set; } = new();
    [JsonPropertyName("workflow")] public CoreWorkflow? Workflow { get; set; }
    [JsonPropertyName("findings")] public List<CoreFinding> Findings { get; set; } = new();
}

/// <summary>The start and end of a workflow net, and whether it is sound: true, false, or null when not decided.</summary>
public sealed class CoreWorkflow
{
    [JsonPropertyName("start")] public string Start { get; set; } = "";
    [JsonPropertyName("end")] public string End { get; set; } = "";
    [JsonPropertyName("sound")] public bool? Sound { get; set; }
}

public sealed class CoreFinding
{
    [JsonPropertyName("rule")] public string Rule { get; set; } = "";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "information";
    [JsonPropertyName("element")] public string? Element { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("marking")] public string? Marking { get; set; }
    [JsonPropertyName("cover")] public JsonElement? Cover { get; set; }
    [JsonPropertyName("firings")] public JsonElement? Firings { get; set; }

    /// <summary>The theorem a statement rests on, where it was proven from the structure.</summary>
    [JsonPropertyName("proof")] public string? Proof { get; set; }
}

// What ptmap reports. The names are the format; they are written out so that a
// rename of a property in C# cannot change what a CI job reads.

public sealed record ReportedFinding(
    [property: JsonPropertyName("rule")] string Rule,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("element")] string? Element,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("internalElementId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InternalElementId = null,
    [property: JsonPropertyName("marking"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Marking = null,
    [property: JsonPropertyName("cover"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Cover = null,
    [property: JsonPropertyName("firings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Firings = null,
    [property: JsonPropertyName("proof"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Proof = null);

public sealed record WorkflowReport(
    [property: JsonPropertyName("start")] string Start,
    [property: JsonPropertyName("end")] string End,
    [property: JsonPropertyName("sound")] bool? Sound);

public sealed record Counts(
    [property: JsonPropertyName("error")] int Error,
    [property: JsonPropertyName("warning")] int Warning,
    [property: JsonPropertyName("information")] int Information);

public sealed record NetReport(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("hierarchy")] string? Hierarchy,
    [property: JsonPropertyName("analysed")] bool Analysed,
    [property: JsonPropertyName("problem")] string? Problem,
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("stopped")] string? Stopped,
    [property: JsonPropertyName("markings")] int Markings,
    [property: JsonPropertyName("unbounded")] IReadOnlyList<string> Unbounded,
    [property: JsonPropertyName("classes")] IReadOnlyList<string> Classes,
    [property: JsonPropertyName("workflow")] WorkflowReport? Workflow,
    [property: JsonPropertyName("counts")] Counts Counts,
    [property: JsonPropertyName("findings")] IReadOnlyList<ReportedFinding> Findings);

public sealed record ReportLimits(
    [property: JsonPropertyName("markings")] int Markings,
    [property: JsonPropertyName("milliseconds")] int? Milliseconds);

public sealed record AnalyzeReport(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("core")] string Core,
    [property: JsonPropertyName("limits")] ReportLimits Limits,
    [property: JsonPropertyName("nets")] IReadOnlyList<NetReport> Nets,
    [property: JsonPropertyName("counts")] Counts Counts,
    [property: JsonPropertyName("exit")] int Exit)
{
    /// <summary>The titles of the behavioural rules from the core; not part of the JSON report.</summary>
    [JsonIgnore] public IReadOnlyDictionary<string, string> Rules { get; init; } = new Dictionary<string, string>();
}
