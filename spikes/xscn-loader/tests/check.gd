extends Node

const MAIN_PATH := "res://scenes/Main.xscn"
const HOST_PATH := "res://scenes/Host.tscn"
const ROUNDTRIP_PATH := "user://roundtrip.xscn"
const SAME_PATH := "user://same.xscn"
const TITLE_TEXT := "Hello from xscn"

var _all_passed := true


func _ready() -> void:
	_report("C1", _check_load())
	_report("C2", _check_instanced())
	_report("C3", _check_roundtrip())
	_report("C4", _check_same_text())
	get_tree().quit(0 if _all_passed else 1)


func _report(id: String, failure: String) -> void:
	if failure.is_empty():
		print("CHECK %s PASS" % id)
	else:
		_all_passed = false
		print("CHECK %s FAIL %s" % [id, failure])


func _check_load() -> String:
	var scene = load(MAIN_PATH)
	if not scene is PackedScene:
		return "load(%s) returned %s, not a PackedScene" % [MAIN_PATH, scene]
	var root: Node = scene.instantiate()
	var failure := _check_main_node(root, "Main")
	root.free()
	return failure


func _check_main_node(root: Node, expected_name: String) -> String:
	if not root is Control:
		return "the root is a %s, not a Control" % root.get_class()
	if root.name != expected_name:
		return "the root is named %s, not %s" % [root.name, expected_name]
	var title := root.get_node_or_null("Title")
	if not title is Label:
		return "there is no Label child named Title"
	if title.text != TITLE_TEXT:
		return "Title.text is '%s', not '%s'" % [title.text, TITLE_TEXT]
	return ""


func _check_instanced() -> String:
	var host_scene = load(HOST_PATH)
	if not host_scene is PackedScene:
		return "load(%s) returned %s, not a PackedScene" % [HOST_PATH, host_scene]
	var host: Node = host_scene.instantiate()
	var failure := ""
	var main := host.get_node_or_null("Main")
	if main == null:
		failure = "Host has no child named Main"
	elif main.scene_file_path != MAIN_PATH:
		failure = "Main.scene_file_path is '%s', not '%s'" % [main.scene_file_path, MAIN_PATH]
	else:
		failure = _check_main_node(main, "Main")
	host.free()
	return failure


func _check_roundtrip() -> String:
	var root: Node = load(MAIN_PATH).instantiate()
	root.get_node("Title").text = "Edited"
	var packed := PackedScene.new()
	var pack_error := packed.pack(root)
	root.free()
	if pack_error != OK:
		return "PackedScene.pack returned %s" % error_string(pack_error)
	var save_error := ResourceSaver.save(packed, ROUNDTRIP_PATH)
	if save_error != OK:
		return "ResourceSaver.save(%s) returned %s" % [ROUNDTRIP_PATH, error_string(save_error)]
	var text := FileAccess.get_file_as_string(ROUNDTRIP_PATH)
	if not text.contains("Edited"):
		return "the saved text does not contain 'Edited':\n%s" % text
	var reloaded = ResourceLoader.load(ROUNDTRIP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	if not reloaded is PackedScene:
		return "reloading %s returned %s, not a PackedScene" % [ROUNDTRIP_PATH, reloaded]
	var instance: Node = reloaded.instantiate()
	var title := instance.get_node_or_null("Title")
	var failure := ""
	if title == null:
		failure = "the reloaded scene has no Title"
	elif title.text != "Edited":
		failure = "the reloaded Title.text is '%s', not 'Edited'" % title.text
	instance.free()
	return failure


func _check_same_text() -> String:
	var save_error := ResourceSaver.save(load(MAIN_PATH), SAME_PATH)
	if save_error != OK:
		return "ResourceSaver.save(%s) returned %s" % [SAME_PATH, error_string(save_error)]
	var saved := FileAccess.get_file_as_string(SAME_PATH)
	var original := FileAccess.get_file_as_string(MAIN_PATH)
	if saved == original:
		return ""
	return "the saved text differs from %s\n--- saved\n%s--- original\n%s" % [MAIN_PATH, saved, original]
