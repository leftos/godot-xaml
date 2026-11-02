using GodotXaml.Core.Diagnostics;

namespace GodotXaml.Cli;

/// <summary>A quoted string in a file the run does not write that names a file it converts, or part of such a name.</summary>
/// <param name="File">The file that holds it.</param>
/// <param name="Line">Its 1-based line.</param>
/// <param name="Column">The 1-based column of the quote that opens it.</param>
/// <param name="Key">The path it holds, or for a fragment the source extension.</param>
/// <param name="Value">The path the run converts it to, or for a fragment the output extension.</param>
/// <param name="CarriesUid">Whether the reference carries a uid of its own, so it still resolves once the source goes.</param>
/// <param name="Kind">Whether the string is the path, holds it inside longer text, or may build one at runtime.</param>
internal sealed record ScannedReference(string File, int Line, int Column, string Key, string Value, bool CarriesUid, ScriptHitKind Kind);

/// <summary>The quoting rules of a file kind, which decide where a quoted string opens and closes.</summary>
internal enum QuoteGrammar
{
    /// <summary>Godot's value text, in <c>.tscn</c> and <c>.tres</c>: a backslash escapes the next character.</summary>
    GodotText,

    /// <summary>XML, in <c>.xscn</c> and <c>.xres</c>: an attribute value closes at the next same quote, and holds Godot value text.</summary>
    Xml,
}

/// <summary>Finds the references to the files a run converts in the scenes, resources, scripts and <c>project.godot</c> it does not write.</summary>
internal static class ReferenceScan
{
    private const string ResourceCall = "Resource(";
    private const string CommentOpen = "<!--";
    private const string CommentClose = "-->";
    private static readonly string[] Extensions = [".tscn", ".tres", ".xscn", ".xres"];
    private static readonly char[] Quotes = ['"', '\''];

    /// <summary>Finds every whole quoted rename key in the scene and resource files under a root, and every script hit.</summary>
    /// <param name="root">The project root to walk.</param>
    /// <param name="renames">Old <c>res://</c> path to new, compared ordinal.</param>
    /// <param name="skip">The full paths of the files the run reads or writes itself.</param>
    /// <param name="error">Where a folder or file that cannot be read is reported.</param>
    /// <returns>Every reference found, in file, line and column order.</returns>
    internal static IEnumerable<ScannedReference> Find(
        string root,
        IReadOnlyDictionary<string, string> renames,
        HashSet<string> skip,
        TextWriter error
    )
    {
        List<ScannedReference> references = [];
        foreach (string file in Files(root, error))
        {
            if (skip.Contains(Path.GetFullPath(file)))
            {
                continue;
            }
            string? text = SourceText.Read(file, error);
            if (text is not null)
            {
                ScanFile(file, text, renames, references);
            }
        }
        return references;
    }

    /// <summary>Every quoted span in a text, with the 1-based line and the column of its opening quote.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="grammar">The quoting rules of the file the text comes from.</param>
    /// <returns>Each span's line, column and content, in line and column order.</returns>
    internal static IEnumerable<(int Line, int Column, string Text)> Quoted(string text, QuoteGrammar grammar)
    {
        List<(int Line, int Column, string Text)> spans = [];
        string[] lines = text.Split('\n');
        if (grammar == QuoteGrammar.Xml)
        {
            XmlSpans(lines, spans);
        }
        else
        {
            for (int index = 0; index < lines.Length; index++)
            {
                GodotTextSpans(lines[index], index + 1, 0, spans);
            }
        }
        return [.. spans.OrderBy(span => span.Line).ThenBy(span => span.Column)];
    }

    // The spans of Godot value text that starts at a 0-based offset in its line. Each quote kind is paired on its own, so
    // a path in double quotes inside a single-quoted value is a span too.
    private static void GodotTextSpans(string text, int line, int offset, List<(int Line, int Column, string Text)> spans)
    {
        foreach (char quote in Quotes)
        {
            for (int at = text.IndexOf(quote); at >= 0; at = text.IndexOf(quote, at + 1))
            {
                int close = CloseQuote(text, quote, at);
                if (close < 0)
                {
                    break;
                }
                spans.Add((line, offset + at + 1, text[(at + 1)..close]));
                at = close;
            }
        }
    }

