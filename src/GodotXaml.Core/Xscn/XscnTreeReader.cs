using System.Collections;
using System.Collections.Immutable;
using GodotXaml.Core.Diagnostics;
using GodotXaml.Core.Metadata;
using Microsoft.Language.Xml;
using TextSpan = GodotXaml.Core.Diagnostics.TextSpan;

namespace GodotXaml.Core.Xscn;

/// <summary>Reads one document's XML tree into the model, collecting a diagnostic for every problem on the way.</summary>
/// <param name="source">The document's text.</param>
/// <param name="metadata">The metadata the document is read with.</param>
/// <param name="names">The attribute names of that metadata.</param>
internal sealed partial class XscnTreeReader(string source, GodotMetadata metadata, XscnPropertyNames names)
{
    private const string ResourcePrefix = "res://";

    // A document deeper than this would exhaust the stack of every program that reads it, which .NET cannot catch; no
    // Godot scene needs the depth.
    private const int MaxElementDepth = 512;

    private readonly List<XscnDiagnostic> diagnostics = [];
    private readonly Dictionary<string, string> sceneFolders = new(StringComparer.Ordinal);
    private readonly List<XscnResource> resources = [];
    private readonly HashSet<string> resourceKeys = new(StringComparer.Ordinal);
    private readonly List<string> uidsLeading = [];
    private readonly List<string> uidsTrailing = [];
    private readonly List<string> resourcesLeading = [];
    private readonly List<string> resourcesTrailing = [];
    private ImmutableArray<XscnUid> uids = [];
    private string? formatPrefix;
    private bool uidTableSeen;
    private bool resourceTableSeen;

    // The depth of the element being read: the root is 1, and every element inside another is one deeper.
    private int elementDepth = 1;

    private enum TagKind
    {
        Property,
        Class,
        Format,
        Scene,
        Undeclared,
    }

    /// <summary>The diagnostics found so far, in source order.</summary>
    public ImmutableArray<XscnDiagnostic> Diagnostics => [.. diagnostics.OrderBy(d => d.Span.Start)];

    /// <summary>Reads the document.</summary>
    /// <param name="kind">Whether the root is a node or a resource.</param>
    /// <returns>The document; a stand-in root of unknown type when the text has no root element.</returns>
    public XscnDocument Read(XscnDocumentKind kind)
    {
        XmlDocumentSyntax syntax = Parser.ParseText(source);
        diagnostics.AddRange(XmlSyntaxErrors.Collect(syntax));
        IXmlElementSyntax? root = syntax.RootSyntax;
        if (root is null || FullName(root).Length == 0)
        {
            Report(XscnCodes.MissingRoot, "The document has no root element.", new TextSpan(0, 0));
            return new XscnDocument(kind, [], [], [], StandInRoot(kind));
        }

        ImmutableArray<XscnNamespace> namespaces = ReadNamespaces(root);
        uids = root.Elements.FirstOrDefault(e => IsFormat(e, "Uids")) is { } table ? [.. ReadUidTable(table)] : [];
        XscnElement element = kind == XscnDocumentKind.Scene ? ReadRootNode(root) : (XscnElement)ReadRootResource(root);
        return new XscnDocument(kind, namespaces, uids, [.. resources], WithDocumentComments(element, syntax))
        {
            UidsLeadingComments = CommentList(uidsLeading),
            UidsTrailingComments = CommentList(uidsTrailing),
            ResourcesLeadingComments = CommentList(resourcesLeading),
            ResourcesTrailingComments = CommentList(resourcesTrailing),
        };
    }

    private static XscnElement StandInRoot(XscnDocumentKind kind) =>
        kind == XscnDocumentKind.Scene
            ? new XscnNode { Type = new XscnUnknownType(""), Name = "" }
            : new XscnResource { Type = new XscnUnknownType("") };

    private static TextSpan SpanOf(SyntaxNode node) => new(node.SpanStart, node.Width);

    private static TextSpan NameSpan(IXmlElementSyntax element) => element.NameNode is { } name ? SpanOf(name) : SpanOf(element.AsNode);

    private static string FullName(IXmlElementSyntax element) => element.NameNode?.FullName ?? "";

    private static bool IsBlank(SyntaxNode item) => item is XmlTextSyntax text && string.IsNullOrWhiteSpace(text.Value);

    private void Report(string code, string message, TextSpan span) => diagnostics.Add(new XscnDiagnostic(code, XscnSeverity.Error, message, span));

    // Comments before the root lead it; comments after it trail it, after the ones inside it.
    private XscnElement WithDocumentComments(XscnElement element, XmlDocumentSyntax syntax) =>
        element with
        {
            LeadingComments = CommentList([.. Comments(syntax.PrecedingMisc), .. element.LeadingComments]),
            TrailingComments = CommentList([.. element.TrailingComments, .. Comments(syntax.FollowingMisc)]),
        };

    private IEnumerable<string> Comments(IEnumerable misc) => misc.OfType<XmlCommentSyntax>().Select(CommentText);

    // The text between <!-- and -->, verbatim; an unclosed comment runs to its end.
    private string CommentText(XmlCommentSyntax comment)
    {
        int start = comment.BeginComment.SpanStart + comment.BeginComment.Width;
        int end = comment.EndComment.Width > 0 ? comment.EndComment.SpanStart : comment.SpanStart + comment.Width;
        return source[start..Math.Max(start, end)];
    }

