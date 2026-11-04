# godot-xaml

Godot scenes and resources as nested, readable XAML, opened, edited and saved by the Godot editor itself.

A `.tscn` file is flat: every node is a header, nesting is a `parent="Body/Layout"` path, and enum properties are stored as their backing integers (`layout_mode = 2`, `stretch_mode = 5`). godot-xaml stores the same scene as `.xscn`:

```xml
<MarginContainer Name="EncyclopediaTile"
    CustomMinimumSize="0, 300"
    SizeFlagsHorizontal="ExpandFill">
  <Button Name="%Press" ThemeTypeVariation="ActionCard" />
  <MarginContainer Name="Body" MouseFilter="Ignore" ThemeTypeVariation="CardInset">
    <VBoxContainer Name="Layout" MouseFilter="Ignore">
      <TextureRect Name="%Art"
          CustomMinimumSize="0, 230"
          MouseFilter="Ignore"
          ExpandMode="IgnoreSize"
          StretchMode="KeepAspectCentered" />
      <Label Name="%Name"
          ThemeTypeVariation="SubtitleLabel"
          HorizontalAlignment="Center"
          AutowrapMode="WordSmart" />
    </VBoxContainer>
  </MarginContainer>
</MarginContainer>
```

Names are those of Godot's C# API, and values are enum names, never integers.

## Install

You need a Godot 4.7 .NET project (tested on 4.7.2). The full walk-through is in [Getting started](docs/guide/getting-started.md).

1. Give the project a solution file (`.sln` or `.slnx`) that contains its `.csproj`, if it has none, or an export fails on the C# step: `dotnet new sln -n <AssemblyName>`, then `dotnet sln add <AssemblyName>.csproj`.
2. Download `godot-xaml-addon-0.3.0.zip` from [the latest release](https://github.com/leftos/godot-xaml/releases/latest) and unzip it at the project root, which puts it at `addons/godot_xaml/`, dotfiles included.
3. Add the package to your `.csproj`: `<PackageReference Include="GodotXaml.Core" Version="0.3.0" />`.

   The project's own `.csproj` must compile `addons/godot_xaml/`: a `<Compile Remove="addons\**" />` makes every file fail with `No loader found`. It also needs `<EnableDynamicLoading>true</EnableDynamicLoading>`. `godot/GodotXaml.Addon.csproj` is a working example.
4. Run `dotnet build` before you first open the project. An editor that finds no built assembly turns the plugin off.
5. Turn the plugin on: Project > Project Settings > Plugins > godot-xaml, or add this to `project.godot` before opening:

   ```ini
   [editor_plugins]
   enabled=PackedStringArray("res://addons/godot_xaml/plugin.cfg")
   ```
6. Open the project, then close it and open it again. Godot's first scan runs before the addon's classes register, so `.xscn` files show in the FileSystem dock only from the second open, which also writes their `.uid` sidecars. Commit those. A checkout Godot imported before the addon arrived fails every `.xscn` with "Failed loading scene" until it is imported again, by the editor or by `godot --headless --path <project> --import`; this holds for headless and scripted runs too.

An export needs nothing more: with the plugin on, scenes ship as Godot binary.

To update, raise the package version and replace `addons/godot_xaml/` with the new release's folder, then import the project again (an unzip leaves the zip's older timestamps, so Godot does not notice the change alone).

To run a `.xscn` from the command line, use `godot --path <project> --scene res://X.xscn`: a bare `.xscn` path plays the main scene instead.

The command-line tool installs best as a local tool pinned to the same version as the addon and `GodotXaml.Core`: run `dotnet new tool-manifest` if the project has no `.config/dotnet-tools.json`, then `dotnet tool install GodotXaml.Cli --version 0.3.0`, and run it as `dotnet godot-xaml`. A global install (`dotnet tool install -g GodotXaml.Cli`) also works.

## Language server

The language server gives an editor diagnostics, formatting, hover and completions for `.xscn` and `.xres` files over the Language Server Protocol on stdin/stdout. It is a NativeAOT executable, so it needs no .NET install. Each GitHub release carries it as six zips, `godot-xaml-ls-<version>-<rid>.zip`, for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64`; each holds the executable alone. [`.github/workflows/build-server.yml`](.github/workflows/build-server.yml) builds and attaches them. The macOS executables are signed and notarised; see [the limitations](docs/guide/limitations.md#tooling) for what that does and does not cover. No editor extension that launches it exists yet; point an editor's LSP client at the executable. See [Getting started](docs/guide/getting-started.md#editor-support).

## Converting a project

`godot-xaml convert --project <root>` converts every `.tscn` and `.tres` under a project to `.xscn` and `.xres` (`--back` goes the other way), rewrites the `res://` references between them, and edits the scripts and `project.godot` that name a converted file. It is all or nothing: if any file fails, the project is left as it was.

Otherwise the originals are copied to `.godot-xaml-backup/` before they are removed, unless you pass `--nobackup`. `addons/` is never touched, and the files Godot loads before any addon (the custom theme, the bus layout and the like) stay as text. See [the CLI README](src/GodotXaml.Cli/README.md#convert).

## Documentation

- [Getting started](docs/guide/getting-started.md), then [the editor](docs/guide/editor.md), [exporting](docs/guide/export.md), [converting a project](docs/guide/converting.md), [troubleshooting](docs/guide/troubleshooting.md) and [limitations](docs/guide/limitations.md).
- [The format reference](docs/format/README.md): every element and attribute, [every value type](docs/format/values.md) and [every diagnostic code](docs/format/diagnostics.md).
- [The CLI reference](src/GodotXaml.Cli/README.md) and [the core library](src/GodotXaml.Core/README.md).

## Status

0.3.0 is the current release, for Godot 4.7 with .NET; it lets a game load `.xres` files whose class comes from a mounted pack, and fixes a crash as Godot quits. What changed in each release is in [`CHANGELOG.md`](CHANGELOG.md). It ships:

- `.xscn` and `.xres` open, edit and save in the Godot editor through a C# addon, with no `.tscn` beside them.
- Exported games load `.xscn` and `.xres` as Godot binary, with no addon code run on a load and no raw XML shipped, once the `godot-xaml` editor plugin is enabled.
- A converter for existing `.tscn` and `.tres` files, which rewrites the `res://` references between the files it converts.
- A CLI to convert, check and format.
- A language server, as a native executable per platform attached to each release.

Still to come in version 1: the Visual Studio and VS Code extensions that use the language server, and a live preview pane in Visual Studio. The plan is in [`docs/plans/v1-design.md`](docs/plans/v1-design.md) and the settled choices in [`docs/DECISIONS.md`](docs/DECISIONS.md).

## License

MIT.

The float text writer in `src/GodotXaml.Core/Values/Grisu2.cs` is a port of Godot's grisu2 and carries its MIT and Apache-2.0 notice, the one in Godot's `thirdparty/grisu2/LICENSE`.

The case tables in `src/GodotXaml.Core/Names/GodotCaseTables.g.cs` are generated from Godot's `core/string/ucaps.h` and carry its MIT notice.
