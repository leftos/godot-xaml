using System.Collections.Immutable;
using GodotXaml.Core.Metadata;
using Microsoft.Language.Xml;
using TextSpan = GodotXaml.Core.Diagnostics.TextSpan;

namespace GodotXaml.Core.Xscn;

/// <summary>What an offset of a document's text is on, for completion and hover.</summary>
public enum XscnCursorKind
{
    /// <summary>Nothing: outside every element, before or after the root.</summary>
    None,

    /// <summary>Inside a comment.</summary>
    Comment,

    /// <summary>In an element's content, between its child elements.</summary>
    Content,

    /// <summary>On a start tag's name, or right after its <c>&lt;</c>.</summary>
    TagName,

    /// <summary>On an attribute's name.</summary>
    AttributeName,

    /// <summary>On an attribute's value, between its quotes.</summary>
    AttributeValue,

    /// <summary>In a start tag, after its name, on no attribute's name or value.</summary>
    BetweenAttributes,

    /// <summary>In an end tag.</summary>
    EndTag,
}

/// <summary>An element open around the cursor's element, as its tag writes it.</summary>
/// <param name="Prefix">The element's namespace prefix as written; null for a Godot class tag.</param>
/// <param name="LocalName">The element's local name as written (<c>Button</c>, <c>Button.Theme</c>).</param>
/// <param name="Class">
/// The element's Godot class, resolved as <see cref="XscnCursorContext.Class"/> is for it; null when the metadata does not
/// resolve it.
/// </param>
public sealed record XscnOpenElement(string? Prefix, string LocalName, GodotClassInfo? Class);

/// <summary>What an offset of a document's text is on, and the element around it.</summary>
/// <param name="Kind">What the offset is on.</param>
/// <param name="ElementPrefix">
/// The element's namespace prefix as written: null for a Godot class tag, <c>x</c> for a format element, or a scene
/// prefix; null when there is no element.
/// </param>
/// <param name="ElementName">
/// The element's local name as written (<c>Button</c>, <c>Instance</c>, <c>Button.Theme</c> for a property element);
/// null when there is no element or its name is not written yet.
/// </param>
/// <param name="Class">
/// The element's Godot class when the metadata resolves it: a class tag's class, or a property element's owner class;
/// null otherwise.
/// </param>
/// <param name="AttributeName">
/// The attribute's full name as written (<c>MouseFilter</c>, <c>x:Key</c>, <c>ThemeOverrideStyles.panel</c>) when
/// <paramref name="Kind"/> is <see cref="XscnCursorKind.AttributeName"/> or <see cref="XscnCursorKind.AttributeValue"/>;
/// null otherwise.
/// </param>
/// <param name="Replace">
/// The token a completion replaces: the tag name, the end tag's name, the attribute name, or the value's text between the
/// quotes, around the offset; empty at the offset when there is none.
/// </param>
/// <param name="Typed">The text of <paramref name="Replace"/> from its start to the offset.</param>
public sealed record XscnCursorContext(
    XscnCursorKind Kind,
    string? ElementPrefix,
    string? ElementName,
    GodotClassInfo? Class,
    string? AttributeName,
    TextSpan Replace,
    string Typed
)
{
    /// <summary>
    /// The elements open around the cursor's element, outermost first, the cursor's own element excluded. For a tag name
    /// typed in text the enclosing element is the last ancestor; an element whose name is not written yet is left out.
    /// </summary>
    public ImmutableArray<XscnOpenElement> Ancestors { get; init; } = [];
}

/// <summary>Finds what an offset is on, over the XML tree of a possibly broken text.</summary>
/// <param name="text">The document's text.</param>
/// <param name="offset">The offset, from 0 to the text's length.</param>
/// <param name="metadata">The metadata a class tag is resolved with.</param>
internal sealed class XscnContextReader(string text, int offset, GodotMetadata metadata)
{
    /// <summary>Reads the context at the offset.</summary>
    /// <returns>The context.</returns>
    public XscnCursorContext Read()
    {
        XmlDocumentSyntax document = Parser.ParseText(text);
        List<SyntaxNode> nodes = Descendants(document);
        if (nodes.OfType<XmlCommentSyntax>().FirstOrDefault(InComment) is { } comment)
        {
            return Context(XscnCursorKind.Comment, comment.ParentElement, null, Empty);
        }

        // Elements nest, so the innermost element around the offset is the one that starts last.
        IXmlElementSyntax? element = nodes.OfType<IXmlElementSyntax>().Where(Contains).MaxBy(e => e.AsNode.SpanStart);
        return element is null ? InText(null) : InElement(element);
    }

