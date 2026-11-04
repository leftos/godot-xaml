# godot-xaml: notes for agents

This folder is the godot-xaml addon, version 0.3.0. It lets this Godot project keep scenes as `.xscn` and resources as `.xres`: XML whose elements are nodes and whose attributes are properties, named after Godot's C# API.

The format reference is at https://github.com/leftos/godot-xaml/blob/v0.3.0/docs/format/README.md, and the values at https://github.com/leftos/godot-xaml/blob/v0.3.0/docs/format/values.md.

## Rules

- **Do not edit the files in this folder.** They are the addon, copied from a release; to update it, replace the folder with a newer release's.
- **New scenes are `.xscn` and new resources `.xres`.** Never write a `.tscn` beside an `.xscn` of the same scene. Converting existing files is the owner's call.
- **Leave text files Godot loads early as `.tscn`/`.tres`:** the files named by the custom theme, custom font, default bus layout, custom mouse cursor, default environment and translations settings, and the files they reference. Godot loads them before any addon exists.
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
- **Check and format after every edit.** Run `dotnet godot-xaml check <paths>` and `dotnet godot-xaml format <paths>`, and fix every reported code. `check` reports parse problems; a few problems (`GXC` codes) show only when Godot loads the file. The codes are listed at https://github.com/leftos/godot-xaml/blob/v0.3.0/docs/format/diagnostics.md.
- **Use the CLI at this addon's version.** Install it as a local tool of the project, pinned to 0.3.0: `dotnet new tool-manifest` when the project has no `.config/dotnet-tools.json`, then `dotnet tool install GodotXaml.Cli --version 0.3.0`. When the addon is updated, update the tool to the same version (`dotnet tool update GodotXaml.Cli --version <new version>`). A global `godot-xaml` of another version may check against a different format.
- **The language server** (diagnostics, formatting, hover and completions over LSP on stdin/stdout) is a native executable with no .NET install, in `godot-xaml-ls-<version>-<rid>.zip` on each GitHub release, for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64`. How to get it: https://github.com/leftos/godot-xaml/blob/v0.3.0/docs/guide/getting-started.md#editor-support. It does not replace `check` and `format`.
- **Commit `.uid` sidecars** (`main.xscn.uid`). Never delete one, and never invent a uid. Leave the `<x:Uids>` table to the editor.
- **Move and rename files in the Godot editor,** so references are rewritten.
- **Turn on the preview server only while you use it.** You may turn on `godot_xaml/preview_server/enabled` when you need the live preview: tell the user, and turn it off again when you are done, so it is not left saved in `project.godot`. Never turn on `godot_xaml/preview_server/allow_scripts`; whether previewed text may run the project's scripts is the user's choice.

## Problems with godot-xaml itself

File an issue at https://github.com/leftos/godot-xaml/issues with the Godot version, this addon's version, the exact message, and the smallest `.xscn` that shows it. Work around it in the project, not in this folder.
