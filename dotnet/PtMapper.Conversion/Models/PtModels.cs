namespace PtMapper.Conversion.Models;

/// <summary>A point in diagram coordinates.</summary>
public sealed record PtPoint(double X, double Y);

/// <summary>
/// Bounds of a node, top-left anchored, matching DC::Bounds and the modeler's
/// native representation. PNML carries the centre instead; the conversion
/// between the two happens at the PNML boundary, not here.
/// </summary>
public sealed record PtBounds(double X, double Y, double Width, double Height)
{
    public PtPoint Centre => new(X + Width / 2, Y + Height / 2);
}

public abstract class PtNode
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public PtBounds? Bounds { get; set; }

    /// <summary>Label placement relative to the node centre, when the editor moved it.</summary>
    public PtPoint? LabelOffset { get; set; }
}

public sealed class PtPlace : PtNode
{
    /// <summary>Static initial marking. Non-negative; 0 is the default.</summary>
    public int InitialMarking { get; set; }
}

public sealed class PtTransition : PtNode
{
    /// <summary>
    /// ISO/IEC 15909-1 has no silent transition. The modeler does, and PNML
    /// carries it in a toolspecific section, so it is kept here to survive a
    /// round trip rather than being asserted as part of the standard.
    /// </summary>
    public bool IsSilent { get; set; }
}

public sealed class PtArc
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";

    /// <summary>Arc weight. Positive; 1 is the default and is not serialized.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Intermediate routing points, without the two docking endpoints.</summary>
    public List<PtPoint> Waypoints { get; } = new();

    /// <summary>
    /// Where the arc's label sits, relative to the middle of its polyline.
    ///
    /// An arc has one label and it is the inscription, the weight: that is what
    /// the modeler draws next to an arc and what a user can drag. The arc's name
    /// is not rendered, so it has no position of its own. The reference point is
    /// the middle of the middle segment, which is where the modeler puts the
    /// label when a file brings no position, so both sides mean the same thing.
    /// </summary>
    public PtPoint? LabelOffset { get; set; }
}

/// <summary>One place/transition net: the unit a PNML &lt;net&gt; and an AML PT_Net hold.</summary>
public sealed class PtNet
{
    public string Id { get; set; } = "net";
    public string? Name { get; set; }

    public List<PtPlace> Places { get; } = new();
    public List<PtTransition> Transitions { get; } = new();
    public List<PtArc> Arcs { get; } = new();

    public IEnumerable<PtNode> Nodes => Places.Cast<PtNode>().Concat(Transitions);

    /// <summary>
    /// What a reader had to leave out, in words. Empty for a clean file. A
    /// reader that drops something without saying so leaves the next writer
    /// to delete it from the document, which is how an arc with one missing
    /// link once vanished from a file on the next Update.
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Ids of elements the reader saw but could not represent, in the id form
    /// the net uses. An updater must not treat these as "no longer in the
    /// diagram": they never reached the diagram in the first place.
    /// </summary>
    public HashSet<string> Unresolved { get; } = new(StringComparer.Ordinal);

    public PtNode? FindNode(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}
