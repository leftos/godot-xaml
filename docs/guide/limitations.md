# Limitations

What godot-xaml does not do, or does differently from a `.tscn`.

## Platform

- **Godot 4.7 with .NET, tested on 4.7.2.** The addon is C#, so the GDScript-only build of Godot cannot load it.
- **One metadata file per Godot version.** Property types and enum names come from Godot 4.7's metadata, shipped in the package. A later Godot version that renames or retypes a property can change how a file reads; such a release of godot-xaml will say so and say how to upgrade.

## Format

- **Order inside a script's exports, and inside a Theme's or a TileSet's own properties, is not kept.** An `.xscn` sorts attributes alphabetically. Converting back to `.tscn` restores Godot's order for everything else.
- **An editor save drops a stored default.** The conversion keeps a property a `.tscn` stores at its class's default, but Godot's own saver omits a default, so saving the scene in the editor writes it without.
- **Hand formatting is not kept.** Every save writes the canonical layout; comments are kept.
- **Script properties are untyped.** The format cannot know an `[Export]` or `@export` property's type without the script, so its value is Godot's value text, strings quoted (`title='"Main menu"'`).
- **`Signal` and `Callable` values** can only be empty (`Signal()`, `Callable()`), as in any saved Godot file.

## Projects

- **Files Godot loads before any addon stay `.tscn` and `.tres`:** the files named by the project settings for the custom theme and font, the default bus layout, the custom mouse cursor, the default environment and the translations, and what they reference. No loader for `.xscn` exists that early.
- **A path a script builds at runtime is not rewritten** by `convert --project`; it warns about them (`GXP0006`).
- **Files outside the project, code that tests a `.tscn` extension, and code that reads scene files as text are not rewritten** by `convert --project`, which edits only the project's scripts and `project.godot` where a string is exactly a converted path. A project's tests and tools beside the game are the usual case ([Converting a project](converting.md#what-it-does-not-rewrite)).
- **`godot-xaml check` reports parse problems only.** Problems found while converting a file for a load (`GXC` codes) show in the editor's Output panel.
- **The first open after installing** does not list `.xscn` files; the second does ([Working in the editor](editor.md#the-first-open)).
- **The editor loads each changed `.xscn` in full to read its node groups** (for Project Settings > Global Groups), where Godot reads a `.tscn` as text. It does this on the first open of a project without `.godot/scene_groups_cache.cfg` (on a fresh clone that is the second open, since the first lists no `.xscn`), after a file scan for each new or changed scene, and on each save or update of a scene for that one file.

  It runs on the main thread, so the editor waits under "Updating Scene Groups". A scene costs roughly ten times what it costs as `.tscn`: a few seconds for a thousand scenes on a desktop. A broken `.xscn` logs its load errors at each of these points. Godot 4.7.2 gives a loader no cheaper way to answer.
- **A headless export exits 0** even when a file failed to convert; check the log for `ERROR: godot-xaml:` ([Exporting a game](export.md)).

## Tooling

- **The language server has no editor extension yet.** It provides diagnostics, formatting, hover and completions over the Language Server Protocol, as a native executable for win-x64, win-arm64, linux-x64, linux-arm64, osx-x64 and osx-arm64 ([Getting started](getting-started.md#editor-support)); the Visual Studio and VS Code extensions that launch it, and a live preview pane in Visual Studio, are planned for later releases.
- **Completion of `res://` paths, scene-instance tags and `x:Connect` values is not built.**
- **The macOS zips are notarised but not stapled.** A bare executable cannot hold a stapled ticket, so a Mac's first launch of the server checks the ticket with Apple online.
- **The live preview pane in Visual Studio is not released.** The editor side of the preview exists: a [server in the Godot editor](editor.md#live-preview-server) renders `.xscn` text for a client, and is off by default.
- `godot-xaml check` and `format` are the tools outside the editor.
