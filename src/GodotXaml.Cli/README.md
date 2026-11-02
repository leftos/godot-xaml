# godot-xaml

The `godot-xaml` command-line tool checks, converts and formats Godot scenes and resources written as XAML.

## Install

```sh
dotnet tool install -g GodotXaml.Cli
```

## Check

```sh
godot-xaml check <path>...
```

Each path is a `.xscn` or `.xres` file, or a folder searched recursively for both. `--metadata <file>` selects the Godot metadata file; the default is the `metadata/godot-4.7.json` shipped with the tool.

A file whose text is not UTF-8 is refused, so it is never misread.

A path whose file name starts with `-` would read as an option; write it with a leading `./`, as in `godot-xaml check ./-name.xscn`.

Each problem is one line, in the form editors and CI read:

```text
ui/main.xscn(2,4): error GX0104: <Label> has no Name; every node needs one.
```

The last line is the summary:

```text
1 file(s) checked, 1 error(s), 0 warning(s)
```

The exit code is 0 when no file has an error, 1 when one does, and 2 for a usage error, a path that cannot be read, or a file that is not UTF-8 text.

## Convert

```sh
godot-xaml convert <path>... [--force] [--metadata <file>]
```

Each path is a `.tscn`, `.tres`, `.xscn` or `.xres` file, or a folder searched recursively for all four. Each file is converted the other way (`.tscn` to `.xscn`, `.tres` to `.xres`, and back) and written beside its source, which stays:

```text
ui/main.tscn -> ui/main.xscn
```

A file's uid moves with it: a `.tscn` header's `uid` is written to the new file's `.uid` sidecar (`main.xscn.uid`), and an `.xscn` with a sidecar gets that uid in the `.tscn` header.

An output that already exists, or its `.uid` sidecar, is refused, and nothing is written for that file:

```text
ui/main.tscn: ui/main.xscn exists (use --force)
```

`--force` overwrites for that run. To overwrite always, put a `godot-xaml.json` in the repo (the first one found walking up from the working directory is used):

```json
{ "convert": { "overwrite": true } }
```

Both files of a pair given in the same run (`main.tscn` and `main.xscn`) are refused, even under `--force`, so a run never converts a file onto its own source: `ui/main.tscn: ui/main.xscn is also being converted`.

Every reference a converted file makes to another file converted in the same run is rewritten in the output, so a scene that instances `res://ui/button.tscn` says `res://ui/button.xscn` instead: instance and inherited scene paths, file properties, `Resource("res://…")` in value text, `<x:Uids>` paths and `x:InstancePlaceholder`. Files are grouped by their nearest `project.godot`, and a reference is rewritten only within its own project. A reference to a file that was not converted stays as it is.

A reference to a converted file from a scene or resource the run does not write — including a source it refused or could not convert — is left as it is and warned about, with its file, line and column (`warning GXP0002`). The message says whether the reference carries a uid of its own — an `ext_resource`'s `uid` attribute, an `.xscn` or `.xres` `<x:Uids>` entry for its path, or a `uid://` in the same `Resource(…)` call — which keeps the reference resolving once the source is removed. A folder holding its own `project.godot` is another project, and a folder whose name starts with `.` or that holds a `.gdignore` is not part of the project's resources; none of them is scanned. Nor are the `bin` and `obj` folders directly beside a `.csproj`, for any file kind.

The project's `.gd` and `.cs` scripts and its `project.godot` are scanned too, and never written. A string literal that is exactly a converted path is warned about (`warning GXP0002`): `refers to res://ui/button.tscn, which this run converts to res://ui/button.xscn; no uid on this reference; it breaks once the source is removed`. A converted path inside a longer string is warned about and not rewritten (`warning GXP0006`: `holds …, which this run converts to …, inside a longer string; it is not rewritten`), and so is a literal ending in an extension the run converts, since a path built from it is not rewritten (`warning GXP0006`: `ends in .tscn, and this run converts such files to .xscn; a path built from it is not rewritten`). Comments are skipped. These warnings are counted in the summary line and do not change the exit code.

`export_presets.cfg`, `.gdextension` files, `.import` files, other `.cfg` files and `.csproj` files are not scanned.

A script that cannot be read, is refused access, or is not UTF-8 text is reported on its own line (`<path>: cannot be read.`, `<path>: access denied.`, `<path>: not UTF-8 text.`).

An output that cannot be written is counted as an error and the run goes on with the rest. A file the run does write that refers to that output's new path — quoted, or through an instance written as a namespace tag — is then warned about once (`warning GXP0005`), with a location when the path itself appears in the file, since the reference names a file the run never wrote; the old file is still there.

The source is kept, so it still carries the uid its output now has, and the run warns about it (`warning GXP0003`) until the source is removed. A source with no `project.godot` above it is converted with its references as they are, and warned about (`warning GXP0001`). Text the rewriter cannot change, such as a path in a value it could not parse, is warned about (`warning GXP0004`). Warnings do not change the exit code.

A problem with a file is one line, and that file is not written: a `.tscn` the reader cannot read (`error GXT0001`, with its line and column), a document the conversion cannot write (`error GXT0002`), a `.tres` holding a scene or a `.tscn` holding a resource (`error GXT0003`), or a conversion issue (`error GXC…` or `warning GXC…`, with where it is). The other files go on. The last line is the summary:

```text
3 converted, 1 refused, 1 error(s), 0 warning(s)
```

The exit code is 0 when every file converted, 1 when one had an error or was refused, and 2 for a usage error, a file that cannot be read or written, or a `godot-xaml.json` that is not valid.

## Format

```sh
godot-xaml format <path>... [--check] [--metadata <file>]
```

Each path is a `.xscn` or `.xres` file, or a folder searched recursively for both. Each file is rewritten in place in the canonical layout, and each one that changed is named:

```text
formatted ui/main.xscn
1 of 14 file(s) changed
```

`--check` changes nothing: it names each file that would change (`ui/main.xscn: not canonical`) and exits 1 when any would, for CI and hooks.

A file with an error is printed in `check`'s form and left as it is. The exit code is 0 when nothing failed (and, under `--check`, nothing would change), 1 otherwise, and 2 for a usage or I/O error.
