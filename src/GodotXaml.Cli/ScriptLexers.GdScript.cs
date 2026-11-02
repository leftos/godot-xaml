using System.Globalization;
using System.Text;

namespace GodotXaml.Cli;

/// <summary>
/// One small lexer per file kind, each yielding the string literals of a text and skipping its comments. None throws:
/// text that cannot be lexed ends that file's literals at that point. This part holds the GDScript lexer, after Godot's
/// <c>GDScriptTokenizerText::string</c>, and the escapes the lexers share.
/// </summary>
internal static partial class ScriptLexers
{
    /// <summary>The fewest quotes that make a multi-line or raw delimiter.</summary>
    internal const int RawQuoteCount = 3;

    private const int MaxCodePoint = 0x10FFFF;

    // Decodes the escape whose backslash is at an offset, and gives the offset after it.
    private delegate int EscapeDecoder(string text, int at, StringBuilder into);

    /// <summary>The string literals of a <c>.gd</c> text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The literals, in offset order.</returns>
    internal static List<ScriptLiteral> GdScript(string text)
    {
        List<ScriptLiteral> found = [];
        int at = 0;
        while (at < text.Length)
        {
            char current = text[at];
            if (current == '#')
            {
                at = GodotLineEnd(text, at);
            }
            else if (current is '"' or '\'')
            {
                ScriptLiteral? literal = GdScriptString(text, at);
                if (literal is null)
                {
                    return found;
                }
                found.Add(literal);
                at = literal.End + literal.QuoteCount;
            }
            else
            {
                at++;
            }
        }
        return found;
    }

    private static ScriptLiteral? GdScriptString(string text, int quote)
    {
        int count = Run(text, quote, text[quote]) >= RawQuoteCount ? RawQuoteCount : 1;
        return HasRawPrefix(text, quote) ? GdScriptRaw(text, quote, count) : Escaped(text, quote, count, GdScriptEscape);
    }

    // A backslash before a line feed drops both. Before a carriage return and line feed the tokenizer drops only the
    // carriage return, so the line feed stays in the text.
    private static int GdScriptEscape(string text, int at, StringBuilder into)
    {
        char next = text[at + 1];
        bool newline = next == '\n' || (next == '\r' && at + 2 < text.Length && text[at + 2] == '\n');
        return newline ? at + 2 : GodotEscape(text, at, into);
    }

    // An 'r' opens a raw string unless it ends an identifier. A backslash before the quote or another backslash stays
    // in the text and still does not close the string.
    private static ScriptLiteral? GdScriptRaw(string text, int quote, int count)
    {
        int start = quote + count;
        int at = start;
        while (at < text.Length)
        {
            if (text[at] == '\\')
            {
                at += at + 1 < text.Length && (text[at + 1] == text[quote] || text[at + 1] == '\\') ? 2 : 1;
            }
            else if (Run(text, at, text[quote]) >= count)
            {
                return new ScriptLiteral(quote, start, at, text[start..at], LiteralForm.Raw, count);
            }
            else
            {
                at++;
            }
        }
        return null;
    }

    private static bool HasRawPrefix(string text, int quote)
    {
        if (quote == 0 || text[quote - 1] != 'r')
        {
            return false;
        }
        return quote == 1 || !(char.IsLetterOrDigit(text[quote - 2]) || text[quote - 2] == '_');
    }

    // A string in which a backslash escapes the next character, up to its closing quotes; null when it does not close.
    private static ScriptLiteral? Escaped(string text, int quote, int count, EscapeDecoder decode)
    {
        int start = quote + count;
        StringBuilder content = new();
        int at = start;
        while (at < text.Length)
        {
            if (text[at] == '\\')
            {
                if (at + 1 == text.Length)
                {
                    return null;
                }
                at = decode(text, at, content);
            }
            else if (Run(text, at, text[quote]) >= count)
            {
                return new ScriptLiteral(quote, start, at, content.ToString(), LiteralForm.Regular, count);
            }
            else
            {
                content.Append(text[at]);
                at++;
            }
        }
        return null;
    }

    private static int GodotEscape(string text, int at, StringBuilder into) =>
        text[at + 1] switch
        {
            'u' => AppendHex(text, at, 4, into),
            'U' => AppendHex(text, at, 6, into),
            _ => AppendSimple(text, at, SimpleEscape(text[at + 1]), into),
        };

    // An escape neither language treats differently; null for any other.
    private static char? SimpleEscape(char code) =>
        code switch
        {
            '\'' => '\'',
            '"' => '"',
            '\\' => '\\',
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            _ => null,
        };

    private static int AppendSimple(string text, int at, char? decoded, StringBuilder into)
    {
        if (decoded is null)
        {
            into.Append(text, at, 2);
        }
        else
        {
            into.Append(decoded.Value);
        }
        return at + 2;
    }

    // A hexadecimal escape of a fixed length. One with too few digits, or past the last code point, stays as its two
    // characters. A surrogate is appended alone, so two escapes that spell a pair make the one character.
    private static int AppendHex(string text, int at, int digits, StringBuilder into)
    {
        int first = at + 2;
        bool complete = first + digits <= text.Length;
        if (!complete || !uint.TryParse(text.AsSpan(first, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            into.Append(text, at, 2);
            return first;
        }
        if (value > MaxCodePoint)
        {
            into.Append(text, at, 2);
            return first;
        }
        if (value <= char.MaxValue)
        {
            into.Append((char)value);
        }
        else
        {
            into.Append(char.ConvertFromUtf32((int)value));
        }
        return first + digits;
    }

    // How many of a character sit in a row from an offset.
    private static int Run(string text, int at, char character)
    {
        int end = at;
        while (end < text.Length && text[end] == character)
        {
            end++;
        }
        return end - at;
    }

    // Godot's comments, here and in project.godot, end at a line feed.
    private static int GodotLineEnd(string text, int at)
    {
        int end = text.IndexOf('\n', at);
        return end < 0 ? text.Length : end;
    }
}
