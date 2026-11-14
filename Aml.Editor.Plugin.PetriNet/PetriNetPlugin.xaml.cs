// Petri net plugin for the AutomationML Editor.
//
// Hosts a diagram-js based place/transition modeler in a WebView2 and keeps it
// in sync with a PT_Net instance hierarchy in the open AML document. PNML
// (ISO/IEC 15909-2) is the wire format between the two, so the same bytes that
// travel to the mapper are what any other Petri net tool would read.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aml.Editor.Plugin.Contracts;
using Aml.Editor.Plugin.PetriNet.Bridge;
using Aml.Editor.Plugin.PetriNet.Diagnostics;
using Aml.Editor.Plugin.WPFBase;
using Aml.Engine.CAEX;
using Microsoft.Win32;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;

namespace Aml.Editor.Plugin.PetriNet;

public partial class PetriNetPlugin : PluginViewBase, ISupportsThemes, INotifyAMLDocumentLoad
{
    private const int MaxStatusLogLines = 500;

    private PtWebView? _bridge;
    private CAEXDocument? _document;

    /// <summary>
    /// The hierarchy the viewer is bound to. The id is the reliable half: a user
    /// can rename a hierarchy in the editor tree at any time, and binding by name
    /// alone would silently lose it and make the next update add a second one.
    /// The name is kept for display and as a fallback for hierarchies that
    /// predate the id being written.
    /// </summary>
    private string? _hierarchyId;

    private string? _hierarchyName;

    /// <summary>
    /// The modeler's own export of what it last imported. A change event with
    /// exactly this content is import fallout rather than a user edit, so the
    /// comparison is by content and no timing window can swallow a real edit.
    /// </summary>
    private string? _echoBaseline;

    /// <summary>Latest PNML from the viewer that has not been written back yet.</summary>
    private string? _pendingPnml;

    /// <summary>
    /// Set while the page is being handed its own unsaved net back after a
    /// reload. The import acknowledgement must not clear the pending edits in
    /// that case: they are still unsaved, they are merely on the canvas again.
    /// </summary>
    private bool _restoringPending;

    /// <summary>
    /// True while the echo baseline is the unsaved diagram itself (after a page
    /// reload restored it) rather than the net loaded from the document. Undoing
    /// back to that baseline is then not "back to the document".
    /// </summary>
    private bool _baselineIsPending;

    /// <summary>The last theme the editor announced, re-sent after every page boot.</summary>
    private string? _lastTheme;

    /// <summary>Which net of the hierarchy the canvas shows, when it holds several.</summary>
    private int _selectedNetIndex;

    /// <summary>Guards the selector against reacting to its own repopulation.</summary>
    private bool _updatingNetSelector;

    /// <summary>What the user decided about saving and about large updates.</summary>
    private readonly PluginSettings _settings = PluginSettings.Load();

    /// <summary>One row of the findings list: the element by name, not by id.</summary>
    private sealed record FindingRow(string Severity, string Rule, string Element, string Message, string? ElementId);

