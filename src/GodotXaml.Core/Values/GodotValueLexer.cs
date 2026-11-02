namespace GodotXaml.Core.Values;

/// <summary>The token types of Godot's value grammar (variant_parser.h, VariantParser::TokenType).</summary>
internal enum GodotTokenType
{
    CurlyBracketOpen,
    CurlyBracketClose,
    BracketOpen,
    BracketClose,
    ParenthesisOpen,
    ParenthesisClose,
    Identifier,
    String,
    StringName,
    Number,
    Color,
    Colon,
    Comma,
    Period,
    Equal,
    Eof,
}

/// <summary>A token: its type, its text and, for a number or a colour, its value.</summary>
/// <param name="Type">The token type.</param>
/// <param name="Text">An identifier's name, a string's unescaped text, or a number's source text; empty otherwise.</param>
/// <param name="Value">A number's <see cref="GodotInt"/> or <see cref="GodotFloat"/>, or a colour's <see cref="GodotReal"/>.</param>
/// <param name="Line">The 1-based line the token starts on.</param>
/// <param name="Column">The 1-based column the token starts at.</param>
internal readonly record struct GodotToken(GodotTokenType Type, string Text, GodotValue? Value, int Line, int Column);

/// <summary>Godot's value tokenizer, a port of VariantParser::get_token (core/variant/variant_parser.cpp:162-520).</summary>
/// <param name="text">The text.</param>
/// <param name="position">The index to start at.</param>
/// <param name="line">The 1-based line of that index.</param>
internal sealed class GodotValueLexer(string text, int position, int line)
{
    // variant_parser.cpp:187-241, the one-character tokens.
    private static readonly Dictionary<char, GodotTokenType> Punctuation = new()
    {
        ['{'] = GodotTokenType.CurlyBracketOpen,
        ['}'] = GodotTokenType.CurlyBracketClose,
        ['['] = GodotTokenType.BracketOpen,
        [']'] = GodotTokenType.BracketClose,
        ['('] = GodotTokenType.ParenthesisOpen,
        [')'] = GodotTokenType.ParenthesisClose,
        [':'] = GodotTokenType.Colon,
        [','] = GodotTokenType.Comma,
        ['.'] = GodotTokenType.Period,
        ['='] = GodotTokenType.Equal,
    };

    // variant_parser.cpp:215-415, the characters that open a comment, a colour or a string; null for a comment.
    private static readonly Dictionary<char, Func<GodotValueLexer, int, GodotToken?>> Openers = new()
    {
        [';'] = (lexer, _) => lexer.SkipComment(),
        ['#'] = (lexer, start) => lexer.LexColor(start),
        ['&'] = (lexer, start) => lexer.LexStringName(start),
        ['@'] = (lexer, start) => lexer.LexStringName(start),
        ['"'] = (lexer, start) => lexer.LexString(GodotTokenType.String, start, start + 1),
    };

    // variant_parser.cpp:300-314, the escapes with a name.
    private static readonly Dictionary<char, int> NamedEscapes = new()
    {
        ['b'] = 8,
        ['t'] = 9,
        ['n'] = 10,
        ['f'] = 12,
        ['r'] = 13,
    };

    // color.cpp:341-359: the hex digits per component for each length Color::html takes.
    private static readonly Dictionary<int, int> HtmlDigitWidths = new()
    {
        [3] = 1,
        [4] = 1,
        [6] = 2,
        [8] = 2,
    };

    private readonly string _text = text;

    // The index the current line starts at, kept in step with Line so that a column is its distance from there.
    private int _lineStart = position > 0 ? text.LastIndexOf('\n', position - 1) + 1 : 0;

    /// <summary>The index of the next character to read.</summary>
    public int Position { get; private set; } = position;

    /// <summary>The 1-based line of <see cref="Position"/>.</summary>
    public int Line { get; private set; } = line;

    /// <summary>The next token.</summary>
    /// <returns>The token; <see cref="GodotTokenType.Eof"/> at the end of the text or at a NUL (variant_parser.cpp:183-186).</returns>
    public GodotToken Next()
    {
        while (true)
        {
            if (Position >= _text.Length || _text[Position] == '\0')
            {
                return Make(GodotTokenType.Eof, Position);
            }

            GodotToken? token = LexAt(Position);
            if (token is { } found)
            {
                return found;
            }
        }
    }

    /// <summary>The error Godot reports at an index of the text.</summary>
    /// <param name="reason">Godot's error text.</param>
    /// <param name="index">The index the error is at.</param>
    /// <returns>The exception to throw.</returns>
    public GodotValueParseException Error(string reason, int index) => new(reason, Line, ColumnAt(index));

