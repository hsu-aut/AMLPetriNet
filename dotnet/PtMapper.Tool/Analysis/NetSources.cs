using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;

namespace PtMapper.Tool.Analysis;

/// <summary>A net of a file, with the hierarchy and, for AML, the element it came from.</summary>
public sealed record NetSource(PtNet Net, string? Hierarchy, InternalElementType? Element);

/// <summary>Reads the nets of a PNML or AML file, for the commands that look at them.</summary>
public static class NetSources
{
    /// <summary>The nets of the file, each with the hierarchy and, for AML, the element it came from.</summary>
    public static List<NetSource> Load(string file, string? hierarchyName, TextWriter error, out string? fault)
    {
        fault = null;
        if (!File.Exists(file)) throw new FileNotFoundException($"{file}: no such file.", file);

        if (!file.EndsWith(".aml", StringComparison.OrdinalIgnoreCase))
        {
            if (hierarchyName != null) error.WriteLine("note: a hierarchy name means nothing for a PNML file and is ignored.");
            using var stream = File.OpenRead(file);
            return Pnml.ReadAll(stream).Select(n => new NetSource(n, null, null)).ToList();
        }

        var document = CAEXDocument.LoadFromFile(file);
        var hierarchies = document.CAEXFile.InstanceHierarchy.ToList();
        if (hierarchyName != null)
        {
            hierarchies = hierarchies.Where(h => h.Name == hierarchyName).ToList();
            if (hierarchies.Count == 0)
            {
                fault = $"{file} has no InstanceHierarchy '{hierarchyName}'.";
                return new List<NetSource>();
            }
        }

        var sources = new List<NetSource>();
        foreach (var hierarchy in hierarchies)
        {
            // Read and NetElements come out in the same order, so they pair up.
            var nets = CaexToPtNet.Read(hierarchy);
            var elements = CaexToPtNet.NetElements(hierarchy);
            for (var i = 0; i < nets.Count; i++) sources.Add(new NetSource(nets[i], hierarchy.Name, elements[i]));
        }

        return sources;
    }
}
