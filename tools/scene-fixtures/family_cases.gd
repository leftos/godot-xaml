extends RefCounted
## The dynamic-family cases, which generate.gd calls.
##
## Each case is a static function that takes the generator and builds its scene or resource
## through the generator's helpers (_root, _add, _save_scene, _save), returning an error or "".


## A Skeleton3D of a 12-bone chain whose PhysicalBoneSimulator3D holds a PhysicalBone3D per bone,
## each with its bone_name and a joint type cycling through pin, cone, hinge, slider and 6DOF, with
## joint_constraints/ keys off their defaults; beside it a SoftBody3D on a subdivided BoxMesh with
## 12 pinned points, some attached to a node and some with an offset. joint_type creates the
## JointData that PhysicalBone3D::_set forwards joint_constraints/ to, so it is set first; the key
## names are each JointData's _get_property_list (physical_bone_3d.cpp). The SoftBody3D's points
## are pinned before the attachment nodes exist, since SoftBody3D::_add_pinned_point reads a found
## node's global transform, which a node outside the tree does not have (soft_body_3d.cpp).
static func physics_bones(generator) -> String:
	var root: Node3D = generator._root("Node3D")
	var skeleton: Skeleton3D = generator._add(root, "Skeleton3D", "Skeleton3D")
	for index in 12:
		skeleton.add_bone("bone_%d" % index)
		skeleton.set_bone_parent(index, index - 1)
		skeleton.set_bone_rest(index, Transform3D(Basis(), Vector3(0, 0.25 * (index + 1), 0)))
	var simulator: Node = generator._add(
		skeleton, "PhysicalBoneSimulator3D", "PhysicalBoneSimulator3D"
	)
	for index in 12:
		var bone: PhysicalBone3D = generator._add(simulator, "Physical%d" % index, "PhysicalBone3D")
		bone.set("bone_name", "bone_%d" % index)
		var joint_type := index % 5 + 1
		bone.set("joint_type", joint_type)
		var amount := 0.1 * (index + 1)
		match joint_type:
			PhysicalBone3D.JOINT_TYPE_PIN:
				bone.set("joint_constraints/bias", amount)
				bone.set("joint_constraints/damping", 2.0)
				bone.set("joint_constraints/impulse_clamp", 4.0)
			PhysicalBone3D.JOINT_TYPE_CONE:
				bone.set("joint_constraints/swing_span", 30.0)
				bone.set("joint_constraints/twist_span", 90.0)
				bone.set("joint_constraints/softness", amount)
			PhysicalBone3D.JOINT_TYPE_HINGE:
				bone.set("joint_constraints/angular_limit_enabled", true)
				bone.set("joint_constraints/angular_limit_upper", 45.0)
				bone.set("joint_constraints/angular_limit_lower", -45.0)
				bone.set("joint_constraints/angular_limit_relaxation", 2.0)
			PhysicalBone3D.JOINT_TYPE_SLIDER:
				bone.set("joint_constraints/linear_limit_upper", 2.0)
				bone.set("joint_constraints/linear_limit_lower", -2.0)
				bone.set("joint_constraints/angular_limit_damping", amount)
			PhysicalBone3D.JOINT_TYPE_6DOF:
				bone.set("joint_constraints/x/linear_limit_enabled", false)
				bone.set("joint_constraints/y/linear_spring_enabled", true)
				bone.set("joint_constraints/y/linear_spring_stiffness", 5.0)
				bone.set("joint_constraints/z/angular_limit_upper", 30.0)
				bone.set("joint_constraints/z/erp", amount)

	var soft_body: SoftBody3D = generator._add(root, "SoftBody3D", "SoftBody3D")
	var box := BoxMesh.new()
	box.subdivide_width = 2
	box.subdivide_height = 2
	box.subdivide_depth = 2
	soft_body.mesh = box
	for index in 12:
		var attachment := NodePath("../Anchor%d" % (index % 3)) if index % 4 == 0 else NodePath()
		soft_body.set_point_pinned(2 * index + 1, true, attachment)
		if index % 3 == 1:
			soft_body.set("attachments/%d/offset" % index, Vector3(0.5, index, -0.5))
	for index in 3:
		generator._add(root, "Anchor%d" % index, "Node3D")
	return generator._save_scene("physics_bones", root)


