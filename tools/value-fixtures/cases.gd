extends RefCounted
## The canonical value cases: each value is built by Godot itself, and its expected text is
## `var_to_str(value)`. A case is `[name, value]`; the name is the fixture's file name.

const ESCAPES_QUOTED := "nl\n quote\" backslash\\ tab\t apostrophe'"
const ESCAPES := ESCAPES_QUOTED + " bel\u0007 vt\u000b ff\u000c bs\u0008 eé"
const NON_ASCII := "ellipsis… bullet• astral\U01f600"


static func all() -> Array:
	var cases: Array = []
	cases.append_array(_scalars())
	cases.append_array(_floats())
	cases.append_array(_strings())
	cases.append_array(_real_constructors())
	cases.append_array(_int_constructors())
	cases.append_array(_misc())
	cases.append_array(_arrays())
	cases.append_array(_dictionaries())
	cases.append_array(_packed())
	cases.append_array(_objects())
	return cases


## The cases written by the resource saver with compat off (format=4): each value is saved as
## metadata beside a PackedVector4Array, which turns compat off for the whole file.
static func format4() -> Array:
	var long := PackedByteArray()
	for i in 100:
		long.append((i * 37) % 256)
	return [
		["bytes_empty", PackedByteArray()],
		["bytes_one", PackedByteArray([255])],
		["bytes_two", PackedByteArray([0, 1])],
		["bytes_three", PackedByteArray([1, 2, 3])],
		["bytes_long", long],
		["bytes_in_array", [PackedByteArray([7, 8]), 1]],
		["float_negative_inf", -INF],
		["vector2_negative_inf", Vector2(-INF, 1)],
	]


static func _scalars() -> Array:
	return [
		["null", null],
		["bool_true", true],
		["bool_false", false],
		["int_zero", 0],
		["int_negative", -42],
		["int_max", 9223372036854775807],
		["int_min", -9223372036854775807 - 1],
	]


static func _floats() -> Array:
	return [
		["float_zero", 0.0],
		["float_negative_zero", -0.0],
		["float_one", 1.0],
		["float_tenth", 0.1],
		["float_third", 1.0 / 3.0],
		["float_sum", 0.1 + 0.2],
		["float_1e14", 1e14],
		["float_1e15", 1e15],
		["float_1e20", 1e20],
		["float_1e_4", 0.0001],
		["float_1e_5", 0.00001],
		["float_1_5e_5", 0.000015],
		["float_2p53", 9007199254740992.0],
		["float_big", 123456789012345678.0],
		["float_1e300", 1e300],
		["float_inf", INF],
		["float_negative_inf", -INF],
		["float_nan", NAN],
	]


static func _strings() -> Array:
	return [
		["string_empty", ""],
		["string_plain", "hello"],
		["string_escapes", ESCAPES],
		["string_non_ascii", NON_ASCII],
		["string_name_empty", &""],
		["string_name_plain", &"Master"],
		["string_name_escapes", StringName(ESCAPES_QUOTED + " ff\u000c bs\u0008 cr\r")],
		# c_escape writes \a and \v, which the tokenizer reads back as the letters a and v.
		["string_name_bel_vt", StringName("bel\u0007 vt\u000b")],
		["string_name_non_ascii", StringName(NON_ASCII)],
		["node_path_empty", NodePath("")],
		["node_path_parent", NodePath("..")],
		["node_path_property", NodePath("Child/Grandchild:position:x")],
		["node_path_escapes", NodePath("a b/c\"d'e\\f")],
	]


static func _real_constructors() -> Array:
	return [
		["vector2", Vector2(0.1, -2.5)],
		["vector2_zero", Vector2(0, 0)],
		["vector2_inf", Vector2(INF, -INF)],
		["vector2_nan", Vector2(NAN, 3)],
		["vector3", Vector3(1, 2.25, -1e-7)],
		["vector4", Vector4(1e20, 0.3, 1.0 / 3.0, -0.0)],
		["rect2", Rect2(1.5, 2, 640, 480)],
		["plane", Plane(0, 1, 0, 0.5)],
		["quaternion", Quaternion(0, 0.7071068, 0, 0.7071068)],
		["aabb", AABB(Vector3(-1, -1, -1), Vector3(2, 2, 2))],
		["transform2d", Transform2D(0.5, Vector2(10, 20))],
		["basis", Basis(Vector3(0, 1, 0), 0.25)],
		["transform3d", Transform3D(Basis(Vector3(1, 0, 0), 0.5), Vector3(1, 2, 3))],
		["projection", Projection.create_perspective(70, 1.5, 0.05, 4000)],
		["color", Color(1, 0.5, 0.25, 0.1)],
		["color_html", Color.html("#3366cc80")],
	]


static func _int_constructors() -> Array:
	return [
		["vector2i", Vector2i(1, -2)],
		["vector3i", Vector3i(2147483647, -2147483648, 0)],
		["vector4i", Vector4i(1, 2, 3, 4)],
		["rect2i", Rect2i(0, 0, 1920, 1080)],
	]


