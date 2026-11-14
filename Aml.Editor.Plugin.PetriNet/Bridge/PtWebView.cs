using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Aml.Editor.Plugin.PetriNet.Bridge;

/// <summary>
/// Lifecycle and messaging wrapper around the WebView2 control that hosts the
/// Petri net modeler. The control itself belongs to the XAML; this class owns
/// the bridge.
///
/// Follows the FPD plugin's FpbWebView closely, including the hard-won parts:
/// readiness is only asserted when the page says so, a reload or renderer crash
/// drops back out of it, and pushes that arrive early are buffered.
/// </summary>
public sealed class PtWebView : IDisposable
{
    private const string VirtualHost = "ptnjs.local";
    private const string AssetsFolder = "ptnjs-assets";

    private readonly WebView2 _view;

    private bool _navStarted;
    private bool _ready;
    private string? _pendingPnml;
    private TaskCompletionSource<string?>? _svgRequest;
    private string? _pendingTheme;

    // The findings last handed over. Kept, not only buffered: a page that is
    // loaded again after a crash starts with an empty list, and the check does
    // not run again by itself.
    private IReadOnlyList<HostFinding>? _hostFindings;
    private bool _disposed;

    private EventHandler<CoreWebView2WebMessageReceivedEventArgs>? _webMessageHandler;
    private EventHandler<CoreWebView2NavigationCompletedEventArgs>? _navigationHandler;
    private EventHandler<CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
    private EventHandler<CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;

    public event Action? Ready;
    public event Action<string>? OnError;
    public event Action<string>? OnInfo;

    /// <summary>A user edit: the payload is the modeler's current PNML.</summary>
    public event Action<string>? OnDiagramChanged;

    /// <summary>
    /// Acknowledgement of an import, carrying the modeler's own export of what
    /// it just loaded. The host keeps it as the echo baseline: a later change
    /// with identical content is import fallout, not an edit.
    /// </summary>
    public event Action<string?>? OnImported;

    public event Action<string, string>? OnJsLog;

    /// <summary>A simulation started (true) or stopped (false) on the canvas.</summary>
    public event Action<bool>? SimulationChanged;

    /// <summary>
    /// What the user selected on the canvas, by its PNML id, or null where the
    /// selection was cleared. The counterpart of SelectElement, which points
    /// the canvas at an element the host picked.
    /// </summary>
    public event Action<string?>? ElementSelected;

    /// <summary>The transitions that just fired, while a simulation is running.</summary>
    public event Action<string[]>? TransitionsFired;

    public PtWebView(WebView2 view) => _view = view;

    public bool IsReady => _ready;

    /// <summary>Whether a net is waiting for the page to boot. The host checks this before pushing another.</summary>
    public bool HasPendingImport => _pendingPnml != null;

    /// <summary>
    /// Load the page again. The host's Ready handler then puts the right net
    /// back. Used after a renderer crash, where the tab would otherwise stay
    /// blank with nothing to click.
    /// </summary>
    public void Reload()
    {
        if (_disposed || _view.CoreWebView2 == null) return;
        try
        {
            _view.CoreWebView2.Reload();
        }
        catch (Exception ex)
        {
            ReportError("Reload failed: " + ex.Message);
        }
    }