## A TileSet with 12 of each layer kind (occlusion, physics, terrain sets of two or three
## terrains, navigation, custom data of six types), each off its defaults, and 12 sources at ids
## 0 to 11: every third a TileSetScenesCollectionSource with scene tiles at ids 1, 10 and 12, the
## rest a TileSetAtlasSource on a 64 px GradientTexture2D (a 4x4 grid of 16 px regions) holding a
## tile with alternatives 1 and 10, an animated tile and a 2x1 tile, with per-tile data on two
## layers of each kind; then source, coords and alternative tile proxies. The layers come before
## the sources, since a TileData sizes its layer data from its TileSet, and each atlas has its
## texture before its tiles, since TileSetAtlasSource::has_room_for_tile refuses a tile outside
## the texture's grid (tile_set.cpp). Key names: TileSet, TileSetAtlasSource,
## TileSetScenesCollectionSource and TileData::_get_property_list (tile_set.cpp).
static func tile_set_layers(generator) -> String:
	var tile_set := TileSet.new()
	var material := PhysicsMaterial.new()
	material.friction = 0.25
	var custom_types := [TYPE_INT, TYPE_STRING, TYPE_FLOAT, TYPE_COLOR, TYPE_BOOL, TYPE_VECTOR2]
	for index in 12:
		tile_set.add_occlusion_layer()
		tile_set.set_occlusion_layer_light_mask(index, 2 << index)
		tile_set.set_occlusion_layer_sdf_collision(index, index % 2 == 1)
		tile_set.add_physics_layer()
		tile_set.set_physics_layer_collision_layer(index, 2 << index)
		tile_set.set_physics_layer_collision_mask(index, (1 << index) | 4)
		tile_set.set_physics_layer_collision_priority(index, 2.0 + index)
		if index % 2 == 0:
			tile_set.set_physics_layer_physics_material(index, material)
		tile_set.add_terrain_set()
		tile_set.set_terrain_set_mode(index, index % 3)
		for terrain in 2 + index % 2:
			tile_set.add_terrain(index)
			tile_set.set_terrain_name(index, terrain, "terrain_%d_%d" % [index, terrain])
			tile_set.set_terrain_color(index, terrain, Color(0.1 * terrain, 0.05 * index, 0.5))
		tile_set.add_navigation_layer()
		tile_set.set_navigation_layer_layers(index, 2 << index)
		tile_set.add_custom_data_layer()
		tile_set.set_custom_data_layer_name(index, "data_%d" % index)
		tile_set.set_custom_data_layer_type(index, custom_types[index % custom_types.size()])

	var texture := GradientTexture2D.new()
	texture.width = 64
	texture.height = 64
	var occluder := OccluderPolygon2D.new()
	occluder.polygon = PackedVector2Array([Vector2(-4, -4), Vector2(4, -4), Vector2(0, 4)])
	var navigation := NavigationPolygon.new()
	navigation.vertices = PackedVector2Array([Vector2(-8, -8), Vector2(8, -8), Vector2(8, 8)])
	navigation.add_polygon(PackedInt32Array([0, 1, 2]))
	var tile_scene := PackedScene.new()
	var scene_root := Node2D.new()
	scene_root.name = "SceneTile"
	tile_scene.pack(scene_root)
	scene_root.free()

	for index in 12:
		if index % 3 == 2:
			var scenes := TileSetScenesCollectionSource.new()
			tile_set.add_source(scenes, index)
			for scene_id in [10, 1, 12]:
				scenes.create_scene_tile(tile_scene, scene_id)
				scenes.set_scene_tile_display_placeholder(scene_id, scene_id != 1)
			continue
		var atlas := TileSetAtlasSource.new()
		atlas.texture = texture
		tile_set.add_source(atlas, index)
		atlas.create_tile(Vector2i(0, 0))
		atlas.create_tile(Vector2i(1, 0))
		atlas.set_tile_animation_frames_count(Vector2i(1, 0), 2)
		atlas.set_tile_animation_speed(Vector2i(1, 0), 2.0)
		atlas.set_tile_animation_frame_duration(Vector2i(1, 0), 1, 0.5)
		atlas.create_tile(Vector2i(0, 2), Vector2i(2, 1))
		atlas.create_alternative_tile(Vector2i(0, 0), 1)
		atlas.create_alternative_tile(Vector2i(0, 0), 10)
		atlas.get_tile_data(Vector2i(0, 0), 1).flip_h = true
		atlas.get_tile_data(Vector2i(0, 0), 10).modulate = Color(0.5, 0.5, 1.0)
		var data: TileData = atlas.get_tile_data(Vector2i(0, 0), 0)
		data.set_terrain_set(index)
		data.set_terrain(0)
		var bit := (
			TileSet.CELL_NEIGHBOR_BOTTOM_RIGHT_CORNER
			if index % 3 == TileSet.TERRAIN_MODE_MATCH_CORNERS
			else TileSet.CELL_NEIGHBOR_RIGHT_SIDE
		)
		data.set_terrain_peering_bit(bit, 1)
		for layer in [index, (index + 6) % 12]:
			data.set_occluder_polygons_count(layer, 1)
			data.set_occluder_polygon(layer, 0, occluder)
			data.set_constant_linear_velocity(layer, Vector2(layer + 1, 0))
			data.add_collision_polygon(layer)
			data.set_collision_polygon_points(
				layer, 0, PackedVector2Array([Vector2(-8, -8), Vector2(8, -8), Vector2(0, 8)])
			)
			data.set_collision_polygon_one_way(layer, 0, layer % 2 == 0)
			data.set_navigation_polygon(layer, navigation)
			data.set_custom_data_by_layer_id(layer, _custom_value(custom_types[layer % 6], layer))

	tile_set.set_source_level_tile_proxy(11, 0)
	tile_set.set_source_level_tile_proxy(5, 1)
	tile_set.set_coords_level_tile_proxy(1, Vector2i(0, 0), 10, Vector2i(1, 0))
	tile_set.set_coords_level_tile_proxy(3, Vector2i(1, 0), 4, Vector2i(0, 0))
	tile_set.set_alternative_level_tile_proxy(6, Vector2i(0, 0), 1, 7, Vector2i(0, 0), 10)
	tile_set.set_alternative_level_tile_proxy(9, Vector2i(0, 0), 10, 0, Vector2i(0, 0), 1)
	return generator._save("tile_set_layers.tres", tile_set)


