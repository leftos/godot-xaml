# Exporting a game

An exported game loads `.xscn` and `.xres` as Godot binary scenes and resources, so no addon code runs when a scene or resource loads, and no XML is shipped.

## What the export does

With the `godot-xaml` plugin turned on (Project > Project Settings > Plugins), every export:

1. converts each `.xscn` and `.xres` it includes to Godot binary (`.scn` or `.res`);
2. adds a `.remap` beside the original path, so `load("res://ui/main_menu.xscn")` and every reference in other scenes find the binary copy;
3. leaves the XML out of the pack.

Nothing else is needed: export presets, the export dialog and `--export-release` on the command line work as usual.

## Requirements

- **The plugin must be on.** Without it, the export ships the raw XML instead of binary copies; the game still loads it through the addon's loaders, but more slowly, and it is not a supported setup.
- **The addon's `.cs` files must stay in the export.** Godot registers the loaders from the exported list of script classes, and a loader a filter drops is never registered. If an include or exclude filter in the export preset drops `addons/godot_xaml/`, the export reports that it would drop the loaders and names the filter to fix.
- **The project needs a solution file** (`.sln` or `.slnx`) that contains its `.csproj`, or the C# step of the export fails ([Getting started](getting-started.md#install-the-addon), step 1).

## When a file fails to convert

A file that does not load is skipped, and the export logs an error naming it:

```text
ERROR: godot-xaml: res://ui/broken.xscn: <reason>
```

The export dialog shows "Failed." A headless export (`godot --headless --export-release …`) still exits 0, because Godot gives an export plugin no way to change its exit code. A build script should therefore search the log for `ERROR: godot-xaml:`.

Running `godot-xaml check` on the project before exporting catches the parse errors earlier.
