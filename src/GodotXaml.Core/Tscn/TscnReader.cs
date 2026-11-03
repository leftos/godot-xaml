using System.Collections.Immutable;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tscn;

/// <summary>
/// Reads Godot's .tscn and .tres text into a <see cref="TscnDocument"/>, as ResourceLoaderText reads it
/// (scene/resources/resource_format_text.cpp) over VariantParser's tag and value grammar.
/// </summary>
public static partial class TscnReader
{
    // resource_format_text.cpp:1139-1183: the header tags and the kind each starts.
    private static readonly Dictionary<string, TscnKind> HeaderKinds = new(StringComparer.Ordinal)
    {
        ["gd_scene"] = TscnKind.Scene,
        ["gd_resource"] = TscnKind.Resource,
    };

    /// <summary>Reads a whole file's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="TscnParseException">The text is not one Godot's loader reads, or refers to an id no earlier section defines.</exception>
    public static TscnDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            var scanner = new TscnScanner(WithoutBom(text));
            var builder = new DocumentBuilder(ReadHeader(scanner));
            for (TscnItem? item = scanner.Next(); item is not null; item = scanner.Next())
            {
                builder.Add(item);
            }

            return builder.Build(scanner);
        }
        catch (GodotValueParseException error)
        {
            throw new TscnParseException(error.Reason, error.Line, error.Column, error);
        }
    }

    /// <summary>Reads only a file's header, for a caller that wants its uid and not the document.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The header's uid, or null when it has none.</returns>
    /// <exception cref="TscnParseException">The header is not one Godot's loader reads.</exception>
    public static string? ReadUid(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return ReadHeader(new TscnScanner(WithoutBom(text))).Header.Uid;
        }
        catch (GodotValueParseException error)
        {
            throw new TscnParseException(error.Reason, error.Line, error.Column, error);
        }
    }

    // resource_format_text.cpp:1127-1136: the loader skips a leading UTF-8 BOM.
    private static string WithoutBom(string text) => text.Length > 0 && text[0] == (char)0xFEFF ? text[1..] : text;

    private static (TscnKind Kind, TscnHeader Header) ReadHeader(TscnScanner scanner)
    {
        TscnTag tag = scanner.Next() switch
        {
            null => throw TscnScanner.Error("Unexpected end of file", 1, 1),
            TscnTag found => found,
            TscnItem other => throw TscnScanner.Error("Expected '['", other.Line, other.Column),
        };
        TscnKind kind = HeaderKinds.TryGetValue(tag.Name, out TscnKind found2)
            ? found2
            : throw TscnScanner.Error($"Unrecognized file type '{tag.Name}'", tag.Line, tag.Column);

        var fields = new TscnTagFields(tag);
        fields.Value("load_steps");
        string? type = kind == TscnKind.Resource ? fields.Required("type", "Missing 'type' field in 'gd_resource' tag") : null;
        var header = new TscnHeader
        {
            Type = type,
            ScriptClass = fields.String("script_class"),
            Format = CheckFormat(fields.Int("format"), tag),
            Uid = fields.String("uid"),
        };
        return (kind, header with { Extra = fields.Rest() });
    }

    // resource_format_text.cpp:1152-1157.
    private static int? CheckFormat(int? format, TscnTag tag) =>
        format > 4 ? throw TscnScanner.Error("Saved with newer format version", tag.Line, tag.Column) : format;

    private static ImmutableArray<string> Strings(TscnTagFields fields, string name) =>
        fields.Value(name) switch
        {
            null => [],
            GodotArray array when array.Items.All(item => item is GodotString or GodotStringName) => [.. array.Items.Select(TextOf)],
            GodotPackedStrings packed => packed.Items,
            _ => throw fields.WrongType(name, "an array of strings"),
        };

    private static string TextOf(GodotValue value) => value is GodotString text ? text.Value : ((GodotStringName)value).Value;

    private static ImmutableArray<GodotValue> Binds(TscnTagFields fields) =>
        fields.Value("binds") switch
        {
            null => [],
            GodotArray array => array.Items,
            _ => throw fields.WrongType("binds", "an array"),
        };
}
