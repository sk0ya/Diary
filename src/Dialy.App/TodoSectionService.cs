using System.Text.RegularExpressions;

namespace Dialy.App;

public readonly record struct TodoItem(int LineIndex, string Text, bool IsCompleted);

public static partial class TodoSectionService
{
    private const string TodoHeading = "## TODO";

    public static IReadOnlyList<TodoItem> Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return Array.Empty<TodoItem>();
        }

        var document = MarkdownDocument.Parse(markdown);
        if (!TryFindTodoSection(document.Lines, out var startIndex, out var endIndex))
        {
            return Array.Empty<TodoItem>();
        }

        var items = new List<TodoItem>();
        for (var lineIndex = startIndex + 1; lineIndex < endIndex; lineIndex++)
        {
            var match = TodoLineRegex().Match(document.Lines[lineIndex]);
            if (!match.Success)
            {
                continue;
            }

            var text = match.Groups["text"].Value.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var isCompleted = string.Equals(match.Groups["state"].Value, "x", StringComparison.OrdinalIgnoreCase);
            items.Add(new TodoItem(lineIndex, text, isCompleted));
        }

        return items;
    }

    public static string AddTodo(string? markdown, string todoText)
    {
        var trimmedTodo = todoText.Trim();
        if (trimmedTodo.Length == 0)
        {
            return markdown ?? string.Empty;
        }

        var document = MarkdownDocument.Parse(markdown ?? string.Empty);

        if (TryFindTodoSection(document.Lines, out var startIndex, out var endIndex))
        {
            var insertIndex = FindTodoInsertIndex(document.Lines, startIndex, endIndex);
            document.Lines.Insert(insertIndex, $"- [ ] {trimmedTodo}");
        }
        else
        {
            InsertTodoSection(document.Lines, trimmedTodo);
        }

        document.EnsureTrailingNewline();
        return document.Compose();
    }

    public static bool TrySetCompletion(string? markdown, int lineIndex, bool isCompleted, out string updatedMarkdown)
    {
        var document = MarkdownDocument.Parse(markdown ?? string.Empty);

        if (lineIndex < 0 || lineIndex >= document.Lines.Count)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        var match = TodoLineRegex().Match(document.Lines[lineIndex]);
        if (!match.Success)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        var text = match.Groups["text"].Value.Trim();
        if (text.Length == 0)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        document.Lines[lineIndex] = $"- [{(isCompleted ? "x" : " ")}] {text}";
        document.EnsureTrailingNewline();

        updatedMarkdown = document.Compose();
        return true;
    }

    public static bool TryRemoveTodo(string? markdown, int lineIndex, out string updatedMarkdown, out string removedText)
    {
        var document = MarkdownDocument.Parse(markdown ?? string.Empty);
        removedText = string.Empty;

        if (lineIndex < 0 || lineIndex >= document.Lines.Count)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        var match = TodoLineRegex().Match(document.Lines[lineIndex]);
        if (!match.Success)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        removedText = match.Groups["text"].Value.Trim();
        if (removedText.Length == 0)
        {
            updatedMarkdown = markdown ?? string.Empty;
            return false;
        }

        document.Lines.RemoveAt(lineIndex);
        document.EnsureTrailingNewline();

        updatedMarkdown = document.Compose();
        return true;
    }

    private static void InsertTodoSection(List<string> lines, string todoText)
    {
        var insertIndex = FindNewSectionInsertIndex(lines);
        var sectionLines = new[]
        {
            "## TODO",
            $"- [ ] {todoText}",
            string.Empty
        };

        lines.InsertRange(insertIndex, sectionLines);
    }

    private static int FindNewSectionInsertIndex(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var firstNonEmptyIndex = lines.FindIndex(static line => !string.IsNullOrWhiteSpace(line));
        if (firstNonEmptyIndex < 0)
        {
            return 0;
        }

        var insertIndex = firstNonEmptyIndex + 1;
        while (insertIndex < lines.Count && string.IsNullOrWhiteSpace(lines[insertIndex]))
        {
            insertIndex++;
        }

        return insertIndex;
    }

    private static int FindTodoInsertIndex(List<string> lines, int startIndex, int endIndex)
    {
        var insertIndex = endIndex;
        while (insertIndex > startIndex + 1 && string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
        {
            insertIndex--;
        }

        return insertIndex;
    }

    private static bool TryFindTodoSection(IReadOnlyList<string> lines, out int startIndex, out int endIndex)
    {
        startIndex = -1;
        endIndex = lines.Count;

        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i].Trim(), TodoHeading, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            startIndex = i;
            break;
        }

        if (startIndex < 0)
        {
            return false;
        }

        for (var i = startIndex + 1; i < lines.Count; i++)
        {
            if (!HeadingLineRegex().IsMatch(lines[i]))
            {
                continue;
            }

            endIndex = i;
            break;
        }

        return true;
    }

    [GeneratedRegex(@"^\s*-\s+\[(?<state>[ xX])\]\s*(?<text>.*?)\s*$")]
    private static partial Regex TodoLineRegex();

    [GeneratedRegex(@"^\s*#{1,6}\s+\S")]
    private static partial Regex HeadingLineRegex();

    private sealed class MarkdownDocument(List<string> lines, string newLine, bool endsWithNewLine)
    {
        public List<string> Lines { get; } = lines;
        private string NewLine { get; } = newLine;
        private bool EndsWithNewLine { get; set; } = endsWithNewLine;

        public static MarkdownDocument Parse(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return new MarkdownDocument([], Environment.NewLine, false);
            }

            var newLine = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
            var endsWithNewLine = normalized.EndsWith('\n');
            var lines = normalized.Split('\n').ToList();

            if (endsWithNewLine && lines.Count > 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return new MarkdownDocument(lines, newLine, endsWithNewLine);
        }

        public void EnsureTrailingNewline()
        {
            EndsWithNewLine = true;
        }

        public string Compose()
        {
            if (Lines.Count == 0)
            {
                return string.Empty;
            }

            var markdown = string.Join(NewLine, Lines);
            return EndsWithNewLine ? markdown + NewLine : markdown;
        }
    }
}
