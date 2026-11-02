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

You need a Godot 4.7+ .NET project. `GodotXaml.Core` reaches NuGet with the first release; until then, run `dotnet pack src/GodotXaml.Core/GodotXaml.Core.csproj -c Release -o <folder>` here and add that folder as a NuGet source in your project's `nuget.config`.

1. Give the project a solution file if it has none, named after `dotnet/project/assembly_name`, or an export fails on the C# step: `dotnet new sln -n <AssemblyName>`, then `dotnet sln <AssemblyName>.slnx add <AssemblyName>.csproj`.
2. Copy `godot/addons/godot_xaml/` from this repository to `addons/godot_xaml/` in your project, dotfiles included.
3. Add the package to your `.csproj`: `<PackageReference Include="GodotXaml.Core" Version="0.1.0" />`.
4. Run `dotnet build` before you first open the project. An editor that finds no built assembly turns the plugin off.
5. Turn the plugin on: Project > Project Settings > Plugins > godot-xaml, or add this to `project.godot` before opening:

   ```ini
   [editor_plugins]
   enabled=PackedStringArray("res://addons/godot_xaml/plugin.cfg")
   ```
6. Open the project, then close it and open it again. Godot's first scan runs before the addon's classes register, so `.xscn` files show in the FileSystem dock only from the second open, which also writes their `.uid` sidecars. Commit those.

An export needs nothing more: with the plugin on, scenes ship as Godot binary.

To update, raise the package version and copy the folder again.

## Converting a project

`godot-xaml convert --project <root>` converts every `.tscn` and `.tres` under a project to `.xscn` and `.xres` (`--back` goes the other way), rewrites the `res://` references between them, and edits the scripts and `project.godot` that name a converted file. It is all or nothing: if any file fails, the project is left as it was. Otherwise the originals are copied to `.godot-xaml-backup/` before they are removed, unless you pass `--nobackup`. `addons/` is never touched. See [the CLI README](src/GodotXaml.Cli/README.md#convert).

## Status

Not released yet. The plan is in [`docs/plans/v1-design.md`](docs/plans/v1-design.md) and the settled choices in [`docs/DECISIONS.md`](docs/DECISIONS.md). Version 1 targets Godot 4.7+ with .NET:

- `.xscn` and `.xres` open, edit and save in the Godot editor through a C# addon, with no `.tscn` beside them.
- Exported games load `.xscn` and `.xres` as Godot binary, with no addon code and no raw XML shipped, once the `godot-xaml` editor plugin is enabled.
- A converter for existing `.tscn` and `.tres` files, which rewrites the `res://` references between the files it converts.
- A language server for Visual Studio and VS Code, and a live preview pane in Visual Studio.
- A CLI to convert, check and format.

## License

MIT.

The float text writer in `src/GodotXaml.Core/Values/Grisu2.cs` is a port of Godot's grisu2 and carries its MIT and Apache-2.0 notice, the one in Godot's `thirdparty/grisu2/LICENSE`.
