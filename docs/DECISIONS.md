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

## The metadata file

- The dump tool is a GDScript run by headless Godot (`godot --headless --script`), with no build step, so it shares no files with the C# core.
- Its output is JSON, one file per Godot minor version (`metadata/godot-4.7.json`). Each class lists only its own properties, with a link to its parent. Defaults are stored as Godot's own value text (`Vector2(0, 0)`), the grammar a `.tscn` uses, so the core reads them with its `.tscn` value reader.

## Test fixtures are synthetic

The `.tscn`/`.tres` reader and writer are tested on small scenes and resources made for the tests, one per section and value type, then re-saved by headless Godot 4.7.2 so the expected bytes are 4.7.2's own. They are committed. The owner's projects are not copied into this public repo: the converter runs on them in place (GXAML-8).

## Canonical formatting on save

Every save, by the Godot editor or the formatter, writes one fixed layout, like gofmt: diffs come only from real changes. Comments are kept, attached to the node or attribute they precede; hand line breaks and attribute order are not.

## References: paths inline, XML namespaces for scenes

- A file is referenced by its `res://` path where it is used (`Texture="res://Art/frame.png"`).
- Each referenced file's uid is kept in a `<Scene.Uids>` table the tools maintain, so a move made outside the editor can still be resolved.
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
