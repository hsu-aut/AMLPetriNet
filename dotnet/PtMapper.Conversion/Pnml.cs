using System.Globalization;
using System.Xml.Linq;
using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// Reads and writes PNML (ISO/IEC 15909-2) for basic place/transition nets.
///
/// This is the counterpart of web/src/pnml/index.js: the two must agree on the
/// same conventions, or a model loses layout on its way through the editor.
///
///  - &lt;position&gt; on a node is its CENTRE, &lt;dimension&gt; its width/height.
///  - &lt;graphics&gt; on an arc holds only the intermediate points; the docking
///    endpoints are recomputed from node geometry by whoever renders it.
///  - A name &lt;offset&gt; is relative to the node centre.
///  - A silent transition is a modeler concept, not a standard one, so it lives
///    in a &lt;toolspecific&gt; section that foreign readers ignore.
/// </summary>
public static class Pnml
{
    public static readonly XNamespace Ns = "http://www.pnml.org/version-2009/grammar/pnml";

    public const string PtNetType = "http://www.pnml.org/version-2009/grammar/ptnet";

    private const string ToolName = "ptn-js";
    private const string ToolVersion = "1.0";

    private static readonly (double Width, double Height) DefaultSize = (50, 50);
    private static readonly (double Width, double Height) DefaultSilentSize = (20, 50);

    // ── read ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first net of a PNML document. Convenience for the common case; a
    /// document can hold several, and <see cref="ReadAll"/> returns all of them.
    /// </summary>
    public static PtNet Read(string pnmlXml) => ReadAll(pnmlXml)[0];

    /// <summary>
    /// Every net in a PNML document. Pages are flattened: the AML side has no
    /// page concept, and the modeler emits a single page.
    /// </summary>
    public static List<PtNet> ReadAll(string pnmlXml)
    {
        if (string.IsNullOrWhiteSpace(pnmlXml))
            throw new ArgumentException("PNML document is empty.", nameof(pnmlXml));

        return ReadAll(new StringReader(pnmlXml));
    }

    /// <summary>
    /// Every net in a PNML file, read from its bytes.
    ///
    /// Prefer this over the string overload for anything that came from disk or
    /// over the wire: the XML declaration may name an encoding other than UTF-8,
    /// and a caller that decoded the bytes itself has already mangled every
    /// character outside ASCII by the time the parser could have honoured it.
    /// </summary>
    public static List<PtNet> ReadAll(Stream pnml)
    {
        ArgumentNullException.ThrowIfNull(pnml);
        return Read(reader => System.Xml.XmlReader.Create(pnml, reader));
    }

    private static List<PtNet> ReadAll(TextReader text) =>
        Read(settings => System.Xml.XmlReader.Create(text, settings));

    private static List<PtNet> Read(Func<System.Xml.XmlReaderSettings, System.Xml.XmlReader> open)
    {
        XDocument doc;
        try
        {
            // A PNML file usually comes from somewhere else, so the parser is
            // configured to refuse a DTD outright. That closes both external
            // entity resolution and entity expansion; a legitimate P/T net has
            // no use for either, and with DTDs prohibited there is no entity
            // budget left to set.
            var settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = open(settings);
            doc = XDocument.Load(reader);
        }
        catch (System.Xml.XmlException e)
        {
            throw new FormatException("PNML document is not well-formed XML: " + e.Message, e);
        }

        var netElements = doc.Root?.Elements(Ns + "net").ToList() ?? new List<XElement>();
        if (netElements.Count == 0)
        {
            var root = doc.Root?.Name;
            throw new FormatException(root == null || root.Namespace == Ns
                ? "PNML document contains no <net>."
                : $"This is not a PNML document: the root element is <{root.LocalName}> in namespace "
                  + $"'{root.Namespace}', and PNML uses '{Ns.NamespaceName}'.");
        }

        return netElements.Select(ReadNet).ToList();
    }

