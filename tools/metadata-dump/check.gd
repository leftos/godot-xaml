extends SceneTree
## Asserts known Godot 4.7 facts about a metadata file written by dump.gd.
##
## Run: godot --headless --path tools/metadata-dump --script res://check.gd --
##     --file <absolute path>
## Prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` per assertion, and exits 1 when any
## failed.

const LF := 10
const CR := 13

var _failed := false


func _initialize() -> void:
	var path := _arg_value("--file")
	if path.is_empty():
		push_error("check.gd: pass -- --file <absolute path to the metadata JSON>")
		quit(1)
		return
	var bytes := FileAccess.get_file_as_bytes(path)
	if bytes.is_empty():
		var reason := error_string(FileAccess.get_open_error())
		_check("LOAD", "cannot read %s (%s)" % [path, reason])
		quit(1)
		return
	var data: Variant = JSON.parse_string(bytes.get_string_from_utf8())
	if not data is Dictionary:
		_check("LOAD", "%s is not a JSON object" % path)
		quit(1)
		return
	_run_checks(data, bytes)
	quit(1 if _failed else 0)


func _run_checks(data: Dictionary, bytes: PackedByteArray) -> void:
	_check("K1", _k1(data))
	_check("K2", _k2(data))
	_check("K3", _k3(data))
	_check("K4", _k4(data))
	_check("K5", _k5(data))
	_check("K6", _k6(data))
	_check("K7", _k7(data))
	_check("K8", _k8(data))
	_check("K9", _k9(bytes))
	_check("K10", _k10(data))
	_check("K11", _k11(data))
	_check("K12", _k12(data))
	_check("K13", _k13(data))
	_check("K14", _k14(data))


func _k1(data: Dictionary) -> String:
	var control := _class(data, "Control")
	return _first(
		[
			_mismatch("Control.parent", control.get("parent"), "CanvasItem"),
			_mismatch("Control.instantiable", control.get("instantiable"), true),
		]
	)


func _k2(data: Dictionary) -> String:
	var prop := _property(data, "Control", "mouse_filter")
	return _first(
		[
			_mismatch("mouse_filter.enumType", prop.get("enumType"), "Control.MouseFilter"),
			_mismatch("mouse_filter.bitfield", prop.get("bitfield"), false),
			_mismatch("mouse_filter.default", prop.get("default"), "0"),
			_enum_has(_class(data, "Control"), "MouseFilter", "MOUSE_FILTER_IGNORE", 2),
		]
	)


func _k3(data: Dictionary) -> String:
	var prop := _property(data, "Control", "size_flags_horizontal")
	return _first(
		[
			_mismatch("size_flags_horizontal.enumType", prop.get("enumType"), "Control.SizeFlags"),
			_mismatch("size_flags_horizontal.bitfield", prop.get("bitfield"), true),
			_enum_has(_class(data, "Control"), "SizeFlags", "SIZE_EXPAND_FILL", 3),
		]
	)


func _k4(data: Dictionary) -> String:
	var prop := _property(data, "Node2D", "position")
	return _first(
		[
			_mismatch("Node2D.position.type", prop.get("type"), "Vector2"),
			_mismatch("Node2D.position.default", prop.get("default"), "Vector2(0, 0)"),
		]
	)


func _k5(data: Dictionary) -> String:
	var prop := _property(data, "Label", "text")
	return _first(
		[
			_mismatch("Label.text.type", prop.get("type"), "String"),
			_mismatch("Label.text.default", prop.get("default"), '""'),
		]
	)


func _k6(data: Dictionary) -> String:
	var canvas_item := _class(data, "CanvasItem")
	var detail := _mismatch("CanvasItem.instantiable", canvas_item.get("instantiable"), false)
	if not detail.is_empty():
		return detail
	var props: Array = canvas_item.get("properties", [])
	if props.is_empty():
		return "CanvasItem has no properties"
	for prop: Dictionary in props:
		if prop.has("default"):
			return "CanvasItem.%s has a default %s" % [prop.get("name"), var_to_str(prop.default)]
	return ""


