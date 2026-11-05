# GodotXaml.Core

The core of godot-xaml: it reads and writes Godot's XAML-style `.xscn` scenes and `.xres` resources, and converts them to and from Godot's own `.tscn` and `.tres` text. It references no Godot assembly, so it builds and runs its tests without the engine.

## Metadata

The engine's classes, properties, enums and defaults ship inside this assembly, one file per Godot version:

```csharp
IReadOnlyList<string> versions = GodotMetadata.BundledVersions;   // "4.7"
GodotMetadata metadata = GodotMetadata.LoadBundled("4.7");
```

`GodotMetadata.LoadBundled` throws an `ArgumentException` naming the versions the assembly carries when asked for one of another. `GodotMetadata.Load(path)` and `GodotMetadata.Load(Stream)` read a metadata file from anywhere else.

Every call below takes the metadata, since it types each attribute and names each enum member.

## Read an `.xscn` or `.xres`

`XscnParser.Parse` never throws on bad text. It returns the document it could build, every problem as a diagnostic, and whether any is an error:

```csharp
using GodotXaml.Core.Xscn;

XscnParseResult result = XscnParser.Parse(text, XscnDocumentKind.Scene, metadata);
foreach (XscnDiagnostic diagnostic in result.Diagnostics)
{
    LinePosition at = new LineMap(text).OneBased(diagnostic.Span.Start);
    Console.WriteLine($"({at.Line},{at.Column}): {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
}
if (result.HasErrors)
{
    return;
}
XscnDocument document = result.Document;
```

`XscnDocumentKind.Resource` reads an `.xres`. The document model (`XscnDocument`, `XscnNode`, `XscnResource`, `XscnProperty`, `XscnConnection`) is immutable, and keeps each part's comments. Every code is listed in [the diagnostics reference](https://github.com/leftos/godot-xaml/blob/main/docs/format/diagnostics.md).

## Write one

`XscnWriter.Write` gives the canonical text: the layout and attribute order every save uses, and every property the document holds (only a scalar `layout_mode` under a Container is left out).

```csharp
string text = XscnWriter.Write(document, metadata);
```

## Convert to and from `.tscn`

The conversions go model to model, so a `.tscn` is read with `TscnReader` and written with `TscnWriter`:

```csharp
using GodotXaml.Core.Conversion;
using GodotXaml.Core.Tscn;

TscnDocument tscn = TscnReader.Read(tscnText);
ConversionResult<XscnDocument> forward = TscnToXscn.Convert(tscn, metadata, new ConversionOptions());

ConversionResult<TscnDocument> back = XscnToTscn.Convert(forward.Document, metadata, new ConversionOptions { Seed = 42 });
string tscnAgain = TscnWriter.Write(back.Document);
```

Each result carries its `ConversionIssue`s (a `GXC` code, a severity, a message and where it is). A result with an error must not be written.

`ConversionOptions` holds what a host knows beyond the file:

- `Seed`: the seed new ids are drawn from;
- `Previous`: the file a save replaces, so its `x:Resources` keys and comments are kept;
- `SceneFiles`: a lookup that reads the files the conversion needs (instanced scenes, and the `.gdshader` or text `.tres` mesh a shader material or a mesh instance refers to), so properties come back in Godot's order;
- `ScriptClassOf`: a script's global class name.

`TscnReader.Read` throws `TscnParseException`, with Godot's own error text, for a file Godot's loader would refuse.

## Format reference

The `.xscn` and `.xres` formats are documented in [docs/format](https://github.com/leftos/godot-xaml/blob/main/docs/format/README.md), and every value type's text in [values.md](https://github.com/leftos/godot-xaml/blob/main/docs/format/values.md).

## License

MIT. The repository, with the addon, the CLI and the language server, is at [github.com/leftos/godot-xaml](https://github.com/leftos/godot-xaml).
