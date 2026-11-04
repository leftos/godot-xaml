using GodotXaml.Core.Tscn;
using static GodotXaml.Core.Tests.Conversion.BackHelpers;

namespace GodotXaml.Core.Tests.Conversion;

// A dynamic name (one a class adds in _get_property_list, which ClassDB does not list) goes in its declaring class's place.
public sealed class PropertyOrderTests
{
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

        Assert.Equal(["libraries/lib", "current_animation"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["speed_scale", "blend_times", "script"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["current", "script"], PropertyNames(scene.Nodes[0].Properties));
    }

    [Fact]
    public void ADynamicNameComesAfterTheClassList()
    {
        TscnDocument scene = Back(
            $$"""
            <PopupMenu {{X}} Name="Root">
              <x:Prop Name="item_0/text" Value='"One"' />
              <x:Prop Name="item_count" Value="1" />
            </PopupMenu>
            """
        );

        Assert.Equal(["item_count", "item_0/text"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["instance_shader_parameters/p", "mesh", "surface_material_override/0"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["instance_shader_parameters/p", "texture"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["script", "libraries/x"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(["render_priority", "shader", "shader_parameter/c", "script"], PropertyNames(Assert.Single(scene.SubResources).Properties));
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

        Assert.Equal(["current", "script", "alpha", "metadata/m"], PropertyNames(scene.Nodes[0].Properties));
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

        Assert.Equal(
            ["tracks/0/type", "tracks/0/path", "tracks/0/interp", "tracks/2/type", "tracks/10/type"],
            PropertyNames(resource.ResourceProperties)
        );
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

        Assert.Equal(
            ["bones/0/name", "bones/0/parent", "bones/0/enabled", "bones/2/name", "bones/10/name"],
            PropertyNames(scene.Nodes[0].Properties)
        );
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
            PropertyNames(resource.ResourceProperties)
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

        Assert.Equal(["bones/0/name", "bones/0/rest", "bones/0/bone_meta/tag"], PropertyNames(scene.Nodes[0].Properties));
    }

    // Skeleton3D::_set reads the 3.x pose before bound_children, after every 4.x key of the bone (skeleton_3d.cpp:110-135).
    [Fact]
    public void ASkeletonBonesDeprecatedKeysComeAfterItsOwn()
    {
        TscnDocument scene = Back(
            $$"""
            <Skeleton3D {{X}} Name="Root">
              <x:Prop Name="bones/0/bound_children" Value="PackedInt32Array(1, 2)" />
              <x:Prop Name="bones/0/pose" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)" />
              <x:Prop Name="bones/0/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)" />
            </Skeleton3D>
            """
        );

        Assert.Equal(["bones/0/rest", "bones/0/pose", "bones/0/bound_children"], PropertyNames(scene.Nodes[0].Properties));
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
            PropertyNames(resource.ResourceProperties)
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
            PropertyNames(resource.ResourceProperties)
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
            PropertyNames(resource.ResourceProperties)
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

        Assert.Equal(["nodes/other/3/node", "nodes/other/3/position", "nodes/other/3/expression"], PropertyNames(resource.ResourceProperties));
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
            PropertyNames(resource.ResourceProperties)
        );
    }

    // AnimationNodeTransition::_set adds an input only on input_<count>/name; input_count is not stored.
    [Fact]
    public void ATransitionInputStartsWithItsNameAndInputsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <AnimationNodeTransition {{X}}>
              <x:Prop Name="input_0/auto_advance" Value="true" />
              <x:Prop Name="input_0/break_loop_at_end" Value="false" />
              <x:Prop Name="input_0/name" Value='"in_0"' />
              <x:Prop Name="input_0/reset" Value="true" />
              <x:Prop Name="input_1/auto_advance" Value="false" />
              <x:Prop Name="input_1/break_loop_at_end" Value="true" />
              <x:Prop Name="input_1/name" Value='"in_1"' />
              <x:Prop Name="input_1/reset" Value="false" />
              <x:Prop Name="input_10/auto_advance" Value="false" />
              <x:Prop Name="input_10/break_loop_at_end" Value="false" />
              <x:Prop Name="input_10/name" Value='"in_10"' />
              <x:Prop Name="input_10/reset" Value="true" />
              <x:Prop Name="input_2/auto_advance" Value="false" />
              <x:Prop Name="input_2/break_loop_at_end" Value="false" />
              <x:Prop Name="input_2/name" Value='"in_2"' />
              <x:Prop Name="input_2/reset" Value="true" />
            </AnimationNodeTransition>
            """
        );

        Assert.Equal(
            [
                "input_0/name",
                "input_0/auto_advance",
                "input_0/break_loop_at_end",
                "input_0/reset",
                "input_1/name",
                "input_1/auto_advance",
                "input_1/break_loop_at_end",
                "input_1/reset",
                "input_2/name",
                "input_2/auto_advance",
                "input_2/break_loop_at_end",
                "input_2/reset",
                "input_10/name",
                "input_10/auto_advance",
                "input_10/break_loop_at_end",
                "input_10/reset",
            ],
            PropertyNames(resource.ResourceProperties)
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

        Assert.Equal(["pattern_0", "pattern_1", "pattern_2", "pattern_10"], PropertyNames(resource.ResourceProperties));
    }

    // TileSet::_get_property_list lists each layer kind's layers in numeric order, each layer's keys in a fixed order, and each
    // terrain set's mode before its terrains, each terrain's name before its color.
    [Fact]
    public void TileSetLayersRunInNumericOrderEachInItsKeyOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSet {{X}}>
              <x:Prop Name="custom_data_layer_10/name" Value='"b"' />
              <x:Prop Name="custom_data_layer_2/name" Value='"a"' />
              <x:Prop Name="custom_data_layer_2/type" Value="2" />
              <x:Prop Name="navigation_layer_10/layers" Value="4" />
              <x:Prop Name="navigation_layer_2/layers" Value="2" />
              <x:Prop Name="occlusion_layer_10/light_mask" Value="4" />
              <x:Prop Name="occlusion_layer_2/light_mask" Value="2" />
              <x:Prop Name="occlusion_layer_2/sdf_collision" Value="true" />
              <x:Prop Name="physics_layer_10/collision_layer" Value="4" />
              <x:Prop Name="physics_layer_2/collision_layer" Value="2" />
              <x:Prop Name="physics_layer_2/collision_priority" Value="3.0" />
              <x:Prop Name="terrain_set_10/mode" Value="1" />
              <x:Prop Name="terrain_set_2/mode" Value="2" />
              <x:Prop Name="terrain_set_2/terrain_0/color" Value="Color(1, 0, 0, 1)" />
              <x:Prop Name="terrain_set_2/terrain_0/name" Value='"t"' />
            </TileSet>
            """
        );

        Assert.Equal(
            [
                "occlusion_layer_2/light_mask",
                "occlusion_layer_2/sdf_collision",
                "occlusion_layer_10/light_mask",
                "physics_layer_2/collision_layer",
                "physics_layer_2/collision_priority",
                "physics_layer_10/collision_layer",
                "terrain_set_2/mode",
                "terrain_set_2/terrain_0/name",
                "terrain_set_2/terrain_0/color",
                "terrain_set_10/mode",
                "navigation_layer_2/layers",
                "navigation_layer_10/layers",
                "custom_data_layer_2/name",
                "custom_data_layer_2/type",
                "custom_data_layer_10/name",
            ],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // TileSet::_get_property_list lists the tile proxies source level, coords level, then alternative level, after the sources.
    [Fact]
    public void TileSetProxiesRunSourceCoordsAlternativeAfterTheSources()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSet {{X}}>
              <x:Prop Name="tile_proxies/alternative_level" Value="[]" />
              <x:Prop Name="tile_proxies/coords_level" Value="[]" />
              <x:Prop Name="tile_proxies/source_level" Value="[]" />
              <x:Prop Name="sources/0" Value='Resource("res://s.tres")' />
            </TileSet>
            """
        );

        Assert.Equal(
            ["sources/0", "tile_proxies/source_level", "tile_proxies/coords_level", "tile_proxies/alternative_level"],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // TileSetScenesCollectionSource::_get_property_list lists its scene tiles by id (create_scene_tile sorts scenes_ids), each
    // tile's scene, then display_placeholder.
    [Fact]
    public void ScenesCollectionTilesRunInIdOrderSceneFirst()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSetScenesCollectionSource {{X}}>
              <x:Prop Name="scenes/1/scene" Value='Resource("res://a.tscn")' />
              <x:Prop Name="scenes/10/display_placeholder" Value="true" />
              <x:Prop Name="scenes/10/scene" Value='Resource("res://a.tscn")' />
              <x:Prop Name="scenes/2/display_placeholder" Value="true" />
              <x:Prop Name="scenes/2/scene" Value='Resource("res://a.tscn")' />
            </TileSetScenesCollectionSource>
            """
        );

        Assert.Equal(
            ["scenes/1/scene", "scenes/2/scene", "scenes/2/display_placeholder", "scenes/10/scene", "scenes/10/display_placeholder"],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // TileData::set_terrain and set_terrain_peering_bit fail while the alternative's terrain_set is unset (tile_set.cpp), and
    // TileSetAtlasSource::_set creates a tile and an alternative on any of its keys, so every terrain key comes after every
    // other tile key.
    [Fact]
    public void AnAtlasTileSetsItsTerrainSetBeforeItsTerrainAndPeeringBits()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSetAtlasSource {{X}}>
              <x:Prop Name="0:0/0" Value="0" />
              <x:Prop Name="0:0/0/terrain" Value="0" />
              <x:Prop Name="0:0/0/terrain_set" Value="0" />
              <x:Prop Name="0:0/0/terrains_peering_bit/right_side" Value="1" />
              <x:Prop Name="1:0/0" Value="0" />
              <x:Prop Name="1:0/0/terrain" Value="1" />
              <x:Prop Name="1:0/0/terrain_set" Value="0" />
            </TileSetAtlasSource>
            """
        );

        Assert.Equal(
            ["0:0/0", "0:0/0/terrain_set", "1:0/0", "1:0/0/terrain_set", "0:0/0/terrain", "1:0/0/terrain", "0:0/0/terrains_peering_bit/right_side"],
            PropertyNames(resource.ResourceProperties)
        );
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
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
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // SkeletonModification2DCCDIK::_get_property_list lists the joints in numeric order, each joint's keys in a fixed order.
    [Fact]
    public void ACcdikJointListsItsBoneFirstAndJointsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SkeletonModification2DCcdik {{X}} CcdikDataChainLength="11">
              <x:Prop Name="joint_data/0/bone2d_node" Value='NodePath("")' />
              <x:Prop Name="joint_data/0/bone_index" Value="0" />
              <x:Prop Name="joint_data/0/constraint_angle_invert" Value="true" />
              <x:Prop Name="joint_data/0/constraint_angle_max" Value="10.0" />
              <x:Prop Name="joint_data/0/constraint_angle_min" Value="-5.0" />
              <x:Prop Name="joint_data/0/constraint_in_localspace" Value="false" />
              <x:Prop Name="joint_data/0/enable_constraint" Value="true" />
              <x:Prop Name="joint_data/0/rotate_from_joint" Value="true" />
              <x:Prop Name="joint_data/10/bone2d_node" Value='NodePath("")' />
              <x:Prop Name="joint_data/10/bone_index" Value="10" />
              <x:Prop Name="joint_data/10/enable_constraint" Value="false" />
              <x:Prop Name="joint_data/10/rotate_from_joint" Value="false" />
              <x:Prop Name="joint_data/2/bone_index" Value="2" />
            </SkeletonModification2DCcdik>
            """
        );

        Assert.Equal(
            [
                "ccdik_data_chain_length",
                "joint_data/0/bone_index",
                "joint_data/0/bone2d_node",
                "joint_data/0/rotate_from_joint",
                "joint_data/0/enable_constraint",
                "joint_data/0/constraint_angle_min",
                "joint_data/0/constraint_angle_max",
                "joint_data/0/constraint_angle_invert",
                "joint_data/0/constraint_in_localspace",
                "joint_data/2/bone_index",
                "joint_data/10/bone_index",
                "joint_data/10/bone2d_node",
                "joint_data/10/rotate_from_joint",
                "joint_data/10/enable_constraint",
            ],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // SkeletonModification2DFABRIK::_get_property_list lists the joints in numeric order, each joint's keys in a fixed order.
    [Fact]
    public void AFabrikJointListsItsBoneFirstAndJointsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SkeletonModification2DFabrik {{X}} FabrikDataChainLength="11">
              <x:Prop Name="joint_data/1/bone2d_node" Value='NodePath("")' />
              <x:Prop Name="joint_data/1/bone_index" Value="1" />
              <x:Prop Name="joint_data/1/magnet_position" Value="Vector2(1, 2)" />
              <x:Prop Name="joint_data/10/bone2d_node" Value='NodePath("")' />
              <x:Prop Name="joint_data/10/bone_index" Value="10" />
              <x:Prop Name="joint_data/10/magnet_position" Value="Vector2(3, 4)" />
              <x:Prop Name="joint_data/10/use_target_rotation" Value="true" />
              <x:Prop Name="joint_data/2/bone_index" Value="2" />
            </SkeletonModification2DFabrik>
            """
        );

        Assert.Equal(
            [
                "fabrik_data_chain_length",
                "joint_data/1/bone_index",
                "joint_data/1/bone2d_node",
                "joint_data/1/magnet_position",
                "joint_data/2/bone_index",
                "joint_data/10/bone_index",
                "joint_data/10/bone2d_node",
                "joint_data/10/magnet_position",
                "joint_data/10/use_target_rotation",
            ],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // SkeletonModification2DJiggle::_get_property_list lists use_colliders and collision_mask, then the joints in numeric
    // order, each joint's keys in a fixed order.
    [Fact]
    public void AJiggleJointListsItsBoneFirstAndJointsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SkeletonModification2DJiggle {{X}} JiggleDataChainLength="11">
              <x:Prop Name="collision_mask" Value="6" />
              <x:Prop Name="joint_data/0/bone2d_node" Value='NodePath("")' />
              <x:Prop Name="joint_data/0/bone_index" Value="0" />
              <x:Prop Name="joint_data/0/damping" Value="0.5" />
              <x:Prop Name="joint_data/0/gravity" Value="Vector2(0, 6)" />
              <x:Prop Name="joint_data/0/mass" Value="0.5" />
              <x:Prop Name="joint_data/0/override_defaults" Value="true" />
              <x:Prop Name="joint_data/0/stiffness" Value="2.0" />
              <x:Prop Name="joint_data/0/use_gravity" Value="true" />
              <x:Prop Name="joint_data/10/bone_index" Value="10" />
              <x:Prop Name="joint_data/2/bone_index" Value="2" />
              <x:Prop Name="use_colliders" Value="true" />
            </SkeletonModification2DJiggle>
            """
        );

        Assert.Equal(
            [
                "jiggle_data_chain_length",
                "use_colliders",
                "collision_mask",
                "joint_data/0/bone_index",
                "joint_data/0/bone2d_node",
                "joint_data/0/override_defaults",
                "joint_data/0/stiffness",
                "joint_data/0/mass",
                "joint_data/0/damping",
                "joint_data/0/use_gravity",
                "joint_data/0/gravity",
                "joint_data/2/bone_index",
                "joint_data/10/bone_index",
            ],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // SkeletonModification2DPhysicalBones::_get_property_list lists the joints' node paths in numeric order.
    [Fact]
    public void PhysicalBoneJointsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SkeletonModification2DPhysicalBones {{X}} PhysicalBoneChainLength="11">
              <x:Prop Name="joint_0_nodepath" Value='NodePath("A")' />
              <x:Prop Name="joint_10_nodepath" Value='NodePath("C")' />
              <x:Prop Name="joint_2_nodepath" Value='NodePath("B")' />
            </SkeletonModification2DPhysicalBones>
            """
        );

        Assert.Equal(
            ["physical_bone_chain_length", "joint_0_nodepath", "joint_2_nodepath", "joint_10_nodepath"],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // SkeletonModificationStack2D::_get_property_list lists the modifications in numeric order.
    [Fact]
    public void StackModificationsRunInNumericOrder()
    {
        TscnDocument resource = BackResource(
            $$"""
            <SkeletonModificationStack2D {{X}} ModificationCount="11">
              <x:Prop Name="modifications/0" Value='Resource("res://m0.tres")' />
              <x:Prop Name="modifications/10" Value='Resource("res://m10.tres")' />
              <x:Prop Name="modifications/2" Value='Resource("res://m2.tres")' />
            </SkeletonModificationStack2D>
            """
        );

        Assert.Equal(["modification_count", "modifications/0", "modifications/2", "modifications/10"], PropertyNames(resource.ResourceProperties));
    }

    // PhysicalBone3D::_get_property_list lists its JointData's keys after bone_name, each joint type's keys in a fixed order
    // (ConeJointData, SliderJointData::_get_property_list).
    [Fact]
    public void APhysicalBoneJointListsItsConstraintsInItsJointTypesOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <Node3D {{X}} Name="Root">
              <PhysicalBone3D Name="Cone" JointType="Cone" bone_name='"a"'>
                <x:Prop Name="joint_constraints/bias" Value="0.3" />
                <x:Prop Name="joint_constraints/relaxation" Value="1.0" />
                <x:Prop Name="joint_constraints/softness" Value="0.2" />
                <x:Prop Name="joint_constraints/swing_span" Value="30.0" />
                <x:Prop Name="joint_constraints/twist_span" Value="90.0" />
              </PhysicalBone3D>
              <PhysicalBone3D Name="Slider" JointType="Slider" bone_name='"b"'>
                <x:Prop Name="joint_constraints/angular_limit_damping" Value="0.4" />
                <x:Prop Name="joint_constraints/angular_limit_lower" Value="0.0" />
                <x:Prop Name="joint_constraints/angular_limit_upper" Value="0.0" />
                <x:Prop Name="joint_constraints/linear_limit_lower" Value="-2.0" />
                <x:Prop Name="joint_constraints/linear_limit_upper" Value="2.0" />
              </PhysicalBone3D>
            </Node3D>
            """
        );

        Assert.Equal(
            [
                "joint_type",
                "bone_name",
                "joint_constraints/swing_span",
                "joint_constraints/twist_span",
                "joint_constraints/bias",
                "joint_constraints/softness",
                "joint_constraints/relaxation",
            ],
            PropertyNames(scene.Nodes[1].Properties)
        );
        Assert.Equal(
            [
                "joint_type",
                "bone_name",
                "joint_constraints/linear_limit_upper",
                "joint_constraints/linear_limit_lower",
                "joint_constraints/angular_limit_upper",
                "joint_constraints/angular_limit_lower",
                "joint_constraints/angular_limit_damping",
            ],
            PropertyNames(scene.Nodes[2].Properties)
        );
    }

    // SixDOFJointData::_get_property_list lists the axes x, y, z, each axis's keys in a fixed order.
    [Fact]
    public void ASixDofJointListsItsAxesInOrderEachWithItsKeysInOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <PhysicalBone3D {{X}} Name="Root" JointType="Type6Dof">
              <x:Prop Name="joint_constraints/x/angular_damping" Value="1.0" />
              <x:Prop Name="joint_constraints/x/linear_limit_enabled" Value="false" />
              <x:Prop Name="joint_constraints/y/erp" Value="0.5" />
              <x:Prop Name="joint_constraints/y/linear_spring_stiffness" Value="5.0" />
              <x:Prop Name="joint_constraints/z/angular_equilibrium_point" Value="0.0" />
              <x:Prop Name="joint_constraints/z/linear_limit_upper" Value="0.0" />
            </PhysicalBone3D>
            """
        );

        Assert.Equal(
            [
                "joint_type",
                "joint_constraints/x/linear_limit_enabled",
                "joint_constraints/x/angular_damping",
                "joint_constraints/y/linear_spring_stiffness",
                "joint_constraints/y/erp",
                "joint_constraints/z/linear_limit_upper",
                "joint_constraints/z/angular_equilibrium_point",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // SoftBody3D::_get_property_list lists pinned_points, then the attachments in numeric order, each attachment's keys in a
    // fixed order.
    [Fact]
    public void ASoftBodyAttachmentListsItsPointFirstAndAttachmentsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <SoftBody3D {{X}} Name="Root" pinned_points="[1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21]">
              <x:Prop Name="attachments/0/offset" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="attachments/0/point_index" Value="1" />
              <x:Prop Name="attachments/0/spatial_attachment_path" Value='NodePath("../Anchor")' />
              <x:Prop Name="attachments/10/offset" Value="Vector3(0.5, 10, -0.5)" />
              <x:Prop Name="attachments/10/point_index" Value="21" />
              <x:Prop Name="attachments/2/point_index" Value="5" />
            </SoftBody3D>
            """
        );

        Assert.Equal(
            [
                "pinned_points",
                "attachments/0/point_index",
                "attachments/0/spatial_attachment_path",
                "attachments/0/offset",
                "attachments/2/point_index",
                "attachments/10/point_index",
                "attachments/10/offset",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // environment.cpp registers glow_levels/1..7 after glow_enabled and before glow_normalized, ahead of glow_intensity.
    [Fact]
    public void AClassDbSlashNameGoesAtItsClassListPlace()
    {
        TscnDocument resource = BackResource(
            $$"""
            <Environment {{X}} GlowEnabled="true" GlowIntensity="1.5" GlowNormalized="true">
              <x:Prop Name="glow_levels/1" Value="0.5" />
            </Environment>
            """
        );

        Assert.Equal(["glow_enabled", "glow_levels/1", "glow_normalized", "glow_intensity"], PropertyNames(resource.ResourceProperties));
    }

    // hinge_joint_3d.cpp registers params/bias, then angular_limit/..., then motor/..., all after Joint3D's own list.
    [Fact]
    public void AJointsSlashNamesGoInTheirClassListOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <HingeJoint3D {{X}} Name="Root" NodeA="../A">
              <x:Prop Name="motor/enable" Value="true" />
              <x:Prop Name="angular_limit/upper" Value="1.0" />
              <x:Prop Name="params/bias" Value="0.5" />
              <x:Prop Name="angular_limit/enable" Value="true" />
            </HingeJoint3D>
            """
        );

        Assert.Equal(
            ["node_a", "params/bias", "angular_limit/enable", "angular_limit/upper", "motor/enable"],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // AnimationPlayer::_get_property_list adds next/ before blend_times, so the first family's names come first.
    [Fact]
    public void TwoFamiliesOfOneClassKeepTheTablesOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <AnimationPlayer {{X}} Name="Root">
              <x:Prop Name="blend_times" Value="[]" />
              <x:Prop Name="next/idle" Value='"walk"' />
            </AnimationPlayer>
            """
        );

        Assert.Equal(["next/idle", "blend_times"], PropertyNames(scene.Nodes[0].Properties));
    }

    // Control's theme overrides rank at Control's place in the chain, before a family a deeper class adds.
    [Fact]
    public void ThemeOverridesComeBeforeADeeperClassesDynamicPropertyNames()
    {
        TscnDocument scene = Back(
            $$"""
            <GraphNode {{X}} Name="Root" ThemeOverrideColors.resizer_color="1, 0, 0, 1">
              <x:Prop Name="slot/0/left_icon" Value='Resource("res://i.png")' />
            </GraphNode>
            """
        );

        Assert.Equal(["theme_override_colors/resizer_color", "slot/0/left_icon"], PropertyNames(scene.Nodes[0].Properties));
    }

    // Theme::_get_property_list names a data type's base_type and its type-keyed values after Theme's own list, before the
    // script; a name no family names is a script's own, ranked after every class-list name however early it stands.
    [Fact]
    public void AThemesTypeKeysComeAfterItsListAndBeforeTheScript()
    {
        TscnDocument scene = Back(
            $$"""
            <Label {{X}} Name="Root">
              <Label.Theme>
                <Theme Script="res://t.gd" DefaultFontSize="20">
                  <x:Prop Name="other/x" Value="1" />
                  <x:Prop Name="Label/base_type" Value='&amp;"Label"' />
                  <x:Prop Name="Label/colors/font_color" Value="Color(1, 0, 0, 1)" />
                </Theme>
              </Label.Theme>
            </Label>
            """
        );

        Assert.Equal(
            ["default_font_size", "Label/base_type", "Label/colors/font_color", "script", "other/x"],
            PropertyNames(Assert.Single(scene.SubResources).Properties)
        );
    }

    // TileSetAtlasSource::_get_property_list names a tile <x>:<y>/..., in its family's place rather than a slash-named tail.
    [Fact]
    public void AnAtlasTileNameMatchesItsFamily()
    {
        TscnDocument resource = BackResource(
            $$"""
            <TileSetAtlasSource {{X}}>
              <x:Prop Name="other/x" Value="1" />
              <x:Prop Name="0:0/size_in_atlas" Value="Vector2i(1, 1)" />
            </TileSetAtlasSource>
            """
        );

        Assert.Equal(["0:0/size_in_atlas", "other/x"], PropertyNames(resource.ResourceProperties));
    }

    // AudioEffectEQ::_get_property_list names a band band_db/<frequency>_hz.
    [Fact]
    public void AnEqBandNameMatchesItsFamily()
    {
        TscnDocument resource = BackResource(
            $$"""
            <AudioEffectEQ {{X}}>
              <x:Prop Name="other/x" Value="1" />
              <x:Prop Name="band_db/32_hz" Value="1.0" />
            </AudioEffectEQ>
            """
        );

        Assert.Equal(["band_db/32_hz", "other/x"], PropertyNames(resource.ResourceProperties));
    }

    // A key the family's key order lacks goes after every listed key of its entry, in source order.
    [Fact]
    public void AnUnknownKeyGoesAfterTheListedKeysOfItsEntry()
    {
        TscnDocument resource = BackResource(
            $$"""
            <Animation {{X}}>
              <x:Prop Name="tracks/0/zzz" Value="1" />
              <x:Prop Name="tracks/0/type" Value='"value"' />
              <x:Prop Name="tracks/0/aaa" Value="1" />
              <x:Prop Name="tracks/0/use_blend" Value="true" />
            </Animation>
            """
        );

        Assert.Equal(["tracks/0/type", "tracks/0/use_blend", "tracks/0/zzz", "tracks/0/aaa"], PropertyNames(resource.ResourceProperties));
    }

    // AnimationNodeBlendSpace1D::_set adds a point only on blend_point_<count>/node.
    [Fact]
    public void ABlendSpace1DPointStartsWithItsNode()
    {
        TscnDocument resource = BackResource(
            $$"""
            <AnimationNodeBlendSpace1D {{X}}>
              <x:Prop Name="blend_point_1/name" Value='&amp;"b"' />
              <x:Prop Name="blend_point_0/pos" Value="1.0" />
              <x:Prop Name="blend_point_1/node" Value='Resource("res://b.tres")' />
              <x:Prop Name="blend_point_0/node" Value='Resource("res://a.tres")' />
            </AnimationNodeBlendSpace1D>
            """
        );

        Assert.Equal(
            ["blend_point_0/node", "blend_point_0/pos", "blend_point_1/node", "blend_point_1/name"],
            PropertyNames(resource.ResourceProperties)
        );
    }

    // An index past int's range ranks last in its family, after every index Godot could hold.
    [Fact]
    public void AnIndexPastIntsRangeRanksLastInItsFamily()
    {
        TscnDocument resource = BackResource(
            $$"""
            <Animation {{X}}>
              <x:Prop Name="tracks/99999999999999999999/type" Value='"value"' />
              <x:Prop Name="tracks/5/type" Value='"value"' />
            </Animation>
            """
        );

        Assert.Equal(["tracks/5/type", "tracks/99999999999999999999/type"], PropertyNames(resource.ResourceProperties));
    }

    // TabBar::_get_property_list (PropertyListHelper, tab_bar.cpp) lists a tab's keys title, tooltip, icon and disabled,
    // the tabs in numeric order past ten.
    [Fact]
    public void ATabBarTabListsItsTitleFirstAndTabsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <TabBar {{X}} Name="Root" tab_count="12">
              <x:Prop Name="tab_10/disabled" Value="true" />
              <x:Prop Name="tab_10/icon" Value='Resource("res://t.tres")' />
              <x:Prop Name="tab_10/title" Value='"Ten"' />
              <x:Prop Name="tab_10/tooltip" Value='"Tip"' />
              <x:Prop Name="tab_2/disabled" Value="true" />
              <x:Prop Name="tab_2/icon" Value='Resource("res://t.tres")' />
              <x:Prop Name="tab_2/title" Value='"Two"' />
              <x:Prop Name="tab_2/tooltip" Value='"Tip"' />
            </TabBar>
            """
        );

        Assert.Equal(
            [
                "tab_count",
                "tab_2/title",
                "tab_2/tooltip",
                "tab_2/icon",
                "tab_2/disabled",
                "tab_10/title",
                "tab_10/tooltip",
                "tab_10/icon",
                "tab_10/disabled",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // PopupMenu::_get_property_list (PropertyListHelper, popup_menu.cpp) lists an item's keys text, icon, checkable,
    // checked, id, disabled and separator, the items in numeric order past ten.
    [Fact]
    public void APopupMenuItemListsItsTextFirstAndItemsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <PopupMenu {{X}} Name="Root" item_count="12">
              <x:Prop Name="item_10/checkable" Value="1" />
              <x:Prop Name="item_10/checked" Value="true" />
              <x:Prop Name="item_10/disabled" Value="true" />
              <x:Prop Name="item_10/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="item_10/id" Value="110" />
              <x:Prop Name="item_10/separator" Value="false" />
              <x:Prop Name="item_10/text" Value='"Ten"' />
              <x:Prop Name="item_2/checkable" Value="1" />
              <x:Prop Name="item_2/checked" Value="true" />
              <x:Prop Name="item_2/disabled" Value="true" />
              <x:Prop Name="item_2/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="item_2/id" Value="102" />
              <x:Prop Name="item_2/separator" Value="false" />
              <x:Prop Name="item_2/text" Value='"Two"' />
            </PopupMenu>
            """
        );

        Assert.Equal(
            [
                "item_count",
                "item_2/text",
                "item_2/icon",
                "item_2/checkable",
                "item_2/checked",
                "item_2/id",
                "item_2/disabled",
                "item_2/separator",
                "item_10/text",
                "item_10/icon",
                "item_10/checkable",
                "item_10/checked",
                "item_10/id",
                "item_10/disabled",
                "item_10/separator",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // ItemList::_get_property_list (PropertyListHelper, item_list.cpp) lists an item's keys text, icon, selectable and
    // disabled, the items in numeric order past ten.
    [Fact]
    public void AnItemListItemListsItsTextFirstAndItemsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <ItemList {{X}} Name="Root" item_count="12">
              <x:Prop Name="item_10/disabled" Value="true" />
              <x:Prop Name="item_10/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="item_10/selectable" Value="false" />
              <x:Prop Name="item_10/text" Value='"Ten"' />
              <x:Prop Name="item_2/disabled" Value="true" />
              <x:Prop Name="item_2/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="item_2/selectable" Value="false" />
              <x:Prop Name="item_2/text" Value='"Two"' />
            </ItemList>
            """
        );

        Assert.Equal(
            [
                "item_count",
                "item_2/text",
                "item_2/icon",
                "item_2/selectable",
                "item_2/disabled",
                "item_10/text",
                "item_10/icon",
                "item_10/selectable",
                "item_10/disabled",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // OptionButton::_get_property_list (PropertyListHelper, option_button.cpp) lists an item's keys text, icon, id,
    // disabled and separator under popup/item_N/, the items in numeric order past ten.
    [Fact]
    public void AnOptionButtonItemListsItsTextFirstAndItemsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <OptionButton {{X}} Name="Root" item_count="12">
              <x:Prop Name="popup/item_10/disabled" Value="true" />
              <x:Prop Name="popup/item_10/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="popup/item_10/id" Value="110" />
              <x:Prop Name="popup/item_10/separator" Value="false" />
              <x:Prop Name="popup/item_10/text" Value='"Ten"' />
              <x:Prop Name="popup/item_2/disabled" Value="true" />
              <x:Prop Name="popup/item_2/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="popup/item_2/id" Value="102" />
              <x:Prop Name="popup/item_2/separator" Value="false" />
              <x:Prop Name="popup/item_2/text" Value='"Two"' />
            </OptionButton>
            """
        );

        Assert.Equal(
            [
                "item_count",
                "popup/item_2/text",
                "popup/item_2/icon",
                "popup/item_2/id",
                "popup/item_2/disabled",
                "popup/item_2/separator",
                "popup/item_10/text",
                "popup/item_10/icon",
                "popup/item_10/id",
                "popup/item_10/disabled",
                "popup/item_10/separator",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // MenuButton::_get_property_list (PropertyListHelper, menu_button.cpp) lists an item's keys text, icon, checkable,
    // checked, id, disabled and separator under popup/item_N/, the items in numeric order past ten.
    [Fact]
    public void AMenuButtonItemListsItsTextFirstAndItemsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <MenuButton {{X}} Name="Root" item_count="12">
              <x:Prop Name="popup/item_10/checkable" Value="1" />
              <x:Prop Name="popup/item_10/checked" Value="true" />
              <x:Prop Name="popup/item_10/disabled" Value="true" />
              <x:Prop Name="popup/item_10/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="popup/item_10/id" Value="310" />
              <x:Prop Name="popup/item_10/separator" Value="false" />
              <x:Prop Name="popup/item_10/text" Value='"Ten"' />
              <x:Prop Name="popup/item_2/checkable" Value="1" />
              <x:Prop Name="popup/item_2/checked" Value="true" />
              <x:Prop Name="popup/item_2/disabled" Value="true" />
              <x:Prop Name="popup/item_2/icon" Value='Resource("res://a.tres")' />
              <x:Prop Name="popup/item_2/id" Value="302" />
              <x:Prop Name="popup/item_2/separator" Value="false" />
              <x:Prop Name="popup/item_2/text" Value='"Two"' />
            </MenuButton>
            """
        );

        Assert.Equal(
            [
                "item_count",
                "popup/item_2/text",
                "popup/item_2/icon",
                "popup/item_2/checkable",
                "popup/item_2/checked",
                "popup/item_2/id",
                "popup/item_2/disabled",
                "popup/item_2/separator",
                "popup/item_10/text",
                "popup/item_10/icon",
                "popup/item_10/checkable",
                "popup/item_10/checked",
                "popup/item_10/id",
                "popup/item_10/disabled",
                "popup/item_10/separator",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // FileDialog::_get_property_list (PropertyListHelper, file_dialog.cpp) lists an option's keys name, values and
    // default, the options in numeric order past ten.
    [Fact]
    public void AFileDialogOptionListsItsNameFirstAndOptionsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <FileDialog {{X}} Name="Root" option_count="12">
              <x:Prop Name="option_10/default" Value="1" />
              <x:Prop Name="option_10/name" Value='"Ten"' />
              <x:Prop Name="option_10/values" Value='PackedStringArray("a", "b")' />
              <x:Prop Name="option_2/default" Value="1" />
              <x:Prop Name="option_2/name" Value='"Two"' />
              <x:Prop Name="option_2/values" Value='PackedStringArray("a", "b")' />
            </FileDialog>
            """
        );

        Assert.Equal(
            ["option_count", "option_2/name", "option_2/values", "option_2/default", "option_10/name", "option_10/values", "option_10/default"],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // TabContainer::_get_property_list (PropertyListHelper, tab_container.cpp) lists a tab's keys title, icon, disabled and
    // hidden, the tabs in numeric order past ten.
    [Fact]
    public void ATabContainerTabListsItsTitleFirstAndTabsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <TabContainer {{X}} Name="Root">
              <x:Prop Name="tab_10/disabled" Value="true" />
              <x:Prop Name="tab_10/hidden" Value="true" />
              <x:Prop Name="tab_10/icon" Value='Resource("res://t.tres")' />
              <x:Prop Name="tab_10/title" Value='"Ten"' />
              <x:Prop Name="tab_2/disabled" Value="true" />
              <x:Prop Name="tab_2/hidden" Value="true" />
              <x:Prop Name="tab_2/icon" Value='Resource("res://t.tres")' />
              <x:Prop Name="tab_2/title" Value='"Two"' />
            </TabContainer>
            """
        );

        Assert.Equal(
            ["tab_2/title", "tab_2/icon", "tab_2/disabled", "tab_2/hidden", "tab_10/title", "tab_10/icon", "tab_10/disabled", "tab_10/hidden"],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }

    // GraphNode::_get_property_list (graph_node.cpp) lists a slot's left_enabled, left_type, left_color and left_icon, then
    // its right keys in the same shape, then draw_stylebox, the slots in numeric order past ten.
    [Fact]
    public void AGraphNodeSlotListsItsLeftSideFirstAndSlotsRunInNumericOrder()
    {
        TscnDocument scene = Back(
            $$"""
            <GraphNode {{X}} Name="Root">
              <x:Prop Name="slot/10/draw_stylebox" Value="true" />
              <x:Prop Name="slot/10/left_color" Value="Color(1, 0, 0, 1)" />
              <x:Prop Name="slot/10/left_enabled" Value="true" />
              <x:Prop Name="slot/10/left_icon" Value="null" />
              <x:Prop Name="slot/10/left_type" Value="1" />
              <x:Prop Name="slot/10/right_color" Value="Color(0, 0, 1, 1)" />
              <x:Prop Name="slot/10/right_enabled" Value="true" />
              <x:Prop Name="slot/10/right_icon" Value="null" />
              <x:Prop Name="slot/10/right_type" Value="2" />
              <x:Prop Name="slot/2/draw_stylebox" Value="false" />
              <x:Prop Name="slot/2/left_color" Value="Color(1, 0, 0, 1)" />
              <x:Prop Name="slot/2/left_enabled" Value="true" />
              <x:Prop Name="slot/2/left_icon" Value="null" />
              <x:Prop Name="slot/2/left_type" Value="1" />
              <x:Prop Name="slot/2/right_color" Value="Color(0, 0, 1, 1)" />
              <x:Prop Name="slot/2/right_enabled" Value="true" />
              <x:Prop Name="slot/2/right_icon" Value="null" />
              <x:Prop Name="slot/2/right_type" Value="2" />
            </GraphNode>
            """
        );

        Assert.Equal(
            [
                "slot/2/left_enabled",
                "slot/2/left_type",
                "slot/2/left_color",
                "slot/2/left_icon",
                "slot/2/right_enabled",
                "slot/2/right_type",
                "slot/2/right_color",
                "slot/2/right_icon",
                "slot/2/draw_stylebox",
                "slot/10/left_enabled",
                "slot/10/left_type",
                "slot/10/left_color",
                "slot/10/left_icon",
                "slot/10/right_enabled",
                "slot/10/right_type",
                "slot/10/right_color",
                "slot/10/right_icon",
                "slot/10/draw_stylebox",
            ],
            PropertyNames(scene.Nodes[0].Properties)
        );
    }
}
