# Changelog

## Unreleased

### Added

- Godot 4.8 `.xscn` files can hold the scene's own properties in `x:Scene` and an embedded scene inside a `PackedScene` resource.
- Godot 4.8 projects get 4.8's classes, property typing and hover docs, taken from Godot 4.8 beta 2.

### Changed

- The CLI reads a file with no `x:Godot` marker with its project's Godot version, warning `GXT0009` when it lacks that version's metadata.
- The editor reads a file with no `x:Godot` marker with the running Godot's version, and every editor save stamps the running version.

## 0.5.0 - 2026-11-04

### Added

- Every `.xscn` and `.xres` the tools write names its Godot metadata version in a root `x:Godot` attribute; godot-xaml 0.4.0 cannot read a marked file.
- The CLI, language server and editor read each file with the metadata its `x:Godot` names, and never restamp one naming a version they lack.
- `godot-xaml upgrade` moves a project's `.xscn` and `.xres` files to a newer Godot version's metadata, all or nothing, with a `--check` mode.

### Changed

- Converting a `.tscn` keeps every property it stores, even at its default, and `format` keeps a default a hand-written `.xscn` spells.
- `convert --project` and `upgrade` back up the originals in a folder beside the project, so the project's own scans never find the copies.

### Fixed

- Saving a `.xscn` the writer cannot write fails with `Error.InvalidData` and an error naming the file, whatever the writer refused.
- A hover on a `{Key}` reference finds an `x:Resources` entry whose key holds an escaped character such as `&amp;`.
- Flags completion treats each space-separated word as its own part, so completing after a word no longer replaces it.
- The addon's lock fields no longer fail a host project's info-level `dotnet format` check (IDE0330).
- Completion and hover follow the reader's namespace rules, and a typed space offers completions only between attributes or in a flags value.
- `convert --project` no longer warns about the addon's own files, and its summary counts every warning a roll-up line stands for.
- Converting back to `.tres`, a font's baked textures, glyphs and kerning pairs keep Godot's order, each glyph's keys in the order Godot writes them.
- Converting a `.xres` back to `.tres` keeps its global class's `script_class` once the project has been opened in the editor.
- The macOS language-server zips hold the executable at their root, as the other platforms' zips do.

## 0.4.0 - 2026-11-04

### Added

- A language server, a native executable for Windows, Linux and macOS on each release, gives `.xscn` and `.xres` diagnostics, formatting, hover and completions.
- With the project setting `godot_xaml/preview_server/enabled` on, the Godot editor runs a local preview server that renders unsaved `.xscn` text, scripts off.

### Fixed

- Converting back, shader parameters, global shader overrides and blend shapes keep Godot's order, even from a same-run mesh `.xres`; an unreadable source warns `GXC0122`.
- Converting back, TileMap layers, MeshLibrary items and surface material overrides keep Godot's order past the tenth, with each layer's and item's keys in Godot's order.
- Converting back to `.tres`, audio bus layouts, EQ bands, stream randomizers, label outlines, skins, skeleton profiles and font caches keep Godot's order past the tenth entry.
- Converting back to `.tres`, a TileSetAtlasSource keeps each tile's keys together in Godot's order, tiles by coordinates and alternatives by id.
- Converting back to `.tres`, a VisualShader's `flags/` keep the order Godot saves for its shader mode, not always Spatial's.
- Converting back to `.tscn`, a Control inside a Container gets the `layout_mode` Godot saves for it, and a TabContainer's tabs and a GraphNode's slots keep Godot's order past the tenth.
- Converting back to `.tscn`, the items of a TabBar, PopupMenu, ItemList, OptionButton, MenuButton or FileDialog keep Godot's order past the tenth, each item's keys in the order Godot writes them.
- A `.xscn` or `.xres` nested more than 512 elements deep reports `GX0120` instead of crashing whatever reads it.
- A raw `.xres` inside a pack a game mounts reports its class and script class, as one on disk does.
- The addon's temp folder no longer keeps the half-written files a save interrupted by a crash leaves behind.
- Converting back to `.tscn`, a Skeleton3D bone's Godot 3 keys come after its other keys, `pose` before `bound_children`, as Godot reads them.

## 0.3.0 - 2026-11-03

### Added

- `XresFormatLoader.RefreshGlobalClasses()`, called after mounting a pack, lets `.xres` files whose script is one of the pack's global classes report and load as that class.

### Changed

- On Linux and macOS, the addon's temporary converted files live in your own private cache folder instead of the temp folder every account shares.

### Fixed

- Loading and saving `.xscn` and `.xres` files no longer leaves engine values for .NET's garbage collector, which could crash Godot as it quits.

## 0.2.1 - 2026-11-03

### Changed

- The addon's `AGENTS.md` and the agent skill install the CLI as a local tool at the addon's version, run as `dotnet godot-xaml`.

### Fixed

