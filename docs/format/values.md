# Value text

How every Godot value type is written in an `.xscn` or `.xres` attribute. The rest of the format is in [README.md](README.md).

A property's type decides how its value is read, and the text never decides. The type comes from the metadata file of the Godot version (`metadata/godot-4.7.json`). A value has one of two forms:

- **Compact text**, the short XAML form. It is used for the types in the first table below, on a property the metadata types.
- **Godot value text**, the text a `.tscn` holds. It is used for every other type, and for every property whose type the format cannot know: a script's properties, `metadata/…`, `shader_parameter/…`, and names that differ in type between classes on an instance or an override.

The writer uses compact text only when reading it back gives the same value; otherwise the property is written as [`x:Prop`](README.md#xprop) with Godot value text.

## Compact text

| Type | Compact text | Example |
|---|---|---|
| `bool` | `True` or `False`; either case is read | `Visible="False"` |
| `int` | A decimal integer | `ZIndex="3"` |
| `int` with an enum | The member's C# name, or the integer | `MouseFilter="Ignore"` |
| `int` with flags | Member names joined with `, `, or the integer. Bits no name covers are written as an integer. | `SizeFlagsHorizontal="ExpandFill"` |
| `int` with an enum hint but no enum | The hint's display name in PascalCase, or the integer ([below](#integers-with-an-enum-hint-but-no-enum)) | `LayoutMode="Anchors"` |
| `float` | A number with no trailing `.0`, and `inf`, `inf_neg` or `nan` ([below](#floats)) | `AnchorRight="1"`, `Rotation="1.5707964"` |
| `String` | The text itself, unquoted and unescaped beyond XML | `Text="[b]{0}[/b] items"` |
| `StringName` | The text itself, as for `String` | `ThemeTypeVariation="TitleLabel"` |
| `NodePath` | The bare path | `RemotePath="../Target"` |
| `Vector2`, `Vector3`, `Vector4` | The components joined with `, `, as floats | `Position="32, 16"` |
| `Vector2i`, `Vector3i`, `Vector4i` | The components joined with `, `, as integers | `Size="640, 480"` |
| `Color` | Exactly four floats, red, green, blue and alpha, joined with `, `. Never hex, which cannot hold Godot's float channels. | `Modulate="1, 1, 1, 0.5"` |
| A resource | A `res://` path, a `{Key}` from `x:Resources`, or `{x:Null}` | `Texture="res://art/hero.png"`, `Theme="{Ui}"` |

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Label
      Name="Count"
      AnchorRight="1"
      LayoutMode="Anchors"
      Modulate="1, 1, 1, 0.5"
      MouseFilter="Stop"
      SizeFlagsHorizontal="ExpandFill"
      Text="[b]{0}[/b] items"
      ThemeTypeVariation="TitleLabel"
      Visible="False" />
</Control>
```

```xml xscn
<Node2D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Sprite2D
      Name="Hero"
      Position="32, 16"
      Rotation="1.5707964"
      Texture="res://art/hero.png"
      ZIndex="3" />
  <RemoteTransform2D Name="Follow" RemotePath="../Hero" />
</Node2D>
```

Null is a resource property's default, so `{x:Null}` is written only where null differs from the default, such as on an instance or an override.

A wrong value is `GX0105`, with a message saying what was expected, such as `Expected 2 numbers separated by commas.` An enum name the type does not have is `GX0106`, which lists the names it does have.

### Floats

Floats are written as Godot's text saver writes them, through a port of its grisu2 code. The rules:

- **Digits:** the shortest digits that read back as the same value; a value that is exactly a 32-bit float gets a float's digits (`0.1`, not `0.10000000149011612`).
- **No trailing zero:** `1`, not `1.0`.
- **Notation:** fixed for magnitudes from 1e-4 up to 1e15, and exponent form otherwise, with a sign and at least two exponent digits (`1e+20`, `1e-05`).
- **Negative zero** is written `0`.
- **Non-finite values** are `inf`, `inf_neg` and `nan`. The reader also accepts `-inf`.

### Integers with an enum hint but no enum

A few integer properties carry an enum-style hint with no C# enum behind it, among them `layout_mode`, `anchors_preset` and `ProgressBar.fill_mode`. They are written by the hint's display name in PascalCase (`LayoutMode="Container"`, `AnchorsPreset="FullRect"`), and the integer is read too.

### Strings

`String` and `StringName` values are the text itself. XML's own escapes apply ([below](#xml-quoting-and-escaping)); there are no others.

A character XML 1.0 cannot hold, such as a NUL or a lone surrogate, cannot sit in an attribute. A string holding one is written with [`x:Prop`](README.md#xprop), as Godot value text with a `\uXXXX` escape.

## Godot value text

Every other type is written exactly as Godot writes it in a `.tscn`:

| Type | Godot value text |
|---|---|
| `Rect2` | `Rect2(1.5, 2, 640, 480)`: position, then size |
| `Rect2i` | `Rect2i(0, 0, 1920, 1080)` |
| `Transform2D` | `Transform2D(0.87758255, 0.47942555, -0.47942555, 0.87758255, 10, 20)`: the x axis, the y axis, the origin |
| `Transform3D` | `Transform3D(1, 0, 0, 0, 0.87758255, -0.47942555, 0, 0.47942555, 0.87758255, 1, 2, 3)`: the basis by rows, then the origin |
| `Basis` | `Basis(0.9689124, 0, 0.24740396, 0, 1, 0, -0.24740396, 0, 0.9689124)` |
| `Quaternion` | `Quaternion(0, 0.7071068, 0, 0.7071068)`: x, y, z, w |
| `Plane` | `Plane(0, 1, 0, 0.5)`: the normal, then the distance |
| `AABB` | `AABB(-1, -1, -1, 2, 2, 2)`: position, then size |
| `Projection` | `Projection(…)`: sixteen floats, column by column |
| `Array` | `[1, 2, "a"]`, elements joined with `, ` |
| Typed `Array` | `Array[int]([1, 2])`, `Array[Material]([])` |
| `Dictionary` | `{` and `}` with one `key: value` entry per line, joined with `,`, keys in Godot's order; empty is `{}` |
| Typed `Dictionary` | `Dictionary[String, int]({…})`, with `Variant` for an untyped side |
| `PackedByteArray` | `PackedByteArray(0, 1, 255)` |
| `PackedInt32Array`, `PackedInt64Array` | `PackedInt32Array(1, 2, 3)` |
| `PackedFloat32Array`, `PackedFloat64Array` | `PackedFloat32Array(0.5, 1)` |
| `PackedStringArray` | `PackedStringArray("a", "b")` |
| `PackedVector2Array`, `PackedVector3Array`, `PackedVector4Array` | `PackedVector2Array(0, 0, 0.1, -1, 1, 1)`, flat components |
| `PackedColorArray` | `PackedColorArray(1, 0, 0, 1, 0, 0, 1, 1)`, flat components |
| `RID` | `RID(12345)`, or `RID()` |
| `Signal`, `Callable` | `Signal()` and `Callable()`, the only forms a saved file holds |
| `Object` | `Object(Resource,"resource_name":"",…)` |
| `null` | `null` |
| `String` (in value text) | `"text"` quoted, with `\"`, `\\`, `\n` and `\t` escapes |
| `StringName` (in value text) | `&"name"` |
| `NodePath` (in value text) | `NodePath("../Target")` |
| `Vector2` and the other vectors (in value text) | `Vector2(10, 20)` |
| `Color` (in value text) | `Color(1, 0.5, 0.25, 1)` |
| A file (in value text) | `Resource("res://art/hero.png")`, or `Resource("uid://…", "res://art/hero.png")` |
| An `x:Resources` entry (in value text) | `SubResource("Key")` |

The reader also takes Godot's older names (`Quat`, `Rect3`, `Matrix32`, `Transform`, the `Pool…Array`s), and colours as `#rrggbb` or `#rrggbbaa` in value text. The writer always writes the current names.

`ExtResource("…")` is never used: an `.xscn` has no `[ext_resource]` sections, so it is refused (`GX0105`). A file is `Resource("res://…")`.

```xml xscn
<Node3D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Node3D
      Name="Pivot"
      Transform="Transform3D(1, 0, 0, 0, 0.87758255, -0.47942555, 0, 0.47942555, 0.87758255, 1, 2, 3)" />
</Node3D>
```

```xml xscn
<Node2D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Sprite2D Name="Frame" RegionEnabled="True" RegionRect="Rect2(0, 0, 64, 64)" />
  <Line2D Name="Path" Points="PackedVector2Array(0, 0, 32, 0, 32, 32)" />
</Node2D>
```

## Values of unknown type

On a property the format cannot type, every value is Godot value text, strings quoted. Such properties include a script's properties, `metadata/…`, `shader_parameter/…`, and, on an instance or an override, a name whose type differs between classes:

```xml xscn
<Node xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Node
      Name="Holder"
      Script="res://holder.gd"
      speed="2.5"
      target='NodePath("../Target")'
      title='"[Esc] to close"'
      x:NodePaths="target" />
  <Node Name="Target" />
</Node>
```

Two shortcuts read the same on any property: a lone `Resource("res://…")` is a file, and `{Key}` is an `x:Resources` entry.

## XML quoting and escaping

Attribute values are XML, so the writer:

- quotes with `"`, or with `'` when the value holds a `"` and no `'`;
- escapes `&` as `&amp;`, `<` as `&lt;`, and the quote character in use as `&quot;` or `&apos;`;
- writes line breaks and tabs as character references (`&#10;`, `&#13;`, `&#9;`), since XML turns a literal line break or tab in an attribute into a space.

A multi-line value therefore stays on one line in the file:

```xml xscn
<Node
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    Name="Root"
    EditorDescription='first line&#10;second "quoted" line'
    Metadata.table='{&#10;"a": [1, 2],&#10;"b": 2&#10;}' />
```

## Name lists

`x:Groups` and `x:NodePaths` hold names joined with `, ` (`x:Groups="enemies, flying"`). A name that holds a comma, starts with `[` or has spaces at an edge makes the writer use a Godot array of strings instead: `x:Groups='["a, b", "c"]'`.
