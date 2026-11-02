# godot-xaml docs

## Map

- [`ARCHITECTURE.md`](ARCHITECTURE.md): the components, what each owns, and where to start for a change.
- [`DECISIONS.md`](DECISIONS.md): the owner's decisions and the Godot source facts behind them.
- [`plans/v1-design.md`](plans/v1-design.md): version 1's components, order of work and open questions.
- `plans/MAIN.md`: a snapshot of the Linear plan (team GXAML), generated, never edited by hand.

## Glossary

- **Canonical format**: the one layout every save writes, whatever the file looked like before, so a diff shows only real changes.
- **Compat mode**: the writer flag Godot uses to write a file as format 3: byte arrays as number lists and negative infinity as `inf_neg`; off, it writes base64 and `-inf`. `ValueWriteOptions.Compat`, on by default.
- **Fixture generator**: `tools/value-fixtures/`, the headless Godot script that writes the expected value text under `tests/fixtures/values/`.
- **Gate**: a command a change must pass (build, test, format check), run through `./run.ps1 <verb>` and `tools/gate.ps1`, which log to `.tmp/`; the Gates section of the `godot-xaml-nextup` skill lists them and which hook runs each.
- **Metadata dump**: the tool in `tools/metadata-dump/` that walks `ClassDB` in a headless Godot run and writes the metadata file.
- **Metadata file**: `metadata/godot-<major>.<minor>.json`, the per-Godot-version file of every class's own properties (Godot name, type, enum or flags type, hint, default), enums and, for `Control` and `Window` classes, the theme-override items (name, type, hint), dumped from `ClassDB`, so tools outside Godot know what the backing integers mean. C# names are computed by the core, not stored.
- **Preview pane**: the Visual Studio tool window that shows the scene being edited, rendered by Godot.
- **Round trip**: converting a `.tscn` to `.xscn` and back (or the reverse) and getting the same scene as Godot loads it.
- **Value grammar**: Godot's text syntax for a property value, such as `Vector2(0, 0)`, shared by `.tscn`, `.tres` and the metadata defaults.
- **`.xres`**: a Godot resource (a theme, a StyleBox) in the XAML format; the counterpart of `.tres`.
- **`.xscn`**: a Godot scene in the XAML format; the counterpart of `.tscn`.
