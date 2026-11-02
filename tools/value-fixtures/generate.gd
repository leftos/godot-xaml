extends SceneTree
## Writes the value fixtures: Godot's own text for values, loose inputs and float corpora.
##
## Run: godot --headless --path tools/value-fixtures --script res://generate.gd --
##     --out-dir <absolute tests/fixtures/values folder>
## canonical/<case>.txt is var_to_str of a value cases.gd builds; loose/<case>.in.txt is a text
## from inputs.txt and loose/<case>.out.txt is var_to_str(str_to_var(text)); floats/ holds one
## `<hex bits>\t<text>` line per double or float32; format4/<case>.txt is the text the resource
## saver writes for a value with compat off. Every file is LF with no final newline.

const Cases := preload("res://cases.gd")
const FOLDERS: Array[String] = ["canonical", "loose", "floats", "format4"]
const FORMAT4_PATH := "user://format4.tres"
const META_PREFIX := "metadata/v = "
const ERROR_PREFIX := "expects_error_"
const NULL_PREFIX := "null_"
const RANDOM_COUNT := 2500
const SEED := 0x2545F4914F6CDD1D

var _state: int = SEED
var _written: int = 0


func _initialize() -> void:
	var error := _run()
	if not error.is_empty():
		push_error("generate.gd: " + error)
		quit(1)
		return
	print("value-fixtures: wrote %d files" % _written)
	quit(0)


func _run() -> String:
	var out_dir := _arg_value("--out-dir")
	if out_dir.is_empty():
		return "pass -- --out-dir <absolute folder>"
	if OS.has_feature("double"):
		return "this is a double-precision build; the fixtures need real_t = float"
	for folder in FOLDERS:
		var error := _clear(out_dir.path_join(folder))
		if not error.is_empty():
			return error
	var steps: Array[String] = [
		_write_canonical(out_dir.path_join("canonical")),
		_write_loose(out_dir.path_join("loose")),
		_write_floats(out_dir.path_join("floats")),
		_write_format4(out_dir.path_join("format4")),
	]
	for error in steps:
		if not error.is_empty():
			return error
	return ""


func _clear(folder: String) -> String:
	var made := DirAccess.make_dir_recursive_absolute(folder)
	if made != OK:
		return "cannot create %s (%s)" % [folder, error_string(made)]
	for file in DirAccess.get_files_at(folder):
		var removed := DirAccess.remove_absolute(folder.path_join(file))
		if removed != OK:
			return "cannot remove %s (%s)" % [file, error_string(removed)]
	return ""


func _write_canonical(folder: String) -> String:
	for entry: Array in Cases.all():
		var error := _write(folder.path_join(entry[0] + ".txt"), var_to_str(entry[1]))
		if not error.is_empty():
			return error
	return ""


func _write_loose(folder: String) -> String:
	var text := FileAccess.get_file_as_string("res://inputs.txt")
	if text.is_empty():
		return "cannot read res://inputs.txt (%s)" % error_string(FileAccess.get_open_error())
	for line in text.split("\n"):
		if line.is_empty() or line.begins_with("#"):
			continue
		var tab := line.find("\t")
		if tab == -1:
			return "inputs.txt line has no tab: %s" % line
		var error := _write_loose_case(folder, line.left(tab), line.substr(tab + 1))
		if not error.is_empty():
			return error
	return ""


func _write_loose_case(folder: String, case_name: String, raw: String) -> String:
	var input := raw.replace("<LF>", "\n")
	var value: Variant = str_to_var(input)
	var error := _write(folder.path_join(case_name + ".in.txt"), input)
	if not error.is_empty():
		return error
	if case_name.begins_with(ERROR_PREFIX):
		if value != null:
			return (
				"%s parsed to %s; an expects_error_ case must fail" % [case_name, var_to_str(value)]
			)
		return ""
	if value == null and not case_name.begins_with(NULL_PREFIX):
		return "str_to_var returned null for %s: %s" % [case_name, input]
	return _write(folder.path_join(case_name + ".out.txt"), var_to_str(value))


func _write_floats(folder: String) -> String:
	var doubles := PackedStringArray()
	for bits in _double_patterns():
		doubles.append(_hex64(bits) + "\t" + var_to_str(_double_from_bits(bits)))
	var error := _write(folder.path_join("doubles.txt"), "\n".join(doubles))
	if not error.is_empty():
		return error
	var singles := PackedStringArray()
	for bits in _float32_patterns():
		var text := var_to_str(Vector2(_float32_from_bits(bits), 0))
		singles.append("%08x" % bits + "\t" + text.substr(8, text.find(", ") - 8))
	return _write(folder.path_join("float32.txt"), "\n".join(singles))


func _write_format4(folder: String) -> String:
	for entry: Array in Cases.format4():
		var text := _saved_text(entry[1])
		if text.begins_with("!"):
			return "%s: %s" % [entry[0], text.substr(1)]
		var error := _write(folder.path_join(entry[0] + ".txt"), text)
		if not error.is_empty():
			return error
	return ""


