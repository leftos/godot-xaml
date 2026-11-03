PROTOCOL: 1 overlay: none (no docs/agents/explore.md), read CLAUDE.md; 2 README: done; 3 Task Index: row "Register or debug the .xscn loader or saver in the editor" is nearest, no export row; 4 design docs: plans/v1-design.md grep only (export lines 9, 20); 5 source: done (Godot 4.7.2 at F:\Godot\src-4.7.2, addon XscnFormatLoader.cs); 6 tree search: done for export/remap/uid/global-class paths, none of it in the docs.

Anchor: repo root X:/dev/godot-xaml (main checkout). All Godot paths below are under F:\Godot\src-4.7.2.

# GXAML-10: export support for .xscn/.xres

The design for GXAML-10 (Addon: export support for .xscn/.xres), from the exploration below. It is built after GXAML-9's loader and saver.

## Owner rulings

- **Route:** an `EditorExportPlugin`, registered by an `EditorPlugin` the project enables, loads each `.xscn` and `.xres` through the loader at export, saves Godot binary (`.scn`/`.res`) and adds it with `add_file(binaryPath, bytes, remap: true)`, so the exported game loads binary through Godot's `.remap` and needs no addon code at load. The loader is packed in the game too, as a fallback and for a `.xscn` autoload's type check (`main/main.cpp:4520`).
- **A file that fails to convert fails the export**, naming the file and the error.
- **"Export selected scenes" mode:** the export plugin detects that the loader's `.cs` would be dropped from the filtered global class list and fails the export with a message naming the include filter to add (`addons/godot_xaml/*.cs`).
- **`.xres` type:** the loader's `_GetResourceType` reads the `.xres` root tag (a prefix read, not a full parse) and returns that class, so a `Theme.xres` is a Theme in the editor's pickers and in "export selected resources"; an unreadable first tag reports `Resource`. This is the loader's work (GXAML-9).

Still open, decided when building: where the temp files of route A live in an exported game (`user://` is always writable; the OS temp folder may not be on web, console or mobile), shared with GXAML-9.

# Exploration: what Godot 4.7.2 does on export with .xscn/.xres

## Answer in one paragraph

Export treats a file by what the loader says its type is, never by extension. Our loader's `_GetResourceType` (`godot/addons/godot_xaml/XscnFormatLoader.cs:29`) says PackedScene/Resource, so the exporter packs .xscn/.xres (verbatim, no conversion) in "all resources", and in "selected scenes" when the file is ticked. Godot converts text to binary only for `.tres`/`.tscn` (`editor/export/editor_export_platform.cpp:1714`).

An `EditorExportPlugin` can replace the file with binary output through `add_file(path, bytes, remap=true)`, which writes a `.remap` file, and the exported game then loads `res://a.xscn` from `res://a.scn`. A C# global-class loader is registered in the exported game before autoloads and the main scene, provided its `.cs` file was exported. That last condition fails in "selected scenes" mode unless the project adds an include filter.

## 1. What the exporter packs

- "Export all resources" (`EXPORT_ALL_RESOURCES`): `_export_find_resources` (`editor_export_platform.cpp:639-650`) inserts every editor-filesystem file whose type is not `TextFile`.

  The editor filesystem lists a file only when some loader reports its extension (`editor/file_system/editor_file_system.cpp:3744-3770` builds `valid_extensions` from the loaders). .xscn/.xres therefore are in, once the C# loader is registered, with type from `ResourceLoader::get_resource_type` (`editor_file_system.cpp:1356`).
- "Export selected resources" (`EXPORT_CUSTOMIZED`), the same function family: `_export_find_customized_resources` (`editor_export_platform.cpp:652-668`) inserts every file whose per-file mode is not REMOVE. .xscn/.xres are in unless marked "remove".
- "Export selected scenes/resources" (`export_project_files`, `editor_export_platform.cpp:1336-1345`): for each file ticked in the dialog, `_export_find_dependencies` adds it and its dependencies. With "selected scenes" a file is skipped unless `ResourceLoader::get_resource_type(file) == "PackedScene"` (L1340).

  That check is a live call into our loader, so `.xscn` passes (extension-only type, `XscnFormatLoader.cs:29-35`). A `.xres` is typed "Resource", so it is skipped in scenes-only mode unless some scene depends on it. The dialog tree uses the same type test (`editor/export/project_export.cpp:1110`).
