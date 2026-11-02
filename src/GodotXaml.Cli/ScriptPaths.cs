using System.Text;

namespace GodotXaml.Cli;

/// <summary>The kinds of file whose string literals hold paths without being scenes or resources.</summary>
internal enum ScriptKind
{
    /// <summary>A <c>.gd</c> file.</summary>
    GdScript,

    /// <summary>A <c>.cs</c> file.</summary>
    CSharp,

    /// <summary>A <c>project.godot</c> file.</summary>
    ProjectFile,
}

/// <summary>How a literal's content is spelled, which decides how a new value is written into it.</summary>
internal enum LiteralForm
{
    /// <summary>A backslash escapes the next character.</summary>
    Regular,

    /// <summary>A C# <c>@"..."</c>, where a doubled quote is one quote.</summary>
    Verbatim,

    /// <summary>The content is the text as written.</summary>
    Raw,

    /// <summary>A C# <c>$</c> literal.</summary>
    Interpolated,
}

/// <summary>What a literal holds of a renamed path.</summary>
internal enum ScriptHitKind
{
    /// <summary>The literal's whole text is the path.</summary>
    Whole,

    /// <summary>The literal's text holds the path inside something longer.</summary>
    Inside,

    /// <summary>The literal's text ends in a renamed extension, so it may be part of a path built at runtime.</summary>
    Fragment,
}

/// <summary>A string literal of a script or a <c>project.godot</c>.</summary>
/// <param name="Quote">The offset of the opening quote.</param>
/// <param name="Start">The offset of the content, after the opening delimiter.</param>
/// <param name="End">The offset after the content, before the closing delimiter.</param>
/// <param name="Text">The content decoded. Each hole of an interpolated literal is a single <c>\0</c>.</param>
/// <param name="Form">How the content is spelled.</param>
/// <param name="QuoteCount">How many quote characters make the delimiter.</param>
internal sealed record ScriptLiteral(int Quote, int Start, int End, string Text, LiteralForm Form, int QuoteCount);

/// <summary>A literal that holds a renamed path, or part of one.</summary>
/// <param name="Literal">The literal.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Key">The rename key, or for a fragment the source extension.</param>
/// <param name="Value">The new path, or for a fragment the output extension.</param>
internal sealed record ScriptHit(ScriptLiteral Literal, ScriptHitKind Kind, string Key, string Value);

/// <summary>A text with its whole literals rewritten.</summary>
/// <param name="Text">The new text.</param>
/// <param name="Rewritten">How many literals changed.</param>
/// <param name="Left">Every hit not rewritten, in offset order.</param>
internal sealed record ScriptRewrite(string Text, int Rewritten, IReadOnlyList<ScriptHit> Left);

/// <summary>Finds and rewrites renamed paths in scripts and <c>project.godot</c>, by each literal's value and never by the call around it.</summary>
internal static class ScriptPaths
{
    private const string ResScheme = "res://";
    private const string UidScheme = "uid://";

    /// <summary>The kind of a file, by its name.</summary>
    /// <param name="file">The file's path.</param>
    /// <returns>Its kind, or null when it is not a script or a <c>project.godot</c>.</returns>
    internal static ScriptKind? KindOf(string file)
    {
        string extension = Path.GetExtension(file);
        if (string.Equals(extension, ".gd", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.GdScript;
        }
        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.CSharp;
        }
        return string.Equals(Path.GetFileName(file), "project.godot", StringComparison.OrdinalIgnoreCase) ? ScriptKind.ProjectFile : null;
    }

    /// <summary>Every string literal of a text, comments skipped. Text that cannot be lexed ends the list at that point.</summary>
    /// <param name="text">The text.</param>
    /// <param name="kind">The kind of file it comes from.</param>
    /// <returns>The literals, in offset order.</returns>
    internal static IReadOnlyList<ScriptLiteral> Literals(string text, ScriptKind kind) =>
        kind switch
        {
            ScriptKind.GdScript => ScriptLexers.GdScript(text),
            ScriptKind.CSharp => ScriptLexers.CSharp(text),
            _ => ScriptLexers.ProjectFile(text),
        };

    /// <summary>Every literal of a text that holds a renamed path or part of one.</summary>
    /// <param name="text">The text.</param>
    /// <param name="kind">The kind of file it comes from.</param>
    /// <param name="renames">Old <c>res://</c> path to new, compared ordinal.</param>
    /// <returns>The hits, in offset order and within a literal in ordinal key order.</returns>
    internal static IReadOnlyList<ScriptHit> Find(string text, ScriptKind kind, IReadOnlyDictionary<string, string> renames)
    {
        List<string> keys = [.. renames.Keys.Order(StringComparer.Ordinal)];
        Dictionary<string, string> extensions = kind == ScriptKind.ProjectFile ? [] : KeyExtensions(keys, renames);
        List<ScriptHit> hits = [];
        foreach (ScriptLiteral literal in Literals(text, kind))
        {
            string compared = HasAutoloadStar(literal, kind) ? literal.Text[1..] : literal.Text;
            if (renames.TryGetValue(compared, out string? value))
            {
                hits.Add(new ScriptHit(literal, ScriptHitKind.Whole, compared, value));
                continue;
            }
            int before = hits.Count;
            AddInside(literal, keys, renames, hits);
            if (hits.Count == before)
            {
                AddFragment(literal, extensions, hits);
            }
        }
        return hits;
    }

