# godot-xaml docs

## Map

- [`guide/getting-started.md`](guide/getting-started.md): the guides for people using godot-xaml: installing, [the editor](guide/editor.md), [exporting](guide/export.md), [converting a project](guide/converting.md), [troubleshooting](guide/troubleshooting.md) and [limitations](guide/limitations.md).
- [`../agents/skill/godot-xaml/SKILL.md`](../agents/skill/godot-xaml/SKILL.md): the docs for agents, a Claude Code skill installed user-level by `tools/install-skill.ps1`; the same rules ship in the addon as [`AGENTS.md`](../godot/addons/godot_xaml/AGENTS.md), and [`../llms.txt`](../llms.txt) indexes the docs.
- [`format/README.md`](format/README.md): the `.xscn` and `.xres` format reference, with [`format/values.md`](format/values.md) for every value type's text and [`format/diagnostics.md`](format/diagnostics.md) for every error and warning code.
- [`ARCHITECTURE.md`](ARCHITECTURE.md): the components, what each owns, and where to start for a change.
- [`DEVELOPMENT.md`](DEVELOPMENT.md): the toolchain, the `run.ps1` verbs, the hooks, the Godot-side harnesses and regenerating the metadata file.
- [`DECISIONS.md`](DECISIONS.md): the owner's decisions and the Godot source facts behind them.
- [`plans/v1-design.md`](plans/v1-design.md): version 1's components, order of work and open questions.
- [`plans/MAIN.md`](plans/MAIN.md): a snapshot of the Linear plan (team GXAML), generated, never edited by hand.

## Glossary

