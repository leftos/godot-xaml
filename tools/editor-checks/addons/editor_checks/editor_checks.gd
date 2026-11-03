@tool
extends EditorPlugin

const MODE_ARG := "--xscn-editor-checks="
const OUT_ARG := "--out="
const WAIT_LIMIT_MS := 60000
const MAIN_PATH := "res://fixtures/Main.xscn"
const HOST_PATH := "res://fixtures/Host.tscn"
const RENAME_PATH := "res://fixtures/Rename.xscn"
const RENAMED_PATH := "res://fixtures/Renamed.xscn"
const RENAME_HOST_PATH := "res://fixtures/RenameHost.xscn"
const RENAME_TAG := "<fixtures:Rename "
const RENAMED_TAG := "<fixtures:Renamed "
const SUB_PATH := "res://fixtures/Sub.xscn"
const SAVED_AS_PATH := "res://fixtures/SavedAs.xscn"
const E11_PATH := "res://fixtures/E11.xscn"
const E11_RENAMED_PATH := "res://fixtures/E11Renamed.xscn"
const SAVE_AS_ITEM := "Save Scene As..."
const PROBE_PATH := "res://fixtures/ReloadProbe.cs"
const ASSEMBLY_PATH := "res://.godot/mono/temp/bin/Debug/GodotXaml.Addon.dll"
const READY_PATH := "user://main_ready.txt"
const REBUILD_MARKER := "// editor-checks: rebuild marker\n"
const EDITED_TEXT := "Edited in editor"
const RELOADED_TEXT := "After reload"
const E12_SCRIPT_PATH := "res://fixtures/E12Named.gd"
const E12_XRES_PATH := "res://fixtures/E12Named.xres"
const E12_CLASS := "E12NamedResource"
const E12_SCRIPT_TEXT := (
	"class_name E12NamedResource\nextends Resource\n\n" + "@export var count: int = 0\n"
)
const E12_XRES_TEXT := (
	'<Resource xmlns:x="https://github.com/leftos/godot-xaml/format/1"'
	+ ' Script="res://fixtures/E12Named.gd" count="3" />\n'
)

var _mode := ""
var _out := ""
var _passed := 0
var _total := 0
var _e12_emitted := false
var _e12_still_found := false


func _enter_tree() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with(MODE_ARG):
			_mode = arg.trim_prefix(MODE_ARG)
		elif arg.begins_with(OUT_ARG):
			_out = arg.trim_prefix(OUT_ARG)
	if _mode.is_empty():
		return
	_run()


func _run() -> void:
	if _out.is_empty():
		push_error("editor_checks: the user argument --out=<absolute folder> is missing")
		get_tree().quit(1)
		return
	var scanned := await _wait_for(_first_scan_done, "the first filesystem scan")
	if not scanned.is_empty():
		_emit("NOTE START %s" % scanned)
	match _mode:
		"run":
			await _run_all()
		"reopen":
			await _report("E8", await _check_reopen())
		_:
			await _report("MODE", "unknown mode '%s'; expected run or reopen" % _mode)
	_emit("DONE %d/%d" % [_passed, _total])
	get_tree().quit(0 if _total > 0 and _passed == _total else 1)


func _run_all() -> void:
	await _report("E1", _check_dialogs())
	await _report("E2", await _check_open("E2"))
	await _report("E3", await _check_edit_save())
	await _report("E4", await _check_run_scene())
	await _report("E5", await _check_instance_host())
	await _report("E6", await _check_rebuild_reload())
	await _report("E7", await _check_rename())
	await _report("E9", await _check_paste())
	await _report("E10", await _check_save_as())
	await _report("E11", await _check_rename_open())
	await _report("E12", await _check_class_removed())


func _check_dialogs() -> String:
	var open_list := ResourceLoader.get_recognized_extensions_for_type("PackedScene")
	if not open_list.has("xscn"):
		return "the Open dialog list %s has no xscn" % [open_list]
	var save_list := ResourceSaver.get_recognized_extensions(PackedScene.new())
	if not save_list.has("xscn"):
		return "the Save dialog list %s has no xscn" % [save_list]
	return ""


