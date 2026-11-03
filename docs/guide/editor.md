# Working in the editor

With the addon installed ([Getting started](getting-started.md)), `.xscn` and `.xres` files behave in the editor as `.tscn` and `.tres` do. This page covers what is worth knowing beyond that.

## Opening and saving

- **Opening:** `.xscn` files are listed in Open Scene, in scene pickers, and in the FileSystem dock, and open with a double-click. `.xres` files show in every resource picker of a matching type.
- **Saving:** Scene > Save, Save As and the FileSystem dock's new-resource commands accept `.xscn` and `.xres`. A scene saved as `.tscn` stays a `.tscn`: the extension decides the format.
- **Running:** the scene runs, and is instanced into other scenes, `.tscn` or `.xscn`, as any scene is.

Each load converts the file to Godot's own text format in memory and loads that. So a scene behaves exactly as its `.tscn` would, sub-resources and their undo history included.

## What a save keeps

Every save writes the [canonical form](../format/README.md#the-canonical-form): one fixed layout, so two saves of the same scene are byte for byte the same, and a diff shows only real changes.

- **Comments are kept.** An XML comment belongs to the element after it, and a save carries it over, even when the node is renamed or moved. A comment whose element was deleted is dropped with a warning in the Output panel naming it.
- **`x:Resources` keys are kept.** A shared resource keeps its key, and every `{Key}` reference with it, after you edit it.
- **Hand formatting is not kept.** Line breaks and attribute order you wrote by hand are replaced by the canonical layout. `godot-xaml format` applies the same layout outside the editor.

## Uids and `.uid` sidecars

Godot gives every scene and resource a uid, so a reference survives a move. A `.tscn` keeps its uid in its header. An `.xscn` or `.xres` keeps it in a sidecar file beside it, as a script does: `main_menu.xscn.uid`.

- **Commit the sidecars.** Without one, the file gets a new uid on the next open, and uid references to it break.
- **References keep their uids too.** Each file a scene refers to has its uid in the scene's `<x:Uids>` table, which the editor fills on save.
- **Move and rename in the FileSystem dock.** The editor rewrites the paths and uids inside every scene that refers to the moved file. A move outside the editor still resolves through the uids, until the next save writes the new paths.

## The first open

Godot scans the project's files before any addon's classes register. On the first open after installing, or after deleting `.godot/`, `.xscn` and `.xres` files are not yet listed in the FileSystem dock, and no `.uid` sidecars exist. Close the project and open it again: the second open lists them and writes the sidecars.

## Errors in a file

A file that does not parse fails to load, as a broken `.tscn` does. The Output panel names the file and each problem:

```text
res://ui/main_menu.xscn:4:6: GX0104: <Button> has no Name; every node needs one.
```

Fix the text and reload the scene. Every code is listed in [the diagnostics reference](../format/diagnostics.md). `godot-xaml check` reports the same problems without opening the editor.

## C# and GDScript

Property names in the file are those of Godot's C# API (`SizeFlagsHorizontal`), whatever language the scripts are in. A script's own exported properties keep the names the script gives them, whether `speed` in GDScript or `Speed` in C#. Their values are in Godot's value text, since the format cannot know their types without running the script.
