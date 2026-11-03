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