- A file that loads with parse warnings shows them in Godot's Output as warnings, and a failed load reports its warnings as warnings, not errors.
- A failed load removes converted temp files that earlier failed loads kept more than a day ago.
- An `.xres` whose script declares a global class appears in Quick Load and typed drops for that class, and loads by its class name.

## 0.2.0 - 2026-11-03

### Added

- `LineMap` turns a 0-based line and column back into an offset, exactly or clamped as the language server protocol reads positions.
- `GodotXaml.Core.Projects` finds a file's Godot project, maps `res://` paths to disk and back, and reads the project's Godot version; `GodotMetadata.LoadBundledForProject` picks matching metadata.
- `GodotMetadata` lists a class's ancestors and every property and signal it has, inherited ones included.
- `XscnParser.ContextAt` says what an offset of an `.xscn` or `.xres` is on, broken text included, and `XscnFormatVocabulary` lists the format's `x:` elements and attributes.

### Fixed

- A TileSet whose tiles have terrains keeps them: converting back no longer writes a tile's terrain before its terrain set, which made Godot drop it.
- Converting back writes TileSet layers, tile proxies and scene tiles in Godot's order.
- A property Godot registers with a type its getter does not return (SpringBoneCollision3D `BoneName`) is written as a plain attribute on every class, not `x:Prop`.
- A scene whose built-in sub-resource another scene borrowed, by Make Local or a drag, keeps that entry's `x:Resources` comment when saved after the other scene.
- A scene saved through a `res://` path containing `..` keeps its keyed sub-resources' `x:Resources` comments.
- Converting back writes PhysicalBone3D joint constraints and SoftBody3D attachments in Godot's order.
- Converting back writes slash-named properties such as Environment's `glow_levels/1` and joint `params/` at their place in Godot's order.

## 0.1.1 - 2026-11-03

### Fixed

- A scene whose AnimationPlayer has a default animation library converts to an `.xscn` that reads back.
- An `.xscn` edited outside the editor loads its new content at once, instead of the cached old scene until .NET collected garbage.
- Converting back to `.tscn` numbers a node's file references and orders placeholder properties as Godot does, and names `.json`, `.po`, `.mo`, `.gdextension` and `.ogv` types.
- Converting back writes animation tracks, bones, blend points, transitions, shader nodes, replication properties, tile patterns, IK and spring-bone chains and skeleton modifications in Godot's order.
- Converting back places names a class adds itself (`libraries/`, `shader_parameter/`, `current`) in that class's place, so an AnimationPlayer's libraries load before its current animation.
- `godot-xaml check` and every host report a second format prefix, an empty property element, an empty `x:Key` and a placeholder at the scene's root.
- A signal-named attribute on an instance that fails as a property now suggests `<x:Connect>`.
- A property Godot registers with a type its getter does not return (`Oversampling` on `AudioEffectPitchShift`) is written as an attribute of its default's kind instead of `x:Prop`.
- A `.tscn` property name holding a control character is reported as error GXC0119 instead of crashing the conversion.
- A scene holding another scene's built-in sub-resource, through Make Local or a drag in the inspector, keeps its own `x:Resources` keys and comments on save.
- Converting back warns (GXC0120) when `x:Uids` gives one path two different uids, as `convert` can make by rewriting one file's path onto another's.
- The core's `.tscn` reader rejects and reads a bare resource header, cross-typed name and path fields, and a first-node placeholder as Godot does.
- `convert` mints the same new ids for a project in any checkout, seeding them from each file's `res://` path.
- `convert --project` keeps as text the files that scripts of startup resources name, so they load before the addon.
- `convert`'s reference scan reads entity-encoded attribute values and whole attribute names, and an `x:Uid` entry split over lines.
- `godot-xaml` reads `@name` as a path, takes every argument after `--` as a path, and refuses NUL-holding input such as BOM-less UTF-16.
- C# names follow Godot's own case tables and count characters as Godot does, for names beyond ASCII.

### Changed

- The guides cover launching a `.xscn` with `--scene`, re-importing after the addon arrives, and the CRLF tool manifest a local install writes.

## 0.1.0 - 2026-11-02

### Added

- `.xscn` scenes and `.xres` resources: Godot scenes as nested XAML, named after Godot's C# API, with enum values by name.
- A Godot editor addon that opens, edits and saves `.xscn` and `.xres` files, keeping comments, resource keys and uids across saves.
- Exported games load `.xscn` and `.xres` as Godot binary: no addon code runs on a load, and no XML ships.
- The `godot-xaml` command-line tool checks, formats and converts files, printing problems in the form editors and CI read.
- `godot-xaml convert` turns `.tscn` and `.tres` into `.xscn` and `.xres` and back, rewriting references between the files it converts.
- `convert --project` converts a whole project all or nothing, backs up the originals, edits scripts' paths, and leaves files Godot loads before addons as text.
- The `GodotXaml.Core` NuGet package reads, writes and converts the formats from C#, with documentation for IntelliSense.
- Documentation: user guides, a format reference covering every value type and diagnostic code, and an agent guide shipped with a Claude Code skill.
