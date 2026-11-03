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

    // VisualShader::_get_property_list lists modes/ in the order ShaderTypes registers the shader mode's render modes
    // (shader_types.cpp:238-247), not in name order.
    [Fact]
    public void VisualShaderModesComeInTheShaderTypesOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <VisualShader {{X}}>
              <x:Prop Name="modes/blend" Value="1" />
              <x:Prop Name="modes/cull" Value="2" />
              <x:Prop Name="modes/depth_draw" Value="1" />
              <x:Prop Name="modes/depth_test" Value="2" />
              <x:Prop Name="modes/diffuse" Value="2" />
              <x:Prop Name="modes/specular" Value="1" />
            </VisualShader>
            """
        );

        Assert.Equal(
            ["modes/blend", "modes/depth_draw", "modes/depth_test", "modes/cull", "modes/diffuse", "modes/specular"],
            Names(resource.ResourceProperties)
        );
    }

    // VisualShader::_get_property_list lists flags/ in the order ShaderTypes registers the Spatial render modes
    // (shader_types.cpp:240-260), depth_test_disabled at depth_test's place.
    [Fact]
    public void VisualShaderFlagsComeInTheShaderTypesOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <VisualShader {{X}}>
              <x:Prop Name="flags/depth_prepass_alpha" Value="true" />
              <x:Prop Name="flags/depth_test_disabled" Value="true" />
              <x:Prop Name="flags/fog_disabled" Value="true" />
              <x:Prop Name="flags/shadows_disabled" Value="true" />
              <x:Prop Name="flags/skip_vertex_transform" Value="true" />
              <x:Prop Name="flags/specular_occlusion_disabled" Value="true" />
              <x:Prop Name="flags/unshaded" Value="true" />
              <x:Prop Name="flags/wireframe" Value="true" />
            </VisualShader>
            """
        );

        Assert.Equal(
            [
                "flags/depth_prepass_alpha",
                "flags/depth_test_disabled",
                "flags/unshaded",
                "flags/wireframe",
                "flags/skip_vertex_transform",
                "flags/shadows_disabled",
                "flags/fog_disabled",
                "flags/specular_occlusion_disabled",
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

    // With no skeleton, SpringBoneSimulator3D::set_root_bone frees a setting's joints, and the collision setters return early
    // while enable_all_child_collisions is true; joints and collisions run in numeric order, each joint's keys in Godot's order.
    [Fact]
    public void ASpringBoneSettingSetsItsBonesFirstAndItsJointsInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <SpringBoneSimulator3D {{X}} Name="Root">
              <x:Prop Name="settings/0/end_bone" Value="3" />
              <x:Prop Name="settings/0/individual_config" Value="true" />
              <x:Prop Name="settings/0/joint_count" Value="11" />
              <x:Prop Name="settings/0/joints/0/drag" Value="0.1" />
              <x:Prop Name="settings/0/joints/0/radius" Value="0.2" />
              <x:Prop Name="settings/0/joints/0/stiffness" Value="0.3" />
              <x:Prop Name="settings/0/joints/1/drag" Value="0.1" />
              <x:Prop Name="settings/0/joints/1/radius" Value="0.2" />
              <x:Prop Name="settings/0/joints/1/stiffness" Value="0.3" />
              <x:Prop Name="settings/0/joints/10/drag" Value="0.1" />
              <x:Prop Name="settings/0/joints/10/radius" Value="0.2" />
              <x:Prop Name="settings/0/joints/10/stiffness" Value="0.3" />
              <x:Prop Name="settings/0/joints/2/drag" Value="0.1" />
              <x:Prop Name="settings/0/joints/2/radius" Value="0.2" />
              <x:Prop Name="settings/0/joints/2/stiffness" Value="0.3" />
              <x:Prop Name="settings/0/root_bone" Value="0" />
              <x:Prop Name="settings/0/root_bone_name" Value='"a"' />
              <x:Prop Name="settings/1/collision_count" Value="11" />
              <x:Prop Name="settings/1/collisions/0" Value='NodePath("C0")' />
              <x:Prop Name="settings/1/collisions/1" Value='NodePath("C1")' />
              <x:Prop Name="settings/1/collisions/10" Value='NodePath("C10")' />
              <x:Prop Name="settings/1/enable_all_child_collisions" Value="false" />
            </SpringBoneSimulator3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/root_bone_name",
                "settings/0/root_bone",
                "settings/0/end_bone",
                "settings/0/individual_config",
                "settings/0/joint_count",
                "settings/0/joints/0/radius",
                "settings/0/joints/0/stiffness",
                "settings/0/joints/0/drag",
                "settings/0/joints/1/radius",
                "settings/0/joints/1/stiffness",
                "settings/0/joints/1/drag",
                "settings/0/joints/2/radius",
                "settings/0/joints/2/stiffness",
                "settings/0/joints/2/drag",
                "settings/0/joints/10/radius",
                "settings/0/joints/10/stiffness",
                "settings/0/joints/10/drag",
                "settings/1/enable_all_child_collisions",
                "settings/1/collision_count",
                "settings/1/collisions/0",
                "settings/1/collisions/1",
                "settings/1/collisions/10",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // SpringBoneSimulator3D::_get_property_list lists a setting's excluded collisions after its joints and before its
    // collisions, each list in numeric order.
    [Fact]
    public void ASpringBoneSettingListsItsExcludedCollisionsBeforeItsCollisions()
    {
        TscnDocument scene = Back(
            $$"""
            <SpringBoneSimulator3D {{X}} Name="Root">
              <x:Prop Name="settings/0/collision_count" Value="1" />
              <x:Prop Name="settings/0/collisions/0" Value='NodePath("C0")' />
              <x:Prop Name="settings/0/enable_all_child_collisions" Value="true" />
              <x:Prop Name="settings/0/exclude_collision_count" Value="11" />
              <x:Prop Name="settings/0/exclude_collisions/0" Value='NodePath("E0")' />
              <x:Prop Name="settings/0/exclude_collisions/1" Value='NodePath("E1")' />
              <x:Prop Name="settings/0/exclude_collisions/10" Value='NodePath("E10")' />
              <x:Prop Name="settings/0/individual_config" Value="true" />
              <x:Prop Name="settings/0/joint_count" Value="1" />
              <x:Prop Name="settings/0/joints/0/radius" Value="0.2" />
            </SpringBoneSimulator3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/individual_config",
                "settings/0/joint_count",
                "settings/0/joints/0/radius",
                "settings/0/enable_all_child_collisions",
                "settings/0/exclude_collision_count",
                "settings/0/exclude_collisions/0",
                "settings/0/exclude_collisions/1",
                "settings/0/exclude_collisions/10",
                "settings/0/collision_count",
                "settings/0/collisions/0",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // IterateIK3D::_get_property_list lists every setting's target_node, then each setting's chain keys (with no skeleton
    // ChainIK3D::set_root_bone frees the joints), then every setting's joint keys.
    [Fact]
    public void AnIterateIKNodeListsTargetsThenChainsThenJoints()
    {
        TscnDocument scene = Back(
            $$"""
            <Fabrik3D {{X}} Name="Root">
              <x:Prop Name="settings/0/end_bone" Value="1" />
              <x:Prop Name="settings/0/joint_count" Value="2" />
              <x:Prop Name="settings/0/joints/0/limitation/right_axis" Value="1" />
              <x:Prop Name="settings/0/joints/0/rotation_axis" Value="1" />
              <x:Prop Name="settings/0/joints/1/limitation/right_axis" Value="2" />
              <x:Prop Name="settings/0/joints/1/rotation_axis" Value="2" />
              <x:Prop Name="settings/0/root_bone" Value="0" />
              <x:Prop Name="settings/0/target_node" Value='NodePath("T0")' />
              <x:Prop Name="settings/1/end_bone" Value="2" />
              <x:Prop Name="settings/1/joint_count" Value="2" />
              <x:Prop Name="settings/1/joints/0/limitation/right_axis" Value="1" />
              <x:Prop Name="settings/1/joints/0/rotation_axis" Value="1" />
              <x:Prop Name="settings/1/joints/1/limitation/right_axis" Value="2" />
              <x:Prop Name="settings/1/joints/1/rotation_axis" Value="2" />
              <x:Prop Name="settings/1/root_bone" Value="1" />
              <x:Prop Name="settings/1/target_node" Value='NodePath("T1")' />
            </Fabrik3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/target_node",
                "settings/1/target_node",
                "settings/0/root_bone",
                "settings/0/end_bone",
                "settings/0/joint_count",
                "settings/1/root_bone",
                "settings/1/end_bone",
                "settings/1/joint_count",
                "settings/0/joints/0/rotation_axis",
                "settings/0/joints/0/limitation/right_axis",
                "settings/0/joints/1/rotation_axis",
                "settings/0/joints/1/limitation/right_axis",
                "settings/1/joints/0/rotation_axis",
                "settings/1/joints/0/limitation/right_axis",
                "settings/1/joints/1/rotation_axis",
                "settings/1/joints/1/limitation/right_axis",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // SplineIK3D::_get_property_list lists every setting's path and tilt keys, then each setting's chain keys.
    [Fact]
    public void ASplineIKNodeListsPathsThenChains()
    {
        TscnDocument scene = Back(
            $$"""
            <SplineIK3D {{X}} Name="Root">
              <x:Prop Name="settings/0/joint_count" Value="2" />
              <x:Prop Name="settings/0/path_3d" Value='NodePath("P0")' />
              <x:Prop Name="settings/0/root_bone" Value="0" />
              <x:Prop Name="settings/0/tilt_enabled" Value="true" />
              <x:Prop Name="settings/1/joint_count" Value="3" />
              <x:Prop Name="settings/1/path_3d" Value='NodePath("P1")' />
              <x:Prop Name="settings/1/root_bone" Value="1" />
              <x:Prop Name="settings/1/tilt_enabled" Value="true" />
            </SplineIK3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/path_3d",
                "settings/0/tilt_enabled",
                "settings/1/path_3d",
                "settings/1/tilt_enabled",
                "settings/0/root_bone",
                "settings/0/joint_count",
                "settings/1/root_bone",
                "settings/1/joint_count",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // TwoBoneIK3D::_get_property_list lists each setting's keys in turn, settings in numeric order.
    [Fact]
    public void ATwoBoneIKSettingRunsInGodotsKeyOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <TwoBoneIK3D {{X}} Name="Root">
              <x:Prop Name="settings/0/end_bone" Value="2" />
              <x:Prop Name="settings/0/end_bone/direction" Value="1" />
              <x:Prop Name="settings/0/end_bone/length" Value="0.5" />
              <x:Prop Name="settings/0/end_bone_name" Value='""' />
              <x:Prop Name="settings/0/extend_end_bone" Value="true" />
              <x:Prop Name="settings/0/middle_bone" Value="1" />
              <x:Prop Name="settings/0/pole_direction" Value="2" />
              <x:Prop Name="settings/0/pole_node" Value='NodePath("P0")' />
              <x:Prop Name="settings/0/root_bone" Value="0" />
              <x:Prop Name="settings/0/target_node" Value='NodePath("T0")' />
              <x:Prop Name="settings/0/use_virtual_end" Value="false" />
              <x:Prop Name="settings/10/extend_end_bone" Value="true" />
              <x:Prop Name="settings/10/middle_bone" Value="2" />
              <x:Prop Name="settings/10/root_bone" Value="1" />
              <x:Prop Name="settings/10/target_node" Value='NodePath("T10")' />
              <x:Prop Name="settings/10/use_virtual_end" Value="true" />
            </TwoBoneIK3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/target_node",
                "settings/0/pole_node",
                "settings/0/root_bone",
                "settings/0/middle_bone",
                "settings/0/pole_direction",
                "settings/0/end_bone_name",
                "settings/0/end_bone",
                "settings/0/use_virtual_end",
                "settings/0/extend_end_bone",
                "settings/0/end_bone/direction",
                "settings/0/end_bone/length",
                "settings/10/target_node",
                "settings/10/root_bone",
                "settings/10/middle_bone",
                "settings/10/use_virtual_end",
                "settings/10/extend_end_bone",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // LimitAngularVelocityModifier3D::_get_property_list lists each chain's root bone, then its end bone.
    [Fact]
    public void ALimitAngularVelocityChainListsItsRootBoneFirst()
    {
        TscnDocument scene = Back(
            $$"""
            <LimitAngularVelocityModifier3D {{X}} Name="Root">
              <x:Prop Name="chains/0/end_bone" Value="3" />
              <x:Prop Name="chains/0/end_bone_name" Value='""' />
              <x:Prop Name="chains/0/root_bone" Value="0" />
              <x:Prop Name="chains/0/root_bone_name" Value='""' />
              <x:Prop Name="chains/10/end_bone" Value="4" />
              <x:Prop Name="chains/10/end_bone_name" Value='""' />
              <x:Prop Name="chains/10/root_bone" Value="1" />
              <x:Prop Name="chains/10/root_bone_name" Value='""' />
            </LimitAngularVelocityModifier3D>
            """
        );

        Assert.Equal(
            [
                "chains/0/root_bone_name",
                "chains/0/root_bone",
                "chains/0/end_bone_name",
                "chains/0/end_bone",
                "chains/10/root_bone_name",
                "chains/10/root_bone",
                "chains/10/end_bone_name",
                "chains/10/end_bone",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // AimModifier3D::_get_property_list calls BoneConstraint3D::get_property_list (every setting's constraint keys) before
    // it lists every setting's own keys.
    [Fact]
    public void AnAimModifierListsConstraintsThenItsOwnKeys()
    {
        TscnDocument scene = Back(
            $$"""
            <AimModifier3D {{X}} Name="Root">
              <x:Prop Name="settings/0/amount" Value="0.5" />
              <x:Prop Name="settings/0/apply_bone" Value="0" />
              <x:Prop Name="settings/0/forward_axis" Value="1" />
              <x:Prop Name="settings/0/reference_bone" Value="1" />
              <x:Prop Name="settings/0/reference_type" Value="0" />
              <x:Prop Name="settings/0/relative" Value="false" />
              <x:Prop Name="settings/0/use_euler" Value="true" />
              <x:Prop Name="settings/10/amount" Value="0.25" />
              <x:Prop Name="settings/10/apply_bone" Value="2" />
              <x:Prop Name="settings/10/forward_axis" Value="3" />
              <x:Prop Name="settings/10/reference_bone" Value="3" />
              <x:Prop Name="settings/10/reference_type" Value="0" />
              <x:Prop Name="settings/10/use_euler" Value="true" />
            </AimModifier3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/amount",
                "settings/0/apply_bone",
                "settings/0/reference_type",
                "settings/0/reference_bone",
                "settings/10/amount",
                "settings/10/apply_bone",
                "settings/10/reference_type",
                "settings/10/reference_bone",
                "settings/0/forward_axis",
                "settings/0/use_euler",
                "settings/0/relative",
                "settings/10/forward_axis",
                "settings/10/use_euler",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // CopyTransformModifier3D::_get_property_list calls BoneConstraint3D::get_property_list (every setting's constraint keys)
    // before it lists every setting's own keys.
    [Fact]
    public void ACopyTransformModifierListsConstraintsThenItsOwnKeys()
    {
        TscnDocument scene = Back(
            $$"""
            <CopyTransformModifier3D {{X}} Name="Root">
              <x:Prop Name="settings/0/additive" Value="true" />
              <x:Prop Name="settings/0/amount" Value="0.5" />
              <x:Prop Name="settings/0/apply_bone" Value="0" />
              <x:Prop Name="settings/0/axes" Value="3" />
              <x:Prop Name="settings/0/copy" Value="1" />
              <x:Prop Name="settings/0/invert" Value="2" />
              <x:Prop Name="settings/0/reference_bone" Value="1" />
              <x:Prop Name="settings/0/reference_type" Value="0" />
              <x:Prop Name="settings/0/relative" Value="false" />
              <x:Prop Name="settings/10/amount" Value="0.25" />
              <x:Prop Name="settings/10/copy" Value="2" />
              <x:Prop Name="settings/10/reference_bone" Value="3" />
            </CopyTransformModifier3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/amount",
                "settings/0/apply_bone",
                "settings/0/reference_type",
                "settings/0/reference_bone",
                "settings/10/amount",
                "settings/10/reference_bone",
                "settings/0/copy",
                "settings/0/axes",
                "settings/0/invert",
                "settings/0/relative",
                "settings/0/additive",
                "settings/10/copy",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // ConvertTransformModifier3D::_get_property_list calls BoneConstraint3D::get_property_list (every setting's constraint
    // keys) before it lists every setting's own keys.
    [Fact]
    public void AConvertTransformModifierListsConstraintsThenItsOwnKeys()
    {
        TscnDocument scene = Back(
            $$"""
            <ConvertTransformModifier3D {{X}} Name="Root">
              <x:Prop Name="settings/0/additive" Value="true" />
              <x:Prop Name="settings/0/amount" Value="0.5" />
              <x:Prop Name="settings/0/apply/axis" Value="1" />
              <x:Prop Name="settings/0/apply/range_min" Value="-1.0" />
              <x:Prop Name="settings/0/apply/transform_mode" Value="1" />
              <x:Prop Name="settings/0/apply_bone" Value="0" />
              <x:Prop Name="settings/0/reference/axis" Value="2" />
              <x:Prop Name="settings/0/reference/transform_mode" Value="2" />
              <x:Prop Name="settings/0/reference_bone" Value="1" />
              <x:Prop Name="settings/0/reference_type" Value="0" />
              <x:Prop Name="settings/0/relative" Value="false" />
              <x:Prop Name="settings/10/amount" Value="0.25" />
              <x:Prop Name="settings/10/apply/axis" Value="2" />
              <x:Prop Name="settings/10/reference_bone" Value="3" />
            </ConvertTransformModifier3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/amount",
                "settings/0/apply_bone",
                "settings/0/reference_type",
                "settings/0/reference_bone",
                "settings/10/amount",
                "settings/10/reference_bone",
                "settings/0/apply/transform_mode",
                "settings/0/apply/axis",
                "settings/0/apply/range_min",
                "settings/0/reference/transform_mode",
                "settings/0/reference/axis",
                "settings/0/relative",
                "settings/0/additive",
                "settings/10/apply/axis",
            ],
            Names(scene.Nodes[0].Properties)
        );
    }

    // BoneTwistDisperser3D::_get_property_list lists each setting's keys in turn, its joints after joint_count in numeric
    // order.
    [Fact]
    public void ABoneTwistDisperserSettingListsItsJointsInNumericOrderAfterItsKeys()
    {
        TscnDocument scene = Back(
            $$"""
            <BoneTwistDisperser3D {{X}} Name="Root">
              <x:Prop Name="settings/0/disperse_mode" Value="2" />
              <x:Prop Name="settings/0/end_bone" Value="11" />
              <x:Prop Name="settings/0/end_bone_direction" Value="2" />
              <x:Prop Name="settings/0/extend_end_bone" Value="true" />
              <x:Prop Name="settings/0/joint_count" Value="12" />
              <x:Prop Name="settings/0/joints/0/twist_amount" Value="0.5" />
              <x:Prop Name="settings/0/joints/1/twist_amount" Value="0.25" />
              <x:Prop Name="settings/0/joints/10/twist_amount" Value="0.75" />
              <x:Prop Name="settings/0/joints/2/twist_amount" Value="0.125" />
              <x:Prop Name="settings/0/root_bone" Value="0" />
              <x:Prop Name="settings/10/disperse_mode" Value="1" />
              <x:Prop Name="settings/10/end_bone" Value="3" />
              <x:Prop Name="settings/10/joint_count" Value="2" />
              <x:Prop Name="settings/10/joints/0/twist_amount" Value="0.5" />
              <x:Prop Name="settings/10/root_bone" Value="2" />
              <x:Prop Name="settings/10/twist_from" Value="Quaternion(0, 0.6, 0, 0.8)" />
              <x:Prop Name="settings/10/twist_from_rest" Value="false" />
              <x:Prop Name="settings/10/weight_position" Value="0.25" />
            </BoneTwistDisperser3D>
            """
        );

        Assert.Equal(
            [
                "settings/0/root_bone",
                "settings/0/end_bone",
                "settings/0/extend_end_bone",
                "settings/0/end_bone_direction",
                "settings/0/disperse_mode",
                "settings/0/joint_count",
                "settings/0/joints/0/twist_amount",
                "settings/0/joints/1/twist_amount",
                "settings/0/joints/2/twist_amount",
                "settings/0/joints/10/twist_amount",
                "settings/10/root_bone",
                "settings/10/end_bone",
                "settings/10/twist_from_rest",
                "settings/10/twist_from",
                "settings/10/disperse_mode",
                "settings/10/weight_position",
                "settings/10/joint_count",
                "settings/10/joints/0/twist_amount",
            ],
            Names(scene.Nodes[0].Properties)
        );
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