## Six classes whose dynamic families a PropertyListHelper sizes by a count property, each node
## with 12 entries so the entries past 9 sort against the single digits: a TabBar of 12 tabs, a
## PopupMenu of 12 items (plain, check and radio ones, some separators, some checked, disabled or
## numbered past the default id), an ItemList of 12 rows, an OptionButton and a MenuButton of 12
## popup items, and a FileDialog of 12 options. Entries 0, 3, 6, 9, 10 and 11 of the five classes
## that list an icon key carry one shared texture, its sub-resource id pinned, since the saver
## strips a value equal to its default and the icon's place (second, or third in a tab) is
## otherwise never written. Each class's keys in the order its _bind_methods PropertyListHelper
## registration gives them: tab_bar.cpp (title, tooltip, icon, disabled), popup_menu.cpp and
## menu_button.cpp (text, icon, checkable, checked, id, disabled, separator), item_list.cpp
## (text, icon, selectable, disabled), option_button.cpp (text, icon, id, disabled, separator)
## and file_dialog.cpp (name, values, default).
static func gui_item_arrays(generator) -> String:
	var root: Control = generator._root("Control")
	var icon := _icon()
	_gui_tab_bar(generator, root, icon)
	_gui_popup_menu(generator, root, icon)
	_gui_item_list(generator, root, icon)
	_gui_option_button(generator, root, icon)
	_gui_menu_button(generator, root, icon)
	_gui_file_dialog(generator, root)
	return generator._save_scene("gui_item_arrays", root)


## The texture the cases' icons share, its own and its gradient's sub-resource ids pinned.
static func _icon() -> GradientTexture2D:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color.RED, Color.BLUE])
	gradient.resource_scene_unique_id = "Gradient_1c0ns"
	var icon := GradientTexture2D.new()
	icon.width = 16
	icon.height = 16
	icon.gradient = gradient
	icon.resource_scene_unique_id = "GradientTexture2D_1c0ns"
	return icon


