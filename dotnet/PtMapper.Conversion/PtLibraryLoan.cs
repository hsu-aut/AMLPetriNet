using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// Keeps a document's own way of naming the ISO_PT classes.
///
/// A document either carries the ISO_PT libraries or references them through an
/// alias, and both are legitimate. Creating an element needs the class
/// definitions at hand, so a referencing document gets them added, used, and
/// taken away again; the paths on the new elements are then rewritten to the
/// alias the rest of the document already uses.
///
/// Without this, editing one node in
/// <c>BridgingExample_MPS500.aml</c> would write four class libraries into a
/// file whose author deliberately kept them outside, and the new element would
/// name its classes differently from every element beside it.
/// </summary>
internal sealed class PtLibraryLoan
{
    /// <summary>Class path attributes CAEX uses. All of them can name an ISO_PT class.</summary>
    private static readonly string[] PathAttributes =
    {
        "RefBaseSystemUnitPath", "RefBaseClassPath", "RefBaseRoleClassPath",
        "RefRoleClassPath", "RefAttributeType",
    };

    private static readonly string[] Libraries =
    {
        PtNames.InterfaceClassLib, PtNames.RoleClassLib,
        PtNames.SystemUnitClassLib, PtNames.AttributeTypeLib,
    };

    private readonly CAEXDocument _doc;
    private readonly string? _alias;
    private readonly bool _carriedThemAlready;

    private PtLibraryLoan(CAEXDocument doc, string? alias, bool carriedThemAlready)
    {
        _doc = doc;
        _alias = alias;
        _carriedThemAlready = carriedThemAlready;
    }

    /// <summary>Record how the document stands before anything is written to it.</summary>
    internal static PtLibraryLoan Open(CAEXDocument doc) =>
        new(doc, AliasInUse(doc), CarriesLibraries(doc));

    /// <summary>
    /// Whether the libraries will be borrowed and given back, which is the case
    /// for a document that names them through an alias rather than carrying
    /// them. Such a document keeps its references, and nothing shared is
    /// embedded into it either: its author chose that shape.
    /// </summary>
    internal bool IsBorrowing => !_carriedThemAlready && _alias != null;

    /// <summary>Give the borrowed libraries back, if they were borrowed at all.</summary>
    internal void Close()
    {
        // Nothing to give back: the document brought its own libraries, or it
        // never named an external one, in which case the inlined libraries are
        // the only definition there is and have to stay.
        if (_carriedThemAlready || _alias == null) return;
        if (!CarriesLibraries(_doc)) return;

        foreach (var name in Libraries)
        {
            _doc.CAEXFile.InterfaceClassLib[name]?.Remove();
            _doc.CAEXFile.RoleClassLib[name]?.Remove();
            _doc.CAEXFile.SystemUnitClassLib[name]?.Remove();
            _doc.CAEXFile.AttributeTypeLib[name]?.Remove();
        }

        // After removal, so that only the paths that still matter are touched.
        Requalify(_doc.CAEXFile.Node, _alias);
    }

    /// <summary>
    /// The alias this document uses for the ISO_PT library, read from the paths
    /// it already contains rather than from the external reference list: an
    /// alias that nothing points at tells us nothing about the convention.
    /// </summary>
    private static string? AliasInUse(CAEXDocument doc)
    {
        foreach (var attribute in PathAttributesOf(doc.CAEXFile.Node))
        {
            var value = attribute.Value;
            var at = value.IndexOf('@');
            if (at <= 0) continue;

            var rest = value.AsSpan(at + 1);
            foreach (var library in Libraries)
            {
                if (!rest.StartsWith(library) || rest.Length <= library.Length) continue;
                if (rest[library.Length] != '/') continue;

                return value[..at];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the document carries any of the four ISO_PT libraries.
    ///
    /// Any, not all: a document with three of them is broken, and the fourth is
    /// then added rather than the whole set being borrowed and taken away
    /// again, which would leave it just as broken.
    /// </summary>
    private static bool CarriesLibraries(CAEXDocument doc) =>
        Libraries.Any(name =>
            doc.CAEXFile.InterfaceClassLib[name] != null
            || doc.CAEXFile.RoleClassLib[name] != null
            || doc.CAEXFile.SystemUnitClassLib[name] != null
            || doc.CAEXFile.AttributeTypeLib[name] != null);

    /// <summary>Put the alias in front of every unqualified ISO_PT path.</summary>
    private static void Requalify(XElement root, string alias)
    {
        foreach (var attribute in PathAttributesOf(root))
        {
            var value = attribute.Value;
            if (value.Contains('@')) continue;

            foreach (var library in Libraries)
            {
                if (!value.StartsWith(library, StringComparison.Ordinal)) continue;
                if (value.Length <= library.Length || value[library.Length] != '/') continue;

                attribute.Value = alias + "@" + value;
                break;
            }
        }
    }

    private static IEnumerable<XAttribute> PathAttributesOf(XElement root) =>
        root.DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => PathAttributes.Contains(a.Name.LocalName));
}
