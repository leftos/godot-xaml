using System.Collections.Immutable;

namespace GodotXaml.Core.Values;

// Packed arrays and their legacy names (core/variant/variant_parser.cpp:601-675, 1411-1618).
public static partial class GodotValueReader
{
    private static void AddPacked(Dictionary<string, Func<GodotValueLexer, GodotValue>> parsers)
    {
        AddConstructor(parsers, GodotValueTables.PackedBytes, ReadPackedBytes);
        AddConstructor(parsers, GodotValueTables.PackedInts, lexer => new GodotPackedInts([.. ReadConstruct(lexer).Select(ToInt32)]));
        AddConstructor(parsers, GodotValueTables.PackedLongs, lexer => new GodotPackedLongs([.. ReadConstruct(lexer).Select(ToLong)]));
        AddConstructor(parsers, GodotValueTables.PackedFloats, lexer => new GodotPackedFloats([.. ReadConstruct(lexer).Select(ToFloat)]));
        AddConstructor(parsers, GodotValueTables.PackedDoubles, lexer => new GodotPackedDoubles([.. ReadConstruct(lexer).Select(ToDouble)]));
        AddConstructor(parsers, GodotValueTables.PackedStrings, ReadPackedStrings);
        foreach (KeyValuePair<GodotPackedKind, ConstructorSpec> packed in GodotValueTables.PackedReals)
        {
            AddConstructor(parsers, packed.Value, lexer => new GodotPackedReals(packed.Key, WholeElements(ReadConstruct(lexer), packed.Value.Arity)));
        }
    }

    // :1556, :1574, :1592, :1610: the element count is the component count divided by the width, so a ragged tail is
    // dropped.
    private static ImmutableArray<float> WholeElements(List<GodotValue> components, int width) =>
        [.. components.Take(components.Count - (components.Count % width)).Select(ToFloat)];

    // :601-675, _parse_byte_array: nothing, one base64 string, or numbers.
    private static GodotPackedBytes ReadPackedBytes(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '(' in constructor");
        GodotToken token = lexer.Next();
        return token.Type switch
        {
            GodotTokenType.ParenthesisClose => new GodotPackedBytes([]),
            GodotTokenType.String => ReadBase64(lexer, token),
            GodotTokenType.Number or GodotTokenType.Identifier => new GodotPackedBytes(ReadByteList(lexer, token)),
            _ => throw Error("Expected base64 string, or list of numbers in constructor", token),
        };
    }

    // :610-629: CryptoCore::b64_decode of the string, then ')'.
    private static GodotPackedBytes ReadBase64(GodotValueLexer lexer, GodotToken token)
    {
        // Convert.FromBase64String skips whitespace inside the string; CryptoCore::b64_decode rejects it.
        if (token.Text.Any(char.IsWhiteSpace))
        {
            throw Error("Invalid base64-encoded string", token);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(token.Text);
        }
        catch (FormatException)
        {
            throw Error("Invalid base64-encoded string", token);
        }

        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')' in constructor");
        return new GodotPackedBytes([.. bytes]);
    }

    // :631-662: numbers or stor_fix names split by ',', each narrowed to a byte as Vector<uint8_t>::push_back does:
    // Variant::_to_int<uint8_t> takes an int's low byte, and gives a double uint8_t(double), which x86-64 converts
    // through a 32-bit cvttsd2si, so a double goes to int32 first.
    private static ImmutableArray<byte> ReadByteList(GodotValueLexer lexer, GodotToken first)
    {
        ImmutableArray<byte>.Builder bytes = ImmutableArray.CreateBuilder<byte>();
        GodotToken token = first;
        while (true)
        {
            bytes.Add(unchecked((byte)ToInt32(ByteNumber(token))));
            GodotToken next = lexer.Next();
            if (next.Type == GodotTokenType.ParenthesisClose)
            {
                return bytes.ToImmutable();
            }

            token = next.Type == GodotTokenType.Comma ? lexer.Next() : throw Error("Expected ',' or ')' in constructor", next);
        }
    }

    private static GodotValue ByteNumber(GodotToken token)
    {
        bool number = token.Type == GodotTokenType.Number || (token.Type == GodotTokenType.Identifier && NonFiniteNames.ContainsKey(token.Text));
        return number ? ConstructorNumber(token) : throw Error("Expected number in constructor", token);
    }

    // :1501-1546: strings split by ',', a ',' allowed before the ')'.
    private static GodotPackedStrings ReadPackedStrings(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        ImmutableArray<string>.Builder items = ImmutableArray.CreateBuilder<string>();
        GodotToken token = lexer.Next();
        while (token.Type != GodotTokenType.ParenthesisClose)
        {
            items.Add(token.Type == GodotTokenType.String ? token.Text : throw Error("Expected string", token));
            token = AfterString(lexer);
        }

        return new GodotPackedStrings(items.ToImmutable());
    }

    // After a string: the ')', or a ',' and the token after it.
    private static GodotToken AfterString(GodotValueLexer lexer)
    {
        GodotToken next = lexer.Next();
        if (next.Type == GodotTokenType.Comma)
        {
            return lexer.Next();
        }

        return next.Type == GodotTokenType.ParenthesisClose ? next : throw Error("Expected ',' or ')'", next);
    }

    // _parse_construct<int64_t>: an int as it is, a double converted as x86-64 converts it.
    private static long ToLong(GodotValue argument) => argument is GodotInt integer ? integer.Value : ToInt64(((GodotFloat)argument).Value);

    private static double ToDouble(GodotValue argument) => argument is GodotInt integer ? integer.Value : ((GodotFloat)argument).Value;
}