    /// <summary>One entry of the net picker: a net, and the hierarchy that holds it.</summary>
    private sealed record NetChoice(string? HierarchyId, string? HierarchyName, int NetIndex, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The net the shown findings were produced from. Kept so a saved report
    /// describes exactly what the panel shows, even if the diagram moved on.
    /// </summary>
    private (PtNet Net, IReadOnlyList<PtFinding> Findings)? _lastValidation;

    public PetriNetPlugin()
    {
        InitializeComponent();

        // No dots or slashes: the editor turns DisplayName into a WPF x:Name and
        // an XML element name in its config, and both reject them.
        DisplayName = "AMLPetriNet";
        IsReactive = true;

        PluginLog.Init();
        PluginLog.DebugEnabled = _settings.DebugLogging;
        PluginLog.OnLine += OnDiagnosticsLine;
        var version = typeof(PetriNetPlugin).Assembly.GetName().Version?.ToString(3) ?? "?";
        PluginLog.Info($"Plugin v{version} constructing. Log file: {PluginLog.FilePath}");

        // Say up front whether this build can work in this editor. Without it a
        // missing dependency or an unpacked asset folder only shows up later, as
        // an exception on the first canvas message or as an empty panel.
        try
        {
            var checks = StartupCheck.Run();
            foreach (var line in StartupCheck.Format(checks).Split(Environment.NewLine))
                PluginLog.Info(line.TrimEnd());

            foreach (var failure in checks.Where(c => !c.Ok))
                PluginLog.Error($"Startup check failed: {failure.What} ({failure.Detail})");
        }
        catch (Exception ex)
        {
            PluginLog.Error("Startup check threw", ex);
        }

        // New Net and Import PNML live in the panel's Net menu only. The editor
        // toolbar shows the commands of one plugin at a time, so with AMLFPBjs
        // loaded it showed the FPD commands inside this plugin's tab, and without
        // it every command appeared twice. The commands wait for a document.
        NewNetItem.Command = new RelayCommand<object>(_ => ExecuteNewNet(), _ => _document != null);
        ImportPnmlItem.Command = new RelayCommand<object>(_ => ExecuteImportPnml(), _ => _document != null);

        // Created here, not in Loaded: the editor can call DocumentLoaded before
        // the view is loaded, and a null bridge would drop that net on the floor
        // instead of buffering it until the page boots.
        CreateBridge();

        Loaded += async (_, __) =>
        {
            if (LogFilePathLabel != null) LogFilePathLabel.Text = "→ " + PluginLog.FilePath;
            if (DebugToggle != null) DebugToggle.IsChecked = PluginLog.DebugEnabled;
            if (SaveAfterUpdateToggle != null) SaveAfterUpdateToggle.IsChecked = _settings.SaveAfterUpdate;
            if (FollowSelectionToggle != null) FollowSelectionToggle.IsChecked = _settings.FollowSelection;
            if (AskBeforeLargeUpdatesToggle != null) AskBeforeLargeUpdatesToggle.IsChecked = _settings.AskBeforeLargeUpdates;
            if (LargeUpdateThresholdInput != null)
                LargeUpdateThresholdInput.Text = _settings.LargeUpdateThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await EnsureBridgeAsync();

            if (_document == null)
            {
                // A view constructed after the editor fired DocumentLoaded
                // (undock, late install, session restore) never receives that
                // callback, so look the document up directly.
                var found = DocumentDiscoverer.TryFindCurrentDocument();
                if (found != null) AttachDocument(found);
            }
            // A page that is already up keeps whatever is on its canvas. When
            // the page boots, or boots again, the Ready handler below fills it,
            // and that one knows whether there are unsaved edits to bring back.
        };

        // Deliberately no Unloaded handler. This editor fires Loaded/Unloaded on
        // every reparent of the visual tree, tab switches and panel resizes
        // included, and the FPD plugin learned the same lesson: tearing the
        // bridge down here reloaded the page each time and threw away every
        // unsaved canvas edit with it. The WebView lives as long as the view;
        // DocumentUnLoaded and ApplicationClose are the ends of its life.
    }

    // ── bridge ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires up the bridge object. Cheap and synchronous; the WebView2 boot in
    /// <see cref="EnsureBridgeAsync"/> is the slow half. Splitting the two means
    /// pushes that arrive early are buffered rather than lost.
    /// </summary>
    private void CreateBridge()
    {
        if (_bridge != null) return;

        _bridge = new PtWebView(Modeler);
        _bridge.OnInfo += PluginLog.Info;
        _bridge.OnError += message => PluginLog.Error(message);
        _bridge.OnJsLog += (level, message) =>
        {
            switch (level)
            {
                case "error": PluginLog.Error("[modeler] " + message); break;
                case "warn": PluginLog.Warn("[modeler] " + message); break;
                default: PluginLog.Debug("[modeler] " + message); break;
            }
        };
        _bridge.OnImported += baseline =>
        {
            _echoBaseline = baseline;
            if (_restoringPending)
            {
                // The canvas shows the unsaved edits again; they are still unsaved.
                _restoringPending = false;
                _baselineIsPending = true;
            }
            else
            {
                _pendingPnml = null;
                _baselineIsPending = false;
            }
            UpdatePendingLabel();
        };
        _bridge.OnDiagramChanged += OnDiagramChanged;
        _bridge.ElementSelected += OnElementSelected;
        _bridge.TransitionsFired += OnTransitionsFired;
        _bridge.Ready += OnModelerReady;
    }

    /// <summary>
    /// Runs on every page boot: the first one, an F5, a recovery after a
    /// renderer crash. Unsaved edits win over the document, the theme is sent
    /// again because the new page knows nothing of it, and a net the host
    /// already buffered before the boot is left to the bridge to flush.
    /// </summary>
    private void OnModelerReady()
    {
        if (_bridge == null) return;

        if (_pendingPnml != null)
        {
            _restoringPending = true;
            _bridge.ImportPnml(_pendingPnml);
            PluginLog.Info("Modeler booted; restored the unsaved diagram.");
        }
        else if (!_bridge.HasPendingImport)
        {
            var hierarchy = CurrentHierarchy();
            if (hierarchy != null) PushToModeler(hierarchy);
        }

        if (_lastTheme != null) _bridge.SendTheme(_lastTheme);
    }

    private async Task EnsureBridgeAsync()
    {
        CreateBridge();
        await _bridge!.InitAsync();
    }

    /// <summary>
    /// A click on the canvas, pointed at the same element in the editor tree.
    ///
    /// The canvas knows its elements by the id the reader derived for them,
    /// which is not always the id the document carries, so the element is looked
    /// up the way the reader found it (CaexToPtNet.FindElement) rather than by
    /// handing the diagram's id to the editor.
    ///
    /// An unsaved diagram is a special case worth naming: an element the user
    /// has just drawn is not in the document yet, so there is nothing to select
    /// and nothing to report either. Silence is right here; a user clicking
    /// around a net does not want a message per click.
    /// </summary>
    private void OnElementSelected(string? id)
    {
        if (!_settings.FollowSelection || string.IsNullOrEmpty(id)) return;

        try
        {
            var hierarchy = CurrentHierarchy();
            if (hierarchy == null) return;

            var nets = CaexToPtNet.NetElements(hierarchy);
            var netIE = _selectedNetIndex >= 0 && _selectedNetIndex < nets.Count
                ? nets[_selectedNetIndex]
                : nets.FirstOrDefault();
            if (netIE == null) return;

            var element = CaexToPtNet.FindElement(netIE, id);
            if (element?.ID == null)
            {
                PluginLog.Debug($"Selected '{id}' is not in the document (yet).");
                return;
            }

            if (!EditorTree.TrySelect(element.ID))
            {
                PluginLog.Debug($"The editor did not take the selection of '{element.Name}'.");
            }
        }
        catch (Exception ex)
        {
            // Following along is a convenience; losing it must not cost the user
            // anything else.
            PluginLog.Debug("Following the canvas selection failed: " + ex.Message);
        }
    }

    /// <summary>
    /// What fired, in the status line. A run never writes to the document, so
    /// this is the only trace of it on the host side.
    /// </summary>
    private void OnTransitionsFired(string[] ids)
    {
        if (ids.Length == 0) return;

        var net = CurrentNet();
        string Name(string id) =>
            net?.Transitions.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))?.Name ?? id;

