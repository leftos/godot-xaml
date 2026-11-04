using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// Answers a hover with a fenced signature line and, where the class reference has one, the description: on a class tag, a
/// property or signal attribute, an enum member in a value, a <c>{Key}</c> reference and an <c>x:</c> attribute. Anything
/// else (whitespace, content, a comment, an unknown prefix or class, a name the metadata lacks) has no hover. The class
/// reference of each Godot minor version is read on the first hover that needs it, and kept.
/// </summary>
internal sealed partial class HoverHandler(DocumentStore store, ProjectCache projects, ParseFunction parse, DocsLoader loadDocs)
{
    private const string FormatLine = "godot-xaml format attribute: https://github.com/leftos/godot-xaml/blob/main/docs/format/README.md";

    private const string GlobalScope = "@GlobalScope";

    // C#'s reserved keywords, which GodotSharp escapes with '@' when an argument is named like one.
    private static readonly FrozenSet<string> CSharpKeywords = FrozenSet.ToFrozenSet(
        [
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "false",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "goto",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "true",
            "try",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "using",
            "virtual",
            "void",
            "volatile",
            "while",
        ],
        StringComparer.Ordinal
    );

    private readonly ConcurrentDictionary<string, Lazy<ClassDocs?>> docsByVersion = new(StringComparer.Ordinal);

    /// <summary>The hover at a position of a document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="position">The 0-based position, clamped into the text.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The hover, or null for a document not open or a position with nothing to describe.</returns>
    public async Task<Hover?> HoverAsync(string uri, LspPosition position, CancellationToken cancellation)
    {
        if (store.Find(uri) is not { } document)
        {
            return null;
        }

        // The project's lookup takes its lock and may load metadata: it must not run on the dispatch thread.
        await Task.Yield();

        ProjectContext? project = await projects.ResolveAsync(document.Path).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        Hover? hover = await Task.Run(() => new Reader(this, parse, document, project, position).Read(), cancellation).ConfigureAwait(false);

        // A change or a close that landed while it ran makes the answer describe text the buffer no longer holds.
        return store.IsCurrent(document) ? hover : null;
    }

    // The class reference of a Godot minor version, read once; null when the server carries none.
    private ClassDocs? DocsFor(string version) => docsByVersion.GetOrAdd(version, v => new Lazy<ClassDocs?>(() => loadDocs(v))).Value;

    // An attribute value that refers to an x:Resources entry: a key in braces, with no whitespace, brace, colon, comma or quote.
    [GeneratedRegex("""^\s*\{([^\s{}:,"]+)\}\s*$""", RegexOptions.CultureInvariant)]
    private static partial Regex KeyReference();

    // A fenced signature and the paragraphs after it, and the span it describes.
    private sealed record Found(string Signature, IReadOnlyList<string?> Paragraphs, TextSpan Span)
    {
        public string Markdown()
        {
            StringBuilder text = new();
            text.Append("```csharp\n").Append(Signature).Append("\n```");
            foreach (string? paragraph in Paragraphs)
            {
                if (!string.IsNullOrEmpty(paragraph))
                {
                    text.Append("\n\n").Append(paragraph);
                }
            }

            return text.ToString();
        }
    }

    // One hover's work over one document's text.
    private sealed class Reader(HoverHandler handler, ParseFunction parse, OpenDocument document, ProjectContext project, LspPosition position)
    {
        private readonly string text = document.Text;
        private readonly GodotMetadata metadata = project.Metadata;
        private readonly LineMap lines = new(document.Text);
        private int offset;