func _check_open(id: String) -> String:
	EditorInterface.open_scene_from_path(MAIN_PATH)
	var what := "%s to open with a root Main and a child Title" % MAIN_PATH
	var failure := await _wait_for(_main_is_open, what)
	if failure.is_empty():
		return ""
	return "%s (%s; the edited root is %s)" % [failure, id, _describe_edited()]


func _check_reopen() -> String:
	var failure := await _check_open("E8")
	if not failure.is_empty():
		return failure
	var title := EditorInterface.get_edited_scene_root().get_node("Title") as Label
	if title == null:
		return "the reopened scene's Title is not a Label"
	if title.text != RELOADED_TEXT:
		return "the reopened Title holds '%s', not '%s'" % [title.text, RELOADED_TEXT]
	return ""


func _check_edit_save() -> String:
	var failure := await _set_title_and_save(EDITED_TEXT)
	if not failure.is_empty():
		return failure
	return _expect_saved(EDITED_TEXT, "E3-Main.xscn")


func _check_run_scene() -> String:
	if FileAccess.file_exists(READY_PATH):
		var removed := DirAccess.remove_absolute(ProjectSettings.globalize_path(READY_PATH))
		if removed != OK:
			return "cannot delete %s: %s" % [READY_PATH, error_string(removed)]
	EditorInterface.play_current_scene()
	var failure := await _wait_for(_ready_file_written, "the run to write %s" % READY_PATH)
	if failure.is_empty():
		var text := FileAccess.get_file_as_string(READY_PATH)
		if text != EDITED_TEXT:
			failure = "%s holds '%s', not '%s'" % [READY_PATH, text, EDITED_TEXT]
	return _join(failure, await _stop_run())


func _check_instance_host() -> String:
	EditorInterface.open_scene_from_path(HOST_PATH)
	var failure := await _wait_for(_edited_is.bind(HOST_PATH), "%s to open" % HOST_PATH)
	if not failure.is_empty():
		return "%s (the edited root is %s)" % [failure, _describe_edited()]
	var main_scene = load(MAIN_PATH)
	if not main_scene is PackedScene:
		return "load(%s) returned %s, not a PackedScene" % [MAIN_PATH, main_scene]
	var root := EditorInterface.get_edited_scene_root()
	var instance: Node = main_scene.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	instance.name = "Main2"
	root.add_child(instance)
	instance.owner = root
	var save_failure := _mark_and_save()
	if not save_failure.is_empty():
		return save_failure
	var host_saved := _file_contains.bind(HOST_PATH, "Main2")
	failure = await _wait_for(host_saved, "%s to name Main2" % HOST_PATH)
	if not failure.is_empty():
		return failure
	var text := FileAccess.get_file_as_string(HOST_PATH)
	_write_out("E5-Host.tscn", text)
	return _host_text_failure(text)


func _host_text_failure(text: String) -> String:
	var main_ext_count := 0
	var instance_count := 0
	for line in text.split("\n"):
		if line.begins_with("[ext_resource") and line.contains('path="%s"' % MAIN_PATH):
			main_ext_count += 1
		if line.begins_with("[node") and line.contains("instance=ExtResource("):
			instance_count += 1
	if main_ext_count != 1:
		return "Host.tscn has %d ext_resource lines for %s, not 1" % [main_ext_count, MAIN_PATH]
	if instance_count != 2:
		return "Host.tscn has %d instanced [node lines, not 2" % instance_count
	return ""


func _check_rebuild_reload() -> String:
	EditorInterface.open_scene_from_path(MAIN_PATH)
	var what := "%s to be the edited scene" % MAIN_PATH
	var failure := await _wait_for(_edited_is.bind(MAIN_PATH), what)
	if failure.is_empty():
		failure = await _rebuild_on_run()
	if not failure.is_empty():
		return failure
	failure = await _set_title_and_save(RELOADED_TEXT)
	if not failure.is_empty():
		return failure
	return _expect_saved(RELOADED_TEXT, "E6-Main.xscn")


