using System.Collections.Immutable;
using System.Globalization;

namespace GodotXaml.Core.Values;

// References and Object(...). ExtResource and SubResource are read as the text resource loader reads them, one id
// (scene/resources/resource_format_text.cpp:103-134); Resource as parse_value's own branch reads it, a path, a uid
// or both (core/variant/variant_parser.cpp:1090-1187).
public static partial class GodotValueReader
{
    private const string ObjectEof = "Unexpected EOF while parsing Object()";

    // Each reference name's reader, from just after its '('.
    private static Dictionary<string, Func<GodotValueLexer, GodotValue>> BuildReferenceNames() =>
        new(StringComparer.Ordinal)
        {
            ["ExtResource"] = lexer =>
                ReadResourceId(lexer, GodotRefKind.External, "Expected number (old style sub-resource index) or String (ext-resource ID)"),
            ["SubResource"] = lexer => ReadResourceId(lexer, GodotRefKind.Sub, "Expected number (old style sub-resource index) or string"),
            ["Resource"] = ReadPathRef,
        };

    private static void AddReferences(Dictionary<string, Func<GodotValueLexer, GodotValue>> parsers)
    {
        foreach (KeyValuePair<string, Func<GodotValueLexer, GodotValue>> reference in ReferenceNames)
        {
            parsers[reference.Key] = lexer => ReadReference(lexer, reference.Value);
        }

        parsers["Object"] = ReadObject;
    }

    private static GodotValue ReadReference(GodotValueLexer lexer, Func<GodotValueLexer, GodotValue> read)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        return read(lexer);
    }

    // resource_format_text.cpp:103-134: a string id, or a legacy number id taken as its text (String id = token.value).
    private static GodotResourceRef ReadResourceId(GodotValueLexer lexer, GodotRefKind kind, string reason)
    {
        GodotToken token = lexer.Next();
        string id = token.Type switch
        {
            GodotTokenType.String => token.Text,
            GodotTokenType.Number => token.Value is GodotInt integer ? integer.Value.ToString(CultureInfo.InvariantCulture) : token.Text,
            _ => throw Error(reason, token),
        };
        Expect(lexer, GodotTokenType.ParenthesisClose, "Expected ')'");
        return new GodotResourceRef(kind, id);
    }

    // variant_parser.cpp:1125-1186: one or two strings; the one beginning uid:// is the uid.
    private static GodotPathRef ReadPathRef(GodotValueLexer lexer)
    {
        GodotToken first = Expect(lexer, GodotTokenType.String, "Expected string as argument for Resource()");
        string? uid = null;
        string? path = null;
        AddReferencePart(first, ref uid, ref path);

        GodotToken token = lexer.Next();
        if (token.Type == GodotTokenType.Comma)
        {
            AddReferencePart(Expect(lexer, GodotTokenType.String, "Expected string in Resource reference"), ref uid, ref path);
            token = lexer.Next();
        }

        return token.Type == GodotTokenType.ParenthesisClose ? new GodotPathRef(uid, path) : throw Error("Expected ')'", token);
    }

    private static void AddReferencePart(GodotToken token, ref string? uid, ref string? path)
    {
        if (token.Text.StartsWith("uid://", StringComparison.Ordinal))
        {
            uid = uid is null ? token.Text : throw Error("Two uid:// paths in one Resource reference", token);
            return;
        }

        path = string.IsNullOrEmpty(path) ? token.Text : throw Error("Two non-uid paths in one Resource reference", token);
    }

    // variant_parser.cpp:995-1089: Object(Class, "name": value, ...); the class is kept as text, never instantiated.
    private static GodotObject ReadObject(GodotValueLexer lexer)
    {
        Expect(lexer, GodotTokenType.ParenthesisOpen, "Expected '('");
        GodotToken type = Expect(lexer, GodotTokenType.Identifier, "Expected identifier with type of object");
        Expect(lexer, GodotTokenType.Comma, "Expected ',' after object type");

        EnterNesting(lexer);
        ImmutableArray<GodotProperty>.Builder properties = ImmutableArray.CreateBuilder<GodotProperty>();
        GodotToken token = NextIn(lexer, ObjectEof);
        while (token.Type != GodotTokenType.ParenthesisClose)
        {
            if (token.Type != GodotTokenType.String)
            {
                throw Error("Expected property name as string", token);
            }

            Expect(lexer, GodotTokenType.Colon, "Expected ':'");
            properties.Add(new GodotProperty(token.Text, ParseValue(lexer, NextIn(lexer, ObjectEof))));
            token = AfterElement(lexer, GodotTokenType.ParenthesisClose, "Expected '}' or ','", ObjectEof);
        }

        lexer.Depth--;
        return new GodotObject(type.Text, properties.ToImmutable());
    }
}
