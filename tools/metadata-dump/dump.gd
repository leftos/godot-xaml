extends SceneTree
## Writes the engine's class metadata to <out-dir>/godot-<major>.<minor>.json.
##
## Run: godot --headless --path tools/metadata-dump --script res://dump.gd --
##     --api <absolute path to extension_api.json> --out-dir <absolute folder>
## ClassDB gives the classes, their own properties, defaults and enums; the extension API dump
## gives each property's enum or bitfield type, its getter's return type, and the global enums.

const SKIPPED_USAGE := PROPERTY_USAGE_CATEGORY | PROPERTY_USAGE_GROUP | PROPERTY_USAGE_SUBGROUP
const ENUM_PREFIX := "enum::"
const BITFIELD_PREFIX := "bitfield::"
const TYPED_ARRAY_PREFIX := "typedarray::"
const TYPED_DICTIONARY_PREFIX := "typeddictionary::"
const THEME_OVERRIDE_PREFIX := "theme_override_"

var _instantiate_failures := PackedStringArray()
var _own_defaults := {}
var _default_overrides := {}
var _has_defaults := {}


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
	var error_log := DumpErrorLog.new()
	OS.add_logger(error_log)
	var error := _dump(api, out_dir, error_log)
	OS.remove_logger(error_log)
	return error


## Reads ClassDB into the dump and writes it under `out_dir`, failing when Godot logs any error.
func _dump(api: Dictionary, out_dir: String, error_log: DumpErrorLog) -> String:
	var version := Engine.get_version_info()
	var data := {
		"godot": version.string,
		"globalEnums": _global_enums(api),
		"classes": _classes(_api_property_types(api)),
	}
	if not _instantiate_failures.is_empty():
		return _instantiate_error()
	var logged := error_log.failure()
	if not logged.is_empty():
		return logged
	var out_path := out_dir.path_join("godot-%d.%d.json" % [version.major, version.minor])
	var write_error := _write_metadata(out_path, data)
	if not write_error.is_empty():
		return write_error
	print("dump.gd: wrote %s" % out_path)
	return ""


func _instantiate_error() -> String:
	return "cannot instantiate %s to read its theme overrides" % ", ".join(_instantiate_failures)


## Writes `data` to `path` as two-space, key-sorted JSON ending in a line feed.
func _write_metadata(path: String, data: Dictionary) -> String:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s (%s)" % [path, error_string(FileAccess.get_open_error())]
	var stored := file.store_string(JSON.stringify(data, "  ", true) + "\n")
	if not stored:
		var write_error := "cannot write %s (%s)" % [path, error_string(file.get_error())]
		file.close()
		return write_error
	file.close()
	return ""


## Maps class name to property name to `{"type": <API type>, "getter": <getter's API type>}`.
##
## `type` is the extension API's type string for the property. A property's own type reads `int`
## for an enum registered by hint alone, so the type of its getter's return value, which carries
## `enum::` or `bitfield::`, is preferred. `getter` is the API type the getter returns, or "" when
## the extension API has no such getter.
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
			props[prop.name] = {
				"type": getter_type if _is_enum_type(getter_type) else prop.type,
				"getter": getter_type,
			}
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


## The Variant type name, as `type_string` gives it, that the getter of `property` returns, read
## from ClassDB for a getter the extension API leaves out (`Curve._get_data`): the `return` entry
## of the method on `class_title` or its nearest ancestor that declares it. "" for no getter or a
## getter returning Nil or Variant.
func _classdb_getter_type(class_title: String, property: String) -> String:
	var getter := String(ClassDB.class_get_property_getter(class_title, property))
	var current := class_title
	while not current.is_empty() and not getter.is_empty():
		for method: Dictionary in ClassDB.class_get_method_list(current, true):
			if String(method.name) != getter:
				continue
			var returned: Dictionary = method.get("return", {})
			var variant_type: int = returned.get("type", TYPE_NIL)
			return "" if variant_type == TYPE_NIL else type_string(variant_type)
		current = String(ClassDB.get_parent_class(current))
	return ""


func _is_enum_type(api_type: String) -> bool:
	return api_type.begins_with(ENUM_PREFIX) or api_type.begins_with(BITFIELD_PREFIX)


## The Variant type name, as `type_string` gives it, of a value of the extension API type
## `api_type`; "" for one that names none: no getter, an enum or bitfield (typed by `enumType`),
## or `Variant`. A typed array or dictionary is an Array or Dictionary, a class an Object.
func _getter_variant_type(api_type: String) -> String:
	if api_type.is_empty() or _is_enum_type(api_type):
		return ""
	if api_type.begins_with(TYPED_ARRAY_PREFIX):
		return type_string(TYPE_ARRAY)
	if api_type.begins_with(TYPED_DICTIONARY_PREFIX):
		return type_string(TYPE_DICTIONARY)
	if ClassDB.class_exists(api_type):
		return type_string(TYPE_OBJECT)
	for variant_type in range(1, TYPE_MAX):
		if type_string(variant_type) == api_type:
			return api_type
	return ""


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