    // A comment goes to the list of those waiting for the next element; anything else but blank text is out of place.
    private void ReadLooseItem(SyntaxNode item, List<string> pending)
    {
        if (item is XmlCommentSyntax comment)
        {
            pending.Add(CommentText(comment));
        }
        else if (!IsBlank(item))
        {
            Report(XscnCodes.UnexpectedText, "Only elements and comments belong here.", SpanOf(item));
        }
    }

    // A repeated attribute is reported with the XML errors; the first of each name is the one read.
    private static IEnumerable<XmlAttributeSyntax> FirstOfEachName(IXmlElementSyntax element)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        return element.Attributes.Where(a => a.NameNode is not { FullName.Length: > 0 } name || seen.Add(name.FullName));
    }

    private ImmutableArray<XscnNamespace> ReadNamespaces(IXmlElementSyntax root)
    {
        List<XscnNamespace> found = [];
        foreach (AttributeText attribute in FirstOfEachName(root).Where(DeclaresNamespace).Select(Attribute))
        {
            if (ReadNamespace(attribute) is { } scene)
            {
                found.Add(scene);
            }
        }

        return [.. found];
    }

    // xmlns:x binds the format; any other prefix binds a res:// folder of scenes. There is no default namespace.
    private XscnNamespace? ReadNamespace(AttributeText attribute)
    {
        if (attribute.Prefix.Length == 0)
        {
            Report(XscnCodes.InvalidNamespace, "A default namespace is not used: Godot classes are tags without a prefix.", attribute.Span);
            return null;
        }

        if (attribute.Value == XscnParser.FormatNamespace)
        {
            if (formatPrefix is { } first)
            {
                Report(
                    XscnCodes.InvalidNamespace,
                    $"The format namespace is already bound to the prefix '{first}'; xmlns:{attribute.LocalName} is not used.",
                    attribute.Span
                );
                return null;
            }

            formatPrefix = attribute.LocalName;
            return null;
        }

        if (attribute.Value.StartsWith(ResourcePrefix, StringComparison.Ordinal) && attribute.Value.EndsWith('/'))
        {
            sceneFolders[attribute.LocalName] = attribute.Value;
            return new XscnNamespace(attribute.LocalName, attribute.Value);
        }

        string expected = $"a res:// folder ending in '/', or the format namespace {XscnParser.FormatNamespace}";
        Report(XscnCodes.InvalidNamespace, $"xmlns:{attribute.LocalName} must name {expected}.", attribute.ValueSpan);
        return null;
    }

    private TagKind KindOf(string prefix, string localName) =>
        localName.Contains('.', StringComparison.Ordinal) ? TagKind.Property
        : prefix.Length == 0 ? TagKind.Class
        : prefix == formatPrefix ? TagKind.Format
        : sceneFolders.ContainsKey(prefix) ? TagKind.Scene
        : TagKind.Undeclared;

    private TagKind KindOf(IXmlElementSyntax element) => KindOf(element.NameNode?.Prefix ?? "", LocalName(element));

    private static string LocalName(IXmlElementSyntax element) => element.NameNode?.LocalName ?? "";

    private bool IsFormat(IXmlElementSyntax element, string localName) => KindOf(element) == TagKind.Format && LocalName(element) == localName;

    // A class tag names a Godot class by its C# name; an unknown one is kept by name with a warning.
    private XscnType ClassTag(IXmlElementSyntax element)
    {
        string name = LocalName(element);
        if (metadata.FindClassByCSharpName(name) is { } cls)
        {
            return new XscnClassType(cls);
        }

        string message = $"No Godot class is named {name}; the element is kept with an unknown type.";
        diagnostics.Add(new XscnDiagnostic(XscnCodes.UnknownClass, XscnSeverity.Warning, message, NameSpan(element)));
        return new XscnUnknownType(name);
    }

    // A scene tag is its namespace's folder and its name, with the extension of the matching uid table path, else .xscn.
    private XscnSceneType SceneTag(IXmlElementSyntax element)
    {
        string stem = sceneFolders[element.NameNode?.Prefix ?? ""] + LocalName(element);
        string? listed = uids.Select(u => u.Path).FirstOrDefault(p => IsStemOf(stem, p));
        return new XscnSceneType(listed ?? stem + ".xscn");
    }

    // Shared with the writer, which checks that a scene tag reads back as its path.
    internal static bool IsStemOf(string stem, string path) =>
        path.Length > stem.Length + 1
        && path.StartsWith(stem, StringComparison.Ordinal)
        && path[stem.Length] == '.'
        && path.IndexOfAny(['/', '.'], stem.Length + 1) < 0;

    private XscnUnknownType UndeclaredTag(IXmlElementSyntax element)
    {
        string prefix = element.NameNode?.Prefix ?? "";
        Report(XscnCodes.UndeclaredPrefix, $"The prefix '{prefix}' is not declared; declare xmlns:{prefix} on the root.", NameSpan(element));
        return new XscnUnknownType(FullName(element));
    }

    private static GodotClassInfo? ClassOf(XscnType type) => (type as XscnClassType)?.Class;
}