func _rebuild_on_run() -> String:
	var stamp := _read_stamp()
	if stamp.is_empty():
		return "%s did not load as a script that gives a stamp" % PROBE_PATH
	var built_before := FileAccess.get_modified_time(ASSEMBLY_PATH)
	var failure := _append_marker(PROBE_PATH)
	if not failure.is_empty():
		return failure
	EditorInterface.play_current_scene()
	failure = await _wait_for(func(): return EditorInterface.is_playing_scene(), "the run to start")
	failure = _join(failure, await _stop_run())
	if not failure.is_empty():
		return failure
	var rebuilt := func(): return FileAccess.get_modified_time(ASSEMBLY_PATH) > built_before
	failure = await _wait_for(rebuilt, "the build on run to rewrite %s" % ASSEMBLY_PATH)
	if not failure.is_empty():
		return failure
	return await _wait_for(_stamp_changed.bind(stamp), "the stamp of %s to change" % PROBE_PATH)


func _read_stamp() -> String:
	var script = load(PROBE_PATH)
	if not script is Script:
		return ""
	var probe = script.new()
	if probe == null:
		return ""
	return probe.call("GetStamp")


func _stamp_changed(old: String) -> bool:
	var stamp := _read_stamp()
	return not stamp.is_empty() and stamp != old


func _append_marker(path: String) -> String:
	var file := FileAccess.open(path, FileAccess.READ_WRITE)
	if file == null:
		return "cannot open %s: %s" % [path, error_string(FileAccess.get_open_error())]
	file.seek_end()
	file.store_string(REBUILD_MARKER)
	file.close()
	return ""


func _check_rename() -> String:
	var failure := await _rename_in_dock(RENAME_PATH, RENAMED_PATH.get_file())
	if not failure.is_empty():
		return failure
	var what := (
		"%s to replace %s, with %s naming it" % [RENAMED_PATH, RENAME_PATH, RENAME_HOST_PATH]
	)
	failure = await _wait_for(_rename_landed, what)
	if not failure.is_empty():
		return "%s; the host holds: %s" % [failure, FileAccess.get_file_as_string(RENAME_HOST_PATH)]
	return _host_instance_failure()


func _rename_landed() -> bool:
	if FileAccess.file_exists(RENAME_PATH) or not FileAccess.file_exists(RENAMED_PATH):
		return false
	if not FileAccess.file_exists(RENAMED_PATH + ".uid"):
		return false
	var host := FileAccess.get_file_as_string(RENAME_HOST_PATH)
	if host.contains(RENAME_PATH) or host.contains(RENAME_TAG):
		return false
	return host.contains(RENAMED_TAG)


func _host_instance_failure() -> String:
	var host = ResourceLoader.load(RENAME_HOST_PATH, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP)
	if not host is PackedScene:
		return "loading %s returned %s, not a PackedScene" % [RENAME_HOST_PATH, host]
	var root: Node = host.instantiate()
	var failure := ""
	if root == null:
		return "%s does not instantiate" % RENAME_HOST_PATH
	var child := root.get_node_or_null("Rename")
	if child == null:
		failure = "the %s instance has no child Rename" % RENAME_HOST_PATH
	elif child.scene_file_path != RENAMED_PATH:
		failure = "Rename.scene_file_path is '%s', not '%s'" % [child.scene_file_path, RENAMED_PATH]
	root.free()
	return failure


func _rename_in_dock(path: String, new_name: String) -> String:
	var failure := await _press_in_dock(path, KEY_F2)
	if not failure.is_empty():
		return failure
	var tree := _dock_tree()
	var edits := tree.find_children("*", "LineEdit", true, false)
	if edits.is_empty():
		return "the dock's tree has no LineEdit after F2 on %s" % path
	var edit: LineEdit = edits[0]
	edit.text = new_name
	edit.text_submitted.emit(new_name)
	return ""