        public Hover? Read()
        {
            // The position names the character under the mouse; the context reads a cursor, so it is asked just after it.
            offset = lines.ClampedOffset(new LinePosition(position.Line, position.Character));
            XscnCursorContext context = XscnParser.ContextAt(text, Math.Min(offset + 1, text.Length), metadata);
            if (offset < context.Replace.Start || offset >= context.Replace.End)
            {
                return null;
            }

            Found? found = context.Kind switch
            {
                XscnCursorKind.TagName => ClassTag(context),
                XscnCursorKind.AttributeName => AttributeName(context),
                XscnCursorKind.AttributeValue => AttributeValue(context),
                _ => null,
            };
            return found is null ? null : new Hover(new MarkupContent("markdown", found.Markdown()), RangeOf(found.Span));
        }

        private ClassDocs? Docs => handler.DocsFor(project.Version);

        private LspRange RangeOf(TextSpan span)
        {
            LinePosition start = lines.ZeroBased(span.Start);
            LinePosition end = lines.ZeroBased(span.End);
            return new LspRange(new LspPosition(start.Line, start.Column), new LspPosition(end.Line, end.Column));
        }

        private Found? ClassTag(XscnCursorContext context)
        {
            if (ClassOf(context) is not { } cls)
            {
                return null;
            }

            string parent = cls.Parent is { } parentName ? " : " + CSharpClassName(parentName) : "";
            ClassDoc? doc = Docs?.Class(cls.Name);
            return new Found($"class {cls.CSharpName}{parent}", [doc?.Brief, doc?.Description], context.Replace);
        }

        private Found? AttributeName(XscnCursorContext context)
        {
            string name = context.AttributeName!;
            int colon = name.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                return name[..colon] == FormatPrefix() ? new Found(name, [FormatLine], context.Replace) : null;
            }

            if (ClassOf(context) is not { } cls)
            {
                return null;
            }

            if (metadata.FindPropertyByCSharpName(cls, name) is { } property)
            {
                return Property(cls, property, context.Replace);
            }

            return metadata.FindSignalByCSharpName(cls, name) is { } signal ? Signal(cls, signal, context.Replace) : null;
        }

        private Found Property(GodotClassInfo cls, GodotPropertyInfo property, TextSpan span)
        {
            XscnValueType type = XscnValueText.TypeOf(metadata, property);
            XscnEnumNames? names = type.Names ?? type.Declared?.Names;
            string? defaultLine = metadata.FindDefault(cls, property.Name) is { } found ? $"Default: {found.Text}" : null;
            string? valuesLine = names is { Names.Length: > 0 }
                ? $"{(names.IsFlags ? "Flags" : "Values")}: {string.Join(", ", names.Names.Select(n => n.Name))}"
                : null;
            string? description = Described(cls, doc => doc.Members, property.Name);
            return new Found($"{TypeName(property.Type, property.ClassName)} {property.CSharpName}", [defaultLine, valuesLine, description], span);
        }

        private Found Signal(GodotClassInfo cls, GodotSignalInfo signal, TextSpan span)
        {
            string arguments = string.Join(", ", signal.Arguments.Select(a => $"{TypeName(a.Type, a.ClassName)} {CamelCase(a.Name)}"));
            return new Found($"signal {signal.CSharpName}({arguments})", [Described(cls, doc => doc.Signals, signal.Name)], span);
        }

        private Found? AttributeValue(XscnCursorContext context)
        {
            string value = text.Substring(context.Replace.Start, context.Replace.Length);
            Match key = KeyReference().Match(value);
            if (key.Success)
            {
                return Reference(key.Groups[1].Value, context.Replace);
            }

            string? name = context.AttributeName;
            if (name is null || name.Contains(':', StringComparison.Ordinal) || ClassOf(context) is not { } cls)
            {
                return null;
            }

            return metadata.FindPropertyByCSharpName(cls, name) is { } property ? EnumValue(property, context.Replace) : null;
        }

        private Found? Reference(string key, TextSpan span)
        {
            XscnParseResult result = parse(text, document.Kind, metadata);
            return result.Document.Resources.FirstOrDefault(r => r.Key == key)?.Type is XscnClassType entry
                ? new Found($"{{{key}}}: {entry.Class.CSharpName}", [], span)
                : null;
        }

