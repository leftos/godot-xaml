# godot-xaml — architecture

godot-xaml lets a Godot 4.7+ .NET project keep its scenes and resources as XAML-style `.xscn` and `.xres` files that Godot opens, edits and saves directly. The code so far is the metadata dump and the start of the C# core; the planned components and their order are in [`plans/v1-design.md`](plans/v1-design.md), and the settled choices in [`DECISIONS.md`](DECISIONS.md). Terms used in a project sense are in the glossary in [`README.md`](README.md).

The rule that shapes it: one C# core, with no Godot dependency, owns the format; the Godot addon, the CLI and the language server are thin hosts over it.

## Task Index

| Task | Files, in order | Deep doc |
|---|---|---|
| Change a format rule | [`DECISIONS.md`](DECISIONS.md) → [`plans/v1-design.md`](plans/v1-design.md) | — |
| Add or change what the metadata dump records (property, enum, default) | [`DECISIONS.md`](DECISIONS.md) ("The metadata file") → `tools/metadata-dump/dump.gd` → `metadata/godot-4.7.json` → Godot source `F:\Godot\src-4.7.2`: `core/core_bind.cpp:1839-1879` (the ClassDB calls), `core/object/class_db.cpp:2172-2227` (defaults), `core/extension/extension_api_dump.cpp:53-85` (enum type strings; the dump reads the getter's return type) | [`tools/metadata-dump/README.md`](../tools/metadata-dump/README.md) |
| Look up a Godot class, property, enum or signal, or change how enum members are named in C# | [`tools/metadata-dump/README.md`](../tools/metadata-dump/README.md) (the file's format) → `src/GodotXaml.Core/Metadata/` (`GodotMetadata`) → `src/GodotXaml.Core/Names/` (`GodotNames`, `GodotEnumNames`) → Godot source `F:\Godot\src-4.7.2`: `modules/mono/editor/bindings_generator.cpp` (`_determine_enum_prefix`, `_apply_prefix_to_enum_constants`) | [`tools/metadata-dump/README.md`](../tools/metadata-dump/README.md) |
| Read or write a value in Godot's text grammar, or change float text | [`DECISIONS.md`](DECISIONS.md) ("The metadata file", "Test fixtures are synthetic") → `src/GodotXaml.Core/Values/` (`GodotValueLexer`, `GodotValueReader`, `GodotValueWriter`, `GodotFloatFormat`) → `tools/value-fixtures/` and `tests/fixtures/values/` → Godot source `F:\Godot\src-4.7.2`: `core/variant/variant_parser.cpp` (`VariantParser::get_token`, `parse_value`, `VariantWriter::write`, `rtos_fix`), `thirdparty/grisu2/grisu2.h` | [`tools/value-fixtures/README.md`](../tools/value-fixtures/README.md) |
| Read or write a `.tscn`/`.tres` file | [`DECISIONS.md`](DECISIONS.md) ("Test fixtures are synthetic") → `src/GodotXaml.Core/Tscn/` (`TscnReader`, `TscnWriter`, `TscnDocument`) → `tools/scene-fixtures/` and `tests/fixtures/scenes/` → Godot source `F:\Godot\src-4.7.2`: `scene/resources/resource_format_text.cpp` (`ResourceFormatSaverTextInstance::save`, `ResourceLoaderText`), `scene/resources/packed_scene.cpp` | [`tools/scene-fixtures/README.md`](../tools/scene-fixtures/README.md) |
| Change how an `.xscn` attribute's name or value maps to a Godot property | [`DECISIONS.md`](DECISIONS.md) ("Attribute values are typed by the metadata", "Slash-named properties are dotted attributes", "Integer properties with a hint but no enum") → [`plans/xscn-core.md`](plans/xscn-core.md) → `src/GodotXaml.Core/Xscn/` (`XscnPropertyNames`, `XscnValueText`, `XscnEnumText`) → `src/GodotXaml.Core/Metadata/` | — |
| Parse an `.xscn`/`.xres` or change a diagnostic | [`DECISIONS.md`](DECISIONS.md) ("Attribute values are typed by the metadata", "Slash-named properties are dotted attributes") → [`plans/xscn-core.md`](plans/xscn-core.md) → `src/GodotXaml.Core/Xscn/` (`XscnParser`, `XscnTreeReader*.cs`, `XmlText`, `XmlSyntaxErrors`) → `src/GodotXaml.Core/Diagnostics/` (`XscnCodes`, `XscnDiagnostic`, `LineMap`) | — |
| Register or debug the `.xscn` loader or saver in the editor | [`DECISIONS.md`](DECISIONS.md) ("The addon registers from C#, as global classes") → `spikes/xscn-loader/addons/xscn_spike/` → Godot source `F:\Godot\src-4.7.2`: `core/io/resource_loader.cpp:1656-1713`, `core/io/resource_saver.cpp:230-283`, `editor/file_system/editor_file_system.cpp:2199-2205`, `modules/mono/csharp_script.cpp:594-1030` | [`spikes/xscn-loader/README.md`](../spikes/xscn-loader/README.md) |
| Add or change a C# gate, hook or test location | [`.claude/skills/godot-xaml-nextup/SKILL.md`](../.claude/skills/godot-xaml-nextup/SKILL.md) (Gates) → Test locations below → `run.ps1`, `.pre-commit-config.yaml`, `Directory.Build.props`, `.editorconfig` | — |

## Layers

Each component gets an entry here in the commit that adds its project.

- **Metadata dump** (`tools/metadata-dump/`): a GDScript run by headless Godot that writes `metadata/godot-<major>.<minor>.json` from `ClassDB` and `extension_api.json`. It shares no files with the C# core; `dump.ps1` runs it and `check.gd` checks its output.
- **Metadata file** (`metadata/godot-4.7.json`): the committed output, one per Godot minor version; the core and the language server read it, never a running Godot.
- **Loader spike** (`spikes/xscn-loader/`): a throwaway Godot 4.7 .NET project that proved a C# loader and saver for `.xscn` work in the editor (`DECISIONS.md`, "The addon registers from C#, as global classes"). Its spike `.xscn` format is not the real one. It is deleted once the Godot addon (GXAML-9) replaces it, and nothing else may depend on it.
- **Value grammar** (`src/GodotXaml.Core/Values/`): Godot's text syntax for a value, as `GodotValue` records read by `GodotValueReader.Read` and written by `GodotValueWriter.Write` (`ValueWriteOptions` sets compat mode). Float text is `GodotFloatFormat`, over a port of Godot's grisu2 (`Grisu2.cs`).

  It covers the whole grammar: null, bool, int, float, String, StringName, NodePath, the math and colour constructors, RID, Signal and Callable; arrays and typed arrays (`GodotArray`, `GodotTypeRef`); dictionaries and typed dictionaries (`GodotDictionary`, `GodotEntry`).

  It also covers the packed arrays (`GodotPackedBytes`, `GodotPackedInts`, `GodotPackedLongs`, `GodotPackedFloats`, `GodotPackedDoubles`, `GodotPackedStrings`, `GodotPackedReals`) with their legacy `Pool*Array` aliases; references (`GodotResourceRef` for `ExtResource` and `SubResource`, `GodotPathRef` for `Resource(...)`); and `Object(...)` (`GodotObject`, `GodotProperty`).

  Dictionaries are written in Godot's key order (`GodotKeyOrder`, internal): string keys by code point, other keys by Variant type then value, and keys Godot cannot order keep source order. `ValueWriteOptions.Compat` also decides `PackedByteArray`: a number list on, `PackedByteArray("base64")` off.

  Nesting past 512 arrays, dictionaries or objects raises `GodotValueParseException` ("Nesting is too deep"), so a hostile text cannot overflow the stack of the language server or editor. `GodotValueLexer` and `GodotValueReader.ReadAt` are internal, for the `.tscn` reader.
- **Metadata loader** (`src/GodotXaml.Core/Metadata/`): `GodotMetadata.Load` reads `metadata/godot-<minor>.json` into immutable records (`GodotClassInfo`, `GodotPropertyInfo` with a typed `Default` and `IsStored`, `GodotEnumInfo`, `GodotEnumMember`, `GodotThemeOverrideInfo`, `GodotSignalInfo`, `GodotSignalArgument`), each with its C# name. `FindDefault(cls, godotName)` returns a property's effective default for a class (`GodotPropertyDefault`: text and value) or null: none for a class with `hasDefaults` false, else from the class's own declaration, else its `defaultOverrides`, else its parent's; it throws for a class the metadata lacks.

  Lookups are `FindClass`, `FindClassByCSharpName`, `Inherits`, `FindProperty` and `FindPropertyByCSharpName` (both walk up the parents), `FindEnum` (the class's enum, then the global one) and `FindThemeOverride`. A malformed file raises `InvalidDataException` naming what is wrong. A signal whose name starts with `_` has no C# name, as Godot keeps it out of its C# API.

  Enum member names come from `GodotEnumNames` (`Names/`, internal), a port of Godot's enum naming in `bindings_generator.cpp`: shared prefix stripping, the FLAG/FLAGS and digit rules, and the global `Error` enum's forced prefix.
- **Fixture generator** (`tools/value-fixtures/`): a headless Godot 4.7.2 project that writes `tests/fixtures/values/{canonical,loose,floats,format4}/`, Godot's own bytes, never edited by hand. It shares no files with the C# core.
- **Tscn** (`src/GodotXaml.Core/Tscn/`, namespace `GodotXaml.Core.Tscn`): the `.tscn`/`.tres` document model. `TscnReader.Read` gives a `TscnDocument` (header, ext and sub resources, resource properties, nodes, connections, editables) with values as `GodotValue`s; `TscnWriter.Write` writes it back byte-identical for files Godot 4.7.2 wrote.

  `TscnWriter.FormatOf` picks format 3 or 4 as Godot's saver does. `TscnDocument.NewExtResourceId` and `NewSubResourceId` mint ids from a caller's seed in Godot's id alphabet.

  The reader rejects what Godot's text loader rejects, with Godot's error text (`TscnParseException`), and accepts what it accepts. Unknown attributes on a known tag are kept verbatim; `load_steps` is read and dropped. The writer refuses a resource document that holds nodes and a scene that holds resource properties.
- **Scene fixture generator** (`tools/scene-fixtures/`): a headless Godot 4.7.2 project that saves each case with `ResourceSaver.save` and copies the bytes to `tests/fixtures/scenes/`. It shares no files with the C# core.
- **Xscn model** (`src/GodotXaml.Core/Xscn/`, namespace `GodotXaml.Core.Xscn`): the immutable `.xscn` document model. `XscnDocument` holds namespaces, uids, the root and the resources; elements are `XscnClassType`, `XscnSceneType` or `XscnUnknownType`, with `XscnNode`, `XscnOverride`, `XscnResource`, `XscnProperty` and `XscnConnection`. Each element, property, connection and uid, and the document's two tables (`UidsLeadingComments`, `ResourcesTrailingComments` and their pairs), holds the text of its comments in `LeadingComments` or `TrailingComments`. A property value is an `XscnScalar`, `XscnFile`, `XscnKeyRef`, `XscnInline` or `XscnInvalid`. Constructors reject uninitialised arrays.

  `XscnPropertyNames` maps an attribute name to a Godot property and back (known class, unknown class through the global C# name index, dotted slash-named attributes, script properties verbatim); `AttributeName` returns null when `x:Prop` is needed.

  `XscnValueText` (`TypeOf`, `Read`, `Write`) gives each type its compact form and Godot value text otherwise; `Write` returns `XscnAttributeText` or `XscnNeedsProp`. `XscnEnumText` names enum members, .NET-style flags text and hint display names as Godot's inspector numbers them.
- **Xscn parser** (`src/GodotXaml.Core/Xscn/`): `XscnParser.Parse(text, kind, metadata)` always returns an `XscnParseResult`: a document plus its diagnostics and `HasErrors`, never throwing on bad text. `XmlText` reads the XML through GuiLabs.Language.Xml, `XmlSyntaxErrors` turns the library's errors into `GX0001`, and `XscnTreeReader` (split into `.Elements`, `.Attributes` and `.Format` partials) builds the model, using `XscnPropertyNames` and `XscnValueText` for attributes.
- **Diagnostics** (`src/GodotXaml.Core/Diagnostics/`): `TextSpan(start, length)`, UTF-16 offsets into a source text, for the language server; `XscnDiagnostic` (code, `XscnSeverity`, message, span) with the stable `GX` codes in `XscnCodes`; `LineMap` turns an offset into a 0-based (language server) or 1-based (loader) `LinePosition`.
- **Core library** (`src/GodotXaml.Core`, net8.0): owns Godot's C# naming rules (`Names/GodotNames.cs`, a port of `modules/mono/utils/naming_utils.cpp` in the 4.7.2 source; enum members in `Names/GodotEnumNames.cs`), the value grammar, the metadata loader, the Tscn document model, the Xscn model and the Xscn parser above (its one dependency is GuiLabs.Language.Xml, for the XML syntax tree). It is packed as the NuGet package `GodotXaml.Core` (package readme `src/GodotXaml.Core/README.md`), which a game project references beside the copied addon folder, and it carries the metadata files as embedded resources, read with `GodotMetadata.LoadBundled`, since inside Godot the assembly is loaded from a stream and has no file path. The canonical writer and the `.tscn` conversion are built here next (GXAML-37, GXAML-7).

  It never references a Godot assembly, and its internals are visible to `tests/GodotXaml.Core.Tests`.

## Integration Footguns

- The dump needs the console build of Godot on Windows (`Godot_console.exe`); the GUI build loses Godot's output and exit code.
- `metadata/godot-<minor>.json` is about 1.9 MB, so `.pre-commit-config.yaml` raises `check-added-large-files` to 4096 KB.
- `tests/fixtures/values/**` is `-text` in `.gitattributes` and excluded from the trailing-whitespace, end-of-file and mixed-line-ending hooks, because the files are Godot's exact bytes (no final newline, raw control characters). Regenerate them with the generator; never edit one.
- gdformat on Windows rewrites `.gd` with CRLF; `.gitattributes` keeps `*.gd` LF. The gdformat and gdlint hooks cover `tools/metadata-dump/`, `tools/value-fixtures/` and `tools/scene-fixtures/`.
- The fixture generator needs the console build of Godot and fails on a double-precision build.
- `tests/fixtures/scenes/**` is Godot's exact bytes too (`tests/fixtures/** -text`, excluded from the whitespace hooks). A node's `unique_id` cannot be pinned from a script, so every run of `tools/scene-fixtures/` rewrites those numbers: regenerate only when a case changes, and commit the whole folder. No fixture has `uid=`, since a script run has no editor file system to supply uids; the writer's unit test covers it.

## Test locations

- `tools/metadata-dump/check.gd`: facts about a dumped file, run with `godot --headless --path tools/metadata-dump --script res://check.gd -- --file <abs>`; prints `CHECK <id> PASS|FAIL`.
- `tests/GodotXaml.Core.Tests` (net10.0, xunit.v3 on Microsoft.Testing.Platform): tests of `src/GodotXaml.Core`, in folders mirroring the core's (`Names/` for `GodotNames`). Run with `./run.ps1 test`.

  `Metadata/` holds the loader (`GodotMetadataTests`). `Names/` also holds `GodotEnumNamesTests` and `GodotSharpEnumTests`, which checks every class and global enum's member names against GodotSharp 4.7.2's compiled assembly, read with `System.Reflection.Metadata` from the `GodotSharp` NuGet package (referenced for the file only, `ExcludeAssets="all"`); editor-class enums are skipped, being in GodotSharpEditor (GXAML-39).

  `Values/` holds the float format against Godot's corpus, the lexer, scalar values (`ScalarValueTests`), containers (`ContainerValueTests`), Godot's key order (`GodotKeyOrderTests`), packed arrays (`PackedValueTests`), references and `Object` (`ReferenceValueTests`), the fixture files and every default in `metadata/godot-4.7.json` read and written back unchanged (`MetadataDefaultsTests`); `RepoFiles` finds the repo root.

  `Xscn/` holds the model (`XscnModelTests`), attribute names (`XscnPropertyNamesTests`), value text (`XscnValueTextTests`) and enum text (`XscnEnumTextTests`).

  `Xscn/` also holds the parser (`XscnParserTests`) and its diagnostics (`XscnParserErrorTests`); `Diagnostics/` holds `LineMapTests`.

  `Tscn/` holds the round trip of every scene fixture (`TscnRoundTripTests`), the document models and Godot's rejections and acceptances (`TscnReaderTests`), and the writer's layout, format switch, minted ids and refusals (`TscnWriterTests`).
- `tests/fixtures/values/`: the expected value text, written by `tools/value-fixtures/` in `canonical/`, `loose/`, `floats/` and `format4/` (the saver's text with compat off); `tests/GodotXaml.Core.Tests/Values/` reads it.
- `tests/fixtures/scenes/`: `.tscn` and `.tres` files saved by Godot's text saver, written by `tools/scene-fixtures/`; `tests/GodotXaml.Core.Tests/Tscn/` reads it.

## Deep docs

None yet.