func _k7(data: Dictionary) -> String:
	var global_enums: Dictionary = data.get("globalEnums", {})
	if not global_enums.has("HorizontalAlignment"):
		return "globalEnums has no HorizontalAlignment"
	return _values_have("globalEnums.Side", global_enums.get("Side", {}), "SIDE_LEFT", 0)


func _k8(data: Dictionary) -> String:
	var prop := _property(data, "Control", "layout_mode")
	if prop.is_empty():
		return "Control has no property layout_mode"
	if int(prop.get("usage", 0)) & PROPERTY_USAGE_STORAGE == 0:
		return "Control.layout_mode.usage %s lacks PROPERTY_USAGE_STORAGE" % prop.get("usage")
	return ""


func _k9(bytes: PackedByteArray) -> String:
	if bytes[bytes.size() - 1] != LF:
		return "the file does not end with a line feed"
	if bytes.has(CR):
		return "the file contains a carriage return at byte %d" % bytes.find(CR)
	return ""


func _k10(data: Dictionary) -> String:
	var entry := _theme_override(data, "Label", "theme_override_colors/font_color")
	if entry.is_empty():
		return "Label has no theme_override_colors/font_color"
	return _mismatch("Label.theme_override_colors/font_color.type", entry.get("type"), "Color")


func _k11(data: Dictionary) -> String:
	var entry := _theme_override(data, "Button", "theme_override_styles/normal")
	if entry.is_empty():
		return "Button has no theme_override_styles/normal"
	return _first(
		[
			_mismatch("Button.theme_override_styles/normal.type", entry.get("type"), "Object"),
			_mismatch(
				"Button.theme_override_styles/normal.hintString",
				entry.get("hintString"),
				"StyleBox"
			),
		]
	)


func _k12(data: Dictionary) -> String:
	var entries: Array = _class(data, "Window").get("themeOverrides", [])
	for entry: Dictionary in entries:
		if String(entry.get("name", "")).begins_with("theme_override_"):
			return ""
	return "Window.themeOverrides has no theme_override_ entry"


func _k13(data: Dictionary) -> String:
	if _class(data, "Node2D").has("themeOverrides"):
		return "Node2D has a themeOverrides key"
	return ""


func _k14(data: Dictionary) -> String:
	for prop: Dictionary in _class(data, "Control").get("properties", []):
		if String(prop.get("name", "")).contains("/"):
			return "Control.properties has %s" % prop.get("name")
	return ""


func _check(id: String, detail: String) -> void:
	if detail.is_empty():
		print("CHECK %s PASS" % id)
		return
	_failed = true
	print("CHECK %s FAIL %s" % [id, detail])


func _class(data: Dictionary, name: String) -> Dictionary:
	var classes: Dictionary = data.get("classes", {})
	return classes.get(name, {})


func _property(data: Dictionary, class_title: String, name: String) -> Dictionary:
	for prop: Dictionary in _class(data, class_title).get("properties", []):
		if prop.get("name") == name:
			return prop
	return {}


func _theme_override(data: Dictionary, class_title: String, name: String) -> Dictionary:
	for entry: Dictionary in _class(data, class_title).get("themeOverrides", []):
		if entry.get("name") == name:
			return entry
	return {}


func _enum_has(class_entry: Dictionary, enum_name: String, member: String, value: int) -> String:
	var enums: Dictionary = class_entry.get("enums", {})
	return _values_have(enum_name, enums.get(enum_name, {}), member, value)


func _values_have(label: String, enum_entry: Dictionary, member: String, value: int) -> String:
	for pair: Array in enum_entry.get("values", []):
		if pair.size() == 2 and pair[0] == member and int(pair[1]) == value:
			return ""
	return "%s has no pair [%s, %d]" % [label, member, value]


func _mismatch(label: String, actual: Variant, expected: Variant) -> String:
	if typeof(actual) == typeof(expected) and actual == expected:
		return ""
	return "%s is %s, expected %s" % [label, var_to_str(actual), var_to_str(expected)]


func _first(details: Array) -> String:
	for detail: String in details:
		if not detail.is_empty():
			return detail
	return ""


func _arg_value(name: String) -> String:
	var args := OS.get_cmdline_user_args()
	var index := args.find(name)
	if index == -1 or index + 1 >= args.size():
		return ""
	return args[index + 1]
