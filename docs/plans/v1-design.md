# Version 1 design

How godot-xaml version 1 is built and in what order. The settled choices are in [`../DECISIONS.md`](../DECISIONS.md); this file holds the components, the order of work and the questions still open. Each Linear issue links the section it builds.

## Components

- **Core** (`src/GodotXaml.Core`, .NET library, no Godot dependency): the `.xscn`/`.xres` model, its parser and canonical writer, the `.tscn`/`.tres` reader and writer, and the conversion between them. Reads the metadata file for property types, enum names and defaults.
- **Metadata dump** (a GDScript or C# tool run by headless Godot): walks `ClassDB` and writes, for every class, each property's C# name, Godot name, type, enum or flags hint and default. Committed per Godot minor version, so the core needs no running Godot.
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

Each is settled with the recommended answer unless the work shows otherwise; the answer goes into `DECISIONS.md`.

- **Signal connections.** Recommended: a `Connections` property element on the node that receives the signal, `<Connect Signal="Pressed" To=".." Method="OnPressed" />`.
- **The node's script.** Recommended: `Script="res://…/Foo.cs"` as an attribute, the way the inspector shows it, rather than XAML's `x:Class`.
- **Instanced scenes with edited children** (`editable_children`, a child's property overridden). Recommended: the overridden child written as a nested element marked `Override="true"`, holding only the changed properties.
- **Inherited scenes.** Recommended: the root element names its base scene, `<c:Modal Inherits="true">`.
- **Groups, metadata and resource-local properties.** Recommended: `Groups="a, b"` and a `<Node.Meta>` property element.
- **Preview transport.** Recommended: the Visual Studio pane talks to the open Godot editor's addon over a local socket, which renders the text into a `SubViewport` and sends back an image and each node's rectangle (for click-to-select), and falls back to a headless-window Godot run, as godot-mcp's `preview_scene` does, when no editor is open.
