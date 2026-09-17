using Aml.Engine.CAEX;

namespace PtMapper.Conversion;

/// <summary>
/// How the mapper reads the CAEX structure around a net: ids, links and class
/// paths. One place, because three slightly different copies of these rules
/// once disagreed about what a link points at.
/// </summary>
internal static class PtCaex
{
    /// <summary>
    /// How CAEX ids are compared: exactly, character for character.
    ///
    /// CAEX declares ID as a string and XML treats <c>Start</c> and <c>start</c>
    /// as two different names, so a tolerant comparison would join elements the
    /// document keeps apart. The code used to mix both, which let the reader
    /// resolve a link the updater then failed to find. A reference that differs
    /// only in case therefore does not resolve: its arc is reported as
    /// unresolved and kept, not deleted.
    /// </summary>
    internal static readonly StringComparer Ids = StringComparer.Ordinal;

    /// <summary>
    /// The interface id a link side names, or null for an empty side.
    ///
    /// CAEX 3.0 writes the interface id into RefPartnerSideA and B, and that is
    /// all this reads. CAEX 2.15 wrote "OwnerID:InterfaceName" instead, but the
    /// ISO_PT library is CAEX 3.0, so a document that uses its classes cannot be
    /// in the older form. Splitting at a colon would only damage an id that
    /// happens to contain one.
    /// </summary>
    internal static string? PartnerId(string? side) => string.IsNullOrEmpty(side) ? null : side;

    /// <summary>
    /// Every InternalLink at or below an element.
    ///
    /// Both known documents put the arc-to-node links on the net element itself,
    /// but CAEX allows them on any element, and a link a tool placed on the arc
    /// would otherwise leave that arc looking unconnected.
    /// </summary>
    internal static IEnumerable<InternalLinkType> LinksBelow(InternalElementType element)
    {
        foreach (var link in element.InternalLink) yield return link;

        foreach (var child in element.InternalElement)
        foreach (var link in LinksBelow(child))
            yield return link;
    }

    /// <summary>Every InternalLink in the document, with the element that owns it.</summary>
    internal static IEnumerable<(InternalLinkType Link, InternalElementType Owner)> AllLinks(CAEXDocument doc)
    {
        foreach (var ih in doc.CAEXFile.InstanceHierarchy)
        foreach (var element in Descend(ih.InternalElement))
        foreach (var link in element.InternalLink)
            yield return (link, element);
    }

    /// <summary>Every element in a subtree, depth first, in document order.</summary>
    internal static IEnumerable<InternalElementType> Descend(IEnumerable<InternalElementType> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            foreach (var nested in Descend(element.InternalElement)) yield return nested;
        }
    }

    /// <summary>Whether a class path ends in the given class name.</summary>
    internal static bool IsClass(string? path, string className)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var slash = path.LastIndexOf('/');
        var last = slash >= 0 ? path[(slash + 1)..] : path;
        return string.Equals(last, className, StringComparison.Ordinal);
    }
}
