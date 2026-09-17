using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

/// <summary>
/// Gives positions to nodes that have none.
///
/// PNML carries layout only if the producing tool bothered, and plenty do not.
/// Without this every such node ends up at the origin, so the whole net is one
/// pile and the file is effectively unusable in the editor. Nodes that already
/// have bounds are never moved: someone's layout is theirs.
///
/// The arrangement follows the layered (Sugiyama-style) method: the
/// cycles are broken, the nodes are put in columns along the longest path from
/// where the net starts, the order within a column is chosen so that as few
/// arcs as possible cross, and the arcs that close a cycle are routed around the
/// drawing instead of straight across it. Everything is deterministic, ties
/// included: the same net always comes out the same way, whatever its elements
/// are called.
/// </summary>
public static class PtLayout
{
    public const double NodeSize = 50;
    public const double SilentTransitionWidth = 20;

    private const double ColumnSpacing = 140;
    private const double RowSpacing = 110;

    /// <summary>Distance between the drawing and a routed arc, and between two routed arcs.</summary>
    private const double LaneGap = 30;

    /// <summary>Above this many nodes the neighbour swapping is skipped; see Transpose.</summary>
    private const int TransposeLimit = 80;

    private const double OriginX = 80;
    private const double OriginY = 80;

    /// <summary>
    /// Positions every node that has no bounds. Returns how many were placed, so
    /// a caller can tell the user their file had no layout.
    /// </summary>
    public static int ArrangeMissing(PtNet net)
    {
        ArgumentNullException.ThrowIfNull(net);

        var unplaced = net.Nodes.Where(n => n.Bounds == null).ToList();
        if (unplaced.Count == 0) return 0;

        var (column, back) = AssignColumns(net);
        var row = Straighten(net, column, AssignRows(net, column, back), back);

        foreach (var node in unplaced)
        {
            var width = node is PtTransition { IsSilent: true } ? SilentTransitionWidth : NodeSize;

            node.Bounds = new PtBounds(
                OriginX + column[node.Id] * ColumnSpacing,
                OriginY + row[node.Id] * RowSpacing,
                width,
                NodeSize);
        }

        RouteBackEdges(net, unplaced, column, row, back);

        return unplaced.Count;
    }

    // ── columns ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The column each node sits in, and the arcs that had to be set aside to
    /// get there.
    ///
    /// A column is the node's distance along the longest path from where the
    /// net starts, measured on the net with its cycles broken. Breaking them
    /// first is what keeps a column from running away: on a graph without
    /// cycles the longest path is bounded by the number of nodes, whereas
    /// relaxing it over a cycle once laid a ten-node loop out seventeen thousand
    /// pixels wide.
    ///
    /// Nodes that nothing feeds are then pulled right, to just before the
    /// earliest node they feed. Without that, an input place supplying only a
    /// late transition would sit at the far left with a long arc across the
    /// whole diagram; the MPS500 quality-check net has two of those.
    /// </summary>
    private static (Dictionary<string, int> Column, HashSet<(string From, string To)> Back) AssignColumns(PtNet net)
    {
        var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var predecessors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in net.Nodes)
        {
            successors.TryAdd(node.Id, new List<string>());
            predecessors.TryAdd(node.Id, new List<string>());
        }

        var edges = net.Arcs
            .Where(a => successors.ContainsKey(a.SourceId) && successors.ContainsKey(a.TargetId))
            .Where(a => a.SourceId != a.TargetId)
            .Select(a => (From: a.SourceId, To: a.TargetId))
            .Distinct()
            .ToList();

        var back = BackEdges(net, edges);
        foreach (var (from, to) in edges)
        {
            if (back.Contains((from, to))) continue;
            successors[from].Add(to);
            predecessors[to].Add(from);
        }

