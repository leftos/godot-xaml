## The preview server checks of the editor checks: each returns "" for a pass or why it failed,
## as the plugin's own checks do.
extends RefCounted

# The scene the reload check plays, so the editor builds on run; the plugin names it too.
const MAIN_PATH := "res://fixtures/Main.xscn"
const E13_PATH := "res://fixtures/E13.xscn"
const E13_FRONT := "Front"
const E13_FRONT_COLOR := Color(0, 0, 1, 1)
const E13_SIZE := Vector2i(640, 480)
const SCRIPTED_SCENE_PATH := "res://fixtures/Scripted.tscn"
const SCRIPTED_SCRIPT_PATH := "res://fixtures/Scripted.gd"
const STATIC_INIT_MARKER := "gx_e14_static_init"
const INIT_MARKER := "gx_e14_init"
const SCRIPT_REFUSAL := "is a script that an instance placeholder would load"
const PLACEHOLDER_TEXT_FORMAT := (
	'<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="%s">'
	+ '<InstancePlaceholder Name="Ph" x:InstancePlaceholder="%s" /></Control>'
)
const PREVIEW_SETTING := "godot_xaml/preview_server/enabled"
const DISCOVERY_FRAMES := 120
const COLOR_TOLERANCE := 0.02
const PreviewClient := preload("res://addons/editor_checks/preview_client.gd")

# The editor checks plugin, untyped so its own helpers can be called on it.
var _checks
var _tree: SceneTree


func _init(checks: EditorPlugin) -> void:
	_checks = checks
	_tree = checks.get_tree()


func _join(first: String, second: String) -> String:
	return _checks._join(first, second)


func _wait_for(condition: Callable, what: String) -> String:
	return await _checks._wait_for(condition, what)


# Reloads the assembly while the server runs: the old server stops with its connections, port and
# discovery file, a new one starts on its own, and the setting still turns it off and on.
func check_preview_reload() -> String:
	EditorInterface.open_scene_from_path(MAIN_PATH)
	var opened := "%s to be the edited scene" % MAIN_PATH
	var failure := await _wait_for(Callable(_checks, "_edited_is").bind(MAIN_PATH), opened)
	if not failure.is_empty():
		return failure
	ProjectSettings.set_setting(PREVIEW_SETTING, true)
	var held := PreviewClient.new(_tree)
	failure = await _reload_session(held)
	held.close()
	ProjectSettings.set_setting(PREVIEW_SETTING, false)
	var left := await _wait_discovery_file(false)
	if not left.is_empty():
		failure = _join(failure, "the discovery file %s is still there with the setting off" % left)
	return failure


func _reload_session(held: PreviewClient) -> String:
	var old_file := await _wait_discovery_file(true)
	if old_file.is_empty():
		return _no_discovery_file()
	var connected := await _preview_connect(held, old_file)
	var failure: String = connected["failure"]
	if not failure.is_empty():
		return "before the reload: %s" % failure
	failure = await _checks._rebuild_on_run()
	if not failure.is_empty():
		return failure
	var old_name := old_file.get_file()
	var renewed := func(): return not _discovery_file_except(old_name).is_empty()
	var what := "a discovery file for pid %d other than %s" % [OS.get_process_id(), old_name]
	failure = await _wait_for(renewed, what)
	if not failure.is_empty():
		return failure
	var new_file := _discovery_file_except(old_name)
	failure = await _renewed_server_failure(held, old_file, connected["info"], new_file)
	return _join(failure, await _setting_followed_failure())


func _renewed_server_failure(
	held: PreviewClient, old_file: String, old_info: Dictionary, new_file: String
) -> String:
	var failure := ""
	if FileAccess.file_exists(old_file):
		failure = "the old discovery file %s is still there" % old_file
	var info = JSON.parse_string(FileAccess.get_file_as_string(new_file))
	if not info is Dictionary:
		return _join(failure, "the discovery file %s holds no JSON object" % new_file)
	if info["token"] == old_info["token"]:
		failure = _join(failure, "the new discovery file's token is the old one")
	if int(info["pid"]) != OS.get_process_id():
		failure = _join(failure, "the new discovery file's pid is %s" % info["pid"])
	var params := {"token": old_info["token"], "project": old_info["project"]}
	var after: Dictionary = await held.request(9, "ping", params)
	var closed: bool = after.has("error") and not after.has("id")
	if not closed or str(after["error"]).begins_with("timed out"):
		failure = _join(failure, "the held connection got %s, not closed" % _describe_reply(after))
	var fresh := PreviewClient.new(_tree)
	var connected := await _preview_connect(fresh, new_file)
	fresh.close()
	var new_failure: String = connected["failure"]
	if not new_failure.is_empty():
		failure = _join(failure, "the new server: %s" % new_failure)
	return _join(failure, await _old_port_failure(int(old_info["port"]), int(info["port"])))


