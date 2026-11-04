# Working in the editor

With the addon installed ([Getting started](getting-started.md)), `.xscn` and `.xres` files behave in the editor as `.tscn` and `.tres` do. This page covers what is worth knowing beyond that.

## Opening and saving

- **Opening:** `.xscn` files are listed in Open Scene, in scene pickers, and in the FileSystem dock, and open with a double-click. `.xres` files show in every resource picker of a matching type.
- **Saving:** Scene > Save, Save As and the FileSystem dock's new-resource commands accept `.xscn` and `.xres`. A scene saved as `.tscn` stays a `.tscn`: the extension decides the format.
- **Running:** the scene runs, and is instanced into other scenes, `.tscn` or `.xscn`, as any scene is.

Each load converts the file to Godot's own text format, writes that to a temporary file outside the project, and loads it with Godot's text loader. So a scene behaves as its `.tscn` would, its sub-resources included.

A change made to an open `.xscn` outside the editor is noticed when the editor window regains focus, as for a `.tscn`: Godot asks whether to reload it from disk.

## What a save keeps

Every save writes the [canonical form](../format/README.md#the-canonical-form): one fixed layout, so two saves of the same scene are byte for byte the same, and a diff shows only real changes.

- **Comments are kept.** An XML comment belongs to the element after it, and a save carries it over, even when the node is renamed or moved. A comment whose element, property, connection or table entry was removed is dropped with a warning in the Output panel naming it (`GXC0117`).
- **`x:Resources` keys are kept.** A shared resource keeps its key, and every `{Key}` reference with it, after you edit it.
- **Hand formatting is not kept.** Line breaks and attribute order you wrote by hand are replaced by the canonical layout. `godot-xaml format` applies the same layout outside the editor.

## Uids and `.uid` sidecars

Godot gives every scene and resource a uid, so a reference survives a move. A `.tscn` keeps its uid in its header. An `.xscn` or `.xres` keeps it in a sidecar file beside it, as a script does: `main_menu.xscn.uid`.

- **Commit the sidecars.** Without one, a fresh clone, or any checkout without the `.godot/` cache, gives the file a new uid. A reference that names the file only by uid then breaks, and one that also carries the path falls back to the path with a warning.
- **References keep their uids too.** Each file a scene refers to has its uid in the scene's `<x:Uids>` table, which the editor fills on save.
- **Move and rename in the FileSystem dock.** The editor rewrites the paths and uids inside every scene that refers to the moved file. A file moved outside the editor together with its `.uid` sidecar still resolves through its uid once the editor has rescanned, until the next save writes the new path.

## The first open

Godot scans the project's files before any addon's classes register. On the first open after installing, or after deleting `.godot/`, `.xscn` and `.xres` files are not yet listed in the FileSystem dock, and no `.uid` sidecars exist. Close the project and open it again: the second open lists them and writes the sidecars. A headless `godot --headless --path <project> --import` does the same rescan, and a scripted run needs it ([Getting started](getting-started.md#install-the-addon)).

## Errors in a file

A file that does not parse fails to load, as a broken `.tscn` does. The Output panel shows an error for each problem, naming the file, the 1-based line and column, and the code:

```text
res://ui/main_menu.xscn:4:6: GX0104: <Button> has no Name; every node needs one.
```

A file that loads can show parse warnings in the Output panel too, in the same form (`GX0107` for a tag no class has). Fix the text and reload the scene. Every code is listed in [the diagnostics reference](../format/diagnostics.md).

`godot-xaml check` reports the parse problems (`GX` codes) without opening the editor. A few problems show only when the file is converted for a load (`GXC` codes, such as a `{Key}` no `x:Resources` entry has), and those appear only in the Output panel.

## C# and GDScript

Property names in the file are those of Godot's C# API (`SizeFlagsHorizontal`), whatever language the scripts are in. A script's own exported properties keep the names the script gives them, whether `speed` in GDScript or `Speed` in C#. Their values are in Godot's value text, since the format cannot know their types without running the script.

## Live preview server

A client such as the coming Visual Studio preview pane can send the text of an `.xscn` to the open Godot editor and get back Godot's image of the scene and the rectangle of each Control. The text need not be saved, and the server never touches your file.

- **Turning it on:** set the project setting `godot_xaml/preview_server/enabled`. It is off by default, so no editor listens until you ask. It is in Project Settings once the addon is enabled. Turning it off or on takes effect at once, and the server starts again on its own after a C# rebuild, with a new port.
- **Who can reach it:** only programs on your machine. One server runs per editor. While it runs, the editor writes a discovery file in your user's local data folder (`%LOCALAPPDATA%\godot-xaml\editors\` on Windows, the platform's equivalent elsewhere) and removes it when the server stops. The first request on a connection must carry the token from that file.
- **Scripts:** by default nothing the previewed text references runs a script. Scripts are stripped, with the exported values kept, and a file that cannot be proven free of scripts is refused with `GXC0121` (the cases are in [the diagnostics reference](../format/diagnostics.md)). To preview with scripts, add the project setting `godot_xaml/preview_server/allow_scripts` as `true` by hand; the addon does not list it.
- **Waiting:** a render waits while the editor scans or imports files.
