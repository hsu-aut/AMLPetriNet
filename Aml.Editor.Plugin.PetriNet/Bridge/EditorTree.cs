// Pointing the editor's tree at an element.
//
// Aml.Editor.API is the documented way in: AMLEditor is a static entry point and
// SelectObjectById/ExpandObjectById are part of its surface. Every call is
// wrapped, because at design time, in a test and in a host that is not the AML
// Editor there is no editor to talk to, and a plugin must not throw into the UI
// over a convenience.

using Aml.Editor.API;
using Aml.Editor.Plugin.PetriNet.Diagnostics;

namespace Aml.Editor.Plugin.PetriNet.Bridge;

public static class EditorTree
{
    /// <summary>
    /// Expands to and selects the element with this CAEX id in the editor tree.
    /// </summary>
    /// <returns>whether the editor took it.</returns>
    public static bool TrySelect(string? caexId)
    {
        if (string.IsNullOrWhiteSpace(caexId)) return false;

        var editor = Editor();
        if (editor == null) return false;

        try
        {
            editor.ExpandObjectById(caexId);
            editor.SelectObjectById(caexId);
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Selecting in the editor tree failed: " + ex.Message);
            return false;
        }
    }

    // The constructor is not public; the running instance comes from the static
    // accessor. Separate method so that a missing Aml.Editor.API fails inside
    // the try rather than at the call site.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static AMLEditor? Instance() => AMLEditor.AMLApplication;

    private static AMLEditor? Editor()
    {
        try
        {
            return Instance();
        }
        catch (Exception ex)
        {
            PluginLog.Debug("The editor API is not available: " + ex.Message);
            return null;
        }
    }
}