# Selects the file's item in the dock's tree and presses a key on it, which the dock's shortcuts
# read.
func _press_in_dock(path: String, keycode: Key) -> String:
	EditorInterface.select_file(path)
	await get_tree().process_frame
	var tree := _dock_tree()
	if tree == null:
		return "the FileSystem dock has no Tree"
	var item := _find_tree_item(tree.get_root(), path)
	if item == null:
		return "the dock has no item for %s" % path
	item.select(0)
	tree.grab_focus()
	var key := InputEventKey.new()
	key.keycode = keycode
	key.physical_keycode = keycode
	key.pressed = true
	get_viewport().push_input(key)
	await get_tree().process_frame
	return ""


func _dock_lists(path: String) -> bool:
	var tree := _dock_tree()
	return tree != null and _find_tree_item(tree.get_root(), path) != null


func _describe_dock(path: String) -> String:
	var tree := _dock_tree()
	if tree == null:
		return "the dock has no Tree"
	var folder := _find_tree_item(tree.get_root(), path.get_base_dir() + "/")
	var names: Array[String] = []
	if folder != null:
		for child in folder.get_children():
			names.append(str(child.get_metadata(0)))
	return (
		"the tree is %s, its %s item holds %s; the file's type is '%s', it %s on disk"
		% [
			"visible" if tree.is_visible_in_tree() else "hidden",
			path.get_base_dir(),
			names if folder != null else "nothing (no item)",
			EditorInterface.get_resource_filesystem().get_file_type(path),
			"is" if FileAccess.file_exists(path) else "is not",
		]
	)


func _dock_tree() -> Tree:
	var trees := EditorInterface.get_file_system_dock().find_children("*", "Tree", true, false)
	return null if trees.is_empty() else trees[0]


func _find_tree_item(item: TreeItem, path: String) -> TreeItem:
	if item == null:
		return null
	if item.get_metadata(0) == path:
		return item
	for child in item.get_children():
		var found := _find_tree_item(child, path)
		if found != null:
			return found
	return null


func _check_paste() -> String:
	var path := "res://fixtures/E9.xscn"
	var failure := await _open_copy(path)
	if not failure.is_empty():
		return failure
	var a := _edited_panel("A")
	var b := _edited_panel("B")
	if a == null or b == null or a.material == null:
		return "%s has no panels A and B with a material on A" % path
	failure = await _copy_material_between(a, b)
	if not failure.is_empty():
		return failure
	if a.material.get_instance_id() != b.material.get_instance_id():
		return "Paste gave B its own material, not A's object"
	if not a.material.resource_path.contains("::"):
		return "A's material has the path '%s', with no ::" % a.material.resource_path
	return await _save_shared_material(path)


func _save_shared_material(path: String) -> String:
	var save_failure := _mark_and_save()
	if not save_failure.is_empty():
		return save_failure
	var failure := await _wait_for(
		_holds_shared_material.bind(path), "%s to share one material" % path
	)
	_write_out("E9.xscn", FileAccess.get_file_as_string(path))
	return failure


func _copy_material_between(from: Control, to: Control) -> String:
	var material := from.material
	EditorInterface.inspect_object(from)
	var what := "the inspector to show %s's material" % from.name
	var failure := await _wait_for(func(): return _picker_holding(material) != null, what)
	if failure.is_empty():
		failure = await _choose_in_picker(_picker_holding(material), "Copy")
	if not failure.is_empty():
		return failure
	EditorInterface.inspect_object(to)
	what = "the inspector to show %s's empty material" % to.name
	failure = await _wait_for(func(): return _empty_material_picker() != null, what)
	if failure.is_empty():
		failure = await _choose_in_picker(_empty_material_picker(), "Paste")
	if not failure.is_empty():
		return failure
	what = "Paste to give %s a material" % to.name
	return await _wait_for(func(): return to.material != null, what)


func _picker_holding(resource: Resource) -> EditorResourcePicker:
	for picker in _inspector_pickers():
		if picker.edited_resource == resource:
			return picker
	return null