- Autoloads and their dependencies are always added in these two modes (`editor_export_platform.cpp:1347-1363`).
- Dependencies come from the editor filesystem's cached `fi->deps` (`_export_find_dependencies`, L677-687). The cache is filled at scan time by `EditorFileSystem::_get_dependencies` (`editor_file_system.cpp:2080-2100`) through `ResourceLoader::get_dependencies` (`core/io/resource_loader.cpp:1348-1358`) into the loader's `_GetDependencies` (`resource_loader.cpp:184-192`).

  Our loader does not override `_GetDependencies` yet (`XscnFormatLoader.cs` has no such method), so today deps are empty and "selected" modes would miss anything an .xscn references. So the answer is yes, it goes through our loader, but at editor scan time (new/modified file: `editor_file_system.cpp:1366`, `2493`), not at export time.

  At export only the cache is read. First-scan caveat: DECISIONS.md "First import" says the first scan runs before the C# classes register, so deps for a brand-new project's .xscn are empty until a second scan.
- Include filter: `_edit_filter_list(paths, include_filter, false)` (L1370) adds any file matching the filter, scripts included. Remember this for question 3.
- The `.uid` sidecars are never in `paths`: the list comes from the filesystem tree, `.uid` is not a loader extension.

## 2. Binary conversion, and replacing a file

- Where it happens: `editor_export_platform.cpp:1483` reads `editor/export/convert_text_resources_to_binary`; `:1714` `force_binary = convert_text_to_binary && (path.has_extension("tres") || path.has_extension("tscn"))`, passed to `_export_customize` (L1013-1137). Only those two extensions.

  A scene is loaded, instanced with `GEN_EDIT_STATE_INSTANCE`, repacked and saved as `export-<md5 of path>-<name>.scn` (L1066-1097); a resource is loaded and saved as `.res` (L1100-1129). The path is then recorded in `path_remaps` (L1717-1721). `.xscn`/`.xres` get `force_binary=false`, and with no customize plugins `_export_customize` returns the path unchanged (L1014-1016): the raw XML goes into the pack.
- Hook A, `_export_file` plus `add_file(remap)`. Per file, each plugin's `_export_file(path, type, features)` runs (L1543-1549). `add_file(p_path, bytes, p_remap)` (`editor_export_plugin.cpp:63-69`) queues an extra file; the exporter saves it into the pack (L1559-1563). With `remap=true` it does `do_export=false` (the original is not packed) and pushes `{path -> extra file path}` (L1564-1568).

  `skip()` (`editor_export_plugin.cpp:344`) also stops the original. At the end, for each pair it writes `<from>.remap` containing `[remap]\npath="<to>"` (L1782-1798). So a plugin that, for `res://a.xscn`, loads it, saves it as binary and calls `AddFile("res://a.scn", bytes, true)` yields `a.scn` and `a.xscn.remap` in the pack. The plugin's own `_ExportFile` receives `type` "PackedScene" for .xscn and "Resource" for .xres from our loader.
- Hook B, the customize pair. A plugin whose `_BeginCustomizeScenes` returns true and whose `_CustomizeScene(scene, path)` returns a non-null node (`editor_export_plugin.cpp:385-386`, used at `editor_export_platform.cpp:1071-1097`), or the resource equivalents (L1104-1129), makes Godot itself save `export-<md5>-<name>.scn`/`.res` and add the remap (L1717-1720).

  It is cached across exports (`file_cache`, L1480-1509, 1732-1747) and works for non-import files only. The cost is that once any plugin opts in, every scene and resource in the project is loaded and instanced at export (L1066-1070, 1101), and scenes are repacked, not saved as loaded. Its behaviour matches Godot's own tscn-to-scn path.
- Runtime lookup: `ResourceLoader::_path_remap` (`resource_loader.cpp:1442-1528`) first `ResourceUID::ensure_path`s, then, if `<path>.remap` exists, reads `path=` from it. `load_threaded` (L538-541), `exists` (L1254), `get_dependencies` (L1349) all call it. `_load` then picks the loader by the remapped path's extension (L284-286), so `a.scn` goes to the built-in binary loader and `a.xscn` is never read.
- Gap: `ResourceLoader::get_resource_type` does not remap (L1386-1397), and `ResourceFormatLoaderBinary::get_resource_type` opens the path it is given (`core/io/resource_format_binary.cpp:1497-1508`). For `res://a.xscn` in a remapped export that file is absent, so the binary loader answers "".

  Consequence: `main/main.cpp:4520` (`get_resource_type(info.path) == "PackedScene"` for each autoload) is false for an `.xscn` autoload; it then falls into `ResourceLoader::load` and fails with "path is not pointing to a scene or a script" (L4549). The fix is for our loader to still be registered in the game (its extension-only `_GetResourceType` answers PackedScene without reading the file).
