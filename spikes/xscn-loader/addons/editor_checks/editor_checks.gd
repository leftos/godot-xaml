@tool
extends EditorPlugin

const MODE_ARG := "--xscn-editor-checks="
const OUT_ARG := "--out="
const START_DELAY_S := 3.0
const WAIT_LIMIT_MS := 60000
const RELOAD_SETTLE_MS := 2000
const MAIN_PATH := "res://scenes/Main.xscn"
const MOVED_PATH := "res://scenes/Moved.xscn"
const HOST_PATH := "res://scenes/Host.tscn"
const SAVER_PATH := "res://addons/xscn_spike/XscnFormatSaver.cs"
const ASSEMBLY_PATH := "res://.godot/mono/temp/bin/Debug/XscnSpike.dll"
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
	await get_tree().create_timer(START_DELAY_S).timeout
	var scanned := await _wait_for(_scan_done, "the first filesystem scan")
	if not scanned.is_empty():
		_emit("NOTE START %s" % scanned)
	match _mode:
		"run":
			await _run_all()
		"reopen":
			await _report("E8", await _check_open("E8"))
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
	await _report("E7", await _check_move())


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


func _check_edit_save() -> String:
	var failure := await _set_title_and_save(EDITED_TEXT)
	if not failure.is_empty():
		return failure
	return _expect_xml(EDITED_TEXT, "E3-Main.xscn")


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
	EditorInterface.mark_scene_as_unsaved()
	var saved := EditorInterface.save_scene()
	if saved != OK:
		return "save_scene returned %s" % error_string(saved)
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
	var original := FileAccess.get_file_as_bytes(SAVER_PATH)
	if original.is_empty():
		return "cannot read %s: %s" % [SAVER_PATH, error_string(FileAccess.get_open_error())]
	var failure := await _rebuild_and_save(original)
	return _join(failure, _write_bytes(SAVER_PATH, original))


func _rebuild_and_save(original: PackedByteArray) -> String:
	EditorInterface.open_scene_from_path(MAIN_PATH)
	var what := "%s to be the edited scene" % MAIN_PATH
	var failure := await _wait_for(_edited_is.bind(MAIN_PATH), what)
	if failure.is_empty():
		failure = await _rebuild_on_run(original)
	if not failure.is_empty():
		return failure
	failure = await _set_title_and_save(RELOADED_TEXT)
	if not failure.is_empty():
		return failure
	return _expect_xml(RELOADED_TEXT, "E6-Main.xscn")


func _rebuild_on_run(original: PackedByteArray) -> String:
	var built_before := FileAccess.get_modified_time(ASSEMBLY_PATH)
	var marked := original.duplicate()
	marked.append_array(REBUILD_MARKER.to_utf8_buffer())
	var failure := _write_bytes(SAVER_PATH, marked)
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
	var settle_start := Time.get_ticks_msec()
	var settled := func(): return Time.get_ticks_msec() - settle_start >= RELOAD_SETTLE_MS
	await _wait_for(settled, "the assembly watcher to reload")
	_emit(
		(
			(
				"NOTE E6 the build on run rewrote %s; no reload signal is readable from GDScript, "
				+ "so the check waited for the run, its stop and %d ms of watcher periods"
			)
			% [ASSEMBLY_PATH, RELOAD_SETTLE_MS]
		)
	)
	return ""


func _check_move() -> String:
	_emit("NOTE E7 renamed through DirAccess and a rescan, an approximation of the dock's rename")
	var failure := _rename_scene(MAIN_PATH, MOVED_PATH, true)
	if failure.is_empty():
		failure = await _rescan()
	if failure.is_empty():
		failure = _host_resolves_moved()
	var restored := _rename_scene(MOVED_PATH, MAIN_PATH, false)
	if restored.is_empty():
		restored = await _rescan()
	return _join(failure, restored)


func _rename_scene(from: String, to: String, required: bool) -> String:
	for suffix in ["", ".uid"]:
		var source := ProjectSettings.globalize_path(from + suffix)
		if not FileAccess.file_exists(source):
			if required:
				return "%s does not exist" % (from + suffix)
			continue
		var target := ProjectSettings.globalize_path(to + suffix)
		var renamed := DirAccess.rename_absolute(source, target)
		if renamed != OK:
			var names := [from + suffix, to + suffix, error_string(renamed)]
			return "renaming %s to %s returned %s" % names
	return ""


func _rescan() -> String:
	EditorInterface.get_resource_filesystem().scan()
	await get_tree().process_frame
	return await _wait_for(_scan_done, "the filesystem scan")


func _host_resolves_moved() -> String:
	var host = ResourceLoader.load(HOST_PATH, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP)
	if not host is PackedScene:
		return "loading %s returned %s, not a PackedScene" % [HOST_PATH, host]
	var root: Node = host.instantiate()
	var main := root.get_node_or_null("Main")
	var failure := ""
	if main == null:
		failure = "the Host instance has no child Main"
	elif main.scene_file_path != MOVED_PATH:
		failure = "Main.scene_file_path is '%s', not '%s'" % [main.scene_file_path, MOVED_PATH]
	root.free()
	return failure


func _set_title_and_save(text: String) -> String:
	var root := EditorInterface.get_edited_scene_root()
	if root == null or root.scene_file_path != MAIN_PATH:
		return "the edited scene is %s, not %s" % [_describe_edited(), MAIN_PATH]
	var title := root.get_node_or_null("Title") as Label
	if title == null:
		return "the edited scene has no Label named Title"
	title.text = text
	EditorInterface.mark_scene_as_unsaved()
	var saved := EditorInterface.save_scene()
	if saved != OK:
		return "save_scene returned %s" % error_string(saved)
	var what := "%s to contain '%s'" % [MAIN_PATH, text]
	return await _wait_for(_file_contains.bind(MAIN_PATH, text), what)


func _expect_xml(text: String, copy_name: String) -> String:
	var content := FileAccess.get_file_as_string(MAIN_PATH)
	_write_out(copy_name, content)
	if not content.begins_with("<?xml"):
		return "%s does not start with <?xml: %s" % [MAIN_PATH, content.left(200)]
	if not content.contains(text):
		return "%s does not contain '%s'" % [MAIN_PATH, text]
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


func _write_bytes(path: String, bytes: PackedByteArray) -> String:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s: %s" % [path, error_string(FileAccess.get_open_error())]
	file.store_buffer(bytes)
	file.close()
	return ""
