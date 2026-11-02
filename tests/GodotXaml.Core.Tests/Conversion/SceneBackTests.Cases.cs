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
        ["placeholder"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="974068946">
              <InstancePlaceholder Name="Later" x:InstancePlaceholder="res://cases/lone_root.tscn" x:UniqueId="841232467" />
            </Node>
            """,
        ["script_node"] = $$"""
            <Node {{X}} Name="Root" x:UniqueId="784806209">
              <Node Name="Holder" Script="res://holder.gd" target='NodePath("../Target")' x:NodePaths="target" x:UniqueId="1536725368" />
              <Node Name="Target" x:UniqueId="1544414068" />
            </Node>
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