    private TextSpan Empty => new(offset, 0);

    private static List<SyntaxNode> Descendants(SyntaxNode root)
    {
        List<SyntaxNode> found = [];
        Stack<SyntaxNode> pending = new([root]);
        while (pending.TryPop(out SyntaxNode? node))
        {
            found.Add(node);
            foreach (SyntaxNode child in node.ChildNodes)
            {
                pending.Push(child);
            }
        }

        return found;
    }

    private static int EndOf(SyntaxNode node) => node.SpanStart + node.Width;

    private static bool IsWritten(SyntaxNode? token) => token is { Width: > 0 };

    // An unclosed comment or element runs on through the whitespace after it.
    private bool InComment(XmlCommentSyntax comment) =>
        comment.SpanStart < offset && (IsWritten(comment.EndComment) ? offset < EndOf(comment) : offset <= comment.End);

    private bool Contains(IXmlElementSyntax element)
    {
        SyntaxNode node = element.AsNode;
        SyntaxNode? closer = element switch
        {
            XmlElementSyntax full => full.EndTag?.GreaterThanToken,
            XmlEmptyElementSyntax empty => empty.SlashGreaterThanToken,
            _ => null,
        };
        return node.SpanStart < offset && (IsWritten(closer) ? offset < EndOf(node) : offset <= node.End);
    }

    private XscnCursorContext InElement(IXmlElementSyntax element)
    {
        if (offset <= StartTagLimit(element))
        {
            return InStartTag(element);
        }

        return
            element is XmlElementSyntax { EndTag: { } endTag } && IsWritten(endTag.LessThanSlashToken) && offset >= EndOf(endTag.LessThanSlashToken)
            ? InEndTag(element, endTag)
            : InText(element);
    }

    // Where the start tag ends: at its '>' or '/>', or, without one, past the whitespace after it.
    private static int StartTagLimit(IXmlElementSyntax element)
    {
        (SyntaxNode? closer, SyntaxNode startTag) = element switch
        {
            XmlElementSyntax full => (full.StartTag?.GreaterThanToken, (SyntaxNode?)full.StartTag ?? full),
            _ => (((XmlEmptyElementSyntax)element).SlashGreaterThanToken, element.AsNode),
        };
        return IsWritten(closer) ? closer!.SpanStart : startTag.End;
    }

    private XscnCursorContext InEndTag(IXmlElementSyntax element, XmlElementEndTagSyntax endTag)
    {
        TextSpan name = endTag.NameNode is { } endName ? new TextSpan(endName.SpanStart, endName.Width) : Empty;
        return Context(XscnCursorKind.EndTag, element, null, offset <= name.End ? name : Empty);
    }

    private XscnCursorContext InStartTag(IXmlElementSyntax element)
    {
        TextSpan name = element.NameNode is { } written ? new TextSpan(written.SpanStart, written.Width) : Empty;
        if (offset <= name.End)
        {
            return Context(XscnCursorKind.TagName, element, null, name);
        }

        return element.AttributesNode.OfType<XmlAttributeSyntax>().Select(a => OnAttribute(element, a)).FirstOrDefault(c => c is not null)
            ?? TypedAttribute(element);
    }

    // A word the XML tree did not make an attribute of is a name being typed.
    private XscnCursorContext TypedAttribute(IXmlElementSyntax element)
    {
        TextSpan word = WordAround();
        return word.Start < offset && word.Start > 0 && char.IsWhiteSpace(text[word.Start - 1])
            ? Context(XscnCursorKind.AttributeName, element, text.Substring(word.Start, word.Length), word)
            : Context(XscnCursorKind.BetweenAttributes, element, null, Empty);
    }

    private XscnCursorContext? OnAttribute(IXmlElementSyntax element, XmlAttributeSyntax attribute)
    {
        string? fullName = attribute.NameNode?.FullName;
        if (attribute.NameNode is { } name && name.SpanStart < offset && offset <= EndOf(name))
        {
            return Context(XscnCursorKind.AttributeName, element, fullName, new TextSpan(name.SpanStart, name.Width));
        }

        return ValueSpan(attribute) is { } value && value.Start <= offset && offset <= value.End
            ? Context(XscnCursorKind.AttributeValue, element, fullName, value)
            : null;
    }