        // The enum member under the cursor in an enum property's value.
        private Found? EnumValue(GodotPropertyInfo property, TextSpan value)
        {
            XscnValueType type = XscnValueText.TypeOf(metadata, property);
            if ((type.Names ?? type.Declared?.Names) is not { Names.Length: > 0 } names || WordAt(value) is not { } word)
            {
                return null;
            }

            string written = text.Substring(word.Start, word.Length);
            if (names.Names.FirstOrDefault(n => n.Name == written) is not { } member)
            {
                return null;
            }

            string signature = $"{member.Name} = {member.Value.ToString(CultureInfo.InvariantCulture)}";
            return new Found(signature, [ConstantDescription(property, member.Name)], word);
        }

        // The run of name characters under the cursor, inside the value.
        private TextSpan? WordAt(TextSpan value)
        {
            if (offset >= value.End || !IsWordChar(text[offset]))
            {
                return null;
            }

            int start = offset;
            while (start > value.Start && IsWordChar(text[start - 1]))
            {
                start--;
            }

            int end = offset;
            while (end < value.End && IsWordChar(text[end]))
            {
                end++;
            }

            return new TextSpan(start, end - start);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // An enum constant's description: its enum's class's (or the global scope's) constant of the member's Godot name.
        private string? ConstantDescription(GodotPropertyInfo property, string csharpName)
        {
            GodotEnumInfo? enumInfo = property.EnumType is { } enumType ? metadata.FindEnum(enumType) : null;
            if (enumInfo?.Members.FirstOrDefault(m => m.CSharpName == csharpName) is not { } member)
            {
                return null;
            }

            return OwnerOf(enumInfo) is { } owner ? Described(owner, doc => doc.Constants, member.GodotName) : GlobalConstant(member.GodotName);
        }

        // A global enum constant's description, from the global scope's constants.
        private string? GlobalConstant(string godotName) =>
            Docs?.Class(GlobalScope) is { } global ? NonEmpty(global.Constants?.GetValueOrDefault(godotName)?.Description) : null;

        // The class that declares a class enum; null for a global enum.
        private GodotClassInfo? OwnerOf(GodotEnumInfo enumInfo)
        {
            int dot = enumInfo.QualifiedName.LastIndexOf('.');
            return dot > 0 ? metadata.FindClass(enumInfo.QualifiedName[..dot]) : null;
        }

        // The first non-empty description of a name up a class's chain: a class that overrides a member lists it with none.
        private string? Described(GodotClassInfo cls, Func<ClassDoc, Dictionary<string, MemberDoc>?> table, string godotName)
        {
            if (Docs is not { } docs)
            {
                return null;
            }

            return metadata
                .AncestorsOf(cls)
                .Select(c => docs.Class(c.Name) is { } doc ? NonEmpty(table(doc)?.GetValueOrDefault(godotName)?.Description) : null)
                .FirstOrDefault(d => d is not null);
        }

        private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

        // The class of a class tag the cursor is in or on; null for a format element, a scene tag or a property element.
        private static GodotClassInfo? ClassOf(XscnCursorContext context) =>
            context.ElementPrefix is null && context.ElementName is { } name && !name.Contains('.', StringComparison.Ordinal) ? context.Class : null;

        private string TypeName(string variantType, string className) => NameRules.TypeName(metadata, variantType, className);

        private string CSharpClassName(string godotName) => NameRules.CSharpClassName(metadata, godotName);

        private string? FormatPrefix() => NameRules.FormatPrefix(text);

        // A Godot snake_case argument name as the C# API names it (toggled_on, toggledOn; event, @event).
        private static string CamelCase(string snake)
        {
            string[] words = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return snake;
            }

            string name = words[0] + string.Concat(words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
            return CSharpKeywords.Contains(name) ? "@" + name : name;
        }
    }
}
