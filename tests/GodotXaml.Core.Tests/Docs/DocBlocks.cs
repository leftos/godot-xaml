using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Docs;

/// <summary>An XML example a Markdown file fences as <c>xml xscn</c> or <c>xml xres</c>.</summary>
/// <param name="Path">The repository-relative path of the file, with forward slashes.</param>
/// <param name="Line">The 1-based line of the block's first content line.</param>
/// <param name="Kind">The document kind the info string names.</param>
/// <param name="Canonical">Whether the block must equal its canonical output.</param>
/// <param name="InvalidCode">The diagnostic code the block must produce, or null.</param>
/// <param name="Text">The block's content, its fence's indentation removed, ending in a newline.</param>
internal sealed record DocBlock(string Path, int Line, XscnDocumentKind Kind, bool Canonical, string? InvalidCode, string Text);

/// <summary>The tagged XML examples of a Markdown file, read out of its fenced code blocks.</summary>
internal static class DocBlocks
{
    private const string SceneInfo = "xscn";
    private const string ResourceInfo = "xres";
    private const string CanonicalTag = "canonical";
    private const string InvalidTag = "invalid";

    /// <summary>Every tagged XML example in a Markdown text, in the order it appears.</summary>
    /// <param name="markdown">The Markdown text.</param>
    /// <param name="relativePath">The repository-relative path the blocks report.</param>
    /// <returns>The blocks; an empty list when there are none.</returns>
    /// <exception cref="InvalidOperationException">A fence is unclosed, or an info string carries a tag that is not known.</exception>
    public static IReadOnlyList<DocBlock> Extract(string markdown, string relativePath)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<DocBlock> blocks = [];
        int index = 0;
        while (index < lines.Length)
        {
            int indent = Indentation(lines[index]);
            int ticks = Backticks(lines[index], indent);
            if (ticks < 3)
            {
                index++;
                continue;
            }

            int close = index + 1;
            while (close < lines.Length && !Closes(lines[close], indent, ticks))
            {
                close++;
            }

            if (close == lines.Length)
            {
                throw new InvalidOperationException($"{relativePath}:{index + 1}: the fence opened here is never closed.");
            }

            DocBlock? block = BlockOf(lines, index, close, indent, relativePath);
            if (block is not null)
            {
                blocks.Add(block);
            }

            index = close + 1;
        }

        return blocks;
    }

    private static DocBlock? BlockOf(string[] lines, int open, int close, int indent, string relativePath)
    {
        string[] words = lines[open][(indent + Backticks(lines[open], indent))..].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!IsTaggedDocument(words))
        {
            return null;
        }

        (bool canonical, string? invalidCode) = TagsOf(words, open, relativePath);
        XscnDocumentKind kind = words[1] == SceneInfo ? XscnDocumentKind.Scene : XscnDocumentKind.Resource;
        return new DocBlock(relativePath, open + 2, kind, canonical, invalidCode, TextOf(lines, open, close, indent));
    }

    private static bool IsTaggedDocument(string[] words) =>
        words.Length >= 2 && words[0] == "xml" && (words[1] == SceneInfo || words[1] == ResourceInfo);

    private static (bool Canonical, string? InvalidCode) TagsOf(string[] words, int open, string relativePath)
    {
        bool canonical = false;
        string? invalidCode = null;
        for (int index = 2; index < words.Length; index++)
        {
            if (words[index] == CanonicalTag)
            {
                canonical = true;
                continue;
            }

            if (words[index] != InvalidTag || index + 1 == words.Length)
            {
                throw new InvalidOperationException($"{relativePath}:{open + 1}: unknown tag '{words[index]}' in an info string.");
            }

            invalidCode = words[++index];
        }

        return (canonical, invalidCode);
    }

    private static string TextOf(string[] lines, int open, int close, int indent)
    {
        List<string> content = [];
        for (int index = open + 1; index < close; index++)
        {
            content.Add(DeIndented(lines[index], indent));
        }

        return string.Join("\n", content) + "\n";
    }

    private static int Indentation(string line)
    {
        int spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }

        return spaces;
    }

    private static int Backticks(string line, int indent)
    {
        int ticks = 0;
        while (indent + ticks < line.Length && line[indent + ticks] == '`')
        {
            ticks++;
        }

        return ticks;
    }

    private static bool Closes(string line, int indent, int ticks)
    {
        int spaces = Indentation(line);
        if (spaces > indent)
        {
            return false;
        }

        return Backticks(line, spaces) >= ticks && line[(spaces + Backticks(line, spaces))..].Trim().Length == 0;
    }

    private static string DeIndented(string line, int indent)
    {
        int spaces = 0;
        while (spaces < indent && spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }

        return line[spaces..];
    }
}
