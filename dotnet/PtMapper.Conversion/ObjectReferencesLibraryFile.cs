using System.Reflection;
using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// Embeds Rainer Drath's object reference attribute types into a document.
///
/// Unlike the ISO_PT libraries and the diagram-interchange types, this one is
/// not reproduced in code: it is an official AutomationML artefact and the file
/// is the authority. It ships inside this assembly so that a document can be
/// made self-contained without the file lying beside the program, which is what
/// the plugin needs: it runs inside the AML Editor, with no library folder in
/// reach.
///
/// Why a document needs it at all: the PT_Net classes carry a refObj attribute
/// typed by this library, which the published ISO_PT_DomainLibrary does too. A
/// document that has the ISO_PT classes inline but only a file reference for
/// this one opens in the editor with that attribute unresolved, because the
/// editor does not follow file references.
/// </summary>
public static class ObjectReferencesLibraryFile
{
    private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

    /// <summary>The library file's content, as it was published.</summary>
    public static string Xml { get; } = ReadEmbedded();

    /// <summary>
    /// Appends the library to a document if it is not already there. Returns
    /// whether it was added.
    /// </summary>
    public static bool EnsureIn(CAEXFileType caex)
    {
        ArgumentNullException.ThrowIfNull(caex);

        if (caex.AttributeTypeLib[ObjectReferencesLibrary.LibName] != null) return false;

        var library = XDocument.Parse(Xml).Root?
            .Elements(Caex + "AttributeTypeLib")
            .FirstOrDefault(e => (string?)e.Attribute("Name") == ObjectReferencesLibrary.LibName);

        if (library == null)
            throw new InvalidOperationException(
                $"The embedded library holds no AttributeTypeLib '{ObjectReferencesLibrary.LibName}'.");

        caex.Node.Add(new XElement(library));
        return true;
    }

    private static string ReadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ObjectReferencesLibrary.FileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{ObjectReferencesLibrary.FileName}' is not embedded in {assembly.GetName().Name}. "
                + "Check the EmbeddedResource item in the project file.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
