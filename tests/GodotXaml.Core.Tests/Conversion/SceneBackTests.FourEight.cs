using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;
using static GodotXaml.Core.Tests.Conversion.BackHelpers;

namespace GodotXaml.Core.Tests.Conversion;

// Godot 4.8 .xscn scenes, with x:Scene and scenes embedded in PackedScene resources, to what Godot 4.8 wrote.
public sealed partial class SceneBackTests
{
    private static readonly GodotMetadata Metadata48 = GodotMetadata.LoadBundled("4.8");

    public static TheoryData<string> Fixtures48 =>
        new("scene_meta", "embedded_scene", "embedded_only", "embedded_empty", "embedded_target", "embedded_instance");

    // Ids normalised on both sides; property order is compared as Godot wrote it.
    [Theory]
    [MemberData(nameof(Fixtures48))]
    public void ConvertsAFourEightXscnToTheGodotScene(string fixture)
    {
        TscnDocument expected = TscnReader.Read(ReadFixture("tscn48", fixture + ".tscn"));

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Golden48(fixture), Metadata48, Options48(fixture, expected));

        Assert.Empty(back.Issues);
        Assert.Equal(TscnTestNormalizer.Comparable(expected), TscnTestNormalizer.Comparable(back.Document));
    }

    // An older Godot cannot load x:Scene or an embedded scene, so the way back fails rather than drop them.
    [Theory]
    [InlineData("scene_meta", "x:Scene")]
    [InlineData("embedded_scene", "x:Scene")]
    [InlineData("embedded_only", "PackedScene")]
    public void AFourEightXscnUnderFourSevenMetadataIsRefused(string fixture, string location)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Golden48(fixture), TestMetadata, Options with { Uid = "uid://refused" });

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal(ConversionCodes.NeedsNewerGodot, issue.Code);
        Assert.Equal(ConversionSeverity.Error, issue.Severity);
        Assert.Equal($"this file holds a Godot 4.8 x:Scene element or embedded scene, which Godot {TestMetadata.Version} cannot load", issue.Message);
        Assert.Equal(location, issue.Location);
        Assert.Equal(TscnKind.Scene, back.Document.Kind);
        Assert.Equal("uid://refused", back.Document.Header.Uid);
        Assert.Empty(back.Document.Nodes);
        Assert.Empty(back.Document.SubResources);
        Assert.Empty(back.Document.ExtResources);
    }

    // The 4.8 saver's walk: the embedded scene's nodes before the PackedScene is recorded, a resource after what it uses, the
    // ext_resource numbered where the embedded scene first meets it.
    [Fact]
    public void AnEmbeddedScenesIdsFollowTheSaversOrder()
    {
        TscnDocument expected = TscnReader.Read(ReadFixture("tscn48", "embedded_instance.tscn"));
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Golden48("embedded_instance"), Metadata48, Options48("embedded_instance", expected));

        Assert.Empty(back.Issues);
        TscnExtResource ext = Assert.Single(back.Document.ExtResources);
        Assert.Equal("res://cases/embedded_target.tscn", ext.Path);
        Assert.StartsWith("1_", ext.Id, StringComparison.Ordinal);
        Assert.Equal(["CanvasItemMaterial", "PackedScene", "Resource"], back.Document.SubResources.Select(sub => sub.Type));
        TscnScene embedded = Assert.IsType<TscnScene>(back.Document.SubResources[1].Scene);
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, ext.Id), Assert.Single(embedded.Nodes, node => node.Name == "Held").Instance);
        TscnNode tinted = Assert.Single(embedded.Nodes, node => node.Name == "Tinted");
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, back.Document.SubResources[0].Id), Assert.Single(tinted.Properties).Value);
    }

    // The embedded root's path is "." as the main root's is; its site keys are prefixed with its holder's, so a resource at
    // the same property of both roots does not take the main one's seeded id.
    [Fact]
    public void AnEmbeddedSceneRootsSiteKeyDoesNotCollideWithTheMainRoots()
    {
        const string inner = """
                <Node Name="InnerRoot">
                  <Node.Metadata.m>
                    <Resource ResourceName="Inner" />
                  </Node.Metadata.m>
                </Node>
            """;

        string withInner = MainIdOf(EmbeddingScene(inner));
        string withoutInner = MainIdOf(EmbeddingScene("""<Node Name="InnerRoot" />"""));

        Assert.Equal(withoutInner, withInner);
    }

    [Fact]
    public void ASceneWithOnlyAnEmbeddedSceneGetsAnEmptyResourceSection()
    {
        TscnDocument expected = TscnReader.Read(ReadFixture("tscn48", "embedded_only.tscn"));
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Golden48("embedded_only"), Metadata48, Options48("embedded_only", expected));

        Assert.Empty(back.Issues);
        Assert.True(back.Document.HasSceneResourceSection);
        Assert.Empty(back.Document.SceneProperties);
        Assert.Contains("\n[resource]\n", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    // A 4.7 PackedScene keeps its nodes in _bundled, and Godot 4.7 writes no [resource] section for a scene.
    [Fact]
    public void AFourSevenSceneWithAPackedSceneSubResourceGetsNoResourceSection()
    {
        var packed = new XscnResource
        {
            Type = new XscnClassType(TestMetadata.FindClass("PackedScene")!),
            Properties = [new XscnProperty("_bundled", new XscnScalar(new GodotInt(0)))],
        };
        var root = new XscnNode
        {
            Type = new XscnClassType(TestMetadata.FindClass("Node")!),
            Name = "Root",
            Properties = [new XscnProperty("metadata/scene", new XscnInline(packed))],
        };
        var document = new XscnDocument(XscnDocumentKind.Scene, [], [], [], root, null);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, TestMetadata, Options);

        Assert.Empty(back.Issues);
        Assert.Equal("PackedScene", Assert.Single(back.Document.SubResources).Type);
        Assert.False(back.Document.HasSceneResourceSection);
        Assert.DoesNotContain("[resource]", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    // A scene whose root holds a Resource at metadata/m and a PackedScene embedding the given root.
    private static string EmbeddingScene(string innerRoot) =>
        $$"""
            <Node {{X}} x:Godot="4.8" Name="Root">
              <Node.Metadata.inner>
                <PackedScene>
            {{innerRoot}}
                </PackedScene>
              </Node.Metadata.inner>
              <Node.Metadata.m>
                <Resource ResourceName="Main" />
              </Node.Metadata.m>
            </Node>
            """;

    // The sub_resource id the main root's metadata/m gets, with ids minted from the seed.
    private static string MainIdOf(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata48);
        Assert.Empty(parsed.Diagnostics);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata48, Options);
        Assert.Empty(back.Issues);
        TscnProperty main = Assert.Single(back.Document.Nodes[0].Properties, property => property.Name == "metadata/m");
        return Assert.IsType<GodotResourceRef>(main.Value).Id;
    }

    private static XscnDocument Golden48(string fixture)
    {
        XscnParseResult parsed = XscnParser.Parse(ReadFixture("converted48", fixture + ".xscn"), XscnDocumentKind.Scene, Metadata48);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    // The options ConvertsXscnToTheGodotScene converts with.
    private static ConversionOptions Options48(string fixture, TscnDocument expected) =>
        Options with
        {
            ExtResourceTypes = new TypesOf(expected),
            Uid = expected.Header.Uid,
            SceneFiles = FixtureScenes.Instance,
            SourceKey = $"res://cases/{fixture}.xscn",
        };
}
