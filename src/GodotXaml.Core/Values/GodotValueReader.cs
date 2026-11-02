namespace GodotXaml.Core.Values;

/// <summary>Reads Godot's value text, a port of VariantParser::parse_value (core/variant/variant_parser.cpp:677-1642).</summary>
public static class GodotValueReader
{
    // variant_parser.cpp:150-160, stor_fix: the names a constructor argument may use for a non-finite value.
    private static readonly Dictionary<string, double> NonFiniteNames = new(StringComparer.Ordinal)
    {
        ["inf"] = double.PositiveInfinity,
        ["-inf"] = double.NegativeInfinity,
        ["inf_neg"] = double.NegativeInfinity,
        ["nan"] = double.NaN,
    };

    // The one dispatch point for identifiers (variant_parser.cpp:694-1622). An identifier missing here is
    // "Unexpected identifier", as in Godot; containers, packed arrays, references and Object raise that error.
    private static readonly Dictionary<string, Func<GodotValueLexer, GodotValue>> Identifiers = BuildIdentifiers();

    /// <summary>Reads a whole text as one value; only whitespace and <c>;</c> comments may follow it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="GodotValueParseException">The text is not one value Godot reads.</exception>
    public static GodotValue Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int position = 0;
        int line = 1;
        GodotValue value = ReadAt(text, ref position, ref line);

        GodotToken rest = new GodotValueLexer(text, position, line).Next();
        if (rest.Type != GodotTokenType.Eof)
        {
            throw Error($"Expected end of text, got '{GodotValueTables.TokenName(rest.Type)}'", rest);
        }

