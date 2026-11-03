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
/// <c>OpenXRCompositionLayer</c>, which adds any slash name a GDExtension supplies (the engine ships none). Every row states
/// how Godot creates its entries on load (<see cref="EntryCreation"/>): in any order; sized first by a count property ranked
/// before the family, named on the row; or on one key. Godot creates an entry only on one key (the creating key), so each
/// entry's names come creating key first, and entries in numeric order, which a family that appends at the next index
/// (Animation tracks, replication properties, TileSet patterns) needs. Such a pattern names each entry's number in an
/// <c>index</c> group and any key after it in a <c>key</c> group, and the row carries the declaring class's key order. A
/// sized-first row may carry the same groups and key order, and one nested level inside a key (a <c>subindex</c> group, a
/// <c>subkey</c> group and the declaring class's nested key order), which keeps a round trip byte-identical, and which the
/// load order needs where keys reset or hide one another.
/// </summary>
internal static class DynamicPropertyFamilies
{
    // scene/resources/animation.cpp, Animation::_get_property_list; Animation::_set creates a track only on its type.
    private static readonly string[] TrackKeys =
    [
        "type",
        "imported",
        "enabled",
        "path",
        "compressed_track",
        "interp",
        "loop_wrap",
        "keys",
        "use_blend",
    ];

    // scene/3d/skeleton_3d.cpp, Skeleton3D::_get_property_list (bone_meta/<name> after these); Skeleton3D::_set adds a bone
    // only on its name.
    private static readonly string[] BoneKeys = ["name", "parent", "rest", "enabled", "position", "rotation", "scale", "bone_meta"];

    // scene/animation/animation_blend_space_1d.cpp and animation_blend_space_2d.cpp, _get_property_list; each _set adds a
    // point only on its node.
    private static readonly string[] BlendPointKeys = ["node", "pos", "name"];

    // scene/animation/animation_blend_tree.cpp, AnimationNodeTransition::_get_property_list.
    private static readonly string[] TransitionInputKeys = ["name", "auto_advance", "break_loop_at_end", "reset"];

    // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list; VisualShader::_set creates a node only on its
    // node, and casts the node unchecked for size, input_ports, output_ports and expression.
    private static readonly string[] VisualShaderNodeKeys = ["node", "position", "size", "input_ports", "output_ports", "expression"];

    // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list lists modes/ in the order
    // servers/rendering/shader_types.cpp, ShaderTypes::ShaderTypes registers the render modes with options; no shader mode
    // registers two of these in another order.
    private static readonly string[] VisualShaderModes = ["blend", "depth_draw", "depth_test", "cull", "diffuse", "specular"];

    // The same list's flags/: the Spatial render modes without options in ShaderTypes::ShaderTypes order, with
    // depth_test_disabled at depth_test's place.
    private static readonly string[] VisualShaderFlags =
    [
        "depth_prepass_alpha",
        "depth_test_disabled",
        "sss_mode_skin",
        "unshaded",
        "wireframe",
        "skip_vertex_transform",
        "world_vertex_coords",
        "ensure_correct_normals",
        "shadows_disabled",
        "ambient_light_disabled",
        "shadow_to_opacity",
        "vertex_lighting",
        "particle_trails",
        "alpha_to_coverage",
        "alpha_to_coverage_and_one",
        "debug_shadow_splits",
        "fog_disabled",
        "specular_occlusion_disabled",
    ];

    // modules/visual_shader/visual_shader.cpp, type_string: VisualShader::_get_property_list lists the graphs in this order,
    // each graph's nodes by id, then its connections, which is the order Godot saves them in.
    private static readonly string[] VisualShaderTypes =
    [
        "vertex",
        "fragment",
        "light",
        "start",
        "process",
        "collide",
        "start_custom",
        "process_custom",
        "sky",
        "fog",
        "texture_blit",
    ];

    // modules/multiplayer/scene_replication_config.cpp, SceneReplicationConfig::_get_property_list;
    // SceneReplicationConfig::_set creates an entry only on its path, when the entry's index equals the count so far.
    private static readonly string[] ReplicationPropertyKeys = ["path", "spawn", "replication_mode"];

    // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list: one setting's keys. With no skeleton
    // (a scene sets properties before it adds the node), set_root_bone and set_end_bone free the setting's joints, so the
    // bones come before joint_count and the joints; set_collision_count and set_collision_path return early while
    // enable_all_child_collisions is true, so it comes before the collisions.
    private static readonly string[] SpringSettingKeys =
    [
        "root_bone_name",
        "root_bone",
        "end_bone_name",
        "end_bone",
        "extend_end_bone",
        "end_bone/direction",
        "end_bone/length",
        "center_from",
        "center_node",
        "center_bone_name",
        "center_bone",
        "individual_config",
        "rotation_axis",
        "rotation_axis_vector",
        "radius/value",
        "radius/damping_curve",
        "stiffness/value",
        "stiffness/damping_curve",
        "drag/value",
        "drag/damping_curve",
        "gravity/value",
        "gravity/damping_curve",
        "gravity/direction",
        "joint_count",
        "joints",
        "enable_all_child_collisions",
        "exclude_collision_count",
        "exclude_collisions",
        "collision_count",
        "collisions",
    ];

    // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list: one joint's keys.
    private static readonly string[] SpringJointKeys =
    [
        "bone_name",
        "bone",
        "rotation_axis",
        "rotation_axis_vector",
        "radius",
        "stiffness",
        "drag",
        "gravity",
        "gravity_direction",
    ];

    // scene/3d/chain_ik_3d.cpp, ChainIK3D::get_property_list: one setting's chain keys (its joints/<M>/bone_name and bone are
    // read-only and never stored). With no skeleton, set_root_bone and set_end_bone free the setting's joints.
    private static readonly string[] ChainKeys =
    [
        "root_bone_name",
        "root_bone",
        "end_bone_name",
        "end_bone",
        "extend_end_bone",
        "end_bone/direction",
        "end_bone/length",
        "joint_count",
    ];

    // scene/3d/iterate_ik_3d.cpp, IterateIK3D::_get_property_list: one joint's keys. It lists every setting's target_node,
    // then ChainIK3D::get_property_list, then every setting's joint keys.
    private static readonly string[] IterateJointKeys =
    [
        "rotation_axis",
        "rotation_axis_vector",
        "limitation",
        "limitation/right_axis",
        "limitation/right_axis_vector",
        "limitation/rotation_offset",
    ];

    // scene/3d/two_bone_ik_3d.cpp, TwoBoneIK3D::_get_property_list: one setting's keys.
    private static readonly string[] TwoBoneSettingKeys =
    [
        "target_node",
        "pole_node",
        "root_bone_name",
        "root_bone",
        "middle_bone_name",
        "middle_bone",
        "pole_direction",
        "pole_direction_vector",
        "end_bone_name",
        "end_bone",
        "use_virtual_end",
        "extend_end_bone",
        "end_bone/direction",
        "end_bone/length",
    ];

    // scene/3d/limit_angular_velocity_modifier_3d.cpp, LimitAngularVelocityModifier3D::_get_property_list: one chain's keys.
    private static readonly string[] LimitChainKeys = ["root_bone_name", "root_bone", "end_bone_name", "end_bone"];

    // scene/3d/bone_constraint_3d.cpp, BoneConstraint3D::get_property_list: one setting's constraint keys. Each subclass's
    // _get_property_list calls it for every setting before it lists every setting's own keys.
    private static readonly string[] ConstraintKeys =
    [
        "amount",
        "apply_bone_name",
        "apply_bone",
        "reference_type",
        "reference_bone_name",
        "reference_bone",
        "reference_node",
    ];

    private static readonly string ConstraintPattern = $@"^settings/(?<index>\d+)/(?<key>{string.Join("|", ConstraintKeys)})$";

    // scene/3d/aim_modifier_3d.cpp, AimModifier3D::_get_property_list: one setting's own keys.
    private static readonly string[] AimKeys = ["forward_axis", "use_euler", "primary_rotation_axis", "use_secondary_rotation", "relative"];

    // scene/3d/copy_transform_modifier_3d.cpp, CopyTransformModifier3D::_get_property_list: one setting's own keys.
    private static readonly string[] CopyKeys = ["copy", "axes", "invert", "relative", "additive"];

    // scene/3d/convert_transform_modifier_3d.cpp, ConvertTransformModifier3D::_get_property_list: one setting's own keys.
    private static readonly string[] ConvertKeys =
    [
        "apply/transform_mode",
        "apply/axis",
        "apply/range_min",
        "apply/range_max",
        "reference/transform_mode",
        "reference/axis",
        "reference/range_min",
        "reference/range_max",
        "relative",
        "additive",
    ];

    // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list: one setting's keys, its joints after
    // joint_count.
    private static readonly string[] DisperserSettingKeys =
    [
        "root_bone_name",
        "root_bone",
        "end_bone_name",
        "end_bone",
        "extend_end_bone",
        "end_bone_direction",
        "reference_bone_name",
        "twist_from_rest",
        "twist_from",
        "disperse_mode",
        "weight_position",
        "damping_curve",
        "joint_count",
        "joints",
    ];

    // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list: one joint's keys.
    private static readonly string[] DisperserJointKeys = ["bone_name", "bone", "twist_amount"];

    private static readonly Row[] Entries =
    [
        // scene/animation/animation_mixer.cpp, AnimationMixer::_get_property_list
        Row.AnyOrder("AnimationMixer", @"^libraries/"),
        // scene/animation/animation_player.cpp, AnimationPlayer::_get_property_list
        Row.AnyOrder("AnimationPlayer", @"^next/"),
        Row.AnyOrder("AnimationPlayer", @"^blend_times$"),
        // scene/animation/animation_tree.cpp, AnimationTree::_get_property_list (AnimationTree::_update_properties)
        Row.AnyOrder("AnimationTree", @"^parameters/"),
        // scene/2d/audio_listener_2d.cpp, AudioListener2D::_get_property_list
        Row.AnyOrder("AudioListener2D", @"^current$"),
        // scene/3d/audio_listener_3d.cpp, AudioListener3D::_get_property_list
        Row.AnyOrder("AudioListener3D", @"^current$"),
        // scene/audio/audio_stream_player.cpp, AudioStreamPlayer::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        Row.AnyOrder("AudioStreamPlayer", @"^parameters/"),
        // scene/2d/audio_stream_player_2d.cpp, AudioStreamPlayer2D::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        Row.AnyOrder("AudioStreamPlayer2D", @"^parameters/"),
        // scene/3d/audio_stream_player_3d.cpp, AudioStreamPlayer3D::_get_property_list (AudioStreamPlayerInternal::get_property_list)
        Row.AnyOrder("AudioStreamPlayer3D", @"^parameters/"),
        // scene/main/canvas_item.cpp, CanvasItem::_get_property_list
        Row.AnyOrder("CanvasItem", @"^instance_shader_parameters/"),
        // scene/3d/visual_instance_3d.cpp, GeometryInstance3D::_get_property_list
        Row.AnyOrder("GeometryInstance3D", @"^instance_shader_parameters/"),
        // scene/3d/mesh_instance_3d.cpp, MeshInstance3D::_get_property_list
        Row.AnyOrder("MeshInstance3D", @"^blend_shapes/"),
        Row.SizedFirst("MeshInstance3D", @"^surface_material_override/\d+$", "mesh"),
        // scene/gui/graph_node.cpp, GraphNode::_get_property_list
        Row.AnyOrder("GraphNode", @"^slot/\d+/"),
        // scene/gui/tab_container.cpp, TabContainer::_get_property_list (PropertyListHelper)
        Row.AnyOrder("TabContainer", @"^tab_\d+/"),
        // scene/gui/tab_bar.h, TabBar::_get_property_list (PropertyListHelper)
        Row.SizedFirst("TabBar", @"^tab_\d+/", "tab_count"),
        // scene/gui/popup_menu.h, PopupMenu::_get_property_list (PropertyListHelper)
        Row.SizedFirst("PopupMenu", @"^item_\d+/", "item_count"),
        // scene/gui/item_list.h, ItemList::_get_property_list (PropertyListHelper)
        Row.SizedFirst("ItemList", @"^item_\d+/", "item_count"),
        // scene/gui/option_button.h, OptionButton::_get_property_list (PropertyListHelper)
        Row.SizedFirst("OptionButton", @"^popup/item_\d+/", "item_count"),
        // scene/gui/menu_button.h, MenuButton::_get_property_list (PropertyListHelper)
        Row.SizedFirst("MenuButton", @"^popup/item_\d+/", "item_count"),
        // scene/gui/file_dialog.h, FileDialog::_get_property_list (PropertyListHelper)
        Row.SizedFirst("FileDialog", @"^option_\d+/", "option_count"),
        // scene/main/shader_globals_override.cpp, ShaderGlobalsOverride::_get_property_list
        Row.AnyOrder("ShaderGlobalsOverride", @"^params/"),
        // scene/2d/skeleton_2d.cpp, Skeleton2D::_get_property_list
        Row.AnyOrder("Skeleton2D", @"^modification_stack$"),
        // scene/2d/skeleton_2d.cpp, Bone2D::_get_property_list
        Row.AnyOrder("Bone2D", @"^auto_calculate_length_and_angle$"),
        Row.AnyOrder("Bone2D", @"^length$"),
        Row.AnyOrder("Bone2D", @"^bone_angle$"),
        Row.AnyOrder("Bone2D", @"^editor_settings/show_bone_gizmo$"),
        // scene/2d/tile_map.cpp, TileMap::_get_property_list (PropertyListHelper)
        Row.AnyOrder("TileMap", @"^format$"),
        Row.AnyOrder("TileMap", @"^layer_\d+/"),
        // scene/3d/skeleton_3d.cpp, Skeleton3D::_get_property_list
        Row.OnKey("Skeleton3D", @"^bones/(?<index>\d+)/(?<key>[^/]*)", BoneKeys),
        // scene/3d/physics/physical_bone_3d.cpp, PhysicalBone3D::_get_property_list (and its JointData kinds)
        Row.AnyOrder("PhysicalBone3D", @"^bone_name$"),
        Row.AnyOrder("PhysicalBone3D", @"^joint_constraints/"),
        // scene/3d/physics/soft_body_3d.cpp, SoftBody3D::_get_property_list
        Row.AnyOrder("SoftBody3D", @"^pinned_points$"),
        Row.SizedFirst("SoftBody3D", @"^attachments/\d+/", "pinned_points"),
        // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list: setting by setting, each joint in
        // numeric order.
        Row.SizedFirst(
            "BoneTwistDisperser3D",
            @"^settings/(?<index>\d+)/(?:(?<key>joints)/(?<subindex>\d+)/(?<subkey>[^/]*)$|(?<key>.*))",
            "setting_count",
            DisperserSettingKeys,
            DisperserJointKeys
        ),
        // aim_modifier_3d.cpp, copy_transform_modifier_3d.cpp and convert_transform_modifier_3d.cpp, each class's
        // _get_property_list (BoneConstraint3D::get_property_list): every setting's constraint keys, then every setting's own keys.
        .. BoneConstraintRows("AimModifier3D", AimKeys),
        .. BoneConstraintRows("CopyTransformModifier3D", CopyKeys),
        .. BoneConstraintRows("ConvertTransformModifier3D", ConvertKeys),
        // scene/3d/limit_angular_velocity_modifier_3d.cpp, LimitAngularVelocityModifier3D::_get_property_list
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^chains/(?<index>\d+)/(?<key>.*)", "chain_count", LimitChainKeys, []),
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^joints/\d+/", "joint_count"),
        // scene/3d/iterate_ik_3d.cpp, IterateIK3D::_get_property_list (ChainIK3D::get_property_list): every setting's
        // target_node, then each setting's chain keys, then every setting's joint keys.
        Row.SizedFirst("IterateIK3D", @"^settings/(?<index>\d+)/(?<key>target_node)$", "setting_count", ["target_node"], []),
        Row.SizedFirst("IterateIK3D", @"^settings/(?<index>\d+)/(?!joints/)(?<key>.*)", "setting_count", ChainKeys, []),
        Row.SizedFirst(
            "IterateIK3D",
            @"^settings/(?<index>\d+)/(?<key>joints)/(?<subindex>\d+)/(?<subkey>.*)",
            "setting_count",
            ["joints"],
            IterateJointKeys
        ),
        Row.SizedFirst("IterateIK3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/two_bone_ik_3d.cpp, TwoBoneIK3D::_get_property_list
        Row.SizedFirst("TwoBoneIK3D", @"^settings/(?<index>\d+)/(?<key>.*)", "setting_count", TwoBoneSettingKeys, []),
        // scene/3d/spline_ik_3d.cpp, SplineIK3D::_get_property_list (ChainIK3D::get_property_list): every setting's path
        // and tilt keys, then each setting's chain keys.
        Row.SizedFirst(
            "SplineIK3D",
            @"^settings/(?<index>\d+)/(?<key>path_3d|tilt_enabled|tilt_fade_in|tilt_fade_out)$",
            "setting_count",
            ["path_3d", "tilt_enabled", "tilt_fade_in", "tilt_fade_out"],
            []
        ),
        Row.SizedFirst("SplineIK3D", @"^settings/(?<index>\d+)/(?<key>.*)", "setting_count", ChainKeys, []),
        // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list: setting by setting, each joint
        // and collision in numeric order.
        Row.SizedFirst(
            "SpringBoneSimulator3D",
            @"^settings/(?<index>\d+)/(?:(?<key>joints|exclude_collisions|collisions)/(?<subindex>\d+)(?:/(?<subkey>[^/]*))?$|(?<key>.*))",
            "setting_count",
            SpringSettingKeys,
            SpringJointKeys
        ),
        // modules/multiplayer/multiplayer_spawner.cpp, MultiplayerSpawner::_get_property_list (editor-only: never stored)
        Row.AnyOrder("MultiplayerSpawner", @"^_spawnable_scene_count$"),
        Row.SizedFirst("MultiplayerSpawner", @"^scenes/\d+$", "_spawnable_scene_count"),
        // modules/gridmap/grid_map.cpp, GridMap::_get_property_list
        Row.AnyOrder("GridMap", @"^baked_meshes$"),
        Row.AnyOrder("GridMap", @"^data$"),
        // scene/resources/theme.cpp, Theme::_get_property_list (one list, sorted by name)
        Row.AnyOrder("Theme", @"^[^/]+/(?:base_type$|(?:icons|styles|fonts|font_sizes|colors|constants)/)"),
        // scene/resources/2d/tile_set.cpp, TileSet::_get_property_list
        Row.AnyOrder("TileSet", @"^occlusion_layer_\d+/"),
        Row.AnyOrder("TileSet", @"^physics_layer_\d+/"),
        Row.AnyOrder("TileSet", @"^terrain_set_\d+/"),
        Row.AnyOrder("TileSet", @"^navigation_layer_\d+/"),
        Row.AnyOrder("TileSet", @"^custom_data_layer_\d+/"),
        Row.AnyOrder("TileSet", @"^sources/\d+$"),
        Row.AnyOrder("TileSet", @"^tile_proxies/"),
        // TileSet::_set appends at most one pattern per name (its loop calls add_pattern, which refuses a pattern it holds).
        Row.OnKey("TileSet", @"^pattern_(?<index>\d+)$", []),
        // scene/resources/2d/tile_set.cpp, TileSetAtlasSource::_get_property_list
        Row.AnyOrder("TileSetAtlasSource", @"^\d+:\d+/"),
        // scene/resources/2d/tile_set.cpp, TileSetScenesCollectionSource::_get_property_list
        Row.AnyOrder("TileSetScenesCollectionSource", @"^scenes/\d+/"),
        // scene/resources/2d/tile_set.cpp, TileMapPattern::_get_property_list
        Row.AnyOrder("TileMapPattern", @"^tile_data$"),
        // scene/resources/2d/tile_set.cpp, TileData::_get_property_list
        Row.AnyOrder("TileData", @"^occlusion_layer_\d+/"),
        Row.AnyOrder("TileData", @"^physics_layer_\d+/"),
        Row.AnyOrder("TileData", @"^terrains_peering_bit/"),
        Row.AnyOrder("TileData", @"^navigation_layer_\d+/"),
        Row.AnyOrder("TileData", @"^custom_data_\d+$"),
        // scene/resources/mesh.cpp, ArrayMesh::_get_property_list (editor-only: never stored)
        Row.AnyOrder("ArrayMesh", @"^surface_\d+/"),
        // scene/resources/font.cpp, FontFile::_get_property_list
        Row.AnyOrder("FontFile", @"^language_support_override/"),
        Row.AnyOrder("FontFile", @"^script_support_override/"),
        Row.AnyOrder("FontFile", @"^cache/\d+/"),
        // scene/resources/3d/skin.cpp, Skin::_get_property_list
        Row.AnyOrder("Skin", @"^bind_count$"),
        Row.SizedFirst("Skin", @"^bind/\d+/", "bind_count"),
        // scene/resources/3d/mesh_library.cpp, MeshLibrary::_get_property_list
        Row.AnyOrder("MeshLibrary", @"^item/\d+/"),
        // scene/resources/skeleton_profile.cpp, SkeletonProfile::_get_property_list
        Row.SizedFirst("SkeletonProfile", @"^groups/\d+/", "group_size"),
        Row.SizedFirst("SkeletonProfile", @"^bones/\d+/", "bone_size"),
        // scene/resources/bone_map.cpp, BoneMap::_get_property_list
        Row.AnyOrder("BoneMap", @"^bone_map/"),
        // servers/audio/audio_server.cpp, AudioBusLayout::_get_property_list
        Row.AnyOrder("AudioBusLayout", @"^bus/\d+/"),
        // servers/audio/effects/audio_effect_eq.cpp, AudioEffectEQ::_get_property_list
        Row.AnyOrder("AudioEffectEQ", @"^band_db/\d+_hz$"),
        // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list
        Row.AnyOrder("VisualShader", @"^mode$"),
        .. VisualShaderModes.Select(mode => Row.AnyOrder("VisualShader", $"^modes/{mode}$")),
        Row.AnyOrder("VisualShader", @"^modes/"),
        .. VisualShaderFlags.Select(flag => Row.AnyOrder("VisualShader", $"^flags/{flag}$")),
        Row.AnyOrder("VisualShader", @"^flags/"),
        Row.AnyOrder("VisualShader", @"^stencil/(?:enabled|reference)$"),
        Row.AnyOrder("VisualShader", @"^stencil_modes/"),
        Row.AnyOrder("VisualShader", @"^stencil_flags/"),
        Row.AnyOrder("VisualShader", @"^varyings/"),
        Row.AnyOrder("VisualShader", @"^preview_params/"),
        // Type by type in Godot's save order, each type's nodes by id with node first, then its connections:
        // VisualShader::_set connects with connect_nodes_forced, which needs the nodes and their ports.
        .. VisualShaderNodeRows(),
        // A graph type the list does not name (VisualShader::_set reads it as vertex) still needs its node first.
        Row.OnKey("VisualShader", @"^nodes/[^/]+/(?<index>\d+)/(?<key>[^/]*)", VisualShaderNodeKeys),
        Row.AnyOrder("VisualShader", @"^nodes/"),
        // modules/multiplayer/scene_replication_config.cpp, SceneReplicationConfig::_get_property_list
        Row.OnKey("SceneReplicationConfig", @"^properties/(?<index>\d+)/(?<key>[^/]*)", ReplicationPropertyKeys),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeBlendTree::_get_property_list
        Row.AnyOrder("AnimationNodeBlendTree", @"^nodes/"),
        Row.AnyOrder("AnimationNodeBlendTree", @"^node_connections$"),
        // scene/animation/animation_node_state_machine.cpp, AnimationNodeStateMachine::_get_property_list
        Row.AnyOrder("AnimationNodeStateMachine", @"^states/"),
        Row.AnyOrder("AnimationNodeStateMachine", @"^transitions$"),
        Row.AnyOrder("AnimationNodeStateMachine", @"^graph_offset$"),
        // scene/animation/animation_blend_space_1d.cpp, AnimationNodeBlendSpace1D::_get_property_list
        Row.OnKey("AnimationNodeBlendSpace1D", @"^blend_point_(?<index>\d+)/(?<key>[^/]*)", BlendPointKeys),
        // scene/animation/animation_blend_space_2d.cpp, AnimationNodeBlendSpace2D::_get_property_list
        Row.OnKey("AnimationNodeBlendSpace2D", @"^blend_point_(?<index>\d+)/(?<key>[^/]*)", BlendPointKeys),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeTransition::_get_property_list; _set adds an input only on
        // input_<count>/name, and input_count is an editor-only property the file never holds.
        Row.OnKey("AnimationNodeTransition", @"^input_(?<index>\d+)/(?<key>[^/]*)", TransitionInputKeys),
        // scene/resources/2d/skeleton/skeleton_modification_2d_ccdik.cpp, SkeletonModification2DCCDIK::_get_property_list
        Row.SizedFirst("SkeletonModification2DCCDIK", @"^joint_data/\d+/", "ccdik_data_chain_length"),
        Row.AnyOrder("SkeletonModification2DCCDIK", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_fabrik.cpp, SkeletonModification2DFABRIK::_get_property_list
        Row.SizedFirst("SkeletonModification2DFABRIK", @"^joint_data/\d+/", "fabrik_data_chain_length"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_jiggle.cpp, SkeletonModification2DJiggle::_get_property_list
        Row.AnyOrder("SkeletonModification2DJiggle", @"^use_colliders$"),
        Row.AnyOrder("SkeletonModification2DJiggle", @"^collision_mask$"),
        Row.SizedFirst("SkeletonModification2DJiggle", @"^joint_data/\d+/", "jiggle_data_chain_length"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_lookat.cpp, SkeletonModification2DLookAt::_get_property_list
        Row.AnyOrder("SkeletonModification2DLookAt", @"^enable_constraint$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_min$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_max$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_invert$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_in_localspace$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^additional_rotation$"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_physicalbones.cpp, SkeletonModification2DPhysicalBones::_get_property_list
        Row.AnyOrder("SkeletonModification2DPhysicalBones", @"^fetch_bones$"),
        Row.SizedFirst("SkeletonModification2DPhysicalBones", @"^joint_\d+_nodepath$", "physical_bone_chain_length"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_stackholder.cpp, SkeletonModification2DStackHolder::_get_property_list
        Row.AnyOrder("SkeletonModification2DStackHolder", @"^held_modification_stack$"),
        Row.AnyOrder("SkeletonModification2DStackHolder", @"^editor/draw_gizmo$"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_twoboneik.cpp, SkeletonModification2DTwoBoneIK::_get_property_list
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_one_bone_idx$"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_one_bone2d_node$"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_two_bone_idx$"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_two_bone2d_node$"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^editor/draw_gizmo$"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^editor/draw_min_max$"),
        // scene/resources/2d/skeleton/skeleton_modification_stack_2d.cpp, SkeletonModificationStack2D::_get_property_list
        Row.SizedFirst("SkeletonModificationStack2D", @"^modifications/\d+$", "modification_count"),
        // scene/resources/material.cpp, ShaderMaterial::_get_property_list
        Row.AnyOrder("ShaderMaterial", @"^shader_parameter/"),
        // scene/resources/animation.cpp, Animation::_get_property_list
        Row.AnyOrder("Animation", @"^_compression$"),
        Row.AnyOrder("Animation", @"^markers$"),
        Row.OnKey("Animation", @"^tracks/(?<index>\d+)/(?<key>[^/]*)", TrackKeys),
        // scene/resources/curve.h, Curve::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve", @"^point_\d+/", "point_count"),
        // scene/resources/curve.h, Curve2D::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve2D", @"^point_\d+/", "point_count"),
        // scene/resources/curve.h, Curve3D::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve3D", @"^point_\d+/", "point_count"),
        // scene/resources/label_settings.h, LabelSettings::_get_property_list (PropertyListHelper)
        Row.SizedFirst("LabelSettings", @"^stacked_outline_\d+/", "stacked_outline_count"),
        Row.SizedFirst("LabelSettings", @"^stacked_shadow_\d+/", "stacked_shadow_count"),
        // servers/audio/audio_stream.h, AudioStreamRandomizer::_get_property_list (PropertyListHelper)
        Row.SizedFirst("AudioStreamRandomizer", @"^stream_\d+/", "streams_count"),
    ];

    private static readonly ImmutableArray<(string Class, DynamicFamily Family)> Families =
    [
        .. Entries.Select(entry =>
            (
                entry.Class,
                new DynamicFamily(
                    new Regex(entry.Pattern, RegexOptions.CultureInvariant),
                    entry.Creation,
                    entry.SizedBy,
                    [.. entry.KeyOrder],
                    [.. entry.SubKeyOrder]
                )
            )
        ),
    ];

    private static readonly FrozenDictionary<string, ImmutableArray<DynamicFamily>> ByClass = Families
        .GroupBy(row => row.Class, StringComparer.Ordinal)
        .ToFrozenDictionary(group => group.Key, group => group.Select(row => row.Family).ToImmutableArray(), StringComparer.Ordinal);

    // Two rows for a BoneConstraint3D subclass: every setting's constraint keys (BoneConstraint3D::get_property_list), then
    // every setting's own keys in the subclass's order.
    private static Row[] BoneConstraintRows(string cls, string[] ownKeys) =>
        [
            Row.SizedFirst(cls, ConstraintPattern, "setting_count", ConstraintKeys, []),
            Row.SizedFirst(cls, @"^settings/(?<index>\d+)/(?<key>.*)", "setting_count", ownKeys, []),
        ];

    // Two VisualShader rows per graph type, in type_string order: the type's nodes, then its connections.
    private static IEnumerable<Row> VisualShaderNodeRows() =>
        VisualShaderTypes.SelectMany(type =>
            new[]
            {
                Row.OnKey("VisualShader", $@"^nodes/{type}/(?<index>\d+)/(?<key>[^/]*)", VisualShaderNodeKeys),
                Row.AnyOrder("VisualShader", $@"^nodes/{type}/connections$"),
            }
        );

    /// <summary>How Godot creates a dynamic family's entries when a scene or resource loads, which decides their order on the way back.</summary>
    internal enum EntryCreation
    {
        /// <summary>Each name sets its value on its own, or any key of an entry creates it: the source order loads.</summary>
        AnyOrder,

        /// <summary>
        /// A count property ranked before the family sizes its entries, so each name only fills a slot that exists. The row may
        /// carry the declaring class's key order and one nested level, which keeps a round trip byte-identical, and which the
        /// load order needs where keys reset or hide one another.
        /// </summary>
        SizedFirst,

        /// <summary>
        /// Godot creates an entry only on one key (the creating key), so each entry's names come creating key first, and entries
        /// in numeric order, which a family that appends at the next index (Animation tracks, replication properties, TileSet
        /// patterns) needs.
        /// </summary>
        OnKey,
    }

    /// <summary>Every row of the table, in table order.</summary>
    internal static IEnumerable<(string Class, DynamicFamily Family)> All => Families;

    /// <summary>The name families a class adds itself, in the order its <c>_get_property_list</c> adds them.</summary>
    /// <param name="className">The Godot class name.</param>
    /// <returns>The families, each an anchored pattern over a property name with its key order; empty for a class that adds none.</returns>
    public static ImmutableArray<DynamicFamily> Of(string className) =>
        ByClass.TryGetValue(className, out ImmutableArray<DynamicFamily> families) ? families : [];

    /// <summary>A dynamic name family: its anchored pattern, how Godot creates its entries and the key order inside one entry.</summary>
    /// <param name="Pattern">
    /// The pattern over a property name; an <see cref="EntryCreation.OnKey"/> family names an <c>index</c> group, and a
    /// <c>key</c> group when its entries have keys. A <see cref="EntryCreation.SizedFirst"/> family with a key order names
    /// both, and one level nested inside a key adds a <c>subindex</c> group and a <c>subkey</c> group when it has keys.
    /// </param>
    /// <param name="Creation">How Godot creates the family's entries on load.</param>
    /// <param name="SizedBy">The count property's name for a <see cref="EntryCreation.SizedFirst"/> family; null otherwise.</param>
    /// <param name="KeyOrder">The keys of one entry in the declaring class's order, creating key first; empty for a family without keys.</param>
    /// <param name="SubKeyOrder">
    /// The keys of one nested entry in the declaring class's order; empty for a family without nested keys.
    /// </param>
    internal readonly record struct DynamicFamily(
        Regex Pattern,
        EntryCreation Creation,
        string? SizedBy,
        ImmutableArray<string> KeyOrder,
        ImmutableArray<string> SubKeyOrder
    );

    // A table row: a class, its family's pattern and how Godot creates the family's entries. Only the factories make one, so
    // every row states its kind: in any order, sized first by a named count property, or on one key with its key order.
    private sealed record Row
    {
        private Row(string cls, string pattern, EntryCreation creation, string? sizedBy, string[] keyOrder, string[] subKeyOrder)
        {
            Class = cls;
            Pattern = pattern;
            Creation = creation;
            SizedBy = sizedBy;
            KeyOrder = keyOrder;
            SubKeyOrder = subKeyOrder;
        }

        public string Class { get; }

        public string Pattern { get; }

        public EntryCreation Creation { get; }

        public string? SizedBy { get; }

        public string[] KeyOrder { get; }

        public string[] SubKeyOrder { get; }

        // A family whose names Godot sets in any order.
        public static Row AnyOrder(string cls, string pattern) => new(cls, pattern, EntryCreation.AnyOrder, null, [], []);

        // A family whose entries a count property, ranked before it, sizes first.
        public static Row SizedFirst(string cls, string pattern, string sizedBy) => SizedFirst(cls, pattern, sizedBy, [], []);

        // A sized family whose keys reset one another, with the declaring class's key order and, for one nested level, its
        // nested key order (empty for nested entries without keys).
        public static Row SizedFirst(string cls, string pattern, string sizedBy, string[] keyOrder, string[] subKeyOrder) =>
            new(cls, pattern, EntryCreation.SizedFirst, sizedBy, keyOrder, subKeyOrder);

        // A family Godot creates entry by entry on one key; an empty key order for a family with no key group.
        public static Row OnKey(string cls, string pattern, string[] keyOrder) => new(cls, pattern, EntryCreation.OnKey, null, keyOrder, []);
    }
}
