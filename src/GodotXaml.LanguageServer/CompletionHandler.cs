using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Xscn;
using GodotXaml.LanguageServer.Protocol;

namespace GodotXaml.LanguageServer;

/// <summary>
/// Answers a completion with the names or values the cursor's context takes: Godot classes, property elements and
/// <c>x:</c> elements in a tag name; properties, signals, theme overrides and the format's attributes in a start tag; enum
/// and flags members, <c>True</c>/<c>False</c> and <c>{Key}</c> references in a property's value. Each item replaces the
/// token at the cursor (a flags value's part); the list holds every candidate the context takes, and the client filters it.
/// Other values, content, comments and end tags get no list.
/// </summary>
internal sealed class CompletionHandler(DocumentStore store, ProjectCache projects)
{
    /// <summary>The completions at a cursor of a document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="position">The 0-based cursor, between two characters, clamped into the text.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    /// <returns>The completions, or null for a document not open or a cursor where nothing is completed.</returns>
    public async Task<CompletionList?> CompleteAsync(string uri, LspPosition position, CancellationToken cancellation)
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

        CompletionList? list = await Task.Run(() => new CompletionScope(document, project.Metadata).Complete(position), cancellation)
            .ConfigureAwait(false);

        // A change or a close that landed while it ran makes the list complete text the buffer no longer holds.
        return store.IsCurrent(document) ? list : null;
    }
}

/// <summary>A name a completion may offer, before it is filtered by what is typed.</summary>
/// <param name="Label">The name.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Detail">A type, <c>signal</c>, a parent class, an enum member's integer or an entry's class, or null.</param>
/// <param name="Insert">The text the completion inserts in the document's syntax, its label where the label reads as it.</param>
internal sealed record CompletionCandidate(string Label, CompletionItemKind Kind, string? Detail, string Insert);

/// <summary>What an element's tag makes it.</summary>
internal enum ElementShape
{
    /// <summary>An unknown prefix, or no name written.</summary>
    Unknown,

    /// <summary>A Godot class tag, its class known or not.</summary>
    ClassElement,

    /// <summary>A property element, <c>Owner.Property</c>.</summary>
    PropertyElement,

    /// <summary>An element of the format's prefix.</summary>
    FormatElement,

    /// <summary>A scene tag, of a prefix the document declares.</summary>
    SceneTag,
}

/// <summary>One completion's work over one document's text: the context, the names it reads and the list it makes.</summary>
internal sealed partial class CompletionScope
{
    // Indexing every class's properties is costly, so each metadata's attribute names are built once.
    private static readonly ConditionalWeakTable<GodotMetadata, XscnPropertyNames> NamesByMetadata = new();

    private readonly LineMap lines;
    private readonly Lazy<HashSet<string>> declaredPrefixes;

    /// <summary>Reads a document with its project's metadata.</summary>
    /// <param name="document">The document.</param>
    /// <param name="metadata">The metadata.</param>
    public CompletionScope(OpenDocument document, GodotMetadata metadata)
    {
        Text = document.Text;
        Kind = document.Kind;
        Metadata = metadata;
        Names = NamesByMetadata.GetValue(metadata, m => new XscnPropertyNames(m));
        lines = new LineMap(Text);
        FormatPrefix = NameRules.FormatPrefix(Text);
        declaredPrefixes = new(() => [.. NamespaceDeclaration().Matches(Text).Select(m => m.Groups[1].Value)]);
    }

    /// <summary>The document's text.</summary>
    public string Text { get; }

    /// <summary>Scene or resource.</summary>
    public XscnDocumentKind Kind { get; }

    /// <summary>The metadata.</summary>
    public GodotMetadata Metadata { get; }

    /// <summary>The metadata's attribute names.</summary>
    public XscnPropertyNames Names { get; }

    /// <summary>The prefix the document binds to the format's namespace, or null.</summary>
    public string? FormatPrefix { get; }

    /// <summary>The list at a cursor.</summary>
    /// <param name="position">The cursor.</param>
    /// <returns>The list, or null where nothing is completed.</returns>
    public CompletionList? Complete(LspPosition position)
    {
        int offset = lines.ClampedOffset(new LinePosition(position.Line, position.Character));
        XscnCursorContext context = ContextAt(offset);
        if (context.Kind == XscnCursorKind.AttributeValue)
        {
            return new ValueCompletions(this, context, offset).Candidates() is { } value ? List(value.Replace, value.Candidates) : null;
        }

        IEnumerable<CompletionCandidate>? candidates = context.Kind switch
        {
            XscnCursorKind.TagName => new TagCompletions(this, context).Candidates(),
            XscnCursorKind.AttributeName or XscnCursorKind.BetweenAttributes => new AttributeCompletions(this, context).Candidates(),
            _ => null,
        };
        return candidates is null ? null : List(context.Replace, candidates);
    }

    /// <summary>The context at an offset of the text.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The context.</returns>
    public XscnCursorContext ContextAt(int offset) => XscnParser.ContextAt(Text, offset, Metadata);

