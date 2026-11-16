using System.Text;

namespace PtMapper.Tool.Analysis;

/// <summary>Where a report goes when it goes to a file: UTF-8 without a byte order mark, through a temporary file.</summary>
public static class ReportOutput
{
    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Writes the file whole or not at all. Returns the reason where it could not.</summary>
    public static string? Write(string path, string text)
    {
        var temporary = path + ".ptmap-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return $"{path}: {ex.Message}";
        }
    }
}
