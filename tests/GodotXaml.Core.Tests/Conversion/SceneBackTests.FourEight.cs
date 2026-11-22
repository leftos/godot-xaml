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

    // Godot saves a scene as a .tscn, never as a .tres, and its loader refuses a node after a resource file's [resource]; the
    // refusal comes before the older Godot's, which the root's own scene does not reach.
    [Theory]
    [InlineData("4.8")]
    [InlineData("4.7")]
    public void AnXresRootPackedSceneWithASceneIsRefused(string version)
    {
        GodotMetadata metadata = version == "4.8" ? Metadata48 : TestMetadata;
        XscnParseResult parsed = XscnParser.Parse(
            $"""<PackedScene {X} x:Godot="4.8" ResourceName="P"><Node2D Name="R" /></PackedScene>""",
            XscnDocumentKind.Resource,
            Metadata48
        );
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, metadata, Options);

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal((ConversionCodes.NotAResource, ConversionSeverity.Error), (issue.Code, issue.Severity));
        Assert.Equal(
            "an .xres whose root PackedScene holds a scene cannot be written as a .tres: Godot saves a scene as a .tscn, so convert it as an .xscn",
            issue.Message
        );
        Assert.Equal("root", issue.Location);
        string written = TscnWriter.Write(back.Document);
        Assert.DoesNotContain("[node", written, StringComparison.Ordinal);
        Assert.Contains("resource_name = \"P\"", written, StringComparison.Ordinal);
    }

    // A resource whose property holds an embedded scene is refused under older metadata at the PackedScene.
    [Fact]
    public void AFourEightSceneHolderUnderFourSevenMetadataIsRefused()
    {
        XscnParseResult parsed = XscnParser.Parse(ReadFixture("converted48", "scene_holder.xres"), XscnDocumentKind.Resource, Metadata48);
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, TestMetadata, Options);

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal((ConversionCodes.NeedsNewerGodot, ConversionSeverity.Error, "PackedScene"), (issue.Code, issue.Severity, issue.Location));
        Assert.Equal(TscnKind.Resource, back.Document.Kind);
        Assert.Empty(back.Document.SubResources);
    }

    // x:Scene's site key is its own, so a root property named scene, whose resource holds one at the same property, takes
    // no seeded id from it.
    [Fact]
    public void AnXSceneInlineIdDoesNotMoveWhenARootSceneRootPropertyChanges()
    {
        string alone = XSceneInlineId(withRootScene: false);
        string beside = XSceneInlineId(withRootScene: true);

        Assert.Equal(alone, beside);
    }

    [Fact]
    public void APackedSceneWithASceneRootAndBundledDropsBundled()
    {
        var packed = new XscnResource
        {
            Type = Class48("PackedScene"),
            Properties = [new XscnProperty("_bundled", new XscnScalar(new GodotInt(0)))],
            SceneRoot = new XscnNode { Type = Class48("Node"), Name = "InnerRoot" },
        };
        var root = new XscnNode
        {
            Type = Class48("Node"),
            Name = "Root",
            Properties = [new XscnProperty("metadata/scene", new XscnInline(packed))],
        };
        var document = new XscnDocument(XscnDocumentKind.Scene, [], [], [], root, null);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata48, Options);

        ConversionIssue issue = Assert.Single(back.Issues);
        Assert.Equal((ConversionCodes.InvalidValue, ConversionSeverity.Warning), (issue.Code, issue.Severity));
        Assert.Equal("_bundled is dropped: this PackedScene holds an embedded scene, which replaces it", issue.Message);
        TscnSubResource sub = Assert.Single(back.Document.SubResources);
        Assert.Empty(sub.Properties);
        Assert.Equal("InnerRoot", Assert.Single(Assert.IsType<TscnScene>(sub.Scene).Nodes).Name);
        Assert.DoesNotContain("_bundled", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    // A _bundled the embedded scene replaces is never written, so what it refers to gets no section of its own.
    [Fact]
    public void ABundledDroppedForAnEmbeddedSceneLeavesNoUnusedSection()
    {
        var packed = new XscnResource
        {
            Type = Class48("PackedScene"),
            Properties = [new XscnProperty("_bundled", new XscnInline(Named("Unused")))],
            SceneRoot = new XscnNode { Type = Class48("Node"), Name = "InnerRoot" },
        };
        var root = new XscnNode
        {
            Type = Class48("Node"),
            Name = "Root",
            Properties = [new XscnProperty("metadata/scene", new XscnInline(packed))],
        };
        var document = new XscnDocument(XscnDocumentKind.Scene, [], [], [], root, null);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata48, Options);

        Assert.Equal("PackedScene", Assert.Single(back.Document.SubResources).Type);
        Assert.DoesNotContain("Unused", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    // The sub_resource id x:Scene's inline metadata/m gets, with or without a root property named scene holding a resource
    // whose own metadata/m holds one.
    private static string XSceneInlineId(bool withRootScene)
    {
        var holder = new XscnResource { Type = Class48("Resource"), Properties = [new XscnProperty("metadata/m", new XscnInline(Named("Held")))] };
        var root = new XscnNode
        {
            Type = Class48("Node"),
            Name = "Root",
            Properties = withRootScene ? [new XscnProperty("scene", new XscnInline(holder))] : [],
        };
        var document = new XscnDocument(XscnDocumentKind.Scene, [], [], [], root, null)
        {
            Scene = new XscnResource { Type = Class48("PackedScene"), Properties = [new XscnProperty("metadata/m", new XscnInline(Named("Own")))] },
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata48, Options);

        Assert.Empty(back.Issues);
        return Assert.IsType<GodotResourceRef>(Assert.Single(back.Document.SceneProperties).Value).Id;
    }

    private static XscnResource Named(string name) =>
        new() { Type = Class48("Resource"), Properties = [new XscnProperty("resource_name", new XscnScalar(new GodotString(name)))] };

    private static XscnClassType Class48(string name) => new(Metadata48.FindClass(name)!);

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