    private static PtNet ReadNet(XElement netElement)
    {
        var net = new PtNet
        {
            Id = netElement.Attribute("id")?.Value ?? "net",
            Name = Annotation(netElement, "name"),
        };

        var content = Flatten(netElement).ToList();

        // A page reaches a node on another page through a reference node whose
        // "ref" names the real one, possibly through another reference. The
        // AML side has no pages, so arcs are pointed at the concrete node.
        var references = content
            .Where(e => e.Name == Ns + "referencePlace" || e.Name == Ns + "referenceTransition")
            .Select(e => (Id: e.Attribute("id")?.Value, Ref: e.Attribute("ref")?.Value))
            .Where(r => !string.IsNullOrEmpty(r.Id) && !string.IsNullOrEmpty(r.Ref))
            .GroupBy(r => r.Id!)
            .ToDictionary(g => g.Key, g => g.First().Ref!, StringComparer.Ordinal);

        foreach (var element in content.Where(e => e.Name == Ns + "place"))
        {
            var place = new PtPlace
            {
                Id = element.Attribute("id")?.Value ?? "",
                Name = Annotation(element, "name"),
                LabelOffset = ReadNameOffset(element),
            };
            var marking = Annotation(element, "initialMarking");
            if (marking != null && int.TryParse(marking, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens))
                place.InitialMarking = Math.Max(0, tokens);

            place.Bounds = ReadBounds(element, DefaultSize);
            if (place.Id.Length > 0) net.Places.Add(place);
        }

        foreach (var element in content.Where(e => e.Name == Ns + "transition"))
        {
            var transition = new PtTransition
            {
                Id = element.Attribute("id")?.Value ?? "",
                Name = Annotation(element, "name"),
                LabelOffset = ReadNameOffset(element),
                IsSilent = ReadSilent(element),
            };
            transition.Bounds = ReadBounds(element, transition.IsSilent ? DefaultSilentSize : DefaultSize);
            if (transition.Id.Length > 0) net.Transitions.Add(transition);
        }

        var nodeIds = net.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var element in content.Where(e => e.Name == Ns + "arc"))
        {
            var arc = new PtArc
            {
                Id = element.Attribute("id")?.Value ?? "",
                Name = Annotation(element, "name"),
                SourceId = element.Attribute("source")?.Value ?? "",
                TargetId = element.Attribute("target")?.Value ?? "",
            };
            var inscription = Annotation(element, "inscription");
            if (inscription != null && int.TryParse(inscription, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight > 0)
                arc.Weight = weight;

            arc.LabelOffset = ReadAnnotationOffset(element, "inscription");

            var graphics = element.Elements(Ns + "graphics").FirstOrDefault();
            if (graphics != null)
            {
                foreach (var position in graphics.Elements(Ns + "position"))
                    arc.Waypoints.Add(new PtPoint(Number(position.Attribute("x")), Number(position.Attribute("y"))));
            }

            if (arc.Id.Length == 0)
            {
                net.Warnings.Add("An arc without an id was left out.");
                continue;
            }

            arc.SourceId = ResolveReference(arc.SourceId, references, nodeIds);
            arc.TargetId = ResolveReference(arc.TargetId, references, nodeIds);
            if (arc.SourceId.Length == 0 || arc.TargetId.Length == 0)
            {
                net.Warnings.Add($"Arc '{arc.Id}' was left out: its {(arc.SourceId.Length == 0 ? "source" : "target")} "
                    + "does not resolve to a place or transition of this net.");
                net.Unresolved.Add(arc.Id);
                continue;
            }

            net.Arcs.Add(arc);
        }

        return net;
    }