    public async Task InitAsync()
    {
        if (_navStarted || _disposed) return;

        try
        {
            await _view.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            ReportError("WebView2 runtime missing or failed to start: " + ex.Message);
            return;
        }

        // The await above can complete after teardown; touching Settings on a
        // disposed control throws.
        if (_disposed) return;

        var settings = _view.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
        settings.IsStatusBarEnabled = false;

        var assets = ResolveAssetsPath();
        OnInfo?.Invoke("Assets path: " + assets);

        if (!Directory.Exists(assets))
        {
            ReportError($"{AssetsFolder} folder not found at: {assets}");
            return;
        }
        if (!File.Exists(Path.Combine(assets, "index.html")))
        {
            ReportError("index.html missing in assets folder: " + assets);
            return;
        }
        if (!File.Exists(Path.Combine(assets, "ptnjs.esm.js")))
        {
            ReportError("ptnjs.esm.js missing. Was 'npm run build' run in web/ before packing?");
            return;
        }

        _view.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

        _webMessageHandler = (_, e) => OnWebMessage(e);
        _navigationHandler = (_, e) =>
        {
            if (!e.IsSuccess)
            {
                _ready = false;
                ReportError($"Navigation failed: WebErrorStatus={e.WebErrorStatus}");
            }
        };
        // A fresh navigation (first load, or F5 from the context menu) tears
        // down the page's message listener; anything posted before the new page
        // announces itself would be dropped.
        _navigationStartingHandler = (_, __) => _ready = false;
        // A renderer crash otherwise leaves a blank tab with _ready still true,
        // and every later push vanishes without a trace.
        _processFailedHandler = (_, e) =>
        {
            _ready = false;
            ReportError($"WebView2 process failed ({e.ProcessFailedKind}); reloading the page.");
            // Only the renderer can be brought back by a reload; a dead browser
            // process needs the control recreated, which is the editor's call.
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                _view.Dispatcher.BeginInvoke(Reload);
            }
        };

        if (_disposed) return;
        _view.CoreWebView2.WebMessageReceived += _webMessageHandler;
        _view.CoreWebView2.NavigationCompleted += _navigationHandler;
        _view.CoreWebView2.NavigationStarting += _navigationStartingHandler;
        _view.CoreWebView2.ProcessFailed += _processFailedHandler;

