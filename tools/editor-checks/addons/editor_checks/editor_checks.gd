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

var _mode := ""
var _out := ""
var _passed := 0
var _total := 0


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
	var dock := EditorInterface.get_file_system_dock()
	EditorInterface.select_file(path)
	await get_tree().process_frame
	var trees := dock.find_children("*", "Tree", true, false)
	if trees.is_empty():
		return "the FileSystem dock has no Tree"
	var tree: Tree = trees[0]
	var item := _find_tree_item(tree.get_root(), path)
	if item == null:
		return "the dock has no item for %s" % path
	item.select(0)
	tree.grab_focus()
	var f2 := InputEventKey.new()
	f2.keycode = KEY_F2
	f2.physical_keycode = KEY_F2
	f2.pressed = true
	get_viewport().push_input(f2)
	await get_tree().process_frame
	var edits := tree.find_children("*", "LineEdit", true, false)
	if edits.is_empty():
		return "the dock's tree has no LineEdit after F2 on %s" % path
	var edit: LineEdit = edits[0]
	edit.text = new_name
	edit.text_submitted.emit(new_name)
	return ""


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
