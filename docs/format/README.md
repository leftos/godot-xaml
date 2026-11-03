# The `.xscn` and `.xres` formats

This is the reference for the files godot-xaml reads and writes: `.xscn` for a scene, the counterpart of a `.tscn`, and `.xres` for a resource, the counterpart of a `.tres`. It describes format version 1, for Godot 4.7.

Every value type's text is in [values.md](values.md), and every error and warning code in [diagnostics.md](diagnostics.md). For using the files in a project, start with the [guides](../guide/getting-started.md).

## Contents

- [At a glance](#at-a-glance)
- [The file](#the-file)
- [Nodes](#nodes)
- [Properties](#properties)
- [Names: from Godot to C#](#names-from-godot-to-c)
- [Resources](#resources)
- [Files and uids](#files-and-uids)
- [Instanced scenes](#instanced-scenes)
- [Signals](#signals)
- [`x:` attributes on a node](#x-attributes-on-a-node)
- [`x:Prop`](#xprop)
- [Comments](#comments)
- [`.xres` files](#xres-files)
- [The canonical form](#the-canonical-form)
- [What a `.tscn` holds that an `.xscn` does not](#what-a-tscn-holds-that-an-xscn-does-not)

## At a glance

A `.tscn` lists its nodes as flat sections, each naming its parent by path, and stores enum properties as integers. An `.xscn` nests them as elements and uses the names of Godot's C# API:

```xml xscn canonical
<PanelContainer xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <VBoxContainer Name="Column" Alignment="Center" ThemeOverrideConstants.separation="12">
    <Label
        Name="Title"
        HorizontalAlignment="Center"
        SizeFlagsHorizontal="ExpandFill"
        Text="Settings"
        ThemeOverrideColors.font_color="1, 0.5, 0, 1"
        ThemeOverrideFontSizes.font_size="24" />
    <Button Name="%Press" FocusMode="None" MouseFilter="Pass" Text="Press" x:Groups="buttons, ui" />
  </VBoxContainer>
</PanelContainer>
```

- Each node is an element, tagged with its class's C# name (`VBoxContainer`, `CpuParticles2D`).
- Each property is an attribute, named in PascalCase as in C# (`SizeFlagsHorizontal`), with enum values by their C# member names (`ExpandFill`), never by integer.
- Everything the format adds to Godot's own property list takes the `x:` prefix: tables, overrides, groups, and the bookkeeping a `.tscn` keeps.

## The file

An `.xscn` or `.xres` file is an XML 1.0 document in UTF-8. The canonical writer adds no XML declaration and no byte order mark, and ends lines with LF.

The root element declares the format namespace, and binds it to a prefix, conventionally `x`:

```text
xmlns:x="https://github.com/leftos/godot-xaml/format/1"
```

The namespace URI carries the format's version. A second prefix bound to the same URI is an error (`GX0110`); the first binding stays the format prefix.

Godot classes have no prefix, so a default namespace (`xmlns="…"`) is refused (`GX0110`). The only other declarations allowed are scene namespaces, each binding a prefix to a `res://` folder ending in `/` ([Instanced scenes](#instanced-scenes)). Every declaration belongs on the root element (`GX0109`).

The root of an `.xscn` is the scene's root node. The root of an `.xres` is the resource ([`.xres` files](#xres-files)).

## Nodes

A node is an element whose tag is its class's C# name. Child nodes are child elements, in scene order.

```xml xscn
<Node2D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Sprite2D Name="Sprite" Position="32, 16" />
  <Node2D Name="Group">
    <Camera2D Name="Camera" />
  </Node2D>
</Node2D>
```

- **`Name`** is required on every node (`GX0104`), and comes first.
- **A unique name** is written with `%` in front, as `GetNode("%Press")` reads it: `Name="%Press"` is the node `Press` with Godot's `unique_name_in_owner` set.
- **`Script`** is the node's script, a `res://` path, and comes second: `Script="res://player.gd"`.
- **An unknown class**, one the metadata does not know such as a GDExtension class, is kept with its tag as written and warned about (`GX0107`). Its properties are typed as described under [Properties on an element of unknown class](#properties-on-an-element-of-unknown-class).

A node's groups, its index among an instance's children, and the other things a `.tscn` keeps beside its properties are [`x:` attributes](#x-attributes-on-a-node).

## Properties

A property is an attribute named by the property's C# name. Its type, from the metadata file of the Godot version (`metadata/godot-4.7.json`), decides how the value is read, never the text itself. So `Text="[b]Bold[/b]"` and `Text="{0} items"` are plain strings that need no escape.

- **A property whose type the metadata knows** is written in compact text: `True`/`False`, numbers, enum names, unquoted strings, vectors and colours as their components, and files as `res://` paths. Every type's form is in [values.md](values.md).
- **Every other value** is Godot's own value text, exactly as a `.tscn` holds it: transforms, bases, quaternions, arrays, dictionaries and packed arrays (`Transform="Transform2D(1, 0, 0, 1, 0, 0)"`).
- **A property of a script** (an `[Export]` or `@export`) has no type the format can know without the script. Its value is always Godot value text, with strings quoted: `Title='"[Esc] to close"'`.

A property equal to its default for the node's class is not written.

### Properties on an element of unknown class

On an instanced scene, an `x:Override`, or a tag the metadata does not know, the core cannot know the node's class without opening another file. A property there is typed by its C# name across every Godot class:

- When every class that has the name agrees on its type and enum, the value is compact text: `AnchorsPreset="FullRect"`, `CustomMinimumSize="640, 0"`.
- When they disagree, as for `Position`, `Size` and `Mode`, it is Godot value text: `Position="Vector2(10, 20)"`.
- A name no class has is a script property, kept verbatim and read as Godot value text.

### Slash-named properties

A Godot property whose name holds a `/` is written as a dotted attribute. The part before the first slash is in PascalCase, and the key after it is verbatim, since keys are free strings:

| Godot property | Attribute |
|---|---|
| `theme_override_constants/separation` | `ThemeOverrideConstants.separation` |
| `theme_override_colors/font_color` | `ThemeOverrideColors.font_color` |
| `metadata/_tab_index` | `Metadata._tab_index` |
| `shader_parameter/albedo` | `ShaderParameter.albedo` |

Each theme-override family has one fixed type, so its values are compact text:

- colors are `Color`;
- constants and font sizes are `int`;
- fonts are `Font`;
- icons are `Texture2D`;
- styles are `StyleBox`.

`metadata/…` and `shader_parameter/…` have no fixed type, so their values are Godot value text (`Metadata.table='{"a": 1}'`). A key that is not a legal XML name is written with [`x:Prop`](#xprop).

## Names: from Godot to C#

The names are those Godot's C# bindings generate, computed from Godot's own names by the same rules (`modules/mono/utils/naming_utils.cpp` and `modules/mono/editor/bindings_generator.cpp` in Godot's source):

- **Classes** use their C# name, which differs from Godot's for a few classes (`CPUParticles2D` is `CpuParticles2D`).
- **Properties** go from snake_case to PascalCase (`custom_minimum_size` is `CustomMinimumSize`). A property whose C# name would equal its class's name takes a `_` suffix, as in C#.
- **Enum members** use their C# names, with the shared prefix stripped as the bindings strip it (`MOUSE_FILTER_PASS` is `Pass`, `SIZE_EXPAND_FILL` is `ExpandFill`).
- **Signals** go from snake_case to PascalCase (`button_down` is `ButtonDown`). A signal whose name starts with `_` has no C# name, and one whose C# name equals a property's takes a `Signal` suffix.
- **Script names stay as written.** A script's properties, signals and methods keep the names the script gives them.

## Resources

A resource a node or another resource holds is written in one of three ways.

### A file: `res://`

A resource saved in its own file is referenced by its path. On a property typed as a resource, the path stands alone:

```xml xscn
<Node2D xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Sprite2D Name="Sprite" Material="res://cases/material.tres" Texture="res://art/hero.png" />
</Node2D>
```

Inside Godot value text, and on a property of unknown type, a file is `Resource("res://art/hero.png")`, or `Resource("uid://…", "res://art/hero.png")` with its uid. A `.tscn`'s `ExtResource("1_abc")` is not used: it names a section an `.xscn` does not have (`GX0105`).

### Used once: a property element

A sub-resource used by one property is written inline as a property element. The element is named `<Owner.Property>`, where `Owner` is the parent element's own tag, and it holds exactly one resource element tagged with the resource's class (`GX0112` otherwise):

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <Button Name="Ok">
    <Button.ThemeOverrideStyles.normal>
      <StyleBoxFlat BgColor="0, 0, 0, 1" DrawCenter="False" />
    </Button.ThemeOverrideStyles.normal>
  </Button>
</Control>
```

Inside an instance the owner is the instance's tag (`<c:Modal.Theme>`), and inside an override it is `x:Override` (`<x:Override.Theme>`). A resource element holds its properties as attributes and its own sub-resources as property elements, in the same way.

### Shared: the `x:Resources` table

A sub-resource goes in the root's `<x:Resources>` table, under an `x:Key`, when any of these holds:

- several properties use it;
- Godot value text refers to it, as inside an array or a dictionary;
- the property that uses it has no attribute name.

Inlining a shared resource twice would make Godot load two separate objects. An attribute refers to an entry as `{Key}`, and Godot value text as `SubResource("Key")`:

```xml xscn canonical
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <x:Resources>
    <StyleBoxFlat x:Key="Panel" BgColor="0.25, 0.25, 0.25, 1" CornerRadiusTopLeft="4" />
    <StyleBoxFlat x:Key="Hover" BgColor="0.5, 0.5, 0.5, 1" />
  </x:Resources>
  <PanelContainer Name="Frame" ThemeOverrideStyles.panel="{Panel}">
    <x:Prop Name="metadata/odd key" Value="3" />
    <Button Name="Ok" ThemeOverrideStyles.hover="{Hover}">
      <Button.ThemeOverrideStyles.normal>
        <StyleBoxFlat BgColor="0, 0, 0, 1" DrawCenter="False" />
      </Button.ThemeOverrideStyles.normal>
    </Button>
  </PanelContainer>
</Control>
```

The table rules:

- There is one table, on the root only (`GX0115` for a second; `GX0111` elsewhere). It holds only resource elements.
- Every entry has a non-empty `x:Key` (`GX0108`), unique in the table (`GX0116`). A key is at least one character, with no whitespace, braces, `:`, `,` or `"`.
- `x:Key` appears nowhere else (`GX0102`).

Entries may refer to each other by key.

A conversion names a new key after its type (`StyleBoxFlat1`, `CanvasItemMaterial1`). A save from the editor keeps every existing key, so references and diffs stay stable.

`{x:Null}` sets a resource property to null.

## Files and uids

A file is referred to by its `res://` path where it is used. Each referenced file's uid is kept in the `<x:Uids>` table, the root's first child, so a reference still resolves after the file moves:

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <x:Uids>
    <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
  </x:Uids>
  <TextureRect Name="Frame" Texture="res://art/frame.png" />
</Control>
```

The rules:

- `<x:Uids>` holds only `<x:Uid Path="…" Value="uid://…" />` entries, written sorted by path.
- There is one table, on the root only.
- The tools maintain it: the editor fills it on save, and rewrites an entry when a file is moved or renamed in the FileSystem dock.

A scene's or resource's own uid is not in the file. It lives in the `.uid` sidecar beside it (`main.xscn.uid`), as for a script, so commit those sidecars.

## Instanced scenes

An instance of another scene is an element too, written in one of two forms.

**A scene namespace** maps a folder to a prefix on the root. A scene in that folder is then a tag whose local name is the file's base name:

```xml xscn canonical
<Control
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    xmlns:c="res://ui/"
    xmlns:w="res://world/"
    Name="Root">
  <x:Uids>
    <x:Uid Path="res://ui/Modal.tscn" Value="uid://b1modal" />
    <x:Uid Path="res://world/2-arena.xscn" Value="uid://c2arena" />
  </x:Uids>
  <c:Modal Name="Confirm" x:Editable="True" x:Index="2">
    <x:Override Path="Centre/Panel" Visible="False" x:Index="1">
      <Label Name="Added" Text="Hi" />
    </x:Override>
  </c:Modal>
  <w:Map Name="Map" />
  <x:Instance Name="Arena" Scene="res://world/2-arena.xscn" />
</Control>
```

`<w:Map>` is `res://world/Map.xscn`. The extension is `.xscn` unless an `x:Uids` entry names the same folder and base name with another extension, as `res://ui/Modal.tscn` does for `<c:Modal>` above.

**`<x:Instance Scene="res://…">`** names the file outright. The writer uses it when no namespace form reads back as the same path: a base name that is not an XML name (`2-arena`), or a `.tscn` with no `x:Uids` entry.

Scene prefixes name elements only. An attribute with a scene prefix is not read (`GX0114`).

### Overrides

A node inside the instanced scene that this scene changes is an `<x:Override Path="…">`, a direct child of the instance element.

- `Path` is the node's path relative to the instance (`GX0108` without it).
- The override holds only the changed properties. Nodes this scene adds under that node nest inside it, as `Added` does above.
- Overrides never nest inside each other: each names its full path from the instance.

The path lets the file be read without opening the instanced scene. Properties on an instance or an override are typed as on an [element of unknown class](#properties-on-an-element-of-unknown-class).

### Editable children

`x:Editable="True"` on an instance element marks its children editable, the `.tscn`'s `[editable path="…"]`. On an `x:Override` it marks a nested instance editable.

### Inherited scenes

A scene that inherits another has the instanced scene as its root. That is a scene-namespace tag, or `<x:Instance>`, at the root, with its overrides and added nodes inside:

```xml xscn
<x:Instance
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    Name="Root"
    Scene="res://cases/nested.tscn">
  <x:Override Path="A" Position="Vector2(1, 1)" />
  <Node Name="Extra" />
</x:Instance>
```

### Instance placeholders

A node loaded later through `InstancePlaceholder` is an `<InstancePlaceholder>` element holding the scene's path in `x:InstancePlaceholder`:

```xml xscn
<Node xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <InstancePlaceholder Name="Later" x:InstancePlaceholder="res://cases/lone_root.tscn" />
</Node>
```

A placeholder's properties are never dropped as defaults, since Godot saves them against the instanced scene.

## Signals

A connection is written on the node that emits the signal.

**The common case** is a connection received by the scene root, with no flags beyond Godot's default persist, no binds and no unbinds. It is an attribute named by the signal's C# name, whose value is the method:

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root" Script="res://root.gd">
  <Button Name="Go" ButtonDown="OnGoDown" Pressed="OnGoPressed" />
</Control>
```

An attribute is read as a signal only on an element of a known class with no property of that name. On an instance, an override or an element of an unknown class, an attribute named like a signal is a property, never a connection; write the connection there as `<x:Connect>`.

**Every other connection** is an `<x:Connect>` child of the emitter:

```xml xscn canonical
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root" Script="res://root.gd">
  <Button Name="Go" ButtonDown="OnGoDown" Pressed="OnGoPressed">
    <x:Connect Signal="Pressed" To="Panel" Method="Hide" />
    <x:Connect Signal="Toggled" Method="OnToggled" Flags="Deferred, OneShot" />
    <x:Connect Signal="ButtonUp" Method="OnUp" Binds='[1, "a"]' Unbinds="1" />
  </Button>
  <PanelContainer Name="Panel" />
  <x:Instance Name="Dialog" Scene="res://ui/dialog.xscn">
    <x:Connect Signal="Pressed" Method="OnDialog" />
  </x:Instance>
</Control>
```

| Attribute | Meaning | Default |
|---|---|---|
| `Signal` | The signal's C# name, mapped by the emitter's class. A script's signal keeps its own name. | Required, or `GodotSignal` |
| `GodotSignal` | The signal's Godot name, verbatim. The writer uses it where `Signal` would read back as another signal, such as a C# script's `Finished` beside the engine's `finished`. | |
| `To` | The receiver's path from the scene root. | `.`, the root |
| `Method` | The method called. It is never renamed. | Required |
| `Flags` | Names from `Object.ConnectFlags` joined with `, ` (`Deferred, OneShot`), or the integer. Persist is implied and never written. | Persist |
| `Binds` | Extra arguments, as a Godot array: `Binds='[1, "a"]'`. | None |
| `Unbinds` | The number of the signal's arguments dropped from the end. | `0` |
| `FromUidPath`, `ToUidPath` | Godot 4.7's id paths to the emitter and receiver, integers joined with `, `. | Written only where the `.tscn` has them. |

An `<x:Connect>` with both `Signal` and `GodotSignal` is `GX0118`, and one with neither is `GX0108`.

## `x:` attributes on a node

These go on a node, an instance or an override, and the writer puts them after the properties, in this order:

| Attribute | Value | The `.tscn` it stands for |
|---|---|---|
| `x:Editable` | `True` | `[editable path="…"]` for this instance |
| `x:Groups` | Group names joined with `, ` (`x:Groups="buttons, ui"`), or a Godot array of strings when a name holds a comma, starts with `[` or has spaces at an edge | `groups=[…]` |
| `x:Index` | Integer | `index=`, the node's position among an instance's children |
| `x:InstancePlaceholder` | `res://` path | `instance_placeholder=` |
| `x:NodePaths` | Property names joined with `, ` | `node_paths=`, the script properties that hold `NodePath`s to nodes |
| `x:Owner` | Node path | `owner=` on a node other than the root's children |
| `x:OwnerUidPath` | Integers joined with `, ` | `owner_uid_path=` (Godot 4.7) |
| `x:ParentIdPath` | Integers joined with `, ` | `parent_id_path=` (Godot 4.7) |
| `x:UniqueId` | 32-bit integer | `unique_id=` (Godot 4.7), kept so a save does not churn ids |

Each is written only where the `.tscn` has it. Any other `x:` attribute, or one of these on an element that cannot carry it, is `GX0102`.

Unprefixed attributes the format reserves:

- `Name` (and `Scene` on `x:Instance`) on nodes;
- `Path` on `x:Override` and `x:Uid`;
- `Value` on `x:Uid` and `x:Prop`;
- `Name` on `x:Prop`;
- the `x:Connect` attributes above;
- `Script`.

## `x:Prop`

`<x:Prop Name="…" Value="…" />` sets a property by its Godot name, verbatim, with its value as Godot value text. The writer uses it for any property that cannot be a plain attribute:

- a slash-named key that is not an XML name (`metadata/odd key`);
- a script property whose name matches an engine property's C# name on the class;
- a value whose compact text would not read back as itself;
- a string holding a character XML 1.0 cannot hold, written with a `\uXXXX` escape;
- a property set twice, or one with a comment before it.

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <x:Prop Name="metadata/odd key" Value="1" />
</Control>
```

## Comments

XML comments are kept: a comment belongs to the element, property element, connection, uid or table entry that follows it, and a save from the editor carries it over. A comment belongs only between elements; one inside a start tag is `GX0002`.

```xml xscn canonical
<!-- before root -->
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="Root">
  <!-- before uids -->
  <x:Uids>
    <!-- before uid -->
    <x:Uid Path="res://art/frame.png" Value="uid://dframe" />
    <!-- end of uids -->
  </x:Uids>
  <!-- before resources -->
  <x:Resources>
    <!-- before resource -->
    <StyleBoxFlat x:Key="Panel" BgColor="0, 0, 0, 1">
      <!-- end of resource -->
    </StyleBoxFlat>
    <!-- end of resources -->
  </x:Resources>
  <!-- before prop -->
  <x:Prop Name="metadata/odd key" Value="1" />
  <!-- before property element -->
  <Control.Theme>
    <!-- before inline resource -->
    <Theme DefaultFontSize="20" />
    <!-- end of property element -->
  </Control.Theme>
  <!-- before connect -->
  <x:Connect Signal="Resized" Method="OnResized" />
  <!-- before node -->
  <Button Name="Go">
    <!-- end of node -->
  </Button>
  <!-- end of root -->
</Control>
```

A comment a save cannot place, because what it preceded is gone, is dropped with a warning naming it (`GXC0117`), never silently.

## `.xres` files

The root of an `.xres` is the resource, tagged with its class (`GX0117` if the class is not a Resource). It carries the namespace declaration and no `Name`. Its properties are attributes, its sub-resources property elements or `x:Resources` entries, and it may hold `x:Uids`:

```xml xres canonical
<StyleBoxFlat
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    BgColor="0.25, 0.5, 0.75, 1"
    BorderWidthLeft="2"
    ContentMarginLeft="8"
    CornerRadiusTopLeft="6" />
```

Nodes and node-only elements (`x:Connect`, `x:Override`) do not belong in an `.xres` (`GX0111`). A resource with a script carries `Script="res://…"` like a node.

## The canonical form

Every save, by the editor or by `godot-xaml format`, writes one fixed layout, so diffs come only from real changes. The rules:

- **Encoding:** UTF-8 without a BOM, LF line ends, a final newline, no XML declaration.
- **Indentation:** elements indent two spaces, with no blank lines.
- **Start tags:** a start tag stays on one line when it fits in 100 columns. Otherwise the tag name stands alone, and each attribute goes on its own line, indented four spaces past the tag.
- **Attribute order:**
  1. the namespace declarations, on the root: `xmlns:x`, then the scene namespaces;
  2. `Name`, then `Scene` on `x:Instance`, or `Path` on `x:Override`;
  3. `Script`;
  4. every property and signal attribute, in ordinal order of its name;
  5. the `x:` attributes, in the order of the table above, `x:UniqueId` last.

  On a resource, `x:Key` comes first.
- **Content order** inside an element:
  1. `x:Uids`, sorted by path;
  2. `x:Resources`;
  3. property elements and `x:Prop`, sorted by Godot name;
  4. `x:Connect`;
  5. child nodes in scene order.
- **Defaults:** a property equal to its default for the element's class is omitted, on a known class only. `layout_mode` is omitted only under a Container, which decides it; elsewhere it is always written, since Godot's loader honours a written value.
- **Signals as attributes:** only the leading run of common-case connections whose names sort in order is written as attributes. The rest are `x:Connect`.
- **Prefix clashes:** a scene prefix that would clash with the format's is written `x1`, `x2`.

Hand line breaks and attribute order are not kept: `godot-xaml format` rewrites a file into this form, and `godot-xaml format --check` lists the files that are not in it.

## What a `.tscn` holds that an `.xscn` does not

- **`load_steps` and `format`** are not stored: Godot 4.7.2 no longer writes the first, and the second is chosen again when converting back.
- **`ext_resource` and `sub_resource` ids** are not stored: references are paths and keys. Converting back mints ids from where each resource is used, so an edit in one place never renumbers another.
- **`script_class`** is not stored; it follows from the script.
- **The file's own uid** is in its `.uid` sidecar.

Converting back to `.tscn` restores Godot's property order, except among a script's exports and inside a dynamic family such as a Theme's or a TileSet's properties, which an alphabetical `.xscn` cannot tell apart.
