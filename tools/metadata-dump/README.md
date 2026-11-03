# metadata-dump

Writes `metadata/godot-<major>.<minor>.json`: every engine class with its parent, whether it can be instantiated, its own enums and its own properties, plus the defaults it changes for inherited ones, each property with its Variant type, enum or bitfield type, hint, hint string, usage flags, class name and default value. The file is committed; `metadata/godot-4.7.json` is the current one.

## What it reads

- **ClassDB**, from a headless Godot run of `dump.gd`: the classes, parents, `can_instantiate`, each class's own properties (`class_get_property_list(class, true)`), own signals (`class_get_signal_list(class, true)`), defaults (`class_get_property_default_value`) and enums.
- The run fails when Godot logs any error while it reads ClassDB, so a new error cannot hide; the one harmless error `OpenXRRenderModelManager` logs when ClassDB builds it to read its defaults is muted for that class alone.
- **`extension_api.json`**, from `godot --headless --dump-extension-api`: the global enums (`Side`, `HorizontalAlignment`, …) and each property's enum or bitfield type. That type is read from the property's getter return value (`enum::Control.MouseFilter`, `bitfield::Control.SizeFlags`), since the property entry itself reads `int` for an enum registered by hint alone; the getter is looked up on the class and then its ancestors.

## Format

Top level: `godot` (the engine's version string), `globalEnums`, `classes`.

- A class: `parent` (null for `Object`), `instantiable`, `enums`, `properties`, `signals`, and `hasDefaults`, `defaultOverrides`, `themeOverrides` and `listOrder` on some (below). `listOrder`, on a class that declares a slash-named property itself, lists every property name the class declares in ClassDB order, slash-named ones included; `properties` leaves slash-named ones out, so it alone serves typing and C# names, and `listOrder` serves property order.
- An enum: `{"bitfield": bool, "values": [[name, int], …]}`, members in declaration order.
- A property: `name`, `type` (Variant type name), `enumType` (`"Control.MouseFilter"`, or null), `bitfield`, `hint`, `hintString`, `usage`, `className`, `default`.
- A signal: `name` and `args`, an array in ClassDB order of `{name, type, className}` per argument. `type` is the argument's Variant type (`Object` for a node argument) and `className` its class (`Area2D`'s `body_entered` takes `body`, `Object`, `Node2D`). A class with no signals of its own has `[]`.

`default` is Godot's own value text (`var_to_str`: `Vector2(0, 0)`, `""` as `"\"\""`, `"null"` for a null default of an Object or Nil property). The key is left out when no default is known: the class cannot be instantiated (`BaseButton`'s own properties have none), the default is an Object, or ClassDB reports null for a property whose type is neither Object nor Nil.

An instantiable class also has `defaultOverrides` when its own default for an inherited property differs from what its parent chain gives: `{"<property>": "<Godot value text>"}` (`Button` has `focus_mode` `"2"`, since `BaseButton`'s constructor sets it to All; `Label` has `mouse_filter` `"2"`). The key is absent when there are none, and the same null rule applies.

An instantiable class for which ClassDB reports no default for any property that can hold one has `"hasDefaults": false`, written only when false. These are the classes Godot registers as virtual (`BaseButton`, `Range`, `VisualInstance3D`, `Texture`, …), which `ClassDB::class_get_default_property_value` never instantiates, so Godot saves every stored property of such a node; a class with no such property at all (`Object`, `RefCounted`) is marked too.

A property's effective default for a class is none when the class has `hasDefaults` false; else the class's own declaration's `default` (none when it has no `default` key); else the class's override; else the parent's effective default.

Properties keep ClassDB order; category, group and subgroup entries and names containing `/` are left out.

A class that is `Control` or `Window` or inherits one also has `themeOverrides`: an array, in Godot's order, of `{name, type, hint, hintString}` for each `theme_override_<family>/<item>` entry (`Label` has `theme_override_colors/font_color`, type `Color`; `Button` has `theme_override_styles/normal`, type `Object`, hint string `StyleBox`).

It has no `usage` or `default`. The list is the full one, inherited items included, since abstract ancestors cannot be instantiated, and it is empty for a class with no items (`Control`, `Container`). Every other class has no such key. The dump instantiates and frees each such class to read it, and fails the run, naming the classes, when one cannot be instantiated.

Keys are sorted, the indent is two spaces, lines end in LF and the file ends with a newline, so two runs give identical bytes.

## Run it

```
pwsh -NoProfile -File tools/metadata-dump/dump.ps1 -Godot <path to a Godot 4.7 binary>
```

`-Godot` falls back to `$env:GODOT_BIN`; on Windows pass the console build (`Godot_console.exe`) so Godot's output and exit code reach the shell. `-OutDir` writes somewhere other than `metadata/`. The extension API file goes to `.tmp/metadata-dump/`. The script exits with Godot's exit code.

## Check it

```
godot --headless --path tools/metadata-dump --script res://check.gd -- --file <absolute path to the JSON>
```

`check.gd` prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` for each known fact, and exits 1 when any failed. The facts:

- classes and properties: Control's parent, `Node2D.position` and `Label.text` defaults, no defaults on `CanvasItem`, `layout_mode` stored, no `/` name in `Control.properties`;
- default overrides: `Button` overrides `focus_mode` with `"2"`, `Label` overrides `mouse_filter` with `"2"`, `Control` has no `focus_mode` override, `BaseButton.disabled` has no `default` key, and `BaseButton` has `hasDefaults` false while `Button` has no such key, and `OpenXRRenderModelManager` keeps its `tracker` and `make_local_to_pose` defaults with no `hasDefaults` key;
- enums: the `mouse_filter` and `size_flags_horizontal` enum links, the `Side` global enum;
- theme overrides: `Label`'s `font_color` is a `Color`, `Button`'s `styles/normal` is an `Object` of `StyleBox`, `Window` has overrides, `Node2D` has no `themeOverrides`;
- signals: `BaseButton` declares `pressed` and `Button` does not, `Node` declares `ready`, `Area2D`'s `body_entered` takes a `Node2D` `body`;
- the file: LF endings.

## When to re-run it

On each new Godot minor version: run the dump with that version's binary, which writes a new `godot-<major>.<minor>.json`, and run the check against it. A patch release keeps the API, so the file for its minor version stays.
