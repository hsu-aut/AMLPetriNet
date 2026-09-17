using System.Xml.Linq;
using Aml.Engine.CAEX;
using PtMapper.Conversion;
using Xunit;

namespace PtMapper.Tests;

/// <summary>
/// The published artefact ISO_PT_DomainLibrary_v0.3.aml in libraries/ is the
/// authority: it is the library documents reference and the file this
/// repository ships. PtLibraries reproduces it in code so the mapper can create the classes inside
/// a user's document. These tests keep the two from drifting.
///
/// The comparison is structural, not byte-for-byte: element order and formatting
/// are Aml.Engine's business, the class model is ours.
/// </summary>
public class PtLibraryGoldenTests
{
    private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

    private static XDocument Published() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "libraries", PtNames.LibraryFileName));

    private static CAEXFileType Generated()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);
        return doc.CAEXFile;
    }

    [Fact]
    public void InterfaceClassesMatchThePublishedLibrary()
    {
        var expected = ClassNames(Published(), "InterfaceClassLib", "InterfaceClass");
        var actual = Generated().InterfaceClassLib[PtNames.InterfaceClassLib]!
            .InterfaceClass.Select(c => c.Name).OrderBy(n => n).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RoleClassesMatchThePublishedLibrary()
    {
        var expected = ClassNames(Published(), "RoleClassLib", "RoleClass");
        var actual = Generated().RoleClassLib[PtNames.RoleClassLib]!
            .RoleClass.Select(c => c.Name).OrderBy(n => n).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SystemUnitClassesMatchThePublishedLibrary()
    {
        var expected = ClassNames(Published(), "SystemUnitClassLib", "SystemUnitClass");
        var actual = Generated().SystemUnitClassLib[PtNames.SystemUnitClassLib]!
            .SystemUnitClass.Select(c => c.Name).OrderBy(n => n).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SystemUnitClassIdsAreStable()
    {
        // The ids are referenced from documents the mapper has already written,
        // so they are part of the contract, not an implementation detail.
        var published = Published().Descendants(Caex + "SystemUnitClass")
            .ToDictionary(e => (string)e.Attribute("Name")!, e => (string?)e.Attribute("ID"));

        foreach (var suc in Generated().SystemUnitClassLib[PtNames.SystemUnitClassLib]!.SystemUnitClass)
        {
            Assert.True(published.ContainsKey(suc.Name), $"unexpected class {suc.Name}");
            Assert.Equal(published[suc.Name], suc.ID);
        }
    }

    [Fact]
    public void InheritanceMatchesThePublishedLibrary()
    {
        var published = Published().Descendants()
            .Where(e => e.Name == Caex + "RoleClass" || e.Name == Caex + "SystemUnitClass" || e.Name == Caex + "InterfaceClass")
            .ToDictionary(
                e => $"{e.Name.LocalName}/{(string)e.Attribute("Name")!}",
                e => (string?)e.Attribute("RefBaseClassPath"));

        var caex = Generated();
        foreach (var (key, path) in Inheritance(caex))
        {
            Assert.True(published.ContainsKey(key), $"unexpected class {key}");
            Assert.Equal(published[key], path);
        }
    }

    [Fact]
    public void ElementAttributesMatchThePublishedLibrary()
    {
        var published = Published().Descendants(Caex + "RoleClass")
            .ToDictionary(
                e => (string)e.Attribute("Name")!,
                e => e.Elements(Caex + "Attribute").Select(a => (string)a.Attribute("Name")!).OrderBy(n => n).ToList());

        foreach (var role in Generated().RoleClassLib[PtNames.RoleClassLib]!.RoleClass)
        {
            var actual = role.Attribute.Select(a => a.Name).OrderBy(n => n).ToList();
            Assert.Equal(published[role.Name], actual);
        }
    }

    [Fact]
    public void SystemUnitClassAttributesMatchThePublishedLibrary()
    {
        // Attributes an instantiation template declares decide what
        // CreateClassInstance hands the mapper. PT_Net gaining Identification in
        // v0.3 is exactly this kind of change.
        var published = Published().Descendants(Caex + "SystemUnitClass")
            .ToDictionary(
                e => (string)e.Attribute("Name")!,
                e => e.Elements(Caex + "Attribute").Select(a => (string)a.Attribute("Name")!).OrderBy(n => n).ToList());

        foreach (var suc in Generated().SystemUnitClassLib[PtNames.SystemUnitClassLib]!.SystemUnitClass)
        {
            var actual = suc.Attribute.Select(a => a.Name).OrderBy(n => n).ToList();
            Assert.Equal(published[suc.Name], actual);
        }
    }

    [Fact]
    public void ArcDescriptionStatesTheInstanceAttributeConventions()
    {
        // Waypoint_n and LabelOffset are not declared on the class; the PT_Arc
        // description is where the library states how they are written. The
        // mapper depends on that, so a silent removal should fail here.
        var arc = Generated().RoleClassLib[PtNames.RoleClassLib]!.RoleClass[PtNames.Arc]!;

        Assert.Contains(PtNames.Attributes.WaypointPrefix + "1..n", arc.Description);
        Assert.Contains(DiagramInterchangeLibrary.WaypointAttributeTypePath, arc.Description);
        Assert.Contains(PtNames.Attributes.LabelOffset, arc.Description);
        Assert.Equal(
            Published().Descendants(Caex + "RoleClass")
                .Single(e => (string)e.Attribute("Name")! == PtNames.Arc)
                .Element(Caex + "Description")!.Value,
            arc.Description);
    }

    [Fact]
    public void NetCarriesItsOwnIdentification()
    {
        // Added in v0.3. Without it the PNML net id has nowhere to live and a
        // round trip renames the net after its label.
        var caex = Generated();

        Assert.NotNull(caex.RoleClassLib[PtNames.RoleClassLib]!
            .RoleClass[PtNames.Net]!.Attribute[PtNames.Attributes.Identification]);
        Assert.NotNull(caex.SystemUnitClassLib[PtNames.SystemUnitClassLib]!
            .SystemUnitClass[PtNames.Net]!.Attribute[PtNames.Attributes.Identification]);
    }

    [Fact]
    public void ExternalReferencesAreDeclared()
    {
        var aliases = Generated().ExternalReference.Select(r => r.Alias).ToList();

        Assert.Contains(AmlBase.Alias, aliases);
        Assert.Contains(ObjectReferencesLibrary.Alias, aliases);
        Assert.Contains(DiagramInterchangeLibrary.Alias, aliases);
    }

    [Fact]
    public void EnsureLibrariesIsIdempotent()
    {
        var doc = CAEXDocument.New_CAEXDocument();
        PtLibraries.EnsureLibraries(doc.CAEXFile);
        PtLibraries.EnsureLibraries(doc.CAEXFile);

        Assert.Single(doc.CAEXFile.SystemUnitClassLib);
        Assert.Single(doc.CAEXFile.RoleClassLib);
        Assert.Single(doc.CAEXFile.InterfaceClassLib);
        Assert.Single(doc.CAEXFile.AttributeTypeLib);
        Assert.Equal(3, doc.CAEXFile.ExternalReference.Count());
    }

    private static List<string> ClassNames(XDocument document, string libElement, string classElement) =>
        document.Descendants(Caex + libElement)
            .Elements(Caex + classElement)
            .Select(e => (string)e.Attribute("Name")!)
            .OrderBy(n => n)
            .ToList();

    private static IEnumerable<(string Key, string? Path)> Inheritance(CAEXFileType caex)
    {
        foreach (var ic in caex.InterfaceClassLib[PtNames.InterfaceClassLib]!.InterfaceClass)
            yield return ($"InterfaceClass/{ic.Name}", Empty(ic.RefBaseClassPath));

        foreach (var rc in caex.RoleClassLib[PtNames.RoleClassLib]!.RoleClass)
            yield return ($"RoleClass/{rc.Name}", Empty(rc.RefBaseClassPath));

        foreach (var suc in caex.SystemUnitClassLib[PtNames.SystemUnitClassLib]!.SystemUnitClass)
            yield return ($"SystemUnitClass/{suc.Name}", Empty(suc.RefBaseClassPath));

        static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