    /// <summary>Rewrites every literal whose whole text is a renamed path, leaving every other byte as it was.</summary>
    /// <param name="text">The text, with its byte order mark already stripped.</param>
    /// <param name="kind">The kind of file it comes from.</param>
    /// <param name="renames">Old <c>res://</c> path to new, compared ordinal.</param>
    /// <returns>The new text, how many literals changed, and every hit left as it was.</returns>
    internal static ScriptRewrite Rewrite(string text, ScriptKind kind, IReadOnlyDictionary<string, string> renames)
    {
        IReadOnlyList<ScriptHit> hits = Find(text, kind, renames);
        StringBuilder written = new(text);
        List<ScriptHit> left = [];
        int rewritten = 0;
        for (int index = hits.Count - 1; index >= 0; index--)
        {
            ScriptHit hit = hits[index];
            string? spelled = hit.Kind == ScriptHitKind.Whole ? Spelled(text, hit, kind) : null;
            if (spelled is null)
            {
                left.Add(hit);
                continue;
            }
            written.Remove(hit.Literal.Start, hit.Literal.End - hit.Literal.Start).Insert(hit.Literal.Start, spelled);
            rewritten++;
        }
        left.Reverse();
        return new ScriptRewrite(written.ToString(), rewritten, left);
    }

    private static void AddInside(ScriptLiteral literal, List<string> keys, IReadOnlyDictionary<string, string> renames, List<ScriptHit> hits)
    {
        if (!literal.Text.Contains(ResScheme, StringComparison.Ordinal))
        {
            return;
        }
        foreach (string key in keys)
        {
            if (literal.Text.Contains(key, StringComparison.Ordinal))
            {
                hits.Add(new ScriptHit(literal, ScriptHitKind.Inside, key, renames[key]));
            }
        }
    }

    // Each key extension with the output extension of the first key that has it.
    private static Dictionary<string, string> KeyExtensions(List<string> keys, IReadOnlyDictionary<string, string> renames)
    {
        Dictionary<string, string> extensions = new(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            string extension = Path.GetExtension(key);
            if (extension.Length > 0)
            {
                extensions.TryAdd(extension, Path.GetExtension(renames[key]));
            }
        }
        return extensions;
    }

    // A whole res:// or uid:// path to a file this run did not convert is not a built path.
    private static void AddFragment(ScriptLiteral literal, Dictionary<string, string> extensions, List<ScriptHit> hits)
    {
        bool wholePath =
            literal.Form != LiteralForm.Interpolated
            && (literal.Text.StartsWith(ResScheme, StringComparison.Ordinal) || literal.Text.StartsWith(UidScheme, StringComparison.Ordinal));
        if (wholePath)
        {
            return;
        }
        foreach ((string extension, string output) in extensions)
        {
            if (literal.Text.EndsWith(extension, StringComparison.Ordinal))
            {
                hits.Add(new ScriptHit(literal, ScriptHitKind.Fragment, extension, output));
                return;
            }
        }
    }

    // The hit's value in the literal's own spelling, or null when that spelling cannot hold it.
    private static string? Spelled(string text, ScriptHit hit, ScriptKind kind)
    {
        ScriptLiteral literal = hit.Literal;
        char quote = text[literal.Quote];
        string? spelled = literal.Form switch
        {
            LiteralForm.Regular => EscapedRegular(hit.Value, quote),
            LiteralForm.Verbatim => DoubledQuotes(hit.Value),
            LiteralForm.Raw => AsRaw(hit.Value, quote),
            _ => Interpolated(text, literal, hit.Value),
        };
        return spelled is not null && HasAutoloadStar(literal, kind) ? "*" + spelled : spelled;
    }

    private static bool HasAutoloadStar(ScriptLiteral literal, ScriptKind kind) => kind == ScriptKind.ProjectFile && literal.Text.StartsWith('*');

    private static string EscapedRegular(string value, char quote) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(quote.ToString(), "\\" + quote, StringComparison.Ordinal);

    private static string DoubledQuotes(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static string? AsRaw(string value, char quote) => value.Contains(quote, StringComparison.Ordinal) ? null : value;

    // Only a literal with no hole reaches here, so its braces are all text.
    private static string? Interpolated(string text, ScriptLiteral literal, string value)
    {
        string? spelled;
        if (literal.QuoteCount >= ScriptLexers.RawQuoteCount)
        {
            spelled = AsRaw(value, '"');
        }
        else
        {
            spelled = HasVerbatimPrefix(text, literal.Quote) ? DoubledQuotes(value) : EscapedRegular(value, '"');
        }
        return spelled?.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
    }

    // True for $@" and @$", the two spellings of a verbatim interpolated literal.
    private static bool HasVerbatimPrefix(string text, int quote) => text[quote - 1] == '@' || (quote >= 2 && text[quote - 2] == '@');
}
