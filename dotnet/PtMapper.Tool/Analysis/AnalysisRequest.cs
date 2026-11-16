using System.Text;
using System.Text.Json;
using PtMapper.Conversion.Models;

namespace PtMapper.Tool.Analysis;

/// <summary>
/// The request the analysis bundle reads on stdin: the nets as plain data and the
/// limits. The shape is documented in web/src/analysis.js.
/// </summary>
public static class AnalysisRequest
{
    public static string Build(IReadOnlyList<PtNet> nets, int markings, int? timeLimitMilliseconds, string kind = "analyse", string? format = null)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("kind", kind);
            if (format != null) json.WriteString("format", format);

            json.WriteStartObject("limits");
            json.WriteNumber("markings", markings);
            if (timeLimitMilliseconds is { } ms) json.WriteNumber("milliseconds", ms);
            else json.WriteNull("milliseconds");
            json.WriteEndObject();

            json.WriteStartArray("nets");
            for (var i = 0; i < nets.Count; i++) WriteNet(json, i.ToString(System.Globalization.CultureInfo.InvariantCulture), nets[i]);
            json.WriteEndArray();

            json.WriteEndObject();
        }

        // Without a byte order mark: the reader on the other side does not expect one.
        return new UTF8Encoding(false).GetString(stream.ToArray());
    }

    private static void WriteNet(Utf8JsonWriter json, string key, PtNet net)
    {
        json.WriteStartObject();
        json.WriteString("key", key);

        json.WriteStartArray("places");
        foreach (var place in net.Places)
        {
            json.WriteStartObject();
            json.WriteString("id", place.Id);
            json.WriteString("name", place.Name ?? "");
            json.WriteNumber("marking", place.InitialMarking);
            json.WriteEndObject();
        }
        json.WriteEndArray();

        json.WriteStartArray("transitions");
        foreach (var transition in net.Transitions)
        {
            json.WriteStartObject();
            json.WriteString("id", transition.Id);
            json.WriteString("name", transition.Name ?? "");
            json.WriteEndObject();
        }
        json.WriteEndArray();

        json.WriteStartArray("arcs");
        foreach (var arc in net.Arcs)
        {
            json.WriteStartObject();
            json.WriteString("id", arc.Id);
            json.WriteString("source", arc.SourceId);
            json.WriteString("target", arc.TargetId);
            json.WriteNumber("weight", arc.Weight);
            json.WriteEndObject();
        }
        json.WriteEndArray();

        json.WriteEndObject();
    }
}
