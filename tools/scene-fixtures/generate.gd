extends SceneTree
## Writes the scene fixtures: .tscn and .tres files saved by Godot's own text saver.
##
## Run: godot --headless --path tools/scene-fixtures --script res://generate.gd --
##     --out-dir <absolute tests/fixtures/scenes folder>
## Each case builds a scene or resource in code, saves it with ResourceSaver.save under
## res://cases/ (cases reference each other there) and copies the saved bytes unchanged to
## <out-dir>/<case>.tscn or .tres. Ext and sub-resource ids are pinned; node unique_id values
## are random on every run (Node.set_unique_scene_id is not exposed to scripts).

const FamilyCases := preload("res://family_cases.gd")
const CASES_DIR := "res://cases"
## Scene inheritance cannot be set up from a script (Node.set_scene_inherited_state is not
## exposed), so the inherited case loads this one-node scene that inherits nested.tscn, edits
## it and lets the saver write the result.
const INHERITED_SEED := (
	'[gd_scene format=3]\n\n[ext_resource type="PackedScene" path="res://cases/nested.tscn"'
	+ ' id="1"]\n\n[node name="Root" instance=ExtResource("1")]\n'
)

var _out_dir := ""
var _written: int = 0


func _initialize() -> void:
	var error := _run()
	if not error.is_empty():
		push_error("generate.gd: " + error)
		quit(1)
		return
	print("scene-fixtures: wrote %d files" % _written)
	quit(0)


func _run() -> String:
	_out_dir = _arg_value("--out-dir")
	if _out_dir.is_empty():
		return "pass -- --out-dir <absolute folder>"
	for folder in [ProjectSettings.globalize_path(CASES_DIR), _out_dir]:
		var error := _clear(folder)
		if not error.is_empty():
			return error
	var steps: Array[Callable] = [
		_lone_root,
		_nested,
		_unique_name,
		_groups,
		_script_node,
		_material,
		_gradient_texture,
		_sub_resources,
		_ext_resource,
		_instanced,
		_inherited,
		_connections,
		_script_class,
		_packed_bytes,
		_quoted_name,
		_placeholder,
		_multiline,
		_property_order,
		_order_inner,
		_instanced_order,
		_placeholder_order,
		_animation_tracks,
		_skeleton_bones,
		_bone_chain_settings,
		_two_bone_ik_modifiers,
		_visual_shader_types,
		_visual_shader_nodes,
		_replication_properties,
		_tile_set_patterns,
		_animation_nodes,
		_animation_player_library,
		_skeleton_modification_chains,
		_skeleton_modification_singles,
		FamilyCases.physics_bones.bind(self),
		FamilyCases.tile_set_layers.bind(self),
		FamilyCases.gui_item_arrays.bind(self),
		FamilyCases.gui_tabs_graph.bind(self),
		FamilyCases.visual_shader_modes.bind(self),
		FamilyCases.visual_shader_depth_test.bind(self),
		FamilyCases.audio_resources.bind(self),
		FamilyCases.theme_font_resources.bind(self),
		FamilyCases.shader_parameters.bind(self),
		FamilyCases.tile_map_grid_map.bind(self),
	]
	for step in steps:
		var error: String = step.call()
		if not error.is_empty():
			return error
	return ""


func _lone_root() -> String:
	return _save_scene("lone_root", _root("Node"))


func _nested() -> String:
	var root := _root("Node")
	var a: Node2D = _add(root, "A", "Node2D")
	a.position = Vector2(4, 2)
	var b := _add(a, "B")
	_add(b, "C")
	_add(root, "D")
	return _save_scene("nested", root)


func _unique_name() -> String:
	var root := _root("Node")
	_add(root, "Child").unique_name_in_owner = true
	return _save_scene("unique_name", root)


func _groups() -> String:
	var root := _root("Node")
	var child := _add(root, "Child")
	child.add_to_group("zeta", true)
	child.add_to_group("alpha", true)
	return _save_scene("groups", root)


func _script_node() -> String:
	var root := _root("Node")
	var holder := _add(root, "Holder")
	var script: Script = load("res://holder.gd")
	_pin(script, "script_node.tscn", "1_h0ld3")
	holder.set_script(script)
	holder.set("target", _add(root, "Target"))
	return _save_scene("script_node", root)


func _material() -> String:
	var material := CanvasItemMaterial.new()
	material.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	return _save("material.tres", material)


func _gradient_texture() -> String:
	var texture := GradientTexture2D.new()
	texture.width = 16
	texture.gradient = _gradient("Gradient_t3x7r")
	return _save("gradient_texture.tres", texture)


func _sub_resources() -> String:
	var root := _root("Node2D")
	var texture := GradientTexture2D.new()
	texture.width = 8
	texture.gradient = _gradient("Gradient_gr4d1")
	texture.resource_scene_unique_id = "GradientTexture2D_0n3us"
	var one: Sprite2D = _add(root, "One", "Sprite2D")
	one.texture = texture
	var shared := CanvasItemMaterial.new()
	shared.light_mode = CanvasItemMaterial.LIGHT_MODE_UNSHADED
	shared.resource_scene_unique_id = "CanvasItemMaterial_sh4r3"
	for node_name in ["Two", "Three"]:
		var sprite: Sprite2D = _add(root, node_name, "Sprite2D")
		sprite.material = shared
	return _save_scene("sub_resources", root)


func _ext_resource() -> String:
	var root := _root("Node2D")
	var material: Material = load(CASES_DIR.path_join("material.tres"))
	_pin(material, "ext_resource.tscn", "1_m4t3r")
	var sprite: Sprite2D = _add(root, "Sprite", "Sprite2D")
	sprite.material = material
	return _save_scene("ext_resource", root)


