using PtMapper.Conversion.Models;

namespace PtMapper.Conversion;

public enum PtSeverity
{
    /// <summary>The net violates ISO/IEC 15909-1 and is not a place/transition net.</summary>
    Error,

    /// <summary>Legal, but almost certainly not what the author meant.</summary>
    Warning,
}

/// <summary>
/// One rule violation. <see cref="ElementId"/> is the PNML id, which is also
/// what the modeler uses, so a finding can be clicked to select its element.
/// </summary>
public sealed record PtFinding(string Rule, PtSeverity Severity, string Message, string? ElementId = null)
{
    public override string ToString() =>
        $"{Severity.ToString().ToUpperInvariant()} {Rule}: {Message}";
}

/// <summary>
/// Structural rules for basic place/transition nets, ISO/IEC 15909-1.
///
/// These are the constraints the standard states about the graph itself, the
/// ones a drawing tool can violate without noticing. The set is deliberately
/// small: 15909-1 has a small ontology, and rules that are really modelling
/// advice do not belong in a conformance check.
///
/// The FPD side runs its equivalent through OCL.NET, which reads CAEX. These
/// work on the net model instead, so the same rules cover a net that came from
/// PNML and never reached AML.
/// </summary>
public static class PtValidator
{
    public static IReadOnlyList<PtFinding> Validate(PtNet net)
    {
        ArgumentNullException.ThrowIfNull(net);

        var findings = new List<PtFinding>();
        findings.AddRange(CheckIdentifiers(net));
        findings.AddRange(CheckBipartite(net));
        findings.AddRange(CheckArcEndpoints(net));
        findings.AddRange(CheckValues(net));
        findings.AddRange(CheckIsolatedNodes(net));
        return findings;
    }

    /// <summary>PNML requires ids to be unique within a document.</summary>
    private static IEnumerable<PtFinding> CheckIdentifiers(PtNet net)
    {
        // The net's own id counts. It is an xs:ID like any other, it becomes the
        // model id in the modeler, and an invalid one takes the import down just
        // as surely as an invalid element id would.
        var all = net.Nodes.Select(n => n.Id)
            .Concat(net.Arcs.Select(a => a.Id))
            .Append(net.Id)
            .ToList();

        foreach (var duplicate in all.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            yield return new PtFinding("PT01", PtSeverity.Error,
                $"The id '{duplicate.Key}' is used by {duplicate.Count()} elements; ids must be unique.",
                duplicate.Key);
        }

        foreach (var empty in all.Where(string.IsNullOrWhiteSpace))
        {
            yield return new PtFinding("PT02", PtSeverity.Error, "An element has no id.", empty);
        }

        // PNML declares ids as xs:ID, which is an XML name. This is not
        // pedantry: the modeler's parser rejects anything else and the whole
        // import fails, so a file like that has to be named here instead.
        foreach (var invalid in all.Where(id => !string.IsNullOrWhiteSpace(id) && !PtIdText.IsXmlName(id)))
        {
            yield return new PtFinding("PT11", PtSeverity.Error,
                $"The id '{invalid}' is not a valid XML name. PNML ids are xs:ID and cannot contain "
                + "characters such as & < > \" or a space, and cannot start with a digit. The modeler "
                + "also refuses letters outside ASCII, such as umlauts.",
                invalid);
        }
    }

    /// <summary>
    /// The defining property: the graph is bipartite. An arc runs from a place
    /// to a transition or the other way round, never between two nodes of the
    /// same kind.
    /// </summary>
    private static IEnumerable<PtFinding> CheckBipartite(PtNet net)
    {
        foreach (var arc in net.Arcs)
        {
            var source = net.FindNode(arc.SourceId);
            var target = net.FindNode(arc.TargetId);
            if (source == null || target == null) continue; // reported by PT04

            if (source is PtPlace && target is PtPlace)
            {
                yield return new PtFinding("PT03", PtSeverity.Error,
                    $"Arc '{arc.Id}' connects two places. A P/T net is bipartite.", arc.Id);
            }
            else if (source is PtTransition && target is PtTransition)
            {
                yield return new PtFinding("PT03", PtSeverity.Error,
                    $"Arc '{arc.Id}' connects two transitions. A P/T net is bipartite.", arc.Id);
            }
        }
    }

    private static IEnumerable<PtFinding> CheckArcEndpoints(PtNet net)
    {
        foreach (var arc in net.Arcs)
        {
            if (net.FindNode(arc.SourceId) == null)
            {
                yield return new PtFinding("PT04", PtSeverity.Error,
                    $"Arc '{arc.Id}' starts at '{arc.SourceId}', which is not a node of this net.", arc.Id);
            }
            if (net.FindNode(arc.TargetId) == null)
            {
                yield return new PtFinding("PT04", PtSeverity.Error,
                    $"Arc '{arc.Id}' ends at '{arc.TargetId}', which is not a node of this net.", arc.Id);
            }

            // Two arcs the same way round between the same pair are redundant:
            // one arc of the combined weight says the same thing.
            var parallel = net.Arcs.Count(other =>
                other.SourceId == arc.SourceId && other.TargetId == arc.TargetId);
            if (parallel > 1 && net.Arcs.First(a => a.SourceId == arc.SourceId && a.TargetId == arc.TargetId).Id == arc.Id)
            {
                yield return new PtFinding("PT05", PtSeverity.Warning,
                    $"{parallel} arcs run from '{arc.SourceId}' to '{arc.TargetId}'. One arc with the summed weight is equivalent.",
                    arc.Id);
            }
        }
    }

    private static IEnumerable<PtFinding> CheckValues(PtNet net)
    {
        foreach (var place in net.Places.Where(p => p.InitialMarking < 0))
        {
            yield return new PtFinding("PT06", PtSeverity.Error,
                $"Place '{place.Id}' has a negative initial marking ({place.InitialMarking}).", place.Id);
        }

        foreach (var arc in net.Arcs.Where(a => a.Weight < 1))
        {
            yield return new PtFinding("PT07", PtSeverity.Error,
                $"Arc '{arc.Id}' has weight {arc.Weight}. An arc weight is a positive integer.", arc.Id);
        }
    }

    /// <summary>
    /// A transition with no incoming arc fires forever; one with no outgoing arc
    /// consumes without producing. Both are legal and occasionally intended, so
    /// they are warnings, not errors.
    /// </summary>
    private static IEnumerable<PtFinding> CheckIsolatedNodes(PtNet net)
    {
        foreach (var node in net.Nodes)
        {
            var incoming = net.Arcs.Count(a => a.TargetId == node.Id);
            var outgoing = net.Arcs.Count(a => a.SourceId == node.Id);

            if (incoming == 0 && outgoing == 0)
            {
                yield return new PtFinding("PT08", PtSeverity.Warning,
                    $"{Kind(node)} '{node.Id}' is not connected to anything.", node.Id);
                continue;
            }

            if (node is PtTransition)
            {
                if (incoming == 0)
                {
                    yield return new PtFinding("PT09", PtSeverity.Warning,
                        $"Transition '{node.Id}' has no input place and is therefore always enabled.", node.Id);
                }
                if (outgoing == 0)
                {
                    yield return new PtFinding("PT10", PtSeverity.Warning,
                        $"Transition '{node.Id}' has no output place and consumes tokens without producing any.", node.Id);
                }
            }
        }
    }

    private static string Kind(PtNode node) => node is PtPlace ? "Place" : "Transition";
}