## Whether a property list entry is one the dump leaves out.
func _is_skipped(entry: Dictionary) -> bool:
	return (entry.usage & SKIPPED_USAGE) != 0 or String(entry.name).contains("/")


func _class_entry(class_title: String, api_props: Dictionary) -> Dictionary:
	var parent := String(ClassDB.get_parent_class(class_title))
	var instantiable := ClassDB.can_instantiate(class_title)
	var props := []
	var list_order := []
	var has_slash_name := false
	for entry: Dictionary in ClassDB.class_get_property_list(class_title, true):
		if (entry.usage & SKIPPED_USAGE) != 0:
			continue
		var name := String(entry.name)
		list_order.append(name)
		if name.contains("/"):
			has_slash_name = true
		if _is_skipped(entry):
			continue
		props.append(_property_entry(class_title, entry, api_props))
	var class_entry := {
		"parent": null if parent.is_empty() else parent,
		"instantiable": instantiable,
		"enums": _class_enums(class_title),
		"properties": props,
		"signals": _signal_entries(class_title),
	}
	# Slash-named names stay out of `properties`; a class holding any lists all its names in
	# ClassDB order, for property order only.
	if has_slash_name:
		class_entry["listOrder"] = list_order
	if not _class_has_defaults(class_title):
		class_entry["hasDefaults"] = false
	if _has_theme_overrides(class_title):
		class_entry["themeOverrides"] = _theme_override_entries(class_title)
	var overrides := _class_overrides(class_title)
	if not overrides.is_empty():
		class_entry["defaultOverrides"] = overrides
	return class_entry


## Whether `class_title` exposes theme-override properties, which only Control and Window do.
func _has_theme_overrides(class_title: String) -> bool:
	if not ClassDB.can_instantiate(class_title):
		return false
	return (
		ClassDB.is_parent_class(class_title, "Control")
		or ClassDB.is_parent_class(class_title, "Window")
	)


## The theme-override properties an instance of `class_title` exposes, inherited items included.
##
## These are instance properties: ClassDB's class property list does not carry them.
func _theme_override_entries(class_title: String) -> Array:
	var instance: Object = ClassDB.instantiate(class_title)
	if instance == null:
		_instantiate_failures.append(class_title)
		return []
	var entries := []
	for entry: Dictionary in instance.get_property_list():
		var name := String(entry.name)
		if not name.begins_with(THEME_OVERRIDE_PREFIX) or not name.contains("/"):
			continue
		var override := {
			"name": name,
			"type": type_string(entry.type),
			"hint": entry.hint,
			"hintString": entry.hint_string,
		}
		entries.append(override)
	instance.free()
	return entries


func _property_entry(class_title: String, entry: Dictionary, api_props: Dictionary) -> Dictionary:
	var api_prop: Dictionary = api_props.get(String(entry.name), {})
	var api_type: String = api_prop.get("type", "")
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
	# Godot saves the getter's value, so a getter type the registration misstates is recorded.
	var api_getter: String = api_prop.get("getter", "")
	var getter_type := (
		_getter_variant_type(api_getter)
		if not api_getter.is_empty()
		else _classdb_getter_type(class_title, String(entry.name))
	)
	if not getter_type.is_empty() and getter_type != prop.type:
		prop["getterType"] = getter_type
	var text: Variant = _class_own_defaults(class_title).get(String(entry.name))
	if text != null:
		prop["default"] = text
	return prop


## The default of each property `class_title` declares itself, as Godot value text, or null for a
## property with no default: a class that cannot be instantiated, or a value Godot has none for.
func _class_own_defaults(class_title: String) -> Dictionary:
	if _own_defaults.has(class_title):
		return _own_defaults[class_title]
	var instantiable := ClassDB.can_instantiate(class_title)
	var defaults := {}
	for entry: Dictionary in ClassDB.class_get_property_list(class_title, true):
		if _is_skipped(entry):
			continue
		defaults[String(entry.name)] = _default_text(class_title, entry) if instantiable else null
	_own_defaults[class_title] = defaults
	return defaults


## Whether Godot reads a default for any of the class's properties.
##
## ClassDB never instantiates a class it marks virtual to read defaults, so every property of such a
## class reports no value, and Godot writes every stored property of a node of it. A class ClassDB
## cannot instantiate has no defaults to lose, and is not marked.
func _class_has_defaults(class_title: String) -> bool:
	if _has_defaults.has(class_title):
		return _has_defaults[class_title]
	var has_defaults := true
	if ClassDB.can_instantiate(class_title):
		has_defaults = false
		for entry: Dictionary in ClassDB.class_get_property_list(class_title, false):
			if _is_skipped(entry) or entry.type == TYPE_OBJECT or entry.type == TYPE_NIL:
				continue
			if _default_text(class_title, entry) != null:
				has_defaults = true
				break
	_has_defaults[class_title] = has_defaults
	return has_defaults


