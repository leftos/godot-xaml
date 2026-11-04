# Language server: exploration map

The code map behind GXAML-14 (the language server) and its open design decisions, read before the design interview that starts that work. The library and shipping research is in [language-server-libraries.md](language-server-libraries.md). Line numbers are against `195e7a4`; re-check them before a brief cites one.

PROTOCOL: 1 overlay: none (no docs/agents/explore.md), read CLAUDE.md; 2 README: done; 3 Task Index: no row for the language server (nearest rows: "Parse an .xscn/.xres or change a diagnostic", "Change the godot-xaml command line"); 4 design docs: done (v1-design.md, DECISIONS.md, format/README.md); 5 source: done (Xscn parser/model, Diagnostics, Metadata, CLI, csproj/props/run.ps1/pack-release); 6 tree search: done for "language server|LSP" only.

GODOT tools: skipped, not a scene question. Linear item text taken from the prompt; MAIN.md read.

Anchor: git root X:/dev/godot-xaml (main checkout). Line numbers below are against HEAD 195e7a4.

# GXAML-14 Language server: map

## 0. Short answer

- Nothing exists. No project, no csproj, no test project, no doc beyond a few sentences. `docs/ARCHITECTURE.md` says "the language server and the editor extensions are still to come" (line 3) and has no Task Index row and no Layers entry for it.
- The core gives a server diagnostics (with spans) and formatting almost for free. It gives completions and hover only in part. The gaps are all in "where is the cursor": the model keeps spans for elements only, and the syntax tree is not exposed.
- Metadata has no doc text, so hover can show signatures (type, enum, default, hint) but no descriptions, unless a new data source is added.
- The CLI's helpers (MetadataFile, ProjectConfig, ProjectRoot, SceneFiles) are all `internal` to `GodotXaml.Cli`, a net10.0 Exe. A server cannot reuse them without moving them into a shared library, or copying them.

## 1. What the core gives

### 1a. Parse on broken text

- `XscnParser.Parse(text, kind, metadata)` always returns an `XscnParseResult(Document, Diagnostics, HasErrors)` and never throws on bad text: src/GodotXaml.Core/Xscn/XscnParser.cs:12, :20-33. `kind` is passed in by the caller (the CLI maps `.xscn`/`.xres` to Scene/Resource at src/GodotXaml.Cli/InputFiles.cs:44-48).
- Broken input that has no root element gives a stand-in root and `GX0004`: XscnTreeReader.cs:51-55, :69-72 (`XscnUnknownType("")`, `Name=""`). A half-typed document therefore still yields a model with whatever could be read.
- Diagnostics come back sorted by `Span.Start`: XscnTreeReader.cs:41.
- The XML level is `GuiLabs.Language.Xml` (`Parser.ParseText`, XscnTreeReader.cs:48). Library errors become `GX0001` through `XmlSyntaxErrors.Collect` (XscnTreeReader.cs:49).
- Unmeasured: how deep an XML nesting the tree reader and writer recurse. A grep for "depth" in src/GodotXaml.Core/Xscn finds only the writer's indent counter (XscnWriter.cs:49-69). The 512 nesting cap in ARCHITECTURE.md:57 is for the Godot value grammar (`GodotValueReader`), not for XML elements. A hostile deeply nested file could overflow the server's stack in the tree reader or writer. Add a test before shipping.

### 1b. Source spans: what the model keeps

- `TextSpan(start, length)` is in UTF-16 offsets: ARCHITECTURE.md:79, src/GodotXaml.Core/Diagnostics/TextSpan.cs.
- The model has spans on elements only. `XscnElement.Span` is "from its start tag's `<` to the end of its end tag": src/GodotXaml.Core/Xscn/XscnModel.cs:140-141. Set at XscnTreeReader.Elements.cs:135 (node and override) and :149 (resource) only.
- The model keeps no span for these:
  - `XscnProperty` (XscnModel.cs:254): no attribute span, no name span, no value span.
  - `XscnConnection` (:307): none.
  - `XscnUid`, `XscnNamespace`: none.
  - An element's tag-name span, or where the start tag ends.
  - `x:Prop`, property elements, and the `x:` attributes: none.
- Attribute name and value spans do exist but only transiently, inside the private `AttributeText(Prefix, LocalName, Value, Span, ValueSpan)` record (XscnTreeReader.Attributes.cs:241). It is used to report diagnostics; the model drops it. `ValueSpan` is the text between the quotes.
- Diagnostic spans, per DECISIONS.md:183 and the code: an element's diagnostics span its tag name (`NameSpan`, XscnTreeReader.cs:76); a value's diagnostics span the value text (Attributes.cs:172); an unknown or duplicate attribute spans the whole attribute (Attributes.cs:147-160).
- Consequence: hover and completion cannot be answered from the model alone. Finding "which attribute is the cursor in, and what is its class" needs either a new span-carrying model or the syntax tree.

### 1c. GuiLabs syntax tree exposure

- Not exposed, internally or publicly, past a single call. `XscnTreeReader.Read` creates `XmlDocumentSyntax syntax = Parser.ParseText(source)` as a local (XscnTreeReader.cs:48) and drops it. `XscnTreeReader` is `internal sealed` (:14); `XscnParseResult` carries no tree.
- `GodotXaml.Core.csproj` references `GuiLabs.Language.Xml` as a normal `PackageReference` (:22), so a server project that references the core gets it transitively and can call `Parser.ParseText` itself. The cost is parsing twice, and two views (model and tree) that must agree on offsets.
- Design options in section 8.

### 1d. Diagnostics and LineMap

- `XscnDiagnostic(Code, Severity, Message, Span)`, severities `Error` and `Warning` only: src/GodotXaml.Core/Diagnostics/XscnDiagnostic.cs:4-18. Codes `GX0001`-`GX0004` and `GX0101`-`GX0118` (:21-94). Every code has a heading in docs/format/diagnostics.md (checked by `DiagnosticsDocTests`).
- `LineMap(text).ZeroBased(offset)` returns a 0-based `LinePosition(Line, Column)` with the column in UTF-16 code units: src/GodotXaml.Core/Diagnostics/LineMap.cs:25-36. Those are the LSP default units. A line ends at LF, CR or CRLF (:48-59). There is NO inverse (position to offset), which completions and hover need. Gap: add `LineMap.Offset(line, column)`.
- `ZeroBased` throws for an offset outside `[0, length]` (:31-32). A server mapping edits must clamp.
- Beyond diagnostics, the parser also emits warnings the CLI prints (`GX0107` unknown class is the only warning per DECISIONS.md:183). `GXC`, `GXP`, `GXT` codes belong to conversion and CLI and are out of scope for a server.

### 1e. Formatting (XscnWriter)

