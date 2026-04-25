using System.Globalization;
using System.IO;
using System.Text;

namespace Dialy.App;

public sealed class DailyNoteService
{
    public DailyNoteService(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ??
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "Dialy",
                            "Entries");
    }

    public string RootDirectory { get; }

    public string EnsureEntry(DateOnly date)
    {
        var entryPath = GetEntryPath(date);
        var entryDirectory = Path.GetDirectoryName(entryPath);
        if (!string.IsNullOrWhiteSpace(entryDirectory))
        {
            Directory.CreateDirectory(entryDirectory);
        }

        if (!File.Exists(entryPath))
        {
            File.WriteAllText(entryPath, BuildTemplate(date), new UTF8Encoding(false));
        }

        return entryPath;
    }

    public string GetEntryPath(DateOnly date)
    {
        return Path.Combine(
            RootDirectory,
            date.Year.ToString("0000", CultureInfo.InvariantCulture),
            date.Month.ToString("00", CultureInfo.InvariantCulture),
            $"{date:yyyy-MM-dd}.md");
    }

    private static string BuildTemplate(DateOnly date)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"# {date:yyyy/MM/dd}",
                string.Empty,
                "- ",
                string.Empty
            ]);
    }
}
