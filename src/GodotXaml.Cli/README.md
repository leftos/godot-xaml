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
