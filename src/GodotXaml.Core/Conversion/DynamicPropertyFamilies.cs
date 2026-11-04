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
/// before the family, named on the row; on one key; or in any order but listed in a key order a sibling property's value
/// chooses (a VisualShader's flags/ by its mode). Godot creates an entry only on one key (the creating key), so each
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
    // only on its name, and reads the 3.x pose and bound_children it kept for compatibility after bone_meta, pose first.
    private static readonly string[] BoneKeys =
    [
        "name",
        "parent",
        "rest",
        "enabled",
        "position",
        "rotation",
        "scale",
        "bone_meta",
        "pose",
        "bound_children",
    ];

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

    // The same list's flags/, which depend on the shader's mode: the render modes without options that ShaderTypes::ShaderTypes
    // registers for the mode, in its order, with Spatial's depth_test_disabled at depth_test's place. Indexed by
    // Shader::Mode (scene/resources/shader.h); Fog registers no render modes and TextureBlit only blend, so neither lists a flag.
    private static readonly string[][] VisualShaderFlags =
    [
        // Spatial
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
        ],
        // CanvasItem
        ["skip_vertex_transform", "unshaded", "light_only", "world_vertex_coords"],
        // Particles
        ["collision_use_scale", "disable_force", "disable_velocity", "keep_data"],
        // Sky
        ["use_half_res_pass", "use_quarter_res_pass", "disable_fog", "use_debanding"],
        // Fog
        [],
        // TextureBlit
        [],
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

    // scene/3d/physics/physical_bone_3d.cpp, the Pin, Cone, Hinge and Slider JointData::_get_property_list: one bone holds
    // one joint type's keys, and this merged order keeps each type's own order (no two types order shared keys differently).
    private static readonly string[] JointConstraintKeys =
    [
        "swing_span",
        "twist_span",
        "bias",
        "damping",
        "impulse_clamp",
        "softness",
        "relaxation",
        "linear_limit_upper",
        "linear_limit_lower",
        "linear_limit_softness",
        "linear_limit_restitution",
        "linear_limit_damping",
        "angular_limit_enabled",
        "angular_limit_upper",
        "angular_limit_lower",
        "angular_limit_bias",
        "angular_limit_softness",
        "angular_limit_relaxation",
        "angular_limit_restitution",
        "angular_limit_damping",
    ];

    // scene/3d/physics/physical_bone_3d.cpp, SixDOFJointData::_get_property_list: the axes, then one axis's keys.
    private static readonly string[] SixDofAxes = ["x", "y", "z"];

    private static readonly string[] SixDofAxisKeys =
    [
        "linear_limit_enabled",
        "linear_limit_upper",
        "linear_limit_lower",
        "linear_limit_softness",
        "linear_spring_enabled",
        "linear_spring_stiffness",
        "linear_spring_damping",
        "linear_equilibrium_point",
        "linear_restitution",
        "linear_damping",
        "angular_limit_enabled",
        "angular_limit_upper",
        "angular_limit_lower",
        "angular_limit_softness",
        "angular_restitution",
        "angular_damping",
        "erp",
        "angular_spring_enabled",
        "angular_spring_stiffness",
        "angular_spring_damping",
        "angular_equilibrium_point",
    ];

    // scene/resources/2d/tile_set.cpp, TileSet::_get_property_list: one terrain set's keys, its mode and then each terrain's
    // name and color in numeric order. A row has one nested level only under a count property, which a terrain set lacks, so
    // the terrains are spelled out: 64 of them, past which a terrain's keys keep source order.
    private static readonly string[] TerrainSetKeys =
    [
        "mode",
        .. Enumerable.Range(0, 64).SelectMany(terrain => new[] { $"terrain_{terrain}/name", $"terrain_{terrain}/color" }),
    ];

    // scene/3d/physics/soft_body_3d.cpp, SoftBody3D::_get_property_list: one attachment's keys.
    private static readonly string[] SoftBodyAttachmentKeys = ["point_index", "spatial_attachment_path", "offset"];

    // scene/resources/2d/skeleton/skeleton_modification_2d_ccdik.cpp, SkeletonModification2DCCDIK::_get_property_list: one
    // joint's keys (editor_draw_gizmo only under an editor hint).
    private static readonly string[] CcdikJointKeys =
    [
        "bone_index",
        "bone2d_node",
        "rotate_from_joint",
        "enable_constraint",
        "constraint_angle_min",
        "constraint_angle_max",
        "constraint_angle_invert",
        "constraint_in_localspace",
        "editor_draw_gizmo",
    ];

    // scene/resources/2d/skeleton/skeleton_modification_2d_fabrik.cpp, SkeletonModification2DFABRIK::_get_property_list: one
    // joint's keys.
    private static readonly string[] FabrikJointKeys = ["bone_index", "bone2d_node", "magnet_position", "use_target_rotation"];

    // scene/resources/2d/skeleton/skeleton_modification_2d_jiggle.cpp, SkeletonModification2DJiggle::_get_property_list: one
    // joint's keys.
    private static readonly string[] JiggleJointKeys =
    [
        "bone_index",
        "bone2d_node",
        "override_defaults",
        "stiffness",
        "mass",
        "damping",
        "use_gravity",
        "gravity",
    ];

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

    private static readonly string ConstraintPattern = $@"^settings/(?<index>[0-9]+)/(?<key>{string.Join("|", ConstraintKeys)})\z";

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

    // scene/gui/tab_bar.cpp, TabBar::_bind_methods (PropertyListHelper registration): one tab's keys.
    private static readonly string[] TabKeys = ["title", "tooltip", "icon", "disabled"];

    // scene/gui/tab_container.cpp, TabContainer::_bind_methods (PropertyListHelper registration): one tab's keys.
    private static readonly string[] ContainerTabKeys = ["title", "icon", "disabled", "hidden"];

    // scene/gui/graph_node.cpp, GraphNode::_get_property_list: one slot's keys.
    private static readonly string[] SlotKeys =
    [
        "left_enabled",
        "left_type",
        "left_color",
        "left_icon",
        "right_enabled",
        "right_type",
        "right_color",
        "right_icon",
        "draw_stylebox",
    ];

    // scene/gui/popup_menu.cpp and scene/gui/menu_button.cpp, PopupMenu::_bind_methods and MenuButton::_bind_methods
    // (PropertyListHelper registrations): one item's keys, the same order in both classes.
    private static readonly string[] PopupItemKeys = ["text", "icon", "checkable", "checked", "id", "disabled", "separator"];

    // scene/gui/item_list.cpp, ItemList::_bind_methods (PropertyListHelper registration): one item's keys.
    private static readonly string[] ListItemKeys = ["text", "icon", "selectable", "disabled"];

    // scene/gui/option_button.cpp, OptionButton::_bind_methods (PropertyListHelper registration): one popup item's keys.
    private static readonly string[] OptionItemKeys = ["text", "icon", "id", "disabled", "separator"];

    // scene/gui/file_dialog.cpp, FileDialog::_bind_methods (PropertyListHelper registration): one option's keys.
    private static readonly string[] DialogOptionKeys = ["name", "values", "default"];

    private static readonly Row[] Entries =
    [
        // scene/animation/animation_mixer.cpp, AnimationMixer::_get_property_list
        Row.AnyOrder("AnimationMixer", @"^libraries/"),
        // scene/animation/animation_player.cpp, AnimationPlayer::_get_property_list
        Row.AnyOrder("AnimationPlayer", @"^next/"),
        Row.AnyOrder("AnimationPlayer", @"^blend_times\z"),
        // scene/animation/animation_tree.cpp, AnimationTree::_get_property_list (AnimationTree::_update_properties)
        Row.AnyOrder("AnimationTree", @"^parameters/"),
        // scene/2d/audio_listener_2d.cpp, AudioListener2D::_get_property_list
        Row.AnyOrder("AudioListener2D", @"^current\z"),
        // scene/3d/audio_listener_3d.cpp, AudioListener3D::_get_property_list
        Row.AnyOrder("AudioListener3D", @"^current\z"),
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
        Row.SizedFirst("MeshInstance3D", @"^surface_material_override/[0-9]+\z", "mesh"),
        // scene/gui/graph_node.cpp, GraphNode::_get_property_list
        Row.OnKey("GraphNode", @"^slot/(?<index>[0-9]+)/(?<key>.*)\z", SlotKeys),
        // scene/gui/tab_container.cpp, TabContainer::_get_property_list (PropertyListHelper)
        Row.OnKey("TabContainer", @"^tab_(?<index>[0-9]+)/(?<key>.*)\z", ContainerTabKeys),
        // scene/gui/tab_bar.cpp, TabBar::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("TabBar", @"^tab_(?<index>[0-9]+)/(?<key>.*)\z", "tab_count", TabKeys, []),
        // scene/gui/popup_menu.cpp, PopupMenu::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("PopupMenu", @"^item_(?<index>[0-9]+)/(?<key>.*)\z", "item_count", PopupItemKeys, []),
        // scene/gui/item_list.cpp, ItemList::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("ItemList", @"^item_(?<index>[0-9]+)/(?<key>.*)\z", "item_count", ListItemKeys, []),
        // scene/gui/option_button.cpp, OptionButton::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("OptionButton", @"^popup/item_(?<index>[0-9]+)/(?<key>.*)\z", "item_count", OptionItemKeys, []),
        // scene/gui/menu_button.cpp, MenuButton::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("MenuButton", @"^popup/item_(?<index>[0-9]+)/(?<key>.*)\z", "item_count", PopupItemKeys, []),
        // scene/gui/file_dialog.cpp, FileDialog::_bind_methods (PropertyListHelper registration)
        Row.SizedFirst("FileDialog", @"^option_(?<index>[0-9]+)/(?<key>.*)\z", "option_count", DialogOptionKeys, []),
        // scene/main/shader_globals_override.cpp, ShaderGlobalsOverride::_get_property_list
        Row.AnyOrder("ShaderGlobalsOverride", @"^params/"),
        // scene/2d/skeleton_2d.cpp, Skeleton2D::_get_property_list
        Row.AnyOrder("Skeleton2D", @"^modification_stack\z"),
        // scene/2d/skeleton_2d.cpp, Bone2D::_get_property_list
        Row.AnyOrder("Bone2D", @"^auto_calculate_length_and_angle\z"),
        Row.AnyOrder("Bone2D", @"^length\z"),
        Row.AnyOrder("Bone2D", @"^bone_angle\z"),
        Row.AnyOrder("Bone2D", @"^editor_settings/show_bone_gizmo\z"),
        // scene/2d/tile_map.cpp, TileMap::_get_property_list (PropertyListHelper)
        Row.AnyOrder("TileMap", @"^format\z"),
        Row.AnyOrder("TileMap", @"^layer_[0-9]+/"),
        // scene/3d/skeleton_3d.cpp, Skeleton3D::_get_property_list
        Row.OnKey("Skeleton3D", @"^bones/(?<index>[0-9]+)/(?<key>[^/]*)", BoneKeys),
        // scene/3d/physics/physical_bone_3d.cpp, PhysicalBone3D::_get_property_list (and its JointData kinds)
        Row.AnyOrder("PhysicalBone3D", @"^bone_name\z"),
        .. JointConstraintRows(),
        // scene/3d/physics/soft_body_3d.cpp, SoftBody3D::_get_property_list: the attachments in numeric order.
        Row.AnyOrder("SoftBody3D", @"^pinned_points\z"),
        Row.SizedFirst("SoftBody3D", @"^attachments/(?<index>[0-9]+)/(?<key>.*)", "pinned_points", SoftBodyAttachmentKeys, []),
        // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list: setting by setting, each joint in
        // numeric order.
        Row.SizedFirst(
            "BoneTwistDisperser3D",
            @"^settings/(?<index>[0-9]+)/(?:(?<key>joints)/(?<subindex>[0-9]+)/(?<subkey>[^/]*)$|(?<key>.*))",
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
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^chains/(?<index>[0-9]+)/(?<key>.*)", "chain_count", LimitChainKeys, []),
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^joints/[0-9]+/", "joint_count"),
        // scene/3d/iterate_ik_3d.cpp, IterateIK3D::_get_property_list (ChainIK3D::get_property_list): every setting's
        // target_node, then each setting's chain keys, then every setting's joint keys.
        Row.SizedFirst("IterateIK3D", @"^settings/(?<index>[0-9]+)/(?<key>target_node)\z", "setting_count", ["target_node"], []),
        Row.SizedFirst("IterateIK3D", @"^settings/(?<index>[0-9]+)/(?!joints/)(?<key>.*)", "setting_count", ChainKeys, []),
        Row.SizedFirst(
            "IterateIK3D",
            @"^settings/(?<index>[0-9]+)/(?<key>joints)/(?<subindex>[0-9]+)/(?<subkey>.*)",
            "setting_count",
            ["joints"],
            IterateJointKeys
        ),
        Row.SizedFirst("IterateIK3D", @"^settings/[0-9]+/", "setting_count"),
        // scene/3d/two_bone_ik_3d.cpp, TwoBoneIK3D::_get_property_list
        Row.SizedFirst("TwoBoneIK3D", @"^settings/(?<index>[0-9]+)/(?<key>.*)", "setting_count", TwoBoneSettingKeys, []),
        // scene/3d/spline_ik_3d.cpp, SplineIK3D::_get_property_list (ChainIK3D::get_property_list): every setting's path
        // and tilt keys, then each setting's chain keys.
        Row.SizedFirst(
            "SplineIK3D",
            @"^settings/(?<index>[0-9]+)/(?<key>path_3d|tilt_enabled|tilt_fade_in|tilt_fade_out)\z",
            "setting_count",
            ["path_3d", "tilt_enabled", "tilt_fade_in", "tilt_fade_out"],
            []
        ),
        Row.SizedFirst("SplineIK3D", @"^settings/(?<index>[0-9]+)/(?<key>.*)", "setting_count", ChainKeys, []),
        // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list: setting by setting, each joint
        // and collision in numeric order.
        Row.SizedFirst(
            "SpringBoneSimulator3D",
            @"^settings/(?<index>[0-9]+)/(?:(?<key>joints|exclude_collisions|collisions)/(?<subindex>[0-9]+)(?:/(?<subkey>[^/]*))?$|(?<key>.*))",
            "setting_count",
            SpringSettingKeys,
            SpringJointKeys
        ),
        // modules/multiplayer/multiplayer_spawner.cpp, MultiplayerSpawner::_get_property_list (editor-only: never stored)
        Row.AnyOrder("MultiplayerSpawner", @"^_spawnable_scene_count\z"),
        Row.SizedFirst("MultiplayerSpawner", @"^scenes/[0-9]+\z", "_spawnable_scene_count"),
        // modules/gridmap/grid_map.cpp, GridMap::_get_property_list
        Row.AnyOrder("GridMap", @"^baked_meshes\z"),
        Row.AnyOrder("GridMap", @"^data\z"),
        // scene/resources/theme.cpp, Theme::_get_property_list (one list, sorted by name)
        Row.AnyOrder("Theme", @"^[^/]+/(?:base_type$|(?:icons|styles|fonts|font_sizes|colors|constants)/)"),
        // scene/resources/2d/tile_set.cpp, TileSet::_get_property_list
        // Each layer kind's layers in numeric order, each layer's keys in a fixed order; TileSet::_set adds layers up to the
        // index on any key, so any order loads.
        Row.OnKey("TileSet", @"^occlusion_layer_(?<index>[0-9]+)/(?<key>.*)", ["light_mask", "sdf_collision"]),
        Row.OnKey(
            "TileSet",
            @"^physics_layer_(?<index>[0-9]+)/(?<key>.*)",
            ["collision_layer", "collision_mask", "collision_priority", "physics_material"]
        ),
        Row.OnKey("TileSet", @"^terrain_set_(?<index>[0-9]+)/(?<key>.*)", TerrainSetKeys),
        Row.OnKey("TileSet", @"^navigation_layer_(?<index>[0-9]+)/(?<key>.*)", ["layers"]),
        Row.OnKey("TileSet", @"^custom_data_layer_(?<index>[0-9]+)/(?<key>.*)", ["name", "type"]),
        Row.AnyOrder("TileSet", @"^sources/[0-9]+\z"),
        // The proxies after the sources, source level, coords level, then alternative level.
        Row.AnyOrder("TileSet", @"^tile_proxies/source_level\z"),
        Row.AnyOrder("TileSet", @"^tile_proxies/coords_level\z"),
        Row.AnyOrder("TileSet", @"^tile_proxies/alternative_level\z"),
        // TileSet::_set appends at most one pattern per name (its loop calls add_pattern, which refuses a pattern it holds).
        Row.OnKey("TileSet", @"^pattern_(?<index>[0-9]+)\z", []),
        // scene/resources/2d/tile_set.cpp, TileSetAtlasSource::_get_property_list
        // TileData::set_terrain and set_terrain_peering_bit fail while terrain_set is unset, and _set creates a tile and an
        // alternative on any of its keys, so every terrain key follows every other tile key.
        Row.AnyOrder("TileSetAtlasSource", @"^[0-9]+:[0-9]+/(?![0-9]+/(?:terrain$|terrains_peering_bit/))"),
        Row.AnyOrder("TileSetAtlasSource", @"^[0-9]+:[0-9]+/[0-9]+/terrain\z"),
        Row.AnyOrder("TileSetAtlasSource", @"^[0-9]+:[0-9]+/[0-9]+/terrains_peering_bit/"),
        // scene/resources/2d/tile_set.cpp, TileSetScenesCollectionSource::_get_property_list
        // Scene tiles by id (create_scene_tile sorts scenes_ids), each tile's scene first; _set creates a tile on either key.
        Row.OnKey("TileSetScenesCollectionSource", @"^scenes/(?<index>[0-9]+)/(?<key>[^/]*)", ["scene", "display_placeholder"]),
        // scene/resources/2d/tile_set.cpp, TileMapPattern::_get_property_list
        Row.AnyOrder("TileMapPattern", @"^tile_data\z"),
        // scene/resources/2d/tile_set.cpp, TileData::_get_property_list
        Row.AnyOrder("TileData", @"^occlusion_layer_[0-9]+/"),
        Row.AnyOrder("TileData", @"^physics_layer_[0-9]+/"),
        Row.AnyOrder("TileData", @"^terrains_peering_bit/"),
        Row.AnyOrder("TileData", @"^navigation_layer_[0-9]+/"),
        Row.AnyOrder("TileData", @"^custom_data_[0-9]+\z"),
        // scene/resources/mesh.cpp, ArrayMesh::_get_property_list (editor-only: never stored)
        Row.AnyOrder("ArrayMesh", @"^surface_[0-9]+/"),
        // scene/resources/font.cpp, FontFile::_get_property_list
        Row.AnyOrder("FontFile", @"^language_support_override/"),
        Row.AnyOrder("FontFile", @"^script_support_override/"),
        Row.AnyOrder("FontFile", @"^cache/[0-9]+/"),
        // scene/resources/3d/skin.cpp, Skin::_get_property_list
        Row.AnyOrder("Skin", @"^bind_count\z"),
        Row.SizedFirst("Skin", @"^bind/[0-9]+/", "bind_count"),
        // scene/resources/3d/mesh_library.cpp, MeshLibrary::_get_property_list
        Row.AnyOrder("MeshLibrary", @"^item/[0-9]+/"),
        // scene/resources/skeleton_profile.cpp, SkeletonProfile::_get_property_list
        Row.SizedFirst("SkeletonProfile", @"^groups/[0-9]+/", "group_size"),
        Row.SizedFirst("SkeletonProfile", @"^bones/[0-9]+/", "bone_size"),
        // scene/resources/bone_map.cpp, BoneMap::_get_property_list
        Row.AnyOrder("BoneMap", @"^bone_map/"),
        // servers/audio/audio_server.cpp, AudioBusLayout::_get_property_list
        Row.AnyOrder("AudioBusLayout", @"^bus/[0-9]+/"),
        // servers/audio/effects/audio_effect_eq.cpp, AudioEffectEQ::_get_property_list
        Row.AnyOrder("AudioEffectEQ", @"^band_db/[0-9]+_hz\z"),
        // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list
        Row.AnyOrder("VisualShader", @"^mode\z"),
        .. VisualShaderModes.Select(mode => Row.AnyOrder("VisualShader", $@"^modes/{mode}\z")),
        Row.AnyOrder("VisualShader", @"^modes/"),
        Row.ByMode("VisualShader", @"^flags/(?<key>[\s\S]*)\z", "mode", VisualShaderFlags),
        Row.AnyOrder("VisualShader", @"^stencil/(?:enabled|reference)\z"),
        Row.AnyOrder("VisualShader", @"^stencil_modes/"),
        Row.AnyOrder("VisualShader", @"^stencil_flags/"),
        Row.AnyOrder("VisualShader", @"^varyings/"),
        Row.AnyOrder("VisualShader", @"^preview_params/"),
        // Type by type in Godot's save order, each type's nodes by id with node first, then its connections:
        // VisualShader::_set connects with connect_nodes_forced, which needs the nodes and their ports.
        .. VisualShaderNodeRows(),
        // A graph type the list does not name (VisualShader::_set reads it as vertex) still needs its node first.
        Row.OnKey("VisualShader", @"^nodes/[^/]+/(?<index>[0-9]+)/(?<key>[^/]*)", VisualShaderNodeKeys),
        Row.AnyOrder("VisualShader", @"^nodes/"),
        // modules/multiplayer/scene_replication_config.cpp, SceneReplicationConfig::_get_property_list
        Row.OnKey("SceneReplicationConfig", @"^properties/(?<index>[0-9]+)/(?<key>[^/]*)", ReplicationPropertyKeys),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeBlendTree::_get_property_list
        Row.AnyOrder("AnimationNodeBlendTree", @"^nodes/"),
        Row.AnyOrder("AnimationNodeBlendTree", @"^node_connections\z"),
        // scene/animation/animation_node_state_machine.cpp, AnimationNodeStateMachine::_get_property_list
        Row.AnyOrder("AnimationNodeStateMachine", @"^states/"),
        Row.AnyOrder("AnimationNodeStateMachine", @"^transitions\z"),
        Row.AnyOrder("AnimationNodeStateMachine", @"^graph_offset\z"),
        // scene/animation/animation_blend_space_1d.cpp, AnimationNodeBlendSpace1D::_get_property_list
        Row.OnKey("AnimationNodeBlendSpace1D", @"^blend_point_(?<index>[0-9]+)/(?<key>[^/]*)", BlendPointKeys),
        // scene/animation/animation_blend_space_2d.cpp, AnimationNodeBlendSpace2D::_get_property_list
        Row.OnKey("AnimationNodeBlendSpace2D", @"^blend_point_(?<index>[0-9]+)/(?<key>[^/]*)", BlendPointKeys),
        // scene/animation/animation_blend_tree.cpp, AnimationNodeTransition::_get_property_list; _set adds an input only on
        // input_<count>/name, and input_count is an editor-only property the file never holds.
        Row.OnKey("AnimationNodeTransition", @"^input_(?<index>[0-9]+)/(?<key>[^/]*)", TransitionInputKeys),
        // scene/resources/2d/skeleton/skeleton_modification_2d_ccdik.cpp, SkeletonModification2DCCDIK::_get_property_list
        Row.SizedFirst("SkeletonModification2DCCDIK", @"^joint_data/(?<index>[0-9]+)/(?<key>.*)", "ccdik_data_chain_length", CcdikJointKeys, []),
        Row.AnyOrder("SkeletonModification2DCCDIK", @"^editor/draw_gizmo\z"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_fabrik.cpp, SkeletonModification2DFABRIK::_get_property_list
        Row.SizedFirst("SkeletonModification2DFABRIK", @"^joint_data/(?<index>[0-9]+)/(?<key>.*)", "fabrik_data_chain_length", FabrikJointKeys, []),
        // scene/resources/2d/skeleton/skeleton_modification_2d_jiggle.cpp, SkeletonModification2DJiggle::_get_property_list
        Row.AnyOrder("SkeletonModification2DJiggle", @"^use_colliders\z"),
        Row.AnyOrder("SkeletonModification2DJiggle", @"^collision_mask\z"),
        Row.SizedFirst("SkeletonModification2DJiggle", @"^joint_data/(?<index>[0-9]+)/(?<key>.*)", "jiggle_data_chain_length", JiggleJointKeys, []),
        // scene/resources/2d/skeleton/skeleton_modification_2d_lookat.cpp, SkeletonModification2DLookAt::_get_property_list
        Row.AnyOrder("SkeletonModification2DLookAt", @"^enable_constraint\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_min\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_max\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_angle_invert\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^constraint_in_localspace\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^additional_rotation\z"),
        Row.AnyOrder("SkeletonModification2DLookAt", @"^editor/draw_gizmo\z"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_physicalbones.cpp, SkeletonModification2DPhysicalBones::_get_property_list
        Row.AnyOrder("SkeletonModification2DPhysicalBones", @"^fetch_bones\z"),
        // Each joint's one key, the joints in numeric order.
        Row.SizedFirst(
            "SkeletonModification2DPhysicalBones",
            @"^joint_(?<index>[0-9]+)_(?<key>nodepath)\z",
            "physical_bone_chain_length",
            ["nodepath"],
            []
        ),
        // scene/resources/2d/skeleton/skeleton_modification_2d_stackholder.cpp, SkeletonModification2DStackHolder::_get_property_list
        Row.AnyOrder("SkeletonModification2DStackHolder", @"^held_modification_stack\z"),
        Row.AnyOrder("SkeletonModification2DStackHolder", @"^editor/draw_gizmo\z"),
        // scene/resources/2d/skeleton/skeleton_modification_2d_twoboneik.cpp, SkeletonModification2DTwoBoneIK::_get_property_list
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_one_bone_idx\z"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_one_bone2d_node\z"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_two_bone_idx\z"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^joint_two_bone2d_node\z"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^editor/draw_gizmo\z"),
        Row.AnyOrder("SkeletonModification2DTwoBoneIK", @"^editor/draw_min_max\z"),
        // scene/resources/2d/skeleton/skeleton_modification_stack_2d.cpp, SkeletonModificationStack2D::_get_property_list
        // Each entry is one modification, the entries in numeric order.
        Row.SizedFirst("SkeletonModificationStack2D", @"^(?<key>modifications)/(?<index>[0-9]+)\z", "modification_count", ["modifications"], []),
        // scene/resources/material.cpp, ShaderMaterial::_get_property_list
        Row.AnyOrder("ShaderMaterial", @"^shader_parameter/"),
        // scene/resources/animation.cpp, Animation::_get_property_list
        Row.AnyOrder("Animation", @"^_compression\z"),
        Row.AnyOrder("Animation", @"^markers\z"),
        Row.OnKey("Animation", @"^tracks/(?<index>[0-9]+)/(?<key>[^/]*)", TrackKeys),
        // scene/resources/curve.h, Curve::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve", @"^point_[0-9]+/", "point_count"),
        // scene/resources/curve.h, Curve2D::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve2D", @"^point_[0-9]+/", "point_count"),
        // scene/resources/curve.h, Curve3D::_get_property_list (PropertyListHelper; editor-only: never stored)
        Row.SizedFirst("Curve3D", @"^point_[0-9]+/", "point_count"),
        // scene/resources/label_settings.h, LabelSettings::_get_property_list (PropertyListHelper)
        Row.SizedFirst("LabelSettings", @"^stacked_outline_[0-9]+/", "stacked_outline_count"),
        Row.SizedFirst("LabelSettings", @"^stacked_shadow_[0-9]+/", "stacked_shadow_count"),
        // servers/audio/audio_stream.h, AudioStreamRandomizer::_get_property_list (PropertyListHelper)
        Row.SizedFirst("AudioStreamRandomizer", @"^stream_[0-9]+/", "streams_count"),
    ];

    private static readonly ImmutableArray<(string Class, DynamicFamily Family)> Families =
    [
        .. Entries.Select(entry => (entry.Class, FamilyOf(entry))),
    ];

    // A row's family: its compiled pattern, the key orders it declares with their span-searched lookups, and the literal its
    // ranker compares instead of the pattern where the pattern is one.
    private static DynamicFamily FamilyOf(Row entry)
    {
        return new DynamicFamily(
            new Regex(entry.Pattern, RegexOptions.CultureInvariant),
            entry.Creation,
            entry.SizedBy,
            [.. entry.KeyOrder],
            [.. entry.SubKeyOrder],
            KeyLookup(entry.KeyOrder),
            KeyLookup(entry.SubKeyOrder),
            LiteralOf(entry),
            entry.ModeBy,
            [.. entry.ModeKeyOrders.Select(order => order.ToImmutableArray())],
            [.. entry.ModeKeyOrders.Select(KeyLookup)]
        );
    }

    // The end anchor every exact row carries: \z, which matches only at the end of the name, where a $ would also match before a
    // final newline the ranker's ordinal comparison rejects.
    private const string EndAnchor = @"\z";

    // A row's pattern is its own literal when it is a prefix (^literal) or an exact name (^literal\z) and holds no pattern
    // syntax, so an ordinal comparison agrees with the pattern without running it. A row with a key order keeps its pattern,
    // since a literal cannot place a key.
    private static (string Text, LiteralMatch Kind)? LiteralOf(Row entry)
    {
        if (entry.KeyOrder.Length > 0 || entry.SubKeyOrder.Length > 0 || LiteralBody(entry.Pattern) is not { } body)
        {
            return null;
        }

        return (body, entry.Pattern.EndsWith(EndAnchor, StringComparison.Ordinal) ? LiteralMatch.Exact : LiteralMatch.Prefix);
    }

    // The part of ^literal or ^literal\z that holds no pattern syntax, else null.
    private static string? LiteralBody(string pattern)
    {
        if (pattern.Length < 2 || !pattern.StartsWith('^'))
        {
            return null;
        }

        string body = pattern.EndsWith(EndAnchor, StringComparison.Ordinal) ? pattern[1..^2] : pattern[1..];
        return body.Length > 0 && Regex.Escape(body) == body ? body : null;
    }

    // Each key's declaring place, sorted by key with the ordinal comparison, so a probe compares the key group's span and
    // allocates nothing.
    private static ImmutableArray<(string Key, int Index)> KeyLookup(string[] keys) =>
        [.. keys.Select((key, index) => (Key: key, Index: index)).OrderBy(entry => entry.Key, StringComparer.Ordinal)];

    private static readonly FrozenDictionary<string, ImmutableArray<DynamicFamily>> ByClass = Families
        .GroupBy(row => row.Class, StringComparer.Ordinal)
        .ToFrozenDictionary(group => group.Key, group => group.Select(row => row.Family).ToImmutableArray(), StringComparer.Ordinal);

    // Two rows for a BoneConstraint3D subclass: every setting's constraint keys (BoneConstraint3D::get_property_list), then
    // every setting's own keys in the subclass's order.
    private static Row[] BoneConstraintRows(string cls, string[] ownKeys) =>
        [
            Row.SizedFirst(cls, ConstraintPattern, "setting_count", ConstraintKeys, []),
            Row.SizedFirst(cls, @"^settings/(?<index>[0-9]+)/(?<key>.*)", "setting_count", ownKeys, []),
        ];

    // One PhysicalBone3D row per joint_constraints/ key, in each JointData's order: the Pin, Cone, Hinge and Slider keys, then
    // the 6DOF keys axis by axis. An any-order row carries no key order, so each key is its own row.
    private static IEnumerable<Row> JointConstraintRows() =>
        [
            .. JointConstraintKeys.Select(key => Row.AnyOrder("PhysicalBone3D", $@"^joint_constraints/{key}\z")),
            .. SixDofAxes.SelectMany(axis => SixDofAxisKeys.Select(key => Row.AnyOrder("PhysicalBone3D", $@"^joint_constraints/{axis}/{key}\z"))),
        ];

    // Two VisualShader rows per graph type, in type_string order: the type's nodes, then its connections.
    private static IEnumerable<Row> VisualShaderNodeRows() =>
        VisualShaderTypes.SelectMany(type =>
            new[]
            {
                Row.OnKey("VisualShader", $@"^nodes/{type}/(?<index>[0-9]+)/(?<key>[^/]*)", VisualShaderNodeKeys),
                Row.AnyOrder("VisualShader", $@"^nodes/{type}/connections\z"),
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

        /// <summary>
        /// Each name sets its value on its own, as in <see cref="AnyOrder"/>, but Godot lists the keys in an order a sibling
        /// property chooses (a VisualShader's flags/ by its mode). The row names the sibling and one key order per value of it;
        /// a value with no order, or one past them, keeps source order, and so does a key the chosen order lacks, after the
        /// listed keys.
        /// </summary>
        ByMode,
    }

    /// <summary>How a family's literal names a property, for a row the pattern alone names by one literal.</summary>
    internal enum LiteralMatch
    {
        /// <summary>The literal is a prefix of the name (<c>^literal</c>), which matches any name it starts.</summary>
        Prefix,

        /// <summary>The literal is the whole name (<c>^literal\z</c>).</summary>
        Exact,
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
    /// <param name="KeyLookup">Each key of <paramref name="KeyOrder"/> with its place there, sorted by key.</param>
    /// <param name="SubKeyLookup">Each key of <paramref name="SubKeyOrder"/> with its place there, sorted by key.</param>
    /// <param name="Literal">
    /// The pattern's whole literal when the pattern is a prefix or an exact name, with how it names the property, which the
    /// ranker compares ordinally instead of running the pattern; null for a family the pattern alone decides.
    /// </param>
    /// <param name="ModeBy">
    /// The sibling property whose integer value chooses the key order of a <see cref="EntryCreation.ByMode"/> family; null
    /// otherwise.
    /// </param>
    /// <param name="ModeKeyOrders">
    /// The key orders of a <see cref="EntryCreation.ByMode"/> family, one per value of <paramref name="ModeBy"/> from 0; empty
    /// otherwise.
    /// </param>
    /// <param name="ModeKeyLookups">Each order of <paramref name="ModeKeyOrders"/> as a lookup sorted by key.</param>
    internal readonly record struct DynamicFamily(
        Regex Pattern,
        EntryCreation Creation,
        string? SizedBy,
        ImmutableArray<string> KeyOrder,
        ImmutableArray<string> SubKeyOrder,
        ImmutableArray<(string Key, int Index)> KeyLookup,
        ImmutableArray<(string Key, int Index)> SubKeyLookup,
        (string Text, LiteralMatch Kind)? Literal,
        string? ModeBy,
        ImmutableArray<ImmutableArray<string>> ModeKeyOrders,
        ImmutableArray<ImmutableArray<(string Key, int Index)>> ModeKeyLookups
    );

    // A table row: a class, its family's pattern and how Godot creates the family's entries. Only the factories make one, so
    // every row states its kind: in any order, sized first by a named count property, on one key with its key order, or in
    // an order a sibling property chooses.
    private sealed record Row
    {
        private Row(
            string cls,
            string pattern,
            EntryCreation creation,
            string? sizedBy,
            string[] keyOrder,
            string[] subKeyOrder,
            string? modeBy,
            string[][] modeKeyOrders
        )
        {
            Class = cls;
            Pattern = pattern;
            Creation = creation;
            SizedBy = sizedBy;
            KeyOrder = keyOrder;
            SubKeyOrder = subKeyOrder;
            ModeBy = modeBy;
            ModeKeyOrders = modeKeyOrders;
        }

        public string? ModeBy { get; }

        public string[][] ModeKeyOrders { get; }

        public string Class { get; }

        public string Pattern { get; }

        public EntryCreation Creation { get; }

        public string? SizedBy { get; }

        public string[] KeyOrder { get; }

        public string[] SubKeyOrder { get; }

        // A family whose names Godot sets in any order.
        public static Row AnyOrder(string cls, string pattern) => new(cls, pattern, EntryCreation.AnyOrder, null, [], [], null, []);

        // A family whose entries a count property, ranked before it, sizes first.
        public static Row SizedFirst(string cls, string pattern, string sizedBy) => SizedFirst(cls, pattern, sizedBy, [], []);

        // A sized family whose keys reset one another, with the declaring class's key order and, for one nested level, its
        // nested key order (empty for nested entries without keys).
        public static Row SizedFirst(string cls, string pattern, string sizedBy, string[] keyOrder, string[] subKeyOrder) =>
            new(cls, pattern, EntryCreation.SizedFirst, sizedBy, keyOrder, subKeyOrder, null, []);

        // A family Godot creates entry by entry on one key; an empty key order for a family with no key group.
        public static Row OnKey(string cls, string pattern, string[] keyOrder) =>
            new(cls, pattern, EntryCreation.OnKey, null, keyOrder, [], null, []);

        // A family whose names Godot sets in any order but lists in the key order the sibling property's integer value picks,
        // one order per value from 0; the pattern names the key in a key group.
        public static Row ByMode(string cls, string pattern, string modeBy, string[][] modeKeyOrders) =>
            new(cls, pattern, EntryCreation.ByMode, null, [], [], modeBy, modeKeyOrders);
    }
}
