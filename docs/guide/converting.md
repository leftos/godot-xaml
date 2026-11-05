# Converting a project

`godot-xaml convert` turns `.tscn` and `.tres` files into `.xscn` and `.xres`, and back. Install it as a local tool pinned to the addon's version (`dotnet tool install GodotXaml.Cli --version <version>`), which runs as `dotnet godot-xaml convert`; a global install (`-g`) runs as `godot-xaml convert`. Every option and message is in [the CLI reference](../../src/GodotXaml.Cli/README.md#convert).

## A whole project

```sh
godot-xaml convert --project path/to/game
```

This converts every `.tscn` and `.tres` under the project. Then:

- every `res://` reference between the converted files is rewritten to the new names;
- every script (`.gd`, `.cs`) and `project.godot` string that is exactly a converted path is edited;
- the originals are removed.

The scenes then load as the same scenes they were.

Before running it:

- **Install the addon first** ([Getting started](getting-started.md)), so the editor can open what the run writes.
- **Commit, or have a clean working tree**, so the run's changes are a diff you can read and undo.

What the run guarantees:

- **All or nothing.** Every output is staged first. If any source fails to convert, nothing in the project changes, and the run lists the errors.
- **A backup.** Before anything changes, the run copies to `.godot-xaml-backup/<project name>/<time>/` in the folder above the project, so the copies are not in the project:
  - the originals and their `.uid` files;
  - every script and `project.godot` it edits;
  - every output `--force` overwrites.

  Git ignores the backup when that folder is inside a repository. The tool never deletes a backup, so they pile up until you delete them. A project with no folder above it (a drive root) is backed up inside itself. Pass `--nobackup` to skip it, for example when the project is in git.
- **What it skips.** It never touches:
  - the project's top-level `addons/` and `.godot/`;
  - folders whose names start with `.`, and folders holding a `.gdignore`;
  - nested projects;
  - the `bin` and `obj` folders beside a `.csproj`;
  - folder links.
- **What stays text.** Files Godot loads before any addon exists stay `.tscn` and `.tres`, since no loader for `.xscn` is registered yet. Those are the custom theme and font, the default bus layout, the custom mouse cursor, the default environment, the translations, and every file those reach. Each such file is named on its own line.
- **What it cannot change.** A path a script builds at runtime (`"res://levels/" + name + ".tscn"`) is not rewritten. The run warns about such paths (`GXP0006`, up to three lines per file, then one line for the rest), and they need a change by hand.

Read the warnings it prints: each names a file and a line.

### What it does not rewrite

The run edits only the project's scripts and `project.godot`, and only where a string is exactly a converted path. These break after it and need a change by hand:

- **Files outside the project root**: a test project, a build or smoke script, a theme or asset tool beside the game. The run never reads them, so it neither edits nor warns about them.
- **Code that recognises a scene by its extension**, such as a `SceneSuffix = ".tscn"` constant or a `path.EndsWith(".tscn")` test. A script inside the project gets a `GXP0006` warning for it; one outside gets nothing.
- **Code that reads a scene file as text**: a test that parses a `.tscn` to check a node or a property, or a tool that edits one with string or regex operations. An `.xscn` is XML, so that code needs rewriting, not only renaming.

Find them before converting. Outside the project, search for scene and resource paths:

```sh
rg -n "\.tscn|\.tres" --glob '!path/to/game/**'
```

Inside it, read the `GXP0006` warnings, which name each file and line that builds or tests a path the run did not rewrite.

## Back to `.tscn`

```sh
godot-xaml convert --project path/to/game --back
```

This does the same the other way. A converted project round-trips: back to `.tscn`, it loads as the same scenes, with Godot's property order restored. The exceptions are the order among a script's exports and inside a Theme or TileSet's properties, which an `.xscn` does not keep.

## Single files

```sh
godot-xaml convert ui/main_menu.tscn
```

This writes `ui/main_menu.xscn` beside the source and keeps the source. References between files converted in the same run are rewritten; a reference to a file the run did not convert stays as it is. Scripts and `project.godot` are only warned about, never edited, so the `--project` form is the one to use for a whole project.

## Keeping files canonical

```sh
godot-xaml check .
godot-xaml format --check .
```

`check` reports every parse problem (`GX` codes) in every `.xscn` and `.xres`, and exits 1 when a file has an error. `format --check` lists the files that are not in the canonical layout and exits 1 when any would change; `format` rewrites them. Either suits a pre-commit hook or CI.
