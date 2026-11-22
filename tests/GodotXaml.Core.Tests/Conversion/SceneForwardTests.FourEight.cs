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
    // The ext_resource of a scene the namespace tests' embedded scenes instance.
    private const string PartExt = """[ext_resource type="PackedScene" path="res://lib/part.xscn" id="1_part"]""";

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
        Assert.Equal("[sub_resource id=\"PackedScene_a1111\"] [node name=\"InnerRoot\"]", issue.Location);
    }

    // The embedded scene is built while the main tree is half built: its nodes and its location prefix are its own, so the
    // main B after it is no repeat of the embedded B and its issue is located in the main scene.
    [Fact]
    public void AMainSceneNodeAfterAnEmbeddedSceneKeepsItsOwnIssuesAndLocations()
    {
        const string text = """
            [gd_scene format=3]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node2D"]

            [node name="B" type="Node2D" parent="."]

            [resource]

            [node name="Root" type="Node"]

            [node name="A" type="Node" parent="."]
            metadata/inner = SubResource("PackedScene_a1111")

            [node name="B" type="Node" parent="." bogus=1]

            """;

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata48, Options);

        ConversionIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ConversionCodes.UnrepresentableAttribute, issue.Code);
        Assert.Equal("[node name=\"B\" parent=\".\"]", issue.Location);
    }

    [Fact]
    public void AnEmbeddedScenesMissingEmitterAndEditableAreReportedAtItsSubResource()
    {
        const string text = """
            [gd_scene format=3]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node2D"]

            [connection signal="ready" from="Missing" to="." method="show"]

            [editable path="Gone"]

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """;

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata48, Options);

        ConversionIssue emitter = Assert.Single(result.Issues, issue => issue.Code == ConversionCodes.ConnectionEmitterNotFound);
        Assert.Equal("[sub_resource id=\"PackedScene_a1111\"] [connection signal=\"ready\" from=\"Missing\"]", emitter.Location);
        ConversionIssue editable = Assert.Single(result.Issues, issue => issue.Code == ConversionCodes.EditableNotFound);
        Assert.Equal("[sub_resource id=\"PackedScene_a1111\"] [editable path=\"Gone\"]", editable.Location);
    }

    [Fact]
    public void AnUnknownSubResourceInAnEmbeddedNodeIsReportedAtItsSubResource()
    {
        const string text = """
            [gd_scene format=3]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node2D"]

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """;
        // The reader refuses an unknown id, so the reference is put in after it.
        TscnDocument read = TscnReader.Read(text);
        TscnSubResource packed = read.SubResources[0];
        TscnScene scene = packed.Scene!;
        TscnProperty material = new("material", new GodotResourceRef(GodotRefKind.Sub, "Nope"));
        TscnScene broken = scene with { Nodes = scene.Nodes.SetItem(0, scene.Nodes[0] with { Properties = [material] }) };
        TscnDocument source = read with { SubResources = read.SubResources.SetItem(0, packed with { Scene = broken }) };

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(source, Metadata48, Options);

        ConversionIssue issue = Assert.Single(result.Issues, issue => issue.Code == ConversionCodes.UnknownSubResourceId);
        Assert.Equal("[sub_resource id=\"PackedScene_a1111\"] [node name=\"InnerRoot\"]", issue.Location);
    }

    // A scene an embedded scene instances gets its folder's namespace, wherever the embedded scene is held.
    [Fact]
    public void AKeyedPackedScenesInstanceGetsItsFoldersNamespace() =>
        AssertNamespaces(
            $$"""
            [gd_scene format=3]

            {{PartExt}}

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]

            [node name="Part" parent="." instance=ExtResource("1_part")]

            [resource]

            [node name="Root" type="Node"]
            metadata/one = SubResource("PackedScene_a1111")
            metadata/two = SubResource("PackedScene_a1111")

            """,
            [("lib", "res://lib/")],
            ["<lib:part Name=\"Part\""],
            document => Assert.Contains(document.Resources, entry => entry.SceneRoot is not null)
        );

    [Fact]
    public void AnXScenePackedScenesInstanceGetsItsFoldersNamespace() =>
        AssertNamespaces(
            $$"""
            [gd_scene format=3]

            {{PartExt}}

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]

            [node name="Part" parent="." instance=ExtResource("1_part")]

            [resource]
            metadata/inner = SubResource("PackedScene_a1111")

            [node name="Root" type="Node"]

            """,
            [("lib", "res://lib/")],
            ["<lib:part Name=\"Part\""],
            document => Assert.IsType<XscnInline>(Assert.Single(document.Scene!.Properties).Value)
        );

    // An embedded scene's node holds a Resource whose property holds the PackedScene that instances the file.
    [Fact]
    public void AnInlineResourceInsideAnEmbeddedSceneGetsItsScenesNamespace() =>
        AssertNamespaces(
            $$"""
            [gd_scene format=3]

            {{PartExt}}

            [sub_resource type="PackedScene" id="PackedScene_b2222"]
            [node name="DeepRoot" type="Node"]

            [node name="Part" parent="." instance=ExtResource("1_part")]

            [sub_resource type="Resource" id="Resource_c3333"]
            metadata/scene = SubResource("PackedScene_b2222")

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]
            metadata/holder = SubResource("Resource_c3333")

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """,
            [("lib", "res://lib/")],
            ["<lib:part Name=\"Part\""],
            document => Assert.Empty(document.Resources)
        );

    [Fact]
    public void AnEmbeddedSceneInsideAnEmbeddedSceneGetsItsFoldersNamespace() =>
        AssertNamespaces(
            $$"""
            [gd_scene format=3]

            {{PartExt}}

            [sub_resource type="PackedScene" id="PackedScene_b2222"]
            [node name="DeepRoot" type="Node"]

            [node name="Part" parent="." instance=ExtResource("1_part")]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]
            metadata/deep = SubResource("PackedScene_b2222")

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            """,
            [("lib", "res://lib/")],
            ["<lib:part Name=\"Part\""],
            document => Assert.Empty(document.Resources)
        );

    [Fact]
    public void AnXresEmbeddedScenesInstanceGetsItsFoldersNamespace()
    {
        string text = $$"""
            [gd_resource type="Resource" format=3]

            {{PartExt}}

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]

            [node name="Part" parent="." instance=ExtResource("1_part")]

            [resource]
            metadata/scene = SubResource("PackedScene_a1111")

            """;

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata48, Options);

        Assert.Empty(result.Issues);
        Assert.Equal(XscnDocumentKind.Resource, result.Document.Kind);
        Assert.Equal([("lib", "res://lib/")], result.Document.Namespaces.Select(ns => (ns.Prefix, ns.Folder)));
        Assert.Contains("<lib:part Name=\"Part\"", XscnWriter.Write(result.Document, Metadata48), StringComparison.Ordinal);
    }

    // Two folders with one last segment: the main tree's takes the plain prefix although the embedded scene's node comes
    // first in tree order.
    [Fact]
    public void TheMainTreesFolderTakesAPrefixBeforeAnEmbeddedScenesFolder() =>
        AssertNamespaces(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://a/cases/main.xscn" id="1_main"]
            [ext_resource type="PackedScene" path="res://b/cases/held.xscn" id="2_held"]

            [sub_resource type="PackedScene" id="PackedScene_a1111"]
            [node name="InnerRoot" type="Node"]

            [node name="Held" parent="." instance=ExtResource("2_held")]

            [resource]

            [node name="Root" type="Node"]
            metadata/inner = SubResource("PackedScene_a1111")

            [node name="Main" parent="." instance=ExtResource("1_main")]

            """,
            [("cases", "res://a/cases/"), ("cases2", "res://b/cases/")],
            ["<cases:main Name=\"Main\"", "<cases2:held Name=\"Held\""],
            _ => { }
        );

    // Converts a scene with no issue, checks its shape, its declared namespaces and that the written text tags each
    // instance with its folder's prefix.
    private static void AssertNamespaces(string text, (string Prefix, string Folder)[] namespaces, string[] tags, Action<XscnDocument> shape)
    {
        XscnDocument document = Convert48(text);
        shape(document);
        Assert.Equal(namespaces, document.Namespaces.Select(ns => (ns.Prefix, ns.Folder)));
        string written = XscnWriter.Write(document, Metadata48);
        foreach (string tag in tags)
        {
            Assert.Contains(tag, written, StringComparison.Ordinal);
        }
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

    // The previous document's ids come from its way back, which older metadata refuses for x:Scene: the refusal still
    // answers ids, none, so the forward conversion pairs no entry by id.
    [Fact]
    public void AForwardConversionOverAPreviousFourEightDocumentUnderOlderMetadataDoesNotThrow()
    {
        string text = ReadFixture("converted48", "scene_meta.xscn")
            .Replace(
                "  <x:Scene",
                "  <x:Resources>\n    <Resource x:Key=\"Kept\" ResourceName=\"Kept\" />\n  </x:Resources>\n  <x:Scene",
                StringComparison.Ordinal
            );
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata48);
        Assert.Empty(parsed.Diagnostics);
        Assert.Single(parsed.Document.Resources);
        TscnDocument source = TscnReader.Read("[gd_scene format=3]\n\n[node name=\"Root\" type=\"Node\"]\n");

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(
            source,
            Metadata,
            Options with
            {
                Previous = new PreviousDocument(parsed.Document, Metadata),
            }
        );

        Assert.Equal("Root", Root(result.Document).Name);
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