        _navStarted = true;
        _view.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
    }

    private static string ResolveAssetsPath()
    {
        var location = typeof(PtWebView).Assembly.Location;
        var pluginDir = !string.IsNullOrEmpty(location) ? Path.GetDirectoryName(location) : null;
        return Path.Combine(pluginDir ?? AppContext.BaseDirectory, AssetsFolder);
    }

    /// <summary>
    /// Push a PNML document into the modeler. Returns true when it actually went
    /// out, false when it was only buffered: callers use that to decide whether
    /// to arm echo suppression, because a buffered push posts later.
    /// </summary>
    public bool ImportPnml(string pnml)
    {
        if (string.IsNullOrEmpty(pnml) || _disposed) return false;

        if (!_ready || _view.CoreWebView2 == null)
        {
            _pendingPnml = pnml;
            return false;
        }

        Post(new { type = JsMessageType.ImportPnml, pnml });
        return true;
    }

    /// <summary>Ask the modeler to send its current PNML back.</summary>
    public void RequestExport()
    {
        if (_disposed || !_ready || _view.CoreWebView2 == null) return;
        Post(new { type = JsMessageType.RequestExport });
    }

    /// <summary>
    /// The diagram as an SVG picture, as the modeler draws it right now.
    ///
    /// Unlike the PNML export this is a question with an answer, so the caller
    /// awaits it instead of subscribing. Null when the page is not there, or
    /// when it does not answer in time: a dialog waiting forever on a page that
    /// crashed would look like the editor hanging.
    /// </summary>
    public async Task<string?> RequestSvgAsync(TimeSpan timeout)
    {
        if (_disposed || !_ready || _view.CoreWebView2 == null) return null;

        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _svgRequest = answer;
        try
        {
            Post(new { type = JsMessageType.RequestSvg });
            var finished = await Task.WhenAny(answer.Task, Task.Delay(timeout)).ConfigureAwait(true);
            return finished == answer.Task ? await answer.Task.ConfigureAwait(true) : null;
        }
        finally
        {
            _svgRequest = null;
        }
    }

    public void SendTheme(string theme)
    {
        if (string.IsNullOrEmpty(theme) || _disposed) return;

        if (!_ready || _view.CoreWebView2 == null)
        {
            _pendingTheme = theme;
            return;
        }

        Post(new { type = JsMessageType.SetTheme, theme });
    }

    /// <summary>
    /// Hand the findings of the plugin's own check to the page, which lists them
    /// with the ones it computes. They stay there until this is called again;
    /// an empty list clears them. Sent again when the page is loaded anew.
    /// </summary>
    public void SendHostFindings(IReadOnlyList<HostFinding> findings)
    {
        if (_disposed) return;

        // An empty list over an empty list says nothing; the check clears on
        // every load, and most loads follow a net that had nothing to report.
        var unchanged = findings.Count == 0 && (_hostFindings is null || _hostFindings.Count == 0);

        _hostFindings = findings;
        if (unchanged || !_ready || _view.CoreWebView2 == null) return;

        Post(HostFindingsMessage.Create(findings));
    }

    /// <summary>Select an element and scroll it into view. A missed select is recoverable.</summary>
    public void SelectElement(string id)
    {
        if (string.IsNullOrEmpty(id) || _disposed || !_ready || _view.CoreWebView2 == null) return;
        Post(new { type = JsMessageType.SelectElement, id });
    }

    private void Post(object envelope) =>
        _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(envelope));

    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed) return;

        JsToHostMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<JsToHostMessage>(e.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            ReportError("Failed to parse a message from the modeler: " + ex.Message);
            return;
        }
        if (message is null) return;

        switch (message.Type)
        {
            case JsMessageType.Ready:
                _ready = true;
                if (!string.IsNullOrEmpty(message.Url)) OnInfo?.Invoke("Modeler booted at " + message.Url);
                Ready?.Invoke();
                FlushPending();
                break;

            case JsMessageType.Changed:
                if (!string.IsNullOrEmpty(message.Pnml)) OnDiagramChanged?.Invoke(message.Pnml!);
                break;

            case JsMessageType.Imported:
                OnImported?.Invoke(message.Pnml);
                break;

            case JsMessageType.Svg:
                _svgRequest?.TrySetResult(message.Svg);
                break;

            case JsMessageType.Error:
                ReportError(message.Message ?? "(unspecified)");
                break;

            case JsMessageType.Log:
                OnJsLog?.Invoke(message.Level ?? "log", message.Message ?? "");
                break;

            case JsMessageType.Simulation:
                // A simulation never writes into the model, so the host has
                // nothing to do about it. It is reported because a user who
                // wonders why the canvas shows other markings than the
                // document finds the answer in the log.
                SimulationChanged?.Invoke(message.Active ?? false);
                OnInfo?.Invoke(message.Active == true ? "Simulation started" : "Simulation stopped");
                break;

            case JsMessageType.Selected:
                ElementSelected?.Invoke(string.IsNullOrEmpty(message.Id) ? null : message.Id);
                break;

            case JsMessageType.Fired:
                TransitionsFired?.Invoke(message.Ids ?? Array.Empty<string>());
                break;

            default:
                ReportError($"Unknown message type from the modeler: '{message.Type}'.");
                break;
        }
    }

    private void FlushPending()
    {
        if (_pendingPnml is { } pnml)
        {
            _pendingPnml = null;
            ImportPnml(pnml);
        }
        if (_pendingTheme is { } theme)
        {
            _pendingTheme = null;
            SendTheme(theme);
        }
        if (_hostFindings is { } findings) Post(HostFindingsMessage.Create(findings));
    }

    private void ReportError(string message)
    {
        if (_view.Dispatcher.CheckAccess()) OnError?.Invoke(message);
        else _view.Dispatcher.Invoke(() => OnError?.Invoke(message));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            var core = _view?.CoreWebView2;
            if (core != null)
            {
                if (_webMessageHandler != null) core.WebMessageReceived -= _webMessageHandler;
                if (_navigationHandler != null) core.NavigationCompleted -= _navigationHandler;
                if (_navigationStartingHandler != null) core.NavigationStarting -= _navigationStartingHandler;
                if (_processFailedHandler != null) core.ProcessFailed -= _processFailedHandler;
            }
        }
        catch
        {
            // CoreWebView2 may already be gone; nothing left to unsubscribe.
        }

        _webMessageHandler = null;
        _navigationHandler = null;
        _navigationStartingHandler = null;
        _processFailedHandler = null;
        _ready = false;
        _pendingPnml = null;
        _pendingTheme = null;
        _hostFindings = null;
    }
}
