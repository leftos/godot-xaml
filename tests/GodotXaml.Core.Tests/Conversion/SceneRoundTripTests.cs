using System.Text;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// .tscn to .xscn and back: through the model with the forward ids kept, which must write Godot's bytes again, and through
// the .xscn text with new ids, which must write the same scene but for its ids and property order.
public sealed class SceneRoundTripTests
{
    private const string Nested = """[ext_resource type="PackedScene" path="res://cases/nested.tscn" id="1_n3st3"]""";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    public static TheoryData<string> Fixtures =>
        new(
            "animation_player_library",
            "bone_chain_settings",
            "connections",
            "ext_resource",
            "groups",
            "gui_item_arrays",
            "inherited",
            "instanced",
            "instanced_order",
            "lone_root",
            "multiline",
            "nested",
            "order_inner",
            "physics_bones",
            "placeholder",
            "placeholder_order",
            "property_order",
            "script_node",
            "skeleton_bones",
            "skeleton_modification_chains",
            "sub_resources",
            "two_bone_ik_modifiers",
            "unique_name"
        );

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsGodotSceneThereAndBackByteIdentical(string fixture)
    {
        string text = ReadFixture(fixture + ".tscn");

        Assert.Equal(text, ThroughModel(text, XscnDocumentKind.Scene));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConvertsGodotSceneThroughXscnTextAndBack(string fixture)
    {
        string text = ReadFixture(fixture + ".tscn");

        Assert.Equal(Comparable(TscnReader.Read(text)), Comparable(ThroughText(text, XscnDocumentKind.Scene)));
    }

    [Fact]
    public void UidsOnTheHeaderAndAnExtResourceRoundTrip() =>
        AssertRoundTrips(
            """
            [gd_scene format=3 uid="uid://b1x2y3"]

            [ext_resource type="Texture2D" uid="uid://c4d5e6" path="res://icon.png" id="1_ic0n5"]

            [node name="Root" type="Sprite2D" unique_id=1]
            texture = ExtResource("1_ic0n5")

            """
        );

    // Godot writes each section on one line, longer than the source line limit allows.
    private const string IdPathNode =
        "[node name=\"Child\" type=\"Node\" parent=\".\" parent_id_path=PackedInt32Array(1) owner=\"Inst\" "
        + "owner_uid_path=PackedInt32Array(1, 2) unique_id=3]";

    private const string IdPathConnection =
        "[connection signal=\"ready\" from=\"Inst\" to=\".\" method=\"update_configuration_warnings\" "
        + "from_uid_path=PackedInt32Array(2, 5) to_uid_path=PackedInt32Array(1)]";

    [Fact]
    public void IdPathsRoundTrip() =>
        AssertRoundTrips(
            $"""
            [gd_scene format=3]

            {Nested}

            [node name="Root" type="Node" unique_id=1]

            [node name="Inst" parent="." unique_id=2 instance=ExtResource("1_n3st3")]

            {IdPathNode}

            {IdPathConnection}

            """
        );

    [Fact]
    public void Format4ContentRoundTrips() =>
        AssertRoundTrips(
            """
            [gd_scene format=4]

            [node name="Root" type="Node" unique_id=1]
            metadata/points = PackedVector4Array(1, 2, 3, 4, 0.5, 0, 0, 1)

            """
        );

    [Fact]
    public void NestedInlineResourcesRoundTrip() =>
        AssertRoundTrips(
            """
            [gd_scene format=3]

            [sub_resource type="Gradient" id="Gradient_gr4d1"]
            colors = PackedColorArray(1, 0, 0, 1, 0, 0, 1, 1)

            [sub_resource type="GradientTexture2D" id="GradientTexture2D_0n3us"]
            gradient = SubResource("Gradient_gr4d1")
            width = 8

            [node name="Root" type="Sprite2D" unique_id=1]
            texture = SubResource("GradientTexture2D_0n3us")

            """
        );

    [Fact]
    public void ResourceSharedInsideAnArrayRoundTrips() =>
        AssertRoundTrips(
            """
            [gd_scene format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_a1b2c"]
            bg_color = Color(1, 0, 0, 1)

            [node name="Root" type="Node" unique_id=1]
            metadata/styles = [SubResource("StyleBoxFlat_a1b2c"), SubResource("StyleBoxFlat_a1b2c")]

            [node name="Panel" type="Panel" parent="." unique_id=2]
            theme_override_styles/panel = SubResource("StyleBoxFlat_a1b2c")

            """
        );

    [Fact]
    public void SubResourceUsedTwiceRoundTrips() =>
        AssertRoundTrips(
            """
            [gd_scene format=3]

            [sub_resource type="CanvasItemMaterial" id="CanvasItemMaterial_sh4r3"]
            light_mode = 1

            [node name="Root" type="Node2D" unique_id=1]

            [node name="One" type="Sprite2D" parent="." unique_id=2]
            material = SubResource("CanvasItemMaterial_sh4r3")

            [node name="Two" type="Sprite2D" parent="." unique_id=3]
            material = SubResource("CanvasItemMaterial_sh4r3")

            """
        );

    [Fact]
    public void ExtResourceInsideADictionaryRoundTrips() =>
        AssertRoundTrips(
            """
            [gd_scene format=3]

            [ext_resource type="Texture2D" path="res://icon.png" id="1_ic0n5"]

            [node name="Root" type="Node" unique_id=1]
            metadata/icons = {
            "main": ExtResource("1_ic0n5")
            }

            """
        );

    [Fact]
    public void ConnectionFromInsideAnInstanceRoundTrips() =>
        AssertRoundTrips(
            $"""
            [gd_scene format=3]

            {Nested}

            [node name="Root" type="Node" unique_id=1]

            [node name="Inst" parent="." unique_id=2 instance=ExtResource("1_n3st3")]

            [connection signal="ready" from="Inst/A" to="." method="update_configuration_warnings"]

            """
        );

    [Fact]
    public void EditableInstanceWithAnAddedNodeRoundTrips() =>
        AssertRoundTrips(
            $"""
            [gd_scene format=3]

            {Nested}

            [node name="Root" type="Node" unique_id=1]

            [node name="Inst" parent="." unique_id=2 instance=ExtResource("1_n3st3")]

            [node name="Added" type="Node" parent="Inst/A" index="1" unique_id=3]

            [editable path="Inst"]

            """
        );

    // Overrides never nest, so Added goes inside override A with A/B its sibling, and the way back writes A, A/Added, A/B:
    // the same sections, which Godot orders by index=, in another order.
    [Fact]
    public void NestedOverrideOrderKeepsTheSameNodes()
    {
        string text = $"""
            [gd_scene format=3]

            {Nested}

            [node name="Root" type="Node" unique_id=1]

            [node name="Inst" parent="." unique_id=2 instance=ExtResource("1_n3st3")]

            [node name="A" parent="Inst" index="0" unique_id=3]
            position = Vector2(9, 9)

            [node name="B" parent="Inst/A" index="0" unique_id=4]
            process_priority = 2

            [node name="Added" type="Node" parent="Inst/A" index="1" unique_id=5]

            [editable path="Inst"]

            """;

        Assert.Equal(Sections(text), Sections(ThroughModel(text, XscnDocumentKind.Scene)));
        Assert.Equal(Sections(Comparable(TscnReader.Read(text))), Sections(Comparable(ThroughText(text, XscnDocumentKind.Scene))));
    }

    [Fact]
    public void ThemeTypePropertiesRoundTrip() =>
        AssertRoundTrips(
            """
            [gd_resource type="Theme" format=3]

            [sub_resource type="StyleBoxFlat" id="StyleBoxFlat_b1234"]
            bg_color = Color(1, 0, 0, 1)

            [resource]
            Button/colors/font_color = Color(0, 1, 0, 1)
            Button/styles/normal = SubResource("StyleBoxFlat_b1234")

            """,
            XscnDocumentKind.Resource
        );

    // The default library's key is empty, so it is no XML name and takes x:Prop with its resource in the table.
    [Fact]
    public void ADefaultAnimationLibraryRoundTripsThroughXProp()
    {
        const string Text = """
            [gd_scene format=3]

            [sub_resource type="AnimationLibrary" id="AnimationLibrary_d3f4u"]

            [node name="Root" type="AnimationPlayer" unique_id=1]
            libraries/ = SubResource("AnimationLibrary_d3f4u")

            """;

        string xscn = XscnWriter.Write(Forward(TscnReader.Read(Text), XscnDocumentKind.Scene).Document, Metadata);

        Assert.Contains("<x:Prop Name=\"libraries/\"", xscn, StringComparison.Ordinal);
        Assert.Empty(XscnParser.Parse(xscn, XscnDocumentKind.Scene, Metadata).Diagnostics);
        AssertRoundTrips(Text);
    }

    private static void AssertRoundTrips(string text, XscnDocumentKind kind = XscnDocumentKind.Scene)
    {
        Assert.Equal(text, ThroughModel(text, kind));
        Assert.Equal(Comparable(TscnReader.Read(text)), Comparable(ThroughText(text, kind)));
    }

    // Forward, then back with the forward ids kept; the written text.
    private static string ThroughModel(string text, XscnDocumentKind kind)
    {
        TscnDocument source = TscnReader.Read(text);
        ConversionResult<XscnDocument> forward = Forward(source, kind);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, Metadata, BackOptions(source) with { KeepIds = forward.Ids });

        Assert.Empty(back.Issues);
        return TscnWriter.Write(back.Document);
    }

