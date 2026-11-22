using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// Godot 4.8 scenes to XscnDocument: a scene's own [resource] properties become x:Scene, and a PackedScene sub-resource's
// own nodes become its resource's scene root, with the references inside them counted.
public sealed partial class SceneForwardTests
{
    private static readonly GodotMetadata Metadata48 = GodotMetadata.LoadBundled("4.8");

    public static TheoryData<string> Scenes48 =>
        new("scene_meta", "embedded_scene", "embedded_only", "embedded_empty", "embedded_target", "embedded_instance");

    [Theory]
    [MemberData(nameof(Scenes48))]
    public void ConvertsAFourEightSceneToItsGoldenXscn(string name)
    {
        TscnDocument source = TscnReader.Read(ReadFixture("tscn48", name + ".tscn"));

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(source, Metadata48, Options);

        Assert.Empty(result.Issues);
        Assert.Equal(ReadFixture("converted48", name + ".xscn"), XscnWriter.Write(result.Document, Metadata48));
    }

    [Theory]
    [MemberData(nameof(Scenes48))]
    public void AFourEightGoldenXscnIsCanonical(string name)
    {
        string golden = ReadFixture("converted48", name + ".xscn");

        XscnParseResult parsed = XscnParser.Parse(golden, XscnDocumentKind.Scene, Metadata48);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(golden, XscnWriter.Write(parsed.Document, Metadata48));
    }

    [Fact]
    public void AFourEightSceneKeepsItsOwnProperties()
    {
        XscnDocument document = Convert48(ReadFixture("tscn48", "scene_meta.tscn"));

        XscnResource scene = Assert.IsType<XscnResource>(document.Scene);
        Assert.Equal("PackedScene", Assert.IsType<XscnClassType>(scene.Type).Class.Name);
        Assert.Null(scene.Key);
        Assert.Equal(
            [
                new XscnProperty("resource_name", new XscnScalar(new GodotString("Hello"))),
                new XscnProperty("metadata/answer", new XscnScalar(new GodotInt(42))),
            ],
            scene.Properties
        );
        XscnNode root = Root(document);
        Assert.Equal("Root", root.Name);
        Assert.Equal("Child", Assert.IsType<XscnNode>(Assert.Single(root.Children)).Name);
    }

    [Fact]
    public void AnEmbeddedSceneBecomesTheSceneRootOfItsPackedScene()
    {
        XscnDocument document = Convert48(ReadFixture("tscn48", "embedded_scene.tscn"));

        XscnResource scene = Assert.IsType<XscnResource>(document.Scene);
        Assert.Equal(["metadata/inner", "metadata/other"], scene.Properties.Select(property => property.GodotName));
        XscnNode inner = Assert.IsType<XscnNode>(ResourceOf(document, scene.Properties[0].Value).SceneRoot);
        Assert.Equal("InnerRoot", inner.Name);
        Assert.Equal("Node2D", Assert.IsType<XscnClassType>(inner.Type).Class.Name);
        XscnNode child = Assert.IsType<XscnNode>(Assert.Single(inner.Children));
        Assert.Equal("InnerChild", child.Name);
        XscnConnection connection = Assert.Single(child.Connections);
        Assert.Equal(("visibility_changed", ".", "show"), (connection.Signal, connection.To, connection.Method));
        XscnResource other = ResourceOf(document, scene.Properties[1].Value);
        Assert.Equal(new XscnProperty("resource_name", new XscnScalar(new GodotString("Other"))), Assert.Single(other.Properties));
    }

    [Fact]
    public void AnEmbeddedSceneOnlyAPropertyUsesGetsNoXScene()
    {
        XscnDocument document = Convert48(ReadFixture("tscn48", "embedded_only.tscn"));

        Assert.Null(document.Scene);
        XscnResource packed = ResourceOf(document, Property(Root(document), "metadata/inner"));
        Assert.Equal("PackedScene", Assert.IsType<XscnClassType>(packed.Type).Class.Name);
        Assert.Equal("InnerRoot", Assert.IsType<XscnNode>(packed.SceneRoot).Name);
    }

