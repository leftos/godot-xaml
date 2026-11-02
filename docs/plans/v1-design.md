# Version 1 design

How godot-xaml version 1 is built and in what order. The settled choices are in [`../DECISIONS.md`](../DECISIONS.md); this file holds the components, the order of work and the questions still open. Each Linear issue links the section it builds.

## Components

- **Core** (`src/GodotXaml.Core`, .NET library, no Godot dependency): the `.xscn`/`.xres` model, its parser and canonical writer, the `.tscn`/`.tres` reader and writer, and the conversion between them. Reads the metadata file for property types, enum names and defaults.
- **Metadata dump** (a GDScript run by headless Godot, `tools/metadata-dump/`): walks `ClassDB` and writes `metadata/godot-<minor>.json`. For every class it records each property's Godot name, type, enum or flags type, and default, plus each enum's members. Committed per Godot minor version, so the core needs no running Godot. The core computes C# names from Godot's own naming rules (`modules/mono/utils/naming_utils.cpp`), checked in a test against `GodotSharp.dll`.
- **Godot addon** (`addons/godot_xaml/`, C#): the `ResourceFormatLoader` and `ResourceFormatSaver` for `.xscn`/`.xres`, an export plugin that converts them to binary, and the preview server the Visual Studio pane talks to.
- **CLI** (`godot-xaml`, .NET tool): `convert`, `check`, `format`, for people, agents and CI.
- **Language server** (.NET): completions, hover, diagnostics and formatting from the core and the metadata; shared by the Visual Studio and VS Code extensions.
- **Visual Studio extension**: the language server plus the preview pane.
- **VS Code extension**: the language server.

## Order

1. **Spike: does a C# loader and saver hold up in the editor?** The smallest `.xscn` loader and saver in a C# addon, in Godot 4.7: open, edit visually, save, run with F6, instance into another scene, move a file in the FileSystem dock. Then rebuild the C# project with the scene open and see whether the loader survives the assembly reload. The outcome decides whether the addon registers from C#, or needs a GDScript shim that survives reloads.
2. **Metadata dump**, then **core**: model, parser, canonical writer, `.tscn` reader and writer, round trip.
3. **Converter on real scenes**: every scene and resource in delve-the-dungeon, opening-hand and openpax converted and converted back, compared node by node with what Godot loads.
4. **Godot addon** on the core: loader, saver, export plugin.
5. **CLI** and **godot-mcp support**.
6. **Language server**, then the **VS Code** and **Visual Studio** extensions, then the **preview pane**.

## Open questions

Each is settled with the recommended answer unless the work shows otherwise; the answer goes into `DECISIONS.md`. The format's questions (signals, scripts, overrides, inherited scenes, groups and metadata) are settled there.

- **Preview transport.** Recommended: the Visual Studio pane talks to the open Godot editor's addon over a local socket, which renders the text into a `SubViewport` and sends back an image and each node's rectangle (for click-to-select), and falls back to a headless-window Godot run, as godot-mcp's `preview_scene` does, when no editor is open.
