# godot-xaml format, condensed

The full reference is [docs/format/README.md](../../../docs/format/README.md), with every value type in [values.md](../../../docs/format/values.md) and every code in [diagnostics.md](../../../docs/format/diagnostics.md).

## Elements

| Element | Where | What it is |
|---|---|---|
| `<ClassName Name="…">` | Anywhere a node goes | A node of a Godot class, by C# name |
| `<prefix:SceneName Name="…">` | Anywhere a node goes | An instance of `res://folder/SceneName.xscn`, where `xmlns:prefix="res://folder/"` is on the root |
| `<x:Instance Name="…" Scene="res://…">` | Anywhere a node goes, or the root (an inherited scene) | An instance named by path |
| `<x:Override Path="A/B" …>` | Direct child of an instance | Changes to the node `A/B` inside the instance; new child nodes nest in it |
| `<x:Connect Signal="…" To="…" Method="…" Flags="…" Binds='[…]' Unbinds="n" />` | Child of the emitter | A connection that is not the plain root-receiver case |
| `<x:Resources>` | First children of the root, after `x:Uids` | Shared sub-resources, each with `x:Key` |
| `<x:Uids><x:Uid Path="res://…" Value="uid://…" /></x:Uids>` | First child of the root | Uids of referenced files, maintained by the tools |
| `<x:Prop Name="godot/name" Value="value text" />` | Child of a node or resource | A property that cannot be an attribute |
| `<Owner.Property><Class … /></Owner.Property>` | Child of `Owner` | A sub-resource used once, inline |

## `x:` attributes on a node

`x:Groups="a, b"`, `x:Editable="True"` (an instance's children are editable), `x:Index`, `x:InstancePlaceholder="res://…"`, `x:NodePaths`, `x:Owner`, `x:OwnerUidPath`, `x:ParentIdPath`, `x:UniqueId`. Keep the ones a file has; add only `x:Groups` and `x:Editable` by hand.

## Values

| Property type | Write | Example |
|---|---|---|
| bool | `True`, `False` | `Visible="False"` |
| int, float | Number, no trailing `.0`; `inf`, `inf_neg`, `nan` | `ZIndex="2"`, `Rotation="1.5707964"` |
| enum, flags | C# member names, flags joined with `, ` | `MouseFilter="Ignore"`, `SizeFlagsHorizontal="ExpandFill"` |
| String, StringName | The text, unquoted | `Text="[b]{0}[/b]"` |
| NodePath | The bare path | `RemotePath="../Hero"` |
| Vector2/3/4, Vector2i/3i/4i | Components joined with `, ` | `Position="32, 16"` |
| Color | Four floats | `Modulate="1, 0.5, 0, 1"` |
| Resource | `res://…`, `{Key}`, `{x:Null}` | `Texture="res://art/hero.png"` |
| Everything else | Godot value text | `Transform="Transform2D(1, 0, 0, 1, 10, 20)"`, `Points="PackedVector2Array(0, 0, 32, 0)"` |
| Script properties, `metadata/…`, `shader_parameter/…` | Godot value text, strings quoted | `title='"Main menu"'`, `Metadata.tab='0'` |

A Godot property with a `/` in its name is a dotted attribute: `ThemeOverrideColors.font_color="1, 1, 1, 1"`, `Metadata.tab_index="0"`.

A line break inside a value is `&#10;`.

## A scene with an instance, an override, a shared resource and signals

```xml xscn
<Control
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    xmlns:ui="res://ui/"
    Name="Hud"
    Script="res://ui/hud.gd">
  <x:Resources>
    <StyleBoxFlat x:Key="Panel" BgColor="0.1, 0.1, 0.1, 0.8" />
  </x:Resources>
  <PanelContainer Name="Top" ThemeOverrideStyles.panel="{Panel}">
    <Label Name="%Score" Text="0" />
  </PanelContainer>
  <PanelContainer Name="Bottom" ThemeOverrideStyles.panel="{Panel}" />
  <ui:PauseMenu Name="Pause" Visible="False">
    <x:Override Path="Buttons/Resume" Text="Back to game" />
  </ui:PauseMenu>
  <Button Name="Menu" Pressed="OnMenuPressed" Text="Menu">
    <x:Connect Signal="Pressed" To="Pause" Method="show" Flags="Deferred" />
  </Button>
</Control>
```

## Codes you will meet

| Code | Meaning | Fix |
|---|---|---|
| GX0001 | Not well-formed XML | Fix the XML at the position given |
| GX0103 | Undeclared prefix | Declare `xmlns:p="res://folder/"` on the root |
| GX0104 | A node has no `Name` | Add one |
| GX0105 | A value is not text of its type | Write it in the form above; never `ExtResource(…)` |
| GX0106 | Not a member of the enum | Use one of the names the message lists |
| GX0107 | Unknown class (warning) | Check the tag is the class's C# name; a GDExtension class is fine |
| GX0108 | Missing attribute | `x:Override` needs `Path`, `x:Resources` entries need `x:Key` |
| GX0111 | Element in the wrong place | Tables on the root only; no nodes inside a resource |
| GX0112 | Bad property element | Name it `<ParentTag.Property>` and give it one resource |
| GXC0102 | `{Key}` names no `x:Resources` entry; shown on a Godot load, not by `check` | Add the entry or fix the key |
| GXP0006 | A path a script builds was not rewritten | Edit the script by hand |
