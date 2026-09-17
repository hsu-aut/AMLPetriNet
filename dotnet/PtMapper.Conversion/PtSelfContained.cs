using System.Xml.Linq;

namespace PtMapper.Conversion;

/// <summary>
/// Turns a document that references its libraries into one that carries them.
///
/// The AML Editor does not resolve file-based ExternalReferences, so a document
/// written by the mapper opens there with its class paths unresolved: the
/// elements are all present, but nothing knows what a PT_Place is. For anything
/// that has to be opened by someone else, or in the editor at all, the libraries
/// have to travel with the file.
///
/// The AML base libraries stay a reference by file name. Every AML tool knows
/// them, and inlining them would triple the file for no benefit.
///
/// This is how BridgingExample_MPS500_selfcontained.aml in examples/paper is
/// derived from the modular bridging example. It lives in the toolchain so that
/// the step is reproducible instead of depending on a one-off script.
/// </summary>
public static class PtSelfContained
{
    private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

    /// <summary>Library elements of a CAEX file, in the order the schema wants them.</summary>
    private static readonly string[] LibraryTags =
    {
        "InterfaceClassLib", "RoleClassLib", "SystemUnitClassLib", "AttributeTypeLib",
    };

    /// <summary>The full child order of CAEXFile, so the result stays schema-valid.</summary>
    private static readonly string[] ChildOrder =
    {
        "SuperiorStandardVersion", "SourceDocumentInformation", "ExternalReference",
        "InstanceHierarchy", "InterfaceClassLib", "RoleClassLib", "SystemUnitClassLib", "AttributeTypeLib",
    };

    /// <summary>Aliases whose libraries this mapper knows how to inline.</summary>
    public static IReadOnlyList<string> InlinableAliases { get; } = new[]
    {
        DiagramInterchangeLibrary.Alias,
        ObjectReferencesLibrary.Alias,
    };

    /// <summary>
    /// Inlines the libraries behind <see cref="InlinableAliases"/>.
    /// </summary>
    /// <param name="amlXml">The document to transform.</param>
    /// <param name="libraryXmlByAlias">
    /// The content of each library file, keyed by the alias its ExternalReference
    /// uses. An alias with no entry is left as a reference.
    /// </param>
    /// <returns>The transformed document, and what was inlined.</returns>
    public static (string Xml, IReadOnlyList<string> Inlined) Inline(
        string amlXml, IReadOnlyDictionary<string, string> libraryXmlByAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amlXml);
        ArgumentNullException.ThrowIfNull(libraryXmlByAlias);

        var document = XDocument.Parse(amlXml);
        var root = document.Root ?? throw new FormatException("The document has no root element.");

        var inlined = new List<string>();

        foreach (var alias in InlinableAliases)
        {
            var reference = root.Elements(Caex + "ExternalReference")
                .FirstOrDefault(e => (string?)e.Attribute("Alias") == alias);
            if (reference == null) continue;

            if (!libraryXmlByAlias.TryGetValue(alias, out var libraryXml))
                continue;

            var libraryRoot = XDocument.Parse(libraryXml).Root
                ?? throw new FormatException($"The library for alias '{alias}' has no root element.");

            foreach (var tag in LibraryTags)
            {
                foreach (var lib in libraryRoot.Elements(Caex + tag))
                {
                    var name = (string?)lib.Attribute("Name");
                    var present = root.Elements(Caex + tag)
                        .Any(existing => (string?)existing.Attribute("Name") == name);
                    if (present) continue;

                    root.Add(new XElement(lib));
                }
            }

            reference.Remove();
            inlined.Add(alias);
        }

        Reorder(root);

        // The alias prefixes only had meaning while the libraries were external.
        // Rewriting them to the document-internal form is a text operation
        // because they appear inside attribute values all over the tree.
        var xml = document.Declaration + Environment.NewLine + document;
        foreach (var alias in inlined)
            xml = xml.Replace(alias + "@", string.Empty, StringComparison.Ordinal);

        return (xml, inlined);
    }

    /// <summary>
    /// Puts the children of CAEXFile back into schema order. Appending libraries
    /// leaves them after the InstanceHierarchy, which the CAEX 3.0 schema does
    /// not allow.
    /// </summary>
    private static void Reorder(XElement root)
    {
        var ordered = ChildOrder
            .SelectMany(tag => root.Elements(Caex + tag))
            .ToList();

        // Anything not named in the order list keeps its place at the end rather
        // than being dropped.
        var rest = root.Elements().Except(ordered).ToList();

        foreach (var child in root.Elements().ToList()) child.Remove();
        foreach (var child in ordered) root.Add(child);
        foreach (var child in rest) root.Add(child);
    }

    /// <summary>
    /// Reads the library files this mapper can inline from a directory, keyed by
    /// alias. Missing files are skipped, so a partial set still works.
    /// </summary>
    public static Dictionary<string, string> ReadLibraries(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var byAlias = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (alias, fileName) in new[]
                 {
                     (DiagramInterchangeLibrary.Alias, DiagramInterchangeLibrary.FileName),
                     (ObjectReferencesLibrary.Alias, ObjectReferencesLibrary.FileName),
                 })
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path)) byAlias[alias] = File.ReadAllText(path);
        }

        return byAlias;
    }
}
