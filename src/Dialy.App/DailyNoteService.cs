using System.Globalization;
using System.IO;
using System.Text;

namespace Dialy.App;

public sealed class DailyNoteService
{
    public static readonly string DefaultRootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Dialy",
        "Entries");

    public DailyNoteService(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? DefaultRootDirectory
            : rootDirectory;
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

    public HashSet<DateOnly> GetExistingEntries(int year, int month)
    {
        var dir = Path.Combine(
            RootDirectory,
            year.ToString("0000", CultureInfo.InvariantCulture),
            month.ToString("00", CultureInfo.InvariantCulture));

        var result = new HashSet<DateOnly>();
        if (!Directory.Exists(dir)) return result;

        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                result.Add(date);
        }

        return result;
    }

    private static string BuildTemplate(DateOnly date)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"# {date:yyyy/MM/dd}",
                string.Empty,
                "## TODO",
                string.Empty,
                "## Note",
                string.Empty,
                "- ",
                string.Empty
            ]);
    }
}
