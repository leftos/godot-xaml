extends Resource

@export var count: int = 0:
	set(value):
		count = value
		CheckHandoff.new().Take(self)
