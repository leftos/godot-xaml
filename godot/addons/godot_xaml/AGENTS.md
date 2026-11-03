# godot-xaml: notes for agents

This folder is the godot-xaml addon, version 0.1.0. It lets this Godot project keep scenes as `.xscn` and resources as `.xres`: XML whose elements are nodes and whose attributes are properties, named after Godot's C# API.

The format reference is at https://github.com/leftos/godot-xaml/blob/v0.1.0/docs/format/README.md, and the values at https://github.com/leftos/godot-xaml/blob/v0.1.0/docs/format/values.md.

## Rules

- **Do not edit the files in this folder.** They are the addon, copied from a release; to update it, replace the folder with a newer release's.
- **New scenes are `.xscn` and new resources `.xres`.** Never write a `.tscn` beside an `.xscn` of the same scene. Converting existing files is the owner's call.
- **Leave text files Godot loads early as `.tscn`/`.tres`:** the custom theme, the bus layout, the default environment, and the files they reference. Godot loads them before any addon exists.
- **Names are those of Godot's C# API.**
  - Tags are class names (`CpuParticles2D`).
  - Attributes are PascalCase properties (`SizeFlagsHorizontal`).
  - Enum values are member names (`MouseFilter="Ignore"`), never integers.
- **Value forms:**
  - Vectors are their components (`Position="32, 16"`).
  - Colours are four floats, never hex.
  - Strings are unquoted.
  - Everything else is Godot's value text, as in a `.tscn`: transforms, arrays, dictionaries, and every script property, whose strings are quoted (`title='"Main menu"'`).
- **Files are `res://` paths.** In value text a file is `Resource("res://…")`; never use `ExtResource(…)`.
- **Every node has a `Name`.** The root declares `xmlns:x="https://github.com/leftos/godot-xaml/format/1"`.
- **Nodes inside an instanced scene** are changed with `<x:Override Path="…">`, a direct child of the instance element.
- **Check and format after every edit.** Run `godot-xaml check <paths>` and `godot-xaml format <paths>`, and fix every reported code. Install the tool with `dotnet tool install -g GodotXaml.Cli`. The codes are listed at https://github.com/leftos/godot-xaml/blob/v0.1.0/docs/format/diagnostics.md.
- **Commit `.uid` sidecars** (`main.xscn.uid`). Never delete one, and never invent a uid. Leave the `<x:Uids>` table to the editor.
- **Move and rename files in the Godot editor,** so references are rewritten.

## Problems with godot-xaml itself

File an issue at https://github.com/leftos/godot-xaml/issues with the Godot version, this addon's version, the exact message, and the smallest `.xscn` that shows it. Work around it in the project, not in this folder.
