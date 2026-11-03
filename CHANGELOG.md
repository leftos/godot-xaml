# Changelog

## Unreleased

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
