---
name: godot-xaml
description: Use when reading, writing or editing a Godot `.xscn` scene or `.xres` resource (godot-xaml's XAML format), when a Godot project has `addons/godot_xaml/`, when converting `.tscn`/`.tres` files to or from `.xscn`/`.xres`, or when a `godot-xaml check` or a Godot load reports a `GX`, `GXC`, `GXP` or `GXT` code.
---

# godot-xaml

godot-xaml stores Godot scenes as `.xscn` and resources as `.xres`: XML in the XAML style, with nodes nested as elements and properties as attributes named after Godot's C# API. The Godot editor opens, edits and saves them through the addon in `addons/godot_xaml/`. The `godot-xaml` command-line tool checks, formats and converts them.

[reference.md](reference.md) beside this file is the condensed format; the full reference is [docs/format](../../../docs/format/README.md).

## Is the project using it?

A project uses godot-xaml when it has `addons/godot_xaml/plugin.cfg`, or `.xscn`/`.xres` files.

- **In such a project,** write new scenes as `.xscn` and new resources as `.xres`.
- **Never keep a scene in both formats.** Never write a `.tscn` beside an `.xscn` of the same scene; the editor would load either, and the two drift.
- **Moving existing files** to the format is the project owner's call. Do not convert a project, or a file, without being asked.
- **A file left as `.tscn` or `.tres` in a converted project** may be one Godot loads before any addon (the custom theme, the bus layout). Leave it as text.

## Writing an `.xscn` by hand

```xml xscn canonical
<Control
    xmlns:x="https://github.com/leftos/godot-xaml/format/1"
    Name="MainMenu"
    Script="res://ui/main_menu.gd">
  <VBoxContainer Name="Buttons" Alignment="Center">
    <Button Name="%Play" SizeFlagsHorizontal="ExpandFill" Text="Play" />
    <Button Name="Quit" Text="Quit" />
  </VBoxContainer>
</Control>
```

The rules that trip agents most:

1. **The root declares `xmlns:x="https://github.com/leftos/godot-xaml/format/1"`.** No default `xmlns`.
2. **Every node has a `Name`.** A unique name is `Name="%Play"`.
3. **Tags are C# class names** (`CpuParticles2D`, not `CPUParticles2D`).
4. **Attributes are C# property names** in PascalCase (`SizeFlagsHorizontal`).
5. **Enum values are C# member names**, never integers (`MouseFilter="Ignore"`, `SizeFlagsHorizontal="ExpandFill"`).
6. **Value forms:**
   - vectors are their components (`Position="32, 16"`);
   - colours are exactly four floats (`Modulate="1, 1, 1, 0.5"`), never hex;
   - floats have no trailing `.0`;
   - booleans are `True` and `False`.
7. **Strings are unquoted** (`Text="{0} items"`).
8. **Godot value text for the rest.** Transforms, arrays, dictionaries, packed arrays, and every script property are Godot's own text, as in a `.tscn`. Strings there are quoted: `speed="2.5"`, `title='"Main menu"'`, `target='NodePath("../Hero")'`.
9. **Files are `res://` paths** (`Texture="res://art/hero.png"`). In value text a file is `Resource("res://…")`. Never `ExtResource(…)`.
10. **A sub-resource used once** is a property element:

    ```xml
    <Button.Theme>
      <Theme … />
    </Button.Theme>
    ```

    One used twice, or from inside value text, goes in `<x:Resources>` with an `x:Key`, referenced as `{Key}` or `SubResource("Key")`.
11. **Leave defaults out.** A value equal to the default is noise; `godot-xaml format` removes it.
12. **Nodes inside an instanced scene** are changed with `<x:Override Path="Child/Path" …/>`, a direct child of the instance element. Never re-declare the instanced scene's nodes.
13. **Signals:** a connection to the scene root is an attribute on the emitter, `Pressed="OnPlayPressed"`. Any other connection is `<x:Connect Signal="Pressed" To="Panel" Method="Hide" />`.

## After every edit

```sh
godot-xaml check <files or folder>
godot-xaml format <files or folder>
```

`check` reports every parse problem as `path(line,col): error GXnnnn: message`. Fix each one; [reference.md](reference.md#codes-you-will-meet) lists the common codes. `format` rewrites files into the canonical layout the editor writes, so a later editor save does not produce a noisy diff. Commit only files that `check` passes.

`check` does not run the conversion a load runs. A few problems show only when Godot loads the file, as `GXC` codes in the Output panel; an unknown `{Key}` (`GXC0102`) is the common one. When a Godot run is available, load the scene once, for example with godot-mcp, to catch them.

When the tool is missing, it installs with `dotnet tool install -g GodotXaml.Cli`.

## Uids

- **Commit `.uid` sidecars.** A scene's own uid is in its sidecar, `main_menu.xscn.uid`. Never delete one, and never invent a uid value.
- **Leave `<x:Uids>` to the tools.** It maps each referenced path to its uid, and the editor maintains it on save. When you add a reference by hand, the `res://` path alone is enough.
- **Move and rename in the Godot editor,** or with a tool that drives it. That rewrites every reference. A move done with `git mv` leaves references to the old path, resolved only through `<x:Uids>` until the next save.

## Converting

```sh
godot-xaml convert --project <root>          # every .tscn/.tres to .xscn/.xres, all or nothing
godot-xaml convert --project <root> --back   # and back
```

Run these only when the owner asks. The run backs up the originals to `.godot-xaml-backup/`, rewrites references and script strings, and warns about paths it could not rewrite as `GXP0006`, up to three lines per file and then one line for the rest. Those need a change by hand.

## When godot-xaml itself is wrong

File an issue on [github.com/leftos/godot-xaml](https://github.com/leftos/godot-xaml/issues) for any of these:

- a file the editor saved that `check` rejects;
- a conversion that changes a scene;
- a message that does not help;
- a missing feature.

Give the Godot and godot-xaml versions, the exact message, and the smallest `.xscn` that shows it. Work around the problem in the project, never in the addon's files.
