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
	return cases


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