        return value;
    }

    /// <summary>Reads one value from a position, as VariantParser::parse_value does for a document reader.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The index to start at; the index after the value on return.</param>
    /// <param name="line">The 1-based line of the position, kept in step with it.</param>
    /// <returns>The value.</returns>
    /// <exception cref="GodotValueParseException">The text at the position is not a value Godot reads.</exception>
    internal static GodotValue ReadAt(string text, ref int position, ref int line)
    {
        var lexer = new GodotValueLexer(text, position, line);
        GodotValue value = ParseValue(lexer, lexer.Next());
        position = lexer.Position;
        line = lexer.Line;
        return value;
    }

    /// <summary>The value a token starts.</summary>
    /// <param name="lexer">The lexer, just past the token.</param>
    /// <param name="token">The value's first token.</param>
    /// <returns>The value.</returns>
    internal static GodotValue ParseValue(GodotValueLexer lexer, GodotToken token) =>
        token.Type switch
        {
            GodotTokenType.Identifier => ParseIdentifier(lexer, token),
            GodotTokenType.Number or GodotTokenType.Color => token.Value!,
            GodotTokenType.String => new GodotString(token.Text),
            GodotTokenType.StringName => new GodotStringName(token.Text),

            // variant_parser.cpp:1639: tk_name already quotes punctuation, so Godot's text reads got ''[''.
            _ => throw Error($"Expected value, got '{GodotValueTables.TokenName(token.Type)}'", token),
        };

    private static GodotValue ParseIdentifier(GodotValueLexer lexer, GodotToken token) =>
        Identifiers.TryGetValue(token.Text, out Func<GodotValueLexer, GodotValue>? parse)
            ? parse(lexer)
            : throw Error($"Unexpected identifier '{token.Text}'", token);

    private static Dictionary<string, Func<GodotValueLexer, GodotValue>> BuildIdentifiers()
    {
        // variant_parser.cpp:696-708 and 921-994.
        var parsers = new Dictionary<string, Func<GodotValueLexer, GodotValue>>(StringComparer.Ordinal)
        {
            ["true"] = _ => new GodotBool(true),
            ["false"] = _ => new GodotBool(false),
            ["null"] = _ => GodotNull.Instance,
            ["nil"] = _ => GodotNull.Instance,
            ["NodePath"] = ReadNodePath,
            ["RID"] = ReadRid,
            ["Signal"] = lexer => ReadEmptyCall(lexer, GodotSignal.Instance),
            ["Callable"] = lexer => ReadEmptyCall(lexer, GodotCallable.Instance),
        };
        foreach (KeyValuePair<string, double> nonFinite in NonFiniteNames)
        {
            parsers[nonFinite.Key] = _ => new GodotFloat(nonFinite.Value);
        }

        foreach (KeyValuePair<GodotRealKind, ConstructorSpec> real in GodotValueTables.Reals)
        {
            AddConstructor(parsers, real.Value, lexer => new GodotReal(real.Key, [.. ReadArguments(lexer, real.Value).Select(ToFloat)]));
        }

        foreach (KeyValuePair<GodotIntKind, ConstructorSpec> ints in GodotValueTables.Ints)
        {
            AddConstructor(parsers, ints.Value, lexer => new GodotInts(ints.Key, [.. ReadArguments(lexer, ints.Value).Select(ToInt32)]));
        }

        return parsers;
    }

    private static void AddConstructor(
        Dictionary<string, Func<GodotValueLexer, GodotValue>> parsers,
        ConstructorSpec spec,
        Func<GodotValueLexer, GodotValue> parse
    )
    {
        parsers[spec.Name] = parse;
        foreach (string alias in spec.Aliases)
        {
            parsers[alias] = parse;
        }
    }

    // variant_parser.cpp:552-599 (_parse_construct), then the argument count check each constructor makes.
    private static List<GodotValue> ReadArguments(GodotValueLexer lexer, ConstructorSpec spec)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '(' in constructor");
        var arguments = new List<GodotValue>();
        bool first = true;
        while (first || !NextClosesConstructor(lexer))
        {
            GodotToken token = lexer.Next();
            if (first && token.Type == GodotTokenType.ParenthesisClose)
            {
                break;
            }

            arguments.Add(ConstructorNumber(token));
            first = false;
        }

        if (arguments.Count != spec.Arity)
        {
            throw lexer.Error($"Expected {spec.Arity} arguments for constructor", lexer.Position - 1);
        }

        return arguments;
    }

    // variant_parser.cpp:563-573: after an argument, ',' goes on and ')' ends.
    private static bool NextClosesConstructor(GodotValueLexer lexer)
    {
        GodotToken token = lexer.Next();
        if (token.Type == GodotTokenType.Comma)
        {
            return false;
        }

        return token.Type == GodotTokenType.ParenthesisClose ? true : throw Error("Expected ',' or ')' in constructor", token);
    }

    // variant_parser.cpp:576-594: a number, or a stor_fix name.
    private static GodotValue ConstructorNumber(GodotToken token)
    {
        if (token.Type == GodotTokenType.Number)
        {
            return token.Value!;
        }

        bool named = token.Type == GodotTokenType.Identifier && NonFiniteNames.ContainsKey(token.Text);
        return named ? new GodotFloat(NonFiniteNames[token.Text]) : throw Error("Expected float in constructor", token);
    }

    // variant_parser.cpp:594 pushes the argument into a Vector<real_t> (float): an int converts straight to float, a
    // double narrows.
    private static float ToFloat(GodotValue argument) => argument is GodotInt integer ? integer.Value : (float)((GodotFloat)argument).Value;

    // variant_parser.cpp:723-724 reads into a Vector<int32_t>: an int wraps to 32 bits, a double truncates toward zero
    // a double outside int32, or NaN, gives int.MinValue as x86-64's conversion does.
    private static int ToInt32(GodotValue argument)
    {
        if (argument is GodotInt integer)
        {
            return unchecked((int)integer.Value);
        }

        double value = Math.Truncate(((GodotFloat)argument).Value);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value : int.MinValue;
    }

    // variant_parser.cpp:921-940.
    private static GodotNodePath ReadNodePath(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        GodotToken path = Expect(lexer, GodotTokenType.String, "Expected string as argument for NodePath()");
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return new GodotNodePath(path.Text);
    }

    // variant_parser.cpp:941-964; RID::from_uint64(0) is the empty RID, so an id of 0 reads as RID().
    private static GodotRid ReadRid(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        GodotToken token = lexer.Next();
        if (token.Type == GodotTokenType.ParenthesisClose)
        {
            return new GodotRid(null);
        }

        ulong id = token.Value switch
        {
            GodotInt integer => unchecked((ulong)integer.Value),
            GodotFloat real => unchecked((ulong)ToInt64(real.Value)),
            _ => throw Error("Expected number as argument or ')'", token),
        };
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return new GodotRid(id);
    }

    // x86-64's double-to-int64 conversion, which RID::from_uint64 applies to a float token: a negative id wraps, and a
    // NaN or a value outside int64 gives int64.MinValue.
    private static long ToInt64(double value) =>
        double.IsFinite(value) && value >= -9223372036854775808.0 && value < 9223372036854775808.0 ? (long)value : long.MinValue;

    // variant_parser.cpp:965-994: Signal() and Callable() take no arguments.
    private static GodotValue ReadEmptyCall(GodotValueLexer lexer, GodotValue value)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return value;
    }

    private static GodotToken Expect(GodotValueLexer lexer, GodotTokenType type, string reason)
    {
        GodotToken token = lexer.Next();
        return token.Type == type ? token : throw Error(reason, token);
    }

    private static GodotValueParseException Error(string reason, GodotToken token) => new(reason, token.Line, token.Column);
}
