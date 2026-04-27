using System.IO;
using System.Text;

namespace Diary.App;

public sealed class BacklogTodoService
{
    public BacklogTodoService(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? DailyNoteService.DefaultRootDirectory
            : rootDirectory;
    }

    public string RootDirectory { get; }

    public string BacklogPath => Path.Combine(RootDirectory, "_backlog.md");

    public string Load()
    {
        return File.Exists(BacklogPath)
            ? File.ReadAllText(BacklogPath)
            : BuildTemplate();
    }

    public void Save(string markdown)
    {
        if (IsTemplateContent(markdown))
        {
            if (File.Exists(BacklogPath))
            {
                File.Delete(BacklogPath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(BacklogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(BacklogPath, markdown, new UTF8Encoding(false));
    }

    private static string BuildTemplate()
    {
        return string.Join(
            Environment.NewLine,
            [
                "# Backlog",
                string.Empty,
                "## TODO",
                string.Empty
            ]);
    }

    private static bool IsTemplateContent(string markdown)
    {
        return string.Equals(
            NormalizeLineEndings(markdown),
            NormalizeLineEndings(BuildTemplate()),
            StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
