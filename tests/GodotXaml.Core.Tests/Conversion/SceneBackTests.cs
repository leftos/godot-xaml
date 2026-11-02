using System.Collections.Immutable;
using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .xscn scenes to TscnDocument, written with TscnWriter and compared with what Godot 4.7.2 wrote.
public sealed partial class SceneBackTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    public static TheoryData<string> Fixtures =>
        new(
            "connections",
            "ext_resource",
            "groups",
            "inherited",
            "instanced",
            "lone_root",
            "multiline",
            "nested",
            "placeholder",
            "script_node",
            "sub_resources",
            "unique_name"
        );

    // Ids normalised on both sides, and properties sorted by name, since the .xscn's order is alphabetical and Godot's is
    // its property list's.
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsXscnToTheGodotScene(string fixture)
    {
        TscnDocument expected = TscnReader.Read(ReadFixture(fixture + ".tscn"));
        ConversionOptions options = Options with { ExtResourceTypes = new TypesOf(expected) };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse(Inputs[fixture]), Metadata, options);

        Assert.Empty(back.Issues);
        Assert.Equal(Comparable(expected), Comparable(back.Document));
    }

    [Fact]
    public void InstanceIsANodeWithInstanceAndNoType()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><x:Instance Name="Inst" Scene="res://inner.tscn" /></Node>""");

        TscnNode inst = scene.Nodes[1];
        Assert.Equal(("Inst", null, "."), (inst.Name, inst.Type, inst.Parent));
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, ExtId(scene, "res://inner.tscn")), inst.Instance);
    }

    [Fact]
    public void InheritedRootHasInstanceAndNoType()
    {
        TscnDocument scene = Back($$"""<x:Instance {{X}} Name="Root" Scene="res://base.tscn"><Node Name="Extra" /></x:Instance>""");

        TscnNode root = scene.Nodes[0];
        Assert.Equal(("Root", null, null), (root.Name, root.Type, root.Parent));
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, ExtId(scene, "res://base.tscn")), root.Instance);
        Assert.Equal(".", scene.Nodes[1].Parent);
    }

    [Fact]
    public void OverrideIsANodeWithParentPathThroughTheInstance()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A/B"><x:Prop Name="process_priority" Value="3" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        TscnNode node = scene.Nodes[2];
        Assert.Equal(("B", "Inst/A", null, null), (node.Name, node.Parent, node.Type, node.Instance));
        Assert.Equal(new TscnProperty("process_priority", new GodotInt(3)), Assert.Single(node.Properties));
    }

    [Fact]
    public void EmptyOverrideWritesNoSectionButItsChildrenUseItsPath()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A"><Node Name="Added" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        Assert.Equal([("Root", null), ("Inst", "."), ("Added", "Inst/A")], scene.Nodes.Select(node => (node.Name, node.Parent)));
    }

    [Fact]
    public void ConnectionOnAnOverrideIsFromItsPath()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn">
                <x:Override Path="A/B"><x:Connect Signal="ready" Method="on_ready" /></x:Override>
              </x:Instance>
            </Node>
            """
        );

        Assert.Equal(2, scene.Nodes.Length);
        TscnConnection connection = Assert.Single(scene.Connections);
        Assert.Equal(("ready", "Inst/A/B", ".", "on_ready"), (connection.Signal, connection.From, connection.To, connection.Method));
    }

    [Fact]
    public void UniqueWritesUniqueNameInOwnerFirst()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><Node Name="%Child" EditorDescription="note" /></Node>""");

        Assert.Equal(["unique_name_in_owner", "editor_description"], scene.Nodes[1].Properties.Select(property => property.Name));
        Assert.Equal(new GodotBool(true), scene.Nodes[1].Properties[0].Value);
    }

    [Fact]
    public void EditableWritesAnEditableSectionInPreOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <Node Name="Box">
                <x:Instance Name="Deep" Scene="res://inner.tscn" x:Editable="True" />
              </Node>
              <x:Instance Name="Plain" Scene="res://inner.tscn" />
              <x:Instance Name="Last" Scene="res://inner.tscn" x:Editable="True" />
            </Node>
            """
        );

        Assert.Equal(["Box/Deep", "Last"], scene.Editables.Select(editable => editable.Path));
    }

    // The bind is on the first node, yet its file comes after every node's; the instance comes before the script.
    [Fact]
    public void ExtResourcesAreNumberedInDiscoveryOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node {{X}} Name="Root">
              <x:Connect Signal="ready" Method="set_meta" Binds='["icon", Resource("res://bind.png")]' />
              <x:Instance Name="Inst" Scene="res://inner.tscn" Script="res://inst.gd" />
              <Sprite2D Name="Sprite" Texture="res://sprite.png" />
            </Node>
            """
        );

        Assert.Equal(["res://inner.tscn", "res://inst.gd", "res://sprite.png", "res://bind.png"], scene.ExtResources.Select(ext => ext.Path));
        Assert.Equal(["1_", "2_", "3_", "4_"], scene.ExtResources.Select(ext => ext.Id[..2]));
    }

    // The extension says nothing, so only the instance's declared type gives PackedScene.
    [Fact]
    public void InstanceExtResourceTypeIsPackedScene()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><x:Instance Name="Inst" Scene="res://inner.scene" /></Node>""");

        Assert.Equal("PackedScene", Assert.Single(scene.ExtResources).Type);
    }

    [Fact]
    public void HeaderUidComesFromTheOption()
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse($$"""<Node {{X}} Name="Root" />"""),
            Metadata,
            Options with
            {
                Uid = "uid://b1x2y3",
            }
        );

        Assert.Empty(back.Issues);
        Assert.Equal("uid://b1x2y3", back.Document.Header.Uid);
        Assert.StartsWith("[gd_scene format=3 uid=\"uid://b1x2y3\"]\n", TscnWriter.Write(back.Document), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepIdsReusesRecordedIdsForAScene()
    {
        XscnDocument document = Parse(
            $$"""
            <Node {{X}} Name="Root">
              <x:Instance Name="Inst" Scene="res://inner.tscn" />
              <Sprite2D Name="Sprite">
                <Sprite2D.Material><CanvasItemMaterial /></Sprite2D.Material>
              </Sprite2D>
            </Node>
            """
        );
        XscnNode root = Assert.IsType<XscnNode>(document.Root);
        XscnResource material = Assert.IsType<XscnInline>(Assert.Single(root.Children[1].Properties).Value).Resource;
        var ids = new ConversionIds(
            new Dictionary<string, string> { ["res://inner.tscn"] = "1_keep1" },
            new Dictionary<XscnResource, string>(ReferenceEqualityComparer.Instance) { [material] = "CanvasItemMaterial_keep1" }
        );

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options with { KeepIds = ids });

        Assert.Empty(back.Issues);
        Assert.Equal("1_keep1", Assert.Single(back.Document.ExtResources).Id);
        Assert.Equal("CanvasItemMaterial_keep1", Assert.Single(back.Document.SubResources).Id);
        Assert.Equal(new GodotResourceRef(GodotRefKind.External, "1_keep1"), back.Document.Nodes[1].Instance);
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, "CanvasItemMaterial_keep1"), Assert.Single(back.Document.Nodes[2].Properties).Value);
    }

    [Fact]
    public void SceneWhoseRootIsAResourceIsReported()
    {
        var document = new XscnDocument(
            XscnDocumentKind.Scene,
            [],
            [],
            [],
            new XscnResource { Type = new XscnClassType(Metadata.FindClass("Gradient")!) }
        );

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(document, Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.SceneRootNotANode, ConversionSeverity.Error);
        Assert.Empty(back.Document.Nodes);
    }

    // Under a node of the scene's own, and under another override, an override has no instance to be a path into.
    [Fact]
    public void OverrideOutsideAnInstanceIsReported()
    {
        XscnOverride inner = new() { Path = "C", Properties = [new XscnProperty("process_priority", new XscnScalar(new GodotInt(1)))] };
        XscnOverride outer = new() { Path = "B", Children = [inner] };
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnSceneType("res://inner.tscn"),
                    Name = "Inst",
                    Children = [outer],
                },
                new XscnOverride { Path = "A" },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        Assert.Equal(
            2,
            back.Issues.Count(issue => issue.Code == ConversionCodes.OverrideOutsideInstance && issue.Severity == ConversionSeverity.Error)
        );
        Assert.Equal(("C", "Inst/B"), (back.Document.Nodes[^1].Name, back.Document.Nodes[^1].Parent));
    }

    [Fact]
    public void OverrideWithoutAPathIsReportedAndNotWritten()
    {
        XscnNode root = new()
        {
            Type = new XscnClassType(Metadata.FindClass("Node")!),
            Name = "Root",
            Children =
            [
                new XscnNode
                {
                    Type = new XscnSceneType("res://inner.tscn"),
                    Name = "Inst",
                    Children =
                    [
                        new XscnOverride
                        {
                            Path = "",
                            Children = [new XscnNode { Type = new XscnClassType(Metadata.FindClass("Node")!), Name = "Lost" }],
                        },
                    ],
                },
            ],
        };

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(new XscnDocument(XscnDocumentKind.Scene, [], [], [], root), Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.OverrideWithoutPath, ConversionSeverity.Error);
        Assert.Equal(["Root", "Inst"], back.Document.Nodes.Select(node => node.Name));
    }

    [Theory]
    [InlineData("A/")]
    [InlineData("./A")]
    [InlineData("A//B")]
    [InlineData("..")]
    public void OverrideWithABadPathSegmentIsReported(string path)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse(
                $$"""
                <Node {{X}} Name="Root">
                  <x:Instance Name="Inst" Scene="res://inner.tscn">
                    <x:Override Path="{{path}}" x:Index="0"><Node Name="Lost" /></x:Override>
                  </x:Instance>
                </Node>
                """
            ),
            Metadata,
            Options
        );

        AssertIssue(back.Issues, ConversionCodes.OverrideWithoutPath, ConversionSeverity.Error);
        Assert.Equal(["Root", "Inst"], back.Document.Nodes.Select(node => node.Name));
    }

    [Fact]
    public void UniqueAndAnExplicitUniqueNameInOwnerWriteItOnce()
    {
        TscnDocument scene = Back($$"""<Node {{X}} Name="Root"><Node Name="%Child" UniqueNameInOwner="True" /></Node>""");

        Assert.Equal(new TscnProperty("unique_name_in_owner", new GodotBool(true)), Assert.Single(scene.Nodes[1].Properties));
    }

    [Fact]
    public void UniqueAndUniqueNameInOwnerFalseWarns()
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(
            Parse($$"""<Node {{X}} Name="Root"><Node Name="%Child" UniqueNameInOwner="False" /></Node>"""),
            Metadata,
            Options
        );

        AssertIssue(back.Issues, ConversionCodes.UniqueNameConflict, ConversionSeverity.Warning);
        Assert.Equal(new TscnProperty("unique_name_in_owner", new GodotBool(true)), Assert.Single(back.Document.Nodes[1].Properties));
    }

    // The tag names a class or a scene, which a placeholder's [node] has no place for.
    [Theory]
    [InlineData("""<Node2D Name="P" x:InstancePlaceholder="res://a.tscn" />""")]
    [InlineData("""<x:Instance Name="P" Scene="res://b.tscn" x:InstancePlaceholder="res://a.tscn" />""")]
    public void PlaceholderWithAnotherTagWarns(string element)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse($$"""<Node {{X}} Name="Root">{{element}}</Node>"""), Metadata, Options);

        AssertIssue(back.Issues, ConversionCodes.PlaceholderTypeDropped, ConversionSeverity.Warning);
        TscnNode node = back.Document.Nodes[1];
        Assert.Equal(("P", null, null, "res://a.tscn"), (node.Name, node.Type, node.Instance, node.InstancePlaceholder));
        Assert.Empty(back.Document.ExtResources);
    }

    private static string Comparable(TscnDocument document) =>
        TscnWriter.Write(TscnTestNormalizer.SortProperties(TscnTestNormalizer.Normalize(document)));

    private static XscnDocument Parse(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Document;
    }

    // Parses and converts with no issue.
    private static TscnDocument Back(string text)
    {
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(Parse(text), Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }

    private static string ExtId(TscnDocument scene, string path) => Assert.Single(scene.ExtResources, ext => ext.Path == path).Id;

    private static void AssertIssue(ImmutableArray<ConversionIssue> issues, string code, ConversionSeverity severity) =>
        Assert.Equal(severity, Assert.Single(issues, issue => issue.Code == code).Severity);

    private static string ReadFixture(string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));

    // The fixture's own ext_resource types, so the lookup is exact.
    private sealed class TypesOf(TscnDocument source) : IExtResourceTypes
    {
        public string? ClassOf(string path) => source.ExtResources.FirstOrDefault(ext => ext.Path == path)?.Type;
    }
}
