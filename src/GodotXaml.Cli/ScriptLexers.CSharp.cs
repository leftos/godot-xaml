using System.Text;

namespace GodotXaml.Cli;

/// <summary>The C# lexer, after the language's rules for comments, character literals and every string form.</summary>
internal static partial class ScriptLexers
{
    private const int MaxHoleDepth = 64;
    private const string LineEnds = "\n\r\u0085\u2028\u2029";

    /// <summary>The string literals of a <c>.cs</c> text, each literal inside an interpolation hole among them.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The literals, in offset order.</returns>
    internal static List<ScriptLiteral> CSharp(string text)
    {
        CSharpLexer lexer = new(text);
        return lexer.Literals();
    }

    private sealed class CSharpLexer(string text)
    {
        private readonly List<ScriptLiteral> found = [];
        private int at;

        internal List<ScriptLiteral> Literals()
        {
            bool lexing = true;
            while (lexing && at < text.Length)
            {
                lexing = Step(text[at], 0);
            }
            found.Sort((left, right) => left.Quote.CompareTo(right.Quote));
            return found;
        }

        // A hole's code up to its closing brace, or its format clause when one opens. False ends the file's literals.
        private bool HoleCode(Opening open, int depth)
        {
            int braces = 0;
            int groups = 0;
            while (at < text.Length)
            {
                char current = text[at];
                if (current == '}' && braces == 0)
                {
                    return true;
                }
                if (OpensFormatClause(braces, groups))
                {
                    return FormatClause(open);
                }
                braces += Nesting(current, '{', '}');
                groups += Nesting(current, '(', ')') + Nesting(current, '[', ']');
                if (!Step(current, depth))
                {
                    return false;
                }
            }
            return false;
        }

        private static int Nesting(char current, char open, char close)
        {
            if (current == open)
            {
                return 1;
            }
            return current == close ? -1 : 0;
        }

        // Every ':' at the hole's top level, since an alias qualifier there needs parentheses.
        private bool OpensFormatClause(int braces, int groups) => text[at] == ':' && braces == 0 && groups == 0;

        // Nothing in a format clause is code. It ends at the hole's closing braces, where the at offset is left.
        private bool FormatClause(Opening open)
        {
            while (at < text.Length)
            {
                if (Run(text, at, '}') >= open.Dollars)
                {
                    return true;
                }
                at += FormatCharacterLength(open);
            }
            return false;
        }

        private int FormatCharacterLength(Opening open)
        {
            if (open.Raw)
            {
                return 1;
            }
            if (open.Verbatim)
            {
                return text[at] == '"' && at + 1 < text.Length && text[at + 1] == '"' ? 2 : 1;
            }
            return text[at] == '\\' ? 2 : 1;
        }

        private bool Step(char current, int depth)
        {
            switch (current)
            {
                case '/':
                    Comment();
                    return true;
                case '#':
                    Directive();
                    return true;
                case '\'':
                    CharacterLiteral();
                    return true;
                case '"' or '$' or '@':
                    return StringAt(depth);
                default:
                    at++;
                    return true;
            }
        }

        private void Comment()
        {
            char next = at + 1 < text.Length ? text[at + 1] : '\0';
            if (next == '/')
            {
                at = LineEnd(at);
            }
            else if (next == '*')
            {
                int close = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = close < 0 ? text.Length : close + 2;
            }
            else
            {
                at++;
            }
        }

        // A preprocessor line takes any text, an unmatched apostrophe included.
        private void Directive()
        {
            int before = at - 1;
            while (before >= 0 && char.IsWhiteSpace(text[before]) && !LineEnds.Contains(text[before], StringComparison.Ordinal))
            {
                before--;
            }
            bool firstOnLine = before < 0 || LineEnds.Contains(text[before], StringComparison.Ordinal);
            at = firstOnLine ? LineEnd(at) : at + 1;
        }

        // Skipped so that '"' opens no string. An apostrophe with no partner on its line is stepped past alone.
        private void CharacterLiteral()
        {
            int after = at + 1 < text.Length && text[at + 1] == '\\' ? at + 3 : at + 1;
            int end = LineEnd(at);
            int close = after < end ? text.IndexOf('\'', after, end - after) : -1;
            at = close < 0 ? at + 1 : close + 1;
        }

        private int LineEnd(int from)
        {
            int end = text.AsSpan(from).IndexOfAny(LineEnds);
            return end < 0 ? text.Length : from + end;
        }

        // The string whose prefix or opening quote is here; a '$' or '@' with no string after it is stepped past.
        private bool StringAt(int depth)
        {
            int quote = AfterPrefix();
            if (quote == text.Length || text[quote] != '"')
            {
                at = quote;
                return true;
            }
            return StringWithPrefix(text.AsSpan(at, quote - at), quote, depth);
        }

        private int AfterPrefix()
        {
            int quote = at;
            while (quote < text.Length && text[quote] is '$' or '@')
            {
                quote++;
            }
            return quote;
        }

        private bool StringWithPrefix(ReadOnlySpan<char> prefix, int quote, int depth)
        {
            int dollars = prefix.Count('$');
            bool verbatim = prefix.Contains('@');
            int quotes = verbatim ? 1 : Run(text, quote, '"');
            if (dollars > 0)
            {
                return Interpolated(new Opening(quote, quotes >= RawQuoteCount ? quotes : 1, dollars, verbatim), depth);
            }
            return Finish(verbatim ? Verbatim(quote) : Plain(quote, quotes));
        }

