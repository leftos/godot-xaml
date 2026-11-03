# Exporting a game

An exported game loads `.xscn` and `.xres` as Godot binary scenes and resources. No addon code runs in the game, and no XML is shipped.

## What the export does

With the `godot-xaml` plugin turned on (Project > Project Settings > Plugins), every export:

1. converts each `.xscn` and `.xres` it includes to Godot binary (`.scn` or `.res`);
2. adds a `.remap` beside the original path, so `load("res://ui/main_menu.xscn")` and every reference in other scenes find the binary copy;
3. leaves the XML out of the pack.

Nothing else is needed: export presets, the export dialog and `--export-release` on the command line work as usual.

## Requirements

- **The plugin must be on.** Without it, the export ships the raw XML, which the game cannot load.
- **The addon's scripts must stay in the export.** The game's assembly is built from them. If an include or exclude filter in the export preset drops `addons/godot_xaml/`, the export reports that it would drop the loaders and names the filter to fix.
- **The project needs a solution file** named after `dotnet/project/assembly_name`, or the C# step of the export fails ([Getting started](getting-started.md#install-the-addon), step 1).

## When a file fails to convert

A file that does not load is skipped, and the export logs an error naming it:

```text
ERROR: godot-xaml: res://ui/broken.xscn: <reason>
```

The export dialog shows "Failed." A headless export (`godot --headless --export-release …`) still exits 0, because Godot gives an export plugin no way to change its exit code. A build script should therefore search the log for `ERROR: godot-xaml:`.

Running `godot-xaml check` on the project before exporting catches the same errors earlier.
