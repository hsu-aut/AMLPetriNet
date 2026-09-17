using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using PtMapper.Conversion.Models;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The AML Editor does not resolve file-based ExternalReferences. A document
/// that only points at its libraries opens there with every class path
/// unresolved, so anything meant to be opened by a person has to carry them.
/// </summary>
public class SelfContainedTests
{
    private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

    /// <summary>
    /// Turns an embedded shared library back into a reference: removes it and
    /// puts the alias back in front of every path that named it.
    /// </summary>
    private static void Externalise(Aml.Engine.CAEX.CAEXDocument doc)
    {
        foreach (var (alias, libName, file) in new[]
                 {
                     (DiagramInterchangeLibrary.Alias, DiagramInterchangeLibrary.LibName,
                         DiagramInterchangeLibrary.FileName),
                     (ObjectReferencesLibrary.Alias, ObjectReferencesLibrary.LibName,
                         ObjectReferencesLibrary.FileName),
                 })
        {
            if (doc.CAEXFile.AttributeTypeLib[libName] == null) continue;
            doc.CAEXFile.AttributeTypeLib[libName]!.Remove();

            var reference = doc.CAEXFile.ExternalReference.Append();
            reference.Alias = alias;
            reference.Path = file;

            foreach (var attribute in doc.CAEXFile.Node.DescendantsAndSelf().Attributes()
                         .Where(a => a.Name.LocalName.StartsWith("Ref", StringComparison.Ordinal))
                         .ToList())
            {
                if (attribute.Value.StartsWith(libName + "/", StringComparison.Ordinal))
                    attribute.Value = alias + "@" + attribute.Value;
            }
        }
    }

    private static string LibraryDirectory =>
        Path.Combine(AppContext.BaseDirectory, "libraries");

    /// <summary>
    /// A document that still points at the shared libraries instead of carrying
    /// them.
    ///
    /// The mapper no longer writes one: a document it creates embeds the two
    /// small libraries so that it opens in the AML Editor, which does not follow
    /// file references. What PtSelfContained is for is the other documents, the
    /// ones written before that or by another tool, so the tests build that
    /// shape by taking the embedded libraries back out.
    /// </summary>
    private static string SampleDocument()
    {
        var net = new PtNet { Id = "n", Name = "Net" };
        net.Places.Add(new PtPlace { Id = "p", Name = "P", Bounds = new PtBounds(0, 0, 50, 50) });
        net.Transitions.Add(new PtTransition { Id = "t", Name = "T", Bounds = new PtBounds(150, 0, 50, 50) });
        net.Arcs.Add(new PtArc { Id = "a", SourceId = "p", TargetId = "t" });

        var doc = PtNetToCaex.Convert(net, "PetriNets");
        Externalise(doc);
        var temp = Path.GetTempFileName();
        try
        {
            doc.SaveToFile(temp, prettyPrint: true);
            return File.ReadAllText(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static XElement Inlined()
    {
        var (xml, _) = PtSelfContained.Inline(SampleDocument(), PtSelfContained.ReadLibraries(LibraryDirectory));
        return XDocument.Parse(xml).Root!;
    }

    [Fact]
    public void TheReferencedLibrariesAreEmbedded()
    {
        var root = Inlined();

        var attributeLibs = root.Elements(Caex + "AttributeTypeLib")
            .Select(e => (string?)e.Attribute("Name"))
            .ToList();

        Assert.Contains(DiagramInterchangeLibrary.LibName, attributeLibs);
        Assert.Contains(ObjectReferencesLibrary.LibName, attributeLibs);
    }

    [Fact]
    public void OnlyTheAmlBaseReferenceRemains()
    {
        var aliases = Inlined().Elements(Caex + "ExternalReference")
            .Select(e => (string?)e.Attribute("Alias"))
            .ToList();

        // The base libraries are a URL every AML tool knows; inlining them would
        // bloat the file for nothing.
        Assert.Equal(new[] { AmlBase.Alias }, aliases);
    }

    [Fact]
    public void EveryClassPathResolvesInsideTheFile()
    {
        // The actual promise: nothing in the document points at a library that
        // is not in the document, apart from the AML base ones.
        var root = Inlined();

        var libNames = new[] { "InterfaceClassLib", "RoleClassLib", "SystemUnitClassLib", "AttributeTypeLib" }
            .SelectMany(tag => root.Elements(Caex + tag))
            .Select(e => (string?)e.Attribute("Name"))
            .ToHashSet(StringComparer.Ordinal);

        var unresolved = root.DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName is "RefBaseSystemUnitPath" or "RefAttributeType"
                        or "RefBaseClassPath" or "RefRoleClassPath")
            .Select(a => a.Value)
            .Where(v => v.Length > 0 && !v.Contains('@', StringComparison.Ordinal))
            .Where(v => !libNames.Contains(v.Split('/')[0]))
            .Distinct()
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void AliasPrefixesOfInlinedLibrariesAreGone()
    {
        var (xml, inlined) = PtSelfContained.Inline(SampleDocument(), PtSelfContained.ReadLibraries(LibraryDirectory));

        Assert.NotEmpty(inlined);
        foreach (var alias in inlined)
            Assert.DoesNotContain(alias + "@", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChildOrderStaysSchemaValid()
    {
        // Appending libraries would otherwise leave them after the instance
        // hierarchy, which CAEX 3.0 does not allow.
        var order = Inlined().Elements().Select(e => e.Name.LocalName).ToList();

        var expected = new[]
        {
            "SuperiorStandardVersion", "SourceDocumentInformation", "ExternalReference",
            "InstanceHierarchy", "InterfaceClassLib", "RoleClassLib", "SystemUnitClassLib", "AttributeTypeLib",
        };

        var positions = order.Select(name => Array.IndexOf(expected, name)).Where(i => i >= 0).ToList();
        Assert.Equal(positions.OrderBy(i => i), positions);
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        var libraries = PtSelfContained.ReadLibraries(LibraryDirectory);
        var (once, _) = PtSelfContained.Inline(SampleDocument(), libraries);
        var (twice, inlinedAgain) = PtSelfContained.Inline(once, libraries);

        Assert.Empty(inlinedAgain);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AMissingLibraryFileLeavesThatReferenceAlone()
    {
        // A partial set must not produce a document that claims to be
        // self-contained while a class path dangles.
        var partial = new Dictionary<string, string>
        {
            [DiagramInterchangeLibrary.Alias] =
                File.ReadAllText(Path.Combine(LibraryDirectory, DiagramInterchangeLibrary.FileName)),
        };

        var (xml, inlined) = PtSelfContained.Inline(SampleDocument(), partial);

        Assert.Equal(new[] { DiagramInterchangeLibrary.Alias }, inlined);
        Assert.Contains(ObjectReferencesLibrary.Alias + "@", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentStillReadsBackAsTheSameNet()
    {
        var (xml, _) = PtSelfContained.Inline(SampleDocument(), PtSelfContained.ReadLibraries(LibraryDirectory));

        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".aml");
        try
        {
            File.WriteAllText(temp, xml);
            var net = Assert.Single(CaexToPtNet.ReadAll(CAEXDocument.LoadFromFile(temp)));

            Assert.Single(net.Places);
            Assert.Single(net.Transitions);
            Assert.Single(net.Arcs);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
