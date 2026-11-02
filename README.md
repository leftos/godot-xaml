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

## Status

In design; there is nothing to install yet. The plan is in [`docs/plans/v1-design.md`](docs/plans/v1-design.md) and the settled choices in [`docs/DECISIONS.md`](docs/DECISIONS.md). Version 1 targets Godot 4.7+ with .NET:

- `.xscn` and `.xres` open, edit and save in the Godot editor through a C# addon, with no `.tscn` beside them.
- A converter for existing `.tscn` and `.tres` files, which rewrites the `res://` references between the files it converts.
- A language server for Visual Studio and VS Code, and a live preview pane in Visual Studio.
- A CLI to convert, check and format.

## License

MIT.

The float text writer in `src/GodotXaml.Core/Values/Grisu2.cs` is a port of Godot's grisu2 and carries its MIT and Apache-2.0 notice, the one in Godot's `thirdparty/grisu2/LICENSE`.
