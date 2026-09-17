using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// The attribute type paths for the diagram-interchange types (DD_Bounds,
/// DD_Point, DD_Waypoint), in the form the document at hand uses.
///
/// A document names the OMG_DD library in one of three ways: it carries the
/// library inline and writes <c>OMG_DD_AttributeTypeLib/DD_Point</c>; it
/// references the library file through an ExternalReference and writes
/// <c>OMG_DD@OMG_DD_AttributeTypeLib/DD_Point</c>, with whatever alias it
/// chose; or it has neither yet. Writing the aliased form into a document that
/// carries the library inline, which is what the plugin does with every
/// self-contained file it opens, leaves a dangling alias on every attribute the
/// sync touches, and the tool's own round-trip check refuses the result.
///
/// The form is read once per document and per top-level write, at the
/// <see cref="Refresh"/> the writers call, so the individual attribute writers
/// do not scan the document again for every waypoint.
/// </summary>
internal static class PtDiPaths
{
    private static readonly ConditionalWeakTable<XElement, StrongBox<string>> Cache = new();

    /// <summary>Re-read the document's convention. Called at the start of every write.</summary>
    internal static void Refresh(CAEXDocument doc)
    {
        var key = doc.CAEXFile.Node;
        Cache.Remove(key);
        Cache.Add(key, new StrongBox<string>(Resolve(doc)));
    }

    internal static string Bounds(CAEXBasicObject owner) => Prefix(owner) + "/DD_Bounds";
    internal static string Point(CAEXBasicObject owner) => Prefix(owner) + "/DD_Point";
    internal static string Waypoint(CAEXBasicObject owner) => Prefix(owner) + "/DD_Waypoint";

    private static string Prefix(CAEXBasicObject owner)
    {
        var doc = owner.CAEXDocument;
        if (doc == null) return DiagramInterchangeLibrary.QualifiedLibName;

        var key = doc.CAEXFile.Node;
        if (!Cache.TryGetValue(key, out var box))
        {
            box = new StrongBox<string>(Resolve(doc));
            Cache.Add(key, box);
        }
        return box.Value!;
    }

    private static string Resolve(CAEXDocument doc)
    {
        var caex = doc.CAEXFile;

        // Carried inline: the unqualified path is the only one that resolves.
        if (caex.AttributeTypeLib[DiagramInterchangeLibrary.LibName] != null)
            return DiagramInterchangeLibrary.LibName;

        // Referenced under some alias already: follow the document's lead, so
        // a file whose author called the reference "DD" does not end up with
        // two spellings of the same library.
        var suffix = "@" + DiagramInterchangeLibrary.LibName + "/";
        foreach (var attribute in caex.Node.Descendants().Attributes("RefAttributeType"))
        {
            var value = attribute.Value;
            var at = value.IndexOf(suffix, StringComparison.Ordinal);
            if (at > 0) return value[..at] + "@" + DiagramInterchangeLibrary.LibName;
        }

        // Neither: reference the published file, as the library generator does.
        PtLibraries.EnsureExternalReference(caex, DiagramInterchangeLibrary.Alias, DiagramInterchangeLibrary.FileName);
        return DiagramInterchangeLibrary.QualifiedLibName;
    }
}
