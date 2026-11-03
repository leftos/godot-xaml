using System.Collections.Immutable;
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Metadata;
using GodotXaml.Core.Tests.Values;
using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Tests.Conversion;

// A dynamic name (one a class adds in _get_property_list, which ClassDB does not list) goes in its declaring class's place.
public sealed class PropertyOrderTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly GodotMetadata Metadata = GodotMetadata.Load(RepoFiles.PathOf("metadata", "godot-4.7.json"));

    private static readonly ConversionOptions Options = new() { Seed = 7 };

    // AnimationMixer declares libraries/, so a library exists before AnimationPlayer's current_animation plays it.
    [Fact]
    public void ABaseClassFamilyComesBeforeTheLeafClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <AnimationPlayer {{X}} Name="Root" CurrentAnimation="idle">
              <x:Prop Name="libraries/lib" Value='Resource("res://l.tres")' />
            </AnimationPlayer>
            """
        );

        Assert.Equal(["libraries/lib", "current_animation"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ALeafFamilyWithNoSlashComesBeforeTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <AnimationPlayer {{X}} Name="Root" Script="res://s.gd" SpeedScale="2">
              <x:Prop Name="blend_times" Value="[]" />
            </AnimationPlayer>
            """
        );

        Assert.Equal(["speed_scale", "blend_times", "script"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ALoneDynamicNameComesBeforeTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <AudioListener3D {{X}} Name="Root" Script="res://s.gd">
              <x:Prop Name="current" Value="true" />
            </AudioListener3D>
            """
        );

        Assert.Equal(["current", "script"], Names(scene.Nodes[0].Properties));
    }

    // GeometryInstance3D, a base of MeshInstance3D, declares instance_shader_parameters/, so it precedes mesh.
    [Fact]
    public void FamiliesFollowTheirDeclaringClassesInTheChain()
    {
        TscnDocument scene = Back(
            $$"""
            <MeshInstance3D {{X}} Name="Root" Mesh="res://m.tres">
              <x:Prop Name="surface_material_override/0" Value='Resource("res://a.tres")' />
              <x:Prop Name="instance_shader_parameters/p" Value="1.0" />
            </MeshInstance3D>
            """
        );

        Assert.Equal(["instance_shader_parameters/p", "mesh", "surface_material_override/0"], Names(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ACanvasItemFamilyComesBeforeTheSpriteList()
    {
        TscnDocument scene = Back(
            $$"""
            <Sprite2D {{X}} Name="Root" Texture="res://t.png">
              <x:Prop Name="instance_shader_parameters/p" Value="1.0" />
            </Sprite2D>
            """
        );

        Assert.Equal(["instance_shader_parameters/p", "texture"], Names(scene.Nodes[0].Properties));
    }

    // Node2D does not inherit AnimationMixer, so libraries/ there is a script's own name.
    [Fact]
    public void AFamilyOfAClassOutsideTheChainStaysAfterTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <Node2D {{X}} Name="Root" Script="res://s.gd">
              <x:Prop Name="libraries/x" Value="1" />
            </Node2D>
            """
        );

        Assert.Equal(["script", "libraries/x"], Names(scene.Nodes[0].Properties));
    }

    // Material lists render_priority, ShaderMaterial lists shader, then ShaderMaterial adds shader_parameter/.
    [Fact]
    public void AResourceFamilyComesAfterItsClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <MeshInstance3D {{X}} Name="Root">
              <MeshInstance3D.MaterialOverride>
                <ShaderMaterial Shader="res://s.gdshader" RenderPriority="1" Script="res://m.gd">
                  <x:Prop Name="shader_parameter/c" Value="1.0" />
                </ShaderMaterial>
              </MeshInstance3D.MaterialOverride>
            </MeshInstance3D>
            """
        );

        Assert.Equal(["render_priority", "shader", "shader_parameter/c", "script"], Names(Assert.Single(scene.SubResources).Properties));
    }

    [Fact]
    public void MetadataAndExportsStayLast()
    {
        TscnDocument scene = Back(
            $$"""
            <AudioListener3D {{X}} Name="Root" Script="res://s.gd" Metadata.m="1" alpha="2">
              <x:Prop Name="current" Value="true" />
            </AudioListener3D>
            """
        );

        Assert.Equal(["current", "script", "alpha", "metadata/m"], Names(scene.Nodes[0].Properties));
    }

    // Animation::_set creates a track only on tracks/<count>/type, so each track's type comes first, tracks in numeric order.
    [Fact]
    public void AnAnimationTrackStartsWithItsTypeAndTracksRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <Animation {{X}}>
              <x:Prop Name="tracks/0/interp" Value="2" />
              <x:Prop Name="tracks/0/path" Value='NodePath("A:x")' />
              <x:Prop Name="tracks/0/type" Value='"value"' />
              <x:Prop Name="tracks/10/type" Value='"method"' />
              <x:Prop Name="tracks/2/type" Value='"bezier"' />
            </Animation>
            """
        );

        Assert.Equal(["tracks/0/type", "tracks/0/path", "tracks/0/interp", "tracks/2/type", "tracks/10/type"], Names(resource.ResourceProperties));
    }

    // Skeleton3D::_set adds a bone only on bones/<count>/name and rejects any other key of a bone that does not exist yet.
    [Fact]
    public void ASkeletonBoneStartsWithItsNameAndBonesRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Skeleton3D {{X}} Name="Root">
              <x:Prop Name="bones/0/enabled" Value="false" />
              <x:Prop Name="bones/0/name" Value='"a"' />
              <x:Prop Name="bones/0/parent" Value="-1" />
              <x:Prop Name="bones/10/name" Value='"k"' />
              <x:Prop Name="bones/2/name" Value='"c"' />
            </Skeleton3D>
            """
        );

        Assert.Equal(["bones/0/name", "bones/0/parent", "bones/0/enabled", "bones/2/name", "bones/10/name"], Names(scene.Nodes[0].Properties));
    }

    // AnimationNodeBlendSpace2D::_set adds a point only on blend_point_<count>/node.
    [Fact]
    public void ABlendPointStartsWithItsNode()
    {
        TscnDocument resource = BackResource(
            $$"""
            <AnimationNodeBlendSpace2D {{X}}>
              <x:Prop Name="blend_point_0/name" Value='&amp;"walk"' />
              <x:Prop Name="blend_point_0/node" Value='Resource("res://a.tres")' />
              <x:Prop Name="blend_point_0/pos" Value="Vector2(1, 0)" />
              <x:Prop Name="blend_point_10/node" Value='Resource("res://a.tres")' />
              <x:Prop Name="blend_point_2/node" Value='Resource("res://a.tres")' />
            </AnimationNodeBlendSpace2D>
            """
        );

        Assert.Equal(
            ["blend_point_0/node", "blend_point_0/pos", "blend_point_0/name", "blend_point_2/node", "blend_point_10/node"],
            Names(resource.ResourceProperties)
        );
    }

    // Skeleton3D::_get_property_list lists a bone's bone_meta/ names after its own keys.
    [Fact]
    public void ABoneMetaKeyComesLastInItsBone()
    {
        TscnDocument scene = Back(
            $$"""
            <Skeleton3D {{X}} Name="Root">
              <x:Prop Name="bones/0/bone_meta/tag" Value='"x"' />
              <x:Prop Name="bones/0/name" Value='"a"' />
              <x:Prop Name="bones/0/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)" />
            </Skeleton3D>
            """
        );

        Assert.Equal(["bones/0/name", "bones/0/rest", "bones/0/bone_meta/tag"], Names(scene.Nodes[0].Properties));
    }

    // VisualShader::_set creates a node only on nodes/<type>/<id>/node and casts the node unchecked for expression and
    // input_ports; nodes/<type>/connections needs the nodes and their ports. The output node (id 0) has no node key. The
    // types come in Godot's save order (vertex before fragment), so the round trip is byte-identical.
    [Fact]
    public void AVisualShaderNodeStartsWithItsNodeAndConnectionsFollowTheNodes()
    {
        TscnDocument resource = BackResource(
            $$"""
            <VisualShader {{X}}>
              <x:Prop Name="nodes/fragment/10/expression" Value='"x"' />
              <x:Prop Name="nodes/fragment/10/input_ports" Value='"0,0,a;"' />
              <x:Prop Name="nodes/fragment/10/node" Value='Resource("res://e.tres")' />
              <x:Prop Name="nodes/fragment/10/position" Value="Vector2(1, 0)" />
              <x:Prop Name="nodes/fragment/2/node" Value='Resource("res://c.tres")' />
              <x:Prop Name="nodes/fragment/connections" Value="PackedInt32Array(2, 0, 10, 0)" />
              <x:Prop Name="nodes/vertex/0/position" Value="Vector2(2, 0)" />
              <x:Prop Name="nodes/vertex/10/node" Value='Resource("res://e.tres")' />
              <x:Prop Name="nodes/vertex/10/position" Value="Vector2(3, 0)" />
              <x:Prop Name="nodes/vertex/connections" Value="PackedInt32Array()" />
            </VisualShader>
            """
        );

        Assert.Equal(
            [
                "nodes/vertex/0/position",
                "nodes/vertex/10/node",
                "nodes/vertex/10/position",
                "nodes/vertex/connections",
                "nodes/fragment/2/node",
                "nodes/fragment/10/node",
                "nodes/fragment/10/position",
                "nodes/fragment/10/input_ports",
                "nodes/fragment/10/expression",
                "nodes/fragment/connections",
            ],
            Names(resource.ResourceProperties)
        );
    }

    // VisualShader::_set reads a graph type it does not know as vertex, so its node must still come first.
    [Fact]
    public void AVisualShaderNodeOfAnUnlistedTypeStillStartsWithItsNode()
    {
        TscnDocument resource = BackResource(
            $$"""
            <VisualShader {{X}}>
              <x:Prop Name="nodes/other/3/expression" Value='"x"' />
              <x:Prop Name="nodes/other/3/node" Value='Resource("res://e.tres")' />
              <x:Prop Name="nodes/other/3/position" Value="Vector2(1, 0)" />
            </VisualShader>
            """
        );

        Assert.Equal(["nodes/other/3/node", "nodes/other/3/position", "nodes/other/3/expression"], Names(resource.ResourceProperties));
    }

    // SceneReplicationConfig::_set creates an entry only on properties/<count>/path.
    [Fact]
    public void ReplicationPropertiesRunInNumericOrderWithThePathFirst()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SceneReplicationConfig {{X}}>
              <x:Prop Name="properties/0/path" Value='NodePath(".:p0")' />
              <x:Prop Name="properties/0/replication_mode" Value="1" />
              <x:Prop Name="properties/0/spawn" Value="true" />
              <x:Prop Name="properties/1/path" Value='NodePath(".:p1")' />
              <x:Prop Name="properties/1/replication_mode" Value="2" />
              <x:Prop Name="properties/1/spawn" Value="false" />
              <x:Prop Name="properties/10/path" Value='NodePath(".:p10")' />
              <x:Prop Name="properties/10/replication_mode" Value="0" />
              <x:Prop Name="properties/10/spawn" Value="true" />
              <x:Prop Name="properties/2/path" Value='NodePath(".:p2")' />
              <x:Prop Name="properties/2/replication_mode" Value="1" />
              <x:Prop Name="properties/2/spawn" Value="false" />
            </SceneReplicationConfig>
            """
        );

        Assert.Equal(
            [
                "properties/0/path",
                "properties/0/spawn",
                "properties/0/replication_mode",
                "properties/1/path",
                "properties/1/spawn",
                "properties/1/replication_mode",
                "properties/2/path",
                "properties/2/spawn",
                "properties/2/replication_mode",
                "properties/10/path",
                "properties/10/spawn",
                "properties/10/replication_mode",
            ],
            Names(resource.ResourceProperties)
        );
    }

    // TileSet::_set appends at most one pattern per pattern_<N>, so the patterns must come in numeric order.
    [Fact]
    public void TileSetPatternsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSet {{X}}>
              <x:Prop Name="pattern_0" Value='Resource("res://p0.tres")' />
              <x:Prop Name="pattern_1" Value='Resource("res://p1.tres")' />
              <x:Prop Name="pattern_10" Value='Resource("res://p10.tres")' />
              <x:Prop Name="pattern_2" Value='Resource("res://p2.tres")' />
            </TileSet>
            """
        );

        Assert.Equal(["pattern_0", "pattern_1", "pattern_2", "pattern_10"], Names(resource.ResourceProperties));
    }

    private static IEnumerable<string> Names(ImmutableArray<TscnProperty> properties) => properties.Select(property => property.Name);

    // Parses a resource document and converts it with no issue.
    private static TscnDocument BackResource(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Resource, Metadata);
        Assert.Empty(parsed.Diagnostics);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }

    // Parses and converts with no issue.
    private static TscnDocument Back(string text)
    {
        XscnParseResult parsed = XscnParser.Parse(text, XscnDocumentKind.Scene, Metadata);
        Assert.Empty(parsed.Diagnostics);
        ConversionResult<TscnDocument> back = XscnToTscn.Convert(parsed.Document, Metadata, Options);
        Assert.Empty(back.Issues);
        return back.Document;
    }
}