- **Backup folder**: `<root>/.godot-xaml-backup/<UTC stamp>/`, where `convert --project` copies each original, its `.uid`, each script or `project.godot` it edits and each output it overwrites, at their paths under the root, before changing anything.
- **Builder**: another machine the gate routes a run to, by this PC's gate `remote.json` (`gate remote add`); the editor checks run there, on a hidden desktop.
- **Built path**: a path a script assembles at run time (`"res://ui/" + name + ".tscn"`) instead of writing whole; `convert` cannot rewrite it and warns (`GXP0006`) when a literal ends in an extension the run converts.
- **Canonical format**: the one layout every save writes, whatever the file looked like before, so a diff shows only real changes.
- **Canonical fixture**: an `.xscn` or `.xres` under `tests/fixtures/xscn/canonical/` that the writer must reproduce byte for byte.
- **Case tables**: Godot's lower-to-upper and upper-to-lower code point tables from `core/string/ucaps.h`, written as C# by `tools/case-tables/` into `GodotCaseTables.g.cs` and used by `GodotCase` for the case of a C# name.
- **Compat mode**: the writer flag Godot uses to write a file as format 3: byte arrays as number lists and negative infinity as `inf_neg`; off, it writes base64 and `-inf`. `ValueWriteOptions.Compat`, on by default.
- **Creating key**: the one key on which Godot creates an entry of a dynamic family (`type` of an Animation track, `name` of a Skeleton3D bone, `path` of a replication property); the way back writes it first in its entry.
- **Diagnostic code**: the stable code naming one kind of problem: `GX` plus four digits for the parser (`XscnCodes`), `GXC` for the conversion (`ConversionCodes`), `GXP` and `GXT` for the CLI (`CliCodes`). Each has a heading in [`format/diagnostics.md`](format/diagnostics.md).
- **Dynamic family**: the property names one class adds in its `_get_property_list` rather than registering them with ClassDB (`libraries/<name>` on AnimationMixer, `shader_parameter/<name>` on ShaderMaterial), so the metadata does not list them; `DynamicPropertyFamilies` holds them by declaring class, and the way back places them in that class's slot.
- **Doc example**: a fenced block in the docs whose info string is `xml xscn` or `xml xres`, optionally with `canonical` or `invalid <code>`; the docs tests parse every one.
- **Document store**: `DocumentStore` in `src/GodotXaml.LanguageServer/`, the text of the buffers the editor has open; it overlays the disk for the scene lookup (`ISceneFiles`).
- **Editor checks**: `godot/editor-checks.ps1`, which runs the real addon in a real Godot editor on a hidden desktop and checks opening, editing, saving, running, instancing, a save after an assembly reload and the FileSystem dock, Paste and Save As (`tools/editor-checks/`).
- **Effective default**: a property's default for a given class, as Godot saves against it: none for a class Godot keeps no defaults for (a virtual class such as `BaseButton`); else the class's own declaration's default (none when it declares the property without one); else its override; else its parent's effective default. `GodotMetadata.FindDefault`.
- **Entry kind**: how Godot creates a dynamic family's entries on load, stated by each row of `DynamicPropertyFamilies`: `AnyOrder` (each name sets its value on its own, or any key creates the entry), `SizedFirst` (a named count property, ranked before the family, sizes the entries; a row may carry the declaring class's key order and a nested level), `OnKey` (only the creating key creates an entry), `ByMode`, `Ranked`, `ByName` (Godot lists the names in code-point order) or `BySource` (the order comes from a sibling property's shader or mesh).

  `ByMode`: the key order is chosen by a sibling property's integer value, as VisualShader `flags/` by `mode`. `Ranked`: a ranker method computes the family's whole rank, as `DynamicPropertyFamilies.AtlasTileRank` does for TileSetAtlasSource tiles; the row names a class only when the ranker reads that class's key list.
- **Export plugin**: `XscnExportPlugin`, the Godot `EditorExportPlugin` that converts each `.xscn`/`.xres` to Godot binary when a project is exported; the editor plugin `XscnEditorPlugin` registers it.
- **Fixture generator**: `tools/value-fixtures/`, the headless Godot script that writes the expected value text under `tests/fixtures/values/`; `tools/scene-fixtures/` does the same for scenes and resources under `tests/fixtures/scenes/`.
- **Format pair**: an `.in.xscn`/`.out.xscn` pair under `tests/fixtures/xscn/format/`; the writer's output for the `.in` document must equal the `.out` file, which proves the canonical rewrite.
- **Format prefix**: the XML prefix a document binds to the format's own namespace URI, `x` by convention; the first binding in a document is the one that counts.
- **Gate**: a command a change must pass (build, test, format check), run through `./run.ps1 <verb>` and `tools/gate.ps1`, which log to `.tmp/`; the Gates section of the `godot-xaml-nextup` skill lists them and which hook runs each.
- **Godot property order**: the order Godot lists a section's properties in, which the way back restores (`PropertyOrder`).
- **GodotSignal**: the `<x:Connect>` attribute holding a signal's Godot name verbatim, used where `Signal` (the C# name) would read back as another signal, as a C# script's `Finished` would as the engine's `finished`.
- **Hover docs file**: `metadata/docs/godot-<major>.<minor>.json`, Godot's class reference converted to Markdown by `tools/class-docs/` for the language server's hover; kept outside the metadata embed glob.
- **Kept key**: an `x:Resources` key a save carries over from the previous document to the sub_resource that pairs with its entry, so a save changes only what changed (`PreviousKeys`).
- **Key order**: the order Godot writes a dictionary's entries in: string keys by code point, other keys by Variant type then value; keys Godot cannot order keep their source order. `GodotKeyOrder`.
- **Language server**: `src/GodotXaml.LanguageServer`, the .NET process that speaks the Language Server Protocol over stdin/stdout for the editor extensions; it serves diagnostics, formatting, hover and completions, and ships as a native executable per platform (`godot-xaml-ls-<version>-<rid>.zip` on each release).
- **Left file**: a file `convert --project` keeps as text because Godot loads it before any addon (`StartupFiles`).
- **`macos-signing` environment**: the GitHub environment that alone holds the `MACOS_*` signing secrets; only `main` and `v*` tags may deploy to it, which relies on the ruleset "Release tags: owner only" (only the admin creates, updates or deletes a `v*` tag). Only the `sign-macos` job of `build-server.yml` names it.
- **Metadata dump**: the tool in `tools/metadata-dump/` that walks `ClassDB` in a headless Godot run and writes the metadata file.
- **Metadata file**: `metadata/godot-<major>.<minor>.json`, the per-Godot-version file of every class's own properties (Godot name, type, enum or flags type, hint, default), the defaults it changes for inherited properties (`defaultOverrides`), enums and, for `Control` and `Window` classes, the theme-override items (name, type, hint), dumped from `ClassDB`, so tools outside Godot know what the backing integers mean. C# names are computed by the core, not stored.
- **Metadata loader**: `GodotMetadata` in `src/GodotXaml.Core/Metadata/`, which reads the metadata file into records and looks up classes, properties, enums and signals with their C# names.
- **Minted id**: a new ext-resource or sub-resource id the core makes from a caller's seed, in Godot's id form (`TscnDocument.NewExtResourceId`, `NewSubResourceId`).
- **NativeAOT**: .NET's ahead-of-time compilation to a native executable with no runtime to install; the language server ships this way, one build per platform, since NativeAOT does not cross-compile across operating systems.
- **NeedsProp**: the result (`XscnNeedsProp`) of writing a value that does not fit its type's compact form; the value is then written as an `<x:Prop>` element with Godot value text.
- **Notarisation**: Apple's scan of a signed macOS executable, which leaves a ticket Gatekeeper finds online; a bare executable cannot be stapled, so a Mac's first launch of the language server checks it online (`tools/sign-macos.ps1 -Step Notarise`).
- **Preview pane**: the Visual Studio tool window that shows the scene being edited, rendered by Godot.
- **Previous document**: the file a save replaces, parsed before the save (`ConversionOptions.Previous`); its keys and comments carry onto the new document.
- **Project root**: the folder holding the nearest `project.godot` above a file; `res://` paths are relative to it, and `convert` rewrites references only within one project root (`ProjectRoot`).
- **Property element**: an element named `<Owner.Property>` that holds one inline resource as a property's value (`<Button.Theme><Theme … /></Button.Theme>`), where `Owner` is the parent element's own tag.
- **Rank**: the sort key `PropertyOrder` gives a property name (tier, declaring class, list position, then entry, key, sub-entry, sub-key, layer, leaf for a dynamic family); a rank may read a sibling property of the same section (`RankContext`), as `ByMode` does.
- **Remap**: a `.remap` file Godot adds beside a path in an export, naming the file to load instead; the export plugin uses it to send an `.xscn`/`.xres` path to its binary copy.
- **`res://` path**: a path from a project root, written with each folder and file name in its on-disk casing.
- **Round trip**: converting a `.tscn` to `.xscn` and back (or the reverse) and getting the same scene as Godot loads it.
- **Scene comparison**: `godot/compare.ps1`, which loads each scene fixture and its converted `.xscn`, and each `.tres` fixture and its converted `.xres`, in headless Godot and fails unless a canonical dump of both is identical; the Godot half of the round-trip proof.
- **Scene lookup**: the host's reader of another scene file's text (`ISceneFiles`), which the way back uses to learn the class of an instanced node or an override, and to read a shader's uniforms or a mesh's blend shapes from an external file.
- **Scene namespace**: an XML namespace on the root binding a prefix to a `res://` folder (`xmlns:c="res://ui/"`), so a scene in that folder is instanced as a tag (`<c:Modal Name="Confirm" />`).
- **Seed scene**: the one-node scene `tools/scene-fixtures/` saves first so the inherited-scene case can be saved from it; a script cannot set up scene inheritance directly.
- **Sidecar**: the `.uid` file beside a file that has no header to hold its uid (`main.xscn.uid`); `convert` moves a uid between a `.tscn` header and it.
- **Site key**: where a sub-resource is used in an `.xscn` (its element path and property, nested), from which the way back mints its id, so ids do not shift when the file is edited elsewhere.
- **Source orders**: `SourceOrders`, the per-conversion cache of the orders a `BySource` family reads from a shader's uniforms or a mesh's blend shapes, each file read once.
- **Staging folder**: `<root>/.godot-xaml-staging/<stamp>-<8 hex>/`, where `convert --project` writes every output first; it is removed when the run ends, and a failed run leaves the project as it was.
- **Sub-entry**: a numbered list inside one entry of a dynamic family (`settings/N/joints/M/` on a bone-chain setting), ranked at its list key's place among its outer entry's keys; `PropertyOrder` ranks a name by entry, key, sub-entry, sub-key, then the two deeper levels a `Ranked` family may use, layer and leaf (a TileData's occlusion, physics or navigation layer, and the number or property inside it).
- **Takeover**: the addon saver's step on a save with `ReplaceSubresourcePaths` to a `res://` path, giving each built-in sub-resource the id and path `<file>::<id>` the next load mints.
- **Tolerant parse**: a parse that always returns a document with its diagnostics and never throws on bad text (`XscnParser.Parse`).
- **Value grammar**: Godot's text syntax for a property value, such as `Vector2(0, 0)`, shared by `.tscn`, `.tres` and the metadata defaults.
- **`x:Connect`**: the element on an emitter for a signal connection that is not the common case (another receiver, flags, binds or unbinds).
- **`x:Instance`**: the element instancing a scene by its `res://` path, used where no scene namespace tag reads back as the same file; at the root, the scene inherits that one.
- **`x:Override`**: the element, a direct child of an instance, that holds the changes this scene makes to one node inside it, named by `Path` relative to the instance.
- **`x:Prop`**: the element setting a property by its Godot name with Godot value text, for a property that cannot be a plain attribute.
- **`.xres`**: a Godot resource (a theme, a StyleBox) in the XAML format; the counterpart of `.tres`.
- **`.xscn`**: a Godot scene in the XAML format; the counterpart of `.tscn`.
