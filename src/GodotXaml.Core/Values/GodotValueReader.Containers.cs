using System.Collections.Immutable;

namespace GodotXaml.Core.Values;

// Arrays and dictionaries, plain and typed (core/variant/variant_parser.cpp:1188-1410, 1644-1748).
public static partial class GodotValueReader
{
    private const string ArrayEof = "Unexpected EOF while parsing array";
    private const string DictionaryEof = "Unexpected EOF while parsing dictionary";

    // Godot's parser has no limit, but here a recursion without one could overflow the stack, which .NET cannot catch.
    private const int MaxDepth = 512;

    // The type names Godot does not resolve to a type, which leave a container untyped (variant_parser.cpp:1203-1207:
    // the builtin names start at type 1, so Nil is not one, and Variant names no type at all).
    private static readonly HashSet<string> UntypedNames = new(StringComparer.Ordinal) { "Variant", "Nil" };

    private static void AddContainers(Dictionary<string, Func<GodotValueLexer, GodotValue>> parsers)
    {
        parsers["Array"] = ReadTypedArray;
        parsers["Dictionary"] = ReadTypedDictionary;
    }

    // _parse_array (:1644-1682), after the '[': values split by ',', a ',' allowed before the ']'.
    private static ImmutableArray<GodotValue> ReadItems(GodotValueLexer lexer)
    {
        EnterNesting(lexer);
        ImmutableArray<GodotValue>.Builder items = ImmutableArray.CreateBuilder<GodotValue>();
        GodotToken token = NextIn(lexer, ArrayEof);
        while (token.Type != GodotTokenType.BracketClose)
        {
            items.Add(ParseValue(lexer, token));
            token = AfterElement(lexer, GodotTokenType.BracketClose, "Expected ','", ArrayEof);
        }

        lexer.Depth--;
        return items.ToImmutable();
    }

    // _parse_dictionary (:1684-1748), after the '{': key ':' value pairs split by ',', a ',' allowed before the '}'.
    private static ImmutableArray<GodotEntry> ReadEntries(GodotValueLexer lexer)
    {
        EnterNesting(lexer);
        ImmutableArray<GodotEntry>.Builder entries = ImmutableArray.CreateBuilder<GodotEntry>();
        GodotToken token = NextIn(lexer, DictionaryEof);
        while (token.Type != GodotTokenType.CurlyBracketClose)
        {
            GodotValue key = ParseValue(lexer, token);
            Expect(lexer, GodotTokenType.Colon, "Expected ':'");
            entries.Add(new GodotEntry(key, ParseValue(lexer, NextIn(lexer, DictionaryEof))));
            token = AfterElement(lexer, GodotTokenType.CurlyBracketClose, "Expected '}' or ','", DictionaryEof);
        }

        lexer.Depth--;
        return entries.ToImmutable();
    }

    // After an element: the closing token, or a ',' and the token after it (which may be the closing token).
    private static GodotToken AfterElement(GodotValueLexer lexer, GodotTokenType close, string reason, string eof)
    {
        GodotToken token = NextIn(lexer, eof);
        if (token.Type == GodotTokenType.Comma)
        {
            return NextIn(lexer, eof);
        }

        return token.Type == close ? token : throw Error(reason, token);
    }

    // The containers and Object check is_eof() before each element's token (:1649-1652, :1690-1693, :1036-1039). It is
    // true only once a read has reached the end, so a TK_EOF sought after a ',' or after whitespace goes on to the
    // element's own check: parse_value's "Expected value" or the separator's error.
    private static GodotToken NextIn(GodotValueLexer lexer, string eof) => lexer.ReadPastEnd ? throw lexer.Error(eof, lexer.Position) : lexer.Next();

    // An array, a dictionary or an object one level deeper; the caller lowers the depth when it has read it.
    private static void EnterNesting(GodotValueLexer lexer)
    {
        lexer.Depth++;
        if (lexer.Depth > MaxDepth)
        {
            throw lexer.Error($"Nesting is too deep (more than {MaxDepth} levels)", lexer.Position - 1);
        }
    }

    // :1328-1410: Array[T]([...]).
    private static GodotArray ReadTypedArray(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.BracketOpen, "Expected '['");
        (GodotTypeRef? type, bool closed) = ReadElementType(lexer, GodotTokenType.BracketClose, "Expected type identifier");
        if (!closed)
        {
            Expect(lexer, GodotTokenType.BracketClose, "Expected ']'");
        }

        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        Expect(lexer, GodotTokenType.BracketOpen, "Expected '['");
        ImmutableArray<GodotValue> items = ReadItems(lexer);
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return new GodotArray(items, type);
    }

    // :1188-1327: Dictionary[K, V]({...}).
    private static GodotDictionary ReadTypedDictionary(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.BracketOpen, "Expected '['");
        (GodotTypeRef? keyType, bool comma) = ReadElementType(lexer, GodotTokenType.Comma, "Expected type identifier for key");
        if (!comma)
        {
            Expect(lexer, GodotTokenType.Comma, "Expected ',' after key type");
        }

        (GodotTypeRef? valueType, bool closed) = ReadElementType(lexer, GodotTokenType.BracketClose, "Expected type identifier for value");
        if (!closed)
        {
            Expect(lexer, GodotTokenType.BracketClose, "Expected ']'");
        }

        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        Expect(lexer, GodotTokenType.CurlyBracketOpen, "Expected '{'");
        ImmutableArray<GodotEntry> entries = ReadEntries(lexer);
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return new GodotDictionary(entries, keyType, valueType);
    }

    // A container's element type: a name, or a script reference read as a value. The bare class name Resource meets
    // the token after it instead of '(', and Godot takes it as the class (:1221-1227, :1267-1273, :1358-1364); the
    // returned flag says that token was the one that ends the type. Variant and Nil leave the side untyped.
    private static (GodotTypeRef? Type, bool EndConsumed) ReadElementType(GodotValueLexer lexer, GodotTokenType end, string reason)
    {
        GodotToken name = Expect(lexer, GodotTokenType.Identifier, reason);
        if (!ReferenceNames.TryGetValue(name.Text, out Func<GodotValueLexer, GodotValue>? readReference))
        {
            return (UntypedNames.Contains(name.Text) ? null : new GodotTypeRef(name.Text), false);
        }

        GodotToken open = lexer.Next();
        if (open.Type == GodotTokenType.ParenthesisOpen)
        {
            return (new GodotTypeRef(readReference(lexer)), false);
        }

        bool rescued = name.Text == "Resource" && open.Type == end;
        return rescued ? (new GodotTypeRef(name.Text), true) : throw Error("Expected '('", open);
    }
}