    // Forward, written as .xscn text and parsed again, then back with new ids.
    private static TscnDocument ThroughText(string text, XscnDocumentKind kind)
    {
        TscnDocument source = TscnReader.Read(text);
        XscnParseResult parsed = XscnParser.Parse(XscnWriter.Write(Forward(source, kind).Document, Metadata), kind, Metadata);
        Assert.Empty(parsed.Diagnostics);

        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, BackOptions(source));

        Assert.Empty(back.Issues);
        return back.Document;
    }

    private static ConversionResult<XscnDocument> Forward(TscnDocument source, XscnDocumentKind kind)
    {
        ConversionResult<XscnDocument> forward = TscnToXscn.Convert(source, Metadata, Options);
        Assert.Empty(forward.Issues);
        Assert.Equal(kind, forward.Document.Kind);
        return forward;
    }

    // The fixture's own ext_resource types, and its header's uid, as the saver passes the .uid sidecar's.
    private static ConversionOptions BackOptions(TscnDocument source) =>
        Options with
        {
            ExtResourceTypes = new TypesOf(source),
            Uid = source.Header.Uid,
            SceneFiles = SceneBackTests.FixtureScenes.Instance,
            SourceKey = "res://cases/round_trip.xscn",
        };

    private static string Comparable(TscnDocument document) => TscnWriter.Write(TscnTestNormalizer.Normalize(document));

    // The file's sections, blank-line separated, in ordinal order.
    private static string[] Sections(string text) => [.. text.Split("\n\n").Order(StringComparer.Ordinal)];

    private static string ReadFixture(string file) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFiles.PathOf("tests", "fixtures", "scenes", file)));

    private sealed class TypesOf(TscnDocument source) : IExtResourceTypes
    {
        public string? ClassOf(string path) => source.ExtResources.FirstOrDefault(ext => ext.Path == path)?.Type;
    }
}
