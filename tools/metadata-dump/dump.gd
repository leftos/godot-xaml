extends SceneTree
## Writes the engine's class metadata to <out-dir>/godot-<major>.<minor>.json.
##
## Run: godot --headless --path tools/metadata-dump --script res://dump.gd --
##     --api <absolute path to extension_api.json> --out-dir <absolute folder>
## ClassDB gives the classes, their own properties, defaults and enums; the extension API dump
## gives each property's enum or bitfield type and the global enums.

const SKIPPED_USAGE := PROPERTY_USAGE_CATEGORY | PROPERTY_USAGE_GROUP | PROPERTY_USAGE_SUBGROUP
const ENUM_PREFIX := "enum::"
const BITFIELD_PREFIX := "bitfield::"


func _initialize() -> void:
	var error := _run()
	if not error.is_empty():
		push_error("dump.gd: " + error)
		quit(1)
		return
	quit(0)


func _run() -> String:
	var api_path := _arg_value("--api")
	var out_dir := _arg_value("--out-dir")
	if api_path.is_empty() or out_dir.is_empty():
		return "pass -- --api <absolute path to extension_api.json> --out-dir <absolute folder>"
	var api_text := FileAccess.get_file_as_string(api_path)
	if api_text.is_empty():
		return "cannot read %s (%s)" % [api_path, error_string(FileAccess.get_open_error())]
	var api: Variant = JSON.parse_string(api_text)
	if not api is Dictionary:
		return "%s is not a JSON object" % api_path
	var version := Engine.get_version_info()
	var data := {
		"godot": version.string,
		"globalEnums": _global_enums(api),
		"classes": _classes(_api_property_types(api)),
	}
	var out_path := out_dir.path_join("godot-%d.%d.json" % [version.major, version.minor])
	var file := FileAccess.open(out_path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s (%s)" % [out_path, error_string(FileAccess.get_open_error())]
	if not file.store_string(JSON.stringify(data, "  ", true) + "\n"):
		return "cannot write %s (%s)" % [out_path, error_string(file.get_error())]
	file.close()
	print("dump.gd: wrote %s" % out_path)
	return ""


## Maps class name to property name to the extension API's type string for that property.
##
## A property's own type reads `int` for an enum registered by hint alone, so the type of its
## getter's return value, which carries `enum::` or `bitfield::`, is preferred.
func _api_property_types(api: Dictionary) -> Dictionary:
	var parents := {}
	var getter_types := {}
	for api_class: Dictionary in api.get("classes", []):
		parents[api_class.name] = api_class.get("inherits", "")
		var returns := {}
		for method: Dictionary in api_class.get("methods", []):
			if method.has("return_value"):
				returns[method.name] = method.return_value.type
		getter_types[api_class.name] = returns
	var types := {}
	for api_class: Dictionary in api.get("classes", []):
		var props := {}
		for prop: Dictionary in api_class.get("properties", []):
			var getter: String = prop.get("getter", "")
			var getter_type := _method_return_type(api_class.name, getter, parents, getter_types)
			props[prop.name] = getter_type if _is_enum_type(getter_type) else prop.type
		types[api_class.name] = props
	return types


## The return type of `method` on `class_title` or its nearest ancestor that declares it.
func _method_return_type(
	class_title: String, method: String, parents: Dictionary, getter_types: Dictionary
) -> String:
	var current := class_title
	while not current.is_empty() and not method.is_empty():
		var returns: Dictionary = getter_types.get(current, {})
		if returns.has(method):
			return returns[method]
		current = parents.get(current, "")
	return ""


func _is_enum_type(api_type: String) -> bool:
	return api_type.begins_with(ENUM_PREFIX) or api_type.begins_with(BITFIELD_PREFIX)


func _global_enums(api: Dictionary) -> Dictionary:
	var enums := {}
	for api_enum: Dictionary in api.get("global_enums", []):
		var values := []
		for value: Dictionary in api_enum.get("values", []):
			values.append([value.name, int(value.value)])
		enums[api_enum.name] = {"bitfield": api_enum.get("is_bitfield", false), "values": values}
	return enums


func _classes(api_types: Dictionary) -> Dictionary:
	var classes := {}
	var names := Array(ClassDB.get_class_list())
	names.sort()
	for class_title: String in names:
		classes[class_title] = _class_entry(class_title, api_types.get(class_title, {}))
	return classes


func _class_entry(class_title: String, api_props: Dictionary) -> Dictionary:
	var parent := String(ClassDB.get_parent_class(class_title))
	var instantiable := ClassDB.can_instantiate(class_title)
	var props := []
	for entry: Dictionary in ClassDB.class_get_property_list(class_title, true):
		if entry.usage & SKIPPED_USAGE or String(entry.name).contains("/"):
			continue
		props.append(_property_entry(class_title, entry, instantiable, api_props))
	return {
		"parent": null if parent.is_empty() else parent,
		"instantiable": instantiable,
		"enums": _class_enums(class_title),
		"properties": props,
	}


func _property_entry(
	class_title: String, entry: Dictionary, instantiable: bool, api_props: Dictionary
) -> Dictionary:
	var api_type: String = api_props.get(String(entry.name), "")
	var enum_type: Variant = null
	var bitfield := false
	if api_type.begins_with(ENUM_PREFIX):
		enum_type = api_type.trim_prefix(ENUM_PREFIX)
	elif api_type.begins_with(BITFIELD_PREFIX):
		enum_type = api_type.trim_prefix(BITFIELD_PREFIX)
		bitfield = true
	var prop := {
		"name": String(entry.name),
		"type": type_string(entry.type),
		"enumType": enum_type,
		"bitfield": bitfield,
		"hint": entry.hint,
		"hintString": entry.hint_string,
		"usage": entry.usage,
		"className": String(entry.class_name),
	}
	if instantiable:
		var value: Variant = ClassDB.class_get_property_default_value(class_title, entry.name)
		if not value is Object:
			prop["default"] = var_to_str(value)
	return prop


func _class_enums(class_title: String) -> Dictionary:
	var enums := {}
	for enum_name: String in ClassDB.class_get_enum_list(class_title, true):
		var values := []
		for member: String in ClassDB.class_get_enum_constants(class_title, enum_name, true):
			values.append([member, ClassDB.class_get_integer_constant(class_title, member)])
		enums[enum_name] = {
			"bitfield": ClassDB.is_class_enum_bitfield(class_title, enum_name, true),
			"values": values,
		}
	return enums


func _arg_value(name: String) -> String:
	var args := OS.get_cmdline_user_args()
	var index := args.find(name)
	if index == -1 or index + 1 >= args.size():
		return ""
	return args[index + 1]