func _empty_material_picker() -> EditorResourcePicker:
	for picker in _inspector_pickers():
		if picker.edited_resource == null and picker.base_type.contains("Material"):
			return picker
	return null


func _inspector_pickers() -> Array[Node]:
	return EditorInterface.get_inspector().find_children("*", "EditorResourcePicker", true, false)


func _choose_in_picker(picker: EditorResourcePicker, text: String) -> String:
	var buttons := picker.find_children("*", "Button", false, false)
	if buttons.is_empty():
		return "the resource picker has no Button child"
	buttons.back().pressed.emit()
	var menus := picker.find_children("*", "PopupMenu", false, false)
	if menus.is_empty():
		return "the resource picker has no PopupMenu child after its menu button"
	var menu: PopupMenu = menus[0]
	var chosen := _emit_menu_item(menu, text)
	menu.hide()
	return "" if chosen else "the resource picker's menu has no item %s" % text


func _emit_menu_item(menu: PopupMenu, text: String) -> bool:
	var index := _menu_item_index(menu, text)
	if index < 0:
		return false
	menu.id_pressed.emit(menu.get_item_id(index))
	return true


func _menu_item_index(menu: PopupMenu, text: String) -> int:
	for i in menu.item_count:
		if menu.get_item_text(i) == text:
			return i
	return -1


func _holds_shared_material(path: String) -> bool:
	var text := FileAccess.get_file_as_string(path)
	return text.count("x:Key=") == 1 and text.count('Material="{') == 2


func _check_save_as() -> String:
	var failure := await _open_copy("res://fixtures/E10.xscn")
	if failure.is_empty():
		failure = await _save_as_through_dialog(SAVED_AS_PATH)
	if not failure.is_empty():
		return failure
	var a := _edited_panel("A")
	if a == null or a.material == null:
		return "%s has no panel A with a material" % SAVED_AS_PATH
	if not a.material.resource_path.begins_with(SAVED_AS_PATH + "::"):
		return "A's material has the path '%s' after Save As" % a.material.resource_path
	a.material.light_mode = CanvasItemMaterial.LIGHT_MODE_LIGHT_ONLY
	var save_failure := _mark_and_save()
	if not save_failure.is_empty():
		return save_failure
	failure = await _wait_for(
		_file_contains.bind(SAVED_AS_PATH, 'LightMode="LightOnly"'),
		"%s to hold the edited sub-resource" % SAVED_AS_PATH
	)
	_write_out("E10.xscn", FileAccess.get_file_as_string(SAVED_AS_PATH))
	return failure


func _save_as_through_dialog(path: String) -> String:
	var menu := _menu_with_item(SAVE_AS_ITEM)
	if menu == null:
		return "no editor menu has the item %s" % SAVE_AS_ITEM
	_emit_menu_item(menu, SAVE_AS_ITEM)
	var failure := await _wait_for(func(): return _save_dialog() != null, "the Save As dialog")
	if not failure.is_empty():
		return failure
	var dialog := _save_dialog()
	dialog.current_path = path
	dialog.file_selected.emit(path)
	dialog.hide()
	var what := "Save As to write %s and take its path" % path
	return await _wait_for(func(): return FileAccess.file_exists(path) and _edited_is(path), what)


func _menu_with_item(text: String) -> PopupMenu:
	var base := EditorInterface.get_base_control()
	for menu in base.find_children("*", "PopupMenu", true, false):
		if _menu_item_index(menu, text) >= 0:
			return menu
	return null


func _save_dialog() -> EditorFileDialog:
	var base := EditorInterface.get_base_control()
	for dialog in base.find_children("*", "EditorFileDialog", true, false):
		if dialog.visible and dialog.file_mode == EditorFileDialog.FILE_MODE_SAVE_FILE:
			return dialog
	return null


