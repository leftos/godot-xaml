@tool
extends Control


static func _static_init() -> void:
	Engine.set_meta("gx_e14_static_init", true)


func _init() -> void:
	Engine.set_meta("gx_e14_init", true)
