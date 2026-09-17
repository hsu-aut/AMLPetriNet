using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// The little bit of geometry both directions need: where an arc meets a node.
///
/// PNML does not carry the docking points, and neither does the modeler's
/// import path, so both sides derive them the same way. This mirrors
/// dockingPoint() in web/src/pnml/index.js; keep the two in step.
/// </summary>
public static class PtGeometry
{
    /// <summary>
    /// Where the line from the node centre towards <paramref name="target"/>
    /// leaves the shape. Places render as circles, transitions as rectangles,
    /// so the two cases are solved separately rather than approximating both
    /// with a bounding box.
    /// </summary>
    public static PtPoint DockingPoint(PtNode node, PtPoint target)
    {
        var bounds = node.Bounds;
        if (bounds == null) return new PtPoint(0, 0);

        var centre = bounds.Centre;
        var dx = target.X - centre.X;
        var dy = target.Y - centre.Y;
        if (dx == 0 && dy == 0) return centre;

        var a = bounds.Width / 2;
        var b = bounds.Height / 2;

        // A node with no extent has no border to dock against, and the formulas
        // below would produce NaN through 0/0. Foreign PNML with a <dimension>
        // of zero, or a hand-edited document, gets the centre instead.
        if (a <= 0 || b <= 0) return centre;

        double scale;
        if (node is PtPlace)
        {
            // Ellipse: solve (dx*s/a)^2 + (dy*s/b)^2 = 1 for s.
            scale = 1 / Math.Sqrt(Math.Pow(dx / a, 2) + Math.Pow(dy / b, 2));
        }
        else
        {
            // Rectangle: shrink to whichever edge is hit first.
            var byX = dx == 0 ? double.PositiveInfinity : a / Math.Abs(dx);
            var byY = dy == 0 ? double.PositiveInfinity : b / Math.Abs(dy);
            scale = Math.Min(byX, byY);
        }

        return new PtPoint(Round(centre.X + dx * scale), Round(centre.Y + dy * scale));
    }

    /// <summary>
    /// The full polyline of an arc: source docking point, the stored
    /// intermediate points, target docking point.
    /// </summary>
    public static List<PtPoint> Polyline(PtNet net, PtArc arc)
    {
        var source = net.FindNode(arc.SourceId);
        var target = net.FindNode(arc.TargetId);
        if (source?.Bounds == null || target?.Bounds == null) return new List<PtPoint>(arc.Waypoints);

        var towardsSource = arc.Waypoints.Count > 0 ? arc.Waypoints[0] : target.Bounds.Centre;
        var towardsTarget = arc.Waypoints.Count > 0 ? arc.Waypoints[^1] : source.Bounds.Centre;

        var points = new List<PtPoint> { DockingPoint(source, towardsSource) };
        points.AddRange(arc.Waypoints);
        points.Add(DockingPoint(target, towardsTarget));
        return points;
    }

    /// <summary>Keeps serialized coordinates short without visibly moving anything.</summary>
    private static double Round(double value) => Math.Round(value, 2);
}
