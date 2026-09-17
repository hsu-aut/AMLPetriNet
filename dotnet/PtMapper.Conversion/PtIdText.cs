using System.Text;

namespace PtMapper.Conversion;

/// <summary>
/// Turns an arbitrary identifier into one PNML and the modeler accept.
///
/// PNML declares ids as <c>xs:ID</c>, which is an XML name, and the modeler's
/// parser refuses anything else outright. CAEX ids look nothing like that: they
/// are GUIDs in braces, and documents in the wild put exactly that into
/// <c>Identification/id</c> as well. The MPS500 bridging example does, so
/// rejecting those documents would mean refusing to open the very file this
/// plugin exists for.
///
/// The mapping is deterministic and leaves a valid name untouched, so a
/// document that was already well formed is unaffected, and one that was not
/// can still be opened and edited.
/// </summary>
public static class PtIdText
{
    /// <summary>Prefix for a name that cannot start where the original did.</summary>
    private const char Filler = '_';

    /// <summary>
    /// Whether this string is usable as an id: an xs:ID for PNML, and one the
    /// modeler accepts.
    ///
    /// The two are not the same. XML allows letters of any script in a name, so
    /// "Prüfstation" is a valid xs:ID, but the modeler's parser checks ids
    /// against <c>[a-z_][\w-.]*</c>, which is ASCII only, and refuses the whole
    /// file over one umlaut. The stricter rule is the one that decides whether a
    /// net opens, so it is the one applied here.
    /// </summary>
    public static bool IsXmlName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (!IsNameStart(value[0])) return false;
        for (var i = 1; i < value.Length; i++)
            if (!IsNameChar(value[i])) return false;
        return true;
    }

    private static bool IsNameStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    private static bool IsNameChar(char c) => IsNameStart(c) || c is (>= '0' and <= '9') or '-' or '.';

    /// <summary>
    /// German letters that have a customary ASCII spelling. Everything else
    /// with a diacritic loses the diacritic, é becomes e.
    /// </summary>
    private static string? Transliterate(char c) => c switch
    {
        'ä' => "ae", 'ö' => "oe", 'ü' => "ue",
        'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue",
        'ß' => "ss",
        _ => null,
    };

    /// <summary>
    /// The identifier as a valid XML name. Returns the input unchanged when it
    /// already is one.
    /// </summary>
    public static string ToXmlName(string? value)
    {
        if (IsXmlName(value)) return value!;
        if (string.IsNullOrWhiteSpace(value)) return "id";

        var builder = new StringBuilder(value!.Length + 1);

        foreach (var c in value)
        {
            // "Prüfstation" reads better as "Pruefstation" than as "Prfstation".
            var spelled = Transliterate(c);
            if (spelled != null)
            {
                builder.Append(spelled);
                continue;
            }

            // é is e plus an accent once decomposed; keep the letter.
            foreach (var part in c.ToString().Normalize(NormalizationForm.FormD))
            {
                // Keep what a name may contain, drop the rest. Braces and spaces
                // are the usual offenders; dropping rather than substituting
                // keeps a GUID readable as itself.
                if (IsNameChar(part)) builder.Append(part);
            }
        }

        if (builder.Length == 0) return "id";

        // A name may not start with a digit, a dot or a hyphen.
        if (!IsNameStart(builder[0])) builder.Insert(0, Filler);

        var candidate = builder.ToString();
        return IsXmlName(candidate) ? candidate : "id";
    }

    /// <summary>
    /// Whether two identifiers denote the same element once both are reduced to
    /// valid names. This is what lets an update find an element again whose
    /// stored id is a braced GUID while the diagram carries the cleaned form.
    /// </summary>
    public static bool SameAfterNormalisation(string? a, string? b) =>
        string.Equals(ToXmlName(a), ToXmlName(b), StringComparison.Ordinal);

    /// <summary>
    /// Rewrites every identifier in a net to a valid XML name, arc endpoints
    /// included, and returns how many had to change.
    ///
    /// The reader that comes out of CAEX does this as it goes, because it has to
    /// resolve arc endpoints through the interfaces anyway. A net read from a
    /// PNML file keeps whatever the file said, so this is the pass that makes
    /// such a file displayable without changing the file itself.
    /// </summary>
    public static int NormaliseIds(Models.PtNet net)
    {
        ArgumentNullException.ThrowIfNull(net);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = 0;

        net.Id = ToDistinctXmlName(net.Id, taken);

        foreach (var node in net.Nodes)
        {
            var replacement = ToDistinctXmlName(node.Id, taken);
            if (replacement != node.Id)
            {
                // Only the first node of an id decides where arcs naming that id
                // go. A second node with the same id is numbered, and taking its
                // new name for the rename sent every arc of the first node to it.
                rename.TryAdd(node.Id, replacement);
                node.Id = replacement;
                changed++;
            }
            else
            {
                rename.TryAdd(node.Id, node.Id);
            }
        }

        foreach (var arc in net.Arcs)
        {
            var replacement = ToDistinctXmlName(arc.Id, taken);
            if (replacement != arc.Id)
            {
                arc.Id = replacement;
                changed++;
            }

            // An arc whose endpoints still name the old ids would point nowhere.
            if (rename.TryGetValue(arc.SourceId, out var source)) arc.SourceId = source;
            if (rename.TryGetValue(arc.TargetId, out var target)) arc.TargetId = target;
        }

        return changed;
    }

    /// <summary>
    /// Gives every element of a net an id no other element of it carries, and
    /// says which ones it had to change.
    ///
    /// Duplicate ids make a PNML file invalid, and files like that exist: one
    /// produced by jbpt carries the same arc id twice. Writing it into AML as it
    /// is put two elements with one Identification/id into the document, and
    /// the next round trip renamed one of them. The first element keeps its id,
    /// every later one gets a number, and an arc naming a duplicated node id
    /// stays with the first node. The net's own id counts as taken, since PNML
    /// ids share one space per document.
    /// </summary>
    public static IReadOnlyList<string> MakeIdsDistinct(Models.PtNet net)
    {
        ArgumentNullException.ThrowIfNull(net);

        var elements = net.Nodes.Select(n => (Element: (object)n, n.Id, n.Name))
            .Concat(net.Arcs.Select(a => (Element: (object)a, a.Id, a.Name)))
            .ToList();

        // Every id that is in the file at all, so a number never collides with
        // an element further down that already goes by it.
        var inFile = new HashSet<string>(elements.Select(e => e.Id), StringComparer.Ordinal) { net.Id };
        var taken = new HashSet<string>(StringComparer.Ordinal) { net.Id };
        var messages = new List<string>();

        foreach (var (element, id, name) in elements)
        {
            if (taken.Add(id)) continue;

            var ordinal = 2;
            string candidate;
            do
            {
                candidate = $"{id}_{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                ordinal++;
            }
            while (inFile.Contains(candidate) || !taken.Add(candidate));

            switch (element)
            {
                case Models.PtNode node: node.Id = candidate; break;
                case Models.PtArc arc: arc.Id = candidate; break;
            }

            messages.Add($"Two elements carried the id '{id}'. The later one"
                + (string.IsNullOrWhiteSpace(name) ? "" : $", '{name}',")
                + $" now goes by '{candidate}'.");
        }

        return messages;
    }

    /// <summary>
    /// The normalised identifier, made distinct if that name is already taken.
    /// Two different originals can reduce to the same name (braces dropped from
    /// otherwise equal strings), and two elements with one id would break the
    /// diagram.
    /// </summary>
    public static string ToDistinctXmlName(string? value, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);

        var candidate = ToXmlName(value);
        if (taken.Add(candidate)) return candidate;

        for (var ordinal = 2; ; ordinal++)
        {
            var numbered = $"{candidate}_{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (taken.Add(numbered)) return numbered;
        }
    }
}
