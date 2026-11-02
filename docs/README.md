# godot-xaml docs

## Map

- [`ARCHITECTURE.md`](ARCHITECTURE.md): the components, what each owns, and where to start for a change.
- [`DECISIONS.md`](DECISIONS.md): the owner's decisions and the Godot source facts behind them.
- [`plans/v1-design.md`](plans/v1-design.md): version 1's components, order of work and open questions.
- `plans/MAIN.md`: a snapshot of the Linear plan (team GXAML), generated, never edited by hand.

## Glossary

- **Built path**: a path a script assembles at run time (`"res://ui/" + name + ".tscn"`) instead of writing whole; `convert` cannot rewrite it and warns (`GXP0006`) when a literal ends in an extension the run converts.
- **Canonical format**: the one layout every save writes, whatever the file looked like before, so a diff shows only real changes.
- **Canonical fixture**: an `.xscn` or `.xres` under `tests/fixtures/xscn/canonical/` that the writer must reproduce byte for byte.
- **Compat mode**: the writer flag Godot uses to write a file as format 3: byte arrays as number lists and negative infinity as `inf_neg`; off, it writes base64 and `-inf`. `ValueWriteOptions.Compat`, on by default.
- **Diagnostic code**: the stable `GX` plus four digits that names one kind of parse problem (`GX0001` for an XML syntax error), listed in `XscnCodes`.
- **Effective default**: a property's default for a given class, as Godot saves against it: none for a class Godot keeps no defaults for (a virtual class such as `BaseButton`); else the class's own declaration's default (none when it declares the property without one); else its override; else its parent's effective default. `GodotMetadata.FindDefault`.
- **Export plugin**: `XscnExportPlugin`, the Godot `EditorExportPlugin` that converts each `.xscn`/`.xres` to Godot binary when a project is exported; the editor plugin `XscnEditorPlugin` registers it.
- **Fixture generator**: `tools/value-fixtures/`, the headless Godot script that writes the expected value text under `tests/fixtures/values/`; `tools/scene-fixtures/` does the same for scenes and resources under `tests/fixtures/scenes/`.
- **Format pair**: an `.in.xscn`/`.out.xscn` pair under `tests/fixtures/xscn/format/`; the writer's output for the `.in` document must equal the `.out` file, which proves the canonical rewrite.
- **Gate**: a command a change must pass (build, test, format check), run through `./run.ps1 <verb>` and `tools/gate.ps1`, which log to `.tmp/`; the Gates section of the `godot-xaml-nextup` skill lists them and which hook runs each.
- **Key order**: the order Godot writes a dictionary's entries in: string keys by code point, other keys by Variant type then value; keys Godot cannot order keep their source order. `GodotKeyOrder`.
- **Metadata dump**: the tool in `tools/metadata-dump/` that walks `ClassDB` in a headless Godot run and writes the metadata file.
- **Metadata file**: `metadata/godot-<major>.<minor>.json`, the per-Godot-version file of every class's own properties (Godot name, type, enum or flags type, hint, default), the defaults it changes for inherited properties (`defaultOverrides`), enums and, for `Control` and `Window` classes, the theme-override items (name, type, hint), dumped from `ClassDB`, so tools outside Godot know what the backing integers mean. C# names are computed by the core, not stored.
- **Metadata loader**: `GodotMetadata` in `src/GodotXaml.Core/Metadata/`, which reads the metadata file into records and looks up classes, properties, enums and signals with their C# names.
- **Minted id**: a new ext-resource or sub-resource id the core makes from a caller's seed, in Godot's id form (`TscnDocument.NewExtResourceId`, `NewSubResourceId`).
- **NeedsProp**: the result (`XscnNeedsProp`) of writing a value that does not fit its type's compact form; the value is then written as an `<x:Prop>` element with Godot value text.
- **Preview pane**: the Visual Studio tool window that shows the scene being edited, rendered by Godot.
- **Project root**: the folder holding the nearest `project.godot` above a file; `res://` paths are relative to it, and `convert` rewrites references only within one project root (`ProjectRoot`).
- **`res://` path**: a path from a project root, written with each folder and file name in its on-disk casing.
- **Remap**: a `.remap` file Godot adds beside a path in an export, naming the file to load instead; the export plugin uses it to send an `.xscn`/`.xres` path to its binary copy.
- **Round trip**: converting a `.tscn` to `.xscn` and back (or the reverse) and getting the same scene as Godot loads it.
- **Scene comparison**: `godot/compare.ps1`, which loads each scene fixture and its converted `.xscn` in headless Godot and fails unless a canonical dump of both is identical; the Godot half of the round-trip proof.
- **Seed scene**: the one-node scene `tools/scene-fixtures/` saves first so the inherited-scene case can be saved from it; a script cannot set up scene inheritance directly.
- **Sidecar**: the `.uid` file beside a file that has no header to hold its uid (`main.xscn.uid`); `convert` moves a uid between a `.tscn` header and it.
- **Tolerant parse**: a parse that always returns a document with its diagnostics and never throws on bad text (`XscnParser.Parse`).
- **Value grammar**: Godot's text syntax for a property value, such as `Vector2(0, 0)`, shared by `.tscn`, `.tres` and the metadata defaults.
- **`.xres`**: a Godot resource (a theme, a StyleBox) in the XAML format; the counterpart of `.tres`.
- **`.xscn`**: a Godot scene in the XAML format; the counterpart of `.tscn`.