func _check_rename_open() -> String:
	var path := E11_PATH
	var failure := await _open_copy(path)
	if not failure.is_empty():
		return failure
	var a := _edited_panel("A")
	if a == null or a.material == null:
		return "%s has no panel A with a material" % path
	var material := a.material
	failure = await _rename_in_dock(path, E11_RENAMED_PATH.get_file())
	if not failure.is_empty():
		return failure
	var what := "the open scene and its material to move to %s" % E11_RENAMED_PATH
	failure = await _wait_for(_scene_moved.bind(material), what)
	if failure.is_empty():
		return ""
	return (
		"%s (the edited root is %s, the material is at '%s')"
		% [failure, _describe_edited(), material.resource_path]
	)


func _scene_moved(material: Material) -> bool:
	if FileAccess.file_exists(E11_PATH) or not FileAccess.file_exists(E11_RENAMED_PATH):
		return false
	return (
		_edited_is(E11_RENAMED_PATH) and material.resource_path.begins_with(E11_RENAMED_PATH + "::")
	)


func _check_class_removed() -> String:
	var failure := await _add_class_and_xres()
	if not failure.is_empty():
		return failure
	# Held so the script is in ResourceCache: the Remove dialog emits resource_removed, and the
	# dock's script_classes_updated with it, only for a cached file
	# (editor/file_system/dependency_editor.cpp:785-787).
	var script = load(E12_SCRIPT_PATH)
	if not script is Script:
		return "load(%s) returned %s, not a Script" % [E12_SCRIPT_PATH, script]
	var listed := "the FileSystem dock to list %s" % E12_SCRIPT_PATH
	var known_and_listed := func():
		return _known_to_filesystem(E12_SCRIPT_PATH) and _dock_lists(E12_SCRIPT_PATH)
	failure = await _wait_for(known_and_listed, listed)
	if not failure.is_empty():
		return "%s (%s)" % [failure, _describe_dock(E12_SCRIPT_PATH)]
	failure = await _press_in_dock(E12_SCRIPT_PATH, KEY_DELETE)
	if failure.is_empty():
		var shown := "the dock's Remove dialog for %s" % E12_SCRIPT_PATH
		failure = await _wait_for(func(): return _remove_dialog() != null, shown)
	if not failure.is_empty():
		return failure
	failure = _remove_through_dialog()
	if not failure.is_empty():
		return failure
	script = null
	var what := (
		"%s to be gone and %s no longer found as its class" % [E12_SCRIPT_PATH, E12_XRES_PATH]
	)
	return await _wait_for(_class_removed_landed, what)


func _add_class_and_xres() -> String:
	var failure := _write_project_file(E12_SCRIPT_PATH, E12_SCRIPT_TEXT)
	if not failure.is_empty():
		return failure
	var what := "%s to register the global class %s" % [E12_SCRIPT_PATH, E12_CLASS]
	failure = await _wait_for(_known_to_filesystem.bind(E12_SCRIPT_PATH), what)
	if failure.is_empty():
		failure = await _wait_for(_class_registered, what)
	if not failure.is_empty():
		return failure
	failure = _write_project_file(E12_XRES_PATH, E12_XRES_TEXT)
	if not failure.is_empty():
		return failure
	what = "%s to be found as a %s" % [E12_XRES_PATH, E12_CLASS]
	failure = await _wait_for(_known_to_filesystem.bind(E12_XRES_PATH), what)
	if not failure.is_empty():
		return failure
	return await _wait_for(func(): return _scan_done() and _xres_found_as_class(), what)


# update_file does nothing while a scan runs (editor/file_system/editor_file_system.cpp:1889,
# 2409-2412), so it is asked again until the filesystem knows the file.
func _known_to_filesystem(path: String) -> bool:
	var fs := EditorInterface.get_resource_filesystem()
	if not _scan_done():
		return false
	if fs.get_file_type(path).is_empty():
		fs.update_file(path)
	return not fs.get_file_type(path).is_empty()


