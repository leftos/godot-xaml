# Changelog

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