    // One token from start, or null when the characters there are skipped (whitespace, a comment).
    private GodotToken? LexAt(int start)
    {
        char c = _text[start];
        if (Punctuation.TryGetValue(c, out GodotTokenType type))
        {
            Position++;
            return Make(type, start);
        }

        if (Openers.TryGetValue(c, out Func<GodotValueLexer, int, GodotToken?>? open))
        {
            return open(this, start);
        }

        if (c <= ' ')
        {
            // variant_parser.cpp:179-182, 416-419.
            Position++;
            if (c == '\n')
            {
                BreakLine();
            }

            return null;
        }

        return LexWord(start);
    }

    private GodotToken? SkipComment()
    {
        int end = _text.IndexOf('\n', Position);
        if (end < 0)
        {
            Position = _text.Length;
            return null;
        }

        Position = end + 1;
        BreakLine();
        return null;
    }

    // variant_parser.cpp:242-262: hex digits after '#'; reaching the end of the text gives EOF, as Godot's stream does.
    private GodotToken LexColor(int start)
    {
        int end = start + 1;
        while (end < _text.Length && char.IsAsciiHexDigit(_text[end]))
        {
            end++;
        }

        Position = end;
        if (end >= _text.Length)
        {
            return Make(GodotTokenType.Eof, end);
        }

        GodotReal color = Html(_text[(start + 1)..end]) ?? new GodotReal(GodotRealKind.Color, [0f, 0f, 0f, 1f]);
        return Make(GodotTokenType.Color, start) with { Value = color };
    }

    // color.cpp:331-370, Color::html: 3, 4, 6 or 8 hex digits; any other code is black.
    private static GodotReal? Html(string hex)
    {
        if (!HtmlDigitWidths.TryGetValue(hex.Length, out int width))
        {
            return null;
        }

        float scale = width == 1 ? 15f : 255f;
        float[] components = [1f, 1f, 1f, 1f];
        for (int i = 0; i < hex.Length / width; i++)
        {
            components[i] = Convert.ToInt32(hex.Substring(i * width, width), 16) / scale;
        }

        return new GodotReal(GodotRealKind.Color, [.. components]);
    }

    // variant_parser.cpp:263-276.
    private GodotToken LexStringName(int start)
    {
        if (start + 1 >= _text.Length || _text[start + 1] != '"')
        {
            throw Error("Expected '\"' after '&'", start);
        }

        return LexString(GodotTokenType.StringName, start, start + 2);
    }

    // variant_parser.cpp:277-415, with the string-stream behaviour: \u and \U give any code point.
    private GodotToken LexString(GodotTokenType type, int start, int contentStart)
    {
        int line = Line;
        int column = ColumnAt(start);
        var builder = new System.Text.StringBuilder();
        var pending = new PendingSurrogate();
        Position = contentStart;
        while (true)
        {
            char c = ReadStringChar();
            if (c == '"')
            {
                break;
            }

            if (c == '\\')
            {
                AppendCodePoint(builder, pending.Accept(ReadEscape(), this));
                continue;
            }

            pending.RejectIfOpen(this);
            if (c == '\n')
            {
                BreakLine();
            }

            builder.Append(c);
        }

        pending.RejectIfOpen(this);
        return new GodotToken(type, builder.ToString(), null, line, column);
    }

    // The next character inside a string; the end of the text or a NUL leaves it unterminated.
    private char ReadStringChar()
    {
        if (Position >= _text.Length || _text[Position] == '\0')
        {
            throw Error("Unterminated string", Position);
        }

        return _text[Position++];
    }

    // variant_parser.cpp:291-354: the character after a backslash; any character without a meaning is itself.
    private int ReadEscape()
    {
        char next = ReadStringChar();
        if (next == '\n')
        {
            BreakLine();
        }

        if (NamedEscapes.TryGetValue(next, out int named))
        {
            return named;
        }

        if (next is 'u' or 'U')
        {
            return ReadHex(next == 'U' ? 6 : 4);
        }

        return PairSurrogates(next);
    }

    // A lead surrogate followed by its trail is one code point above U+FFFF; a lone lead is the code unit itself.
    private int PairSurrogates(char lead) =>
        char.IsHighSurrogate(lead) && Position < _text.Length && char.IsLowSurrogate(_text[Position])
            ? char.ConvertToUtf32(lead, _text[Position++])
            : lead;

    // After the newline at Position - 1: the line ends there, and the next one starts at Position.
    private void BreakLine()
    {
        Line++;
        _lineStart = Position;
    }

    private int ReadHex(int length)
    {
        int value = 0;
        for (int i = 0; i < length; i++)
        {
            char c = ReadStringChar();
            if (!char.IsAsciiHexDigit(c))
            {
                throw Error("Malformed hex constant in string", Position - 1);
            }

            value = (value << 4) | Convert.ToInt32(c.ToString(), 16);
        }

        return value;
    }

