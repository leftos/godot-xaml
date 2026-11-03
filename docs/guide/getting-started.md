# Getting started

godot-xaml lets a Godot 4.7+ .NET project keep its scenes as `.xscn` and its resources as `.xres`: nested, readable XML that the Godot editor opens, edits and saves like any other scene. This guide installs it and makes a first scene.

The other guides:

- [Working in the editor](editor.md)
- [Exporting a game](export.md)
- [Converting a project](converting.md)
- [Troubleshooting](troubleshooting.md)
- [Limitations](limitations.md)

The format itself is in the [format reference](../format/README.md).

## What you need

- **A Godot 4.7 .NET project** (the editor build with C# support; tested on 4.7.2). The addon is written in C#, so a GDScript-only build of Godot cannot load it. GDScript scripts in the project are fine.
- **The .NET SDK** that build of Godot asks for.

## Install the addon

1. **Give the project a solution file** (`.sln` or `.slnx`) that contains its `.csproj`, if it has none; otherwise an export fails on the C# step:

   ```sh
   dotnet new sln -n <AssemblyName>
   dotnet sln add <AssemblyName>.csproj
   ```
2. **Copy the addon in.** Download `godot-xaml-addon-<version>.zip` from [the latest release](https://github.com/leftos/godot-xaml/releases/latest) and unzip it at the project root, which puts it at `addons/godot_xaml/`, dotfiles included.
3. **Reference the core package** in the project's `.csproj`, at the same version as the addon:

   ```xml
   <PackageReference Include="GodotXaml.Core" Version="0.1.0" />
   ```

   The `.csproj` must compile `addons/godot_xaml/`: a `<Compile Remove="addons\**" />` makes every scene fail with `No loader found`. It also needs `<EnableDynamicLoading>true</EnableDynamicLoading>`, which Godot's own template sets.
4. **Build once** with `dotnet build`, before you first open the project. An editor that finds no built assembly turns the plugin off.
5. **Turn the plugin on**: Project > Project Settings > Plugins > godot-xaml. Or add this to `project.godot` before opening:

   ```ini
   [editor_plugins]
   enabled=PackedStringArray("res://addons/godot_xaml/plugin.cfg")
   ```

   The plugin makes exports convert each `.xscn` and `.xres` to binary. The loader and saver work without it, but an export without it ships the raw XML instead, which the game still loads, more slowly, through the addon's loaders; it is not a supported setup.
6. **Open the project, close it, and open it again.** Godot scans the project's files before the addon's classes register, so `.xscn` files show in the FileSystem dock only from the second open. That open also writes their `.uid` sidecars. Commit those.

   The addon's loaders register from Godot's global class cache (`.godot/global_script_class_cache.cfg`), which only an editor scan or `godot --headless --import` rewrites. A checkout that Godot imported before the addon arrived fails every `.xscn` with "Failed loading scene" until it is imported again, headless and scripted runs included. Run `godot --headless --path <project> --import` once after installing.

To update, raise the package version and replace `addons/godot_xaml/` with the new release's folder. Unzipping over the old folder leaves files with the zip's older timestamps, so import the project again (open it in the editor, or run `godot --headless --path <project> --import`).

To open a `.xscn` from a script or a headless run, pass it with `--scene`: `godot --path <project> --scene res://ui/main_menu.xscn`. A bare `res://ui/main_menu.xscn` on the command line is not taken as a scene, because Godot does that only for `.scn`, `.tscn`, `.escn`, `.res` and `.tres`, and the main scene runs instead.

## Install the command-line tool

The `godot-xaml` tool checks, formats and converts files outside the editor, for scripts, hooks, CI and agents:

```sh
dotnet tool install -g GodotXaml.Cli
```

As a local tool (`dotnet tool install GodotXaml.Cli`), dotnet writes `.config/dotnet-tools.json` with CRLF line endings and no final newline. A repository that enforces LF will see its hooks change that file; commit it as the hooks leave it.

Its commands are in [the CLI reference](../../src/GodotXaml.Cli/README.md).

## A first scene

In the editor, choose Scene > New Scene, add nodes, and save with the `.xscn` extension (`ui/main_menu.xscn`). Godot writes XML like this:

```xml xscn
<Control xmlns:x="https://github.com/leftos/godot-xaml/format/1" Name="MainMenu">
  <VBoxContainer Name="Buttons" Alignment="Center">
    <Button Name="%Play" Text="Play" />
    <Button Name="Quit" Text="Quit" />
  </VBoxContainer>
</Control>
```

It is as editable by hand as by the editor:

- **Each node is an element**, tagged with its class.
- **Each property is an attribute**, named and valued as in Godot's C# API: `Alignment="Center"`, not `alignment = 1`.
- **Defaults are left out**, so the file holds only what you changed.

Edit the file in any text editor, and Godot picks up the change as it does for a `.tscn`. Edit it in Godot and the file is rewritten in one fixed layout, so a diff shows only what changed.

To check the files for parse problems from the command line:

```sh
godot-xaml check ui/
```

A resource is the same, with `.xres` for the extension: save a Theme, a StyleBox or a Material as `ui/panel.xres`.

## Already have scenes?

`godot-xaml convert --project <root>` converts every `.tscn` and `.tres` in a project, and rewrites the references between them. See [Converting a project](converting.md).