        // Longest path, in topological order so every node is settled before
        // anything that depends on it. Built with TryAdd rather than
        // ToDictionary: a net with duplicate ids is invalid and the validator
        // reports it, but laying it out must not throw on the way to that
        // message.
        var column = new Dictionary<string, int>(StringComparer.Ordinal);
        var waiting = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in net.Nodes)
        {
            column.TryAdd(node.Id, 0);
            waiting.TryAdd(node.Id, predecessors[node.Id].Count);
        }

        var ready = new Queue<string>(
            waiting.Where(e => e.Value == 0).Select(e => e.Key).OrderBy(id => id, StringComparer.Ordinal));

        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            foreach (var next in successors[id])
            {
                if (column[next] < column[id] + 1) column[next] = column[id] + 1;
                if (--waiting[next] == 0) ready.Enqueue(next);
            }
        }

        foreach (var node in net.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            if (predecessors[node.Id].Count > 0) continue;
            if (successors[node.Id].Count == 0) continue;

            column[node.Id] = successors[node.Id].Min(f => column[f]) - 1;
        }

        // A pulled source may have gone left of zero; shift everything back.
        var leftmost = column.Values.DefaultIfEmpty(0).Min();
        if (leftmost < 0)
            foreach (var id in column.Keys.ToList())
                column[id] -= leftmost;

        return (column, back);
    }

    /// <summary>
    /// The arcs that close a cycle, found by a depth-first walk: an arc back to
    /// a node still on the current path is one of them.
    ///
    /// Where the walk starts decides which arc of a cycle counts as the closing
    /// one, and that decides how the whole net reads. The walk starts at the
    /// places that hold tokens, because in a Petri net that is where the process
    /// begins: in the MPS500 example Idle carries the token, and the arcs that
    /// return to it from Release and Discard are the ones that close the loop.
    /// Starting at a place merely nothing feeds instead, Result_NOK, laid the
    /// loop out beginning in the middle, with every node in one row. Places
    /// nothing feeds come next, then everything else, each group in id order so
    /// the result is deterministic.
    /// </summary>
    private static HashSet<(string From, string To)> BackEdges(PtNet net, List<(string From, string To)> edges)
    {
        var outgoing = edges
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).OrderBy(t => t, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var back = new HashSet<(string, string)>();

        // 0 unseen, 1 on the current path, 2 finished. Iterative, because a long
        // chain of places and transitions would otherwise be a deep recursion.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var hasIncoming = edges.Select(e => e.To).ToHashSet(StringComparer.Ordinal);

        var roots = net.Nodes
            .OrderBy(n => n is PtPlace { InitialMarking: > 0 } ? 0 : hasIncoming.Contains(n.Id) ? 2 : 1)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => n.Id);

        foreach (var root in roots)
        {
            if (state.GetValueOrDefault(root, 0) != 0) continue;

            var stack = new Stack<(string Id, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;

            while (stack.Count > 0)
            {
                var (id, next) = stack.Pop();
                var children = outgoing.GetValueOrDefault(id, new List<string>());

                if (next >= children.Count)
                {
                    state[id] = 2;
                    continue;
                }

                stack.Push((id, next + 1));
                var child = children[next];

                switch (state.GetValueOrDefault(child, 0))
                {
                    case 1:
                        back.Add((id, child));
                        break;
                    case 0:
                        state[child] = 1;
                        stack.Push((child, 0));
                        break;
                }
            }
        }

        return back;
    }

    // ── rows ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The row each node sits in, chosen so that the drawing has as few defects
    /// as possible.
    ///
    /// The columns are settled by then; what is left is the order within each
    /// column. This is the ordering phase of the layered method: every node
    /// moves to the average row of its neighbours in the column beside it, the
    /// columns are swept forwards and backwards, and neighbours are swapped while
    /// that helps. The arcs that close a cycle take no part, because they are
    /// routed around the drawing afterwards and would otherwise pull their ends
    /// towards the far side of it.
    ///
    /// Every arrangement the sweep passes through is measured and the best one
    /// kept, and several starting orders are tried. Measuring rather than
    /// trusting the last pass is what makes the outcome independent of the ids.
    /// </summary>
    private static Dictionary<string, double> AssignRows(
        PtNet net, Dictionary<string, int> column, HashSet<(string From, string To)> back)
    {
        var byColumn = net.Nodes
            .DistinctBy(n => n.Id)
            .GroupBy(n => column[n.Id])
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => n.Id).ToList());

        // Neighbours on either side, so a sweep can look the way it is going.
        var left = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var right = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in net.Nodes)
        {
            left.TryAdd(node.Id, new List<string>());
            right.TryAdd(node.Id, new List<string>());
        }

        foreach (var arc in net.Arcs)
        {
            if (!left.ContainsKey(arc.SourceId) || !left.ContainsKey(arc.TargetId)) continue;
            if (column[arc.SourceId] == column[arc.TargetId]) continue;
            if (back.Contains((arc.SourceId, arc.TargetId))) continue;

            var (before, after) = column[arc.SourceId] < column[arc.TargetId]
                ? (arc.SourceId, arc.TargetId)
                : (arc.TargetId, arc.SourceId);

            right[before].Add(after);
            left[after].Add(before);
        }

        var columns = byColumn.Keys.OrderBy(c => c).ToList();

        List<KeyValuePair<int, List<string>>> Start(Func<IEnumerable<string>, IEnumerable<string>> order) =>
            byColumn.Select(e => new KeyValuePair<int, List<string>>(e.Key, order(e.Value).ToList())).ToList();

        var starts = new List<List<KeyValuePair<int, List<string>>>>
        {
            Start(ids => ids),
            Start(Enumerable.Reverse),
            // Busiest first: a node many arcs touch has the most to gain from
            // sitting in the middle, and this start often reaches that directly.
            Start(ids => ids
                .OrderByDescending(id => left[id].Count + right[id].Count)
                .ThenBy(id => id, StringComparer.Ordinal)),
        };

        Dictionary<string, double>? best = null;
        var fewest = int.MaxValue;

        foreach (var initial in starts)
        {
            var layers = initial.ToDictionary(e => e.Key, e => e.Value.ToList());
            var row = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (_, ids) in layers)
                for (var i = 0; i < ids.Count; i++) row[ids[i]] = i;

            void Keep()
            {
                var defects = Defects(net, column, row, back);
                if (defects >= fewest) return;
                fewest = defects;
                best = new Dictionary<string, double>(row, StringComparer.Ordinal);
            }

            Keep();

            // Six sweeps: the arrangement settles well before that on nets of
            // the size a person draws, and the cost is trivial either way.
            for (var pass = 0; pass < 6 && fewest > 0; pass++)
            {
                var forwards = pass % 2 == 0;
                var order = forwards ? columns : Enumerable.Reverse(columns).ToList();

                foreach (var col in order)
                {
                    var neighbours = forwards ? left : right;

                    var ordered = layers[col]
                        .Select(id => (Id: id, Key: Barycentre(id, neighbours, row) ?? row[id]))
                        .OrderBy(e => e.Key)
                        .ThenBy(e => row[e.Id])
                        .ThenBy(e => e.Id, StringComparer.Ordinal)
                        .Select(e => e.Id)
                        .ToList();

                    layers[col] = ordered;
                    for (var i = 0; i < ordered.Count; i++) row[ordered[i]] = i;
                    Keep();
                }

                if (Transpose(net, column, layers, row, columns, back)) Keep();
            }

            if (fewest == 0) break;
        }

        return best ?? byColumn
            .SelectMany(e => e.Value.Select((id, i) => (id, i)))
            .ToDictionary(e => e.id, e => (double)e.i, StringComparer.Ordinal);
    }

    /// <summary>
    /// Moves each column up or down as a whole so that nodes line up with the
    /// nodes they are connected to.
    ///
    /// The ordering fills every column from the top, so a column with one node
    /// put it in the first row even when everything it connects to sat lower.
    /// On the MPS500 net that bent the main path: Idle and Start in the top
    /// row, Checking below them, and a diagonal arc between. Here the tallest
    /// column stays where it is, and every other column, starting with the one
    /// most tied to what is already settled, takes the offset that brings its
    /// nodes closest to their settled neighbours. Offsets go in half rows, so a
    /// column of two can sit symmetrically between the three nodes it joins.
    ///
    /// The order within a column does not change, so no crossing is added. A
    /// shift can still put a long arc through a node, so the result is kept only
    /// if it has no more defects than the arrangement it started from.
    /// </summary>
    private static Dictionary<string, double> Straighten(
        PtNet net, Dictionary<string, int> column, Dictionary<string, double> row,
        HashSet<(string From, string To)> back)
    {
        var neighbours = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in column.Keys) neighbours[id] = new List<string>();
        foreach (var arc in net.Arcs)
        {
            if (!column.ContainsKey(arc.SourceId) || !column.ContainsKey(arc.TargetId)) continue;
            if (arc.SourceId == arc.TargetId || back.Contains((arc.SourceId, arc.TargetId))) continue;
            neighbours[arc.SourceId].Add(arc.TargetId);
            neighbours[arc.TargetId].Add(arc.SourceId);
        }

        var members = column.GroupBy(e => e.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Key).OrderBy(id => row[id]).ToList());
        if (members.Count < 2) return row;

        var shifted = new Dictionary<string, double>(row, StringComparer.Ordinal);
        var settled = new HashSet<string>(StringComparer.Ordinal);

        // The tallest column is the reference. Ties go to the leftmost.
        var anchor = members.OrderByDescending(m => m.Value.Count).ThenBy(m => m.Key).First().Key;
        foreach (var id in members[anchor]) settled.Add(id);
        var open = members.Keys.Where(c => c != anchor).ToHashSet();

        while (open.Count > 0)
        {
            // The column with the most arcs into what is settled goes next.
            var next = open
                .Select(c => (Column: c, Ties: members[c].Sum(id => neighbours[id].Count(settled.Contains))))
                .OrderByDescending(e => e.Ties)
                .ThenBy(e => e.Column)
                .First();
            open.Remove(next.Column);

            var wishes = members[next.Column]
                .Select(id => (Id: id, Settled: neighbours[id].Where(settled.Contains).ToList()))
                .Where(e => e.Settled.Count > 0)
                .Select(e => e.Settled.Average(n => shifted[n]) - row[e.Id])
                .ToList();

            if (wishes.Count > 0)
            {
                var offset = Math.Round(wishes.Average() * 2, MidpointRounding.AwayFromZero) / 2;
                foreach (var id in members[next.Column]) shifted[id] = row[id] + offset;
            }

            foreach (var id in members[next.Column]) settled.Add(id);
        }

        // Nothing above the top row: the drawing starts where it did.
        var top = shifted.Values.Min();
        foreach (var id in shifted.Keys.ToList()) shifted[id] -= top;

        return Defects(net, column, shifted, back) <= Defects(net, column, row, back) ? shifted : row;
    }

    /// <summary>
    /// The average row of a node's neighbours in the neighbouring column, or
    /// null when it has none: a node nothing points at keeps where it is rather
    /// than being pulled to the top.
    /// </summary>
    private static double? Barycentre(
        string id, Dictionary<string, List<string>> neighbours, Dictionary<string, double> row)
    {
        var of = neighbours[id];
        return of.Count == 0 ? null : of.Average(n => row[n]);
    }

    /// <summary>
    /// Swaps neighbours within a column for as long as that removes defects.
    /// Returns whether anything moved.
    ///
    /// Only for nets a person would look at: the defect count is quadratic in
    /// the number of arcs, and trying every neighbour pair against it would turn
    /// a thousand-node import into a wait. Above the threshold the averaging
    /// alone decides.
    /// </summary>
    private static bool Transpose(
        PtNet net,
        Dictionary<string, int> column,
        Dictionary<int, List<string>> layers,
        Dictionary<string, double> row,
        List<int> columns,
        HashSet<(string From, string To)> back)
    {
        if (net.Nodes.Count() > TransposeLimit) return false;

        var moved = false;
        var improving = true;

        while (improving)
        {
            improving = false;

            foreach (var col in columns)
            {
                var ids = layers[col];
                for (var i = 0; i + 1 < ids.Count; i++)
                {
                    var before = Defects(net, column, row, back);

                    (row[ids[i]], row[ids[i + 1]]) = (row[ids[i + 1]], row[ids[i]]);
                    (ids[i], ids[i + 1]) = (ids[i + 1], ids[i]);

                    if (Defects(net, column, row, back) < before)
                    {
                        improving = true;
                        moved = true;
                    }
                    else
                    {
                        (row[ids[i]], row[ids[i + 1]]) = (row[ids[i + 1]], row[ids[i]]);
                        (ids[i], ids[i + 1]) = (ids[i + 1], ids[i]);
                    }
                }
            }
        }

        return moved;
    }

    /// <summary>
    /// What makes a drawing hard to read, counted the way the eye sees it: two
    /// arcs crossing, and an arc running straight through a node that is not
    /// one of its ends. The second counts double, because it is worse: a
    /// crossing is ambiguous for a moment, an arc through a node makes it look
    /// connected to something it is not.
    ///
    /// Counting only crossings once reported the MPS500 net with its loop as
    /// perfect while two arcs ran through four nodes each. The arcs that close a
    /// cycle are left out, since they are routed around the drawing.
    /// </summary>
    private static int Defects(
        PtNet net, Dictionary<string, int> column, Dictionary<string, double> row, HashSet<(string From, string To)> back)
    {
        (double X, double Y) At(string id) => (column[id] * ColumnSpacing, row[id] * RowSpacing);

        var segments = net.Arcs
            .Where(a => column.ContainsKey(a.SourceId) && column.ContainsKey(a.TargetId))
            .Where(a => !back.Contains((a.SourceId, a.TargetId)))
            .Select(a => (From: a.SourceId, To: a.TargetId, A: At(a.SourceId), B: At(a.TargetId)))
            .ToList();

        static double Side((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        var defects = 0;

        for (var i = 0; i < segments.Count; i++)
        for (var j = i + 1; j < segments.Count; j++)
        {
            var (p1, p2) = (segments[i].A, segments[i].B);
            var (p3, p4) = (segments[j].A, segments[j].B);

            var d1 = Side(p3, p4, p1);
            var d2 = Side(p3, p4, p2);
            var d3 = Side(p1, p2, p3);
            var d4 = Side(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                defects++;
        }

        // A node counts as hit when its centre lies within half a node of the
        // arc, strictly between the arc's ends.
        var half = NodeSize / 2;
        foreach (var segment in segments)
        {
            var dx = segment.B.X - segment.A.X;
            var dy = segment.B.Y - segment.A.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared == 0) continue;

            foreach (var (id, _) in column)
            {
                if (id == segment.From || id == segment.To) continue;

                var c = At(id);
                var t = ((c.X - segment.A.X) * dx + (c.Y - segment.A.Y) * dy) / lengthSquared;
                if (t <= 0 || t >= 1) continue;

                var distance = Math.Abs(Side(segment.A, segment.B, c)) / Math.Sqrt(lengthSquared);
                if (distance < half) defects += 2;
            }
        }

        return defects;
    }

    // ── routing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the arcs that close a cycle around the drawing rather than straight
    /// back across it.
    ///
    /// A straight return arc from the end of a loop to its start runs through
    /// everything in between. Routed, it leaves its source vertically, runs
    /// along a lane above or below the drawing, and comes back down or up into
    /// its target, which is how a loop is drawn by hand. Above is taken when
    /// nothing sits above either end in its column, below when nothing sits
    /// below; each routed arc gets its own lane so two of them never share a
    /// segment.
    ///
    /// Only arcs between two nodes this call placed, and only arcs that bring
    /// no bend points of their own: a route someone drew is theirs.
    /// </summary>
    private static void RouteBackEdges(
        PtNet net,
        List<PtNode> unplaced,
        Dictionary<string, int> column,
        Dictionary<string, double> row,
        HashSet<(string From, string To)> back)
    {
        if (back.Count == 0) return;

        var placed = unplaced.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var rowsByColumn = column
            .GroupBy(e => e.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => row[e.Key]).ToList());

        var topEdge = OriginY;
        var bottomEdge = OriginY + rowsByColumn.Values.SelectMany(r => r).DefaultIfEmpty(0).Max() * RowSpacing + NodeSize;

        var laneAbove = 0;
        var laneBelow = 0;

        foreach (var arc in net.Arcs
                     .Where(a => back.Contains((a.SourceId, a.TargetId)))
                     .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            if (arc.Waypoints.Count > 0) continue;
            if (!placed.Contains(arc.SourceId) || !placed.Contains(arc.TargetId)) continue;

            var source = net.FindNode(arc.SourceId)!.Bounds!;
            var target = net.FindNode(arc.TargetId)!.Bounds!;

            bool Clear(string id, bool above) => above
                ? rowsByColumn[column[id]].All(r => r >= row[id])
                : rowsByColumn[column[id]].All(r => r <= row[id]);

            var goAbove = Clear(arc.SourceId, above: true) && Clear(arc.TargetId, above: true);

            var laneY = goAbove
                ? topEdge - LaneGap * ++laneAbove
                : bottomEdge + LaneGap * ++laneBelow;

            // A node carries its name underneath: a place always, a transition
            // since the modeler draws its name below the box as well. A route
            // that runs below the drawing and meets such a node from beneath
            // would draw straight through that name, so the end is met from the
            // side instead: the arc leaves the source to the right and reaches
            // the target from the left, which is also the direction a return to
            // the start reads in. A silent transition has no name. From above
            // there is nothing in the way.
            static bool Named(PtNode? node) => node is not (null or PtTransition { IsSilent: true });
            var sourceSide = !goAbove && Named(net.FindNode(arc.SourceId));
            var targetSide = !goAbove && Named(net.FindNode(arc.TargetId));

            if (sourceSide)
            {
                var x = source.X + source.Width + LaneGap;
                arc.Waypoints.Add(new PtPoint(x, source.Centre.Y));
                arc.Waypoints.Add(new PtPoint(x, laneY));
            }
            else
            {
                arc.Waypoints.Add(new PtPoint(source.Centre.X, laneY));
            }

            if (targetSide)
            {
                var x = target.X - LaneGap;
                arc.Waypoints.Add(new PtPoint(x, laneY));
                arc.Waypoints.Add(new PtPoint(x, target.Centre.Y));
            }
            else
            {
                arc.Waypoints.Add(new PtPoint(target.Centre.X, laneY));
            }
        }
    }
}
