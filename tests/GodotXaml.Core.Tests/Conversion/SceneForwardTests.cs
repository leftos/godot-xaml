using System.Collections.Immutable;
using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Values;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .tscn to XscnDocument: Godot's scene fixtures against hand-written goldens, and the model's facts on small scenes.
public sealed partial class SceneForwardTests
{
    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new();

    public static TheoryData<string> Scenes =>
        new(
            "animation_player_library",
            "bone_chain_settings",
            "connections",
            "ext_resource",
            "groups",
            "inherited",
            "instanced",
            "instanced_order",
            "lone_root",
            "multiline",
            "nested",
            "order_inner",
            "placeholder",
            "placeholder_order",
            "property_order",
            "script_node",
            "skeleton_bones",
            "sub_resources",
            "two_bone_ik_modifiers",
            "unique_name"
        );

    [Theory]
    [MemberData(nameof(Scenes))]
    public void ConvertsGodotSceneToItsGoldenXscn(string name)
    {
        TscnDocument source = TscnReader.Read(ReadFixture("scenes", name + ".tscn"));

        ConversionResult<XscnDocument> result = TscnToXscn.Convert(source, Metadata, Options);

        Assert.Empty(result.Issues);
        Assert.Equal(ReadFixture("converted", name + ".xscn"), XscnWriter.Write(result.Document, Metadata));
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void GoldenXscnIsCanonical(string name)
    {
        string golden = ReadFixture("converted", name + ".xscn");

        XscnParseResult parsed = XscnParser.Parse(golden, XscnDocumentKind.Scene, Metadata);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(golden, XscnWriter.Write(parsed.Document, Metadata));
    }

    [Fact]
    public void InstanceBecomesASceneTypedElement()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" uid="uid://c1butt0n" path="res://ui/button.tscn" id="1_b0tt0"]

            [node name="Root" type="Node"]

            [node name="Press" parent="." instance=ExtResource("1_b0tt0")]

            """
        );

        XscnNode press = Assert.IsType<XscnNode>(Assert.Single(Root(document).Children));
        Assert.Equal("Press", press.Name);
        Assert.Equal(new XscnSceneType("res://ui/button.tscn"), press.Type);
        Assert.Equal(new XscnUid("res://ui/button.tscn", "uid://c1butt0n"), Assert.Single(document.Uids));
    }

    [Fact]
    public void InheritedRootIsTheBaseScene()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://base.tscn" id="1_b4s3s"]

            [node name="Root" instance=ExtResource("1_b4s3s")]

            """
        );