# Records whether the .xres is found as the class on the first script_classes_updated of the
# removal, the dock's own (editor/docks/filesystem_dock.cpp:1806-1811). It comes before the
# removal's update_file (editor/file_system/dependency_editor.cpp:785-787, 816-818), whose
# _update_script_classes re-creates the loaders
# (editor/file_system/editor_file_system.cpp:2197-2203), so at that emit only the plugin's hook
# has rebuilt the class snapshot.
func _remove_through_dialog() -> String:
	_e12_emitted = false
	_e12_still_found = false
	var fs := EditorInterface.get_resource_filesystem()
	# Connected after the plugin's hook, which the plugin made when it entered the tree, so it runs
	# after it: connect appends the slot to the signal's slot map (core/object/object.cpp:1581), a
	# HashMap kept in insertion order (core/templates/hash_map.h:48, 563, 229-232), and emit_signalp
	# calls the slots in that map's order (core/object/object.cpp:1214-1218, 1251).
	fs.script_classes_updated.connect(_on_e12_classes_updated)
	_remove_dialog().get_ok_button().pressed.emit()
	_disconnect_e12()
	if not _e12_emitted:
		return "removing %s through the dock emitted no script_classes_updated" % E12_SCRIPT_PATH
	if _e12_still_found:
		return (
			"on the dock's script_classes_updated for the removed %s, %s is still found as the class %s"
			% [E12_SCRIPT_PATH, E12_XRES_PATH, E12_CLASS]
		)
	return ""


func _on_e12_classes_updated() -> void:
	if _e12_emitted:
		return
	_e12_emitted = true
	_e12_still_found = _xres_found_as_class()
	_disconnect_e12()


func _disconnect_e12() -> void:
	var fs := EditorInterface.get_resource_filesystem()
	if fs.script_classes_updated.is_connected(_on_e12_classes_updated):
		fs.script_classes_updated.disconnect(_on_e12_classes_updated)


func _remove_dialog() -> ConfirmationDialog:
	for child in EditorInterface.get_file_system_dock().get_children():
		if child is ConfirmationDialog and child.visible and child.get_ok_button().text == "Remove":
			return child
	return null


func _class_registered() -> bool:
	if not _scan_done():
		return false
	for entry in ProjectSettings.get_global_class_list():
		if entry["path"] == E12_SCRIPT_PATH and entry["class"] == E12_CLASS:
			return true
	return false


# With the class as the type hint, ResourceLoader.exists asks the .xres loader's _HandlesType
# (core/io/resource_loader.cpp:1257-1262, 72, 135-138), which answers from the plugin's class
# snapshot; get_resource_script_class has no script binding (core/core_bind.cpp). The .xres is
# never loaded here, so no cache entry answers first (core/io/resource_loader.cpp:1250).
func _xres_found_as_class() -> bool:
	return ResourceLoader.exists(E12_XRES_PATH, E12_CLASS)


func _class_removed_landed() -> bool:
	return (
		not FileAccess.file_exists(E12_SCRIPT_PATH) and _scan_done() and not _xres_found_as_class()
	)


func _write_project_file(path: String, text: String) -> String:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s: %s" % [path, error_string(FileAccess.get_open_error())]
	file.store_string(text)
	file.close()
	return ""


func _open_copy(path: String) -> String:
	var from := ProjectSettings.globalize_path(SUB_PATH)
	var copied := DirAccess.copy_absolute(from, ProjectSettings.globalize_path(path))
	if copied != OK:
		return "copying %s to %s returned %s" % [SUB_PATH, path, error_string(copied)]
	EditorInterface.get_resource_filesystem().update_file(path)
	await get_tree().process_frame
	var failure := await _wait_for(_scan_done, "the scan of %s" % path)
	if not failure.is_empty():
		return failure
	EditorInterface.open_scene_from_path(path)
	return await _wait_for(_edited_is.bind(path), "%s to open" % path)


func _edited_panel(panel_name: String) -> Control:
	return EditorInterface.get_edited_scene_root().get_node_or_null(panel_name) as Control