static func _misc() -> Array:
	return [
		["rid_empty", RID()],
		["rid_id", rid_from_int64(12345)],
		["signal_empty", Signal()],
		["callable_empty", Callable()],
	]


static func _arrays() -> Array:
	return [
		["array_empty", []],
		["array_mixed", [null, true, 1, 1.0, 2.5, "a", &"b", Vector2(1, 2)]],
		["array_nested", [[], [1, [2.0]], {}, {"k": [3]}]],
		["array_typed_int", Array([1, 2], TYPE_INT, &"", null)],
		["array_typed_float", Array([1.0, 0.5], TYPE_FLOAT, &"", null)],
		["array_typed_string", Array(["' '", '" "'], TYPE_STRING, &"", null)],
		["array_typed_empty", Array([], TYPE_RID, &"", null)],
		["array_typed_packed", Array([PackedVector2Array()], TYPE_PACKED_VECTOR2_ARRAY, &"", null)],
		["array_typed_class", Array([], TYPE_OBJECT, &"Material", null)],
		["array_typed_resource", Array([], TYPE_OBJECT, &"Resource", null)],
	]


static func _dictionaries() -> Array:
	return [
		["dictionary_empty", {}],
		["dictionary_one", {"a": 1}],
		["dictionary_string_keys", {"{": 1, "(": 2, "'": 3, '"': 4, "[": 5, "B": 6, "a": 7}],
		["dictionary_code_points", {"": 1, "\U01f600": 2, "z": 3, "é": 4}],
		["dictionary_mixed_keys", {"s": 1, &"n": 2, 2.5: 3, 1: 4, -1: 5, 0.5: 6, &"t": 7}],
		["dictionary_bool_keys", {true: 1, false: 2}],
		["dictionary_vector2_keys", {Vector2(1, 2): 1, Vector2(1, -1): 2, Vector2(0, 5): 3}],
		["dictionary_vector2i_keys", {Vector2i(1, 2): 1, Vector2i(0, 9): 2}],
		["dictionary_nested", {"b": {"d": [1, {}], "c": PackedStringArray()}, "a": [{"x": 5.0}]}],
		["dictionary_values", {"f": 1.0, "n": null, "s": "line1\nline2", "v": Vector3(1, 2, 3)}],
		[
			"dictionary_typed",
			Dictionary({"b": 2, "a": 1}, TYPE_STRING, &"", null, TYPE_INT, &"", null)
		],
		[
			"dictionary_typed_variant_value",
			Dictionary({}, TYPE_STRING, &"", null, TYPE_NIL, &"", null),
		],
		[
			"dictionary_typed_variant_key",
			Dictionary({1: "a"}, TYPE_NIL, &"", null, TYPE_STRING, &"", null),
		],
		[
			"dictionary_typed_class",
			Dictionary({}, TYPE_STRING_NAME, &"", null, TYPE_OBJECT, &"Texture2D", null),
		],
	]


static func _packed() -> Array:
	return [
		["packed_byte_empty", PackedByteArray()],
		["packed_byte", PackedByteArray([0, 1, 127, 128, 255])],
		["packed_int32_empty", PackedInt32Array()],
		["packed_int32", PackedInt32Array([-2147483648, 0, 2147483647])],
		["packed_int64_empty", PackedInt64Array()],
		["packed_int64", PackedInt64Array([-9223372036854775807 - 1, 0, 9223372036854775807])],
		["packed_float32_empty", PackedFloat32Array()],
		["packed_float32", PackedFloat32Array([0.1, 1, -2.5, 1e20, INF, -INF, NAN])],
		["packed_float64_empty", PackedFloat64Array()],
		["packed_float64", PackedFloat64Array([0.1, 1, 1.0 / 3.0, 1e300, -INF])],
		["packed_string_empty", PackedStringArray()],
		["packed_string", PackedStringArray(["", "a", ESCAPES_QUOTED, NON_ASCII])],
		["packed_vector2_empty", PackedVector2Array()],
		["packed_vector2", PackedVector2Array([Vector2(0, 0), Vector2(0.1, -1), Vector2(1, 1)])],
		["packed_vector3_empty", PackedVector3Array()],
		["packed_vector3", PackedVector3Array([Vector3(1, 2, 3), Vector3(-0.5, 0, 1e-7)])],
		["packed_color_empty", PackedColorArray()],
		["packed_color", PackedColorArray([Color(1, 0, 0, 1), Color(0.1, 0.2, 0.3, 0.4)])],
		["packed_vector4_empty", PackedVector4Array()],
		["packed_vector4", PackedVector4Array([Vector4(1, 2, 3, 4), Vector4(0.5, 0, -1, 1e20)])],
	]


static func _objects() -> Array:
	return [
		["object_ref_counted", RefCounted.new()],
		["object_resource", Resource.new()],
	]