## The text after `metadata/v = ` in a .tres holding the value, or `!` and an error. The
## PackedVector4Array beside it turns compat off (resource_format_text.cpp:1742-1744).
func _saved_text(value: Variant) -> String:
	var resource := Resource.new()
	resource.set_meta("v", value)
	resource.set_meta("w", PackedVector4Array())
	var saved := ResourceSaver.save(resource, FORMAT4_PATH)
	if saved != OK:
		return "!cannot save %s (%s)" % [FORMAT4_PATH, error_string(saved)]
	var text := FileAccess.get_file_as_string(FORMAT4_PATH)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(FORMAT4_PATH))
	if not text.begins_with("[gd_resource") or text.find(" format=4") == -1:
		return "!the saved file is not format=4: %s" % text.get_slice("\n", 0)
	var start := text.find("\n" + META_PREFIX)
	if start == -1:
		return "!no %s line in the saved file" % META_PREFIX
	start += 1 + META_PREFIX.length()
	var end := text.find("\nmetadata/", start)
	if end == -1:
		end = text.length() - 1
	return text.substr(start, end - start)


func _double_patterns() -> Array[int]:
	var patterns: Array[int] = [
		0,
		1 << 63,
		1,
		0x000FFFFFFFFFFFFF,
		0x0008000000000000,
		0x0010000000000000,
		0x7FEFFFFFFFFFFFFF,
		0x7FF0000000000000,
		-0x0010000000000000,
		0x7FF8000000000000,
	]
	var anchors: Array[float] = [
		9007199254740991.0,
		9007199254740992.0,
		9007199254740994.0,
		3.4028234663852886e38,
		1.0 / 3.0,
		0.1 + 0.2,
		123456789.0,
		-1.5,
		-0.0001,
		-1e15,
	]
	for power in range(-8, 23):
		anchors.append(("1e%d" % power).to_float())
	for anchor in anchors:
		var bits := _bits_of_double(anchor)
		patterns.append_array([bits - 1, bits, bits + 1])
	for i in RANDOM_COUNT:
		patterns.append(_next())
	for i in RANDOM_COUNT:
		var r := _next()
		var exponent := 1003 + ((r >> 1) & 0x7FFFFFFF) % 74
		var bits := (r & 0x000FFFFFFFFFFFFF) | (exponent << 52)
		patterns.append(bits | (1 << 63) if r < 0 else bits)
	return patterns


func _float32_patterns() -> Array[int]:
	var patterns: Array[int] = [
		0,
		0x80000000,
		1,
		0x007FFFFF,
		0x00800000,
		0x7F7FFFFF,
		0x7F800000,
		0xFF800000,
		0x7FC00000,
	]
	var anchors: Array[float] = [16777215.0, 16777216.0, 1.0 / 3.0, 0.1, -1.5, 1e15, 2e-5]
	for power in range(-8, 23):
		anchors.append(("1e%d" % power).to_float())
	for anchor in anchors:
		var bits := _bits_of_float32(anchor)
		patterns.append_array([bits - 1, bits, bits + 1])
	for i in RANDOM_COUNT:
		patterns.append(_next() & 0xFFFFFFFF)
	for i in RANDOM_COUNT:
		var r := _next()
		var exponent := 107 + ((r >> 33) & 0x7FFFFFFF) % 74
		patterns.append((r & 0x807FFFFF) | (exponent << 23))
	return patterns


## xorshift64 with a fixed seed, so every run writes the same corpus.
func _next() -> int:
	_state ^= _state << 13
	_state ^= (_state >> 7) & 0x01FFFFFFFFFFFFFF
	_state ^= _state << 17
	return _state


func _double_from_bits(bits: int) -> float:
	var bytes := PackedByteArray()
	bytes.resize(8)
	bytes.encode_s64(0, bits)
	return bytes.decode_double(0)


func _bits_of_double(value: float) -> int:
	var bytes := PackedByteArray()
	bytes.resize(8)
	bytes.encode_double(0, value)
	return bytes.decode_s64(0)


func _float32_from_bits(bits: int) -> float:
	var bytes := PackedByteArray()
	bytes.resize(4)
	bytes.encode_u32(0, bits)
	return bytes.decode_float(0)


func _bits_of_float32(value: float) -> int:
	var bytes := PackedByteArray()
	bytes.resize(4)
	bytes.encode_float(0, value)
	return bytes.decode_u32(0)


func _hex64(bits: int) -> String:
	return "%08x%08x" % [(bits >> 32) & 0xFFFFFFFF, bits & 0xFFFFFFFF]


func _write(path: String, text: String) -> String:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return "cannot write %s (%s)" % [path, error_string(FileAccess.get_open_error())]
	if not file.store_string(text):
		return "cannot write %s (%s)" % [path, error_string(file.get_error())]
	file.close()
	_written += 1
	return ""


func _arg_value(arg_name: String) -> String:
	var args := OS.get_cmdline_user_args()
	var index := args.find(arg_name)
	if index == -1 or index + 1 >= args.size():
		return ""
	return args[index + 1]
