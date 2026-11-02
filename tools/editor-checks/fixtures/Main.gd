extends Control


func _ready() -> void:
	print("XSCN MAIN READY ", $Title.text)
	var ready_file := FileAccess.open("user://main_ready.txt", FileAccess.WRITE)
	if ready_file == null:
		push_error(
			"cannot write user://main_ready.txt: %s" % error_string(FileAccess.get_open_error())
		)
		return
	ready_file.store_string($Title.text)
