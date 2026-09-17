// What the user decided, kept between sessions as JSON under
//   %APPDATA%\AutomationMLEditor\PetriNetPlugin\settings.json
//
// Deliberately small. A file that cannot be read is replaced by the defaults
// rather than reported: a preference is not worth an error dialog.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aml.Editor.Plugin.PetriNet.Diagnostics;

public sealed class PluginSettings
{
    /// <summary>
    /// Whether an Update also triggers the editor's save command. On by
    /// default, because an update that is not saved is lost when the editor
    /// closes, and the plugin cannot tell the editor that the document changed.
    /// </summary>
    [JsonPropertyName("save_after_update")] public bool SaveAfterUpdate { get; set; } = true;

    /// <summary>
    /// Whether to ask before an update that adds or removes more elements than
    /// <see cref="LargeUpdateThreshold"/>. What it guards against is a diagram
    /// that is not the one the document holds, for instance after the canvas
    /// was reloaded with another net: applying it would delete the difference.
    /// </summary>
    [JsonPropertyName("ask_before_large_updates")] public bool AskBeforeLargeUpdates { get; set; } = true;

    [JsonPropertyName("large_update_threshold")] public int LargeUpdateThreshold { get; set; } = 5;

    [JsonPropertyName("debug_logging")] public bool DebugLogging { get; set; }

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutomationMLEditor", "PetriNetPlugin", "settings.json");

    public static PluginSettings Load()
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath), Json) ?? new PluginSettings()
                    : new PluginSettings();
            }
            catch
            {
                return new PluginSettings();
            }
        }
    }

    /// <summary>Best effort: a preference that cannot be written is not worth failing over.</summary>
    public void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch (Exception ex)
            {
                PluginLog.Debug("Could not save the settings: " + ex.Message);
            }
        }
    }
}