## Whether an entry carries an icon: enough of them past 9 that the icon key's place is written.
static func _carries_icon(index: int) -> bool:
	return index % 3 == 0 or index >= 10


## 12 tabs, each with a title, a tooltip and a disabled flag, some of them with an icon.
static func _gui_tab_bar(generator, root: Node, icon: Texture2D) -> void:
	var tab_bar: TabBar = generator._add(root, "TabBar", "TabBar")
	for index in 12:
		tab_bar.add_tab("Tab %d" % index)
		tab_bar.set_tab_tooltip(index, "Tip %d" % index)
		tab_bar.set_tab_disabled(index, index % 2 == 0)
		if _carries_icon(index):
			tab_bar.set_tab_icon(index, icon)


## 12 items: plain, check and radio ones and separators, some checked, disabled or with an icon.
static func _gui_popup_menu(generator, root: Node, icon: Texture2D) -> void:
	var popup: PopupMenu = generator._add(root, "PopupMenu", "PopupMenu")
	for index in 12:
		var id := 100 + index
		match index % 4:
			0:
				popup.add_item("Item %d" % index, id)
			1:
				popup.add_check_item("Check %d" % index, id)
			2:
				popup.add_radio_check_item("Radio %d" % index, id)
			_:
				popup.add_separator("Separator %d" % index, id)
		if index % 4 != 3:
			popup.set_item_checked(index, index % 3 == 0)
		popup.set_item_disabled(index, index % 5 == 0)
		if _carries_icon(index):
			popup.set_item_icon(index, icon)


## 12 rows, some not selectable, disabled or with an icon.
static func _gui_item_list(generator, root: Node, icon: Texture2D) -> void:
	var item_list: ItemList = generator._add(root, "ItemList", "ItemList")
	for index in 12:
		item_list.add_item("Entry %d" % index)
		item_list.set_item_selectable(index, index % 3 != 0)
		item_list.set_item_disabled(index, index % 4 == 0)
		if _carries_icon(index):
			item_list.set_item_icon(index, icon)


## 12 popup items, some separators, disabled or with an icon.
static func _gui_option_button(generator, root: Node, icon: Texture2D) -> void:
	var option_button: OptionButton = generator._add(root, "OptionButton", "OptionButton")
	for index in 12:
		if index % 5 == 4:
			option_button.add_separator("Separator %d" % index)
		else:
			option_button.add_item("Option %d" % index, 200 + index)
		option_button.set_item_disabled(index, index % 6 == 0)
		if _carries_icon(index):
			option_button.set_item_icon(index, icon)


## 12 popup items: plain, check ones and separators, some checked, disabled or with an icon.
static func _gui_menu_button(generator, root: Node, icon: Texture2D) -> void:
	var menu_button: MenuButton = generator._add(root, "MenuButton", "MenuButton")
	var menu: PopupMenu = menu_button.get_popup()
	for index in 12:
		var id := 300 + index
		match index % 3:
			0:
				menu.add_item("Item %d" % index, id)
			1:
				menu.add_check_item("Check %d" % index, id)
			_:
				menu.add_separator("Separator %d" % index, id)
		if index % 3 != 2:
			menu.set_item_checked(index, index % 4 == 0)
		menu.set_item_disabled(index, index % 5 == 1)
		if _carries_icon(index):
			menu.set_item_icon(index, icon)


## 12 options, each with its own values and default index.
static func _gui_file_dialog(generator, root: Node) -> void:
	var file_dialog: FileDialog = generator._add(root, "FileDialog", "FileDialog")
	for index in 12:
		file_dialog.add_option("Opt %d" % index, PackedStringArray(["fast", "slow"]), 1)


## A value off the default of a custom data layer of the given type.
static func _custom_value(type: int, layer: int) -> Variant:
	match type:
		TYPE_INT:
			return layer + 1
		TYPE_STRING:
			return "value_%d" % layer
		TYPE_FLOAT:
			return 0.5 + layer
		TYPE_COLOR:
			return Color(0.25, 0.5, 0.75)
		TYPE_BOOL:
			return true
		_:
			return Vector2(layer, 1)