- Registration of an export plugin: `EditorExportPlugin` is added only through `EditorPlugin.add_export_plugin` (`editor/plugins/editor_plugin.cpp:463-465`, bound L668). So the addon needs a `[Tool]` `EditorPlugin` with a `plugin.cfg`, enabled under Project Settings > Plugins. A `[GlobalClass]` export plugin alone is never registered. The DECISIONS.md warning about EditorPlugins (loaders removed on script-class update) applies to loaders and savers, not to export plugins.
- Editor-only: both hooks run in the editor, where the loader is registered, so the plugin can call `ResourceLoader.Load`. It needs no temp file outside the project except the saver step: `ResourceSaver.Save(res, "<tmp>.scn")` then `FileAccess.GetFileAsBytes`. (`EditorExportPlugin.get_export_base_path` exists in C++ at `editor_export_plugin.cpp:41` but is not bound to scripts, so the plugin picks its own temp dir.)

## 3. The exported game

- Global classes: `ScriptServer::init_languages` reads `ProjectSettings::get_global_class_list()` (`core/object/script_language.cpp:275-301`), which reads `.godot/global_script_class_cache.cfg` (`core/config/project_settings.cpp:1446-1470`; in an export build a missing file prints "Could not load global script cache", L1456-1458).

  The exporter always includes that file, but filtered: `_get_filtered_cache` keeps only entries whose script `path` is in `paths` (`editor_export_platform.cpp:1869-1928`, forced at L1249, 1803-1823). If the loader's `.cs` file is not in `paths`, the loader's class entry is dropped and it is never registered, silently.
- Registration time: `main.cpp:3867` `ScriptServer::init_languages()` (which also initializes the .NET runtime and project assembly, `modules/mono/csharp_script.cpp`), then `main.cpp:4494-4495` `ResourceLoader::add_custom_loaders(); ResourceSaver::add_custom_savers();`, then autoloads (L4497-4566), then the main scene load (`main.cpp:4762`).

  `add_custom_loaders` (`resource_loader.cpp:1684-1700`) takes every global class whose native base is ResourceFormatLoader and loads its script (`add_custom_resource_format_loader`, L1656-1678). So the loader is live before every autoload and the main scene, if the script is packed.
- The scripts in the pack: C# files are exported by GodotTools' `ExportPlugin._ExportFile` (`modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs:120-154`) as a one-line stub (`"\n"`), unless `dotnet/include_scripts_content` is on.

  The class is found through the assembly's registered script table, not the stub's text: `ResourceFormatLoaderCSharpScript::load` calls `ScriptManagerBridge_GetOrCreateScriptBridgeForPath` (`modules/mono/csharp_script_resource_format.cpp:60-62`). `load_source_code` runs only in DEBUG_ENABLED (L67-70). The stub must exist at the path, which is why the `.cs` must be in `paths`.
