extends SceneTree
## Loads a pack exported from godot/ and checks what it holds, with no godot-xaml addon.
## Arguments after --: <pck> <mode> [<uid of scene.xscn>], mode one of main, broken, dependencies.
## Prints PROBE <id> PASS|FAIL <detail> per check, then DONE.

const CASES := "res://checks/cases/"
const SCENE := CASES + "scene.xscn"
const THEME := CASES + "theme.xres"
const PARENT := CASES + "parent.xscn"
const CHILD := CASES + "child.xscn"
const BROKEN := "res://checks/broken.xscn"
const NOT_XML := CASES + "notxml.xres"
const SCENE_SHAPE := "Root: Label, Panel, Button"

var _failed := false


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		_report("args", false, "expected <pck> <mode> after --, got %s" % [args])
	elif not ProjectSettings.load_resource_pack(args[0]):
		_report("pack", false, "cannot load %s" % args[0])
	elif args[1] == "main":
		_probe_main(args[2] if args.size() > 2 else "")
	elif args[1] == "broken":
		_probe_broken()
	elif args[1] == "dependencies":
		_probe_dependencies()
	else:
		_report("mode", false, "unknown mode %s" % args[1])
	_finish()


func _probe_main(scene_uid: String) -> void:
	_report("P1", FileAccess.file_exists(SCENE + ".remap"), "no %s.remap" % SCENE)
	_check_remap_target("P2", SCENE)
	_report("P3", not FileAccess.file_exists(SCENE), "%s was shipped" % SCENE)
	var shape := _scene_shape(SCENE)
	_report("P4", shape == SCENE_SHAPE, "got %s" % shape)
	_check_theme()
	var uid_shape := _scene_shape(scene_uid) if scene_uid != "" else "no uid given"
	_report("P6", uid_shape == SCENE_SHAPE, "%s gave %s" % [scene_uid, uid_shape])
	_check_parent()
	_check_remap_target("P8", THEME)


func _probe_broken() -> void:
	for entry in [["B1", BROKEN], ["B2", NOT_XML]]:
		var path: String = entry[1]
		var shipped := FileAccess.file_exists(path) or FileAccess.file_exists(path + ".remap")
		_report(entry[0], not shipped, "%s or its .remap is in the pack" % path)


func _probe_dependencies() -> void:
	_check_remap_target("D1", PARENT)
	_check_remap_target("D2", CHILD)


## Checks that the file's .remap is in the pack and names a file that is in the pack too.
func _check_remap_target(id: String, path: String) -> void:
	var remap := ConfigFile.new()
	if remap.load(path + ".remap") != OK:
		_report(id, false, "no %s.remap" % path)
		return
	var target := String(remap.get_value("remap", "path", ""))
	_report(
		id, target != "" and FileAccess.file_exists(target), "%s.remap names %s" % [path, target]
	)


## The root's name and its children's names, as "Root: A, B", or why the scene did not load.
func _scene_shape(path: String) -> String:
	var packed: Variant = ResourceLoader.load(path)
	if not packed is PackedScene:
		return "%s did not load as a PackedScene" % path
	var root: Node = packed.instantiate()
	var names: Array[String] = []
	for child in root.get_children():
		names.append(String(child.name))
	var shape := "%s: %s" % [root.name, ", ".join(names)]
	root.free()
	return shape


func _check_theme() -> void:
	var theme: Variant = ResourceLoader.load(THEME)
	if not theme is Theme:
		_report("P5", false, "%s did not load as a Theme" % THEME)
		return
	_report("P5", theme.default_font != null, "%s has no default_font" % THEME)


func _check_parent() -> void:
	var packed: Variant = ResourceLoader.load(PARENT)
	if not packed is PackedScene:
		_report("P7", false, "%s did not load as a PackedScene" % PARENT)
		return
	var root: Node = packed.instantiate()
	var has_inner := root.has_node("Inst/Inner")
	var found := root.name == &"Parent" and has_inner
	var tree := "%s with Inst/Inner %s" % [root.name, "present" if has_inner else "missing"]
	root.free()
	_report("P7", found, "got %s" % tree)


func _report(id: String, passed: bool, detail: String) -> void:
	if passed:
		print("PROBE %s PASS" % id)
	else:
		_failed = true
		print("PROBE %s FAIL %s" % [id, detail])


func _finish() -> void:
	print("DONE")
	quit(1 if _failed else 0)