- `XscnWriter.Write(document, metadata)` returns the full canonical text: src/GodotXaml.Core/Xscn/XscnWriter.cs:35. It is whole-document only; no range formatting.
- Formatting needs a parse without errors. `godot-xaml format` prints every diagnostic, then returns null (leaves the file untouched) when `result.HasErrors`: src/GodotXaml.Cli/FormatCommand.cs:108-116. Warnings do not stop it.

  The same method catches `CheckCommand.IsCoreRefusal` exceptions from the writer and reports them as an error (FormatCommand.cs:118-126; `CheckCommand.cs:84-91`, code `CliCodes.CoreRefusal`). The refusals are, per DECISIONS.md:196, a plain-text field holding a character XML 1.0 cannot hold (`ArgumentException`) or a lone surrogate. A server must catch those too.
- A server's `textDocument/formatting` would return one `TextEdit` replacing the whole range with the canonical text (or no edit if equal, FormatCommand.cs:88-91), and refuse with a message on errors. The canonical form is LF and a final newline (docs/format/README.md:441-445); the editor's buffer may be CRLF, so the edit replaces the whole range anyway.
- A comment is dropped by the writer only through conversions, not through format: `XscnWriter` writes `LeadingComments`/`TrailingComments` (model fields, XscnModel.cs:127-138), so format keeps comments.
- Info to carry into design: the comment/whitespace-only unmeasured case "format a document with warnings only (unknown class GX0107)": it writes the unknown tag as kept (DECISIONS.md:169).

### 1f. Metadata lookups (completions, hover)

All in src/GodotXaml.Core/Metadata/GodotMetadata.cs; records in GodotMetadataTypes.cs.

