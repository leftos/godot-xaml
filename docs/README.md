# godot-xaml docs

## Map

- [`ARCHITECTURE.md`](ARCHITECTURE.md): the components, what each owns, and where to start for a change.
- [`DECISIONS.md`](DECISIONS.md): the owner's decisions and the Godot source facts behind them.
- [`plans/v1-design.md`](plans/v1-design.md): version 1's components, order of work and open questions.
- `plans/MAIN.md`: a snapshot of the Linear plan (team GXAML), generated, never edited by hand.

## Glossary

- **Canonical format**: the one layout every save writes, whatever the file looked like before, so a diff shows only real changes.
- **Metadata file**: the per-Godot-version file of every class's properties (C# name, Godot name, type, enum or flags names, default), dumped from `ClassDB` by a headless Godot run, so tools outside Godot know what the backing integers mean.
- **Preview pane**: the Visual Studio tool window that shows the scene being edited, rendered by Godot.
- **Round trip**: converting a `.tscn` to `.xscn` and back (or the reverse) and getting the same scene as Godot loads it.
- **`.xres`**: a Godot resource (a theme, a StyleBox) in the XAML format; the counterpart of `.tres`.
- **`.xscn`**: a Godot scene in the XAML format; the counterpart of `.tscn`.
