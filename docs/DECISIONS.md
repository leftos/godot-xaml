# Decisions

The owner's decisions about godot-xaml and the engine facts behind them. Read this before reversing one; a reversal edits the entry in place.

## The format is the only committed file

A scene is committed as `.xscn` and a resource as `.xres`; no `.tscn` or `.tres` sits beside it in the repo. The Godot addon registers a `ResourceFormatLoader` and a `ResourceFormatSaver` for both, so the Godot editor opens, edits and saves them directly, and visual editing in Godot keeps working.

Why it is possible, from the Godot 4.7.2 source (`F:\Godot\src-4.7.2`):

- A script can register both: `ResourceLoader.add_resource_format_loader` and `ResourceSaver.add_resource_format_saver` are bound in `core/core_bind.cpp`, and the virtuals cover uids (`_get_resource_uid`, `_set_uid`), dependencies (`_get_dependencies`) and the FileSystem dock's move and rename (`_rename_dependencies`) in `core/io/resource_loader.h` and `core/io/resource_saver.h`.
- The editor's scene Open and Save dialogs list the extensions every registered loader and saver reports for `PackedScene` (`ResourceLoader::get_recognized_extensions_for_type` and `ResourceSaver::get_recognized_extensions` in `editor/editor_node.cpp`).
- "Run current scene" passes the scene as `--scene <path>` (`editor/run/editor_run.cpp` L151), which takes any extension. Only a bare positional path on the command line must end in `.scn`, `.tscn`, `.escn`, `.res` or `.tres` (`main/main.cpp`).
- Export converts only `.tscn` and `.tres` to binary (`editor/export/editor_export_platform.cpp` L1714), so the addon converts `.xscn` and `.xres` itself at export, or registers its loader in the exported game.
- About ten other places in the editor match `.tscn` by name, none of them on the open, edit, save or run path (for example the large-file warning in `editor/editor_node.cpp` L2539 and the FileSystem dock's open action in `editor/docks/filesystem_dock.cpp`).

## The addon registers from C#, as global classes

The loader and saver are C# classes marked `[GlobalClass]` and `[Tool]`, with bases `ResourceFormatLoader` and `ResourceFormatSaver`. No `EditorPlugin` adds them and no GDScript shim is needed.

An `EditorPlugin` that adds them by hand does not last: on every script-class update the editor removes every script-backed loader and saver and re-adds only global classes (`editor/file_system/editor_file_system.cpp:2199-2205`, `core/io/resource_loader.cpp:1684-1713`, `core/io/resource_saver.cpp:254-283`). And an assembly reload recreates script instances only for tool scripts (`modules/mono/csharp_script.cpp:920-952`).

Measured on Godot 4.7.2 .NET with the GXAML-3 spike (`spikes/xscn-loader/`):
- **Headless.** `--import` and a game run both find the C# global classes. A `.xscn` loads, instances inside a `.tscn`, saves and reloads, and runs with `--scene` the way F6 does.
- **In the editor.** These all passed:
  - The Open and Save dialogs' extension lists hold `xscn`.
  - A `.xscn` opens as a scene tab, is edited and saved back as `.xscn`, and runs as the current scene.
  - It is instanced into a `.tscn`, which records its path and uid.
  - A move resolves through the uid sidecar.
  - It still saves as `.xscn` after a C# rebuild with the scene open, and reopens after an editor restart.
- **Rebuild.** Running a scene builds C# first (`editor/run/editor_run_bar.cpp:350-353` into GodotTools' `_Build`). The editor then reloads the assembly when its file is newer (`GodotTools/HotReloadAssemblyWatcher.cs`, `modules/mono/csharp_script.cpp:594-623`). The spike saw the build rewrite the assembly and the scene save correctly afterwards, but no script can observe the reload itself, so the addon's tests prove it with a counter that the reloaded assembly resets.
- **First import.** A brand-new project's first import writes no `.uid` sidecar for a `.xscn`: the first file scan runs before the C# global classes register, so no loader claims the file yet. The second import writes it. The addon must give every `.xscn` a uid before a scene references it.
- **By hand.** The owner checked the rest in the editor, and both passed:
  - Renaming a `.xscn` in the FileSystem dock renames its `.uid` sidecar too, rewrites the `.tscn` that instances it (path and uid), and the instance still loads.
  - The Open Scene dialog lists `*.xscn` and shows the file.

## XAML syntax with C# names

The format is XML in the XAML style: an element per node, its type as the tag, its properties as attributes. Names are those of Godot's C# API: PascalCase properties (`CustomMinimumSize`, `SizeFlagsHorizontal`) and C# enum member names as values (`MouseFilter="Ignore"`, `StretchMode="KeepAspectCentered"`, `SizeFlagsHorizontal="ExpandFill"`), never the backing integers a `.tscn` stores.

- `Name="%Press"` marks a unique name (`unique_name_in_owner`), the way `GetNode("%Press")` reads it.
- A value equal to the default is not written, and neither is a value the parent decides: `layout_mode` under a `Container` is always Container, so it is omitted.
- Enum names come from Godot itself, dumped to a metadata file by a headless Godot run, so tools outside Godot (the language server, Visual Studio) can name them too. A property's hint string is not enough: it holds display names (`mouse_filter`'s hint is `Stop,Pass (Propagate Up),Ignore`) and leaves out combined constants such as `ExpandFill`, so the dump takes the enum's members from `ClassDB.class_get_enum_constants`, linked to the property by its enum type.