    // The spans of an XML file: each attribute value whole, and the Godot value text's quoted strings inside it. Tags,
    // comments and values run across lines, so the place in the markup carries from one line to the next.
    private static void XmlSpans(string[] lines, List<(int Line, int Column, string Text)> spans)
    {
        Markup markup = Markup.Text;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            for (int at = 0; at < line.Length; )
            {
                (markup, at) = markup switch
                {
                    Markup.Comment => AfterComment(line, at),
                    Markup.Tag => AfterTagCharacter(line, index + 1, at, spans),
                    Markup.DoubleQuoted => AfterValue(line, at, '"', markup),
                    Markup.SingleQuoted => AfterValue(line, at, '\'', markup),
                    _ => AfterText(line, at),
                };
            }
        }
    }

    // Element text up to the next tag or comment.
    private static (Markup Markup, int At) AfterText(string line, int at)
    {
        int open = line.IndexOf('<', at);
        if (open < 0)
        {
            return (Markup.Text, line.Length);
        }
        return string.CompareOrdinal(line, open, CommentOpen, 0, CommentOpen.Length) == 0
            ? (Markup.Comment, open + CommentOpen.Length)
            : (Markup.Tag, open + 1);
    }

    private static (Markup Markup, int At) AfterComment(string line, int at)
    {
        int close = line.IndexOf(CommentClose, at, StringComparison.Ordinal);
        return close < 0 ? (Markup.Comment, line.Length) : (Markup.Text, close + CommentClose.Length);
    }

    // The rest of an attribute value that began on an earlier line; it yields no span.
    private static (Markup Markup, int At) AfterValue(string line, int at, char quote, Markup markup)
    {
        int close = line.IndexOf(quote, at);
        return close < 0 ? (markup, line.Length) : (Markup.Tag, close + 1);
    }

    // One step through a tag: an attribute value opens at the quote after '=' and closes at the next same quote, with no
    // escape, as XscnWriter writes it.
    private static (Markup Markup, int At) AfterTagCharacter(string line, int number, int at, List<(int Line, int Column, string Text)> spans)
    {
        if (line[at] != '=')
        {
            return (line[at] == '>' ? Markup.Text : Markup.Tag, at + 1);
        }
        int open = SkipWhiteSpace(line, at + 1);
        if (open >= line.Length || line[open] is not ('"' or '\''))
        {
            return (Markup.Tag, open);
        }
        int close = line.IndexOf(line[open], open + 1);
        if (close < 0)
        {
            return (line[open] == '"' ? Markup.DoubleQuoted : Markup.SingleQuoted, line.Length);
        }
        string value = line[(open + 1)..close];
        spans.Add((number, open + 1, value));
        GodotTextSpans(value, number, open + 1, spans);
        return (Markup.Tag, close + 1);
    }

    // XML allows white space on either side of an attribute's '='.
    private static int SkipWhiteSpace(string line, int at)
    {
        while (at < line.Length && char.IsWhiteSpace(line[at]))
        {
            at++;
        }
        return at;
    }

    // The quote that closes a span: a backslash escapes the next character, as Godot's value text writes it.
    private static int CloseQuote(string line, char quote, int open)
    {
        for (int at = open + 1; at < line.Length; at++)
        {
            if (line[at] == '\\')
            {
                at++;
            }
            else if (line[at] == quote)
            {
                return at;
            }
        }
        return -1;
    }

    // In a scene or resource a key matches only as a whole quoted string, so "res://ui/button.tscn" and
    // Resource("res://ui/button.tscn") match and "res://ui/button.tscn.import" does not.
    private static void ScanFile(string file, string text, IReadOnlyDictionary<string, string> renames, List<ScannedReference> references)
    {
        ScriptKind? kind = ScriptPaths.KindOf(file);
        if (kind is not null)
        {
            ScanScript(file, text, kind.Value, renames, references);
            return;
        }
        string[] lines = text.Split('\n');
        QuoteGrammar grammar = GrammarOf(file);
        HashSet<string>? uidTable = grammar == QuoteGrammar.Xml ? UidTablePaths(text) : null;
        foreach ((int line, int column, string span) in Quoted(text, grammar))
        {
            if (renames.TryGetValue(span, out string? value))
            {
                bool carriesUid = uidTable?.Contains(span) == true || CarriesUid(lines[line - 1], column, span);
                references.Add(new ScannedReference(file, line, column, span, value, carriesUid, ScriptHitKind.Whole));
            }
        }
    }

    private static void ScanScript(
        string file,
        string text,
        ScriptKind kind,
        IReadOnlyDictionary<string, string> renames,
        List<ScannedReference> references
    )
    {
        LineMap lines = new(text);
        foreach (ScriptHit hit in ScriptPaths.Find(text, kind, renames))
        {
            LinePosition position = lines.OneBased(hit.Literal.Quote);
            references.Add(new ScannedReference(file, position.Line, position.Column, hit.Key, hit.Value, CarriesUid: false, hit.Kind));
        }
    }

    // The reference's own uid: a .tscn or .tres ext_resource's uid attribute, or an .xscn or .xres x:Uids entry for the
    // path, or a uid:// inside the same Resource(...) call as the path. A uid belonging to another reference on the
    // same line does not count.
    private static bool CarriesUid(string line, int column, string span)
    {
        if (line.StartsWith("[ext_resource", StringComparison.Ordinal))
        {
            return line.Contains("uid=\"uid://", StringComparison.Ordinal);
        }
        int quote = column - 1;
        int open = line.LastIndexOf(ResourceCall, quote, StringComparison.Ordinal);
        if (open < 0)
        {
            return false;
        }
        int close = line.IndexOf(')', open);
        int afterSpan = quote + span.Length + 1;
        return close > afterSpan && line[(open + ResourceCall.Length)..close].Contains("uid://", StringComparison.Ordinal);
    }

    /// <summary>The quoting rules of a file, by its extension: XML for <c>.xscn</c> and <c>.xres</c>, Godot value text otherwise.</summary>
    /// <param name="file">The file's path.</param>
    /// <returns>The grammar its quoted strings follow.</returns>
    internal static QuoteGrammar GrammarOf(string file)
    {
        string extension = Path.GetExtension(file);
        bool xml =
            string.Equals(extension, ".xscn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xres", StringComparison.OrdinalIgnoreCase);
        return xml ? QuoteGrammar.Xml : QuoteGrammar.GodotText;
    }

    // Every path the file's x:Uids table gives a uid.
    private static HashSet<string> UidTablePaths(string text)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("<x:Uid ", StringComparison.Ordinal) && line.Contains("uid://", StringComparison.Ordinal))
            {
                string? path = AttributeValue(line, "Path");
                if (path is not null)
                {
                    paths.Add(path);
                }
            }
        }
        return paths;
    }

    /// <summary>The value of an XML attribute on a line, read by the XML grammar.</summary>
    /// <param name="line">The line that holds the attribute.</param>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The value between its quotes, or null when the line has no such attribute or its value does not close.</returns>
    internal static string? AttributeValue(string line, string name)
    {
        int at = line.IndexOf(name + "=", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }
        int open = at + name.Length + 1;
        if (open >= line.Length || line[open] is not ('"' or '\''))
        {
            return null;
        }
        int close = line.IndexOf(line[open], open + 1);
        return close < 0 ? null : line[(open + 1)..close];
    }

    // Every accepted file under the root, in one order on every OS. A folder Godot leaves out of its file system is
    // skipped: a hidden one, one holding .gdignore, and one holding its own project.godot, a nested project. So is a
    // .NET project's build output, its bin and obj.
    private static List<string> Files(string root, TextWriter error)
    {
        List<string> found = [];
        Walk(root, found, new WalkRule(Accepted, Excluded: null, SkipLinks: false), error);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    /// Every file under a project root with one of the given extensions, by the folder rule of the scan's own walk, ordered
    /// by its path under the root with '/' as the separator, compared ordinal. A folder that is a link or a junction is not
    /// entered, so a run never works on files outside the project.
    /// </summary>
    /// <param name="root">The project root's full path.</param>
    /// <param name="extensions">The extensions to find, each with its leading dot, matched case-insensitively.</param>
    /// <param name="skipAddons">Whether <c>addons</c> directly under the root is skipped; one deeper down is walked.</param>
    /// <param name="error">Where a folder that cannot be read is reported.</param>
    /// <returns>The files, or null when a folder could not be read.</returns>
    internal static List<string>? ProjectFiles(string root, IReadOnlyCollection<string> extensions, bool skipAddons, TextWriter error)
    {
        List<string> found = [];
        string? addons = skipAddons ? Path.GetFullPath(Path.Combine(root, "addons")) : null;
        var rule = new WalkRule(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase), addons, SkipLinks: true);
        bool read = Walk(root, found, rule, error);
        return read ? [.. found.OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)] : null;
    }

    // Whether every folder was read; a folder that cannot be is reported and the walk goes on with the rest.
    private static bool Walk(string folder, List<string> found, WalkRule rule, TextWriter error)
    {
        try
        {
            bool holdsCsproj = false;
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                holdsCsproj |= string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase);
                if (rule.Accept(path))
                {
                    found.Add(path);
                }
            }
            bool read = true;
            foreach (string subfolder in Directory.EnumerateDirectories(folder))
            {
                bool walked = !Skip(subfolder) && !(holdsCsproj && IsBuildOutput(subfolder)) && !rule.Leaves(subfolder);
                read &= !walked || Walk(subfolder, found, rule, error);
            }
            return read;
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine($"{folder}: access denied.");
        }
        catch (IOException)
        {
            error.WriteLine($"{folder}: cannot be read.");
        }
        return false;
    }

    // Which files a walk keeps, the one folder it leaves out by full path, and whether it leaves out links and junctions.
    // Only a folder with a link target counts: another reparse point, such as a cloud folder, is walked.
    private sealed record WalkRule(Func<string, bool> Accept, string? Excluded, bool SkipLinks)
    {
        public bool Leaves(string folder) =>
            (Excluded is not null && InputFiles.PathComparer.Equals(Path.GetFullPath(folder), Excluded))
            || (SkipLinks && new DirectoryInfo(folder).LinkTarget is not null);
    }

    private static bool Accepted(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || ScriptPaths.KindOf(path) is not null;

    private static bool Skip(string folder) =>
        Path.GetFileName(folder).StartsWith('.')
        || File.Exists(Path.Combine(folder, ".gdignore"))
        || File.Exists(Path.Combine(folder, "project.godot"));

    private static bool IsBuildOutput(string folder) => Path.GetFileName(folder) is "bin" or "obj";

    // Where an XML scan stands at a character: in element text, a comment, a tag, or an attribute value still open.
    private enum Markup
    {
        Text,
        Comment,
        Tag,
        DoubleQuoted,
        SingleQuoted,
    }
}
