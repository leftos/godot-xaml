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
/// <c>index</c> group and any key after it in a <c>key</c> group, and the row carries the declaring class's key order.
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

    // modules/visual_shader/visual_shader.cpp, VisualShader::_get_property_list; VisualShader::_set creates a node only on its
    // node, and casts the node unchecked for size, input_ports, output_ports and expression.
    private static readonly string[] VisualShaderNodeKeys = ["node", "position", "size", "input_ports", "output_ports", "expression"];

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
        // scene/3d/bone_twist_disperser_3d.cpp, BoneTwistDisperser3D::_get_property_list
        Row.SizedFirst("BoneTwistDisperser3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/aim_modifier_3d.cpp, AimModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        Row.SizedFirst("AimModifier3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/copy_transform_modifier_3d.cpp, CopyTransformModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        Row.SizedFirst("CopyTransformModifier3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/convert_transform_modifier_3d.cpp, ConvertTransformModifier3D::_get_property_list (BoneConstraint3D::get_property_list)
        Row.SizedFirst("ConvertTransformModifier3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/limit_angular_velocity_modifier_3d.cpp, LimitAngularVelocityModifier3D::_get_property_list
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^chains/\d+/", "chain_count"),
        Row.SizedFirst("LimitAngularVelocityModifier3D", @"^joints/\d+/", "joint_count"),
        // scene/3d/iterate_ik_3d.cpp, IterateIK3D::_get_property_list (ChainIK3D::get_property_list)
        Row.SizedFirst("IterateIK3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/two_bone_ik_3d.cpp, TwoBoneIK3D::_get_property_list
        Row.SizedFirst("TwoBoneIK3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/spline_ik_3d.cpp, SplineIK3D::_get_property_list (ChainIK3D::get_property_list)
        Row.SizedFirst("SplineIK3D", @"^settings/\d+/", "setting_count"),
        // scene/3d/spring_bone_simulator_3d.cpp, SpringBoneSimulator3D::_get_property_list
        Row.SizedFirst("SpringBoneSimulator3D", @"^settings/\d+/", "setting_count"),
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
        Row.AnyOrder("VisualShader", @"^modes/"),
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
        // scene/animation/animation_blend_tree.cpp, AnimationNodeTransition::_get_property_list
        Row.SizedFirst("AnimationNodeTransition", @"^input_\d+/", "input_count"),
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
                new DynamicFamily(new Regex(entry.Pattern, RegexOptions.CultureInvariant), entry.Creation, entry.SizedBy, [.. entry.KeyOrder])
            )
        ),
    ];

    private static readonly FrozenDictionary<string, ImmutableArray<DynamicFamily>> ByClass = Families
        .GroupBy(row => row.Class, StringComparer.Ordinal)
        .ToFrozenDictionary(group => group.Key, group => group.Select(row => row.Family).ToImmutableArray(), StringComparer.Ordinal);

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

        /// <summary>A count property ranked before the family sizes its entries, so each name only fills a slot that exists.</summary>
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
    /// <c>key</c> group when its entries have keys.
    /// </param>
    /// <param name="Creation">How Godot creates the family's entries on load.</param>
    /// <param name="SizedBy">The count property's name for a <see cref="EntryCreation.SizedFirst"/> family; null otherwise.</param>
    /// <param name="KeyOrder">The keys of one entry in the declaring class's order, creating key first; empty for a family without keys.</param>
    internal readonly record struct DynamicFamily(Regex Pattern, EntryCreation Creation, string? SizedBy, ImmutableArray<string> KeyOrder);

    // A table row: a class, its family's pattern and how Godot creates the family's entries. Only the factories make one, so
    // every row states its kind: in any order, sized first by a named count property, or on one key with its key order.
    private sealed record Row
    {
        private Row(string cls, string pattern, EntryCreation creation, string? sizedBy, string[] keyOrder)
        {
            Class = cls;
            Pattern = pattern;
            Creation = creation;
            SizedBy = sizedBy;
            KeyOrder = keyOrder;
        }

        public string Class { get; }

        public string Pattern { get; }

        public EntryCreation Creation { get; }

        public string? SizedBy { get; }

        public string[] KeyOrder { get; }

        // A family whose names Godot sets in any order.
        public static Row AnyOrder(string cls, string pattern) => new(cls, pattern, EntryCreation.AnyOrder, null, []);

        // A family whose entries a count property, ranked before it, sizes first.
        public static Row SizedFirst(string cls, string pattern, string sizedBy) => new(cls, pattern, EntryCreation.SizedFirst, sizedBy, []);

        // A family Godot creates entry by entry on one key; an empty key order for a family with no key group.
        public static Row OnKey(string cls, string pattern, string[] keyOrder) => new(cls, pattern, EntryCreation.OnKey, null, keyOrder);
    }
}
