using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// The .xscn declaration, the metadata and options, and the conversions the conversion tests share. The metadata and name
// helpers carry a qualifier (TestMetadata, PropertyNames), since a member named Metadata or Names would resolve to this
// project's namespaces first.
internal static class BackHelpers
{
    public const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    public static readonly GodotMetadata TestMetadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    public static readonly ConversionOptions Options = new() { Seed = 7 };

    public static IEnumerable<string> PropertyNames(ImmutableArray<TscnProperty> properties) => properties.Select(property => property.Name);

    // Parses a scene document and converts it with no issue.
    public static TscnDocument Back(string text) => Convert(XscnParser.Parse(text, XscnDocumentKind.Scene, TestMetadata));

    // Parses a resource document and converts it with no issue.
    public static TscnDocument BackResource(string text) => Convert(XscnParser.Parse(text, XscnDocumentKind.Resource, TestMetadata));

    private static TscnDocument Convert(XscnParseResult parsed)
    {
        Assert.Empty(parsed.Diagnostics);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, TestMetadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }
}