        XscnNode root = Root(document);
        Assert.Equal("Root", root.Name);
        Assert.Equal(new XscnSceneType("res://base.tscn"), root.Type);
        Assert.Empty(root.Children);
        Assert.Equal(new XscnNamespace("res", "res://"), Assert.Single(document.Namespaces));
    }

    [Fact]
    public void NodeWithNoTypeUnderAnInstanceIsAnOverride()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [node name="A" parent="Inst" index="0"]
            visible = false

            """
        );

        XscnOverride change = Assert.IsType<XscnOverride>(Assert.Single(Instance(document).Children));
        Assert.Equal("A", change.Path);
        Assert.Equal(0, change.Index);
        Assert.Equal(new XscnProperty("visible", new XscnScalar(new GodotBool(false))), Assert.Single(change.Properties));
    }

    [Fact]
    public void NodeAddedInsideAnInstanceNestsInASynthesisedOverride()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [node name="Added" type="Node" parent="Inst/A"]

            [node name="Deep" type="Node" parent="Inst/C/D"]

            """
        );

        ImmutableArray<XscnNodeElement> overrides = Instance(document).Children;
        Assert.Equal(["A", "C/D"], overrides.Select(element => Assert.IsType<XscnOverride>(element).Path));
        Assert.All(overrides, element => Assert.Empty(element.Properties));
        Assert.Equal("Added", Assert.IsType<XscnNode>(Assert.Single(overrides[0].Children)).Name);
        Assert.Equal("Deep", Assert.IsType<XscnNode>(Assert.Single(overrides[1].Children)).Name);
    }

    [Fact]
    public void ConnectionFromInsideAnInstanceSitsOnItsOverride()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [node name="A" parent="Inst"]
            visible = false

            [connection signal="ready" from="Inst/A" to="." method="on_a_ready"]
            [connection signal="ready" from="Inst/B" to="." method="on_b_ready"]

            """
        );

        ImmutableArray<XscnNodeElement> overrides = Instance(document).Children;
        Assert.Equal(["A", "B"], overrides.Select(element => Assert.IsType<XscnOverride>(element).Path));
        Assert.Equal("on_a_ready", Assert.Single(overrides[0].Connections).Method);
        Assert.Single(overrides[0].Properties);
        Assert.Equal("on_b_ready", Assert.Single(overrides[1].Connections).Method);
        Assert.Empty(overrides[1].Properties);
        Assert.Empty(Instance(document).Connections);
    }

    [Fact]
    public void ConnectionKeepsFlagsBindsAndIdPaths()
    {
        const string section =
            "[connection signal=\"ready\" from=\"Emitter\" to=\".\" method=\"go\" flags=7 from_uid_path=PackedInt32Array(5, 6) "
            + "to_uid_path=PackedInt32Array(7) unbinds=1 binds= [SubResource(\"StyleBoxFlat_a1b2c\")]]";
        XscnDocument document = Convert(
            $"""
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]

            [node name="Root" type="Node"]

            [node name="Emitter" type="Node" parent="."]

            {section}

            """
        );

        XscnConnection connection = Assert.Single(Assert.Single(Root(document).Children).Connections);
        Assert.Equal(("ready", ".", "go", 7, 1), (connection.Signal, connection.To, connection.Method, connection.Flags, connection.Unbinds));
        Assert.Equal([5, 6], connection.FromUidPath);
        Assert.Equal([7], connection.ToUidPath);
        Assert.Equal("StyleBoxFlat1", Assert.Single(document.Resources).Key);
        Assert.Equal(new GodotResourceRef(GodotRefKind.Sub, "StyleBoxFlat1"), Assert.Single(connection.Binds));
    }

    [Fact]
    public void UniqueNameInOwnerTrueBecomesUnique()
    {
        XscnNode child = OnlyChild(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Press" type="Node" parent="."]
            unique_name_in_owner = true

            """
        );

        Assert.True(child.Unique);
        Assert.Empty(child.Properties);
    }

    [Fact]
    public void UniqueNameInOwnerFalseStaysAProperty()
    {
        XscnNode child = OnlyChild(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Press" type="Node" parent="."]
            unique_name_in_owner = false

            """
        );

        Assert.False(child.Unique);
        Assert.Equal(new XscnProperty("unique_name_in_owner", new XscnScalar(new GodotBool(false))), Assert.Single(child.Properties));
    }

    [Fact]
    public void EditableMarksTheInstanceElement()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [node name="Plain" type="Node" parent="."]

            [editable path="Inst"]

            """
        );

        Assert.Equal([true, false], Root(document).Children.Select(child => child.Editable));
    }

    [Fact]
    public void NamespacesArePerFolderInFirstUseOrder()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://ui/a.tscn" id="1_aaaaa"]
            [ext_resource type="PackedScene" path="res://menu/ui/b.tscn" id="2_bbbbb"]
            [ext_resource type="PackedScene" path="res://top.tscn" id="3_ccccc"]
            [ext_resource type="PackedScene" path="res://ui/d.tscn" id="4_ddddd"]

            [node name="Root" type="Node"]

            [node name="A" parent="." instance=ExtResource("1_aaaaa")]

            [node name="B" parent="." instance=ExtResource("2_bbbbb")]

            [node name="C" parent="." instance=ExtResource("3_ccccc")]

            [node name="D" parent="." instance=ExtResource("4_ddddd")]

            """
        );

        Assert.Equal(
            [new XscnNamespace("ui", "res://ui/"), new XscnNamespace("ui2", "res://menu/ui/"), new XscnNamespace("res", "res://")],
            document.Namespaces
        );
    }

    [Fact]
    public void ExtResourceInValueTextIsResourcePath()
    {
        XscnNode child = OnlyChild(
            """
            [gd_scene format=3]

            [ext_resource type="Texture2D" path="res://icon.png" id="1_ic0n5"]

            [node name="Root" type="Node"]

            [node name="Holder" type="Node" parent="."]
            metadata/list = [ExtResource("1_ic0n5")]

            """
        );

        GodotArray array = Assert.IsType<GodotArray>(Assert.IsType<XscnScalar>(Assert.Single(child.Properties).Value).Value);
        Assert.Equal(new GodotPathRef(null, "res://icon.png"), Assert.Single(array.Items));
    }

    [Fact]
    public void IdsAreRecordedForScenes()
    {
        const string text = """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]
            [ext_resource type="Script" path="res://holder.gd" id="2_h0ld3"]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_d3e4f"]

            [node name="Root" type="Node"]
            script = ExtResource("2_h0ld3")
            metadata/shared = [SubResource("StyleBoxFlat_a1b2c")]
            metadata/once = SubResource("StyleBoxFlat_d3e4f")

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            """;
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(result.Issues);

        XscnResource inline = Assert.IsType<XscnInline>(Root(result.Document).Properties.Single(p => p.GodotName == "metadata/once").Value).Resource;
        ConversionIds ids = Assert.IsType<ConversionIds>(result.Ids);
        Assert.Equal("1_p4rt0", ids.ExtIdOf("res://part.tscn"));
        Assert.Equal("2_h0ld3", ids.ExtIdOf("res://holder.gd"));
        Assert.Equal("StyleBoxFlat_a1b2c", ids.SubIdOf(Assert.Single(result.Document.Resources)));
        Assert.Equal("StyleBoxFlat_d3e4f", ids.SubIdOf(inline));
    }

    [Fact]
    public void ThemeOverrideWithAOnceUsedResourceStaysInline()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_p1234"]

            [node name="Root" type="PanelContainer"]
            theme_override_styles/panel = SubResource("StyleBoxFlat_p1234")

            """
        );

        Assert.Empty(document.Resources);
        Assert.IsType<XscnInline>(Assert.Single(Root(document).Properties).Value);
    }

    [Fact]
    public void PlaceholderPropertyEqualToTheDefaultSurvivesTheWrite()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [node name="Root" type="Node"]

            [node name="Later" parent="." instance_placeholder="res://later.tscn"]
            process_mode = 0

            """
        );

        XscnParseResult parsed = XscnParser.Parse(XscnWriter.Write(document, Metadata), XscnDocumentKind.Scene, Metadata);

        Assert.Empty(parsed.Diagnostics);
        XscnNodeElement later = Assert.Single(Assert.IsType<XscnNode>(parsed.Document.Root).Children);
        Assert.Equal("process_mode", Assert.Single(later.Properties).GodotName);
    }

    [Fact]
    public void EditableInsideAnInstanceGetsASynthesisedOverride()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [editable path="Inst/Sub"]

            """
        );

        XscnOverride change = Assert.IsType<XscnOverride>(Assert.Single(Instance(document).Children));
        Assert.Equal("Sub", change.Path);
        Assert.True(change.Editable);
        Assert.False(Instance(document).Editable);
    }

    [Fact]
    public void ConnectionFromABaseNodeOfAnInheritedRootSitsOnItsOverride()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://base.tscn" id="1_b4s3s"]

            [node name="Root" instance=ExtResource("1_b4s3s")]

            [connection signal="ready" from="A" to="." method="on_a_ready"]

            """
        );

        XscnOverride change = Assert.IsType<XscnOverride>(Assert.Single(Root(document).Children));
        Assert.Equal("A", change.Path);
        Assert.Equal("on_a_ready", Assert.Single(change.Connections).Method);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("xmlns")]
    [InlineData("XmlStuff")]
    public void ReservedXmlPrefixesAreAvoided(string folder)
    {
        XscnDocument document = Convert(
            $"""
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://{folder}/a.tscn" id="1_aaaaa"]

            [node name="Root" type="Node"]

            [node name="A" parent="." instance=ExtResource("1_aaaaa")]

            """
        );

        Assert.Equal(new XscnNamespace("_" + folder, $"res://{folder}/"), Assert.Single(document.Namespaces));
    }

    [Fact]
    public void OverrideSectionAfterItsSynthesisedOverrideIsMerged()
    {
        XscnDocument document = Convert(
            """
            [gd_scene format=3]

            [ext_resource type="PackedScene" path="res://part.tscn" id="1_p4rt0"]

            [node name="Root" type="Node"]

            [node name="Inst" parent="." instance=ExtResource("1_p4rt0")]

            [node name="Added" type="Node" parent="Inst/A"]

            [node name="A" parent="Inst" index="0"]
            visible = false

            """
        );

        XscnOverride change = Assert.IsType<XscnOverride>(Assert.Single(Instance(document).Children));
        Assert.Equal("A", change.Path);
        Assert.Equal(0, change.Index);
        Assert.Equal("visible", Assert.Single(change.Properties).GodotName);
        Assert.Equal("Added", Assert.IsType<XscnNode>(Assert.Single(change.Children)).Name);
    }

    private static XscnDocument Convert(string text)
    {
        ConversionResult<XscnDocument> result = TscnToXscn.Convert(TscnReader.Read(text), Metadata, Options);
        Assert.Empty(result.Issues);
        Assert.Equal(XscnDocumentKind.Scene, result.Document.Kind);
        return result.Document;
    }

    private static XscnNode Root(XscnDocument document) => Assert.IsType<XscnNode>(document.Root);

    private static XscnNode OnlyChild(string text) => Assert.IsType<XscnNode>(Assert.Single(Root(Convert(text)).Children));

    // The root's child named Inst, the instance the tests build on.
    private static XscnNode Instance(XscnDocument document) =>
        Assert.IsType<XscnNode>(Assert.Single(Root(document).Children, child => child is XscnNode { Name: "Inst" }));

    private static string ReadFixture(string folder, string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", folder, file)));
}
