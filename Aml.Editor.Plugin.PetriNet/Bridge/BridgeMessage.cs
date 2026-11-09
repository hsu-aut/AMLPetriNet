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

    // host -> modeler
    public const string ImportPnml = "importPNML";
    public const string RequestExport = "requestExport";
    public const string RequestSvg = "requestSvg";
    public const string SetTheme = "setTheme";
    public const string SelectElement = "selectElement";
}