    // The value's text between the quotes; an unterminated value runs to the end of its text.
    private static TextSpan? ValueSpan(XmlAttributeSyntax attribute)
    {
        if (attribute.ValueNode is not { } value || !IsWritten(value.StartQuoteToken))
        {
            return null;
        }

        int start = EndOf(value.StartQuoteToken);
        int end = IsWritten(value.EndQuoteToken) ? value.EndQuoteToken.SpanStart : Math.Max(start, EndOf(value));
        return new TextSpan(start, end - start);
    }

    // Outside any tag: a name right after a '<' the XML tree did not make an element of is a tag name being typed.
    private XscnCursorContext InText(IXmlElementSyntax? element)
    {
        TextSpan word = WordAround();
        if (word.Start > 0 && text[word.Start - 1] == '<')
        {
            string written = text.Substring(word.Start, word.Length);
            int colon = written.IndexOf(':', StringComparison.Ordinal);
            string? prefix = colon < 0 ? null : written[..colon];
            string local = written[(colon + 1)..];
            XscnCursorContext context = Make(XscnCursorKind.TagName, prefix, local.Length == 0 ? null : local, null, word);
            return context with { Ancestors = AncestorsOf(element, elementIsAncestor: true) };
        }

        return element is null ? Make(XscnCursorKind.None, null, null, null, Empty) : Context(XscnCursorKind.Content, element, null, Empty);
    }

    // The run of XML name characters around the offset.
    private TextSpan WordAround()
    {
        int start = offset;
        while (start > 0 && IsNameChar(text[start - 1]))
        {
            start--;
        }

        int end = offset;
        while (end < text.Length && IsNameChar(text[end]))
        {
            end++;
        }

        return new TextSpan(start, end - start);
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';

    private XscnCursorContext Context(XscnCursorKind kind, IXmlElementSyntax? element, string? attributeName, TextSpan replace)
    {
        XmlNameSyntax? name = element?.NameNode;
        XscnCursorContext context = Make(kind, Written(name?.Prefix), Written(name?.LocalName), attributeName, replace);
        return context with { Ancestors = AncestorsOf(element, elementIsAncestor: false) };
    }

    private static string? Written(string? name) => string.IsNullOrEmpty(name) ? null : name;

    private XscnCursorContext Make(XscnCursorKind kind, string? prefix, string? local, string? attributeName, TextSpan replace) =>
        new(kind, prefix, local, ClassOf(prefix, local), attributeName, replace, text[replace.Start..Math.Max(replace.Start, offset)]);

    // The elements open around the element, outermost first; one whose name is not written yet is left out.
    private ImmutableArray<XscnOpenElement> AncestorsOf(IXmlElementSyntax? element, bool elementIsAncestor)
    {
        ImmutableArray<XscnOpenElement>.Builder found = ImmutableArray.CreateBuilder<XscnOpenElement>();
        IXmlElementSyntax? current = elementIsAncestor ? element : element?.AsNode.ParentElement;
        while (current is not null)
        {
            if (OpenElement(current) is { } open)
            {
                found.Add(open);
            }

            current = current.AsNode.ParentElement;
        }

        found.Reverse();
        return found.ToImmutable();
    }

    // An ancestor as its tag writes it; null for an element whose name the tree has not written yet.
    private XscnOpenElement? OpenElement(IXmlElementSyntax element)
    {
        string? prefix = Written(element.NameNode?.Prefix);
        string? local = Written(element.NameNode?.LocalName);
        return local is null ? null : new XscnOpenElement(prefix, local, ClassOf(prefix, local));
    }

    // A class tag names its class by its C# name, as the tree reader resolves it; a property element names its owner's.
    private GodotClassInfo? ClassOf(string? prefix, string? local)
    {
        if (prefix is not null || local is null)
        {
            return null;
        }

        int dot = local.IndexOf('.', StringComparison.Ordinal);
        string tag = dot < 0 ? local : local[..dot];
        return tag.Length == 0 ? null : metadata.FindClassByCSharpName(tag);
    }
}
