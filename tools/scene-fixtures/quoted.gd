extends Resource
## A resource whose stored property names property_name_encode must quote (ustring.cpp:5053-5064):
## a space, '=', '"', ';', '[', ']' or a character above 126.

var _values := {"with space": 1, "key=value": "x", 'say "hi"': 2, "naïve": true}


func _get_property_list() -> Array[Dictionary]:
	var properties: Array[Dictionary] = []
	for key: String in _values:
		properties.append(
			{"name": key, "type": typeof(_values[key]), "usage": PROPERTY_USAGE_STORAGE}
		)
	return properties


func _get(property: StringName) -> Variant:
	return _values.get(String(property))


func _set(property: StringName, value: Variant) -> bool:
	if not _values.has(String(property)):
		return false
	_values[String(property)] = value
	return true