func _instanced() -> String:
	var root := _root("Node")
	var nested := _scene("nested", "instanced.tscn", "1_n3st3")
	var instance := nested.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	instance.name = "Inst"
	root.add_child(instance)
	instance.owner = root
	instance.process_priority = 7
	instance.get_node("A").position = Vector2(9, 9)
	_add(instance.get_node("A"), "Added")
	root.set_editable_instance(instance, true)
	return _save_scene("instanced", root)


func _inherited() -> String:
	_scene("nested", "inherited.tscn", "1_b4s3s")
	var seed_path := CASES_DIR.path_join("inherited_seed.tscn")
	var file := FileAccess.open(seed_path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s (%s)" % [seed_path, error_string(FileAccess.get_open_error())]
	file.store_string(INHERITED_SEED)
	file.close()
	var inherited_seed: PackedScene = load(seed_path)
	var root := inherited_seed.instantiate(PackedScene.GEN_EDIT_STATE_MAIN)
	root.get_node("A").position = Vector2(1, 1)
	_add(root, "Extra")
	return _save_scene("inherited", root)


func _connections() -> String:
	var root := _root("Node")
	var emitter := _add(root, "Emitter")
	var receiver := _add(root, "Receiver")
	var warnings := receiver.update_configuration_warnings
	emitter.renamed.connect(warnings, CONNECT_PERSIST)
	emitter.tree_exited.connect(warnings, CONNECT_PERSIST | CONNECT_DEFERRED)
	emitter.ready.connect(warnings, CONNECT_PERSIST | CONNECT_ONE_SHOT)
	emitter.tree_entered.connect(receiver.set_process.bind(true), CONNECT_PERSIST)
	emitter.child_entered_tree.connect(warnings.unbind(1), CONNECT_PERSIST)
	return _save_scene("connections", root)


func _script_class() -> String:
	var script: Script = load("res://tagged.gd")
	_pin(script, "script_class.tres", "1_t4gg3")
	var resource: Resource = script.new()
	resource.set("label", "tagged")
	return _save("script_class.tres", resource)


func _packed_bytes() -> String:
	var resource := Resource.new()
	var bytes := PackedByteArray()
	bytes.resize(70)
	for i in bytes.size():
		bytes[i] = i
	resource.set_meta("bytes", bytes)
	resource.set_meta("small", PackedByteArray([1, 2, 3]))
	return _save("packed_bytes.tres", resource)


func _quoted_name() -> String:
	var script: Script = load("res://quoted.gd")
	_pin(script, "quoted_name.tres", "1_qu0t3")
	return _save("quoted_name.tres", script.new())


func _placeholder() -> String:
	var root := _root("Node")
	var lone: PackedScene = load(CASES_DIR.path_join("lone_root.tscn"))
	var later := lone.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	later.name = "Later"
	root.add_child(later)
	later.owner = root
	later.set_scene_instance_load_placeholder(true)
	return _save_scene("placeholder", root)


func _multiline() -> String:
	var root := _root("Node")
	root.editor_description = 'first line\nsecond "quoted" line'
	root.set_meta("table", {"b": 2, "a": [1, 2], "c": {"nested": true}})
	_add(root, "After").editor_description = "one line"
	return _save_scene("multiline", root)


func _property_order() -> String:
	var root := _root("Control")
	var box: Panel = _add(root, "Box", "Panel")
	box.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	box.grow_vertical = Control.GROW_DIRECTION_BEGIN
	var toggle: Button = _add(root, "Toggle", "Button")
	toggle.text = "Hold"
	toggle.toggle_mode = true
	toggle.button_pressed = true
	return _save_scene("property_order", root)


## The scene instanced_order instances: a Control root and a Panel Box in Anchors layout mode,
## so an override that sets Position is saved.
func _order_inner() -> String:
	var root := _root("Control")
	var box: Panel = _add(root, "Box", "Panel")
	box.set("layout_mode", 1)
	return _save_scene("order_inner", root)


## An instance of order_inner with layout_mode Position and grow_vertical Both set on its root
## and, through an override, on Box: neither section names a class, and Position set after
## grow_vertical resets it (control.cpp, Control::_set_layout_mode).
func _instanced_order() -> String:
	var root := _root("Control")
	var inner := _scene("order_inner", "instanced_order.tscn", "1_0rd3r")
	var instance: Control = inner.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	instance.name = "Inst"
	root.add_child(instance)
	instance.owner = root
	instance.set("layout_mode", 0)
	instance.grow_vertical = Control.GROW_DIRECTION_BOTH
	var box: Panel = instance.get_node("Box")
	box.set("layout_mode", 0)
	box.grow_vertical = Control.GROW_DIRECTION_BOTH
	root.set_editable_instance(instance, true)
	return _save_scene("instanced_order", root)


## A placeholder of order_inner with layout_mode Position and grow_vertical Both stored on it:
## the placeholder replays its stored values on the instanced Control in file order
## (instance_placeholder.cpp, InstancePlaceholder::create_instance), and Position set after
## grow_vertical resets it (control.cpp, Control::_set_layout_mode).
func _placeholder_order() -> String:
	var root := _root("Control")
	var inner: PackedScene = load(CASES_DIR.path_join("order_inner.tscn"))
	var later: Control = inner.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	later.name = "Later"
	root.add_child(later)
	later.owner = root
	later.set("layout_mode", 0)
	later.grow_vertical = Control.GROW_DIRECTION_BOTH
	later.set_scene_instance_load_placeholder(true)
	return _save_scene("placeholder_order", root)


## An Animation of 12 tracks of mixed types, one disabled, each with a path and two keys:
## Animation::_set creates a track only on tracks/<count>/type (animation.cpp), so each track's
## type must be set first and the tracks in numeric order, 10 and 11 after 2.
func _animation_tracks() -> String:
	var animation := Animation.new()
	animation.length = 2.0
	var kinds: Array[int] = [
		Animation.TYPE_VALUE,
		Animation.TYPE_METHOD,
		Animation.TYPE_BEZIER,
		Animation.TYPE_ROTATION_3D,
		Animation.TYPE_BLEND_SHAPE,
	]
	for index in 12:
		var kind: int = kinds[index % kinds.size()]
		var track := animation.add_track(kind)
		var target := "Target%d" % index
		var property := _track_property(kind)
		animation.track_set_path(
			track, NodePath(target if property.is_empty() else target + ":" + property)
		)
		for key in 2:
			_insert_key(animation, track, kind, key * 1.0, index + key)
	animation.track_set_enabled(7, false)
	animation.track_set_interpolation_type(0, Animation.INTERPOLATION_CUBIC)
	return _save("animation_tracks.tres", animation)


## A Skeleton3D of 12 bones with names, a parent chain and rests, bone 7 disabled and bone 3
## with bone_meta: Skeleton3D::_set adds a bone only on bones/<count>/name and rejects any
## other key of a bone not yet added (skeleton_3d.cpp).
func _skeleton_bones() -> String:
	var skeleton: Skeleton3D = _root("Skeleton3D")
	for index in 12:
		skeleton.add_bone("bone_%d" % index)
		skeleton.set_bone_parent(index, index - 1 if index != 6 else 0)
		skeleton.set_bone_rest(index, Transform3D(Basis(), Vector3(0, 0.5 * index, 0)))
	skeleton.set_bone_enabled(7, false)
	skeleton.set_bone_meta(3, "tag", "spine")
	return _save_scene("skeleton_bones", skeleton)


## A Skeleton3D of a 12-bone chain holding a SpringBoneSimulator3D (an individual-config setting
## with per-joint values, and a setting with a collision list), a FABRIK3D with per-joint axes, and
## a SplineIK3D, which stores its path and tilt keys, then its chain keys and joint_count (Godot
## stores no joint keys for it). With no skeleton, SpringBoneSimulator3D::set_root_bone and
## ChainIK3D::set_root_bone zero the setting's joints, and SpringBoneSimulator3D's collision
## setters return early while enable_all_child_collisions is true, so the bones and that flag
## must be set before the joints and collisions (spring_bone_simulator_3d.cpp, chain_ik_3d.cpp).
func _bone_chain_settings() -> String:
	var skeleton: Skeleton3D = _root("Skeleton3D")
	for index in 12:
		skeleton.add_bone("bone_%d" % index)
		skeleton.set_bone_parent(index, index - 1)
		skeleton.set_bone_rest(index, Transform3D(Basis(), Vector3(0, 0.25 * (index + 1), 0)))
	for index in 2:
		_add(skeleton, "Sphere%d" % index, "SpringBoneCollisionSphere3D")
		_add(skeleton, "Target%d" % index, "Node3D")
	_add(skeleton, "Path", "Path3D")

	var spring: Node = _add(skeleton, "Spring", "SpringBoneSimulator3D")
	spring.set("setting_count", 2)
	spring.set("settings/0/root_bone", 0)
	spring.set("settings/0/end_bone", 11)
	spring.set("settings/0/individual_config", true)
	spring.set("settings/0/joint_count", 12)
	for joint in 12:
		var prefix := "settings/0/joints/%d/" % joint
		spring.set(prefix + "radius", 0.0625 * (joint + 1))
		spring.set(prefix + "stiffness", 0.5 + 0.03125 * joint)
		spring.set(prefix + "drag", 0.25 + 0.015625 * joint)
		spring.set(prefix + "gravity", 0.125 * (joint + 1))
	spring.set("settings/1/root_bone", 1)
	spring.set("settings/1/end_bone", 3)
	spring.set("settings/1/enable_all_child_collisions", false)
	spring.set("settings/1/collision_count", 2)
	spring.set("settings/1/collisions/0", NodePath("../Sphere0"))
	spring.set("settings/1/collisions/1", NodePath("../Sphere1"))

	var fabrik: Node = _add(skeleton, "Fabrik", "FABRIK3D")
	fabrik.set("setting_count", 2)
	var chains: Array[Vector3i] = [Vector3i(0, 11, 12), Vector3i(6, 8, 3)]
	for setting in 2:
		fabrik.set("settings/%d/target_node" % setting, NodePath("../Target%d" % setting))
	for setting in 2:
		var prefix := "settings/%d/" % setting
		fabrik.set(prefix + "root_bone", chains[setting].x)
		fabrik.set(prefix + "end_bone", chains[setting].y)
		fabrik.set(prefix + "joint_count", chains[setting].z)
	for setting in 2:
		for joint in chains[setting].z:
			fabrik.set("settings/%d/joints/%d/rotation_axis" % [setting, joint], joint % 3)

	var spline: Node = _add(skeleton, "Spline", "SplineIK3D")
	spline.set("setting_count", 1)
	spline.set("settings/0/path_3d", NodePath("../Path"))
	spline.set("settings/0/tilt_enabled", true)
	spline.set("settings/0/root_bone", 0)
	spline.set("settings/0/end_bone", 5)
	spline.set("settings/0/joint_count", 6)
	return _save_scene("bone_chain_settings", skeleton)


## A Skeleton3D of a 12-bone chain holding a TwoBoneIK3D, a LimitAngularVelocityModifier3D, an
## AimModifier3D, a CopyTransformModifier3D, a ConvertTransformModifier3D and a
## BoneTwistDisperser3D, each with 12 settings (or chains), so entries 10 and 11 sort after 2. Each
## value is set in its class's _get_property_list order: the BoneConstraint3D subclasses list every
## setting's base keys (BoneConstraint3D::get_property_list) before every setting's own keys. The
## LimitAngularVelocityModifier3D joints are derived and never stored; the disperser's twist
## amounts are stored only in its custom disperse mode (bone_twist_disperser_3d.cpp).
func _two_bone_ik_modifiers() -> String:
	var skeleton: Skeleton3D = _root("Skeleton3D")
	for index in 12:
		skeleton.add_bone("bone_%d" % index)
		skeleton.set_bone_parent(index, index - 1)
		skeleton.set_bone_rest(index, Transform3D(Basis(), Vector3(0, 0.25 * (index + 1), 0)))
	for index in 12:
		_add(skeleton, "Target%d" % index, "Node3D")
		_add(skeleton, "Pole%d" % index, "Node3D")

	var two_bone: Node = _add(skeleton, "TwoBone", "TwoBoneIK3D")
	two_bone.set("setting_count", 12)
	for setting in 12:
		var prefix := "settings/%d/" % setting
		# 12 bones hold 10 chains of three; settings 10 and 11 reuse the bones of settings 8 and 9.
		var root_bone := setting if setting < 10 else setting - 2
		var virtual_end := setting % 2 == 0
		two_bone.set(prefix + "target_node", NodePath("../Target%d" % setting))
		two_bone.set(prefix + "pole_node", NodePath("../Pole%d" % setting))
		two_bone.set(prefix + "root_bone", root_bone)
		two_bone.set(prefix + "middle_bone", root_bone + 1)
		two_bone.set(prefix + "pole_direction", 1 + setting % 6)
		if not virtual_end:
			two_bone.set(prefix + "end_bone", root_bone + 2)
		two_bone.set(prefix + "use_virtual_end", virtual_end)
		two_bone.set(prefix + "extend_end_bone", true)
		two_bone.set(prefix + "end_bone/direction", setting % 6)
		two_bone.set(prefix + "end_bone/length", 0.0625 * (setting + 1))

	var limit: Node = _add(skeleton, "Limit", "LimitAngularVelocityModifier3D")
	limit.set("chain_count", 12)
	for chain in 12:
		limit.set("chains/%d/root_bone" % chain, chain % 6)
		@warning_ignore("integer_division")
		limit.set("chains/%d/end_bone" % chain, 11 - chain / 6)

	var aim: Node = _add(skeleton, "Aim", "AimModifier3D")
	var copy: Node = _add(skeleton, "Copy", "CopyTransformModifier3D")
	var convert: Node = _add(skeleton, "Convert", "ConvertTransformModifier3D")
	for constraint: Node in [aim, copy, convert]:
		constraint.set("setting_count", 12)
		for setting in 12:
			var prefix := "settings/%d/" % setting
			constraint.set(prefix + "amount", 0.0625 * (setting + 1))
			constraint.set(prefix + "apply_bone", setting)
			constraint.set(prefix + "reference_type", 0)
			constraint.set(prefix + "reference_bone", 11 - setting)
	# Forward axes +X, -X, -Y, +Z, -Z (+Y is the default), each with a primary rotation axis that is
	# neither X (the default) nor parallel to it (AimModifier3D::get_configuration_warnings).
	var forward_axes: Array[int] = [0, 1, 3, 4, 5]
	var primary_axes: Array[int] = [1, 2, 2, 1, 1]
	for setting in 12:
		var prefix := "settings/%d/" % setting
		aim.set(prefix + "forward_axis", forward_axes[setting % 5])
		aim.set(prefix + "use_euler", true)
		aim.set(prefix + "primary_rotation_axis", primary_axes[setting % 5])
		aim.set(prefix + "use_secondary_rotation", setting % 2 == 1)
		aim.set(prefix + "relative", setting % 4 >= 2)
	for setting in 12:
		var prefix := "settings/%d/" % setting
		copy.set(prefix + "copy", 1 + setting % 6)
		copy.set(prefix + "axes", 6 - setting % 6)
		copy.set(prefix + "invert", 1 + setting % 7)
		copy.set(prefix + "relative", setting % 2 == 1)
		copy.set(prefix + "additive", setting % 4 < 2)
	for setting in 12:
		var prefix := "settings/%d/" % setting
		convert.set(prefix + "apply/transform_mode", 1 + setting % 2)
		convert.set(prefix + "apply/axis", 1 + setting % 2)
		convert.set(prefix + "apply/range_min", -0.125 * (setting + 1))
		convert.set(prefix + "apply/range_max", 0.125 * (setting + 1))
		convert.set(prefix + "reference/transform_mode", 2 - setting % 2)
		convert.set(prefix + "reference/axis", 2 - setting % 2)
		convert.set(prefix + "reference/range_min", -0.25 * (setting + 1))
		convert.set(prefix + "reference/range_max", 0.25 * (setting + 1))
		convert.set(prefix + "relative", setting % 2 == 1)
		convert.set(prefix + "additive", setting % 4 < 2)

	# Odd settings extend the end bone with a direction; settings 2, 6 and 10 twist from a set
	# quaternion; settings 0, 3, 6 and 9 are custom (twist amounts per joint), 1, 4, 7 and 10 weighted
	# (a weight position), the rest even (the default).
	var twist_from: Array[Quaternion] = [
		Quaternion(0, 0.6, 0, 0.8),
		Quaternion(0.6, 0, 0, 0.8),
		Quaternion(0, 0, 0.6, 0.8),
	]
	var disperser: Node = _add(skeleton, "Disperser", "BoneTwistDisperser3D")
	disperser.set("setting_count", 12)
	for setting in 12:
		var prefix := "settings/%d/" % setting
		var root_bone := setting % 6
		@warning_ignore("integer_division")
		var end_bone := 11 - setting / 6
		var extended := setting % 2 == 1
		var joint_count := end_bone - root_bone + 1
		disperser.set(prefix + "root_bone", root_bone)
		disperser.set(prefix + "end_bone", end_bone)
		if extended:
			disperser.set(prefix + "extend_end_bone", true)
			disperser.set(prefix + "end_bone_direction", setting % 6)
		if setting % 4 == 2:
			disperser.set(prefix + "twist_from_rest", false)
			@warning_ignore("integer_division")
			disperser.set(prefix + "twist_from", twist_from[setting / 4])
		if setting % 3 == 0:
			disperser.set(prefix + "disperse_mode", 2)
		elif setting % 3 == 1:
			disperser.set(prefix + "disperse_mode", 1)
			disperser.set(prefix + "weight_position", 0.05 * (setting + 1))
		disperser.set(prefix + "joint_count", joint_count)
		if setting % 3 == 0:
			# Without an extended end bone, the last joint's amount is hidden and not stored.
			for joint in joint_count if extended else joint_count - 1:
				disperser.set(prefix + "joints/%d/twist_amount" % joint, 0.5 + 0.03125 * joint)
	return _save_scene("two_bone_ik_modifiers", skeleton)


## A VisualShader with an Expression node (ports and code) at id 10 in the fragment and vertex
## graphs, a GlobalExpression at 11, constants at 2 to 9 and 12, a connection into the Expression
## and the fragment output moved: VisualShader::_set (visual_shader.cpp) creates a node only on
## nodes/<type>/<id>/node and casts it unchecked for expression and input_ports, and its
## connections need the nodes and their ports.
func _visual_shader_nodes() -> String:
	var shader := VisualShader.new()
	var fragment := VisualShader.TYPE_FRAGMENT
	for id in [2, 3, 4, 5, 6, 7, 8, 9, 12]:
		var constant := VisualShaderNodeFloatConstant.new()
		constant.constant = id * 0.5
		shader.add_node(fragment, constant, Vector2(id * 20, 0), id)
	shader.add_node(fragment, _expression("result = vec3(input_a);"), Vector2(400, 40), 10)
	var global := VisualShaderNodeGlobalExpression.new()
	global.expression = "float twice(float x) { return 2.0 * x; }"
	shader.add_node(fragment, global, Vector2(400, 300), 11)
	shader.connect_nodes(fragment, 2, 0, 10, 0)
	shader.set_node_position(fragment, 0, Vector2(700, 60))
	var vertex := VisualShader.TYPE_VERTEX
	shader.add_node(vertex, _expression("result = vec3(input_a * 2.0);"), Vector2(-200, 80), 10)
	return _save("visual_shader_nodes.tres", shader)


## A spatial VisualShader with a constant at id 2 wired into a FloatOp at id 12 and the output
## node moved in each of the 11 graph types, render modes and flags, the stencil keys and 12
## varyings: VisualShader::_get_property_list (visual_shader.cpp) lists the modes, flags,
## stencil keys and varyings in its own order, then the graphs in type_string order. Godot
## keeps and saves every graph whatever the mode. preview_params/ is set too, though _set
## accepts it only in the editor.
func _visual_shader_types() -> String:
	var shader := VisualShader.new()
	for type in VisualShader.TYPE_MAX:
		var constant := VisualShaderNodeFloatConstant.new()
		constant.constant = 1.5 + type
		shader.add_node(type, constant, Vector2(20 + 10 * type, 40), 2)
		var operation := VisualShaderNodeFloatOp.new()
		operation.operator = VisualShaderNodeFloatOp.OP_MUL
		shader.add_node(type, operation, Vector2(220 + 10 * type, 60), 12)
		shader.connect_nodes(type, 2, 0, 12, 0)
		shader.set_node_position(type, 0, Vector2(600 + 10 * type, 80))
	shader.set("modes/blend", 1)
	shader.set("modes/depth_draw", 1)
	shader.set("modes/cull", 2)
	shader.set("modes/diffuse", 2)
	shader.set("modes/specular", 1)
	shader.set("flags/depth_prepass_alpha", true)
	shader.set("flags/unshaded", true)
	shader.set("flags/wireframe", true)
	shader.set("flags/shadows_disabled", true)
	shader.set("flags/fog_disabled", true)
	shader.set("stencil/enabled", true)
	shader.set("stencil/reference", 5)
	shader.set("stencil_flags/write", true)
	shader.set("stencil_modes/compare", 3)
	for index in 12:
		shader.add_varying(
			"v%d" % index,
			index % 2 as VisualShader.VaryingMode,
			index % 4 as VisualShader.VaryingType
		)
	shader.set("preview_params/tint", "Color(1, 0, 0, 1)")
	# A spatial shader stores no mode (spatial is the default), and only spatial lists stencil
	# keys: a second, canvas item shader carries mode.
	var canvas := VisualShader.new()
	canvas.set_mode(Shader.MODE_CANVAS_ITEM)
	var canvas_constant := VisualShaderNodeFloatConstant.new()
	canvas_constant.constant = 0.25
	canvas.add_node(VisualShader.TYPE_FRAGMENT, canvas_constant, Vector2(30, 70), 2)
	shader.set_meta("extra", canvas)
	return _save("visual_shader_types.tres", shader)


## An Expression node with one scalar input, one vec3 output and the code.
func _expression(code: String) -> VisualShaderNodeExpression:
	var expression := VisualShaderNodeExpression.new()
	expression.add_input_port(0, VisualShaderNode.PORT_TYPE_SCALAR, "input_a")
	expression.add_output_port(0, VisualShaderNode.PORT_TYPE_VECTOR_3D, "result")
	expression.expression = code
	return expression


## A SceneReplicationConfig of 12 properties, each with its own mix of spawn and replication
## mode: SceneReplicationConfig::_set (scene_replication_config.cpp) creates an entry only on
## properties/<count>/path, so the entries must come in numeric order, 10 and 11 after 2.
func _replication_properties() -> String:
	var config := SceneReplicationConfig.new()
	var modes: Array[int] = [
		SceneReplicationConfig.REPLICATION_MODE_NEVER,
		SceneReplicationConfig.REPLICATION_MODE_ALWAYS,
		SceneReplicationConfig.REPLICATION_MODE_ON_CHANGE,
	]
	for index in 12:
		var path := NodePath(".:p%d" % index)
		config.add_property(path)
		config.property_set_spawn(path, index % 4 < 2)
		config.property_set_replication_mode(path, modes[index % 3])
	return _save("replication_properties.tres", config)


## A TileSet of 12 patterns, pattern i with one cell at (i, 0): TileSet::_set (tile_set.cpp)
## appends at most one pattern per pattern_<N>, so the patterns must come in numeric order.
func _tile_set_patterns() -> String:
	var tile_set := TileSet.new()
	for index in 12:
		var pattern := TileMapPattern.new()
		pattern.set_cell(Vector2i(index, 0), 0, Vector2i(0, 0))
		tile_set.add_pattern(pattern)
	return _save("tile_set_patterns.tres", tile_set)


## An AnimationNodeBlendTree of 12 nodes added out of name order, a Transition of 12 inputs fed
## by six of them and wired to the output, a StateMachine of 12 states with transitions and a
## graph offset, and a BlendSpace1D and a BlendSpace2D of 12 named points each:
## AnimationNodeBlendTree::_get_property_list (animation_blend_tree.cpp) walks its nodes in the
## order they were added, AnimationNodeStateMachine::_get_property_list sorts its states by
## name, and the blend spaces and the transition list their entries in numeric order.
func _animation_nodes() -> String:
	var tree := AnimationNodeBlendTree.new()
	var transition := AnimationNodeTransition.new()
	transition.input_count = 12
	for index in 12:
		transition.set_input_name(index, "in_%d" % index)
		transition.set_input_as_auto_advance(index, index % 3 == 0)
		transition.set_input_break_loop_at_end(index, index % 4 == 1)
		transition.set_input_reset(index, index % 2 == 0)
	var machine := AnimationNodeStateMachine.new()
	for index in 12:
		machine.add_node(
			"s%d" % index,
			_animation_node("state_%d" % index),
			Vector2(10 + 40 * index, 5 + 15 * index)
		)
	for index in 6:
		machine.add_transition(
			"s%d" % index, "s%d" % (11 - index), AnimationNodeStateMachineTransition.new()
		)
	machine.add_transition("Start", "s0", AnimationNodeStateMachineTransition.new())
	machine.graph_offset = Vector2(15, -25)
	var space_1d := AnimationNodeBlendSpace1D.new()
	var space_2d := AnimationNodeBlendSpace2D.new()
	for index in 12:
		space_1d.add_blend_point(
			_animation_node("line_%d" % index), (index - 5.5) * 0.125, -1, StringName("p%d" % index)
		)
		space_2d.add_blend_point(
			_animation_node("plane_%d" % index),
			Vector2((index - 5.5) * 0.125, 0.5 - 0.0625 * index),
			-1,
			StringName("q%d" % index)
		)
	var nodes: Array = [
		["n10", transition],
		["n2", machine],
		["zeta", space_1d],
		["alpha", space_2d],
		["n1", null],
		["b3", null],
		["omega", null],
		["k7", null],
		["beta", null],
		["c0", null],
		["n11", null],
		["mid", null],
	]
	for index in nodes.size():
		var entry: Array = nodes[index]
		var node: AnimationNode = entry[1]
		if node == null:
			node = _animation_node("tree_%d" % index)
		tree.add_node(entry[0], node, Vector2(10 + 30 * index, 20 + 10 * index))
	tree.connect_node("output", 0, "n10")
	for index in 6:
		tree.connect_node("n10", index, nodes[index + 1][0])
	return _save("animation_nodes.tres", tree)


## An AnimationPlayer with the default library and one named extra, 12 animations each (a distinct
## length, a value track with two keys and three markers; a5 also has a position track and is
## compressed), the next animation of a0 to a11 and several blend times, beside an AnimationTree
## whose BlendSpace1D root holds 3 points at a set blend position.
## AnimationMixer::_get_property_list stores libraries/<name> (animation_mixer.cpp),
## AnimationPlayer::_get_property_list stores next/ and blend_times (animation_player.cpp),
## Animation::_get_property_list stores markers and, once compressed, _compression (animation.cpp).
## AnimationTree builds its parameters/ list lazily in _update_properties (animation_tree.cpp), so
## the list is read once before saving.
func _animation_player_library() -> String:
	var root: Node = _root("Node")
	var player: AnimationPlayer = _add(root, "Player", "AnimationPlayer")
	for library_name in ["", "extra"]:
		var library := AnimationLibrary.new()
		var prefix := "a" if library_name.is_empty() else "b"
		var offset := 0.0 if library_name.is_empty() else 2.0
		for index in 12:
			var animation := Animation.new()
			animation.length = 1.0 + offset + 0.125 * index
			var track := animation.add_track(Animation.TYPE_VALUE)
			animation.track_set_path(track, NodePath("Target:position:x"))
			for key in 2:
				animation.track_insert_key(track, key * 0.5, index + key + offset)
			for marker in 3:
				animation.add_marker("m%d" % marker, 0.25 * marker + 0.0625 * index)
			if library_name.is_empty() and index == 5:
				# Animation::compress stores only 3D transform and blend shape tracks.
				var position := animation.add_track(Animation.TYPE_POSITION_3D)
				animation.track_set_path(position, NodePath("Target"))
				animation.position_track_insert_key(position, 0.0, Vector3(1, 2, 3))
				animation.position_track_insert_key(position, 0.5, Vector3(4, 5, 6))
				animation.compress()
			library.add_animation("%s%d" % [prefix, index], animation)
		player.add_animation_library(library_name, library)
	for index in 12:
		player.animation_set_next("a%d" % index, "extra/b%d" % index)
	for index in 4:
		player.set_blend_time("a%d" % index, "extra/b%d" % (index + 1), 0.25 * (index + 1))

	var tree: AnimationTree = _add(root, "Tree", "AnimationTree")
	var space := AnimationNodeBlendSpace1D.new()
	for index in 3:
		space.add_blend_point(
			_animation_node("a%d" % index), index - 1.0, -1, StringName("p%d" % index)
		)
	tree.tree_root = space
	tree.anim_player = NodePath("../Player")
	tree.get_property_list()
	tree.set("parameters/blend_position", 0.375)
	return _save_scene("animation_player_library", root)


## A Skeleton2D of a 12-bone Bone2D chain whose modification stack holds 12 modifications: a CCDIK,
## a FABRIK, a Jiggle and a PhysicalBones each with 12 joints, then LookAt and TwoBoneIK ones. Each
## value is set in its class's _get_property_list order (skeleton_2d.cpp and
## scene/resources/2d/skeleton/). The stack is never set up outside the tree, so the bone index
## setters store the index unchecked. PhysicalBones fetch_bones and the editor/ rows are listed
## only under an editor hint and are never stored by a script run.
func _skeleton_modification_chains() -> String:
	var skeleton: Skeleton2D = _root("Skeleton2D")
	var parent: Node = skeleton
	for index in 12:
		var bone: Node = _add(parent, "bone_%d" % index, "Bone2D")
		bone.set("auto_calculate_length_and_angle", false)
		bone.set("length", 8.0 + index)
		bone.set("bone_angle", 5.0 * (index + 1))
		bone.set("editor_settings/show_bone_gizmo", false)
		parent = bone

	var ccdik := SkeletonModification2DCCDIK.new()
	ccdik.set("ccdik_data_chain_length", 12)
	for joint in 12:
		var prefix := "joint_data/%d/" % joint
		ccdik.set(prefix + "bone_index", joint)
		ccdik.set(prefix + "rotate_from_joint", joint % 2 == 1)
		if joint % 3 == 0:
			ccdik.set(prefix + "enable_constraint", true)
			ccdik.set(prefix + "constraint_angle_min", -5.0 * (joint + 1))
			ccdik.set(prefix + "constraint_angle_max", 10.0 * (joint + 1))
			ccdik.set(prefix + "constraint_angle_invert", joint % 2 == 1)
			ccdik.set(prefix + "constraint_in_localspace", joint % 2 == 0)

	var fabrik := SkeletonModification2DFABRIK.new()
	fabrik.set("fabrik_data_chain_length", 12)
	for joint in 12:
		var prefix := "joint_data/%d/" % joint
		fabrik.set(prefix + "bone_index", joint)
		# The first joint has no magnet position (SkeletonModification2DFABRIK::_get_property_list).
		if joint > 0:
			fabrik.set(prefix + "magnet_position", Vector2(joint, -0.5 * joint))

	var jiggle := SkeletonModification2DJiggle.new()
	jiggle.set("jiggle_data_chain_length", 12)
	jiggle.set("use_colliders", true)
	jiggle.set("collision_mask", 6)
	for joint in 12:
		var prefix := "joint_data/%d/" % joint
		jiggle.set(prefix + "bone_index", joint)
		# A joint's stiffness is listed only once it overrides the defaults.
		jiggle.set(prefix + "override_defaults", true)
		jiggle.set(prefix + "stiffness", 0.5 * (joint + 1))

	var physical := SkeletonModification2DPhysicalBones.new()
	physical.set("physical_bone_chain_length", 12)
	for joint in 12:
		physical.set("joint_%d_nodepath" % joint, NodePath("Physical%d" % joint))

	var stack := SkeletonModificationStack2D.new()
	for modification: SkeletonModification2D in [ccdik, fabrik, jiggle, physical]:
		stack.add_modification(modification)
	for index in 4:
		var look_at := SkeletonModification2DLookAt.new()
		look_at.set("enable_constraint", true)
		look_at.set("constraint_angle_min", -15.0 * (index + 1))
		stack.add_modification(look_at)
		var two_bone := SkeletonModification2DTwoBoneIK.new()
		two_bone.set("joint_one_bone_idx", index)
		two_bone.set("joint_two_bone_idx", index + 1)
		stack.add_modification(two_bone)
	skeleton.set("modification_stack", stack)
	return _save_scene("skeleton_modification_chains", skeleton)


## A SkeletonModification2DLookAt with every stored key off its default, set in
## SkeletonModification2DLookAt::_get_property_list order (the constraint keys are listed only
## once enable_constraint is on), holding under metadata/extra a SkeletonModification2DStackHolder
## whose held stack has one modification and a SkeletonModification2DTwoBoneIK with its four joint
## keys (scene/resources/2d/skeleton/). The editor/ rows are listed only under an editor hint and
## are never stored by a script run.
func _skeleton_modification_singles() -> String:
	var look_at := SkeletonModification2DLookAt.new()
	look_at.set("bone_index", 2)
	look_at.set("bone2d_node", NodePath("Skeleton2D/bone_2"))
	look_at.set("target_nodepath", NodePath("Target"))
	look_at.set("enable_constraint", true)
	look_at.set("constraint_angle_min", -30.0)
	look_at.set("constraint_angle_max", 45.0)
	look_at.set("constraint_angle_invert", true)
	look_at.set("constraint_in_localspace", false)
	look_at.set("additional_rotation", 90.0)

	var held := SkeletonModificationStack2D.new()
	var held_look_at := SkeletonModification2DLookAt.new()
	held_look_at.set("bone_index", 1)
	held.add_modification(held_look_at)
	var holder := SkeletonModification2DStackHolder.new()
	holder.set("held_modification_stack", held)

	var two_bone := SkeletonModification2DTwoBoneIK.new()
	two_bone.set("joint_one_bone_idx", 3)
	two_bone.set("joint_one_bone2d_node", NodePath("Skeleton2D/bone_3"))
	two_bone.set("joint_two_bone_idx", 4)
	two_bone.set("joint_two_bone2d_node", NodePath("Skeleton2D/bone_3/bone_4"))

	look_at.set_meta("extra", [holder, two_bone])
	return _save("skeleton_modification_singles.tres", look_at)


## An AnimationNodeAnimation that plays the named animation.
func _animation_node(animation: String) -> AnimationNodeAnimation:
	var node := AnimationNodeAnimation.new()
	node.animation = animation
	return node


## The property a track of this kind animates on its target.
func _track_property(kind: int) -> String:
	match kind:
		Animation.TYPE_VALUE:
			return "position:x"
		Animation.TYPE_BEZIER:
			return "position:y"
		Animation.TYPE_ROTATION_3D:
			return "bone_1"
		Animation.TYPE_BLEND_SHAPE:
			return "smile"
	return ""


## Inserts a key of the track's kind at the time, its value drawn from the number.
func _insert_key(animation: Animation, track: int, kind: int, time: float, number: int) -> void:
	match kind:
		Animation.TYPE_VALUE:
			animation.track_insert_key(track, time, number * 0.5)
		Animation.TYPE_METHOD:
			animation.track_insert_key(
				track, time, {"method": "set_meta", "args": ["step", number]}
			)
		Animation.TYPE_BEZIER:
			animation.bezier_track_insert_key(track, time, number * 0.25)
		Animation.TYPE_ROTATION_3D:
			animation.rotation_track_insert_key(track, time, Quaternion(Vector3.UP, number * 0.1))
		Animation.TYPE_BLEND_SHAPE:
			animation.blend_shape_track_insert_key(track, time, number * 0.1)


## A gradient with non-default colours, its sub-resource id pinned.
func _gradient(id: String) -> Gradient:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color.RED, Color.BLUE])
	gradient.resource_scene_unique_id = id
	return gradient


