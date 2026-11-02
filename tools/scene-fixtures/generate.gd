extends SceneTree
## Writes the scene fixtures: .tscn and .tres files saved by Godot's own text saver.
##
## Run: godot --headless --path tools/scene-fixtures --script res://generate.gd --
##     --out-dir <absolute tests/fixtures/scenes folder>
## Each case builds a scene or resource in code, saves it with ResourceSaver.save under
## res://cases/ (cases reference each other there) and copies the saved bytes unchanged to
## <out-dir>/<case>.tscn or .tres. Ext and sub-resource ids are pinned; node unique_id values
## are random on every run (Node.set_unique_scene_id is not exposed to scripts).

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