func _set_title_and_save(text: String) -> String:
	var root := EditorInterface.get_edited_scene_root()
	if root == null or root.scene_file_path != MAIN_PATH:
		return "the edited scene is %s, not %s" % [_describe_edited(), MAIN_PATH]
	var title := root.get_node_or_null("Title") as Label
	if title == null:
		return "the edited scene has no Label named Title"
	title.text = text
	var save_failure := _mark_and_save()
	if not save_failure.is_empty():
		return save_failure
	var what := "%s to contain '%s'" % [MAIN_PATH, text]
	return await _wait_for(_file_contains.bind(MAIN_PATH, text), what)


func _expect_saved(text: String, copy_name: String) -> String:
	var content := FileAccess.get_file_as_string(MAIN_PATH)
	_write_out(copy_name, content)
	if not content.begins_with("<Control"):
		return "%s does not start with <Control: %s" % [MAIN_PATH, content.left(200)]
	if not content.contains('Text="%s"' % text):
		return '%s does not contain Text="%s"' % [MAIN_PATH, text]
	return ""


func _mark_and_save() -> String:
	EditorInterface.mark_scene_as_unsaved()
	var saved := EditorInterface.save_scene()
	if saved != OK:
		return "save_scene returned %s" % error_string(saved)
	return ""


func _stop_run() -> String:
	EditorInterface.stop_playing_scene()
	return await _wait_for(func(): return not EditorInterface.is_playing_scene(), "the run to stop")


func _wait_for(condition: Callable, what: String) -> String:
	var start := Time.get_ticks_msec()
	while not condition.call():
		if Time.get_ticks_msec() - start > WAIT_LIMIT_MS:
			return "timed out waiting for %s" % what
		await get_tree().process_frame
	return ""


func _first_scan_done() -> bool:
	return _scan_done() and EditorInterface.get_resource_filesystem().get_file_type(MAIN_PATH) != ""


func _scan_done() -> bool:
	return not EditorInterface.get_resource_filesystem().is_scanning()


func _main_is_open() -> bool:
	var root := EditorInterface.get_edited_scene_root()
	return _edited_is(MAIN_PATH) and root.name == "Main" and root.get_node_or_null("Title") != null


func _edited_is(path: String) -> bool:
	var root := EditorInterface.get_edited_scene_root()
	return root != null and root.scene_file_path == path


func _ready_file_written() -> bool:
	if not FileAccess.file_exists(READY_PATH):
		return false
	return not FileAccess.get_file_as_string(READY_PATH).is_empty()


func _file_contains(path: String, text: String) -> bool:
	return FileAccess.get_file_as_string(path).contains(text)


func _describe_edited() -> String:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return "none"
	return "%s from '%s'" % [root.name, root.scene_file_path]


func _join(first: String, second: String) -> String:
	if first.is_empty():
		return second
	if second.is_empty():
		return first
	return "%s; %s" % [first, second]


func _report(id: String, failure: String) -> void:
	_total += 1
	var line := "CHECK %s PASS" % id
	if failure.is_empty():
		_passed += 1
	else:
		line = "CHECK %s FAIL %s" % [id, failure.replace("\n", " | ")]
	_emit(line)
	await get_tree().process_frame
	var image := EditorInterface.get_base_control().get_viewport().get_texture().get_image()
	var path := _out.path_join("%s.png" % id)
	var saved := image.save_png(path)
	if saved != OK:
		push_error("editor_checks: cannot save %s: %s" % [path, error_string(saved)])


func _emit(line: String) -> void:
	print(line)
	var path := _out.path_join("results.txt")
	var mode := FileAccess.READ_WRITE if FileAccess.file_exists(path) else FileAccess.WRITE
	var file := FileAccess.open(path, mode)
	if file == null:
		_push_open_error(path)
		return
	file.seek_end()
	file.store_line(line)


func _write_out(file_name: String, text: String) -> void:
	var path := _out.path_join(file_name)
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		_push_open_error(path)
		return
	file.store_string(text)


func _push_open_error(path: String) -> void:
	var reason := error_string(FileAccess.get_open_error())
	push_error("editor_checks: cannot open %s: %s" % [path, reason])
