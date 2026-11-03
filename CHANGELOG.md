# Changelog

## Unreleased

### Fixed

- Converting back to `.tscn` numbers a node's file references and orders placeholder properties as Godot does, and names `.json`, `.po`, `.mo`, `.gdextension` and `.ogv` types.
- `godot-xaml check` and every host report a second format prefix, an empty property element, an empty `x:Key` and a placeholder at the scene's root.
- A signal-named attribute on an instance that fails as a property now suggests `<x:Connect>`.
- A `.tscn` property name holding a control character is reported as error GXC0119 instead of crashing the conversion.
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