    /// <summary>
    /// Follows a chain of reference nodes to the concrete node, or to nothing.
    /// An id that already names a node is returned as it is; a cycle of
    /// references ends the walk.
    /// </summary>
    private static string ResolveReference(string id, Dictionary<string, string> references, HashSet<string> nodeIds)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = id;
        while (current.Length > 0 && !nodeIds.Contains(current) && references.TryGetValue(current, out var next) && visited.Add(current))
            current = next;
        return nodeIds.Contains(current) ? current : "";
    }

    /// <summary>
    /// Places, transitions, arcs and reference nodes of a net, with nested pages
    /// flattened. Iterative, so a file with absurdly deep page nesting cannot
    /// overflow the stack.
    /// </summary>
    private static IEnumerable<XElement> Flatten(XElement root)
    {
        // Breadth over the pages, document order within each: a page's content
        // comes out where a reader expects it, and the pages are visited in
        // the order they appear.
        var pending = new Queue<XElement>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var parent = pending.Dequeue();
            foreach (var child in parent.Elements())
            {
                if (child.Name == Ns + "page")
                    pending.Enqueue(child);
                else if (child.Name == Ns + "place" || child.Name == Ns + "transition" || child.Name == Ns + "arc"
                         || child.Name == Ns + "referencePlace" || child.Name == Ns + "referenceTransition")
                    yield return child;
            }
        }
    }

    /// <summary>
    /// The offset an annotation's label was dragged to, if the file carries one:
    /// &lt;tag&gt;&lt;graphics&gt;&lt;offset x y/&gt;&lt;/graphics&gt;&lt;/tag&gt;.
    /// </summary>
    private static PtPoint? ReadAnnotationOffset(XElement parent, string tag)
    {
        var offset = parent.Elements(Ns + tag)
            .Elements(Ns + "graphics")
            .Elements(Ns + "offset")
            .FirstOrDefault();

        return offset == null ? null : new PtPoint(Number(offset.Attribute("x")), Number(offset.Attribute("y")));
    }

    /// <summary>Value of a PNML annotation: &lt;tag&gt;&lt;text&gt;value&lt;/text&gt;&lt;/tag&gt;.</summary>
    private static string? Annotation(XElement parent, string tag)
    {
        var element = parent.Elements(Ns + tag).FirstOrDefault();
        if (element == null) return null;
        var text = element.Elements(Ns + "text").FirstOrDefault() ?? element;
        var value = text.Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static PtBounds? ReadBounds(XElement node, (double Width, double Height) fallback)
    {
        var graphics = node.Elements(Ns + "graphics").FirstOrDefault();
        var position = graphics?.Elements(Ns + "position").FirstOrDefault();
        if (position == null) return null;

        var dimension = graphics!.Elements(Ns + "dimension").FirstOrDefault();
        // Foreign PNML routinely omits <dimension>; fall back to the size the
        // modeler would give the element itself.
        var width = dimension != null ? Number(dimension.Attribute("x")) : 0;
        var height = dimension != null ? Number(dimension.Attribute("y")) : 0;
        if (width <= 0) width = fallback.Width;
        if (height <= 0) height = fallback.Height;

        var centreX = Number(position.Attribute("x"));
        var centreY = Number(position.Attribute("y"));
        return new PtBounds(centreX - width / 2, centreY - height / 2, width, height);
    }

    private static PtPoint? ReadNameOffset(XElement node)
    {
        var offset = node.Elements(Ns + "name").FirstOrDefault()
            ?.Elements(Ns + "graphics").FirstOrDefault()
            ?.Elements(Ns + "offset").FirstOrDefault();
        return offset == null ? null : new PtPoint(Number(offset.Attribute("x")), Number(offset.Attribute("y")));
    }

    private static bool ReadSilent(XElement node) =>
        node.Elements(Ns + "toolspecific")
            .Elements(Ns + "silent")
            .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A coordinate. NaN and infinity parse happily out of "NaN" or a number
    /// too large for a double, and would then travel into the AML document and
    /// into the diagram, where they render as nothing at all.
    /// </summary>
    private static double Number(XAttribute? attribute) =>
        attribute != null
        && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : 0;

    // ── write ───────────────────────────────────────────────────────────────

    /// <param name="indent">
    /// Indented output for a file a person reads. Off by default, because the
    /// bridge sends this text to the modeler and PNML annotations are mixed
    /// content, where added whitespace is not free.
    /// </param>
    public static string Write(PtNet net, bool indent = false)
    {
        var page = new XElement(Ns + "page", new XAttribute("id", net.Id + "_page"));

        foreach (var place in net.Places)
        {
            var element = new XElement(Ns + "place", new XAttribute("id", place.Id));
            AppendName(element, place.Name, place.LabelOffset);
            // A marking of 0 is the default and carries no information.
            if (place.InitialMarking != 0) AppendAnnotation(element, "initialMarking", place.InitialMarking.ToString(CultureInfo.InvariantCulture));
            AppendNodeGraphics(element, place.Bounds);
            page.Add(element);
        }

        foreach (var transition in net.Transitions)
        {
            var element = new XElement(Ns + "transition", new XAttribute("id", transition.Id));
            AppendName(element, transition.Name, transition.LabelOffset);
            if (transition.IsSilent)
            {
                element.Add(new XElement(Ns + "toolspecific",
                    new XAttribute("tool", ToolName),
                    new XAttribute("version", ToolVersion),
                    new XElement(Ns + "silent", "true")));
            }
            AppendNodeGraphics(element, transition.Bounds);
            page.Add(element);
        }

        foreach (var arc in net.Arcs)
        {
            var element = new XElement(Ns + "arc",
                new XAttribute("id", arc.Id),
                new XAttribute("source", arc.SourceId),
                new XAttribute("target", arc.TargetId));
            AppendName(element, arc.Name, null);
            // A weight of 1 is the default and is left implicit, unless the
            // label has a position of its own. Other editors write the
            // inscription 1 with an offset, and dropping it lost that position
            // on the way AML -> PNML -> AML.
            if (arc.Weight != 1 || arc.LabelOffset != null)
                AppendAnnotation(element, "inscription", arc.Weight.ToString(CultureInfo.InvariantCulture), arc.LabelOffset);
            if (arc.Waypoints.Count > 0)
            {
                element.Add(new XElement(Ns + "graphics",
                    arc.Waypoints.Select(p => new XElement(Ns + "position",
                        new XAttribute("x", Format(p.X)),
                        new XAttribute("y", Format(p.Y))))));
            }
            page.Add(element);
        }

        var netElement = new XElement(Ns + "net",
            new XAttribute("id", net.Id),
            new XAttribute("type", PtNetType));
        AppendName(netElement, net.Name, null);
        netElement.Add(page);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "pnml", netElement));

        return document.Declaration + Environment.NewLine
            + document.ToString(indent ? SaveOptions.None : SaveOptions.DisableFormatting);
    }

    private static void AppendName(XElement parent, string? text, PtPoint? offset)
    {
        if (string.IsNullOrEmpty(text)) return;
        var name = new XElement(Ns + "name", new XElement(Ns + "text", text));
        if (offset != null)
        {
            name.Add(new XElement(Ns + "graphics",
                new XElement(Ns + "offset",
                    new XAttribute("x", Format(offset.X)),
                    new XAttribute("y", Format(offset.Y)))));
        }
        parent.Add(name);
    }

    /// <summary>
    /// A PNML annotation such as initialMarking or inscription, optionally with
    /// the offset its label was dragged to.
    /// </summary>
    private static void AppendAnnotation(XElement parent, string tag, string text, PtPoint? offset = null)
    {
        var annotation = new XElement(Ns + tag, new XElement(Ns + "text", text));

        if (offset != null)
        {
            annotation.Add(new XElement(Ns + "graphics",
                new XElement(Ns + "offset",
                    new XAttribute("x", Format(offset.X)),
                    new XAttribute("y", Format(offset.Y)))));
        }

        parent.Add(annotation);
    }

    private static void AppendNodeGraphics(XElement parent, PtBounds? bounds)
    {
        if (bounds == null) return;

        // The centre follows the size that is written, so that reading it back
        // puts the top-left corner where it was and only a size off the grid
        // moves.
        var width = RoundSize(bounds.Width);
        var height = RoundSize(bounds.Height);
        parent.Add(new XElement(Ns + "graphics",
            new XElement(Ns + "position",
                new XAttribute("x", Format(bounds.X + width / 2)),
                new XAttribute("y", Format(bounds.Y + height / 2))),
            new XElement(Ns + "dimension",
                new XAttribute("x", Format(width)),
                new XAttribute("y", Format(height)))));
    }

    /// <summary>
    /// A node size as PNML allows it. The grammar types &lt;dimension&gt; as a
    /// positive decimal with at most four digits, one of them after the point,
    /// so 50.25 is invalid and 12000 is too. Positions have no such limit.
    /// </summary>
    internal static double RoundSize(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 0.1;
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        if (rounded >= 1000) return Math.Min(Math.Round(rounded, MidpointRounding.AwayFromZero), 9999);
        return Math.Max(rounded, 0.1);
    }

    /// <summary>
    /// Matches how the JavaScript side prints numbers, without a trailing ".0".
    /// The two implementations have to agree on coordinate spelling or a diagram
    /// would appear to have changed every time it crossed between them. They do
    /// not have to produce identical bytes overall: the XML writers differ in
    /// whitespace, and nothing compares their output directly. Verified by
    /// web/tools/verify-interop.mjs.
    /// </summary>
    private static string Format(double value)
    {
        // Nothing non-finite may reach the file: "NaN" is not a PNML coordinate
        // and every reader downstream would have to guess what to do with it.
        if (!double.IsFinite(value)) value = 0;

        return value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