func _old_port_failure(old_port: int, new_port: int) -> String:
	if new_port == old_port:
		_checks._emit(
			"NOTE E16 the new port equals the old one; the old-port refusal is not checked"
		)
		return ""
	var probe := PreviewClient.new(_tree)
	# On Windows a refused loopback connect stays "connecting" until Godot's
	# network/limits/tcp/connect_timeout_seconds runs out (winsock's WSAEALREADY maps to in
	# progress, and the stream peer turns to STATUS_ERROR only at the timeout), so a gone listener
	# is seen at that timeout, while a live one on loopback connects at once.
	var refused: String = await probe.open(old_port)
	probe.close()
	if refused.is_empty():
		return "port %d of the stopped server still accepts a connection" % old_port
	return ""


func _setting_followed_failure() -> String:
	ProjectSettings.set_setting(PREVIEW_SETTING, false)
	var left := await _wait_discovery_file(false)
	if not left.is_empty():
		return "after the reload the setting off left the discovery file %s" % left
	ProjectSettings.set_setting(PREVIEW_SETTING, true)
	if (await _wait_discovery_file(true)).is_empty():
		return "after the reload the setting back on wrote no discovery file"
	return ""


# Turns the preview server on, renders E13.xscn at 320x240 at scale 2 over a loopback client, then
# a broken text of it, which answers the first image again, stale; then turns the server off.
func check_preview_image() -> String:
	return await _with_preview(_preview_session)


# A placeholder to a scripted .tscn previews from its stripped copy: nothing of the scene's script
# runs, neither its static initializer nor its constructor.
func check_preview_placeholder_strip() -> String:
	var failure := _placeholder_precondition()
	if not failure.is_empty():
		return failure
	var text := PLACEHOLDER_TEXT_FORMAT % ["E14", SCRIPTED_SCENE_PATH]
	var session := _placeholder_session.bind("res://fixtures/E14.xscn", text, true)
	return _join(await _with_preview(session), _placeholder_markers_failure("after the render"))


# A placeholder to a .gd is refused with GXC0121, and the script does not load.
func check_preview_placeholder_script() -> String:
	var failure := _placeholder_precondition()
	if not failure.is_empty():
		return failure
	var text := PLACEHOLDER_TEXT_FORMAT % ["E15", SCRIPTED_SCRIPT_PATH]
	var session := _placeholder_session.bind("res://fixtures/E15.xscn", text, false)
	return _join(await _with_preview(session), _placeholder_markers_failure("after the render"))


# A script already loaded would not run its static initializer again, so the check needs it
# unloaded.
func _placeholder_precondition() -> String:
	var failure := _placeholder_markers_failure("before the render")
	if ResourceLoader.has_cached(SCRIPTED_SCRIPT_PATH):
		failure = _join(failure, "%s is already in the resource cache" % SCRIPTED_SCRIPT_PATH)
	if failure.is_empty():
		return ""
	return "precondition: %s" % failure


func _placeholder_markers_failure(when: String) -> String:
	var failure := ""
	for marker in [STATIC_INIT_MARKER, INIT_MARKER]:
		if Engine.has_meta(marker):
			failure = _join(failure, "the Engine meta %s is set %s" % [marker, when])
	return failure


func _placeholder_session(
	client: PreviewClient, discovery: String, document: String, text: String, stripped: bool
) -> String:
	var connected := await _preview_connect(client, discovery)
	var failure: String = connected["failure"]
	if not failure.is_empty():
		return failure
	var params := {"document": document, "text": text, "width": 320, "height": 240}
	var reply: Dictionary = await client.request(2, "render", params)
	var result = reply.get("result")
	if not result is Dictionary:
		return "the render got %s" % _describe_reply(reply)
	var refused := false
	for issue in result.get("issues", []):
		if issue is Dictionary and issue.get("code") == "GXC0121":
			refused = refused or str(issue.get("message", "")).contains(SCRIPT_REFUSAL)
			if stripped:
				return "the render was refused: %s" % _describe_reply(reply)
	if stripped and (result.get("loaded") != true or result.get("stale") != false):
		return "the render got %s, not a loaded fresh result" % _describe_reply(reply)
	if not stripped and (result.get("loaded") != false or not refused):
		return "the render got %s, not a GXC0121 refusal of the script" % _describe_reply(reply)
	return ""


# Turns the preview server on, waits for its discovery file, runs the session with a client, then
# turns the server off and waits for the file to go.
func _with_preview(session: Callable) -> String:
	ProjectSettings.set_setting(PREVIEW_SETTING, true)
	var discovery := await _wait_discovery_file(true)
	var failure := ""
	if discovery.is_empty():
		failure = _no_discovery_file()
	else:
		var client := PreviewClient.new(_tree)
		failure = await session.call(client, discovery)
		client.close()
	ProjectSettings.set_setting(PREVIEW_SETTING, false)
	var left := await _wait_discovery_file(false)
	if not left.is_empty():
		failure = _join(failure, "the discovery file %s is still there with the setting off" % left)
	return failure


func _discovery_folder() -> String:
	return OS.get_environment("LOCALAPPDATA").path_join("godot-xaml").path_join("editors")


# This editor's discovery file, or "" when there is none.
func _discovery_file() -> String:
	return _discovery_file_except("")


