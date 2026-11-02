using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace GodotXaml.Core.Xscn;

/// <summary>Decodes the text of an XML attribute value, which the XML parser hands over as written.</summary>
internal static class XmlText
{
    // XML 1.0 section 4.6, the predefined entities.
    private static readonly FrozenDictionary<string, string> Named = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// An attribute value's text as XML 1.0 section 3.3.3 normalizes it: each reference replaced by its character, and each
    /// literal line break or tab by a space, a carriage return and line feed together by one. An <c>&amp;</c> that starts no
    /// reference XML defines is kept as written; the XML parser reports it.
    /// </summary>
    /// <param name="raw">The text between the quotes, as written.</param>
    /// <returns>The decoded text.</returns>
    public static string DecodeAttribute(string raw)
    {
        var text = new StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            i = AppendNext(raw, i, text);
        }

        return text.ToString();
    }

    private static int AppendNext(string raw, int i, StringBuilder text)
    {
        char c = raw[i];
        if (c == '&')
        {
            return AppendReference(raw, i, text);
        }

        if (c == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
        {
            text.Append(' ');
            return i + 2;
        }

        text.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        return i + 1;
    }

    private static int AppendReference(string raw, int i, StringBuilder text)
    {
        int end = raw.IndexOf(';', i);
        string? decoded = end < 0 ? null : Reference(raw[(i + 1)..end]);
        if (decoded is null)
        {
            text.Append('&');
            return i + 1;
        }

        text.Append(decoded);
        return end + 1;
    }

    private static string? Reference(string name) =>
        name.StartsWith("#x", StringComparison.Ordinal) ? CharacterReference(name[2..], NumberStyles.AllowHexSpecifier)
        : name.StartsWith('#') ? CharacterReference(name[1..], NumberStyles.None)
        : Named.GetValueOrDefault(name);

    private static string? CharacterReference(string digits, NumberStyles style) =>
        digits.Length > 0 && int.TryParse(digits, style, CultureInfo.InvariantCulture, out int code) && IsXmlChar(code)
            ? char.ConvertFromUtf32(code)
            : null;

    // XML 1.0 production Char: the characters a reference may name.
    private static bool IsXmlChar(int code) =>
        code is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF);
}