| Need | API today | Notes |
|---|---|---|
| Every class (element names) | `GodotMetadata.Classes` (:117), `GodotClassInfo.CSharpName`, `.Instantiable` (GodotMetadataTypes.cs:43,49) | Completion must filter `Instantiable` and, in a property element, to Resource classes (`Inherits(cls, "Resource")`, :134). Unmeasured whether the file marks abstract classes through `Instantiable` only. |
| Class by C# name | `FindClassByCSharpName` (:127) | |
| Properties of a class | `GodotClassInfo.Properties` is only the class's OWN list (GodotMetadataTypes.cs:58); `FindProperty/FindPropertyByCSharpName` walk parents (:147,:155) | No public "all properties including inherited" enumerator. `ChainOf` is `internal` (:140). Gap: new public API (or `InternalsVisibleTo`). Also need `IsStored` filter (GodotMetadataTypes.cs:127) and the `/` filter (names with `/` are not properties). |
| Attribute spelling of a property | `XscnPropertyNames.AttributeName(cls, godotName)` (XscnPropertyNames.cs:63) | Returns null when `x:Prop` is needed. Accessor `XscnParser.NamesOf` is `internal` (XscnParser.cs:41) but `new XscnPropertyNames(metadata)` is public (:29) and costs an index build, so a server must cache it per metadata. |
| Slash-named (dotted) attributes | `GodotClassInfo.ThemeOverrides` (full list incl. inherited, only Control/Window classes: GodotMetadataTypes.cs:64, DECISIONS.md:133), `GodotThemeOverrideInfo(Name, Type, Hint, HintString)` | `AttributeName` produces `ThemeOverrideConstants.separation` (XscnPropertyNames.cs:94-100). Other slash families (`metadata/`, `shader_parameter/`, Theme's `Button/styles/normal`) have no metadata (GXAML-78 "slash-named ClassDB properties" is open). |
| Enum values for a property | `XscnEnumText.For(metadata, property)` returns `XscnEnumNames` (`Names`, `IsFlags`): XscnEnumText.cs:112,17-29. `XscnValueText.TypeOf(metadata, cls, property)` (or `TypeOf(metadata, property)` when no class is known) gives `XscnValueType(VariantType, Names)`, typed by the default's kind for the class (XscnValueText.cs:11,61). Raw enums: `FindEnum` (GodotMetadata.cs:202), `GodotEnumMember(GodotName, CSharpName, Value)`. | Covers integer properties with a hint but no enum, via hint display names (DECISIONS.md:117, :171). Flags join with ", ". |
| Bool, numbers, vectors, colour | `XscnValueType.VariantType` string ("bool", "Vector2", "Color", ...) | Completion for bool: `True`/`False` (DECISIONS.md:63). |
| Defaults (hover, "omitted when default") | `FindDefault(cls, godotName)` (:166), `GodotPropertyInfo.DefaultText` | Good for hover. |
| Signals (event attributes) | `GodotClassInfo.Signals` own list; `FindSignalByCSharpName` walks parents (:196); `GodotSignalInfo(Name, CSharpName?, Arguments)` | Same inherited-enumeration gap as properties. Signals with a null `CSharpName` must be left out. `XscnSignalNames` (internal) holds the "attribute is a signal when no property has that C# name" rule (XscnSignalNames.cs:36); completion must reproduce it. |
| Theme override items | `FindThemeOverride` (:217) | |
| x: directives | Not in code as a list. They are in `NodeFormatAttributes` (Attributes.cs:25-38, private) and docs/format/README.md:342-365. Gap: a public table of x: elements and attributes per element kind. |
| Documentation text | None. See below. |

- Hover text from metadata alone: property Godot name, C# name, Variant type, enum, default text, `Hint`/`HintString`. No descriptions. The metadata JSON has no doc fields; a search for `doc|description|brief|tutorial` finds only 7 occurrences, and the file's per-property keys are `bitfield, className, default, enumType, hint, hintString, name, type, usage` (metadata/godot-4.7.json head; tools/metadata-dump/README.md owns the format).
- Possible doc source: Godot's class reference XML at `F:\Godot\src-4.7.2\doc\classes\*.xml` (810 files incl. non-xml entries; Button.xml exists). It has `<brief_description>`, `<description>`, `<member name= ...>` and `<signal>` per class. Options and costs in section 8.

  Also the dump (tools/metadata-dump/dump.gd) cannot read it from ClassDB without a docs-enabled build; the XML is the practical source. Docs there use BBCode with `[member]`, `[code]` and similar, and need conversion to Markdown. C# naming in the docs differs (Godot names), so the join key is the Godot class name and member name, and the server maps via `GodotClassInfo.Name`/`GodotPropertyInfo.Name`.

### 1g. Namespaces, xmlns and how a document names a Godot version

- A document does NOT name its Godot version. The `x:` namespace URI `https://github.com/leftos/godot-xaml/format/1` carries the FORMAT version only (DECISIONS.md:166, docs/format/README.md:58, XscnParser.cs:18). The format README says "for Godot 4.7" (:3).
- `XscnParser.Parse` takes the `GodotMetadata` from the caller. The CLI loads one file, `--metadata` or the shipped `godot-4.7.json` (MetadataFile.cs:9,17), and uses it for everything. The Godot addon uses `GodotMetadata.LoadBundled("4.7")` (ARCHITECTURE.md:81). So today the version is whichever bundle the host ships.

  `GodotMetadata.BundledVersions` lists what the core carries (GodotMetadata.cs:88); the core's csproj embeds `metadata/godot-*.json` by glob (GodotXaml.Core.csproj:28), so a new Godot minor needs no code change to bundle. Only `godot-4.7.json` exists in metadata/ today (the Cli.csproj hard-codes `godot-4.7.json`, Cli.csproj:29).
- Namespaces: `ReadNamespaces` (XscnTreeReader.cs:122-134) fills `XscnDocument.Namespaces` (prefix, `res://` folder) and the format prefix is held privately in `formatPrefix` (:27,:147). The `x` prefix is "whatever the root binds to the format URI" (DECISIONS.md:184).

  Resolving a tag's prefix: `KindOf` (:162). A server must repeat that prefix logic for completion inside a tag name (private). Scene tag to path: `SceneTag` (:190-195): folder + local name, extension from a matching `x:Uids` path else `.xscn`.
- Prefix names appear on the root only (GX0109). Completion of a prefix: from `Namespaces`, plus a "declare a new namespace" action is a nice-to-have.

## 2. How the CLI finds metadata and project config; what a server reuses

- MetadataFile (src/GodotXaml.Cli/MetadataFile.cs, `internal static`): `Shipped = AppContext.BaseDirectory/metadata/godot-4.7.json` (:9); `TryLoad(givenPath, error)` loads and turns each failure into one line on a `TextWriter` (:15-52). It does no version choice. A server needs an exceptions-to-diagnostics shape, not a TextWriter.
- ProjectConfig (ProjectConfig.cs, `internal static`): `godot-xaml.json` found by walking up from the working directory (:42-53, `FileName` :12). One setting only: `{"convert": {"overwrite": true}}`; any other key is an error (:65-69, :90-94). It holds no metadata path, no Godot version and nothing for a language server. The server would need the file extended (a new key means new validation, and the CLI rejects unknown keys, so adding keys is a joint change with the CLI) or a separate config.
- ProjectRoot (now `GodotXaml.Core.Projects.GodotProject`, with `DiskPath` for `res://` to disk, since GXAML-143; this section maps the code as it was before; formerly ProjectRoot.cs, `internal static`): `Find(fileFullPath)` walks up for the nearest `project.godot` (:11-21); `ResPath(root, file[, listings])` gives the `res://` path with each segment in on-disk casing (:27-46); `OnDiskName(name, entries)` (:79) is the case-folding lookup.

  Reusable as is for: validating a file reference (`Texture="res://..."`), completing paths (list the folder under the root), go-to-definition (map `res://` to a disk path, the inverse of `ResPath`: not written; `Path.Combine(root, rest)` plus on-disk casing). No `res://` to disk function exists.
- Also in the CLI and relevant: `StartupFiles` (project.godot settings read, `StartupFiles.Settings`), `ReferenceScan` (per-kind quoting rules, scenes and resources' references), `RunSceneFiles` (an `ISceneFiles` over disk, used by `--back`).

  `ISceneFiles` is a core interface (src/GodotXaml.Core/Conversion/ISceneFiles.cs); the server could implement it over open buffers plus disk to find an instanced scene's class, which `XscnToTscn` needs and which completion on an instance's attributes would need: an instance (`<c:Modal ...>`) has no known class, so attributes there are typed by the C# name across all classes (DECISIONS.md:57, format/README.md:96-100).

  Resolving the real class means reading the instanced scene's root tag; only the way-back conversion does that today (`InstancedClasses`).
- Moving these to a shared library (for example into the core, or a new `GodotXaml.Project` project) is the clean option. The core must stay Godot-free but can hold file-system helpers (it already has `ConversionOptions`); the CLI's rule is "CLI references the core only and is never referenced by it" (ARCHITECTURE.md:45). So the server must not reference the CLI project.
- `res://` handling in the language server needs the project root per document, which the CLI derives per file from `ProjectRoot.Find`. The server's equivalent is per document URI: walk up from the file; with no `project.godot`, treat `res://` references as unvalidated (CLI's `convert` skips such files).

## 3. Completion contexts and what answers them

| Context (cursor in) | Core API today | Missing (new core API / server work) |
|---|---|---|
| Element name: Godot class (child of a node) | `GodotMetadata.Classes` filtered `Instantiable`, `Inherits(cls,"Node")` for scenes | Public ordering and filtering helper; `Instantiable` may include classes that are not Nodes at all (filter on `Node`). Unmeasured whether the metadata marks editor-only classes. |
| Element name: scene instance (`<c:Modal>`) | `XscnDocument.Namespaces` (prefix to folder); `ProjectRoot` for the root | Listing `*.xscn`/`*.tscn` under a namespace folder is server/file-system work; no API. Needs to apply the `IsStemOf` rule (XscnTreeReader.cs:198) for the extension, and the XML-name check for file names (a base name that is not an XML name cannot be a tag, so completion should offer `x:Instance Scene=` for those). |
| Element name after a prefix `x:` | none public | Table of `x:` elements: Uids, Uid, Resources, Instance, Override, Connect, Prop (docs/format/README.md; kind checks at XscnTreeReader.Elements.cs). |
| Property element `<Owner.Property>` | Owner = the parent's tag (DECISIONS.md:177); property must be Resource-typed (`Inherits(..., "Resource")` on `GodotPropertyInfo.ClassName`/`Type=="Object"`) | Enumerating resource-typed properties of the parent's class; no helper. |
| Resource element inside a property element or `x:Resources` | Classes where `Inherits(cls,"Resource")` | Filter by the property's `ClassName` (accepts subclasses). |
| Attribute name on a known class | `Properties` (own only) + `FindPropertyByCSharpName` + `AttributeName`; `Signals`; `ThemeOverrides` | Inherited enumeration; the signal-vs-property clash rule (XscnSignalNames internal); `Name`/`Script`; omit names already present on the element. |
| Attribute name on an instance / override / unknown class | `XscnPropertyNames` `byCSharpName` (private index, all-classes agreement) | The index is private; no enumerator. Plain-name completion over all classes is unspecific. A good answer needs the instanced scene's class (see section 2, `ISceneFiles`). |
| `x:` attribute names | none public | Per-element-kind table (Attributes.cs:25-38 private; format/README.md:342-365). `x:Key` only on table entries (Attributes.cs:131), `Path` only on `x:Override`, `Scene` only on `x:Instance`. |
| Attribute value: enum / flags | `XscnValueText.TypeOf(...).Names.Names` (`XscnEnumName(Name, Value)`) | Resolving the attribute under the cursor to a property (needs the cursor-to-attribute step). For flags: offer the member at the cursor part, not the whole text. |
| Attribute value: bool | `VariantType == "bool"` | Trivial. |
| Attribute value: resource file path | `XscnFile`/`ClassName` of the property; `ProjectRoot.ResPath` | Path completion by listing project files filtered by class (Texture2D: png/svg; Script: gd/cs; Resource: tres/xres); no extension-to-class table in the core except `XscnToTscn.TypesByExtension` (Conversion; used for ext_resource guesses; internal?). |
| Attribute value: `{Key}` reference | `XscnDocument.Resources` (`Key`) | Easy once the model is current: keys from the parsed doc. Only valid on a resource-typed property. |
| Attribute value: signal handler method | none | Not knowable without reading the script (DECISIONS.md:85: "method name is the script's own"). Out of scope or script-scanning later. |
| Attribute value: `Script=` / `Scene=` / `Path=` on x:Override | `Script` path via files; override path from the instanced scene's nodes | Needs the instanced scene's node tree. |
| `x:Connect` attributes (Signal, To, Method, Flags) | `XscnSignalNames.ConnectFlags(metadata)` is internal; `GodotSignalInfo` | `Signal` values from the emitter's class (inherited enumeration again); `To` from node paths in the document (model gives `Children`, `Name`); `Flags` names via `XscnSignalNames.ConnectFlags` (internal). |
| Node `Name`, `x:Groups`, `x:NodePaths` | none | Free text; nothing to complete. |
| `xmlns:` declarations on the root | none | Folder listing under `res://` for scene namespaces. |

Gap summary (new core API work, in priority order):
1. Cursor context: a way to find, for an offset, the enclosing element, attribute (name or value), and whether the cursor is in a start tag, content, or comment. Either spans in the model, or a public wrapper over the GuiLabs tree plus the existing name resolution.
2. Inherited property and signal enumeration (public; today only own lists plus per-name finders).
3. A public table of `x:` elements and attributes per element kind.
4. `LineMap` inverse (position to offset).
5. `res://` to disk path and project file listing.
6. Scene lookup for instances (class of an instanced scene), reusing `ISceneFiles`/`InstancedClasses`.
7. Documentation text for hover (data work, section 8). Done: the docs file `metadata/docs/godot-4.7.json` (GXAML-137, `tools/class-docs/`); nothing reads it yet.

## 4. Build and repo facts

- Solution: `GodotXaml.slnx` (root), folders `/src/` (Cli, Core) and `/tests/` (Cli.Tests, Core.Tests). The Godot addon is deliberately outside (needs the Godot SDK; its own `GodotXaml.Addon.slnx`, ARCHITECTURE.md:44). A new `src/GodotXaml.LanguageServer/GodotXaml.LanguageServer.csproj` and `tests/GodotXaml.LanguageServer.Tests/...csproj` add two `<Project>` lines in the slnx.
- `global.json`: SDK 10.0.401, rollForward latestFeature; test runner Microsoft.Testing.Platform.
- `Directory.Build.props` (all projects): Nullable enable, ImplicitUsings enable, LangVersion latest, TreatWarningsAsErrors true, EnforceCodeStyleInBuild, `AnalysisLevel latest-recommended`, CodeAnalysisTreatWarningsAsErrors, CA1502 threshold via `CodeMetricsConfig.txt`; `VersionPrefix 0.1.0` plus a 7-char commit sha as InformationalVersion (props :11-34). `TargetFramework` stays in each csproj.
- Central package management is ON: `Directory.Packages.props` has `ManagePackageVersionsCentrally` and `PackageVersion` entries: GodotSharp 4.7.2, GuiLabs.Language.Xml 1.2.120, System.CommandLine 2.0.14, xunit.v3 4.0.1. The LSP library chosen later adds a `PackageVersion` line there; a csproj `PackageReference` carries no version.
- Target frameworks: Core net8.0 (the Godot addon loads it; Godot 4.7 minimum is net8.0: Core.csproj:3-4); Cli net10.0 Exe `PackAsTool` with `ToolCommandName godot-xaml`, `SatelliteResourceLanguages en` (Cli.csproj:3-16); both test projects net10.0 Exe with xunit.v3, `<Using Include="Xunit" />`, `IsPackable false`.

  A language server project may be net10.0 (CLI precedent) since it never loads inside Godot, but net8.0 would match what a bundled VS Code extension might run on; this is an open decision (section 8).
- `GenerateDocumentationFile` is true in Core, Cli and the addon csproj, so a missing `///` fails the build (CS1591). ARCHITECTURE.md:35 names three csproj; a new public-API project will join the rule unless the doc is changed.
- InternalsVisibleTo: Core exposes internals to `GodotXaml.Core.Tests` only (Core.csproj:33); Cli to `GodotXaml.Cli.Tests` (Cli.csproj:34). A server project needing `XscnTreeReader`, `XscnSignalNames` and `ChainOf` would need another entry (an alternative to making them public).
- CLI as .NET tool: `dotnet tool install -g GodotXaml.Cli`; metadata copied beside the assembly from `..\..\metadata\godot-4.7.json` (`CopyToOutputDirectory PreserveNewest`, Cli.csproj:29) and found through `AppContext.BaseDirectory` (MetadataFile.cs:9). Note the CLI does not use the core's embedded copy. A language server could use `GodotMetadata.LoadBundled` (no file path needed) and so need no beside-the-exe file.
- run.ps1 verbs: `help, build, godot-build, test, format-check, format` (run.ps1 header). `build` = `dotnet build GodotXaml.slnx -c Release -warnaserror`, `test` = build then `dotnet test` of the solution, `--no-build`; all go through `tools/gate.ps1`, logs in `.tmp/`. Hooks (godot-xaml-nextup SKILL.md Gates): pre-commit csharpier check + 150-column line check; pre-push `build` then `godot-build`; `test` is run by hand.
- Test conventions: tests mirror the source folders (ARCHITECTURE.md:116); CLI tests run in process (`CliProgram.Run` with string writers and per-test temp folders: ARCHITECTURE.md:139); `RepoFiles`/`RepoRoot.cs` find the repo root for fixtures. A server's tests should drive the server's request handlers in process (no process spawn) and, for one end-to-end test, a JSON-RPC pair over in-memory streams.
- Release packing: `tools/pack-release.ps1 -Version x.y.z`: checks four version homes (Directory.Build.props, godot/addons/godot_xaml/plugin.cfg, README.md PackageReference, AGENTS.md links), then `dotnet pack` of the Core and Cli projects and zips the addon, and asserts the output folder holds exactly `GodotXaml.Cli.<v>.nupkg`, `GodotXaml.Core.<v>.nupkg`, `godot-xaml-addon-<v>.zip` (pack-release.ps1:125-135 in the `Assert-ReleaseFolder` function).

  A new shipped artifact (a tool package or extension-bundled binaries) means editing `Invoke-Pack` calls, the `$expected` list and the usage text. `.github/workflows/publish-nuget.yml` pushes release packages to nuget.org by trusted publishing, and the nuget.org policy names that file (ARCHITECTURE.md Task Index row "Cut a release"). A new package therefore needs its own nuget.org trusted-publishing registration.
- Docs that must change with the new component (ARCHITECTURE.md:40 "Each component gets an entry here in the commit that adds its project"): ARCHITECTURE.md Layers + Task Index + Test locations; README.md:73; docs/guide/limitations.md:27 ("planned for later releases"); `docs/guide/getting-started.md` install steps; `llms.txt`; `agents/skill/godot-xaml/`; docs/plans/v1-design.md.

## 5. Existing mentions of the language server (every constraint)

Searched `language server|LanguageServer|LSP` across the repo (excluding metadata, .tmp):

- CLAUDE.md:7 "with a language server and a live preview for Visual Studio and VS Code"; :26 "the addon, the CLI and the language server are hosts over [the core]" (a rule: the server references the core; the core never references the server).
- README.md:73 "Still to come in version 1: a language server for Visual Studio and VS Code".
- src/GodotXaml.Core/README.md:80 (the repo includes "the language server" in its description).
- docs/plans/v1-design.md:11 component line: "**Language server** (.NET): completions, hover, diagnostics and formatting from the core and the metadata; shared by the Visual Studio and VS Code extensions."; :12-13 the extensions; :22 order "Language server, then the VS Code and Visual Studio extensions, then the preview pane."
- docs/DECISIONS.md:47 enum names dumped to metadata so "tools outside Godot (the language server, Visual Studio) can name them too"; :105 "Every host uses [the GuiLabs parser]: ... the language server reports errors as diagnostics and keeps going.

  One parser means one set of parsing rules everywhere."; :158 "One C# core ... serves the Godot addon, the CLI, the language server and the Visual Studio extension."; :168 diagnostics "UTF-16 offsets, a line map giving 0-based positions for the language server and 1-based ones for the loader.

  Any error fails a Godot load."; :222-223 scope: "Visual Studio: completions, enum names and errors while typing ... VS Code: the same language server."; :233 second release: "the language server and the VS Code extension, with the name checks against GodotSharp."
- docs/ARCHITECTURE.md:3 ("language server and the editor extensions are still to come"), :5 ("the Godot addon, the CLI and the language server are thin hosts over it"), :43 ("the core and the language server read [the metadata file], never a running Godot").

  Also :57 ("Nesting past 512 ... so a hostile text cannot overflow the stack of the language server or editor"), :79 ("`TextSpan(start, length)`, UTF-16 offsets into a source text, for the language server"; `LineMap` 0-based for the language server).
- docs/guide/limitations.md:27 "A language server (completion, hover and diagnostics in VS Code and Visual Studio) and a live preview pane in Visual Studio are planned for later releases. Until then, `godot-xaml check` and `format` are the tools outside the editor."
- docs/plans/archive/xscn-core.md:20 repeats the Diagnostics ruling.
- src/GodotXaml.Core/Diagnostics/LineMap.cs:25 "as the language server protocol counts them".
- docs/plans/MAIN.md:39-41 the three Linear lines; docs/plans/.linear-map.json:28 slug `lsp` = GXAML-14.
- Constraints pulled together: (a) tolerant parse, never throws (DECISIONS.md:105, XscnParser.cs:20); (b) offsets UTF-16 and positions 0-based (LineMap); (c) deep nesting must not overflow the stack (value grammar done; XML tree unmeasured); (d) the server is a host, the core stays Godot-free; (e) metadata is read from the file, never from a running Godot; (f) the same parser rules everywhere (no second XML parser in the server).
- Release placement conflict: see DOC DRIFT.

## 6. DEPENDS ON

Open items from the prompt, with what each means for GXAML-14 (from docs/plans/MAIN.md:5-47):

- GXAML-23 (GodotNames Unicode casing and check names against GodotSharp.dll; release vNext): CONSUMED by the server indirectly. Completion offers `CSharpName` values computed by `GodotNames` (class, property, signal names). DECISIONS.md:233 puts "the name checks against GodotSharp" in the same release as the server. A wrong casing would be a wrong completion. Not a hard block; a dependency on correctness. Unmeasured: whether GXAML-23 changes any 4.7 name today.
- GXAML-39 (editor-class enum names vs GodotSharpEditor; vNext): CONSUMED the same way, for enum member names on Editor* classes. Editor classes in scenes are rare, so a soft dependency.
- GXAML-75 (scene comparison covers resources; vNext): independent of the server. It checks the converter, which the server does not run. No dependency.
- GXAML-15 (VS Code extension; vNext): CONSUMES GXAML-14, blocked by it (per the prompt). Needs from 14: a launchable binary and its distribution, the config surface (metadata version, settings), a documented transport (stdio). The extension needs no extra server work beyond that.
- GXAML-16 (Visual Studio extension; vNext+1): CONSUMES GXAML-14, blocked by it. Visual Studio's LSP client has a different file-association and content-type setup, and a server shipped inside a VSIX; that decides whether the server is bundled self-contained or as a tool (section 8).
- GXAML-17 (VS preview pane; vNext+1): independent in code. It would share the same VSIX with GXAML-16. A possible sharing point: a custom LSP notification or command (e.g. "node under cursor", "reveal element at offset") for click-to-select. If the server defines it, 14 must leave room; if GXAML-17 uses the addon's socket (v1-design.md:28), no relation. Treat as an open question.
- GXAML-11 (addon preview server; vNext+1): independent of the server. No dependency.
- Not in the list but touching the work: GXAML-78 (metadata dump: slash-named ClassDB properties; open) limits dotted-attribute completion (metadata lacks e.g. Theme's `Button/styles/normal`, shader parameters); GXAML-49 (`GodotPropertyInfo.Default` points at `FindDefault`) touches the default for hover.

  GXAML-47 (parser: second format prefix, signal attributes on instances, empty property element) changes the parse the server surfaces; GXAML-24 (docs/DEVELOPMENT.md) will want the new project in its verb list; GXAML-29 (drop counts and path:line from durable docs) governs how the server's doc entries are written.

## 7. BRANCH

`BRANCH: feat/language-server`.

Evidence: a new component, with at least two new projects (src/GodotXaml.LanguageServer, tests/GodotXaml.LanguageServer.Tests), edits to the slnx, Directory.Packages.props, core API additions (cursor context, enumeration, LineMap inverse), possibly moving CLI helpers into a shared library (touching the CLI and its tests), docs in six places (section 4), and the release script.

It is bigger than one commit and it blocks two other items, so sub-steps should land on the branch and merge as steps. If the owner prefers trunk with small steps, steps 1-3 (core API additions and tests) are independent of the server project and could land on main first; this is a decision for the owner.

## 8. OPEN DECISIONS

### Settled

- Decision 1, the cursor (owner): option D, a public core API `XscnParser.ContextAt(text, offset, metadata)`, tested in the core test project.
- Decision 3, the library (owner): StreamJsonRpc plus hand-written types for the LSP messages the server uses, with System.Text.Json source generation.
- Decision 4, shipping (owner): option C, self-contained or NativeAOT server executables bundled per platform in each extension, so the user needs no .NET install; this brings a CI build matrix and a packaging script.
- Decision 5, metadata version (owner): option B, the project's Godot version read from `project.godot`'s `config/features`, its bundled metadata loaded, the newest bundled one with a warning when it is not bundled, and the version in use shown to the user.
- Decision 7, shared helpers (owner): moved into the core (it stays Godot-free); the CLI's tests follow the move.
- Decision 8, scope (owner): diagnostics, formatting, hover and completions; GXAML-15 (VS Code) starts once diagnostics work, beside hover and completions. Document symbols, go to definition, code actions and rename are backlog items.
- Decision 9, hover (owner): option C, full descriptions, from a docs file generated per Godot version from Godot's `doc/classes/*.xml` (BBCode converted) and shipped beside the metadata, with Godot's licence notice (Expat, MIT; checked, carried in the file). Its own issue; the docs file is done by GXAML-137.
- Branch (owner): the core additions (proposed steps 1-4) land on `main` as they pass; the server, its packaging and CI (steps 5-9) go on `feat/language-server` with a draft PR, merged once it works end to end.
- Decision 2, sync (orchestrator): full-text sync, a parse on each change with a debounce, a stale parse cancelled.
- Decision 6, project discovery (orchestrator): per document, a walk up for `project.godot` cached per folder, the workspace folders as a fallback; a document outside any project gets `res://` checks off and a hint; path completions list the folder on demand.
- Decisions 10 and 11 (orchestrator): Error and Warning map to LSP's, `code` is the `GX` code with a `codeDescription.href` to its heading in `docs/format/diagnostics.md`, and a metadata load failure is a `window/showMessage`; formatting is whole-document only, and a document with errors gets no edit.
- Decisions 12 and 13 (orchestrator): open buffers overlay the disk for `ISceneFiles`; logs go to `window/logMessage` and stderr, never stdout.

### Options

Each option with what the code makes realistic and its cost.

1. How the server sees the cursor (parse model).
   - A. Add spans to the model: attribute name span, value span, start-tag span, tag-name span on `XscnProperty`, `XscnConnection`, `XscnUid`, elements. Cost: touches the model records (XscnModel.cs), the reader, the writer and conversion constructors, and every test that builds `XscnProperty`/`XscnConnection` (positional records); the model's equality promise ("not promised to compare equal", XscnModel.cs:19-20) already allows spans.
   - B. Expose the GuiLabs tree from the parse (an `XscnParseResult.Syntax` or a new `XscnParser.ParseWithSyntax`) so the server resolves the cursor on the tree and then calls existing name resolution. Cost: makes the third-party type public API (`GuiLabs.Language.Xml` is Apache-2.0 and already a core dependency, but public exposure couples the core's API to it; the package is on nuget); small code change; the model and tree agree by construction.
   - C. The server runs `Parser.ParseText` itself next to `XscnParser.Parse`. Cost: no core change, double parse per keystroke (a scene file is small; unmeasured cost), the two views could diverge if the core's parse ever pre-processes text (it does not today: it passes `source` straight in, XscnTreeReader.cs:48).
   - D. A new core "XscnCursor"/"XscnContext" API: `XscnParser.ContextAt(text, offset, metadata)` returning (kind, class, attribute name, value range). The server stays thin; the logic gets tested in the core test project. Cost: new public surface in the core, but it keeps XML parsing rules in one place (matches DECISIONS.md:105).
2. Incremental sync and re-parse policy: full-text sync (parse the whole file on each change; simplest, files are small) or incremental. The parse is a whole-document tolerant parse with no incremental API (`Parse(string,...)` only, XscnParser.cs:25). Realistic: full sync, parse on change with a debounce, cancel stale parses. Cost of incremental: needs a rewrite of the core reader; not realistic for a first slice.
3. LSP library: another agent is researching; no recommendation here. Constraints the code puts on the choice: must run on net8.0 or net10.0, work with xunit.v3 in-process testing, and allow central package versions; hand-rolled JSON-RPC over stdio is also possible (small request set).
4. How the server is shipped.
   - A. A second .NET tool package (`GodotXaml.LanguageServer`, `dotnet tool install -g`). Needs dotnet SDK on the user's machine (a Godot .NET user has one; a VS Code user may not). Cheapest: copies the CLI's `PackAsTool` pattern (Cli.csproj:6-7), publishing path exists (nuget trusted publishing), and `pack-release.ps1` gains one `Invoke-Pack` and one expected file.
   - B. Add a `godot-xaml lsp` verb to the existing CLI (`System.CommandLine`, 2.0.14). One package, metadata already shipped beside it. Cost: the CLI would then reference an LSP library, and "CLI references the core only" (ARCHITECTURE.md:45) is false or the server project is referenced by the CLI; ARCHITECTURE.md:5 and CLAUDE.md:26 call the language server a separate host.
   - C. Self-contained, platform-specific executables bundled in each extension (a VSIX and a `.vsix` for VS Code per platform). No dotnet needed on the user's machine. Cost: three-plus RIDs of builds, size (a self-contained net runtime is tens of MB; trimming is unmeasured with a reflection-heavy JSON library), a new packaging script, and the release asserts exactly three files (pack-release.ps1:125). Needs a CI matrix.
   - D. Framework-dependent app in the extension plus a dotnet requirement check. Smaller than C, same prerequisite as A.
   - The choice also fixes the TargetFramework (section 4) and whether the metadata is read from `LoadBundled` (no file beside the exe) or a beside-the-exe copy (the CLI's way).
5. Which metadata version a document uses.
   - Today: one file, 4.7, picked by the host; the document says nothing (section 1g). Options:
   - A. Always the bundled latest; one version until a second metadata file exists (only 4.7 exists). Cheapest, matches the CLI.
   - B. Read the project's Godot version from `project.godot` (`config/features=PackedStringArray("4.7", "C#", ...)`), then `GodotMetadata.LoadBundled(minor)`; fall back to the newest bundled version with a warning when the project's version is not bundled. Needs a `project.godot` feature-tag reader (the CLI has a `project.godot` lexer in `ScriptLexers.ProjectFile.cs` for string literals, not for this). Realistic since `BundledVersions` exists (GodotMetadata.cs:88).
   - C. A setting (`godot-xaml.json` key or an LSP initialization option) naming the version or a metadata file path (like `--metadata`). Needed anyway for unbundled versions.
   - D. A version attribute in the document: no, it would change the format (DECISIONS.md:166 says the namespace URI is the format version). Not realistic without an owner ruling.
   - Related DECISIONS.md:59-61 says an upgrader handles typing changes across Godot releases (GXAML-38); a server on the wrong version gives false errors, so surfacing "metadata: Godot 4.7" in a status message matters.
6. How the server finds a document's Godot project.
   - Per document: walk up for `project.godot` (the CLI's `ProjectRoot.Find`), cached per folder; or use the workspace folders the client sends and look for `project.godot` in them (multi-root workspaces, VS "folder" mode vs solution mode in Visual Studio); or the client config. Realistic: per-document walk, workspace folders as a fallback.
   - Documents outside any project: `res://` checks off, a project-less hint.
   - File watching: new files change path completions; options are `workspace/didChangeWatchedFiles` (client must register) or list on demand (no cache; cost = a directory listing per completion).
7. Where the shared helpers live (MetadataFile, ProjectConfig, ProjectRoot, SceneFiles): copy into the server (duplication, two places to fix), move to the core (the core already holds `ISceneFiles`; adds file-system code but still Godot-free), or a new shared `GodotXaml.Project` library (a third package to publish). The CLI's tests exercise these via `CliProgram`, so a move needs those tests to follow.
8. Feature scope of the first slice. The Linear title lists four features; DECISIONS.md:222 (version 1 scope) names "completions, enum names and errors while typing" for Visual Studio. Order that the code makes cheap: diagnostics (done in core, needs only JSON-RPC and the LineMap) and whole-document formatting (done in core) first; then hover (metadata only, no docs); then completions by context (needs the cursor-context work, the largest).

   The owner chooses whether GXAML-15/16 may start after step 3 or must wait for completions. Features not in the title that LSP clients expect to be absent or present: semantic tokens, go to definition (`res://`, `{Key}`, scene tags), document symbols (outline from the node tree: cheap with the model's `Children`), code actions (quick fixes for common GX codes), rename of `x:Key` or node names: all out of the title, none mentioned in docs. A decision for scope.
9. Hover documentation.
   - A. No docs: signature and default only (zero data work).
   - B. Extend the metadata dump to emit descriptions: the dump (GDScript on `ClassDB`) cannot read docs unless Godot's docs are built in (unmeasured); the metadata file (over 2 MB) would grow.
   - C. Generate a separate `docs` file from `F:\Godot\src-4.7.2\doc\classes\*.xml` with a new tool (C# or script) and ship it beside the metadata; license: the class XML is under the engine's Expat (MIT) licence (`COPYRIGHT.txt`, `Files: *`), so the notice travels with the file (its `license` field and `metadata/docs/LICENSE-godot.txt`). BBCode conversion needed. Done by GXAML-137: `tools/class-docs/generate.ps1` writes `metadata/docs/godot-4.7.json`.
   - D. A link to the online docs page per class/property in hover (cheapest after A).
   - The data format must also be versioned per Godot minor, like the metadata.
10. Diagnostic mapping details: only Error and Warning exist (map to LSP Error/Warning); `code` = `GX....` (stable, documented in docs/format/diagnostics.md); a `codeDescription.href` to the heading is possible. Whether to surface `GXC`/`GXP` is not relevant. When the metadata file fails to load, the server needs a way to say so (the CLI exits 2; a server should start with a window message).
11. Formatting policy: whole-document edit only; behaviour on a document with errors (the CLI refuses and prints diagnostics, FormatCommand.cs:114-116; server returns no edits plus a message/the diagnostics already shown); format-on-save and format-on-type are client features. Range formatting: not supported by the writer.
12. Cross-file features read other files: the open-buffer overlay (unsaved scene text) versus disk, for `ISceneFiles`.
13. Logging and telemetry channel: stderr/`window/logMessage`; no stdout noise because stdio is the transport.

### Proposed step list (implementer-sized; each with files and a proving test)

Branch `feat/language-server`. Steps 1-4 are core work with plain unit tests. Steps 5-9 are the server.

1. LineMap inverse and range helpers.
   - Files: src/GodotXaml.Core/Diagnostics/LineMap.cs (add `Offset(line, column)` and a clamp variant), tests/GodotXaml.Core.Tests/Diagnostics/LineMapTests.cs.
   - Proof: `./run.ps1 test`; tests for LF, CR, CRLF, last line without a newline, column past end of line, UTF-16 surrogate pairs, round trip `ZeroBased`/`Offset` for every offset of a sample text.
2. Public enumeration in the metadata loader.
   - Files: src/GodotXaml.Core/Metadata/GodotMetadata.cs (public `PropertiesOf(cls)`, `SignalsOf(cls)`, `AncestorsOf` over the `ChainOf` at :140), tests/GodotXaml.Core.Tests/Metadata/GodotMetadataTests.cs, docs/ARCHITECTURE.md (Metadata loader bullet).
   - Proof: tests that `PropertiesOf(Button)` includes `Control`/`CanvasItem`/`Node` properties and no property with `/`, parent first or child first (decide), and that a signal with a null C# name is excluded.
3. The `x:` vocabulary and cursor context in the core (decision 1 picks the shape; the step is written for option D, adjust for A-C).
   - Files: new src/GodotXaml.Core/Xscn/XscnContext.cs (or the span changes in XscnModel.cs plus XscnTreeReader.*.cs), a public table `XscnFormatVocabulary` for `x:` elements and attributes per element kind drawn from Attributes.cs:25-50 and Elements, tests/GodotXaml.Core.Tests/Xscn/XscnContextTests.cs.
   - Proof: table-driven tests of an offset in each place (tag name, between attributes, attribute name, value, `{`, content, comment, a half-typed `<Button Name="A" Te` and `<Button.`), including broken text, asserting the class resolved, the prefix kind and the replacement range. A test that every `x:` name in the vocabulary is accepted by `XscnParser` without `GX0102`, and that docs/format/README.md's table lists the same names.
4. Move or copy the project helpers behind a library API.
   - Files: ProjectRoot, a res:// to disk resolver (new), metadata selection (`MetadataFile`-like with exceptions), placed per decision 7; adjust src/GodotXaml.Cli to use them if moved; tests (CLI's existing ones must still pass; new tests for res:// to disk with casing).
   - Proof: `./run.ps1 test` green with unchanged CLI behaviour; new tests for the inverse resolver and the version choice from `project.godot`.
5. Server project skeleton, transport, document store, diagnostics.
   - Files: src/GodotXaml.LanguageServer/ (csproj, Program.cs, document store, `DiagnosticsHandler`), tests/GodotXaml.LanguageServer.Tests/ (csproj), GodotXaml.slnx, Directory.Packages.props, docs/ARCHITECTURE.md (Layers entry, Task Index row, Test locations).
   - Proof: in-process test that `didOpen` of a broken fixture (tests/fixtures/xscn, or an inline text with `GX0104`) yields `publishDiagnostics` with the right code, severity, and a 0-based range matching `LineMap`; `didChange` replaces them; `didClose` clears; a hostile input (deeply nested XML, 10 000 elements) does not crash; a stdio round trip (initialize, shutdown, exit) over in-memory streams. `./run.ps1 build` passes with `-warnaserror` and CS1591 if documentation is on for the project.
6. Formatting.
   - Files: `FormattingHandler`, tests.
   - Proof: for each `.in.xscn`/`.out.xscn` pair under tests/fixtures/xscn/format/ the single `TextEdit` result equals the `.out` file; a canonical file gives no edits; a document with an error gives no edit and a message; a writer refusal (a control character in a name) is caught and reported, not thrown.
7. Hover.
   - Files: `HoverHandler`, tests.
   - Proof: hover on a class tag, a property attribute (type, enum, default from `FindDefault`), a signal attribute, an enum value, a `{Key}` (shows the entry's class), an `x:` attribute; null result on whitespace and in an unknown prefix.
8. Completion (split into two commits if large): element names and attribute names; then values (enums, flags, bool, `{Key}`, `res://` paths, x: directives).
   - Files: `CompletionHandler` (+ providers), tests.
   - Proof: table-driven: for a source with a cursor marker, the item labels equal the expected set (e.g. `<Button Mouse|` offers `MouseFilter`, `MouseDefaultCursorShape`, not `MouseFilter` twice if present; `MouseFilter="|"` offers `Stop, Pass, Ignore`; `Alignment="|"` for BoxContainer; `SizeFlagsHorizontal="Expand, |"` flags); the replacement range covers the typed prefix; no completions in comments or content text.
9. Packaging, docs, and extension hand-off.
   - Files: pack-release.ps1 (+ expected files), publish workflow, README.md:73, docs/guide/limitations.md:27, docs/guide/getting-started.md, llms.txt, agents/skill docs, docs/plans/v1-design.md, ARCHITECTURE.md.
   - Proof: `pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/pack.log --slot heavy --timeout 900 -- pwsh -NoProfile -File tools/pack-release.ps1 -Version 0.1.0` produces the expected folder; a smoke test launches the packed binary, sends initialize/shutdown over stdio and gets valid responses; the docs tests still pass.

Size note: steps 3 and 8 are the large ones; 5 needs the LSP library decided; 1, 2, 6 are small. Hover docs (decision 9) are a separate item if option B/C.

## DOC DRIFT

- docs/DECISIONS.md:233 (second release: "the language server and the VS Code extension") vs docs/plans/MAIN.md:39-41 (GXAML-14 and GXAML-15 both "release vNext"; GXAML-16 "vNext+1") vs DECISIONS.md:230-235 (release list: 1 first, 2 second, 3 third; the Linear names `vNext`, `vNext+1`, `vNext+2`).

  Under that mapping the first release is `vNext`, so the server and VS Code extension (the second release) would be `vNext+1` and the Visual Studio extension `vNext+2`. MAIN.md (generated from Linear) has them one release earlier. Code does not decide it; likely the Linear milestone assignment is stale or DECISIONS.md:230 means different labels. The owner (Linear) owns the fix; the doc to edit is DECISIONS.md "Releases" or the Linear releases.
- docs/ARCHITECTURE.md:45 says the CLI "references the core only" and the CLI's helpers (MetadataFile, ProjectConfig, ProjectRoot, SceneFiles) are internal. The v1-design line "shared by the extensions" and the "CLI finds project config" idea imply reuse the code does not allow today. Not a contradiction, a constraint: no doc fix needed until the helpers move.
- docs/ARCHITECTURE.md:35 names "the three csproj files" with GenerateDocumentationFile; a fourth project will join. Fix goes in ARCHITECTURE.md at the commit that adds the project.
- docs/ARCHITECTURE.md:57 says a nesting cap exists "so a hostile text cannot overflow the stack of the language server or editor", but the cap is on the value grammar only (GodotValueReader); the XML element depth has no cap in src/GodotXaml.Core/Xscn (grep "depth": only XscnWriter.cs:49-69).

  Which side is wrong: the doc over-promises for the whole parse path; unmeasured whether GuiLabs's parser or XscnTreeReader overflows. Owner of the fix: ARCHITECTURE.md (Value grammar bullet) or add a cap in code.
- docs/guide/limitations.md:27 says the server is "planned for later releases"; consistent with the code today. Becomes drift when step 9 lands.
- docs/ARCHITECTURE.md:3 (not drift) says "the language server and the editor extensions are still to come". Consistent.

## TASK INDEX GAP

The answer took many reads beyond the Task Index (about 25 reads and 4 searches). Rows that should exist:

- "Build or change the language server (completions, hover, diagnostics, formatting)": docs/plans/v1-design.md (Language server) → docs/DECISIONS.md ("One error-tolerant XML parser", "C# throughout", "Version 1 scope") → src/GodotXaml.Core/Xscn/ (`XscnParser`, `XscnTreeReader*.cs`, `XscnModel.cs`) → src/GodotXaml.Core/Diagnostics/ (`LineMap`, `XscnDiagnostic`) → src/GodotXaml.Core/Metadata/ (`GodotMetadata`).

  Then src/GodotXaml.Core/Xscn/ (`XscnPropertyNames`, `XscnValueText`, `XscnEnumText`, `XscnWriter`) → src/GodotXaml.Cli/ (`ProjectRoot`, `MetadataFile`, `FormatCommand` as the reference behaviour) → the new src/GodotXaml.LanguageServer/ and tests/GodotXaml.LanguageServer.Tests/.
- "Add a completion or hover source (a new metadata field, a doc text)": tools/metadata-dump/README.md → metadata/godot-4.7.json → src/GodotXaml.Core/Metadata/ → the server's providers.
- "Add a project, a package or a shipped artifact": GodotXaml.slnx → Directory.Packages.props → the csproj → tools/pack-release.ps1 (`Invoke-Pack`, `Assert-ReleaseFolder`) → .github/workflows/publish-nuget.yml → ARCHITECTURE.md (Layers, Test locations). The existing "Cut a release" row covers only part.
- The existing row "Parse an .xscn/.xres or change a diagnostic" has no mention that the model carries no attribute spans; a one-line note there saves the next reader the reading of XscnModel.cs.

## MISSING ENTRY POINT

- docs/ARCHITECTURE.md and docs/README.md exist and the Task Index exists. No docs/agents/explore.md overlay (CLAUDE.md read in its place).
- Glossary terms used in a project sense with no entry in docs/README.md: "metadata version" (how a Godot minor is chosen), "Host" (the addon, the CLI and the language server are "hosts"; CLAUDE.md:26 and ARCHITECTURE.md:5 use it as a project term), and, once the server exists, "Language server", "Cursor context" (new). "Scene lookup" and "Tolerant parse" exist and apply.