    // A code point Godot appends; a NUL (ustring.cpp:202-206) and one past U+10FFFF are no character to it, so they
    // become U+FFFD (ustring.cpp:217-219). -1 appends nothing.
    private static void AppendCodePoint(System.Text.StringBuilder builder, int codePoint)
    {
        if (codePoint < 0)
        {
            return;
        }

        builder.Append(codePoint is > 0 and <= 0x10FFFF ? char.ConvertFromUtf32(codePoint) : "�");
    }

    // variant_parser.cpp:421-513: a number, an identifier (which keeps a leading '-'), or an unexpected character.
    private GodotToken LexWord(int start)
    {
        int first = _text[start] == '-' ? start + 1 : start;
        char c = first < _text.Length ? _text[first] : '\0';
        if (char.IsAsciiDigit(c))
        {
            return LexNumber(start, first);
        }

        if (char.IsAsciiLetter(c) || c == '_')
        {
            return LexIdentifier(start, first);
        }

        throw Error("Unexpected character", start);
    }

    private GodotToken LexIdentifier(int start, int first)
    {
        int end = first + 1;
        while (end < _text.Length && (char.IsAsciiLetterOrDigit(_text[end]) || _text[end] == '_'))
        {
            end++;
        }

        Position = end;
        return Make(GodotTokenType.Identifier, start) with { Text = _text[start..end] };
    }

    // variant_parser.cpp:432-493: digits, then '.' and digits, then 'e' or 'E', one sign and digits; a '.' or an
    // exponent makes it a float.
    private GodotToken LexNumber(int start, int first)
    {
        int integral = SkipDigits(first);
        int fraction = At(integral, '.') ? SkipDigits(integral + 1) : integral;
        int end = At(fraction, 'e') || At(fraction, 'E') ? SkipExponent(fraction + 1) : fraction;

        Position = end;
        string number = _text[start..end];
        GodotValue value = end != integral ? new GodotFloat(ParseFloat(number)) : ParseInt(number, start);
        return Make(GodotTokenType.Number, start) with { Text = number, Value = value };
    }

    private bool At(int index, char c) => index < _text.Length && _text[index] == c;

    private int SkipExponent(int index) => SkipDigits(At(index, '+') || At(index, '-') ? index + 1 : index);

    // ustring.cpp:2362, String::to_float; Godot reads "1e" and "1e+" as 1, which .NET does not parse, so a dangling
    // exponent is dropped first.
    private static double ParseFloat(string number)
    {
        string complete = number.TrimEnd('+', '-').TrimEnd('e', 'E');
        return double.Parse(complete, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ustring.cpp:2613-2675, String::to_int; its out-of-range error print is a parse error here.
    private GodotInt ParseInt(string number, int start)
    {
        if (
            long.TryParse(
                number,
                System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture,
                out long value
            )
        )
        {
            return new GodotInt(value);
        }

        string side = number[0] == '-' ? "small" : "large";
        throw Error($"Cannot represent {number} as a 64-bit signed integer, since the value is too {side}.", start);
    }

    private int SkipDigits(int index)
    {
        while (index < _text.Length && char.IsAsciiDigit(_text[index]))
        {
            index++;
        }

        return index;
    }

    // A token that starts at an index of the current line.
    private GodotToken Make(GodotTokenType type, int index) => new(type, "", null, Line, ColumnAt(index));

    private int ColumnAt(int index) => index - _lineStart + 1;

    // variant_parser.cpp:356-398: a UTF-16 lead surrogate from an escape waits for its trail surrogate.
    private sealed class PendingSurrogate
    {
        private const string UnpairedLead = "Invalid UTF-16 sequence in string, unpaired lead surrogate";

        private int _lead;

        // The code point an escape adds, or -1 while a lead surrogate waits.
        public int Accept(int value, GodotValueLexer lexer)
        {
            if ((value & 0xFFFFFC00) == 0xD800)
            {
                RejectIfOpen(lexer);
                _lead = value;
                return -1;
            }

            if ((value & 0xFFFFFC00) == 0xDC00)
            {
                return Pair(value, lexer);
            }

            RejectIfOpen(lexer);
            return value;
        }

        public void RejectIfOpen(GodotValueLexer lexer)
        {
            if (_lead != 0)
            {
                throw lexer.Error(UnpairedLead, lexer.Position - 1);
            }
        }

        private int Pair(int trail, GodotValueLexer lexer)
        {
            if (_lead == 0)
            {
                throw lexer.Error("Invalid UTF-16 sequence in string, unpaired trail surrogate", lexer.Position - 1);
            }

            int codePoint = (_lead << 10) + trail - ((0xD800 << 10) + 0xDC00 - 0x10000);
            _lead = 0;
            return codePoint;
        }
    }
}
