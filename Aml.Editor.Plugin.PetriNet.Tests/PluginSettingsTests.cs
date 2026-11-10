using System.Text.Json;
using Aml.Editor.Plugin.PetriNet.Diagnostics;
using Xunit;

namespace Aml.Editor.Plugin.PetriNet.Tests;

/// <summary>
/// The settings are written to disk as JSON and read back on the next start, so
/// a default and a property name are both part of the behaviour: a renamed
/// property silently resets a user's choice to its default.
/// </summary>
public class PluginSettingsTests
{
    [Fact]
    public void FollowingTheCanvasInTheTreeIsOnUnlessItWasTurnedOff()
    {
        Assert.True(new PluginSettings().FollowSelection);

        var off = JsonSerializer.Deserialize<PluginSettings>("""{"follow_selection":false}""");
        Assert.False(off!.FollowSelection);
    }

    [Fact]
    public void ASettingsFileFromAnOlderVersionKeepsTheDefaults()
    {
        // What a user upgrading from 0.1.16 has on disk: no follow_selection.
        var stored = JsonSerializer.Deserialize<PluginSettings>(
            """{"save_after_update":false,"ask_before_large_updates":true,"large_update_threshold":9,"debug_logging":true}""");

        Assert.NotNull(stored);
        Assert.False(stored!.SaveAfterUpdate);
        Assert.Equal(9, stored.LargeUpdateThreshold);
        Assert.True(stored.DebugLogging);
        // The new setting is the default, not false, which is what an unset
        // boolean would be if it were read out of the file.
        Assert.True(stored.FollowSelection);
    }

    [Fact]
    public void EverySettingSurvivesBeingWrittenAndReadBack()
    {
        var settings = new PluginSettings
        {
            SaveAfterUpdate = false,
            AskBeforeLargeUpdates = false,
            LargeUpdateThreshold = 42,
            FollowSelection = false,
            DebugLogging = true,
        };

        var again = JsonSerializer.Deserialize<PluginSettings>(JsonSerializer.Serialize(settings))!;

        Assert.False(again.SaveAfterUpdate);
        Assert.False(again.AskBeforeLargeUpdates);
        Assert.Equal(42, again.LargeUpdateThreshold);
        Assert.False(again.FollowSelection);
        Assert.True(again.DebugLogging);
    }
}
