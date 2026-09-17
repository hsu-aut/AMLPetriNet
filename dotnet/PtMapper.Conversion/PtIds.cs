using System.Security.Cryptography;
using System.Xml.Linq;
using System.Text;

namespace PtMapper.Conversion;

/// <summary>
/// Deterministic CAEX ids derived from PNML ids.
///
/// The same net must produce the same AML ids on every run, otherwise an update
/// of an existing document would replace every element instead of touching the
/// ones that changed, and any reference a user added into the hierarchy would
/// dangle. Deriving them from the PNML id (plus a role discriminator, so the
/// element and its interfaces do not collide) achieves that without keeping a
/// mapping table anywhere.
/// </summary>
public static class PtIds
{
    /// <summary>Namespace seed, so ids from this mapper cannot collide with other tools'.</summary>
    private const string Namespace = "iso-pt-aml-mapper";

    /// <summary>
    /// Separates the parts of a seed. A character no id can contain, so
    /// For("a", "b") and For("ab") cannot collide. Written as an escape rather
    /// than as a literal NUL byte: a raw one in the source makes the file
    /// binary to git and invisible to grep.
    /// </summary>
    private const string Separator = "\0";

    public static string For(string kind, params string[] parts)
    {
        var seed = Namespace + Separator + kind + Separator + string.Join(Separator, parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);

        // Stamp version 5 and the RFC 4122 variant so the result is a well-formed
        // UUID. Note the byte indices: Guid(byte[]) reads the first three fields
        // little-endian, so the version nibble that shows up in the third group of
        // the rendered form comes from byte 7, not byte 6. The variant nibble does
        // come from byte 8, because the last eight bytes are read in order.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes).ToString("B");
    }

    public static string Element(string pnmlId) => For("element", pnmlId);

    /// <summary>
    /// Id for an interface, derived from the CAEX id of the element that owns
    /// it rather than from the PNML id. The element id is already distinct per
    /// document, so two copies of the same net never share an interface id,
    /// which they did when the PNML id alone was the seed: the same file
    /// imported twice yielded duplicate xs:IDs, and removing an arc in one copy
    /// took the other copy's links with it.
    /// </summary>
    public static string Interface(string ownerCaexId, string interfaceName) => For("interface", ownerCaexId, interfaceName);

    /// <summary>Id for a link, derived from the arc element's CAEX id for the same reason.</summary>
    public static string Link(string arcCaexId, string side) => For("link", arcCaexId, side);

    public static string Net(string netId) => For("net", netId);

    /// <summary>
    /// Id for an instance hierarchy. CAEX allows one and it is what lets a tool
    /// find its own hierarchy again after someone renames it in the editor.
    /// </summary>
    public static string Hierarchy(string hierarchyName) => For("hierarchy", hierarchyName);

    /// <summary>
    /// The id for an element, made distinct if that id is already taken.
    ///
    /// Two cases need this. A file with duplicate PNML ids is invalid and the
    /// validator reports it (rule PT01), but it must still not yield a CAEX
    /// document with two elements sharing an ID. And the same net imported twice
    /// into one document is perfectly legitimate, yet its ids would derive
    /// identically. CAEX ids are unique per document, not per hierarchy, so the
    /// check has to span the whole document.
    ///
    /// The suffix is deterministic, so re-running on the same input produces the
    /// same document.
    /// </summary>
    public static string DistinctElement(string pnmlId, IIdSpace taken) =>
        Distinct("element", pnmlId, taken);

    /// <summary>The id for a net element, distinct within the document.</summary>
    public static string DistinctNet(string netId, IIdSpace taken) => Distinct("net", netId, taken);

    private static string Distinct(string kind, string seed, IIdSpace taken)
    {
        ArgumentNullException.ThrowIfNull(taken);

        var candidate = For(kind, seed);
        for (var ordinal = 2; !taken.Claim(candidate); ordinal++)
            candidate = For(kind, seed, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return candidate;
    }
}

/// <summary>
/// Whatever decides if an id is still free. Kept as an interface so the writer
/// can ask the whole CAEX document rather than only the elements it created
/// itself.
/// </summary>
public interface IIdSpace
{
    /// <summary>Takes the id if it is free. False when it was already in use.</summary>
    bool Claim(string id);
}

/// <summary>An id space backed by a CAEX document, plus whatever this run added.</summary>
public sealed class CaexIdSpace : IIdSpace
{
    private readonly HashSet<string> _taken;

    /// <summary>
    /// Reads every id in the document once, rather than asking the engine per
    /// element: a per-element lookup doubled the time to write a 2000 element
    /// net. One pass over the XML also catches ids the engine's own index does
    /// not know about, such as those on classes or in a foreign hierarchy.
    /// </summary>
    public CaexIdSpace(Aml.Engine.CAEX.CAEXDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var root = document.CAEXFile?.Node;
        if (root == null) return;

        foreach (var attribute in root.DescendantsAndSelf().Attributes("ID"))
        {
            var value = attribute.Value;
            if (!string.IsNullOrEmpty(value)) _taken.Add(value);
        }
    }

    public bool Claim(string id) => _taken.Add(id);
}