        SetStatus(ids.Length == 1
            ? $"Fired {Name(ids[0])}."
            : $"Fired {ids.Length} transitions at once: {string.Join(", ", ids.Select(Name))}.");
    }

    private void OnDiagramChanged(string pnml)
    {
        if (string.Equals(pnml, _echoBaseline, StringComparison.Ordinal))
        {
            PluginLog.Debug("Change matched the import baseline; treated as import fallout.");

            // Undone all the way back to the loaded net: nothing is unsaved any
            // more, and a stale copy must not overwrite the document later.
            if (_pendingPnml != null && !_baselineIsPending)
            {
                _pendingPnml = null;
                UpdatePendingLabel();
            }

            // The same content also arrives when edits are undone back to the
            // loaded net. The check has to follow, or the list on the canvas
            // keeps the findings of the edit that is gone.
            try
            {
                Revalidate(Pnml.Read(pnml), Array.Empty<string>(), open: false);
            }
            catch (Exception ex)
            {
                PluginLog.Debug("Could not check the diagram: " + ex.Message);
            }
            return;
        }

        _pendingPnml = pnml;
        UpdatePendingLabel();

        // The check follows the canvas, as it does in the FPD plugin. It only
        // updates the count here: a panel that opened while someone is still
        // drawing an arc would be in the way.
        try
        {
            Revalidate(Pnml.Read(pnml), Array.Empty<string>(), open: false);
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Could not check the diagram: " + ex.Message);
        }
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>
    /// A short line next to the buttons saying what the last action did.
    /// Everything also goes to the log, but the log sits on the second tab, and
    /// an Update that reported its result only there looked as if nothing had
    /// happened.
    /// </summary>
    private void SetStatus(string text) =>
        Dispatcher.Invoke(() =>
        {
            if (StatusLabel != null) StatusLabel.Text = text;
        });

    private void UpdatePendingLabel()
    {
        if (PendingLabel == null) return;
        Dispatcher.Invoke(() =>
            PendingLabel.Text = _pendingPnml == null ? "" : "unsaved diagram changes");
    }

    // ── plugin identity ─────────────────────────────────────────────────────

    public override string PackageName => "Aml.Editor.Plugin.PetriNet";

    public override DockPositionEnum InitialDockPosition => DockPositionEnum.DockContent;

    public override bool CanClose => true;

    // ── document lifecycle ──────────────────────────────────────────────────

    /// <summary>
    /// Part of the contract for other subscribers. Deliberately never raised
    /// from DocumentLoaded: the editor listens to it and answers by calling
    /// DocumentLoaded again, which feeds itself (the FPD plugin measured 40
    /// calls a second before this was understood).
    /// </summary>
#pragma warning disable CS0067 // required by INotifyAMLDocumentLoad, raised by nobody here
    public event EventHandler<CAEXDocument>? IsDocumentLoaded;
#pragma warning restore CS0067

    public void DocumentLoaded(CAEXDocument document)
    {
        if (document == null)
        {
            DocumentUnLoaded();
            return;
        }
        if (ReferenceEquals(document, _document)) return;

        // The editor sometimes hands out a new CAEXDocument wrapper for the file
        // that is already open (the FPD plugin ran into this and keys its caches
        // by origin instead of by reference). Reattaching would reload the canvas
        // and throw away edits the user has not written back yet, so identity is
        // decided by what the document *is*, not by which object represents it.
        if (IsSameDocument(document, _document))
        {
            PluginLog.Debug("DocumentLoaded: same document, new wrapper. Keeping the current diagram.");
            _document = document;
            return;
        }

        AttachDocument(document);
    }

    /// <summary>
    /// Whether two wrappers describe the same file. OriginID names the authoring
    /// tool and is identical across editor-saved files, so the file name has to
    /// come along; neither alone is enough.
    /// </summary>
    private static bool IsSameDocument(CAEXDocument? a, CAEXDocument? b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;

        static (string Origin, string File) Identity(CAEXDocument doc) => (
            doc.CAEXFile?.SourceDocumentInformation?.FirstOrDefault()?.OriginID ?? "",
            doc.CAEXFile?.FileName ?? "");

        var (originA, fileA) = Identity(a);
        var (originB, fileB) = Identity(b);

        // Two brand new documents share an empty identity; treat those as distinct.
        if (originA.Length == 0 && fileA.Length == 0) return false;

        return string.Equals(originA, originB, StringComparison.Ordinal)
            && string.Equals(fileA, fileB, StringComparison.Ordinal);
    }

    public void DocumentUnLoaded()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(DocumentUnLoaded);
            return;
        }

        _document = null;
        _hierarchyId = null;
        _hierarchyName = null;
        _pendingPnml = null;
        _echoBaseline = null;
        _selectedNetIndex = 0;
        ClearFindings();
        UpdatePendingLabel();
        RefreshNetChoices();
        SetStatus("");
        ShowPlaceholder(true);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        PluginLog.Info("Document closed.");
    }

    public void ApplicationClose()
    {
        _bridge?.Dispose();
        _bridge = null;
        try { PluginLog.Shutdown(); }
        catch { /* nothing useful left to do while shutting down */ }
    }

    /// <summary>
    /// Binds the plugin to a document. The editor's callbacks are not documented
    /// as UI-thread only, and everything below eventually touches WebView2 and
    /// WPF, both of which are thread-affine, so the whole body is marshalled.
    /// </summary>
    private void AttachDocument(CAEXDocument document)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AttachDocument(document));
            return;
        }

        _document = document;
        _pendingPnml = null;
        _selectedNetIndex = 0;
        // The New Net and Import PNML buttons wait for a document; tell them one is here.
        CommandManager.InvalidateRequerySuggested();
        ClearFindings();
        UpdatePendingLabel();

        var hierarchy = FindPetriNetHierarchy(document);
        if (hierarchy == null)
        {
            _hierarchyId = null;
            _hierarchyName = null;
            RefreshNetChoices();
            ShowPlaceholder(true);
            PluginLog.Info("Document has no PT_Net instance hierarchy. Use New Net in the Net menu to add one.");
            SetStatus("This document holds no Petri net.");
            return;
        }

        BindHierarchy(hierarchy);
        ShowPlaceholder(false);
        PushToModeler(hierarchy);
    }

    private static InstanceHierarchyType? FindPetriNetHierarchy(CAEXDocument document)
    {
        var candidates = document.CAEXFile.InstanceHierarchy.Where(CaexToPtNet.ContainsNet).ToList();

        // The canvas shows one net at a time, the first to begin with. The net
        // picker lists the others.
        if (candidates.Count > 1)
        {
            PluginLog.Info($"The document holds {candidates.Count} Petri net hierarchies "
                + $"({string.Join(", ", candidates.Select(h => h.Name))}). Showing '{candidates[0].Name}'; "
                + "the Net list switches to the others.");
        }

        return candidates.FirstOrDefault();
    }

    private InstanceHierarchyType? CurrentHierarchy()
    {
        if (_document == null) return null;

        var hierarchies = _document.CAEXFile.InstanceHierarchy;

        if (!string.IsNullOrEmpty(_hierarchyId))
        {
            var byId = hierarchies.FirstOrDefault(ih => ih.ID == _hierarchyId);
            if (byId != null)
            {
                // Follow a rename rather than fighting it.
                if (byId.Name != _hierarchyName)
                {
                    PluginLog.Debug($"Hierarchy renamed from '{_hierarchyName}' to '{byId.Name}'; following it.");
                    _hierarchyName = byId.Name;
                }
                return byId;
            }
        }

        return _hierarchyName == null ? null : hierarchies.FirstOrDefault(ih => ih.Name == _hierarchyName);
    }

    /// <summary>Binds to a hierarchy, remembering both halves of its identity.</summary>
    private void BindHierarchy(InstanceHierarchyType hierarchy)
    {
        _hierarchyId = hierarchy.ID;
        _hierarchyName = hierarchy.Name;
    }

    private void ShowPlaceholder(bool visible) =>
        Dispatcher.Invoke(() =>
        {
            if (NoDocumentPlaceholder != null)
                NoDocumentPlaceholder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        });

    // ── AML to modeler ──────────────────────────────────────────────────────

    private void PushToModeler(InstanceHierarchyType hierarchy)
    {
        try
        {
            var nets = CaexToPtNet.Read(hierarchy);
            if (nets.Count == 0)
            {
                PluginLog.Warn($"Instance hierarchy '{hierarchy.Name}' holds no readable net.");
                RefreshNetChoices();
                SetStatus($"'{hierarchy.Name}' holds no readable net.");
                return;
            }

            // One canvas shows one net; the picker above chooses which.
            var index = _selectedNetIndex >= 0 && _selectedNetIndex < nets.Count ? _selectedNetIndex : 0;
            _selectedNetIndex = index;
            var net = nets[index];
            RefreshNetChoices();

            var reading = net.Warnings.ToList();
            var placed = PrepareForDisplay(net, report: true);
            foreach (var warning in reading) PluginLog.Warn(warning);
            Revalidate(net, reading, open: true);

            var arranged = placed == 0 ? "." : $". {Count(placed, "node")} had no layout and {(placed == 1 ? "was" : "were")} arranged.";
            SetStatus($"{net.Name ?? net.Id}: {Count(net.Places.Count, "place")}, {Count(net.Transitions.Count, "transition")}, "
                + Count(net.Arcs.Count, "arc") + arranged);

            var pnml = Pnml.Write(net);
            if (_bridge == null)
            {
                // Legitimate between Unloaded and the next Loaded, which
                // re-pushes on its own.
                PluginLog.Debug("View is detached; the net loads again when it comes back.");
            }
            else if (_bridge.ImportPnml(pnml))
            {
                PluginLog.Info($"Loaded net '{net.Name ?? net.Id}' into the modeler.");
            }
            else
            {
                PluginLog.Debug("Modeler still booting; the net is buffered and loads when it announces itself.");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error("Failed to load the net from AML", ex);
        }
    }

    /// <summary>
    /// Lists every Petri net of the document in the picker, whichever hierarchy
    /// holds it, and selects the one on the canvas.
    ///
    /// A document may hold one net per station, in hierarchies of their own.
    /// The canvas used to show the first and mention the rest only in the log,
    /// so the others were in the document and out of reach. With a single net
    /// the picker stays hidden.
    /// </summary>
    private void RefreshNetChoices()
    {
        if (NetSelector == null || NetSelectorLabel == null || NetSelectorSeparator == null) return;

        var choices = new List<NetChoice>();
        if (_document != null)
        {
            foreach (var ih in _document.CAEXFile.InstanceHierarchy.Where(CaexToPtNet.ContainsNet))
            {
                var nets = CaexToPtNet.Read(ih);
                for (var i = 0; i < nets.Count; i++)
                {
                    var netName = string.IsNullOrWhiteSpace(nets[i].Name) ? nets[i].Id : nets[i].Name!;
                    var label = nets.Count > 1 ? $"{ih.Name} / {netName}" : ih.Name ?? netName;
                    choices.Add(new NetChoice(ih.ID, ih.Name, i, label));
                }
            }
        }

        var current = CurrentHierarchy();

        _updatingNetSelector = true;
        try
        {
            NetSelector.Items.Clear();
            foreach (var choice in choices) NetSelector.Items.Add(choice);

            var many = choices.Count > 1;
            var visibility = many ? Visibility.Visible : Visibility.Collapsed;
            NetSelector.Visibility = visibility;
            NetSelectorLabel.Visibility = visibility;
            NetSelectorSeparator.Visibility = visibility;

            NetSelector.SelectedIndex = current == null ? -1 : choices.FindIndex(c => Denotes(c, current) && c.NetIndex == _selectedNetIndex);
        }
        finally
        {
            _updatingNetSelector = false;
        }
    }

    private static bool Denotes(NetChoice choice, InstanceHierarchyType hierarchy) =>
        string.Equals(choice.HierarchyId, hierarchy.ID, StringComparison.Ordinal)
        && string.Equals(choice.HierarchyName, hierarchy.Name, StringComparison.Ordinal);

    private void NetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingNetSelector || NetSelector.SelectedItem is not NetChoice choice || _document == null) return;

        var current = CurrentHierarchy();
        if (current != null && Denotes(choice, current) && choice.NetIndex == _selectedNetIndex) return;

        if (_pendingPnml != null
            && MessageBox.Show(
                "The diagram has unsaved changes. Switching nets discards them. Continue?",
                "Switch net", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            RefreshNetChoices();
            return;
        }

        var target = _document.CAEXFile.InstanceHierarchy.FirstOrDefault(ih => Denotes(choice, ih));
        if (target == null)
        {
            SetStatus($"'{choice.Label}' is no longer in the document.");
            RefreshNetChoices();
            return;
        }

        BindHierarchy(target);
        _selectedNetIndex = choice.NetIndex;
        _pendingPnml = null;
        UpdatePendingLabel();
        ClearFindings();
        PushToModeler(target);
    }

    // ── modeler to AML ──────────────────────────────────────────────────────

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null)
        {
            PluginLog.Warn("No AML document is open.");
            SetStatus("No AML document is open.");
            return;
        }
        if (_pendingPnml == null)
        {
            PluginLog.Info("Nothing to write back: the diagram has no unsaved changes.");
            SetStatus("Nothing to write back: the diagram has no unsaved changes.");
            return;
        }

        try
        {
            var net = Pnml.Read(_pendingPnml);

            // Update in place rather than replacing: anything a user added to an
            // element by hand, and any link out of this hierarchy, has to survive
            // a sync.
            var hierarchy = CurrentHierarchy();
            if (hierarchy != null && !LargeUpdateConfirmed(hierarchy, net))
            {
                SetStatus("Update cancelled; the diagram still has its changes.");
                return;
            }

            PtUpdateSummary summary;
            if (hierarchy == null)
            {
                hierarchy = PtNetToCaex.AppendInto(_document, net, PtNetToCaex.DefaultHierarchyName);
                summary = new PtUpdateSummary(net.Places.Count + net.Transitions.Count + net.Arcs.Count + 1, 0, 0);
            }
            else
            {
                summary = PtNetUpdater.UpdateInPlace(_document, hierarchy, net, _selectedNetIndex);
            }
            BindHierarchy(hierarchy);

            _pendingPnml = null;
            _echoBaseline = null;
            UpdatePendingLabel();
            RefreshNetChoices();
            Revalidate(net, Array.Empty<string>(), open: true);

            PluginLog.Info($"'{hierarchy.Name}': {summary}.");

            // A note means the update reached outside the net, which the user
            // should hear about rather than discover later.
            foreach (var note in summary.Notes) PluginLog.Warn(note);

            // The contract has no way to tell the editor its document changed,
            // so the save has to be triggered through the editor's own command.
            // Whether that happens is the user's choice: an update the editor
            // saves at once cannot be undone by closing the document.
            var saved = false;
            if (_settings.SaveAfterUpdate)
            {
                saved = EditorSaver.TrySaveActiveDocument();
                if (!saved)
                    PluginLog.Warn("Could not trigger the editor's save command. Press Ctrl+S to persist the change.");
            }

            SetStatus($"Updated '{hierarchy.Name}': {summary}."
                + (summary.Notes.Count > 0 ? $" {summary.Notes.Count} note(s) on the Status / Diagnostics tab." : "")
                + (saved ? " Saved." : " Press Ctrl+S to save."));
        }
        catch (Exception ex)
        {
            PluginLog.Error("Failed to write the net into AML", ex);
            SetStatus("Update failed: " + ex.Message);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var hierarchy = CurrentHierarchy();
        if (hierarchy == null)
        {
            PluginLog.Warn("No Petri net instance hierarchy is bound.");
            SetStatus("No Petri net to reload.");
            return;
        }

        if (_pendingPnml != null
            && MessageBox.Show(
                "The diagram has unsaved changes. Reloading from AML discards them. Continue?",
                "Refresh from AML", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        _pendingPnml = null;
        UpdatePendingLabel();
        ClearFindings();
        PushToModeler(hierarchy);
    }

    // ── validation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks a net against the ISO/IEC 15909-1 rules and shows the result.
    ///
    /// The check runs by itself, when a net is loaded, after an Update and as
    /// the canvas changes, the way the FPD plugin does it. A Validate button
    /// that had to be remembered meant a net was usually shown unchecked.
    /// </summary>
    /// <param name="reading">
    /// What reading the document left off the canvas. Listed with the findings,
    /// since an arc that is not drawn is something to look at too.
    /// </param>
    /// <param name="open">
    /// Whether the panel may open by itself. It does after loading and after an
    /// Update when there is an error or something was left off the canvas, and
    /// never while someone is drawing.
    /// </param>
    private void Revalidate(PtNet net, IReadOnlyList<string> reading, bool open)
    {
        var findings = PtValidator.Validate(net);
        _lastValidation = (net, findings);

        // The element a finding is about, by the name a user sees on the canvas.
        // The ids of a document from another tool are GUIDs, and a list of those
        // says nothing.
        string NameOf(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var node = net.FindNode(id);
            if (node != null) return string.IsNullOrWhiteSpace(node.Name) ? id : node.Name!;
            var arc = net.Arcs.FirstOrDefault(a => a.Id == id);
            if (arc != null) return string.IsNullOrWhiteSpace(arc.Name) ? id : arc.Name!;
            return id;
        }

        var rows = reading
            .Select(w => new PtFinding("Reading", PtSeverity.Warning, w))
            .Concat(findings)
            .Select(f => new FindingRow(f.Severity.ToString(), f.Rule, NameOf(f.ElementId), f.Message, f.ElementId))
            .ToList();
        var errors = rows.Count(f => f.Severity == PtSeverity.Error.ToString());
        var warnings = rows.Count - errors;

        if (open)
        {
            PluginLog.Info($"Check: {errors} error(s), {warnings} warning(s).");
            foreach (var finding in findings)
            {
                if (finding.Severity == PtSeverity.Error) PluginLog.Error(finding.ToString());
                else PluginLog.Debug(finding.ToString());
            }
        }

        Dispatcher.Invoke(() =>
        {
            // The same findings, in the list on the canvas. The WPF table below
            // stays: it also carries what reading the document left off the
            // canvas and feeds the conformance report.
            _bridge?.SendHostFindings(findings
                .Select(f => new HostFinding(f.Rule, f.Severity.ToString(), f.ElementId, f.Message))
                .ToList());

            if (FindingsGrid == null || FindingsPanel == null || FindingsButton == null) return;

            FindingsGrid.ItemsSource = rows;
            FindingsSummary.Text = rows.Count == 0
                ? "No findings: the net satisfies the structural rules of ISO/IEC 15909-1."
                : $"{errors} error(s), {warnings} warning(s). Double-click a row to select the element."
                  + (reading.Count > 0
                      ? $" {reading.Count} element(s) of the document are not on the canvas; they stay in the document as they are."
                      : "");

            FindingsButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FindingsButtonText.Text = errors > 0
                ? $"{errors} error(s)" + (warnings > 0 ? $", {warnings} warning(s)" : "")
                : $"{warnings} warning(s)";

            if (rows.Count == 0) SetFindingsPanel(false);
            else if (open && (errors > 0 || reading.Count > 0)) SetFindingsPanel(true);
        });
    }

    private void SetFindingsPanel(bool visible)
    {
        FindingsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FindingsSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FindingsRow.Height = new GridLength(visible ? 180 : 0);
    }

    private void FindingsButton_Click(object sender, RoutedEventArgs e) =>
        SetFindingsPanel(FindingsPanel.Visibility != Visibility.Visible);

    /// <summary>
    /// Findings describe the net they were computed on. Anything that loads or
    /// writes a different net makes them stale, and a stale finding that still
    /// selects an element is worse than no finding at all. The next check fills
    /// them in again.
    /// </summary>
    private void ClearFindings()
    {
        // The list on the canvas keeps the findings of the host through edits,
        // so it is told when they no longer describe the net.
        _bridge?.SendHostFindings(Array.Empty<HostFinding>());

        if (FindingsGrid == null || FindingsPanel == null) return;

        FindingsGrid.ItemsSource = null;
        _lastValidation = null;
        if (FindingsButton != null) FindingsButton.Visibility = Visibility.Collapsed;
        SetFindingsPanel(false);
    }

    private void SaveReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastValidation is not { } validation)
        {
            PluginLog.Warn("No net has been checked yet: open one first.");
            SetStatus("No net has been checked yet: open one first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            FileName = (validation.Net.Name ?? validation.Net.Id) + "-conformance.md",
            Title = "Save the conformance report",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName,
                PtConformanceReport.Build(validation.Net, validation.Findings, _hierarchyName));
            PluginLog.Info("Report written to " + dialog.FileName);
            SetStatus("Conformance report saved: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            PluginLog.Error("Could not write the report", ex);
        }
    }

    private void CloseFindingsButton_Click(object sender, RoutedEventArgs e) => SetFindingsPanel(false);

    private void FindingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindingsGrid.SelectedItem is FindingRow { ElementId: { Length: > 0 } id })
            _bridge?.SelectElement(id);
    }

    /// <summary>
    /// Makes a net from the document fit to be shown, and to be compared with
    /// what comes back from the canvas.
    ///
    /// Both halves matter downstream. The modeler's parser refuses an id that is
    /// not an XML name, and a document written by another tool routinely stores
    /// the CAEX GUID, braces and all; refusing those would mean refusing the
    /// very documents this plugin exists for, so they are normalised for the
    /// canvas and the document keeps its own. A file from another tool often
    /// carries no layout at all, and then every node would sit at the origin in
    /// one pile. Anything that reads the document while the canvas is untouched
    /// has to apply the same two steps, or an export would differ from the
    /// diagram it claims to be.
    /// </summary>
    private static int PrepareForDisplay(PtNet net, bool report)
    {
        var renamed = PtIdText.NormaliseIds(net);
        if (renamed > 0 && report)
        {
            PluginLog.Info($"{renamed} element id(s) are not valid XML names and were adjusted for "
                + "the diagram. The ids in the document are untouched.");
        }

        var placed = PtLayout.ArrangeMissing(net);
        if (placed > 0 && report)
            PluginLog.Info($"{placed} element(s) had no layout; arranged them left to right.");
        return placed;
    }

    /// <summary>
    /// The net as it currently stands: what is on the canvas if the user has
    /// edited it, otherwise what the document holds, prepared the same way the
    /// canvas got it.
    /// </summary>
    private PtNet? CurrentNet()
    {
        if (_pendingPnml != null)
        {
            try
            {
                return Pnml.Read(_pendingPnml);
            }
            catch (Exception ex)
            {
                PluginLog.Error("Could not read the diagram", ex);
                return null;
            }
        }

        var hierarchy = CurrentHierarchy();
        if (hierarchy == null) return null;

        var nets = CaexToPtNet.Read(hierarchy);
        var net = _selectedNetIndex >= 0 && _selectedNetIndex < nets.Count ? nets[_selectedNetIndex] : nets.FirstOrDefault();
        if (net != null) PrepareForDisplay(net, report: false);
        return net;
    }

    // ── toolbar commands ────────────────────────────────────────────────────

    private void ExecuteNewNet()
    {
        if (_document == null) return;

        try
        {
            var net = new PtNet { Id = "net_1", Name = "Petri net" };
            var hierarchy = PtNetToCaex.AppendInto(_document, net, UniqueHierarchyName("PetriNets"));

            BindHierarchy(hierarchy);
            _selectedNetIndex = 0;
            ClearFindings();
            ShowPlaceholder(false);
            PushToModeler(hierarchy);
            PluginLog.Info($"Created empty instance hierarchy '{hierarchy.Name}'.");
            SetStatus($"Created '{hierarchy.Name}'. Draw the net, then Update.");
        }
        catch (Exception ex)
        {
            PluginLog.Error("Failed to create a new net", ex);
        }
    }

    private void ExecuteImportPnml()
    {
        if (_document == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "PNML files (*.pnml)|*.pnml|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Title = "Import a Petri net",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            // A PNML file may hold several nets; taking only the first would
            // quietly drop the rest.
            using var file = File.OpenRead(dialog.FileName);
            var nets = Pnml.ReadAll(file);
            if (nets.Count > 1)
                PluginLog.Info($"The file holds {nets.Count} nets; all of them were imported.");

            // Arrange before writing, not just before displaying: otherwise the
            // document would carry nets with no geometry until the user happens
            // to press Update.
            var placed = nets.Sum(PtLayout.ArrangeMissing);
            if (placed > 0)
                PluginLog.Info($"The file had no layout for {placed} element(s); arranged them left to right.");

            var hierarchy = PtNetToCaex.AppendInto(
                _document, nets, UniqueHierarchyName(Path.GetFileNameWithoutExtension(dialog.FileName)));

            BindHierarchy(hierarchy);
            _selectedNetIndex = 0;
            ClearFindings();
            ShowPlaceholder(false);
            PushToModeler(hierarchy);
            PluginLog.Info($"Imported '{Path.GetFileName(dialog.FileName)}' into '{hierarchy.Name}'.");
            SetStatus($"Imported '{Path.GetFileName(dialog.FileName)}' into '{hierarchy.Name}'.");
        }
        catch (Exception ex)
        {
            PluginLog.Error("PNML import failed", ex);
            MessageBox.Show("Could not import that file:\n\n" + ex.Message,
                "Import PNML", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens the Net menu under its button, the way a drop-down button does.</summary>
    private void NetMenuButton_Click(object sender, RoutedEventArgs e)
    {
        NetMenu.PlacementTarget = NetMenuButton;
        NetMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        NetMenu.IsOpen = true;
    }

    /// <summary>
    /// Saves the diagram as a picture. The modeler draws it, so this is what a
    /// figure in a document should show: the same net the canvas shows, with
    /// its layout, rather than a redrawing of it.
    /// </summary>
    private async void ExportSvgButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge == null || !_bridge.IsReady)
        {
            SetStatus("The canvas is not ready yet.");
            return;
        }

        var svg = await _bridge.RequestSvgAsync(TimeSpan.FromSeconds(10));
        if (string.IsNullOrEmpty(svg))
        {
            PluginLog.Warn("The modeler did not answer the request for a picture.");
            SetStatus("The modeler did not answer the request for a picture.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "SVG files (*.svg)|*.svg|All files (*.*)|*.*",
            FileName = (CurrentNet()?.Name ?? _hierarchyName ?? "petri-net") + ".svg",
            Title = "Export the diagram as a picture",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, svg);
            PluginLog.Info("Picture written to " + dialog.FileName);
            SetStatus("Exported " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            PluginLog.Error("SVG export failed", ex);
            SetStatus("SVG export failed: " + ex.Message);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        // CurrentNet prefers what is on screen and falls back to the selected
        // net in the document, so the export follows the net picker.
        var net = CurrentNet();
        if (net == null)
        {
            PluginLog.Warn("Nothing to export.");
            SetStatus("Nothing to export.");
            return;
        }
        var pnml = Pnml.Write(net, indent: true);

        var dialog = new SaveFileDialog
        {
            Filter = "PNML files (*.pnml)|*.pnml|All files (*.*)|*.*",
            FileName = (_hierarchyName ?? "petri-net") + ".pnml",
            Title = "Export the Petri net",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, pnml);
            PluginLog.Info("Exported to " + dialog.FileName);
            SetStatus("Exported " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            PluginLog.Error("PNML export failed", ex);
        }
    }

    private string UniqueHierarchyName(string preferred)
    {
        if (_document == null) return preferred;

        var taken = _document.CAEXFile.InstanceHierarchy.Select(ih => ih.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(preferred)) return preferred;

        for (var i = 2; ; i++)
        {
            var candidate = $"{preferred}_{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    // ── theming and diagnostics ─────────────────────────────────────────────

    public void OnThemeChanged(ApplicationTheme theme)
    {
        var name = theme.ToString().Contains("Dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        _lastTheme = name;

        if (Dispatcher.CheckAccess()) _bridge?.SendTheme(name);
        else Dispatcher.Invoke(() => _bridge?.SendTheme(name));

        PluginLog.Debug("Theme changed to " + name);
    }

    private void DebugToggle_Changed(object sender, RoutedEventArgs e)
    {
        PluginLog.DebugEnabled = DebugToggle?.IsChecked == true;
        _settings.DebugLogging = PluginLog.DebugEnabled;
        _settings.Save();
    }

    private void SettingsToggle_Changed(object sender, RoutedEventArgs e)
    {
        _settings.SaveAfterUpdate = SaveAfterUpdateToggle?.IsChecked == true;
        _settings.FollowSelection = FollowSelectionToggle?.IsChecked == true;
        _settings.AskBeforeLargeUpdates = AskBeforeLargeUpdatesToggle?.IsChecked == true;
        _settings.Save();
    }

    private void ThresholdInput_Changed(object sender, RoutedEventArgs e)
    {
        if (LargeUpdateThresholdInput == null) return;

        if (int.TryParse(LargeUpdateThresholdInput.Text, out var threshold) && threshold > 0)
        {
            _settings.LargeUpdateThreshold = threshold;
            _settings.Save();
        }

        // Always write the stored value back: a typo should not look accepted.
        LargeUpdateThresholdInput.Text =
            _settings.LargeUpdateThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asks before an update that changes more than the user allowed silently.
    ///
    /// The case this is for is a diagram that does not belong to the document
    /// any more, for instance after the canvas was reloaded with another net.
    /// Applying it would delete the difference, and an update cannot be undone
    /// from the plugin. Counted on the ids, so moving or renaming does not ask.
    /// </summary>
    private bool LargeUpdateConfirmed(InstanceHierarchyType hierarchy, PtNet incoming)
    {
        if (!_settings.AskBeforeLargeUpdates) return true;

        var nets = CaexToPtNet.Read(hierarchy);
        if (nets.Count == 0) return true;
        var stored = _selectedNetIndex >= 0 && _selectedNetIndex < nets.Count ? nets[_selectedNetIndex] : nets[0];

        var before = stored.Nodes.Select(n => n.Id).Concat(stored.Arcs.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
        var after = incoming.Nodes.Select(n => n.Id).Concat(incoming.Arcs.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);

        var added = after.Except(before, StringComparer.Ordinal).Count();
        var removed = before.Except(after, StringComparer.Ordinal).Count();
        if (added + removed <= _settings.LargeUpdateThreshold) return true;

        PluginLog.Warn($"Update would add {added} and remove {removed} element(s); asking first.");
        return MessageBox.Show(
            $"This update adds {added} element(s) and removes {removed}. "
            + "Removed elements are gone from the document, together with anything attached to them.\n\n"
            + "Apply it?",
            "Update InstanceHierarchy", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = PluginLog.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            PluginLog.Error("Could not open the log folder", ex);
        }
    }

    private void OnDiagnosticsLine(string line)
    {
        if (StatusLog == null) return;

        Dispatcher.Invoke(() =>
        {
            StatusLog.AppendText(line + Environment.NewLine);

            // Keep the box bounded: a long session otherwise grows it without limit.
            var lines = StatusLog.LineCount;
            if (lines > MaxStatusLogLines)
            {
                var cut = StatusLog.GetCharacterIndexFromLineIndex(lines - MaxStatusLogLines);
                if (cut > 0) StatusLog.Text = StatusLog.Text[cut..];
            }

            StatusLog.ScrollToEnd();
        });
    }
}
