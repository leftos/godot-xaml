namespace GodotXaml.Core.Tests.Conversion;

// The .xscn each scene fixture converts to by the format's rules (docs/DECISIONS.md, "The core's implementation choices"). A scene
// instanced or inherited by a .tscn path without a uid is <x:Instance Scene="…">, since a tag would read as .xscn.
public sealed partial class SceneBackTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal)
    {
        ["bone_chain_settings"] = $$"""
            <Skeleton3D {{X}} Name="Root" x:UniqueId="931671723">
              <x:Prop Name="bones/0/enabled" Value="true" />
              <x:Prop Name="bones/0/name" Value='"bone_0"' />
              <x:Prop Name="bones/0/parent" Value="-1" />
              <x:Prop Name="bones/0/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/0/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.25, 0)" />
              <x:Prop Name="bones/0/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/0/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/1/enabled" Value="true" />
              <x:Prop Name="bones/1/name" Value='"bone_1"' />
              <x:Prop Name="bones/1/parent" Value="0" />
              <x:Prop Name="bones/1/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/1/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)" />
              <x:Prop Name="bones/1/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/1/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/10/enabled" Value="true" />
              <x:Prop Name="bones/10/name" Value='"bone_10"' />
              <x:Prop Name="bones/10/parent" Value="9" />
              <x:Prop Name="bones/10/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/10/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2.75, 0)" />
              <x:Prop Name="bones/10/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/10/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/11/enabled" Value="true" />
              <x:Prop Name="bones/11/name" Value='"bone_11"' />
              <x:Prop Name="bones/11/parent" Value="10" />
              <x:Prop Name="bones/11/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/11/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 3, 0)" />
              <x:Prop Name="bones/11/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/11/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/2/enabled" Value="true" />
              <x:Prop Name="bones/2/name" Value='"bone_2"' />
              <x:Prop Name="bones/2/parent" Value="1" />
              <x:Prop Name="bones/2/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/2/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.75, 0)" />
              <x:Prop Name="bones/2/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/2/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/3/enabled" Value="true" />
              <x:Prop Name="bones/3/name" Value='"bone_3"' />
              <x:Prop Name="bones/3/parent" Value="2" />
              <x:Prop Name="bones/3/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/3/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0)" />
              <x:Prop Name="bones/3/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/3/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/4/enabled" Value="true" />
              <x:Prop Name="bones/4/name" Value='"bone_4"' />
              <x:Prop Name="bones/4/parent" Value="3" />
              <x:Prop Name="bones/4/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/4/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.25, 0)" />
              <x:Prop Name="bones/4/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/4/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/5/enabled" Value="true" />
              <x:Prop Name="bones/5/name" Value='"bone_5"' />
              <x:Prop Name="bones/5/parent" Value="4" />
              <x:Prop Name="bones/5/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/5/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.5, 0)" />
              <x:Prop Name="bones/5/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/5/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/6/enabled" Value="true" />
              <x:Prop Name="bones/6/name" Value='"bone_6"' />
              <x:Prop Name="bones/6/parent" Value="5" />
              <x:Prop Name="bones/6/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/6/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.75, 0)" />
              <x:Prop Name="bones/6/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/6/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/7/enabled" Value="true" />
              <x:Prop Name="bones/7/name" Value='"bone_7"' />
              <x:Prop Name="bones/7/parent" Value="6" />
              <x:Prop Name="bones/7/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/7/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 0)" />
              <x:Prop Name="bones/7/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/7/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/8/enabled" Value="true" />
              <x:Prop Name="bones/8/name" Value='"bone_8"' />
              <x:Prop Name="bones/8/parent" Value="7" />
              <x:Prop Name="bones/8/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/8/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2.25, 0)" />
              <x:Prop Name="bones/8/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/8/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/9/enabled" Value="true" />
              <x:Prop Name="bones/9/name" Value='"bone_9"' />
              <x:Prop Name="bones/9/parent" Value="8" />
              <x:Prop Name="bones/9/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/9/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2.5, 0)" />
              <x:Prop Name="bones/9/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/9/scale" Value="Vector3(1, 1, 1)" />
              <SpringBoneCollisionSphere3D Name="Sphere0" x:UniqueId="1205412338" />
              <Node3D Name="Target0" x:UniqueId="766362382" />
              <SpringBoneCollisionSphere3D Name="Sphere1" x:UniqueId="967820918" />
              <Node3D Name="Target1" x:UniqueId="430654535" />
              <Path3D Name="Path" x:UniqueId="295948955" />
              <SpringBoneSimulator3D Name="Spring" SettingCount="2" x:UniqueId="1756322195">
                <x:Prop Name="settings/0/center_from" Value="0" />
                <x:Prop Name="settings/0/enable_all_child_collisions" Value="true" />
                <x:Prop Name="settings/0/end_bone" Value="11" />
                <x:Prop Name="settings/0/end_bone_name" Value='""' />
                <x:Prop Name="settings/0/exclude_collision_count" Value="0" />
                <x:Prop Name="settings/0/extend_end_bone" Value="false" />
                <x:Prop Name="settings/0/individual_config" Value="true" />
                <x:Prop Name="settings/0/joint_count" Value="12" />
                <x:Prop Name="settings/0/joints/0/drag" Value="0.25" />
                <x:Prop Name="settings/0/joints/0/gravity" Value="0.125" />
                <x:Prop Name="settings/0/joints/0/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/0/radius" Value="0.0625" />
                <x:Prop Name="settings/0/joints/0/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/0/stiffness" Value="0.5" />
                <x:Prop Name="settings/0/joints/1/drag" Value="0.265625" />
                <x:Prop Name="settings/0/joints/1/gravity" Value="0.25" />
                <x:Prop Name="settings/0/joints/1/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/1/radius" Value="0.125" />
                <x:Prop Name="settings/0/joints/1/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/1/stiffness" Value="0.53125" />
                <x:Prop Name="settings/0/joints/10/drag" Value="0.40625" />
                <x:Prop Name="settings/0/joints/10/gravity" Value="1.375" />
                <x:Prop Name="settings/0/joints/10/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/10/radius" Value="0.6875" />
                <x:Prop Name="settings/0/joints/10/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/10/stiffness" Value="0.8125" />
                <x:Prop Name="settings/0/joints/11/drag" Value="0.421875" />
                <x:Prop Name="settings/0/joints/11/gravity" Value="1.5" />
                <x:Prop Name="settings/0/joints/11/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/11/radius" Value="0.75" />
                <x:Prop Name="settings/0/joints/11/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/11/stiffness" Value="0.84375" />
                <x:Prop Name="settings/0/joints/2/drag" Value="0.28125" />
                <x:Prop Name="settings/0/joints/2/gravity" Value="0.375" />
                <x:Prop Name="settings/0/joints/2/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/2/radius" Value="0.1875" />
                <x:Prop Name="settings/0/joints/2/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/2/stiffness" Value="0.5625" />
                <x:Prop Name="settings/0/joints/3/drag" Value="0.296875" />
                <x:Prop Name="settings/0/joints/3/gravity" Value="0.5" />
                <x:Prop Name="settings/0/joints/3/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/3/radius" Value="0.25" />
                <x:Prop Name="settings/0/joints/3/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/3/stiffness" Value="0.59375" />
                <x:Prop Name="settings/0/joints/4/drag" Value="0.3125" />
                <x:Prop Name="settings/0/joints/4/gravity" Value="0.625" />
                <x:Prop Name="settings/0/joints/4/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/4/radius" Value="0.3125" />
                <x:Prop Name="settings/0/joints/4/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/4/stiffness" Value="0.625" />
                <x:Prop Name="settings/0/joints/5/drag" Value="0.328125" />
                <x:Prop Name="settings/0/joints/5/gravity" Value="0.75" />
                <x:Prop Name="settings/0/joints/5/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/5/radius" Value="0.375" />
                <x:Prop Name="settings/0/joints/5/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/5/stiffness" Value="0.65625" />
                <x:Prop Name="settings/0/joints/6/drag" Value="0.34375" />
                <x:Prop Name="settings/0/joints/6/gravity" Value="0.875" />
                <x:Prop Name="settings/0/joints/6/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/6/radius" Value="0.4375" />
                <x:Prop Name="settings/0/joints/6/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/6/stiffness" Value="0.6875" />
                <x:Prop Name="settings/0/joints/7/drag" Value="0.359375" />
                <x:Prop Name="settings/0/joints/7/gravity" Value="1.0" />
                <x:Prop Name="settings/0/joints/7/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/7/radius" Value="0.5" />
                <x:Prop Name="settings/0/joints/7/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/7/stiffness" Value="0.71875" />
                <x:Prop Name="settings/0/joints/8/drag" Value="0.375" />
                <x:Prop Name="settings/0/joints/8/gravity" Value="1.125" />
                <x:Prop Name="settings/0/joints/8/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/8/radius" Value="0.5625" />
                <x:Prop Name="settings/0/joints/8/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/8/stiffness" Value="0.75" />
                <x:Prop Name="settings/0/joints/9/drag" Value="0.390625" />
                <x:Prop Name="settings/0/joints/9/gravity" Value="1.25" />
                <x:Prop Name="settings/0/joints/9/gravity_direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/0/joints/9/radius" Value="0.625" />
                <x:Prop Name="settings/0/joints/9/rotation_axis" Value="3" />
                <x:Prop Name="settings/0/joints/9/stiffness" Value="0.78125" />
                <x:Prop Name="settings/0/root_bone" Value="0" />
                <x:Prop Name="settings/0/root_bone_name" Value='""' />
                <x:Prop Name="settings/1/center_from" Value="0" />
                <x:Prop Name="settings/1/collision_count" Value="2" />
                <x:Prop Name="settings/1/collisions/0" Value='NodePath("../Sphere0")' />
                <x:Prop Name="settings/1/collisions/1" Value='NodePath("../Sphere1")' />
                <x:Prop Name="settings/1/drag/damping_curve" Value="null" />
                <x:Prop Name="settings/1/drag/value" Value="0.4" />
                <x:Prop Name="settings/1/enable_all_child_collisions" Value="false" />
                <x:Prop Name="settings/1/end_bone" Value="3" />
                <x:Prop Name="settings/1/end_bone_name" Value='""' />
                <x:Prop Name="settings/1/extend_end_bone" Value="false" />
                <x:Prop Name="settings/1/gravity/damping_curve" Value="null" />
                <x:Prop Name="settings/1/gravity/direction" Value="Vector3(0, -1, 0)" />
                <x:Prop Name="settings/1/gravity/value" Value="0.0" />
                <x:Prop Name="settings/1/individual_config" Value="false" />
                <x:Prop Name="settings/1/radius/damping_curve" Value="null" />
                <x:Prop Name="settings/1/radius/value" Value="0.02" />
                <x:Prop Name="settings/1/root_bone" Value="1" />
                <x:Prop Name="settings/1/root_bone_name" Value='""' />
                <x:Prop Name="settings/1/rotation_axis" Value="3" />
                <x:Prop Name="settings/1/stiffness/damping_curve" Value="null" />
                <x:Prop Name="settings/1/stiffness/value" Value="1.0" />
              </SpringBoneSimulator3D>
              <Fabrik3D Name="Fabrik" SettingCount="2" x:UniqueId="624935357">
                <x:Prop Name="settings/0/end_bone" Value="11" />
                <x:Prop Name="settings/0/end_bone_name" Value='""' />
                <x:Prop Name="settings/0/extend_end_bone" Value="false" />
                <x:Prop Name="settings/0/joint_count" Value="12" />
                <x:Prop Name="settings/0/joints/0/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/0/rotation_axis" Value="0" />
                <x:Prop Name="settings/0/joints/1/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/1/rotation_axis" Value="1" />
                <x:Prop Name="settings/0/joints/10/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/10/rotation_axis" Value="1" />
                <x:Prop Name="settings/0/joints/11/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/11/rotation_axis" Value="2" />
                <x:Prop Name="settings/0/joints/2/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/2/rotation_axis" Value="2" />
                <x:Prop Name="settings/0/joints/3/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/3/rotation_axis" Value="0" />
                <x:Prop Name="settings/0/joints/4/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/4/rotation_axis" Value="1" />
                <x:Prop Name="settings/0/joints/5/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/5/rotation_axis" Value="2" />
                <x:Prop Name="settings/0/joints/6/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/6/rotation_axis" Value="0" />
                <x:Prop Name="settings/0/joints/7/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/7/rotation_axis" Value="1" />
                <x:Prop Name="settings/0/joints/8/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/8/rotation_axis" Value="2" />
                <x:Prop Name="settings/0/joints/9/limitation" Value="null" />
                <x:Prop Name="settings/0/joints/9/rotation_axis" Value="0" />
                <x:Prop Name="settings/0/root_bone" Value="0" />
                <x:Prop Name="settings/0/root_bone_name" Value='""' />
                <x:Prop Name="settings/0/target_node" Value='NodePath("../Target0")' />
                <x:Prop Name="settings/1/end_bone" Value="8" />
                <x:Prop Name="settings/1/end_bone_name" Value='""' />
                <x:Prop Name="settings/1/extend_end_bone" Value="false" />
                <x:Prop Name="settings/1/joint_count" Value="3" />
                <x:Prop Name="settings/1/joints/0/limitation" Value="null" />
                <x:Prop Name="settings/1/joints/0/rotation_axis" Value="0" />
                <x:Prop Name="settings/1/joints/1/limitation" Value="null" />
                <x:Prop Name="settings/1/joints/1/rotation_axis" Value="1" />
                <x:Prop Name="settings/1/joints/2/limitation" Value="null" />
                <x:Prop Name="settings/1/joints/2/rotation_axis" Value="2" />
                <x:Prop Name="settings/1/root_bone" Value="6" />
                <x:Prop Name="settings/1/root_bone_name" Value='""' />
                <x:Prop Name="settings/1/target_node" Value='NodePath("../Target1")' />
              </Fabrik3D>
              <SplineIK3D Name="Spline" SettingCount="1" x:UniqueId="2076682939">
                <x:Prop Name="settings/0/end_bone" Value="5" />
                <x:Prop Name="settings/0/end_bone_name" Value='""' />
                <x:Prop Name="settings/0/extend_end_bone" Value="false" />
                <x:Prop Name="settings/0/joint_count" Value="6" />
                <x:Prop Name="settings/0/path_3d" Value='NodePath("../Path")' />
                <x:Prop Name="settings/0/root_bone" Value="0" />
                <x:Prop Name="settings/0/root_bone_name" Value='""' />
                <x:Prop Name="settings/0/tilt_enabled" Value="true" />
                <x:Prop Name="settings/0/tilt_fade_in" Value="1" />
                <x:Prop Name="settings/0/tilt_fade_out" Value="1" />
              </SplineIK3D>
            </Skeleton3D>
            """,
        ["connections"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="1060821705">
              <Node Name="Emitter" x:UniqueId="648309840">
                <x:Connect Signal="ChildEnteredTree" To="Receiver" Method="update_configuration_warnings" Unbinds="1" />
                <x:Connect Signal="Ready" To="Receiver" Method="update_configuration_warnings" Flags="OneShot" />
                <x:Connect Signal="Renamed" To="Receiver" Method="update_configuration_warnings" />
                <x:Connect Signal="TreeEntered" To="Receiver" Method="set_process" Binds="[true]" />
                <x:Connect Signal="TreeExited" To="Receiver" Method="update_configuration_warnings" Flags="Deferred" />
              </Node>
              <Node Name="Receiver" x:UniqueId="1734189678" />
            </Node>
            """,
        ["ext_resource"] = $$"""
            <Node2D {{X}} Name="Root" x:UniqueId="571779315">
              <Sprite2D Name="Sprite" Material="res://cases/material.tres" x:UniqueId="1981545549" />
            </Node2D>
            """,
        ["groups"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="119704589">
              <Node Name="Child" x:Groups="alpha, zeta" x:UniqueId="75316238" />
            </Node>
            """,
        ["inherited"] = $$"""
            <x:Instance {{X}} Name="Root" Scene="res://cases/nested.tscn" x:UniqueId="187941574">
              <x:Override Path="A" Position="Vector2(1, 1)" x:Index="0" x:UniqueId="2045403667" />
              <Node Name="Extra" x:Index="2" x:UniqueId="564944056" />
            </x:Instance>
            """,
        ["instanced"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="1300837998">
              <x:Instance Name="Inst" Scene="res://cases/nested.tscn" ProcessPriority="7" x:Editable="True" x:UniqueId="328417481">
                <x:Override Path="A" Position="Vector2(9, 9)" x:Index="0" x:UniqueId="2045403667">
                  <Node Name="Added" x:Index="1" x:UniqueId="1957543159" />
                </x:Override>
              </x:Instance>
            </Node>
            """,
        ["instanced_order"] = $$"""
            <Control {{X}} Name="Root" AnchorsPreset="TopLeft" LayoutMode="Uncontrolled" x:UniqueId="183478099">
              <x:Instance Name="Inst" Scene="res://cases/order_inner.tscn" GrowVertical="Both" LayoutMode="Position"
                  x:Editable="True" x:UniqueId="1121728355">
                <x:Override Path="Box" GrowVertical="Both" LayoutMode="Position" x:Index="0" x:UniqueId="614292841" />
              </x:Instance>
            </Control>
            """,
        ["lone_root"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="841232467" />
            """,
        ["multiline"] = $$$"""
            <Node
                {{{X}}}
                Name="Root"
                EditorDescription='first line&#10;second "quoted" line'
                Metadata.table='{"a": [1, 2], "b": 2, "c": {"nested": true}}'
                x:UniqueId="137123633">
              <Node Name="After" EditorDescription="one line" x:UniqueId="1226013350" />
            </Node>
            """,
        ["nested"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="328417481">
              <Node2D Name="A" Position="4, 2" x:UniqueId="2045403667">
                <Node Name="B" x:UniqueId="352249410">
                  <Node Name="C" x:UniqueId="127997354" />
                </Node>
              </Node2D>
              <Node Name="D" x:UniqueId="433799269" />
            </Node>
            """,
        ["order_inner"] = $$"""
            <Control {{X}} Name="Root" AnchorsPreset="TopLeft" LayoutMode="Uncontrolled" x:UniqueId="1121728355">
              <Panel Name="Box" LayoutMode="Anchors" x:UniqueId="614292841" />
            </Control>
            """,
        ["placeholder"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="974068946">
              <InstancePlaceholder Name="Later" x:InstancePlaceholder="res://cases/lone_root.tscn" x:UniqueId="841232467" />
            </Node>
            """,
        ["placeholder_order"] = $$"""
            <Control {{X}} Name="Root" AnchorsPreset="TopLeft" LayoutMode="Uncontrolled" x:UniqueId="1347851746">
              <InstancePlaceholder Name="Later" grow_vertical="2" layout_mode="0" x:InstancePlaceholder="res://cases/order_inner.tscn"
                  x:UniqueId="1408917781" />
            </Control>
            """,
        ["property_order"] = $$"""
            <Control {{X}} Name="Root" AnchorsPreset="TopLeft" LayoutMode="Uncontrolled" x:UniqueId="198994451">
              <Panel Name="Box" GrowHorizontal="Begin" GrowVertical="Begin" LayoutMode="Position" x:UniqueId="1109250759" />
              <Button Name="Toggle" ButtonPressed="True" LayoutMode="Position" Text="Hold" ToggleMode="True" x:UniqueId="1733119216" />
            </Control>
            """,
        ["script_node"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="784806209">
              <Node Name="Holder" Script="res://holder.gd" target='NodePath("../Target")' x:NodePaths="target" x:UniqueId="1536725368" />
              <Node Name="Target" x:UniqueId="1544414068" />
            </Node>
            """,
        ["skeleton_bones"] = $$"""
            <Skeleton3D {{X}} Name="Root" x:UniqueId="1637017537">
              <x:Prop Name="bones/0/enabled" Value="true" />
              <x:Prop Name="bones/0/name" Value='"bone_0"' />
              <x:Prop Name="bones/0/parent" Value="-1" />
              <x:Prop Name="bones/0/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/0/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)" />
              <x:Prop Name="bones/0/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/0/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/1/enabled" Value="true" />
              <x:Prop Name="bones/1/name" Value='"bone_1"' />
              <x:Prop Name="bones/1/parent" Value="0" />
              <x:Prop Name="bones/1/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/1/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)" />
              <x:Prop Name="bones/1/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/1/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/10/enabled" Value="true" />
              <x:Prop Name="bones/10/name" Value='"bone_10"' />
              <x:Prop Name="bones/10/parent" Value="9" />
              <x:Prop Name="bones/10/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/10/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 5, 0)" />
              <x:Prop Name="bones/10/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/10/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/11/enabled" Value="true" />
              <x:Prop Name="bones/11/name" Value='"bone_11"' />
              <x:Prop Name="bones/11/parent" Value="10" />
              <x:Prop Name="bones/11/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/11/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 5.5, 0)" />
              <x:Prop Name="bones/11/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/11/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/2/enabled" Value="true" />
              <x:Prop Name="bones/2/name" Value='"bone_2"' />
              <x:Prop Name="bones/2/parent" Value="1" />
              <x:Prop Name="bones/2/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/2/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0)" />
              <x:Prop Name="bones/2/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/2/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/3/bone_meta/tag" Value='"spine"' />
              <x:Prop Name="bones/3/enabled" Value="true" />
              <x:Prop Name="bones/3/name" Value='"bone_3"' />
              <x:Prop Name="bones/3/parent" Value="2" />
              <x:Prop Name="bones/3/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/3/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1.5, 0)" />
              <x:Prop Name="bones/3/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/3/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/4/enabled" Value="true" />
              <x:Prop Name="bones/4/name" Value='"bone_4"' />
              <x:Prop Name="bones/4/parent" Value="3" />
              <x:Prop Name="bones/4/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/4/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 0)" />
              <x:Prop Name="bones/4/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/4/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/5/enabled" Value="true" />
              <x:Prop Name="bones/5/name" Value='"bone_5"' />
              <x:Prop Name="bones/5/parent" Value="4" />
              <x:Prop Name="bones/5/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/5/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 2.5, 0)" />
              <x:Prop Name="bones/5/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/5/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/6/enabled" Value="true" />
              <x:Prop Name="bones/6/name" Value='"bone_6"' />
              <x:Prop Name="bones/6/parent" Value="0" />
              <x:Prop Name="bones/6/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/6/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 3, 0)" />
              <x:Prop Name="bones/6/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/6/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/7/enabled" Value="false" />
              <x:Prop Name="bones/7/name" Value='"bone_7"' />
              <x:Prop Name="bones/7/parent" Value="6" />
              <x:Prop Name="bones/7/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/7/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 3.5, 0)" />
              <x:Prop Name="bones/7/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/7/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/8/enabled" Value="true" />
              <x:Prop Name="bones/8/name" Value='"bone_8"' />
              <x:Prop Name="bones/8/parent" Value="7" />
              <x:Prop Name="bones/8/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/8/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 4, 0)" />
              <x:Prop Name="bones/8/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/8/scale" Value="Vector3(1, 1, 1)" />
              <x:Prop Name="bones/9/enabled" Value="true" />
              <x:Prop Name="bones/9/name" Value='"bone_9"' />
              <x:Prop Name="bones/9/parent" Value="8" />
              <x:Prop Name="bones/9/position" Value="Vector3(0, 0, 0)" />
              <x:Prop Name="bones/9/rest" Value="Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 4.5, 0)" />
              <x:Prop Name="bones/9/rotation" Value="Quaternion(0, 0, 0, 1)" />
              <x:Prop Name="bones/9/scale" Value="Vector3(1, 1, 1)" />
            </Skeleton3D>
            """,
        ["sub_resources"] = $$"""
            <Node2D {{X}} Name="Root" x:UniqueId="944498477">
              <x:Resources>
                <CanvasItemMaterial x:Key="CanvasItemMaterial1" LightMode="Unshaded" />
              </x:Resources>
              <Sprite2D Name="One" x:UniqueId="616929792">
                <Sprite2D.Texture>
                  <GradientTexture2D Width="8">
                    <GradientTexture2D.Gradient>
                      <Gradient Colors="PackedColorArray(1, 0, 0, 1, 0, 0, 1, 1)" />
                    </GradientTexture2D.Gradient>
                  </GradientTexture2D>
                </Sprite2D.Texture>
              </Sprite2D>
              <Sprite2D Name="Two" Material="{CanvasItemMaterial1}" x:UniqueId="217975505" />
              <Sprite2D Name="Three" Material="{CanvasItemMaterial1}" x:UniqueId="350658218" />
            </Node2D>
            """,
        ["unique_name"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="1163440352">
              <Node Name="%Child" x:UniqueId="1112379229" />
            </Node>
            """,
    };
}