## The defaults `class_title` gives the properties it inherits, by property name.
##
## A property is listed only when Godot's value for an instance of the class differs from the
## class's parent's default for it, which includes a parent that has none.
func _class_overrides(class_title: String) -> Dictionary:
	if _default_overrides.has(class_title):
		return _default_overrides[class_title]
	var overrides := {}
	_default_overrides[class_title] = overrides
	if not ClassDB.can_instantiate(class_title):
		return overrides
	var own := _class_own_defaults(class_title)
	var parent := String(ClassDB.get_parent_class(class_title))
	for entry: Dictionary in ClassDB.class_get_property_list(class_title, false):
		var name := String(entry.name)
		if _is_skipped(entry) or own.has(name):
			continue
		var text: Variant = _default_text(class_title, entry)
		if text != null and text != _effective_default(parent, name):
			overrides[name] = text
	return overrides


## The default of `name` as Godot reads it for `class_title`, or null when it has none.
##
## A class Godot keeps no defaults for has none for any property; otherwise a class that declares
## the property uses its own declaration, else its override applies, else its parent's default.
func _effective_default(class_title: String, name: String) -> Variant:
	if class_title.is_empty() or not _class_has_defaults(class_title):
		return null
	var own := _class_own_defaults(class_title)
	if own.has(name):
		return own[name]
	var overrides := _class_overrides(class_title)
	if overrides.has(name):
		return overrides[name]
	return _effective_default(String(ClassDB.get_parent_class(class_title)), name)


## The default of `entry` on an instance of `class_title`, as Godot value text, or null when
## Godot has none.
##
## Godot answers with no value as null, which only an Object or Nil property can hold for real; a
## non-null Object default is left out too.
func _default_text(class_title: String, entry: Dictionary) -> Variant:
	# OpenXRRenderModelManager logs "render_model_extension is null" on each notification it gets
	# outside a tree (openxr_render_model_manager.cpp, _notification), and ClassDB builds and frees
	# one to read its defaults. The notifications it acts on never reach that code, so the error is
	# harmless and is muted for this class alone.
	var muted := class_title == "OpenXRRenderModelManager"
	var print_error_messages := Engine.print_error_messages
	if muted:
		Engine.print_error_messages = false
	var value: Variant = ClassDB.class_get_property_default_value(class_title, entry.name)
	if muted:
		Engine.print_error_messages = print_error_messages
	if value is Object or (value == null and entry.type != TYPE_OBJECT and entry.type != TYPE_NIL):
		return null
	return var_to_str(value)


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


## The signals `class_title` declares itself, in ClassDB order.
##
## Inherited signals are left out, as are inherited properties.
func _signal_entries(class_title: String) -> Array:
	var entries := []
	for signal_info: Dictionary in ClassDB.class_get_signal_list(class_title, true):
		var args := []
		for arg: Dictionary in signal_info.args:
			args.append(_signal_arg(arg))
		entries.append({"name": String(signal_info.name), "args": args})
	return entries


func _signal_arg(arg: Dictionary) -> Dictionary:
	return {
		"name": String(arg.name),
		"type": type_string(arg.type),
		"className": String(arg.class_name),
	}


func _arg_value(name: String) -> String:
	var args := OS.get_cmdline_user_args()
	var index := args.find(name)
	if index == -1 or index + 1 >= args.size():
		return ""
	return args[index + 1]


## Records the errors Godot logs while the dump reads ClassDB, so a run fails on any of them.
##
## A logger is called from other threads, possibly at once, so the entries are kept under a mutex.
class DumpErrorLog:
	extends Logger
	const SHOWN := 5

	var _mutex := Mutex.new()
	var _entries := PackedStringArray()

	func _log_error(
		_function: String,
		file: String,
		line: int,
		code: String,
		rationale: String,
		_editor_notify: bool,
		_error_type: int,
		_script_backtraces: Array[ScriptBacktrace]
	) -> void:
		var detail := rationale if not rationale.is_empty() else code
		_mutex.lock()
		_entries.append("%s:%d: %s" % [file, line, detail])
		_mutex.unlock()

	## The failure message for what Godot logged, or "" when it logged nothing.
	func failure() -> String:
		_mutex.lock()
		var entries := _entries.duplicate()
		_mutex.unlock()
		if entries.is_empty():
			return ""
		return (
			"Godot logged %d error(s) during the dump: %s"
			% [entries.size(), "; ".join(entries.slice(0, SHOWN))]
		)
