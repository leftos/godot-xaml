# Limitations

What godot-xaml 0.1 does not do, or does differently from a `.tscn`.

## Platform

- **Godot 4.7 with .NET, tested on 4.7.2.** The addon is C#, so the GDScript-only build of Godot cannot load it.
- **One metadata file per Godot version.** Property types and enum names come from Godot 4.7's metadata, shipped in the package. A later Godot version that renames or retypes a property can change how a file reads; such a release of godot-xaml will say so and say how to upgrade.

## Format

- **Order inside a script's exports, and inside a Theme's or a TileSet's own properties, is not kept.** An `.xscn` sorts attributes alphabetically. Converting back to `.tscn` restores Godot's order for everything else.
- **Hand formatting is not kept.** Every save writes the canonical layout; comments are kept.
- **Script properties are untyped.** The format cannot know an `[Export]` or `@export` property's type without the script, so its value is Godot's value text, strings quoted (`title='"Main menu"'`).
- **`Signal` and `Callable` values** can only be empty (`Signal()`, `Callable()`), as in any saved Godot file.

## Projects

- **Files Godot loads before any addon stay `.tscn` and `.tres`:** the files named by six project settings (the custom theme and font, the default bus layout, the custom mouse cursor, the default environment, the translations), and what they reference. No loader for `.xscn` exists that early.
- **A path a script builds at runtime is not rewritten** by `convert --project`; it warns about them (`GXP0006`).
- **`godot-xaml check` reports parse problems only.** Problems found while converting a file for a load (`GXC` codes) show in the editor's Output panel.
- **The first open after installing** does not list `.xscn` files; the second does ([Working in the editor](editor.md#the-first-open)).
- **A headless export exits 0** even when a file failed to convert; check the log for `ERROR: godot-xaml:` ([Exporting a game](export.md)).

## Tooling

A language server (completion, hover and diagnostics in VS Code and Visual Studio) and a live preview pane in Visual Studio are planned for later releases. Until then, `godot-xaml check` and `format` are the tools outside the editor.
