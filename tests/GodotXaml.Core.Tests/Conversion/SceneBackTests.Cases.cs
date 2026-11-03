namespace GodotXaml.Core.Tests.Conversion;

// The .xscn each scene fixture converts to by the format's rules (docs/DECISIONS.md, "The core's implementation choices"). A scene
// instanced or inherited by a .tscn path without a uid is <x:Instance Scene="…">, since a tag would read as .xscn.
public sealed partial class SceneBackTests
{
    private const string X = "xmlns:x=\"https://github.com/leftos/godot-xaml/format/1\"";

    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal)
    {
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
