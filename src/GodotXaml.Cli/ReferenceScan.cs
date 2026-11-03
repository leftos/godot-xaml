using System.Text;
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
    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["quot"] = "\"",
        ["apos"] = "'",
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
    };

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
            XmlSpans(lines, spans, tags: null);
        }
        else
        {
            for (int index = 0; index < lines.Length; index++)
            {
                GodotTextSpans(lines[index], index + 1, 0, raw: null, spans);
            }
        }
        return [.. spans.OrderBy(span => span.Line).ThenBy(span => span.Column)];
    }

    // The spans of Godot value text that starts at a 0-based offset in its line. Each quote kind is paired on its own, so
    // a path in double quotes inside a single-quoted value is a span too. A span of a decoded value takes its column
    // from the raw line index of the first character it came from.
    private static void GodotTextSpans(string text, int line, int offset, int[]? raw, List<(int Line, int Column, string Text)> spans)
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
                spans.Add((line, (raw is null ? offset + at : raw[at]) + 1, text[(at + 1)..close]));
                at = close;
            }
        }
    }

    // The spans of an XML file: each attribute value whole, and the Godot value text's quoted strings inside it. Tags,
    // comments and values run across lines, so the place in the markup carries from one line to the next. A walk given
    // tags instead records each tag with its name and its attribute values, which is how an x:Uids table is read.
    private static void XmlSpans(string[] lines, List<(int Line, int Column, string Text)>? spans, List<XamlTag>? tags)
    {
        XamlWalk walk = new(spans, tags);
        Markup markup = Markup.Text;
        for (int index = 0; index < lines.Length; index++)
        {
            for (int at = 0; at < lines[index].Length; )
            {
                (markup, at) = Step(walk, markup, lines[index], index + 1, at);
            }
        }
    }

    // One step through an XML file: the place the markup is at next and the index it starts at.
    private static (Markup Markup, int At) Step(XamlWalk walk, Markup markup, string line, int number, int at)
    {
        if (markup == Markup.Comment)
        {
            return AfterComment(line, at);
        }
        if (markup == Markup.Tag)
        {
            return TagStep(walk, line, number, at);
        }
        if (markup == Markup.Text)
        {
            return AfterText(walk, line, at);
        }
        return AfterValue(line, at, markup == Markup.DoubleQuoted ? '"' : '\'', markup);
    }

    // Element text up to the next tag or comment, which the walk opens.
    private static (Markup Markup, int At) AfterText(XamlWalk walk, string line, int at)
    {
        int open = line.IndexOf('<', at);
        if (open < 0)
        {
            return (Markup.Text, line.Length);
        }
        if (string.CompareOrdinal(line, open, CommentOpen, 0, CommentOpen.Length) == 0)
        {
            return (Markup.Comment, open + CommentOpen.Length);
        }
        walk.Tag = new XamlTag(NameAt(line, open + 1));
        return (Markup.Tag, open + 1);
    }

    // The name of a tag at a 0-based offset: up to white space, '/' or '>'.
    private static string NameAt(string line, int at)
    {
        int start = at;
        while (at < line.Length && !char.IsWhiteSpace(line[at]) && line[at] is not ('/' or '>'))
        {
            at++;
        }
        return line[start..at];
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

    // One step through a tag, which ends at its '>': the walk then hands the tag on.
    private static (Markup Markup, int At) TagStep(XamlWalk walk, string line, int number, int at)
    {
        if (line[at] == '=')
        {
            return AttributeStep(walk, line, number, at);
        }
        if (line[at] != '>')
        {
            return (Markup.Tag, at + 1);
        }
        walk.EndTag();
        return (Markup.Text, at + 1);
    }

    // The attribute whose '=' stands at a 0-based offset: its value opens at the quote after '=' and closes at the next
    // same quote, with no escape, as XscnWriter writes it. The value is read decoded, so an entity-encoded path is found.
    private static (Markup Markup, int At) AttributeStep(XamlWalk walk, string line, int number, int at)
    {
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
        RecordAttribute(walk, AttributeName(line, at), open, number, DecodeValue(line, open + 1, close));
        return (Markup.Tag, close + 1);
    }

    // Records an attribute whose value starts after an opening quote: its tag gets the decoded text, and the spans inside
    // that text are added at the raw line column of the character each came from.
    private static void RecordAttribute(XamlWalk walk, string name, int open, int number, (string Text, int[] Raw) value)
    {
        walk.Tag?.Attributes.Add(new XamlAttribute(name, value.Text));
        if (walk.Spans is null)
        {
            return;
        }
        walk.Spans.Add((number, open + 1, value.Text));
        GodotTextSpans(value.Text, number, 0, value.Raw, walk.Spans);
    }

    // The name of the attribute whose '=' stands at a 0-based offset: the run before it, after white space.
    private static string AttributeName(string line, int at)
    {
        int end = at;
        while (end > 0 && char.IsWhiteSpace(line[end - 1]))
        {
            end--;
        }
        int start = end;
        while (start > 0 && !char.IsWhiteSpace(line[start - 1]) && line[start - 1] != '<')
        {
            start--;
        }
        return line[start..end];
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

    // The text of an XML attribute value that starts at a 0-based offset and ends before another, with its entities
    // decoded, and the raw index in the line of the first character each decoded character came from.
    private static (string Text, int[] Raw) DecodeValue(string line, int start, int end)
    {
        StringBuilder text = new(end - start);
        List<int> raw = new(end - start);
        int at = start;
        while (at < end)
        {
            (string Text, int Length)? entity = line[at] == '&' ? EntityAt(line, at, end) : null;
            if (entity is null)
            {
                text.Append(line[at]);
                raw.Add(at);
                at++;
                continue;
            }
            text.Append(entity.Value.Text);
            for (int index = 0; index < entity.Value.Text.Length; index++)
            {
                raw.Add(at);
            }
            at += entity.Value.Length;
        }
        return (text.ToString(), [.. raw]);
    }

    // What an XML entity at a 0-based offset stands for and the raw characters it takes, or null when it is not one of
    // the five named entities or a decimal or hex character reference to a code point XML can carry.
    private static (string Text, int Length)? EntityAt(string line, int at, int end)
    {
        int semicolon = line.IndexOf(';', at + 1);
        if (semicolon < 0 || semicolon >= end)
        {
            return null;
        }
        string body = line[(at + 1)..semicolon];
        int length = semicolon - at + 1;
        return NamedEntities.TryGetValue(body, out string? named) ? (named, length) : CharacterReference(body, length);
    }

    // A decimal or hex character reference, or null when its text is not one or names a code point XML cannot carry.
    private static (string Text, int Length)? CharacterReference(string body, int length)
    {
        if (body.Length < 2 || body[0] != '#')
        {
            return null;
        }
        int code = DigitValue(body);
        return Carried(code) ? (TextOf(code), length) : null;
    }

    // The number a character reference names, or -1 when it is not a decimal or hex run of digits.
    private static int DigitValue(string body)
    {
        bool hexadecimal = body[1] is 'x' or 'X';
        return Digits(body[(hexadecimal ? 2 : 1)..], hexadecimal ? 16 : 10);
    }

    // The text a code point is written as.
    private static string TextOf(int code) => code <= 0xFFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code);

    // The number a run of digits in a base names, or -1 when the run is empty, too wide for a code point, or holds a
    // digit the base does not have.
    private static int Digits(string digits, int numberBase)
    {
        if (digits.Length is 0 or > 7)
        {
            return -1;
        }
        int code = 0;
        foreach (char digit in digits)
        {
            int value = DigitOf(digit, numberBase);
            if (value < 0)
            {
                return -1;
            }
            code = (code * numberBase) + value;
        }
        return code;
    }

    // Whether a number is a code point XML can carry: a Unicode scalar value, so not a surrogate.
    private static bool Carried(int code) => code > 0 && code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF);

    // The value of a digit in a base, or -1 when the character is not one.
    private static int DigitOf(char digit, int numberBase)
    {
        if (digit is >= '0' and <= '9')
        {
            return digit - '0';
        }
        if (numberBase == 16 && digit is >= 'a' and <= 'f')
        {
            return digit - 'a' + 10;
        }
        if (numberBase == 16 && digit is >= 'A' and <= 'F')
        {
            return digit - 'A' + 10;
        }
        return -1;
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
        return close > afterSpan && DecodeValue(line, open + ResourceCall.Length, close).Text.Contains("uid://", StringComparison.Ordinal);
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

    // Every path the file's x:Uids table gives a uid, read by the scan's own tokenizer so an entry may run across lines.
    private static HashSet<string> UidTablePaths(string text)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (XamlTag tag in Tags(text))
        {
            if (!string.Equals(tag.Name, "x:Uid", StringComparison.Ordinal))
            {
                continue;
            }
            string? path = Attribute(tag, "Path");
            string? value = Attribute(tag, "Value");
            if (path is not null && value?.StartsWith("uid://", StringComparison.Ordinal) == true)
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    /// <summary>The value of an XML attribute on a line, read by the scan's tokenizer with its entities decoded.</summary>
    /// <param name="line">The line that holds the attribute.</param>
    /// <param name="name">The attribute's name, which matches only whole.</param>
    /// <returns>The value between its quotes, or null when the line has no such attribute or its value does not close.</returns>
    internal static string? AttributeValue(string line, string name)
    {
        foreach (XamlTag tag in Tags(line))
        {
            string? value = Attribute(tag, name);
            if (value is not null)
            {
                return value;
            }
        }
        return null;
    }

    // Every tag of a text, with the attributes whose values close on it, read by the scan's tokenizer.
    private static List<XamlTag> Tags(string text)
    {
        List<XamlTag> tags = [];
        XmlSpans(text.Split('\n'), spans: null, tags);
        return tags;
    }

    // The value of one named attribute of a tag, matched whole.
    private static string? Attribute(XamlTag tag, string name) =>
        tag.Attributes.Find(attribute => string.Equals(attribute.Name, name, StringComparison.Ordinal))?.Value;

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

    // One pass over an XML text: what the walk collects, and the tag it is reading, which carries from line to line.
    private sealed class XamlWalk(List<(int Line, int Column, string Text)>? spans, List<XamlTag>? tags)
    {
        public List<(int Line, int Column, string Text)>? Spans { get; } = spans;

        public List<XamlTag>? Tags { get; } = tags;

        public XamlTag? Tag { get; set; }

        // Hands the tag just read to the walk's tags, when it collects them.
        public void EndTag()
        {
            if (Tag is not null)
            {
                Tags?.Add(Tag);
                Tag = null;
            }
        }
    }

    // A tag a walk read: its name, and each attribute whose value closes on a line of it, with the value decoded.
    private sealed class XamlTag(string name)
    {
        public string Name { get; } = name;

        public List<XamlAttribute> Attributes { get; } = [];
    }

    // An attribute of a tag, with its value decoded.
    private sealed record XamlAttribute(string Name, string Value);

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
