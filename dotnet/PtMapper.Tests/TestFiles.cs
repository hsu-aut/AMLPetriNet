namespace PtMapper.Tests;

/// <summary>Files of this repository the tests read, found from the test binary upwards.</summary>
internal static class TestFiles
{
    /// <summary>
    /// The repository root: the directory that holds libraries/ and examples/paper/.
    /// The test output folder carries copies of libraries/ and examples/ too, so
    /// examples/paper/ is what tells the two apart.
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>A file of the MPS500 bridging example, in examples/paper.</summary>
    public static string Paper(string fileName) => Path.Combine(Root, "examples", "paper", fileName);

    /// <summary>A library artefact, in libraries/.</summary>
    public static string Library(string fileName) => Path.Combine(Root, "libraries", fileName);

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null
               && !(Directory.Exists(Path.Combine(dir, "libraries")) && Directory.Exists(Path.Combine(dir, "examples", "paper"))))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repository root not found");
    }
}
