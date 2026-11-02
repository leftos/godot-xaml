using System.Text;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>A tag or a property line, with the 1-based position it starts at.</summary>
/// <param name="Line">The line.</param>
/// <param name="Column">The column.</param>
internal abstract record TscnItem(int Line, int Column);

/// <summary>A section header, <c>[name field=value ...]</c> (variant_parser.cpp:1750-1873, _parse_tag).</summary>
/// <param name="Name">The tag name.</param>
/// <param name="Fields">The fields in source order, a repeated name included.</param>
/// <param name="Line">The line of the <c>[</c>.</param>
/// <param name="Column">The column of the <c>[</c>.</param>
internal sealed record TscnTag(string Name, IReadOnlyList<TscnTagField> Fields, int Line, int Column) : TscnItem(Line, Column);

/// <summary>A tag field: its name, its value and the value's source text.</summary>
/// <param name="Name">The field name.</param>
/// <param name="Value">The value.</param>
/// <param name="Text">The value's source text, trimmed.</param>
/// <param name="Line">The line of the value.</param>
/// <param name="Column">The column of the value.</param>
internal sealed record TscnTagField(string Name, GodotValue Value, string Text, int Line, int Column);

/// <summary>A <c>name = value</c> line (variant_parser.cpp:1891-1961, parse_tag_assign_eof).</summary>
/// <param name="Name">The property name, unquoted.</param>
/// <param name="Value">The value.</param>
/// <param name="Line">The line of the name.</param>
/// <param name="Column">The column of the name.</param>
internal sealed record TscnAssign(string Name, GodotValue Value, int Line, int Column) : TscnItem(Line, Column);

/// <summary>Splits a .tscn or .tres text into tags and property lines, as parse_tag_assign_eof does.</summary>
/// <param name="text">The text.</param>
internal sealed class TscnScanner(string text)
{
    private readonly string _text = text;
    private readonly StringBuilder _key = new();
    private int _position;
    private int _line = 1;
    private int _lineStart;
    private int _keyLine;
    private int _keyColumn;

    /// <summary>The next tag or property line, or null at the end of the text.</summary>
    /// <returns>The item, or null.</returns>
    /// <exception cref="TscnParseException">The text is not one Godot's loader reads.</exception>
    public TscnItem? Next()
    {
        _key.Clear();
        while (_position < _text.Length)
        {
            char c = _text[_position];
            if (c == '[' && _key.Length == 0)
            {
                return ReadTag();
            }

            if (c == '=')
            {
                return ReadAssign();
            }

            ConsumeKeyChar(c);
        }

        // variant_parser.cpp:1906-1908: the text ending inside a key is ERR_FILE_EOF, a section's normal end.
        return null;
    }

    /// <summary>An error at a position.</summary>
    /// <param name="reason">The error text.</param>
    /// <param name="line">The line.</param>
    /// <param name="column">The column.</param>
    /// <returns>The exception to throw.</returns>
    public static TscnParseException Error(string reason, int line, int column) => new(reason, line, column, null);

    // variant_parser.cpp:1910-1959: a comment, a quoted key, a key character or skipped whitespace.
    private void ConsumeKeyChar(char c)
    {
        if (c == ';')
        {
            SkipComment();
            return;
        }

        if (c == '"')
        {
            ReadQuotedKey();
            return;
        }

        _position++;
        if (c == '\n')
        {
            BreakLine();
        }
        else if (c > ' ')
        {
            MarkKeyStart();
            _key.Append(c);
        }
    }

    private void SkipComment()
    {
        int end = _text.IndexOf('\n', _position);
        _position = end < 0 ? _text.Length : end + 1;
        if (end >= 0)
        {
            BreakLine();
        }
    }

    // variant_parser.cpp:1934-1946: a quoted key replaces what came before it.
    private void ReadQuotedKey()
    {
        MarkKeyStart();
        GodotValueLexer lexer = Lexer();
        GodotToken token = lexer.Next();
        Sync(lexer);
        _key.Clear().Append(token.Text);
    }

    private void MarkKeyStart()
    {
        if (_key.Length == 0)
        {
            _keyLine = _line;
            _keyColumn = Column;
        }
    }

    private TscnAssign ReadAssign()
    {
        string name = _key.ToString();
        int line = _key.Length == 0 ? _line : _keyLine;
        int column = _key.Length == 0 ? Column : _keyColumn;
        _position++;
        GodotValue value = GodotValueReader.ReadAt(_text, ref _position, ref _line);
        _lineStart = LineStartOf(_position);
        return new TscnAssign(name, value, line, column);
    }

    private TscnTag ReadTag()
    {
        int line = _line;
        int column = Column;
        GodotValueLexer lexer = Lexer();
        lexer.Next();
        GodotToken nameToken = lexer.Next();
        string name =
            nameToken.Type == GodotTokenType.Identifier
                ? nameToken.Text
                : throw Error("Expected identifier (tag name)", nameToken.Line, nameToken.Column);
        List<TscnTagField> fields = [];
        bool parsingTag = true;
        while (true)
        {
            GodotToken token = lexer.Next();
            if (token.Type == GodotTokenType.BracketClose)
            {
                break;
            }

            if (parsingTag && token.Type is GodotTokenType.Period or GodotTokenType.Colon)
            {
                name += (token.Type == GodotTokenType.Period ? "." : ":") + Expect(lexer, GodotTokenType.Identifier, "Expected identifier").Text;
                continue;
            }

            parsingTag = false;
            fields.Add(ReadField(lexer, token, name));
        }

        Sync(lexer);
        return new TscnTag(name, fields, line, column);
    }

    // variant_parser.cpp:1824-1869: identifier, '=', then any value.
    private TscnTagField ReadField(GodotValueLexer lexer, GodotToken token, string tagName)
    {
        if (token.Type == GodotTokenType.Eof)
        {
            throw Error($"Unexpected EOF while parsing tag '{tagName}'", token.Line, token.Column);
        }

        if (token.Type != GodotTokenType.Identifier)
        {
            throw Error("Expected identifier", token.Line, token.Column);
        }

        Expect(lexer, GodotTokenType.Equal, "Expected '=' after identifier");
        int start = lexer.Position;
        GodotToken first = lexer.Next();
        GodotValue value = GodotValueReader.ParseValue(lexer, first);
        return new TscnTagField(token.Text, value, _text[start..lexer.Position].Trim(), first.Line, first.Column);
    }

    private static GodotToken Expect(GodotValueLexer lexer, GodotTokenType type, string reason)
    {
        GodotToken token = lexer.Next();
        return token.Type == type ? token : throw Error(reason, token.Line, token.Column);
    }

    private GodotValueLexer Lexer() => new(_text, _position, _line);

    private void Sync(GodotValueLexer lexer)
    {
        _position = lexer.Position;
        _line = lexer.Line;
        _lineStart = LineStartOf(_position);
    }

    private int LineStartOf(int index) => index > 0 ? _text.LastIndexOf('\n', index - 1) + 1 : 0;

    private void BreakLine()
    {
        _line++;
        _lineStart = _position;
    }

    /// <summary>The line the scanner last stopped on.</summary>
    public int Line => _line;

    /// <summary>The column the scanner last stopped at.</summary>
    public int Column => _position - _lineStart + 1;
}
