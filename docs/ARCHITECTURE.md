# godot-xaml — architecture

godot-xaml lets a Godot 4.7+ .NET project keep its scenes and resources as XAML-style `.xscn` and `.xres` files that Godot opens, edits and saves directly. The only code so far is the metadata dump; the planned components and their order are in [`plans/v1-design.md`](plans/v1-design.md), and the settled choices in [`DECISIONS.md`](DECISIONS.md). Terms used in a project sense are in the glossary in [`README.md`](README.md).

The rule that shapes it: one C# core, with no Godot dependency, owns the format; the Godot addon, the CLI and the language server are thin hosts over it.

## Task Index

| Task | Files, in order | Deep doc |
|---|---|---|
| Change a format rule | [`DECISIONS.md`](DECISIONS.md) → [`plans/v1-design.md`](plans/v1-design.md) | — |
| Add or change what the metadata dump records (property, enum, default) | [`DECISIONS.md`](DECISIONS.md) ("The metadata file") → `tools/metadata-dump/dump.gd` → `metadata/godot-4.7.json` → Godot source `F:\Godot\src-4.7.2`: `core/core_bind.cpp:1839-1879` (the ClassDB calls), `core/object/class_db.cpp:2172-2227` (defaults), `core/extension/extension_api_dump.cpp:53-85` (enum type strings; the dump reads the getter's return type) | [`tools/metadata-dump/README.md`](../tools/metadata-dump/README.md) |
| Add or change a C# gate, hook or test location | [`.claude/skills/godot-xaml-nextup/SKILL.md`](../.claude/skills/godot-xaml-nextup/SKILL.md) (Gates) → Test locations below → `run.ps1`, `.pre-commit-config.yaml`, `Directory.Build.props`, `.editorconfig` | — |

## Layers

Each component gets an entry here in the commit that adds its project.

- **Metadata dump** (`tools/metadata-dump/`): a GDScript run by headless Godot that writes `metadata/godot-<major>.<minor>.json` from `ClassDB` and `extension_api.json`. It shares no files with the C# core; `dump.ps1` runs it and `check.gd` checks its output.
- **Metadata file** (`metadata/godot-4.7.json`): the committed output, one per Godot minor version; the core and the language server read it, never a running Godot.
- **Core library** (`src/GodotXaml.Core`, net8.0): owns Godot's C# naming rules (`Names/GodotNames.cs`, a port of `modules/mono/utils/naming_utils.cpp` in the 4.7.2 source); the format, parser, writer and `.tscn` conversion are built here next (GXAML-21, GXAML-22, GXAML-5). It never references a Godot assembly, and its internals are visible to `tests/GodotXaml.Core.Tests`.

## Integration Footguns

- The dump needs the console build of Godot on Windows (`Godot_console.exe`); the GUI build loses Godot's output and exit code.
- `metadata/godot-<minor>.json` is about 1.9 MB, so `.pre-commit-config.yaml` raises `check-added-large-files` to 4096 KB.

## Test locations

- `tools/metadata-dump/check.gd`: facts about a dumped file, run with `godot --headless --path tools/metadata-dump --script res://check.gd -- --file <abs>`; prints `CHECK <id> PASS|FAIL`.
- `tests/GodotXaml.Core.Tests` (net10.0, xunit.v3 on Microsoft.Testing.Platform): tests of `src/GodotXaml.Core`, in folders mirroring the core's (`Names/` for `GodotNames`). Run with `./run.ps1 test`.

## Deep docs

None yet.
