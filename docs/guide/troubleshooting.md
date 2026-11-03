# Troubleshooting

## Every scene fails with "No loader found"

The project's `.csproj` does not compile `addons/godot_xaml/`, so the loader classes are not in the game's assembly. Remove any `<Compile Remove="addons\**" />` from the `.csproj`, check that `<EnableDynamicLoading>true</EnableDynamicLoading>` is set, and build again.

## The plugin turns itself off

The editor found no built assembly when it opened, and disabled the plugin. Run `dotnet build` in the project, then turn the plugin on again in Project > Project Settings > Plugins.

## `.xscn` files do not show in the FileSystem dock

This happens on the first open after installing the addon, or after deleting `.godot/`. Godot scanned the project before the addon registered. Close the project and open it again ([Working in the editor](editor.md#the-first-open)).

## Every scene fails with "Failed loading scene"

Godot's global class cache (`.godot/global_script_class_cache.cfg`) was written before the addon arrived, so no loader is registered. Import the project again, by opening it in the editor or with `godot --headless --path <project> --import`. The same fix applies after updating the addon by unzipping.

## Running a `.xscn` from the command line plays the main scene

Godot takes a bare path as a scene only when it ends in `.scn`, `.tscn`, `.escn`, `.res` or `.tres`. Pass the scene with `--scene`: `godot --path <project> --scene res://ui/main_menu.xscn`.

## A scene does not load

The Output panel names the file, the line and column, and each problem with a `GX` code, such as `res://ui/main_menu.xscn:4:6: GX0104: <Button> has no Name; every node needs one.` Look the code up in [the diagnostics reference](../format/diagnostics.md), fix the text, and reload. `godot-xaml check <file>` prints the parse problems (`GX` codes) from the command line; problems found while converting for a load (`GXC` codes) show only in the Output panel.

## A uid reference breaks after a move

A file whose `.uid` sidecar was not committed gets a new uid. Commit every `*.xscn.uid` and `*.xres.uid`, and move and rename files in the FileSystem dock, which rewrites the references to them ([Uids and sidecars](editor.md#uids-and-uid-sidecars)).

## The exported game cannot load a scene

Check, in order:

1. **The plugin is on.** Without it, the raw XML is exported instead of binary copies.
2. **The export log has no `ERROR: godot-xaml:` lines.** A file that failed to convert is skipped, and a headless export still exits 0.
3. **The preset's filters keep `addons/godot_xaml/`.** The export says so when they do not.

See [Exporting a game](export.md).

## A property I set is missing from the file

A value equal to the property's default for the node's class is not written; that is the same as Godot's `.tscn`. `layout_mode` is written outside a Container, since Godot's loader honours a written value.

## The formatter reordered my file

Every save, by the editor or by `godot-xaml format`, writes [the canonical layout](../format/README.md#the-canonical-form). Hand line breaks and attribute order are not kept; comments are.

## A conversion warns about a path it did not rewrite

`GXP0006` is a script string that builds a path at runtime, or holds one inside longer text. Change it by hand. Every `GXP` warning is explained in [the diagnostics reference](../format/diagnostics.md#project-conversion-gxp).

## Reporting a problem

File an issue at [github.com/leftos/godot-xaml/issues](https://github.com/leftos/godot-xaml/issues) with:

- the Godot version;
- the godot-xaml version;
- the exact message;
- when you can share it, the smallest `.xscn` that shows the problem.
