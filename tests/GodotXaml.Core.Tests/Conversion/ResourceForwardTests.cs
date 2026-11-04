using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .tres to XscnDocument: Godot's resource fixtures against hand-written goldens, and the goldens against the format.
public sealed class ResourceForwardTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { ScriptClassOf = path => path == "res://tagged.gd" ? "TaggedResource" : null };

    public static TheoryData<string> Resources =>
        new(
            "animation_nodes",
            "animation_tracks",
            "audio_resources",
            "gradient_texture",
            "material",
            "packed_bytes",
            "quoted_name",
            "replication_properties",
            "script_class",
            "skeleton_modification_singles",
            "theme_font_resources",
            "tile_set_layers",
            "tile_set_patterns",
            "visual_shader_canvas_item",
            "visual_shader_nodes",
            "visual_shader_particles",
            "visual_shader_sky",
            "visual_shader_types"
        );

    [Theory]
    [MemberData(nameof(Resources))]
    public void ConvertsGodotResourceToItsGoldenXres(string name)
    {
        TscnDocument source = TscnReader.Read(ReadFixture("scenes", name + ".tres"));

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(source, Metadata, Options);

        Assert.Empty(result.Issues);
        Assert.Equal(ReadFixture("converted", name + ".xres"), XscnWriter.Write(result.Document, Metadata));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public void GoldenXresIsCanonical(string name)
    {
        string golden = ReadFixture("converted", name + ".xres");

        XscnParseResult parsed = XscnParser.Parse(golden, XscnDocumentKind.Resource, Metadata);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(golden, XscnWriter.Write(parsed.Document, Metadata));
    }

    internal static string ReadFixture(string folder, string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", folder, file)));
}
