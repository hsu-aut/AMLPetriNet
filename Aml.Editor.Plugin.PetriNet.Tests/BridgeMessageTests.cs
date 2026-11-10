using System.Text.Json;
using Aml.Editor.Plugin.PetriNet.Bridge;
using Xunit;

namespace Aml.Editor.Plugin.PetriNet.Tests;

/// <summary>
/// The wire between the modeler and the plugin.
///
/// Both ends are written here, which is exactly why this is worth testing: a
/// property renamed on one side and not the other produces no error anywhere. The
/// page side is checked in a browser (web/tools/verify-bridge.mjs); this is the
/// host side, with the same JSON the page posts.
/// </summary>
public class BridgeMessageTests
{
    private static JsToHostMessage Read(string json) =>
        JsonSerializer.Deserialize<JsToHostMessage>(json)
        ?? throw new InvalidOperationException("the message did not deserialise");

    [Fact]
    public void TheSelectionOfTheCanvasIsUnderstood()
    {
        var message = Read("""{"type":"selected","id":"p_waiting","count":1}""");

        Assert.Equal(JsMessageType.Selected, message.Type);
        Assert.Equal("p_waiting", message.Id);
        Assert.Equal(1, message.Count);
    }

    [Fact]
    public void AClearedSelectionIsUnderstoodAsOne()
    {
        var message = Read("""{"type":"selected","id":null,"count":0}""");

        Assert.Equal(JsMessageType.Selected, message.Type);
        Assert.Null(message.Id);
        Assert.Equal(0, message.Count);
    }

    [Fact]
    public void TheTransitionsThatFiredAreUnderstood()
    {
        var message = Read("""{"type":"fired","ids":["t_mill","t_press"]}""");

        Assert.Equal(JsMessageType.Fired, message.Type);
        Assert.Equal(new[] { "t_mill", "t_press" }, message.Ids);
    }

    [Fact]
    public void AStartedRunIsUnderstood()
    {
        var message = Read("""{"type":"simulation","active":true}""");

        Assert.Equal(JsMessageType.Simulation, message.Type);
        Assert.True(message.Active);
    }

    [Fact]
    public void AMessageWithoutTheNewFieldsStillReads()
    {
        // The host and the page are versioned apart: an older bundle in a newer
        // plugin, or the other way round, must not throw.
        var message = Read("""{"type":"changed","pnml":"<pnml/>"}""");

        Assert.Equal(JsMessageType.Changed, message.Type);
        Assert.Equal("<pnml/>", message.Pnml);
        Assert.Null(message.Ids);
        Assert.Null(message.Count);
        Assert.Null(message.Active);
    }

    [Fact]
    public void AFieldOfATypeTheHostDoesNotExpectIsAnError()
    {
        // Not silently ignored: a page that sends ids as a string rather than a
        // list is a defect, and the bridge reports a message it cannot read.
        Assert.Throws<JsonException>(() => Read("""{"type":"fired","ids":"t_mill"}"""));
    }

    [Fact]
    public void EveryTypeTheHostSwitchesOnIsSpelledOnce()
    {
        // The constants are the only spelling of these strings on the host side;
        // this pins them against the page, which spells them in bridge.js.
        Assert.Equal("ready", JsMessageType.Ready);
        Assert.Equal("imported", JsMessageType.Imported);
        Assert.Equal("changed", JsMessageType.Changed);
        Assert.Equal("svg", JsMessageType.Svg);
        Assert.Equal("log", JsMessageType.Log);
        Assert.Equal("error", JsMessageType.Error);
        Assert.Equal("simulation", JsMessageType.Simulation);
        Assert.Equal("selected", JsMessageType.Selected);
        Assert.Equal("fired", JsMessageType.Fired);
        Assert.Equal("importPNML", JsMessageType.ImportPnml);
        Assert.Equal("requestExport", JsMessageType.RequestExport);
        Assert.Equal("requestSvg", JsMessageType.RequestSvg);
        Assert.Equal("setTheme", JsMessageType.SetTheme);
        Assert.Equal("selectElement", JsMessageType.SelectElement);
    }
}