        private ScriptLiteral? Plain(int quote, int quotes) => quotes >= RawQuoteCount ? Raw(quote, quotes) : Escaped(text, quote, 1, CSharpEscape);

        private bool Finish(ScriptLiteral? literal)
        {
            if (literal is null)
            {
                return false;
            }
            found.Add(literal);
            at = literal.End + literal.QuoteCount;
            return true;
        }

        private ScriptLiteral? Verbatim(int quote)
        {
            int start = quote + 1;
            StringBuilder content = new();
            int cursor = start;
            while (cursor < text.Length)
            {
                if (text[cursor] != '"')
                {
                    content.Append(text[cursor]);
                    cursor++;
                }
                else if (cursor + 1 < text.Length && text[cursor + 1] == '"')
                {
                    content.Append('"');
                    cursor += 2;
                }
                else
                {
                    return new ScriptLiteral(quote, start, cursor, content.ToString(), LiteralForm.Verbatim, 1);
                }
            }
            return null;
        }

        private ScriptLiteral? Raw(int quote, int quotes)
        {
            int start = quote + quotes;
            int close = text.IndexOf(new string('"', quotes), start, StringComparison.Ordinal);
            return close < 0 ? null : new ScriptLiteral(quote, start, close, text[start..close], LiteralForm.Raw, quotes);
        }

        private static int CSharpEscape(string text, int at, StringBuilder into) =>
            text[at + 1] switch
            {
                'u' => AppendHex(text, at, 4, into),
                'U' => AppendHex(text, at, 8, into),
                'x' => AppendHex(text, at, Math.Max(HexRun(text, at + 2), 1), into),
                '0' => AppendSimple(text, at, '\0', into),
                'e' => AppendSimple(text, at, '\x1B', into),
                _ => AppendSimple(text, at, SimpleEscape(text[at + 1]), into),
            };

        // The length of a \x escape, which takes from one to four digits.
        private static int HexRun(string text, int first)
        {
            int count = 0;
            while (count < 4 && first + count < text.Length && char.IsAsciiHexDigit(text[first + count]))
            {
                count++;
            }
            return count;
        }

        private bool Interpolated(Opening open, int depth)
        {
            StringBuilder content = new();
            at = open.Start;
            while (at < text.Length)
            {
                if (Closes(open))
                {
                    found.Add(new ScriptLiteral(open.Quote, open.Start, at, content.ToString(), LiteralForm.Interpolated, open.Quotes));
                    at += open.Quotes;
                    return true;
                }
                if (!InterpolatedStep(open, content, depth))
                {
                    return false;
                }
            }
            return false;
        }

        private bool Closes(Opening open)
        {
            if (open.Raw || !open.Verbatim)
            {
                return Run(text, at, '"') >= open.Quotes;
            }
            return text[at] == '"' && (at + 1 == text.Length || text[at + 1] != '"');
        }

        private bool InterpolatedStep(Opening open, StringBuilder content, int depth)
        {
            switch (text[at])
            {
                case '{':
                    return OpenBrace(open, content, depth);
                case '}':
                    CloseBrace(open, content);
                    return true;
                case '\\' when !open.Raw && !open.Verbatim:
                    return InterpolatedEscape(content);
                case '"' when open.Verbatim:
                    content.Append('"');
                    at += 2;
                    return true;
                default:
                    content.Append(text[at]);
                    at++;
                    return true;
            }
        }

        private bool InterpolatedEscape(StringBuilder content)
        {
            if (at + 1 == text.Length)
            {
                return false;
            }
            at = CSharpEscape(text, at, content);
            return true;
        }

        // With one '$', a doubled brace is one brace of text. With more, a shorter run of braces is text, and a run long
        // enough opens a hole with its last braces.
        private bool OpenBrace(Opening open, StringBuilder content, int depth)
        {
            int run = Run(text, at, '{');
            if (open.Dollars == 1 && run >= 2)
            {
                content.Append('{');
                at += 2;
                return true;
            }
            if (run < open.Dollars)
            {
                content.Append('{', run);
                at += run;
                return true;
            }
            content.Append('{', run - open.Dollars);
            at += run;
            return Hole(open, content, depth);
        }

        private void CloseBrace(Opening open, StringBuilder content)
        {
            bool doubled = open.Dollars == 1 && at + 1 < text.Length && text[at + 1] == '}';
            content.Append('}');
            at += doubled ? 2 : 1;
        }

        // A hole is one '\0' of the literal's text, and is lexed as C# up to its format clause or closing braces.
        private bool Hole(Opening open, StringBuilder content, int depth)
        {
            if (depth == MaxHoleDepth || !HoleCode(open, depth + 1))
            {
                return false;
            }
            content.Append('\0');
            at += Math.Min(Run(text, at, '}'), open.Dollars);
            return true;
        }

        // The delimiter of an interpolated literal: where its quote sits, how many quotes and '$' it has, and whether '@' marks it.
        private readonly record struct Opening(int Quote, int Quotes, int Dollars, bool Verbatim)
        {
            internal int Start => Quote + Quotes;

            internal bool Raw => Quotes >= RawQuoteCount;
        }
    }
}
