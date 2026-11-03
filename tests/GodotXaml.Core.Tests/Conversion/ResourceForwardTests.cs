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

    public static TheoryData<string> Resources => new("gradient_texture", "material", "packed_bytes", "quoted_name", "script_class");

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

    private static string ReadFixture(string folder, string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", folder, file)));
}
