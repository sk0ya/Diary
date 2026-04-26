using System.IO;
using System.Text;

namespace Dialy.App;

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

    public string LoadOrCreate()
    {
        EnsureBacklogFile();
        return File.ReadAllText(BacklogPath);
    }

    public void Save(string markdown)
    {
        var directory = Path.GetDirectoryName(BacklogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(BacklogPath, markdown, new UTF8Encoding(false));
    }

    private void EnsureBacklogFile()
    {
        var directory = Path.GetDirectoryName(BacklogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(BacklogPath))
        {
            File.WriteAllText(BacklogPath, BuildTemplate(), new UTF8Encoding(false));
        }
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
}