- The assembly: `ExportPlugin._ExportBeginImpl` (L170-) runs `dotnet publish` and packs the whole publish folder (`RecursePublishContents`, L316-): the project dll, and `GodotXaml.Core.dll`, `GuiLabs.Language.Xml.dll` as PackageReference outputs (so GXAML-45's PackageReference is enough), beside the exe in `data_<project>_<platform>_<arch>/` (`dotnet/embed_build_outputs` packs them in the PCK instead).

  Build config is `ExportDebug`/`ExportRelease` (L191), which the addon csproj must define. Unmeasured: trimming and Native AOT.
- Temp file outside res://: unmeasured in an exported build (no export was run). By source: `ResourceLoader::_validate_local_path` (`resource_loader.cpp:689-698`) keeps `user://` and absolute OS paths (`localize_path`), recognition is by extension (`ResourceFormatLoader::recognize_path`, L62-83), and `.tscn` goes to the text loader, which reads any `FileAccess` path.

  Our `godot/checks` already prove an absolute OS path loads in the editor build (ARCHITECTURE.md, Test locations). The resource's path is the temp path, so the loader must set it back to `res://a.xscn` (`_Load` receives `originalPath`) or `scene_file_path` and cache identity are wrong. `user://` is always writable; an OS temp dir may not be on a locked-down platform (web, console, mobile). Unmeasured: both in a real exported game.
- The loader's nested loads: a .tscn text converted from an .xscn that references other .xscn/.xres loads them through `ResourceLoader` again, re-entering the loader inside a load (`_Load` is called from loader threads when `use_sub_threads`; C# loader needs to be thread-safe; the `LoadCallCount` counter already is).

## 4. Uids

- Editor: the uid of an .xscn is its `.uid` sidecar (`ResourceFormatLoader::get_resource_uid` reads `<path>.uid` unless `_GetResourceUid` is overridden, `resource_loader.cpp:118-129`); the editor filesystem stores it (`editor_file_system.cpp:1364`).
- Export: the `.uid` sidecars are not exported (not in `paths`). `uid_cache.bin` is written from `paths` x `EditorFileSystem::get_file_uid(path)` (`editor_export_platform.cpp:1896-1912, 1925, 1825-1826`; encoder `core/io/resource_uid.cpp:259-271`). It maps uid to the original path, `res://a.xscn`, in both routes. In the game `ResourceLoader::get_resource_uid` ignores loaders and reads that cache (`resource_loader.cpp:1414-1416`); main.cpp:2287 loads the cache at startup.
- Raw route: `uid://X` -> `res://a.xscn` -> our loader. Works.
- Binary route: `uid://X` -> `res://a.xscn` (cache) -> `a.xscn.remap` -> `res://a.scn` (`_path_remap` L1490-1491). Works (a `uid://` is resolved by `ensure_path` before the `.remap` lookup). Preconditions: the .xscn had a uid at export time (DECISIONS.md "First import" says a first import may have none; GXAML-9 must give every .xscn a uid before a scene references it), and the binary file the plugin saves needs no uid of its own.
- A text-loaded scene that references `uid://` of an .xscn inside the converted tscn (ext_resource uid) resolves the same way.

## 5. Cost of each route

| | Route R: loader in the game (raw .xscn shipped) | Route B: export plugin converts to binary + remap |
|---|---|---|
| Load time | Each load: read XML, parse, convert to tscn text, write temp file, text-parse; uncached. Slowest of the three (binary is Godot's fastest format). | Godot's binary loader; no addon code runs at load. |
| Pack size | XML text | Smaller, no metadata needed at runtime |
| Project config | None for "export all"; for "selected scenes" the include filter must list `addons/godot_xaml/*.cs`. C# project required (already). | Same C# project; plus an `EditorPlugin`/`plugin.cfg` enabled; the loader's `.cs` filter caveat applies only to the autoload-type check. |
| Needs in the game | C# runtime, Core dll, GuiLabs dll, 1.9 MB metadata (embedded), loader `.cs` packed, writable temp location | Nothing for load, except the loader for an `.xscn` autoload type check |
| Failure modes | Loader `.cs` not packed ("selected" modes, exclude filter): global class dropped silently, every .xscn fails to load incl. the main scene (`main.cpp:4767` "Failed loading scene"). Temp dir unwritable. First-scan deps empty. A bug in conversion shows only at runtime. | Plugin not enabled: .xscn ships raw and fails at runtime with "No loader" if the loader is also absent. Conversion errors surface at export. Export-time uid missing. Autoload .xscn type check (see 2). A stale `file_cache` only for Hook B. |
| Fidelity | The text round trip the project proves | Whatever `ResourceSaver` does with the loaded PackedScene (Hook A: the loaded scene, saved as is; Hook B: instanced and repacked). |

## RECOMMENDATION

Route B with Hook A: an export plugin (a `[Tool]` EditorPlugin registering an `EditorExportPlugin`) loads each `.xscn`/`.xres` through the registered loader at export, saves binary `.scn`/`.res`, and calls `add_file(binaryPath, bytes, remap: true)`, so the pack holds `a.scn` plus `a.xscn.remap` and the exported game loads `res://a.xscn` and `uid://` references from binary with no addon work at load.

Evidence: `editor_export_platform.cpp:1564-1568, 1782-1798`, `resource_loader.cpp:1442-1528`, uid cache keyed by original path at `editor_export_platform.cpp:1896-1925`.

Keep the loader packed in the game anyway (route R as the fallback and for the `.xscn` autoload type check at `main.cpp:4520`), and have the plugin add `addons/godot_xaml/*.cs` to the export through a check that fails the export when the loader's `.cs` would be dropped.

## OPEN DECISIONS

1. Route: B with Hook A (above); B with Hook B (Godot's own customize path: cached, repacks scenes, loads every scene and resource at export); R only (raw, loader in game, slowest, no editor plugin); B plus R fallback (recommended).
2. Whether to require the user to enable an EditorPlugin (needed for any export plugin, `editor_plugin.cpp:463`), and how GXAML-45's install form says so (plugin.cfg plus `editor_plugins/enabled` in project.godot).
3. What happens when an `.xscn` fails to convert at export: abort the export (Hook A can add an error through `GetExportPlatform().AddMessage`) or ship the raw file.
4. Whether to ship the raw `.xscn` as well as the binary (a `.xscn` source in a shipped build exposes it; remap=true drops it, which is the default), and whether `dotnet/include_scripts_content` matters (it does not for this).
5. Selected-scenes/resources mode: ship the loader `.cs` through the plugin (cannot: `_get_filtered_cache` filters on `paths`, `add_file` does not add to it) or document the include filter, or detect and fail the export.
6. `_GetResourceType` for `.xres` is "Resource", not the real class; decide whether the loader should read the root tag (cheap) so editor filesystem, `export selected resources` and `ResourceLoader.exists(path, typeHint)` see the true type.
7. Where the saver's temp file goes in the editor (res://.godot/..., user://, OS temp) and whether the same helper serves GXAML-9's runtime path.

## DEPENDS ON

- GXAML-9 (addon loader and saver): hard. Hook A loads with the loader and saves binary; also needs `_GetDependencies` (deps for "selected" modes), `_GetResourceUid` or the `.uid` sidecar guarantee (question 4), and a loader that sets the resource path back to `res://` after loading from a temp file. Not built yet (`XscnFormatLoader.cs:63-64` returns Unavailable).
- GXAML-45 (install form): consumes the answer. It must carry the plugin, `plugin.cfg`, the enable step, the `GodotXaml.Core` PackageReference (publish packs its dll), and the `ExportDebug`/`ExportRelease` build configurations.
- GXAML-7 (conversion both ways): hard for the raw route; for Hook A only through the loader (the plugin never converts itself).
- GXAML-12 (CLI convert/check/format): no.
- GXAML-13 (godot-mcp support): no.

## BRANCH

No. The skeleton at `godot/` and the loader are on `main` (`git log`: `a8539a4` adds the addon shells); the local branches `gxaml-50`, `gxaml-52`, `gxaml-7b`, `gxaml-7c` are other items. No `feat/` branch exists for this item and the work follows GXAML-9 (not landed), so it should wait for it; the first piece (the EditorPlugin, export plugin and a headless check) can be a `feat/addon-export` branch off `main` after GXAML-9's loader exists.

## DOC DRIFT

- `docs/DECISIONS.md:14` says "the addon converts `.xscn` and `.xres` itself at export, or registers its loader in the exported game": correct on the facts (the cited `editor_export_platform.cpp:1714` line is right), but it omits that either route needs the loader's `.cs` in the pack (`_get_filtered_cache`, L1869-1928) and that registering an export plugin needs an EditorPlugin. Right side: code; the doc to fix is DECISIONS.md, in the entry GXAML-10 will add.
- `docs/ARCHITECTURE.md:29` describes the addon without the export plugin; `docs/plans/v1-design.md:9` already lists "an export plugin that converts them to binary", so the two disagree on scope. ARCHITECTURE.md is behind; update it in the commit that builds the plugin.

## TASK INDEX GAP

This answer took about 35 reads and searches beyond the one row and doc.

Row that should exist: "Export `.xscn`/`.xres` in a game build (what packs, remap, uid, loader registration)": `docs/DECISIONS.md` (the GXAML-10 entry) -> `godot/addons/godot_xaml/` (export plugin, EditorPlugin, `XscnFormatLoader`) -> Godot `F:\Godot\src-4.7.2` (the files below) -> `godot/checks/`.

The Godot files: `editor/export/editor_export_platform.cpp:1312-1930`, `editor/export/editor_export_plugin.cpp:63-69`, `core/io/resource_loader.cpp:1442-1528, 1684-1700`, `main/main.cpp:3867, 4494, 4520, 4762`, `modules/mono/editor/GodotTools/GodotTools/Export/ExportPlugin.cs:120-170`.

## MISSING ENTRY POINT

none (README.md and ARCHITECTURE.md with a Task Index exist). Terms used in a project sense with no glossary entry: "export plugin" and "remap" are not in `docs/README.md`.