    [Fact]
    public void AnUnpackedPackedSceneHasNoSceneRoot()
    {
        XscnDocument document = Convert48(ReadFixture("tscn48", "embedded_empty.tscn"));

        Assert.Null(document.Scene);
        XscnResource packed = ResourceOf(document, Property(Root(document), "metadata/empty"));
        Assert.Equal("PackedScene", Assert.IsType<XscnClassType>(packed.Type).Class.Name);
        Assert.Null(packed.SceneRoot);
    }

    [Fact]
    public void AnEmbeddedScenesInstanceKeepsItsExtResourceAndNamespace()
    {
        XscnDocument document = Convert48(ReadFixture("tscn48", "embedded_instance.tscn"));

        XscnNode inner = Assert.IsType<XscnNode>(ResourceOf(document, Property(Root(document), "metadata/inner")).SceneRoot);
        XscnNode held = Assert.IsType<XscnNode>(Assert.Single(inner.Children, child => child is XscnNode { Name: "Held" }));
        Assert.Equal(new XscnSceneType("res://cases/embedded_target.tscn"), held.Type);
        Assert.True(held.Editable);
        XscnOverride part = Assert.IsType<XscnOverride>(Assert.Single(held.Children));
        Assert.Equal(("Part", 0), (part.Path, part.Index));
        Assert.Equal("position", Assert.Single(part.Properties).GodotName);
        XscnNode tinted = Assert.IsType<XscnNode>(Assert.Single(inner.Children, child => child is XscnNode { Name: "Tinted" }));
        XscnResource material = ResourceOf(document, Property(tinted, "material"));
        Assert.Equal("CanvasItemMaterial", Assert.IsType<XscnClassType>(material.Type).Class.Name);
        Assert.Contains(document.Namespaces, ns => ns.Folder == "res://cases/");
    }

    // Every node, connection and editable of every scene of the file reaches the .xscn: the main tree's and each scene
    // root's, an override counting as a node.
    [Theory]
    [MemberData(nameof(Scenes48))]
    public void AFourEightConversionKeepsEveryNodeConnectionAndEditable(string name)
    {
        TscnDocument source = TscnReader.Read(ReadFixture("tscn48", name + ".tscn"));

        XscnDocument document = Convert48(source);

        ImmutableArray<XscnNodeElement> elements = [.. TreeRoots(document).SelectMany(Elements)];
        Assert.Equal(
            source.Scenes.SelectMany(scene => scene.Nodes).Select(node => node.Name).Order(StringComparer.Ordinal),
            elements.Select(NameOf).Order(StringComparer.Ordinal)
        );
        Assert.Equal(source.Scenes.Sum(scene => scene.Connections.Length), elements.Sum(element => element.Connections.Length));
        Assert.Equal(source.Scenes.Sum(scene => scene.Editables.Length), elements.Count(element => element.Editable));
    }

    [Fact]
    public void AnEmbeddedScenesExtraAttributeIsReportedAtItsSubResource()
    {
        const string text = """
            [gd_scene format=3]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node2D" bogus=1]

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """;

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata48, Options);

        ConversionIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ConversionCodes.UnrepresentableAttribute, issue.Code);
        Assert.StartsWith("[sub_resource id=\"PackedScene_a1111\"]", issue.Location, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtResourceUsedOnlyInAnEmbeddedSceneIsNotUnused()
    {
        const string text = """
            [gd_scene format=3]

            [ext_resource type="Texture2D" path="res://art/icon.png" id="1_icon"]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Sprite2D"]
            texture = ExtResource("1_icon")

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """;

        XscnDocument document = Convert48(text);

        XscnNode inner = Assert.IsType<XscnNode>(ResourceOf(document, Property(Root(document), "metadata/inner")).SceneRoot);
        Assert.Equal(new XscnFile("res://art/icon.png"), Property(inner, "texture"));
    }

    // A sub-resource one embedded node uses once goes inline there; uncounted, it would be unused and keyed.
    [Fact]
    public void ASubResourceUsedOnlyInAnEmbeddedSceneIsCountedOnce()
    {
        const string text = """
            [gd_scene format=3]

            [sub_resource type="CanvasItemMaterial" id="CanvasItemMaterial_a1111"]
            blend_mode = 1

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node2D"]
            material = SubResource("CanvasItemMaterial_a1111")

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """;

        XscnDocument document = Convert48(text);

        XscnNode inner = Assert.IsType<XscnNode>(ResourceOf(document, Property(Root(document), "metadata/inner")).SceneRoot);
        XscnInline material = Assert.IsType<XscnInline>(Property(inner, "material"));
        Assert.Equal("CanvasItemMaterial", Assert.IsType<XscnClassType>(material.Resource.Type).Class.Name);
        Assert.DoesNotContain(document.Resources, entry => entry.Type is XscnClassType { Class.Name: "CanvasItemMaterial" });
    }

    [Fact]
    public void ASubResourceCycleThroughAnEmbeddedSceneIsFound()
    {
        TscnDocument document = TscnReader.Read(
            """
            [gd_scene format=3]

            [sub_resource type="Resource" id="Resource_a1111"]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]
            metadata/next = SubResource("Resource_a1111")

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """
        );
        TscnProperty back = new("metadata/back", new GodotResourceRef(GodotRefKind.Sub, "PackedScene_a1111"));
        TscnDocument cyclic = document with
        {
            SubResources = document.SubResources.SetItem(0, document.SubResources[0] with { Properties = [back] }),
        };

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(cyclic, Metadata48, Options);

        Assert.Equal(2, result.Issues.Count(issue => issue.Code == ConversionCodes.SubResourceCycle && issue.Severity == ConversionSeverity.Error));
    }

    private static XscnDocument Convert48(string text) => Convert48(TscnReader.Read(text));

    private static XscnDocument Convert48(TscnDocument source)
    {
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(source, Metadata48, Options);
        Assert.Empty(result.Issues);
        Assert.Equal(XscnDocumentKind.Scene, result.Document.Kind);
        return result.Document;
    }

    private static XscnValue Property(XscnNodeElement element, string name) =>
        Assert.Single(element.Properties, property => property.GodotName == name).Value;

    // The resource a value names: an inline resource, or the x:Resources entry of its key.
    internal static XscnResource ResourceOf(XscnDocument document, XscnValue value) =>
        value switch
        {
            XscnInline inline => inline.Resource,
            XscnKeyRef key => Assert.Single(document.Resources, entry => entry.Key == key.Key),
            _ => throw new Xunit.Sdk.XunitException($"{value} names no resource"),
        };

    // The main tree's root, then the root of every scene a resource of the document holds.
    private static IEnumerable<XscnNodeElement> TreeRoots(XscnDocument document)
    {
        IEnumerable<XscnResource> resources = document.Resources.Concat(document.Scene is { } own ? [own] : []);
        if (document.Root is XscnNodeElement main)
        {
            resources = resources.Concat(Elements(main).SelectMany(element => Inline(element.Properties)));
            yield return main;
        }

        foreach (XscnNodeElement root in resources.SelectMany(SceneRoots))
        {
            yield return root;
        }
    }

    private static IEnumerable<XscnNodeElement> SceneRoots(XscnResource resource)
    {
        IEnumerable<XscnResource> inner = Inline(resource.Properties);
        if (resource.SceneRoot is { } root)
        {
            yield return root;
            inner = inner.Concat(Elements(root).SelectMany(element => Inline(element.Properties)));
        }

        foreach (XscnNodeElement nested in inner.SelectMany(SceneRoots))
        {
            yield return nested;
        }
    }

    private static IEnumerable<XscnResource> Inline(ImmutableArray<XscnProperty> properties) =>
        properties.Select(property => property.Value).OfType<XscnInline>().Select(inline => inline.Resource);

    private static IEnumerable<XscnNodeElement> Elements(XscnNodeElement element) => [element, .. element.Children.SelectMany(Elements)];

    private static string NameOf(XscnNodeElement element) =>
        element switch
        {
            XscnNode node => node.Name,
            XscnOverride change => change.Path[(change.Path.LastIndexOf('/') + 1)..],
            _ => throw new Xunit.Sdk.XunitException($"{element} is neither a node nor an override"),
        };
}
