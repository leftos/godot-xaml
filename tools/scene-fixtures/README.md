# scene-fixtures

Writes `tests/fixtures/scenes/`: `.tscn` and `.tres` files saved by Godot 4.7's own text saver (`ResourceSaver.save`). The core's scene reader and writer are tested against these files, so every expected byte comes from Godot, never from hand.

## Run

```
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/scene-fixtures.log --timeout 300 --slot light -- pwsh -NoProfile -File tools/scene-fixtures/generate.ps1 -Godot F:/Godot/Godot_console.exe
```

`generate.ps1` takes `-Godot` (else `$env:GODOT_BIN`) and an optional `-OutDir` (default `tests/fixtures/scenes/`), runs `generate.gd` headless over this folder's project, prints `scene-fixtures: wrote N files` and exits with Godot's status.

## Regenerating is a deliberate act

Ext-resource and sub-resource ids are pinned (`Resource.set_id_for_path`, `resource_scene_unique_id`), so they are the same on every run. Node `unique_id` values are not: the packer draws them at random (`packed_scene.cpp`, `SceneState::_parse_node`) and `Node.set_unique_scene_id` is not exposed to scripts. Every run therefore rewrites the `unique_id=` numbers of every scene, and nothing else.

Regenerate only when a case is added or changed. A changed case commits the whole folder after the run; a new case is copied alone from an `-OutDir` run, so the other fixtures keep their `unique_id`s. A new fixture also goes in the fixture lists (`SceneForwardTests.Scenes`, `SceneBackTests.Fixtures`, `SceneRoundTripTests.Fixtures`; `SceneBackTests` reads a generated fixture back from its golden unless `SceneBackTests.Inputs` holds a hand-written input, and `godot/compare.ps1` counts the fixture files itself); a `.tres` fixture joins `ResourceConversionTests.Fixtures` and `ResourceForwardTests.Resources` instead of the scene lists.

## Files

- `generate.gd`: the generator (extends `SceneTree`, works in `_initialize`). Each case builds its scene or resource in code, saves it under `res://cases/` (ignored by git; cases reference each other there by `res://` path) and copies the saved bytes unchanged to the output folder.
- `family_cases.gd`: the dynamic-family cases, which generate.gd calls.
- `holder.gd`: a node script with an exported `Node` property, which gives `node_paths=`.
- `tagged.gd`: a resource script with `class_name`, which gives `script_class=`.
- `quoted.gd`: a resource script whose property names `property_name_encode` must quote.

## Cases

