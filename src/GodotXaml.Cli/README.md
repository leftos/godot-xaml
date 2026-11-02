# godot-xaml

The `godot-xaml` command-line tool checks Godot scenes and resources written as XAML, and will convert and format them.

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
