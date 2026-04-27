using System.Globalization;
using System.IO;
using System.Text;

namespace Diary.App;

public sealed class DailyNoteService
{
    public static readonly string DefaultRootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Diary",
        "Entries");

    public DailyNoteService(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? DefaultRootDirectory
            : rootDirectory;
    }

    public string RootDirectory { get; }

    public string PrepareEntry(DateOnly date)
    {
        return GetEntryPath(date);
    }

    public string LoadEntry(DateOnly date)
    {
        var entryPath = GetEntryPath(date);
        return File.Exists(entryPath)
            ? File.ReadAllText(entryPath)
            : BuildTemplate(date);
    }

    public bool ContentMatchesStored(DateOnly date, string content)
    {
        return string.Equals(
            NormalizeLineEndings(content),
            NormalizeLineEndings(LoadEntry(date)),
            StringComparison.Ordinal);
    }

    public bool IsTemplateContent(DateOnly date, string content)
    {
        return string.Equals(
            NormalizeLineEndings(content),
            NormalizeLineEndings(BuildTemplate(date)),
            StringComparison.Ordinal);
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
                    DateTimeStyles.None, out var date) &&
                !IsTemplateContent(date, File.ReadAllText(file)))
            {
                result.Add(date);
            }
        }

        return result;
    }

    public void DeleteEntry(DateOnly date)
    {
        var entryPath = GetEntryPath(date);
        if (!File.Exists(entryPath))
        {
            return;
        }

        File.Delete(entryPath);

        var monthDirectory = Path.GetDirectoryName(entryPath);
        if (!string.IsNullOrWhiteSpace(monthDirectory) &&
            Directory.Exists(monthDirectory) &&
            !Directory.EnumerateFileSystemEntries(monthDirectory).Any())
        {
            Directory.Delete(monthDirectory);

            var yearDirectory = Path.GetDirectoryName(monthDirectory);
            if (!string.IsNullOrWhiteSpace(yearDirectory) &&
                Directory.Exists(yearDirectory) &&
                !Directory.EnumerateFileSystemEntries(yearDirectory).Any())
            {
                Directory.Delete(yearDirectory);
            }
        }
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

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