- `lone_root.tscn`: one node.
- `nested.tscn`: children with parent paths and a property.
- `unique_name.tscn`: `unique_name_in_owner`.
- `groups.tscn`: two groups, which the saver sorts.
- `script_node.tscn`: a script ext resource and `node_paths=`.
- `material.tres`: a plain resource.
- `gradient_texture.tres`: a resource with a sub-resource.
- `sub_resources.tscn`: a sub-resource chain used by one node and a sub-resource shared by two.
- `ext_resource.tscn`: another case's `.tres` as an ext resource.
- `instanced.tscn`: an instance of `nested.tscn` with properties overridden on it and on a node inside it, a child added inside it (`index=`) and editable children (`[editable]`).
- `inherited.tscn`: a scene inheriting `nested.tscn`. Scene inheritance cannot be set up from a script, so the generator writes a one-node seed scene that inherits `nested.tscn`, loads it, edits it and saves the result; the fixture is the saver's output.
- `connections.tscn`: connections with default flags, deferred, one-shot, binds and unbinds.
- `script_class.tres`: a resource with a global script class.
- `packed_bytes.tres`: a PackedByteArray over 64 bytes, which makes the file `format=4`.
- `quoted_name.tres`: property names with a space, `=`, `"` and a non-ASCII character.
- `placeholder.tscn`: `instance_placeholder=`.
- `multiline.tscn`: a multi-line string and a dictionary inside a node section.
- `property_order.tscn`: properties whose result depends on the order they are set in: `grow_horizontal` and `grow_vertical` with `layout_mode`, `button_pressed` with `toggle_mode`.
- `order_inner.tscn`: the scene `instanced_order` instances: a `Control` root and a `Panel` child at `layout_mode = 1`, so an override that sets 0 is saved.
- `instanced_order.tscn`: `layout_mode = 0` and `grow_vertical = 2` on an instance root and, through an override, on its child; in alphabetical order the grow direction is reset, so it proves the way back reads the instanced scene's classes.
- `placeholder_order.tscn`: a `Control` root with a placeholder of `order_inner` that sets `layout_mode` and `grow_vertical`, so the way back reads the placeholder scene's root class.
- `animation_tracks.tres`: an Animation with tracks of several types (value, method, bezier, rotation, blend shape), one disabled and one cubic, each with a path and keys, past ten tracks, so the way back must set each track's `type` first and run the tracks in numeric order.
- `skeleton_bones.tscn`: a Skeleton3D with bones past ten, a branching parent chain, one bone disabled and one with `bone_meta`, so the way back must set each bone's `name` first.
- `bone_chain_settings.tscn`: a Skeleton3D of a 12-bone chain holding a SpringBoneSimulator3D (an individual-config setting with per-joint values, and a setting with a collision list), a FABRIK3D with two settings' joint axes and a SplineIK3D, so the way back must set each setting's bones before its joints and `enable_all_child_collisions` before its collisions, with joints past ten in numeric order.
- `two_bone_ik_modifiers.tscn`: a Skeleton3D of a 12-bone chain holding a TwoBoneIK3D, a LimitAngularVelocityModifier3D, an AimModifier3D, a CopyTransformModifier3D, a ConvertTransformModifier3D and a BoneTwistDisperser3D (custom twist amounts per joint), each with 12 settings or chains, so the way back must keep each class's save order with entries past ten in numeric order.
- `visual_shader_nodes.tres`: a VisualShader with Expression nodes (ports and code) at id 10 in two graphs, a GlobalExpression, constants on both sides of id 10, a connection into an Expression and a moved output node, so the way back must set each node's `node` first, the nodes in numeric order and each graph's `connections` after its nodes.
- `visual_shader_depth_test.tres`: a spatial VisualShader whose `modes/depth_test` is inverted (2), the one Spatial mode no other fixture sets, so the way back must keep the mode Godot stores.
- `visual_shader_types.tres`: a spatial VisualShader with a constant wired into a FloatOp at id 12 and a moved output node in each of the 11 graph types, render modes and flags, the stencil keys and 12 varyings, and a canvas item VisualShader in `metadata/extra` that stores `mode`, so the way back must keep every graph type and each VisualShader family in Godot's save order. `preview_params/` is set but not stored: VisualShader accepts it only in the editor.
- `visual_shader_canvas_item.tres`, `visual_shader_particles.tres`, `visual_shader_sky.tres`: a VisualShader per mode other than spatial that registers flags, storing `mode` and every flag of its mode, so the way back must list `flags/` in the order that mode registers them (canvas item puts `skip_vertex_transform` before `unshaded`, spatial the other way).
- `replication_properties.tres`: a SceneReplicationConfig with properties past ten, each with its own spawn and replication mode, so the way back must set each property's `path` first and run the properties in numeric order.
- `tile_set_patterns.tres`: a TileSet with patterns past ten, each with one cell in its own place, so the way back must run the patterns in numeric order.
- `animation_nodes.tres`: an AnimationNodeBlendTree of 12 nodes added out of name order, holding an AnimationNodeTransition of 12 named inputs wired between the nodes and the output, an AnimationNodeStateMachine of 12 states with transitions and a graph offset, and an AnimationNodeBlendSpace1D and an AnimationNodeBlendSpace2D of 12 named points each, so the way back must set each node, state, point and input before the values that refer to it, with entries past ten in numeric order.
- `animation_player_library.tscn`: an AnimationPlayer with the default library and one named `extra`, 12 animations each (a distinct length, a value track with two keys and three markers; one with a position track, compressed), the next animation of 12 animations past ten and four blend times, beside an AnimationTree whose AnimationNodeBlendSpace1D root holds 3 points at a set blend position, so the way back must keep `libraries/`, `next/`, `blend_times`, `parameters/`, `markers` and `_compression` in Godot's save order.
- `skeleton_modification_chains.tscn`: a Skeleton2D of a 12-bone Bone2D chain (each with a set length and angle and its gizmo hidden) whose SkeletonModificationStack2D holds 12 modifications: a SkeletonModification2DCCDIK (constraints on some joints), a FABRIK (magnet positions), a Jiggle (colliders, a collision mask and per-joint stiffness) and a PhysicalBones (node paths), each with 12 joints, then LookAt and TwoBoneIK ones, so the way back must keep `modifications/`, `joint_data/` and `joint_N_nodepath` in numeric order past ten.
- `skeleton_modification_singles.tres`: a SkeletonModification2DLookAt with its constraint keys and additional rotation off their defaults, whose `metadata/extra` holds a SkeletonModification2DStackHolder (a held stack of one modification) and a SkeletonModification2DTwoBoneIK with its four joint keys, so the way back must keep each class's fixed keys in its `_get_property_list` order.
- `physics_bones.tscn`: a Skeleton3D of a 12-bone chain whose PhysicalBoneSimulator3D holds a PhysicalBone3D per bone (a `bone_name`, a joint type from pin to 6DOF and `joint_constraints/` keys off their defaults), and a SoftBody3D with 12 pinned points, some attached to a node or offset, so the way back must set `joint_type` before `joint_constraints/` and `pinned_points` before `attachments/`, past ten in numeric order.
- `tile_set_layers.tres`: a TileSet with 12 of each layer kind (occlusion, physics, terrain sets with terrains, navigation, custom data), each off its defaults, and 12 sources at ids past ten: TileSetAtlasSources whose tiles have alternatives, an animation, a 2x1 size and per-tile data on the layers, and TileSetScenesCollectionSources with scene tiles; then source, coords and alternative tile proxies, so the way back must keep the layer, source, tile and proxy keys in Godot's save order.
- `gui_item_arrays.tscn`: a TabBar, a PopupMenu, an ItemList, an OptionButton, a MenuButton and a FileDialog, each with 12 entries (plain, checkable and radio items, separators, some checked, disabled, with a shared icon or numbered past the default id), so the way back must keep each class's `_get_property_list` key order and list the entries in numeric order past ten.
- `gui_tabs_graph.tscn`: a TabContainer of 12 pages (a title that differs from the page's name, some disabled, hidden or with a shared icon) and a GraphNode of 12 slots (both ports on and coloured, some with a custom icon, some without a stylebox), so the way back must keep each class's `_get_property_list` key order and list the pages and slots in numeric order past ten.

  It also holds an AudioListener2D and an AudioListener3D marked `current`, and an AudioStreamPlayer, an AudioStreamPlayer2D and an AudioStreamPlayer3D playing one shared stream with `parameters/looping` on.

  The pages stay a way-back regression: a loaded scene sets `tab_N/` before its children exist and TabContainer refuses a tab with no child, so both loaders drop them.
- `audio_resources.tres`: an AudioBusLayout of the Master bus and 12 more (name, solo, mute, bypass and volume off their defaults, each sending to the one before), the first holding 12 EQ effects (EQ6s, an EQ10 and an EQ21 with every band's gain set, some disabled), whose `metadata/extra` holds an AudioStreamRandomizer of 12 weighted streams and a LabelSettings of 12 stacked outlines and 12 stacked shadows, so the way back must keep each bus's keys before its effects, the EQ bands by frequency and each family's key order, with buses, effects and entries in numeric order past ten. The generator adds the buses to the AudioServer, which must hold only the Master bus when it starts, and removes them again.
- `theme_font_resources.tres`: a Theme of 12 types, each with a base type, an icon, a stylebox, a font, a font size, a colour and a constant, whose `metadata/extra` holds a FontFile with 12 language and 12 script support overrides and 12 cache entries off their defaults (each with one size's metrics, the first with two sizes), a Skin of 12 binds, a SkeletonProfile of 12 groups and 12 bones and a BoneMap on that profile, so the way back must keep the Theme's sorted list, each cache entry's keys before its sizes, the Skin's and the profile's key orders, with entries in numeric order past ten.
- `shader_parameters.tscn`: a MeshInstance3D and a ColorRect, each with an embedded ShaderMaterial whose shader declares 12 plain uniforms and 12 instance uniforms out of name order and a `group_uniforms` group, all set off their defaults, beside a ShaderGlobalsOverride of 12 globals, so the way back must keep `shader_parameter/` in the shader's order regrouped by group and `instance_shader_parameters/` and `params/` by name past ten.

  The canvas shader adds a `hint_screen_texture` sampler, which Godot does not list. The generator adds the globals to the RenderingServer and removes them again, and `godot/project.godot` defines them for the compare.
- `tile_map_grid_map.tscn`: a TileMap of 12 layers (named, modulated, y-sorted with an origin and a z index, some disabled or without navigation), a GridMap on a MeshLibrary of 12 items (each with its own BoxMesh and every key off its default) with one cell per item and its meshes baked, and a MeshInstance3D on an ArrayMesh of 12 blend shapes named out of name order and 12 surfaces, each shape's value set and each surface with an override material, so the way back must keep the layer and item keys in Godot's order, `blend_shapes/` in the mesh's order and every family in numeric order past ten.

`uid=` attributes are absent from every fixture: the saver asks the editor's file system for uids, which a `--script` run does not start.
