extends RefCounted
## The Godot 4.8 cases, which generate.gd runs with --four-eight.
##
## Each case is a static function that takes the generator and builds its scene or resource
## through the generator's helpers (_root, _add, _scene, _pin, _save_scene, _save), returning an
## error or "".


## "" when the running engine suits the mode, else the message generate.gd reports.
static func version_error(four_eight: bool) -> String:
	var version := Engine.get_version_info()
	var major: int = version.major
	var minor: int = version.minor
	if four_eight:
		if major == 4 and minor >= 8:
			return ""
		return "the 4.8 cases need Godot 4.8 or newer, running %s" % version.string
	if major == 4 and minor == 7:
		return ""
	return (
		"the 4.7 cases need Godot 4.7, running %s; pass --four-eight for Godot 4.8" % version.string
	)


## Clears res://cases and runs the 4.8 cases in order; the out-dir is left alone, since each case
## overwrites its own file and the folder holds files these cases do not write. "" when every case
## succeeded.
static func run(generator) -> String:
	var error: String = generator._clear(ProjectSettings.globalize_path("res://cases"))
	if not error.is_empty():
		return error
	for step in steps(generator):
		error = step.call()
		if not error.is_empty():
			return error
	return ""


## The 4.8 cases, in the order they run.
static func steps(generator) -> Array[Callable]:
	return [
		scene_meta.bind(generator),
		embedded_scene.bind(generator),
		embedded_only.bind(generator),
		embedded_empty.bind(generator),
		scene_holder.bind(generator),
		embedded_target.bind(generator),
		embedded_instance.bind(generator),
	]


## The scene's own properties in a [resource] section before its first node: resource_name and a
## metadata value (resource_format_text.cpp:2114-2117).
static func scene_meta(generator) -> String:
	var root: Node = generator._root("Node3D")
	generator._add(root, "Child", "Node3D")
	var packed := PackedScene.new()
	var error := _pack("scene_meta", root, packed)
	if not error.is_empty():
		return error
	packed.resource_name = "Hello"
	packed.set_meta("answer", 42)
	return generator._save("scene_meta.tscn", packed)


## An embedded scene and a plain sub-resource, both referred to from the scene's [resource]
## (resource_format_text.cpp:674-681, 2122-2125).
static func embedded_scene(generator) -> String:
	var inner_root := Node2D.new()
	inner_root.name = "InnerRoot"
	var inner_child: Node2D = generator._add(inner_root, "InnerChild", "Node2D")
	inner_child.visibility_changed.connect(inner_root.show, CONNECT_PERSIST)
	var inner := PackedScene.new()
	var error := _pack("embedded_scene", inner_root, inner)
	if not error.is_empty():
		return error
	inner.resource_scene_unique_id = "PackedScene_x10bp"
	var other := Resource.new()
	other.resource_name = "Other"
	other.resource_scene_unique_id = "Resource_r1srk"
	var root: Node = generator._root("Node")
	generator._add(root, "Child", "Node")
	var packed := PackedScene.new()
	error = _pack("embedded_scene", root, packed)
	if not error.is_empty():
		return error
	packed.set_meta("inner", inner)
	packed.set_meta("other", other)
	return generator._save("embedded_scene.tscn", packed)


## An embedded scene no node uses: it is only a meta value on the main scene's root, which gets a
## bare [resource] section of its own (resource_format_text.cpp:2127-2130).
static func embedded_only(generator) -> String:
	var inner_root := Node2D.new()
	inner_root.name = "InnerRoot"
	var inner_child: Node2D = generator._add(inner_root, "InnerChild", "Node2D")
	inner_child.visibility_changed.connect(inner_root.show, CONNECT_PERSIST)
	var inner := PackedScene.new()
	var error := _pack("embedded_only", inner_root, inner)
	if not error.is_empty():
		return error
	inner.resource_scene_unique_id = "PackedScene_b87l8"
	var root: Node = generator._root("Node")
	root.set_meta("inner", inner)
	generator._add(root, "Child", "Node")
	return generator._save_scene("embedded_only", root)


## An unpacked PackedScene as a meta value: it holds no nodes, but the scene still gets its bare
## [resource] section (resource_format_text.cpp:2122-2130).
static func embedded_empty(generator) -> String:
	var empty := PackedScene.new()
	empty.resource_scene_unique_id = "PackedScene_ymsae"
	var root: Node = generator._root("Node")
	root.set_meta("empty", empty)
	return generator._save_scene("embedded_empty", root)


## A .tres holding an embedded scene: its PackedScene sub-resource carries its own [node] sections
## (resource_format_text.cpp:674-681, 402-404).
static func scene_holder(generator) -> String:
	var held_root := Node.new()
	held_root.name = "HeldRoot"
	generator._add(held_root, "HeldChild", "Node")
	var inner := PackedScene.new()
	var error := _pack("scene_holder", held_root, inner)
	if not error.is_empty():
		return error
	inner.resource_scene_unique_id = "PackedScene_gqxle"
	var holder := Resource.new()
	holder.set_meta("scene", inner)
	return generator._save("scene_holder.tres", holder)


## The scene embedded_instance instances: a Node2D root with a Part child.
static func embedded_target(generator) -> String:
	var root: Node = generator._root("Node2D")
	generator._add(root, "Part", "Node2D")
	return generator._save_scene("embedded_target", root)


## An embedded scene holding an editable instance of embedded_target, a property override on the
## instance's Part, a sub-resource material and a connection, beside a sub-resource of the main
## scene: the ext-resource and sub-resource ids the file carries, and where each section lands,
## are what the reader's id order has to follow.
static func embedded_instance(generator) -> String:
	var target: PackedScene = generator._scene(
		"embedded_target", "embedded_instance.tscn", "1_target"
	)
	var inner_root := Node2D.new()
	inner_root.name = "InnerRoot"
	var held: Node2D = target.instantiate()
	held.name = "Held"
	inner_root.add_child(held)
	held.owner = inner_root
	inner_root.set_editable_instance(held, true)
	held.get_node("Part").position = Vector2(3, 1)
	var tinted: Node2D = generator._add(inner_root, "Tinted", "Node2D")
	var material := CanvasItemMaterial.new()
	material.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	material.resource_scene_unique_id = "CanvasItemMaterial_embed"
	tinted.material = material
	held.visibility_changed.connect(inner_root.show, CONNECT_PERSIST)
	var inner := PackedScene.new()
	var error := _pack("embedded_instance", inner_root, inner)
	if not error.is_empty():
		return error
	inner.resource_scene_unique_id = "PackedScene_embed"
	var root: Node = generator._root("Node")
	var main := Resource.new()
	main.resource_name = "Main"
	main.resource_scene_unique_id = "Resource_main"
	root.set_meta("inner", inner)
	root.set_meta("main", main)
	generator._add(root, "Child", "Node")
	return generator._save_scene("embedded_instance", root)


## Packs root into packed and frees root; "" when packed, else the case's error.
static func _pack(case_name: String, root: Node, packed: PackedScene) -> String:
	var error := packed.pack(root)
	root.free()
	if error != OK:
		return "cannot pack %s (%s)" % [case_name, error_string(error)]
	return ""
