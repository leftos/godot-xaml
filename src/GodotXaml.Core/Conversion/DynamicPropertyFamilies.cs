using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace GodotXaml.Core.Conversion;

/// <summary>
/// The names each Godot class adds in its <c>_get_property_list</c> (or through a <c>PropertyListHelper</c>), which ClassDB
/// does not list and the metadata dump cannot see, since an instance shows only the names that exist at that moment. Each
/// class's families are in the order its function adds them, so the way back can place such a name in its declaring
/// class's slot of Godot's list. Read from the Godot 4.7.2 source; editor-only classes, the Missing* and script classes,
/// <c>InstancePlaceholder</c> and the theme overrides of Control and Window are left out, and so is
/// <c>OpenXRCompositionLayer</c>, which adds any slash name a GDExtension supplies (the engine ships none).
/// </summary>
internal static class DynamicPropertyFamilies
{
    private static readonly (string Class, string Pattern)[] Entries =
    [
        // scene/animation/animation_mixer.cpp, AnimationMixer::_get_property_list
        ("AnimationMixer", @"^libraries/"),
        // scene/animation/animation_player.cpp, AnimationPlayer::_get_property_list
        ("AnimationPlayer", @"^next/"),
        ("AnimationPlayer", @"^blend_times$"),
        // scene/animation/animation_tree.cpp, AnimationTree::_get_property_list (AnimationTree::_update_properties)
        ("AnimationTree", @"^parameters/"),
        // scene/2d/audio_listener_2d.cpp, AudioListener2D::_get_property_list
        ("AudioListener2D", @"^current$"),
        // scene/3d/audio_listener_3d.cpp, AudioListener3D::_get_property_list
        ("AudioListener3D", @"^current$"),
        // scene/audio/audio_stream_player.cpp, AudioStreamPlayer::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        ("AudioStreamPlayer", @"^parameters/"),
        // scene/2d/audio_stream_player_2d.cpp, AudioStreamPlayer2D::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        ("AudioStreamPlayer2D", @"^parameters/"),
        // scene/3d/audio_stream_player_3d.cpp, AudioStreamPlayer3D::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        ("AudioStreamPlayer3D", @"^parameters/"),
        // scene/main/canvas_item.cpp, CanvasItem::_get_property_list
        ("CanvasItem", @"^instance_shader_parameters/"),
        // scene/3d/visual_instance_3d.cpp, GeometryInstance3D::_get_property_list
        ("GeometryInstance3D", @"^instance_shader_parameters/"),
        // scene/3d/mesh_instance_3d.cpp, MeshInstance3D::_get_property_list
        ("MeshInstance3D", @"^blend_shapes/"),
        ("MeshInstance3D", @"^surface_material_override/\d+$"),
        // scene/gui/graph_node.cpp, GraphNode::_get_property_list
        ("GraphNode", @"^slot/\d+/"),
        // scene/gui/tab_container.cpp, TabContainer::_get_property_list (PropertyListHelper)
        ("TabContainer", @"^tab_\d+/"),
        // scene/gui/tab_bar.h, TabBar::_get_property_list (PropertyListHelper)
        ("TabBar", @"^tab_\d+/"),
        // scene/gui/popup_menu.h, PopupMenu::_get_property_list (PropertyListHelper)
        ("PopupMenu", @"^item_\d+/"),
        // scene/gui/item_list.h, ItemList::_get_property_list (PropertyListHelper)
        ("ItemList", @"^item_\d+/"),
        // scene/gui/option_button.h, OptionButton::_get_property_list (PropertyListHelper)
        ("OptionButton", @"^popup/item_\d+/"),
        // scene/gui/menu_button.h, MenuButton::_get_property_list (PropertyListHelper)
        ("MenuButton", @"^popup/item_\d+/"),
        // scene/gui/file_dialog.h, FileDialog::_get_property_list (PropertyListHelper)
        ("FileDialog", @"^option_\d+/"),
        // scene/main/shader_globals_override.cpp, ShaderGlobalsOverride::_get_property_list
        ("ShaderGlobalsOverride", @"^params/"),
        // scene/2d/skeleton_2d.cpp, Skeleton2D::_get_property_list
        ("Skeleton2D", @"^modification_stack$"),
        // scene/2d/skeleton_2d.cpp, Bone2D::_get_property_list
        ("Bone2D", @"^auto_calculate_length_and_angle$"),
        ("Bone2D", @"^length$"),
        ("Bone2D", @"^bone_angle$"),
        ("Bone2D", @"^editor_settings/show_bone_gizmo$"),
        // scene/2d/tile_map.cpp, TileMap::_get_property_list (PropertyListHelper)
        ("TileMap", @"^format$"),
        ("TileMap", @"^layer_\d+/"),
        // scene/3d/skeleton_3d.cpp, Skeleton3D::_get_property_list
        ("Skeleton3D", @"^bones/\d+/"),
        // scene/3d/physics/physical_bone_3d.cpp, PhysicalBone3D::_get_property_list (and its JointData kinds)
        ("PhysicalBone3D", @"^bone_name$"),
        ("PhysicalBone3D", @"^joint_constraints/"),
        // scene/3d/physics/soft_body_3d.cpp, SoftBody3D::_get_property_list
        ("SoftBody3D", @"^pinned_points$"),
        ("SoftBody3D", @"^attachments/\d+/"),
        // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list
        ("BoneTwistDisperser3D", @"^settings/\d+/"),
        // scene/3d/aim_modifier_3d.cpp, AimModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        ("AimModifier3D", @"^settings/\d+/"),
        // scene/3d/copy_transform_modifier_3d.cpp, CopyTransformModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        ("CopyTransformModifier3D", @"^settings/\d+/"),
        // scene/3d/convert_transform_modifier_3d.cpp, ConvertTransformModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        ("ConvertTransformModifier3D", @"^settings/\d+/"),
        // scene/3d/limit_angular_velocity_modifier_3d.cpp, LimitAngularVelocityModifier3D::_get_property_list
        ("LimitAngularVelocityModifier3D", @"^chains/\d+/"),
        ("LimitAngularVelocityModifier3D", @"^joints/\d+/"),
        // scene/3d/iterate_ik_3d.cpp, IterateIK3D::_get_property_list (ChainIK3D::get_property_list)
        ("IterateIK3D", @"^settings/\d+/"),
        // scene/3d/two_bone_ik_3d.cpp, TwoBoneIK3D::_get_property_list
        ("TwoBoneIK3D", @"^settings/\d+/"),
        // scene/3d/spline_ik_3d.cpp, SplineIK3D::_get_property_list (ChainIK3D::get_property_list)
        ("SplineIK3D", @"^settings/\d+/"),
        // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list
        ("SpringBoneSimulator3D", @"^settings/\d+/"),
        // modules/multiplayer/multiplayer_spawner.cpp, MultiplayerSpawner::_get_property_list (editor-only: never stored)
        ("MultiplayerSpawner", @"^_spawnable_scene_count$"),
        ("MultiplayerSpawner", @"^scenes/\d+$"),
        // modules/gridmap/grid_map.cpp, GridMap::_get_property_list
        ("GridMap", @"^baked_meshes$"),
        ("GridMap", @"^data$"),
        // scene/resources/theme.cpp, Theme::_get_property_list (one list, sorted by name)
        ("Theme", @"^[^/]+/(?:base_type$|(?:icons|styles|fonts|font_sizes|colors|constants)/)"),
        // scene/resources/2d/tile_set.cpp, TileSet::_get_property_list
        ("TileSet", @"^occlusion_layer_\d+/"),
        ("TileSet", @"^physics_layer_\d+/"),
        ("TileSet", @"^terrain_set_\d+/"),
        ("TileSet", @"^navigation_layer_\d+/"),
        ("TileSet", @"^custom_data_layer_\d+/"),
        ("TileSet", @"^sources/\d+$"),
        ("TileSet", @"^tile_proxies/"),
        ("TileSet", @"^pattern_\d+$"),
        // scene/resources/2d/tile_set.cpp, TileSetAtlasSource::_get_property_list
        ("TileSetAtlasSource", @"^\d+:\d+/"),
        // scene/resources/2d/tile_set.cpp, TileSetScenesCollectionSource::_get_property_list
        ("TileSetScenesCollectionSource", @"^scenes/\d+/"),
        // scene/resources/2d/tile_set.cpp, TileMapPattern::_get_property_list
        ("TileMapPattern", @"^tile_data$"),
        // scene/resources/2d/tile_set.cpp, TileData::_get_property_list
        ("TileData", @"^occlusion_layer_\d+/"),
        ("TileData", @"^physics_layer_\d+/"),
        ("TileData", @"^terrains_peering_bit/"),
        ("TileData", @"^navigation_layer_\d+/"),
        ("TileData", @"^custom_data_\d+$"),
        // scene/resources/mesh.cpp, ArrayMesh::_get_property_list (editor-only: never stored)
        ("ArrayMesh", @"^surface_\d+/"),
        // scene/resources/font.cpp, FontFile::_get_property_list
        ("FontFile", @"^language_support_override/"),
        ("FontFile", @"^script_support_override/"),
        ("FontFile", @"^cache/\d+/"),
        // scene/resources/3d/skin.cpp, Skin::_get_property_list
        ("Skin", @"^bind_count$"),
        ("Skin", @"^bind/\d+/"),
        // scene/resources/3d/mesh_library.cpp, MeshLibrary::_get_property_list
        ("MeshLibrary", @"^item/\d+/"),
        // scene/resources/skeleton_profile.cpp, SkeletonProfile::_get_property_list
        ("SkeletonProfile", @"^groups/\d+/"),
        ("SkeletonProfile", @"^bones/\d+/"),
        // scene/resources/bone_map.cpp, BoneMap::_get_property_list
        ("BoneMap", @"^bone_map/"),
        // servers/audio/audio_server.cpp, AudioBusLayout::_get_property_list
        ("AudioBusLayout", @"^bus/\d+/"),
        // servers/audio/effects/audio_effect_eq.cpp, AudioEffectEQ::_get_property_list
        ("AudioEffectEQ", @"^band_db/\d+_hz$"),
        // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list
        ("VisualShader", @"^mode$"),
        ("VisualShader", @"^modes/"),
        ("VisualShader", @"^flags/"),
        ("VisualShader", @"^stencil/(?:enabled|reference)$"),
        ("VisualShader", @"^stencil_modes/"),
        ("VisualShader", @"^stencil_flags/"),
        ("VisualShader", @"^varyings/"),
        ("VisualShader", @"^preview_params/"),
        ("VisualShader", @"^nodes/"),
        // modules/multiplayer/scene_replication_config.cpp, SceneReplicationConfig::_get_property_list
        ("SceneReplicationConfig", @"^properties/\d+/"),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeBlendTree::_get_property_list
        ("AnimationNodeBlendTree", @"^nodes/"),
        ("AnimationNodeBlendTree", @"^node_connections$"),
        // scene/animation/animation_node_state_machine.cpp, AnimationNodeStateMachine::_get_property_list
        ("AnimationNodeStateMachine", @"^states/"),
        ("AnimationNodeStateMachine", @"^transitions$"),
        ("AnimationNodeStateMachine", @"^graph_offset$"),
        // scene/animation/animation_blend_space_1d.cpp, AnimationNodeBlendSpace1D::_get_property_list
        ("AnimationNodeBlendSpace1D", @"^blend_point_\d+/"),
        // scene/animation/animation_blend_space_2d.cpp, AnimationNodeBlendSpace2D::_get_property_list
        ("AnimationNodeBlendSpace2D", @"^blend_point_\d+/"),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeTransition::_get_property_list
        ("AnimationNodeTransition", @"^input_\d+/"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_ccdik.cpp, SkeletonModification2DCCDIK::_get_property_list
        ("SkeletonModification2DCCDIK", @"^joint_data/\d+/"),
        ("SkeletonModification2DCCDIK", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_fabrik.cpp, SkeletonModification2DFABRIK::_get_property_list
        ("SkeletonModification2DFABRIK", @"^joint_data/\d+/"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_jiggle.cpp, SkeletonModification2DJiggle::_get_property_list
        ("SkeletonModification2DJiggle", @"^use_colliders$"),
        ("SkeletonModification2DJiggle", @"^collision_mask$"),
        ("SkeletonModification2DJiggle", @"^joint_data/\d+/"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_lookat.cpp, SkeletonModification2DLookAt::_get_property_list
        ("SkeletonModification2DLookAt", @"^enable_constraint$"),
        ("SkeletonModification2DLookAt", @"^constraint_angle_min$"),
        ("SkeletonModification2DLookAt", @"^constraint_angle_max$"),
        ("SkeletonModification2DLookAt", @"^constraint_angle_invert$"),
        ("SkeletonModification2DLookAt", @"^constraint_in_localspace$"),
        ("SkeletonModification2DLookAt", @"^additional_rotation$"),
        ("SkeletonModification2DLookAt", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_physicalbones.cpp, SkeletonModification2DPhysicalBones::_get_property_list
        ("SkeletonModification2DPhysicalBones", @"^fetch_bones$"),
        ("SkeletonModification2DPhysicalBones", @"^joint_\d+_nodepath$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_stackholder.cpp, SkeletonModification2DStackHolder::_get_property_list
        ("SkeletonModification2DStackHolder", @"^held_modification_stack$"),
        ("SkeletonModification2DStackHolder", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_twoboneik.cpp, SkeletonModification2DTwoBoneIK::_get_property_list
        ("SkeletonModification2DTwoBoneIK", @"^joint_one_bone_idx$"),
        ("SkeletonModification2DTwoBoneIK", @"^joint_one_bone2d_node$"),
        ("SkeletonModification2DTwoBoneIK", @"^joint_two_bone_idx$"),
        ("SkeletonModification2DTwoBoneIK", @"^joint_two_bone2d_node$"),
        ("SkeletonModification2DTwoBoneIK", @"^editor/draw_gizmo$"),
        ("SkeletonModification2DTwoBoneIK", @"^editor/draw_min_max$"),
        // scene/resources/2d/skeleton/skeleton_modification_stack_2d.cpp, SkeletonModificationStack2D::_get_property_list
        ("SkeletonModificationStack2D", @"^modifications/\d+$"),
        // scene/resources/material.cpp, ShaderMaterial::_get_property_list
        ("ShaderMaterial", @"^shader_parameter/"),
        // scene/resources/animation.cpp, Animation::_get_property_list
        ("Animation", @"^_compression$"),
        ("Animation", @"^markers$"),
        ("Animation", @"^tracks/\d+/"),
        // scene/resources/curve.h, Curve::_get_property_list (PropertyListHelper; editor-only: never stored)
        ("Curve", @"^point_\d+/"),
        // scene/resources/curve.h, Curve2D::_get_property_list (PropertyListHelper; editor-only: never stored)
        ("Curve2D", @"^point_\d+/"),
        // scene/resources/curve.h, Curve3D::_get_property_list (PropertyListHelper; editor-only: never stored)
        ("Curve3D", @"^point_\d+/"),
        // scene/resources/label_settings.h, LabelSettings::_get_property_list (PropertyListHelper)
        ("LabelSettings", @"^stacked_outline_\d+/"),
        ("LabelSettings", @"^stacked_shadow_\d+/"),
        // servers/audio/audio_stream.h, AudioStreamRandomizer::_get_property_list (PropertyListHelper)
        ("AudioStreamRandomizer", @"^stream_\d+/"),
    ];

    private static readonly FrozenDictionary<string, ImmutableArray<Regex>> ByClass = Entries
        .GroupBy(entry => entry.Class, StringComparer.Ordinal)
        .ToFrozenDictionary(
            group => group.Key,
            group => group.Select(entry => new Regex(entry.Pattern, RegexOptions.CultureInvariant)).ToImmutableArray(),
            StringComparer.Ordinal
        );

    /// <summary>The name families a class adds itself, in the order its <c>_get_property_list</c> adds them.</summary>
    /// <param name="className">The Godot class name.</param>
    /// <returns>The families, each an anchored pattern over a property name; empty for a class that adds none.</returns>
    public static ImmutableArray<Regex> Of(string className) => ByClass.TryGetValue(className, out ImmutableArray<Regex> families) ? families : [];
}
