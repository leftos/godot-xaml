using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

public sealed class GodotValueLexerTests
{
    // variant_parser.cpp:421-493: an int unless the text has '.' or 'e'/'E'; one sign may open the exponent.
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("-1", -1L)]
    [InlineData("007", 7L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void LexesInts(string text, long expected) => Assert.Equal(new GodotInt(expected), Single(text, GodotTokenType.Number).Value);

    [Theory]
    [InlineData("1.0", 1.0)]
    [InlineData("1.", 1.0)]
    [InlineData("1e5", 1e5)]
    [InlineData("-1E-3", -1e-3)]
    [InlineData("2.5e+3", 2500.0)]
    [InlineData("1.5e-05", 1.5e-5)]
    [InlineData("1e400", double.PositiveInfinity)]
    public void LexesFloats(string text, double expected) => Assert.Equal(new GodotFloat(expected), Single(text, GodotTokenType.Number).Value);

    // ustring.cpp:2648-2661: an int past Int64 is Godot's error print; the reader makes it a parse error.
    [Theory]
    [InlineData("9223372036854775808", "large")]
    [InlineData("-9223372036854775809", "small")]
    public void RejectsIntsOutsideInt64(string text, string side)
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => Lex(text));
        Assert.Equal($"Cannot represent {text} as a 64-bit signed integer, since the value is too {side}.", error.Reason);
    }

    // Non-numbers: '.' is a period, '+' an unexpected character, and hex stops after the 0.
    [Fact]
    public void LexesALeadingPointAsAPeriod() => Assert.Equal([GodotTokenType.Period, GodotTokenType.Number], Types(".5"));

    [Fact]
    public void RejectsALeadingPlus() => Assert.Equal("Unexpected character", Assert.Throws<GodotValueParseException>(() => Lex("+1")).Reason);

    [Fact]
    public void LexesHexAsZeroThenAnIdentifier()
    {
        List<GodotToken> tokens = Lex("0x10");
        Assert.Equal(new GodotInt(0), tokens[0].Value);
        Assert.Equal((GodotTokenType.Identifier, "x10"), (tokens[1].Type, tokens[1].Text));
    }

    // variant_parser.cpp:421-423, 494-507: a '-' before a letter stays in the identifier.
    [Theory]
    [InlineData("-inf")]
    [InlineData("-nan")]
    [InlineData("inf_neg")]
    [InlineData("_a1")]
    public void LexesIdentifiers(string text) => Assert.Equal(text, Single(text, GodotTokenType.Identifier).Text);

    [Fact]
    public void RejectsAMinusBeforeNothing() =>
        Assert.Equal("Unexpected character", Assert.Throws<GodotValueParseException>(() => Lex("- 1")).Reason);

    [Fact]
    public void LexesPunctuation() =>
        Assert.Equal(
            [
                GodotTokenType.CurlyBracketOpen,
                GodotTokenType.CurlyBracketClose,
                GodotTokenType.BracketOpen,
                GodotTokenType.BracketClose,
                GodotTokenType.ParenthesisOpen,
                GodotTokenType.ParenthesisClose,
                GodotTokenType.Colon,
                GodotTokenType.Comma,
                GodotTokenType.Period,
                GodotTokenType.Equal,
            ],
            Types("{ } [ ] ( ) : , . =")
        );

    // variant_parser.cpp:299-354: five named escapes, \u and \U, and any other character taken as itself.
    [Theory]
    [InlineData("\"\\b\\t\\n\\f\\r\"", "\b\t\n\f\r")]
    [InlineData("\"\\\"\\\\\\'\\/\\a\\v\\0\"", "\"\\'/av0")]
    [InlineData("\"\\u00e9\\u2026\"", "\u00e9\u2026")]
    [InlineData("\"\\U01F600\"", "\U0001F600")]
    [InlineData("\"\\uD83D\\uDE00\"", "\U0001F600")]
    [InlineData("\"\\\U0001F600\"", "\U0001F600")]
    [InlineData("\"\\u0000\"", "\uFFFD")]
    [InlineData("\"a\\u0000b\"", "a\uFFFDb")]
    [InlineData("\"\\U000000\"", "\uFFFD")]
    [InlineData("\"raw\nnewline\ttab\"", "raw\nnewline\ttab")]
    [InlineData("\"\"", "")]
    public void LexesStrings(string text, string expected) => Assert.Equal(expected, Single(text, GodotTokenType.String).Text);

    // variant_parser.cpp:263-276: &"..." and the legacy @"...".
    [Theory]
    [InlineData("&\"name\"")]
    [InlineData("@\"name\"")]
    public void LexesStringNames(string text) => Assert.Equal("name", Single(text, GodotTokenType.StringName).Text);

    [Theory]
    [InlineData("&x", "Expected '\"' after '&'")]
    [InlineData("\"abc", "Unterminated string")]
    [InlineData("\"abc\\", "Unterminated string")]
    [InlineData("\"\\u12", "Unterminated string")]
    [InlineData("\"\\u12G4\"", "Malformed hex constant in string")]
    [InlineData("\"\\uD800\"", "Invalid UTF-16 sequence in string, unpaired lead surrogate")]
    [InlineData("\"\\uD800x\"", "Invalid UTF-16 sequence in string, unpaired lead surrogate")]
    [InlineData("\"\\uD800\\uD800\"", "Invalid UTF-16 sequence in string, unpaired lead surrogate")]
    [InlineData("\"\\uD800\\n\"", "Invalid UTF-16 sequence in string, unpaired lead surrogate")]
    [InlineData("\"\\uDC00\"", "Invalid UTF-16 sequence in string, unpaired trail surrogate")]
    public void RejectsBadStrings(string text, string reason) =>
        Assert.Equal(reason, Assert.Throws<GodotValueParseException>(() => Lex(text)).Reason);

    // variant_parser.cpp:215-229: ';' starts a comment to the end of the line, which still counts.
    [Fact]
    public void SkipsCommentsAndCountsTheirLines()
    {
        GodotToken token = Single("; a comment\n  5 ; another", GodotTokenType.Number);
        Assert.Equal((2, 3), (token.Line, token.Column));
    }

    // variant_parser.cpp:242-262 and color.cpp:331-370: '#' and hex digits are a colour, not a comment.
    [Theory]
    [InlineData("#f80 ", 1f, 8f / 15f, 0f, 1f)]
    [InlineData("#f808 ", 1f, 8f / 15f, 0f, 8f / 15f)]
    [InlineData("#ff8000 ", 1f, 128f / 255f, 0f, 1f)]
    [InlineData("#FF800080,", 1f, 128f / 255f, 0f, 128f / 255f)]
    public void LexesColors(string text, float r, float g, float b, float a) =>
        Assert.Equal(new GodotReal(GodotRealKind.Color, [r, g, b, a]), Lex(text)[0].Value);

    // variant_parser.cpp:246-249: a colour that runs to the end of the text gives EOF.
    [Fact]
    public void LexesAColorAtTheEndAsEof() => Assert.Equal([GodotTokenType.Eof], Types("#f80"));

    // color.cpp:331-366: Color::html logs an invalid code and returns Color(), black, and the token stays a colour.
    [Theory]
    [InlineData("#ff ")]
    [InlineData("#12345 ")]
    [InlineData("# ")]
    public void LexesAColorOfTheWrongLengthAsBlack(string text) => Assert.Equal(new GodotReal(GodotRealKind.Color, [0, 0, 0, 1]), Lex(text)[0].Value);

    [Fact]
    public void ReportsTheLineAndColumnOfAnError()
    {
        GodotValueParseException error = Assert.Throws<GodotValueParseException>(() => Lex("1,\n\n  +"));
        Assert.Equal((3, 3), (error.Line, error.Column));
        Assert.Equal("Unexpected character (line 3, column 3)", error.Message);
    }

    [Fact]
    public void CountsNewlinesInsideStrings() => Assert.Equal(3, Lex("\"a\nb\"\n1")[1].Line);

    // variant_parser.cpp:283-300: a backslash and a raw newline keep the newline in the string, and the newline still
    // counts, so the next token's line and column agree with the text.
    [Fact]
    public void CountsALineBrokenByAnEscapedNewline()
    {
        GodotToken token = Lex("\"a\\\nb\" foo")[1];
        Assert.Equal((GodotTokenType.Identifier, "foo", 2, 4), (token.Type, token.Text, token.Line, token.Column));
    }

    // A column is the distance from the start of the token's line, found without a scan back through the text.
    [Fact]
    public void CountsColumnsInALongSingleLineText()
    {
        const int Count = 200_000;
        List<GodotToken> tokens = Lex(string.Concat(Enumerable.Repeat("1 ", Count)));
        Assert.Equal(Count + 1, tokens.Count);
        Assert.Equal((1, 2 * Count - 1), (tokens[Count - 1].Line, tokens[Count - 1].Column));
    }

    [Fact]
    public void StartsAtAGivenPositionAndLine()
    {
        var lexer = new GodotValueLexer("x = 12", 4, 7);
        GodotToken token = lexer.Next();
        Assert.Equal((new GodotInt(12) as GodotValue, 7, 5), (token.Value, token.Line, token.Column));
        Assert.Equal(6, lexer.Position);
    }

    private static List<GodotToken> Lex(string text)
    {
        var lexer = new GodotValueLexer(text, 0, 1);
        var tokens = new List<GodotToken>();
        GodotToken token;
        do
        {
            token = lexer.Next();
            tokens.Add(token);
        } while (token.Type != GodotTokenType.Eof);

        return tokens;
    }

    // The token types before EOF, or EOF alone when there are none.
    private static List<GodotTokenType> Types(string text)
    {
        List<GodotTokenType> types = [.. Lex(text).Select(token => token.Type).Where(type => type != GodotTokenType.Eof)];
        return types.Count == 0 ? [GodotTokenType.Eof] : types;
    }

    private static GodotToken Single(string text, GodotTokenType type)
    {
        List<GodotToken> tokens = Lex(text);
        Assert.Equal([type, GodotTokenType.Eof], tokens.Select(token => token.Type));
        return tokens[0];
    }
}
