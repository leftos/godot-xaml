# Upgrading to a new Godot version

Each `.xscn` and `.xres` records, in the `x:Godot` marker on its root, the Godot version whose metadata it was written with. A newer editor keeps a file's own typing and marker when it saves, so opening a project in a newer Godot changes nothing in its files. Only `godot-xaml upgrade` moves a project to another version's metadata, and it moves every file at once. Every option and message is in [the CLI reference](../../src/GodotXaml.Cli/README.md#upgrade).

## When to run it

Run it when a godot-xaml release bundles the metadata of a newer Godot version and its notes say that version changes how a file reads. Update the addon and the CLI to that release first ([Getting started](getting-started.md)); the CLI carries only the versions its release bundles.

## The command

```sh
godot-xaml upgrade --to 4.8 --check path/to/game
godot-xaml upgrade --to 4.8 path/to/game
```

Run `--check` first. It writes nothing, names each file that would change and exits 1 when any would. Commit, or have a clean working tree, before the second run.

The run reads every `.xscn` and `.xres` under the project with the metadata its `x:Godot` marker names, and writes it canonically with the `--to` version's, stamping the new marker. It is all or nothing: with an error in any file nothing changes. Before it moves a file in, it copies the originals to `.godot-xaml-backup/<time>/`, which Git ignores. A file whose text would not change is not written.

It skips what `convert --project` skips, including the project's top-level `addons/`. An addon's files keep their marker, and every host still reads them by it.

## What the diagnostics mean

- **`GXT0006`**: a file's marker names another version than the `--from` you passed. Fix `--from`, or leave it out.
- **`GXT0007`**: a file has no marker and you passed no `--from`. Pass `--from` with the version the unmarked files were written with.
- **`GXT0008`**: a property the `--to` version no longer has. The run keeps it as an attribute under its Godot name holding its value text (`legacy_text='"old"'`), the form a script property has, and warns once per class and property in a file. Read the warnings: each names a property to review or replace by hand.
- **`GXT0004`**: a marker names a version the tool does not carry. That file is an error, so nothing is upgraded.

See [the diagnostics](../format/diagnostics.md#gxt0006-marker-disagrees-with---from) for each message.
