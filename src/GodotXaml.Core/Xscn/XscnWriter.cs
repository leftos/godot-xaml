using System.Collections.Frozen;
using System.Text;
using GodotXaml.Core.Metadata;

namespace GodotXaml.Core.Xscn;

/// <summary>
/// Writes a document as canonical <c>.xscn</c> or <c>.xres</c> text: one fixed layout and order, and no property whose
/// value is its class's default, so that every save of the same scene gives the same text.
/// </summary>
public static partial class XscnWriter
{
    private const int MaxLineLength = 100;
    private const string AttributeIndent = "    ";

    // The references an attribute value writes for characters XML would not keep as they are; the quote is added per value.
    private static readonly FrozenDictionary<char, string> References = new Dictionary<char, string>
    {
        ['&'] = "&amp;",
        ['<'] = "&lt;",
        ['\n'] = "&#10;",
        ['\r'] = "&#13;",
        ['\t'] = "&#9;",
    }.ToFrozenDictionary();

    /// <summary>Writes a document in the canonical layout.</summary>
    /// <param name="document">The document, as <see cref="XscnParser.Parse"/> builds it.</param>
    /// <param name="metadata">The metadata of the Godot version the file is written for.</param>
    /// <returns>The text, with LF line ends and a final newline; save it as UTF-8 without a byte order mark.</returns>
    /// <exception cref="ArgumentException">
    /// A property holds an inline resource but has no attribute name to make a property element of (such a resource
    /// belongs in <c>x:Resources</c>); or text holds a character XML 1.0 cannot hold where no escape exists (a name, a key,
    /// a method, a path, a comment), or a lone surrogate anywhere.
    /// </exception>
    public static string Write(XscnDocument document, GodotMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);
        var session = new Session(document, metadata, XscnParser.NamesOf(metadata));
        var text = new StringBuilder();
        foreach (XmlItem item in session.DocumentItems())
        {
            Render(text, item, 0, "the document");
        }

        return text.ToString();
    }

    private static void Render(StringBuilder text, XmlItem item, int depth, string owner)
    {
        string indent = new(' ', depth * 2);
        if (item is XmlComment comment)
        {
            string body = comment.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            CheckXmlText(body, "A comment", owner);
            text.Append(indent).Append("<!--").Append(body).Append("-->\n");
            return;
        }

        var tag = (XmlTag)item;
        AppendStartTag(text, tag, indent);
        if (tag.Content.Count == 0)
        {
            return;
        }

        foreach (XmlItem child in tag.Content)
        {
            Render(text, child, depth + 1, "<" + tag.Name + ">");
        }

        text.Append(indent).Append("</").Append(tag.Name).Append(">\n");
    }

    // One line when it fits in MaxLineLength; else the tag alone, then each attribute on its own line.
    private static void AppendStartTag(StringBuilder text, XmlTag tag, string indent)
    {
        foreach (XmlAttr attribute in tag.Attributes)
        {
            CheckXmlText(attribute.Value, $"The {attribute.Name} attribute", "<" + tag.Name + ">");
        }

        string close = tag.Content.Count == 0 ? " />" : ">";
        List<string> attributes = [.. tag.Attributes.Select(FormatAttribute)];
        string line = indent + "<" + tag.Name + string.Concat(attributes.Select(a => " " + a)) + close;
        if (line.Length <= MaxLineLength || attributes.Count == 0)
        {
            text.Append(line).Append('\n');
            return;
        }

        text.Append(indent).Append('<').Append(tag.Name).Append('\n');
        for (int i = 0; i < attributes.Count; i++)
        {
            text.Append(indent).Append(AttributeIndent).Append(attributes[i]).Append(i == attributes.Count - 1 ? close : "").Append('\n');
        }
    }

    // Values reach here with every character XML cannot hold already escaped; what is left is plain text with no escape.
    private static void CheckXmlText(string text, string what, string element)
    {
        int index = FirstForbidden(text);
        if (index >= 0)
        {
            throw new ArgumentException($"{what} of {element} holds U+{(int)text[index]:X4}, which XML 1.0 cannot hold.");
        }
    }

    // XML 1.0 production Char: a control character other than tab, LF and CR, U+FFFE, U+FFFF or a lone surrogate is not one.
    internal static int FirstForbidden(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (IsForbidden(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    internal static bool IsForbidden(char c) => char.IsSurrogate(c) || c is (< ' ' and not ('\t' or '\n' or '\r')) or '\uFFFE' or '\uFFFF';

    // The value in '"', or in '\'' when it holds '"' and no '\''; only the quote in use is escaped.
    private static string FormatAttribute(XmlAttr attribute)
    {
        char quote =
            attribute.Value.Contains('"', StringComparison.Ordinal) && !attribute.Value.Contains('\'', StringComparison.Ordinal) ? '\'' : '"';
        var text = new StringBuilder(attribute.Name.Length + attribute.Value.Length + 3);
        text.Append(attribute.Name).Append('=').Append(quote);
        foreach (char c in attribute.Value)
        {
            AppendEscaped(text, c, quote);
        }

        return text.Append(quote).ToString();
    }

    private static void AppendEscaped(StringBuilder text, char c, char quote)
    {
        if (c == quote)
        {
            text.Append(quote == '"' ? "&quot;" : "&apos;");
        }
        else if (References.TryGetValue(c, out string? reference))
        {
            text.Append(reference);
        }
        else
        {
            text.Append(c);
        }
    }

    /// <summary>A piece of the output: an element or a comment.</summary>
    private abstract record XmlItem;

    /// <summary>A comment, its text verbatim.</summary>
    private sealed record XmlComment(string Text) : XmlItem;

    /// <summary>An attribute, its value before escaping.</summary>
    private sealed record XmlAttr(string Name, string Value);

    /// <summary>An element: self-closing when it has no content.</summary>
    private sealed record XmlTag(string Name, IReadOnlyList<XmlAttr> Attributes, IReadOnlyList<XmlItem> Content) : XmlItem;
}
