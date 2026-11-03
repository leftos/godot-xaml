using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A file whose loader names the type it loads comes back with that type, from the extension alone.
public sealed class ExtensionTypeTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    // A property with no declared type, so only the extension says what the file holds.
    [Theory]
    [InlineData("res://d.json", "JSON")]
    [InlineData("res://t.po", "Translation")]
    [InlineData("res://t.mo", "Translation")]
    [InlineData("res://e.gdextension", "GDExtension")]
    [InlineData("res://v.ogv", "VideoStreamTheora")]
    [InlineData("res://D.JSON", "JSON")]
    public void TheFilesExtensionNamesItsType(string path, string type)
    {
        XscnParseResult parsed = XscnParser.Parse($$"""<Node {{X}} Name="Root" shader='Resource("{{path}}")' />""", XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal(type, Assert.Single(back.Document.ExtResources).Type);
    }
}