## A case's saved scene, its ext-resource id pinned for the file that uses it.
func _scene(case_name: String, user_file: String, id: String) -> PackedScene:
	var scene: PackedScene = load(CASES_DIR.path_join(case_name + ".tscn"))
	_pin(scene, user_file, id)
	return scene


func _pin(resource: Resource, user_file: String, id: String) -> void:
	resource.set_id_for_path(CASES_DIR.path_join(user_file), id)


func _root(type_name: String) -> Node:
	var root: Node = ClassDB.instantiate(type_name)
	root.name = "Root"
	return root


## Adds a child owned by the scene root above the parent.
func _add(parent: Node, node_name: String, type_name := "Node") -> Node:
	var node: Node = ClassDB.instantiate(type_name)
	node.name = node_name
	parent.add_child(node)
	var scene_root := parent if parent.owner == null else parent.owner
	while scene_root.owner != null:
		scene_root = scene_root.owner
	node.owner = scene_root
	return node


func _save_scene(case_name: String, root: Node) -> String:
	var packed := PackedScene.new()
	var packed_error := packed.pack(root)
	root.free()
	if packed_error != OK:
		return "cannot pack %s (%s)" % [case_name, error_string(packed_error)]
	return _save(case_name + ".tscn", packed)


func _save(file_name: String, resource: Resource) -> String:
	var path := CASES_DIR.path_join(file_name)
	var saved := ResourceSaver.save(resource, path)
	if saved != OK:
		return "cannot save %s (%s)" % [path, error_string(saved)]
	var bytes := FileAccess.get_file_as_bytes(path)
	if bytes.is_empty():
		return "cannot read %s (%s)" % [path, error_string(FileAccess.get_open_error())]
	var target := _out_dir.path_join(file_name)
	var file := FileAccess.open(target, FileAccess.WRITE)
	if file == null:
		return "cannot write %s (%s)" % [target, error_string(FileAccess.get_open_error())]
	if not file.store_buffer(bytes):
		return "cannot write %s (%s)" % [target, error_string(file.get_error())]
	file.close()
	_written += 1
	return ""


func _clear(folder: String) -> String:
	var made := DirAccess.make_dir_recursive_absolute(folder)
	if made != OK:
		return "cannot create %s (%s)" % [folder, error_string(made)]
	for file in DirAccess.get_files_at(folder):
		if not (file.ends_with(".tscn") or file.ends_with(".tres")):
			continue
		var removed := DirAccess.remove_absolute(folder.path_join(file))
		if removed != OK:
			return "cannot remove %s (%s)" % [file, error_string(removed)]
	return ""


func _arg_value(arg_name: String) -> String:
	var args := OS.get_cmdline_user_args()
	var index := args.find(arg_name)
	if index == -1 or index + 1 >= args.size():
		return ""
	return args[index + 1]
