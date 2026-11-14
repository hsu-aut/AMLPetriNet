using System.Text.Json.Serialization;

namespace Aml.Editor.Plugin.PetriNet.Bridge;

/// <summary>
/// Messages received from the embedded modeler. Mirrors what
/// web/src/bridge.js posts. The payload is PNML text rather than a JSON
/// document, which is the one real difference to the FPD plugin's bridge.
/// </summary>
public class JsToHostMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("level")] public string? Level { get; set; }

    /// <summary>PNML document for "changed" and "imported" messages.</summary>
    [JsonPropertyName("pnml")] public string? Pnml { get; set; }

    /// <summary>The diagram as a picture, for "svg" messages.</summary>
    [JsonPropertyName("svg")] public string? Svg { get; set; }

    /// <summary>Whether a simulation is running, for "simulation" messages.</summary>
    [JsonPropertyName("active")] public bool? Active { get; set; }

    /// <summary>The transitions that fired, for "fired" messages.</summary>
    [JsonPropertyName("ids")] public string[]? Ids { get; set; }

    /// <summary>How many elements are selected, for "selected" messages.</summary>
    [JsonPropertyName("count")] public int? Count { get; set; }

    /// <summary>
    /// Why a start was refused or a run ended by itself, for "simulation"
    /// messages, or why a net could not be analysed, for "analysed" ones.
    /// </summary>
    [JsonPropertyName("problem")] public string? Problem { get; set; }

    /// <summary>What an analysis computed, for "analysed" messages.</summary>
    [JsonPropertyName("errors")] public int? Errors { get; set; }
    [JsonPropertyName("warnings")] public int? Warnings { get; set; }
    [JsonPropertyName("information")] public int? Information { get; set; }
}

/// <summary>Type tags on the wire, kept as constants rather than loose strings.</summary>
public static class JsMessageType
{
    // modeler -> host
    public const string Ready = "ready";
    public const string Imported = "imported";
    public const string Changed = "changed";
    public const string Error = "error";
    public const string Log = "log";
    public const string Svg = "svg";
    public const string Simulation = "simulation";
    public const string Selected = "selected";
    public const string Fired = "fired";
    public const string Analysed = "analysed";

    // host -> modeler
    public const string ImportPnml = "importPNML";
    public const string RequestExport = "requestExport";
    public const string RequestSvg = "requestSvg";
    public const string SetTheme = "setTheme";
    public const string SelectElement = "selectElement";
    public const string HostFindings = "hostFindings";
    public const string Simulate = "simulate";
    public const string Analyse = "analyse";
}

/// <summary>
/// One finding of the plugin's own check, as the page lists it. Kept apart from
/// the mapper's finding type so that this assembly's wire format does not
/// depend on it: the page reads the strings below and nothing else.
/// </summary>
/// <param name="Rule">The rule, for instance PT05.</param>
/// <param name="Severity">"error", "warning" or "information", in any case.</param>
/// <param name="Element">The PNML id of the element it is about, or null.</param>
/// <param name="Message">What is wrong, as a sentence.</param>
public sealed record HostFinding(string Rule, string Severity, string? Element, string Message);

/// <summary>
/// The message that hands the findings of the plugin's check to the page, which
/// lists them with the ones the modeler computes. Mirrors web/src/bridge.js.
/// </summary>
public static class HostFindingsMessage
{
    public static object Create(IEnumerable<HostFinding> findings) => new
    {
        type = JsMessageType.HostFindings,
        findings = findings
            .Select(f => new
            {
                rule = f.Rule,
                severity = f.Severity.ToLowerInvariant(),
                element = string.IsNullOrEmpty(f.Element) ? null : f.Element,
                message = f.Message,
            })
            .ToArray(),
    };
}

/// <summary>
/// Starts or ends a run on the canvas, as the Simulate button there does. The
/// page answers every request with one "simulation" message.
/// </summary>
public static class SimulateMessage
{
    public static object Create(bool active) => new { type = JsMessageType.Simulate, active };
}

/// <summary>
/// Asks the page to analyse the net and open the list; answered by "analysed".
/// </summary>
public static class AnalyseMessage
{
    public static object Create() => new { type = JsMessageType.Analyse };
}
