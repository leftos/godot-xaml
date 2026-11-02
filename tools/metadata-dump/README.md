# metadata-dump

Writes `metadata/godot-<major>.<minor>.json`: every engine class with its parent, whether it can be instantiated, its own enums and its own properties, each property with its Variant type, enum or bitfield type, hint, hint string, usage flags, class name and default value. The file is committed; `metadata/godot-4.7.json` is the current one.

## What it reads

- **ClassDB**, from a headless Godot run of `dump.gd`: the classes, parents, `can_instantiate`, each class's own properties (`class_get_property_list(class, true)`), defaults (`class_get_property_default_value`) and enums.
- **`extension_api.json`**, from `godot --headless --dump-extension-api`: the global enums (`Side`, `HorizontalAlignment`, …) and each property's enum or bitfield type. That type is read from the property's getter return value (`enum::Control.MouseFilter`, `bitfield::Control.SizeFlags`), since the property entry itself reads `int` for an enum registered by hint alone; the getter is looked up on the class and then its ancestors.

## Format

Top level: `godot` (the engine's version string), `globalEnums`, `classes`.

- A class: `parent` (null for `Object`), `instantiable`, `enums`, `properties`.
- An enum: `{"bitfield": bool, "values": [[name, int], …]}`, members in declaration order.
- A property: `name`, `type` (Variant type name), `enumType` (`"Control.MouseFilter"`, or null), `bitfield`, `hint`, `hintString`, `usage`, `className`, `default`.

`default` is Godot's own value text (`var_to_str`: `Vector2(0, 0)`, `""` as `"\"\""`, `"null"` for a null default). The key is left out when no default is known: the class cannot be instantiated, or the default is an Object.

Properties keep ClassDB order; category, group and subgroup entries and names containing `/` are left out. Keys are sorted, the indent is two spaces, lines end in LF and the file ends with a newline, so two runs give identical bytes.

## Run it

```
pwsh -NoProfile -File tools/metadata-dump/dump.ps1 -Godot <path to a Godot 4.7 binary>
```

`-Godot` falls back to `$env:GODOT_BIN`; on Windows pass the console build (`Godot_console.exe`) so Godot's output and exit code reach the shell. `-OutDir` writes somewhere other than `metadata/`. The extension API file goes to `.tmp/metadata-dump/`. The script exits with Godot's exit code.

## Check it

```
godot --headless --path tools/metadata-dump --script res://check.gd -- --file <absolute path to the JSON>
```

`check.gd` prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` for each known fact (Control's parent, `mouse_filter` and `size_flags_horizontal` enum links, `Node2D.position` and `Label.text` defaults, no defaults on `CanvasItem`, the `Side` global enum, `layout_mode` stored, LF endings), and exits 1 when any failed.

## When to re-run it

On each new Godot minor version: run the dump with that version's binary, which writes a new `godot-<major>.<minor>.json`, and run the check against it. A patch release keeps the API, so the file for its minor version stays.
