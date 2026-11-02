# GodotXaml.Core

The core of godot-xaml: it reads and writes Godot's XAML-style `.xscn` scenes and `.xres` resources, and converts them to and from Godot's own `.tscn` and `.tres` text. It references no Godot assembly, so it builds and runs its tests without the engine.

## Metadata

The engine's classes, properties, enums and defaults ship inside this assembly, one file per Godot version:

```csharp
IReadOnlyList<string> versions = GodotMetadata.BundledVersions;   // "4.7"
GodotMetadata metadata = GodotMetadata.LoadBundled("4.7");
```

`GodotMetadata.LoadBundled` throws an `ArgumentException` naming the versions the assembly carries when asked for one of another. `GodotMetadata.Load(path)` and `GodotMetadata.Load(Stream)` read a metadata file from anywhere else.

## License

MIT. The repository, with the addon, the CLI and the language server, is at [github.com/leftos/godot-xaml](https://github.com/leftos/godot-xaml).
