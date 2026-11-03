---
name: godot-xaml-nextup
description: Profile for the user-level `nextup` skill in the godot-xaml repo — loaded by `nextup` at its step 0 for this project's plan order and gates. Not a loop of its own; invoke `/nextup`.
---

# godot-xaml profile for `nextup`

The generic loop is the user-level `nextup` skill; this file supplies only what is godot-xaml-specific.

siblings: none
linear: godot-xaml

## Plan and tracker

- The plan lives in Linear: every task is a Linear issue in team GXAML, per `~/.claude/docs/plan-operations.md`; `docs/plans/MAIN.md` is its generated snapshot, never edited by hand. Project order, which is the order the queue is worked: `Spike`, `Core format`, `Godot addon`, `Agent tooling`, `Editor support`, `Later`, `Backlog`.
- A steer or a finding the item does not fix gets an **add**, in the project whose files it shares, else in `Backlog`.
- Tracker: **triage** as plan-operations says, each untriaged issue placed in the project that shares its files, else in `Backlog`.
- A design for open work stays in `docs/plans/<name>.md`, linked from its issue. Once its last step lands, its rulings go into `docs/DECISIONS.md` and the file moves to `docs/plans/archive/`.

## Gates

`./run.ps1 <verb>` runs each command through `tools/gate.ps1`, with a log in `.tmp/`, so it is always called bare: wrapped in another gate call, an outer slot holds the machine for the whole run while the inner gates do the work.

- `build`: `dotnet build GodotXaml.slnx -c Release -warnaserror`.
- `godot-build`: `dotnet build godot/GodotXaml.Addon.csproj -c Debug -warnaserror`, the Godot addon project the solution leaves out (its build needs the Godot SDK, whose configurations are Debug, ExportDebug and ExportRelease).
- `test`: the build, then `dotnet test --solution GodotXaml.slnx -c Release --no-build`.
- `format-check`: `dotnet csharpier check .`.
- `format`: `dotnet csharpier format .`.

Where each runs (`.pre-commit-config.yaml`):

- pre-commit: `csharpier check` on the staged C# files, the 150-character line check (`tools/linelength.py`), and gdformat and gdlint on the metadata dump tool's GDScript.
- pre-push: `run.ps1 build`, then `run.ps1 godot-build`.
- By hand: `run.ps1 test`, which no hook runs, and `run.ps1 format-check` for the whole repo.
- By hand, after a change to the addon's editor behaviour: the editor checks, `godot/editor-checks.ps1` (command in `docs/ARCHITECTURE.md`, Test locations), routed to a builder's hidden desktop.