## Attribute values are typed by the metadata

A property whose type the metadata knows is written in compact XAML text: vectors as their components (`CustomMinimumSize="640, 0"`), `Color` as four floats (`Modulate="1, 1, 1, 0.5"`, never hex, which cannot hold Godot's float32 channels), strings and `StringName`s unquoted (`ThemeTypeVariation="TitleLabel"`), `NodePath`s as the bare path, and files as `res://` paths.

Every other value is written as Godot's own value text, exactly as a `.tscn` holds it: transforms, bases, quaternions, arrays, dictionaries, packed arrays, and every property of a script (`[Export]`), whose type the core cannot know (`Target='NodePath("../..")'`).

The property's type decides how its text is read, never the text's first character. A property the metadata types as `String` or `StringName` is always literal text, so `Text="[b]Bold[/b]"` and `Text="{0} items"` need no escape. A property with no known type (a script property, or one Godot declares as any Variant) is always Godot value text, its strings quoted (`Title='"[Esc] to close"'`).

On an element whose class the core cannot know without opening another file (an instanced scene, `<x:Override>`), a property is typed by its C# name across every Godot class: when every class that has the name agrees on its type and enum, the value is compact text (`AnchorsPreset="FullRect"`, `CustomMinimumSize="640, 0"`); when they disagree (`Position`, `Size`, `Mode`), it is Godot value text (`Position="Vector2(10, 20)"`).

This typing depends on the metadata of one Godot minor version, so a new version can change how a file is read: a name that becomes ambiguous, or a property whose type or enum changes.

Each Godot release that changes typing gets an upgrader that reads a project's files with the old version's metadata and writes them with the new one. When a new metadata file is added, a test lists every name whose typing differs from the previous version's, so whether a release needs an upgrade step is always known.

Booleans are written `True` and `False` (either case is read), and a float has no trailing `.0` (`AnchorRight="1"`), as vector components already have none.

## Format elements and attributes take the `x:` prefix

The default XML namespace is Godot's classes. Everything the format adds rather than Godot's property list takes the reserved `x:` prefix: the uid table (`<x:Uids>`), overrides (`<x:Override>`), and the attributes `x:Index`, `x:UniqueId`, `x:Editable` and `x:Groups`. A script property named `Index` or `Groups` therefore never clashes with them. `Name` and `Script` stay unprefixed, since they are Godot's own `name` and `script`.

## Overrides inside an instance are named by path

A node inside an instanced scene that the scene changes is written as `<x:Override Path="Centre/Panel">`, a child of the instance element, holding only the changed properties; nodes the scene adds under it nest inside it. The path is relative to the instance, so the core parses the file without opening the instanced scene to learn the type of each node on the way. `x:Editable="True"` on the instance element is the `.tscn`'s `[editable path=…]`.

## Slash-named properties are dotted attributes

A Godot property whose name holds a `/` (`theme_override_constants/separation`, `metadata/_tab_index`, `shader_parameter/albedo`) is written as a dotted attribute: the part before the slash in PascalCase, the key after it verbatim, since keys are free strings that would not survive a case change (`ThemeOverrideConstants.separation="12"`, `Metadata._tab_index="0"`).

A value that needs an element, such as an inline resource, uses the same name as a property element (`<VBoxContainer.ThemeOverrideStyles.panel>`). A key that is not a legal XML name falls back to `<x:Prop Name="…" Value="…" />` with the Godot name verbatim.

The value follows the typed-values rule. Each theme-override family has one fixed type (`scene/gui/control.cpp:400-417`: colors are `Color`, constants and font sizes `int`, fonts `Font`, icons `Texture2D`, styles `StyleBox`), so the metadata file records each family's type and the core writes them in compact text (`ThemeOverrideColors.font_color="1, 0.8, 0, 1"`). `metadata/…` and `shader_parameter/…` values have no fixed type, so they are written as Godot value text.

## Signals are attributes on the emitter

A connection is written on the node that emits the signal, like XAML's `Click="Handler"`. The common case, received by the scene root with default flags, is an attribute named by the signal's C# name whose value is the method (`<Button Name="Update" Pressed="on_update_button_pressed" />`).

Any other receiver, non-default flags, binds or unbinds use an element on the emitter, `<x:Connect Signal="Pressed" To="Panel" Method="Hide" Flags="Deferred, OneShot" Binds='[1, "a"]' />`, as does a signal whose name clashes with a property. The method name is the script's own and is never renamed.

## Shared sub-resources live in an `x:Resources` table

A sub-resource used once is written inline, as a property element. One used by several properties, or referenced from inside Godot value text (an array or dictionary), sits in an `<x:Resources>` table at the top of the root with an `x:Key`, like XAML's resource dictionaries. An attribute refers to it as `{Key}`; Godot value text refers to it as `SubResource("Key")`. Inlining a shared resource twice would make Godot load two separate objects.

## Canonical attribute order: Name, Script, then alphabetical

The canonical writer puts `Name` first and `Script` second, then every other property in ordinal alphabetical order, then the `x:` attributes. The order needs no metadata, so a new Godot minor version never reorders a file.

## One error-tolerant XML parser

The core parses `.xscn` and `.xres` with GuiLabs.Language.Xml (formerly Microsoft.Language.Xml; Apache-2.0, targets net8.0 and netstandard2.0), a full-fidelity parser that keeps every span and builds a tree from half-typed text. Every host uses it: the Godot loader treats any error node as a load error, and the language server reports errors as diagnostics and keeps going. One parser means one set of parsing rules everywhere.

A comment is legal XML only between elements, never inside a start tag (XML 1.0, production `STag`), so a comment attaches to the element it precedes.

## Canonical layout

- A start tag stays on one line when it fits in 100 columns; otherwise every attribute goes on its own line, indented 4 spaces from the tag.
- Elements indent 2 spaces. No XML declaration; UTF-8 without a BOM, LF line ends, a final newline.
- Inside an element: `<x:Uids>` first, then `<x:Resources>`, then property elements, then child nodes in scene order.

## Integer properties with a hint but no enum

Nine integer properties carry an enum-style hint with no C# enum behind it (`layout_mode`, `anchors_preset`, `ProgressBar.fill_mode` and others). They are written by the hint's display name for the stored index, in PascalCase (`AnchorsPreset="FullRect"`, `LayoutMode="Anchors"`); the reader also accepts the integer.

## Where the rest of a `.tscn` goes

- `unique_id` is kept as `x:UniqueId`, the last attribute, wherever the `.tscn` has it, so a save from the editor does not churn ids and 4.7's id paths keep resolving.
- An inherited scene is a root element whose tag names a scene (`<c:Modal …>` at the root); no marker, since a root cannot be an instance.
- `Script="res://…"` is an attribute, as the inspector shows it.
- Groups are `x:Groups="a, b"`, sorted as Godot sorts them.
- A node's `index`, a non-root `owner`, `instance_placeholder`, `node_paths` and 4.7's `parent_id_path`, `owner_uid_path`, `from_uid_path` and `to_uid_path` are kept as `x:` attributes (`x:Index`, `x:Owner`, `x:InstancePlaceholder`, `x:NodePaths`, …), written only where the `.tscn` has them, so a conversion without a running Godot round-trips.
- Not stored: `load_steps` (4.7.2 no longer writes it), `format` (chosen again on conversion back), `script_class` (it follows from the script), and `ext_resource` and `sub_resource` ids (references are paths and keys; ids are made again on conversion back). The scene's own uid lives in its `.uid` sidecar.

## The metadata file

- The dump tool is a GDScript run by headless Godot (`godot --headless --script`), with no build step, so it shares no files with the C# core.
- Its output is JSON, one file per Godot minor version (`metadata/godot-4.7.json`). Each class lists only its own properties and signals, with a link to its parent; names containing `/` are not properties.

  The exception is `themeOverrides`, which a class that is `Control` or `Window` or inherits one carries as its full list, inherited items included: each `theme_override_<family>/<item>` entry with its type, hint and hint string, read from an instance, so the file records each family's type and the item names. Defaults are stored as Godot's own value text (`Vector2(0, 0)`), the grammar a `.tscn` uses, so the core reads them with its `.tscn` value reader.

## Test fixtures are synthetic

The `.tscn`/`.tres` reader and writer are tested on small scenes and resources made for the tests, one per section and value type, then re-saved by headless Godot 4.7.2 so the expected bytes are 4.7.2's own. They are committed. A node's `unique_id` cannot be pinned from a script, so regenerating the scene fixtures rewrites those numbers and nothing else. The owner's projects are not copied into this public repo: the converter runs on them in place (GXAML-8).

## Canonical formatting on save

Every save, by the Godot editor or the formatter, writes one fixed layout, like gofmt: diffs come only from real changes. Comments are kept, attached to the node or attribute they precede; hand line breaks and attribute order are not.

## References: paths inline, XML namespaces for scenes

- A file is referenced by its `res://` path where it is used (`Texture="res://Art/frame.png"`).
- Each referenced file's uid is kept in an `<x:Uids>` table the tools maintain, so a move made outside the editor can still be resolved.
- A scene folder maps to an XML namespace (`xmlns:c="res://Scenes/Components/"`), so an instanced scene is a tag: `<c:Modal Name="%Confirm" />`.
- An inline sub-resource is a XAML property element (`<PanelContainer.ThemeOverrides><StyleBoxFlat … /></PanelContainer.ThemeOverrides>`).

## C# throughout

One C# core (parser, writer, metadata, `.tscn` conversion) serves the Godot addon, the CLI, the language server and the Visual Studio extension. The Godot addon is C#; the target is Godot 4.7+ .NET.

## Version 1 scope

- `.xscn` scenes and `.xres` resources, opened, edited and saved in Godot 4.7+.
- A lossless `.tscn`/`.tres` converter, run on the owner's projects (delve-the-dungeon first), with a check that converting back yields the same scene.
- Visual Studio: completions, enum names and errors while typing, and a live preview pane rendered by Godot; clicking a node in the preview selects its element in the text.
- VS Code: the same language server.
- Agent tooling: a CLI (convert, check, format) and godot-mcp support for the new files.

Later versions: editing the selected node in Visual Studio's Properties window, then a full visual designer in Visual Studio.

## Name

The repo is `godot-xaml`, public at `github.com/leftos/godot-xaml`, MIT. Scenes are `.xscn`, resources `.xres`.