    /// <summary>What an element's tag makes it.</summary>
    /// <param name="element">A context on or in the element.</param>
    /// <returns>Its shape.</returns>
    public ElementShape ShapeOf(XscnCursorContext element) =>
        element.ElementPrefix switch
        {
            null when element.ElementName is null => ElementShape.Unknown,
            null => element.ElementName.Contains('.', StringComparison.Ordinal) ? ElementShape.PropertyElement : ElementShape.ClassElement,
            { } prefix when prefix == FormatPrefix => ElementShape.FormatElement,
            { } prefix => declaredPrefixes.Value.Contains(prefix) ? ElementShape.SceneTag : ElementShape.Unknown,
        };

    /// <summary>A format name as the document writes it: an <c>x:</c> name with the document's format prefix.</summary>
    /// <param name="name">The name, with the conventional <c>x:</c> prefix or none.</param>
    /// <returns>The name, or null for an <c>x:</c> name in a document that binds no format prefix.</returns>
    public string? Prefixed(string name)
    {
        if (!name.StartsWith("x:", StringComparison.Ordinal))
        {
            return name;
        }

        return FormatPrefix is { } prefix ? prefix + name[1..] : null;
    }

    /// <summary>The format's names as keyword candidates, each written with the document's format prefix.</summary>
    /// <param name="names">The names, with the conventional <c>x:</c> prefix or none.</param>
    /// <returns>The candidates.</returns>
    public IEnumerable<CompletionCandidate> Keywords(IEnumerable<string> names) =>
        names.Select(Prefixed).OfType<string>().Select(n => new CompletionCandidate(n, CompletionItemKind.Keyword, null, n));

    /// <summary>The instantiable classes that inherit a class, the class included.</summary>
    /// <param name="baseGodotName">The class's Godot name.</param>
    /// <returns>A class candidate for each, its detail its parent's C# name.</returns>
    public IEnumerable<CompletionCandidate> Classes(string baseGodotName) =>
        Metadata.Classes.Where(c => c.Instantiable && Metadata.Inherits(c, baseGodotName)).Select(ClassCandidate);

    /// <summary>The instantiable classes a class hint takes, its exclusions left out.</summary>
    /// <param name="hint">The class name or hint list.</param>
    /// <returns>A class candidate for each.</returns>
    public IEnumerable<CompletionCandidate> HintClasses(string hint) => NameRules.InstantiableOf(Metadata, hint).Select(ClassCandidate);

    /// <summary>A type's name as a property's detail shows it.</summary>
    /// <param name="variantType">The Variant type.</param>
    /// <param name="hint">The class name or hint list of an Object.</param>
    /// <returns>The type's name.</returns>
    public string TypeName(string variantType, string hint) => NameRules.TypeName(Metadata, variantType, hint);

    /// <summary>The start tags open at an offset, outermost first, each by the tag-name context just after its '&lt;'.</summary>
    /// <param name="offset">The offset; a tag that starts there is not counted.</param>
    /// <returns>The open elements.</returns>
    public List<XscnCursorContext> OpenElements(int offset) => [.. StartTags.OpenAt(Text, offset).Select(lessThan => ContextAt(lessThan + 1))];

    /// <summary>
    /// Whether an element of a chain is a resource: by its class when it is a node or a resource class, else by where it is
    /// (in a property element or <c>x:Resources</c>, in a resource, or the root of a resource file).
    /// </summary>
    /// <param name="chain">The open elements, outermost first.</param>
    /// <param name="index">The element's index in the chain.</param>
    /// <returns>True for a resource.</returns>
    public bool IsResource(IReadOnlyList<XscnCursorContext> chain, int index)
    {
        if (chain[index].Class is { } cls && KnownIsResource(cls) is { } known)
        {
            return known;
        }

        if (index == 0)
        {
            return Kind == XscnDocumentKind.Resource;
        }

        XscnCursorContext parent = chain[index - 1];
        return ShapeOf(parent) switch
        {
            ElementShape.PropertyElement => true,
            ElementShape.FormatElement => parent.ElementName == "Resources",
            ElementShape.ClassElement => IsResource(chain, index - 1),
            _ => false,
        };
    }

    private bool? KnownIsResource(GodotClassInfo cls) =>
        Metadata.Inherits(cls, "Node") ? false
        : Metadata.Inherits(cls, "Resource") ? true
        : null;

    private CompletionCandidate ClassCandidate(GodotClassInfo cls) =>
        new(cls.CSharpName, CompletionItemKind.Class, cls.Parent is { } parent ? NameRules.CSharpClassName(Metadata, parent) : null, cls.CSharpName);

    // Each label once (the first candidate of a label wins), sorted by label; the client filters by what is typed.
    private CompletionList List(TextSpan replace, IEnumerable<CompletionCandidate> candidates)
    {
        LinePosition start = lines.ZeroBased(replace.Start);
        LinePosition end = lines.ZeroBased(replace.End);
        LspRange range = new(new LspPosition(start.Line, start.Column), new LspPosition(end.Line, end.Column));
        HashSet<string> seen = new(StringComparer.Ordinal);
        CompletionItem[] items =
        [
            .. candidates
                .Where(c => seen.Add(c.Label))
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CompletionItem(c.Label, c.Kind, c.Detail, c.Insert == c.Label ? null : c.Insert, new TextEdit(range, c.Insert))),
        ];
        return new CompletionList(false, items);
    }

    // Every prefix the document declares.
    [GeneratedRegex("""xmlns:([^\s=:"']+)\s*=""", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceDeclaration();
}