# This editor's discovery file other than the one named skip, or "" when there is none.
func _discovery_file_except(skip: String) -> String:
	var prefix := "%d-" % OS.get_process_id()
	# get_files_at logs an engine error for a folder that does not exist yet.
	if not DirAccess.dir_exists_absolute(_discovery_folder()):
		return ""
	for file_name in DirAccess.get_files_at(_discovery_folder()):
		if file_name == skip:
			continue
		if file_name.begins_with(prefix) and file_name.ends_with(".json"):
			return _discovery_folder().path_join(file_name)
	return ""


# Waits up to DISCOVERY_FRAMES frames for the file to be there (present) or gone; returns the path
# found at the end, "" when there is none.
func _wait_discovery_file(present: bool) -> String:
	for _frame in DISCOVERY_FRAMES:
		if _discovery_file().is_empty() != present:
			break
		await _tree.process_frame
	return _discovery_file()


func _no_discovery_file() -> String:
	return (
		"no discovery file for pid %d appeared in %s within %d frames"
		% [OS.get_process_id(), _discovery_folder(), DISCOVERY_FRAMES]
	)


# Opens the client on the port the discovery file names and pings with its token; returns
# {"failure": "", "info": <the file's object>}, or {"failure": <why>}.
func _preview_connect(client: PreviewClient, discovery: String) -> Dictionary:
	var info = JSON.parse_string(FileAccess.get_file_as_string(discovery))
	if not info is Dictionary:
		return {"failure": "the discovery file %s holds no JSON object" % discovery}
	var failure: String = await client.open(int(info["port"]))
	if not failure.is_empty():
		return {"failure": failure}
	var ping_params := {"token": info["token"], "project": info["project"]}
	var pinged: Dictionary = await client.request(1, "ping", ping_params)
	if not pinged.has("result"):
		return {"failure": "the ping got %s" % _describe_reply(pinged)}
	return {"failure": "", "info": info}


func _preview_session(client: PreviewClient, discovery: String) -> String:
	var connected := await _preview_connect(client, discovery)
	var failure: String = connected["failure"]
	if not failure.is_empty():
		return failure
	var text := FileAccess.get_file_as_string(E13_PATH)
	var params := {"document": E13_PATH, "text": text, "width": 320, "height": 240, "scale": 2}
	var fresh: Dictionary = await client.request(2, "render", params)
	failure = _fresh_image_failure(fresh)
	if not failure.is_empty():
		return failure
	params["text"] = text.replace("</Control>", "")
	var stale: Dictionary = await client.request(3, "render", params)
	return _stale_image_failure(stale, fresh["result"]["image"])


# A reply without its image, which is too long to print.
func _describe_reply(reply: Dictionary) -> String:
	var shown := reply.duplicate(true)
	if shown.get("result") is Dictionary and shown["result"].has("image"):
		shown["result"]["image"] = "<%d base64 chars>" % str(shown["result"]["image"]).length()
	return JSON.stringify(shown)


func _fresh_image_failure(reply: Dictionary) -> String:
	var result = reply.get("result")
	if not result is Dictionary or result.get("stale") != false or not result.has("image"):
		return "the render got %s, not a fresh image" % _describe_reply(reply)
	var image := Image.new()
	var loaded := image.load_png_from_buffer(Marshalls.base64_to_raw(result["image"]))
	if loaded != OK:
		return "the render's image does not load as a PNG: %s" % error_string(loaded)
	if image.get_size() != E13_SIZE:
		return "the render's image is %s, not %s" % [image.get_size(), E13_SIZE]
	if _one_colour(image):
		return "the render's image is one colour, %s" % image.get_pixel(0, 0)
	return _front_pixel_failure(image, result["rects"])


func _one_colour(image: Image) -> bool:
	var first := image.get_pixel(0, 0)
	for y in range(0, image.get_height(), 8):
		for x in range(0, image.get_width(), 8):
			if not _same_colour(image.get_pixel(x, y), first):
				return false
	return true


func _same_colour(a: Color, b: Color) -> bool:
	return (
		absf(a.r - b.r) <= COLOR_TOLERANCE
		and absf(a.g - b.g) <= COLOR_TOLERANCE
		and absf(a.b - b.b) <= COLOR_TOLERANCE
	)


func _front_pixel_failure(image: Image, rects: Array) -> String:
	for rect in rects:
		if rect["path"] != E13_FRONT:
			continue
		var x := int(rect["x"] + rect["width"] / 2)
		var y := int(rect["y"] + rect["height"] / 2)
		var pixel := image.get_pixel(x, y)
		if _same_colour(pixel, E13_FRONT_COLOR):
			return ""
		return (
			"the pixel at (%d, %d), %s's centre, is %s, not %s"
			% [x, y, E13_FRONT, pixel, E13_FRONT_COLOR]
		)
	return "the render's rects %s have none whose path is %s" % [rects, E13_FRONT]


func _stale_image_failure(reply: Dictionary, fresh_image: String) -> String:
	var result = reply.get("result")
	if not result is Dictionary or result.get("stale") != true:
		return "the broken render got %s, not a stale result" % _describe_reply(reply)
	if result.get("image") != fresh_image:
		return "the broken render's stale image is not the fresh render's"
	return ""
