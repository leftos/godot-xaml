# godot-xaml

XAML-style `.xscn` scenes and `.xres` resources for Godot 4.7+ .NET, which the Godot editor opens, edits and saves through a C# addon, with a language server and a live preview for Visual Studio and VS Code. This file is a router: each line names the document that owns a question.

## Start here

- `docs/README.md`: the map and the glossary.
- The plan is Linear team GXAML, worked in the order the `godot-xaml-nextup` profile names.
- `docs/DECISIONS.md`: the owner's decisions and the Godot source facts behind them; read it before reversing one.
- `docs/plans/v1-design.md`: version 1's components, order of work and open questions.
- `README.md`: the user-facing page; a change to what the project does or how it installs updates it in the same commit.

## Architecture

- `docs/ARCHITECTURE.md`: the components and where to start for a change. Read it before exploring the code; a new component or a change to a layer's boundary updates it in the same commit.

## Non-negotiables

- Behaviour of Godot is cited from its source or docs, never assumed: 4.7.2 is `F:\Godot\src-4.7.2`, 4.8 is `F:\Godot\src-4.8-beta2` (engine `F:\Godot\4.8-beta2\`, until 4.8 stable replaces both); `F:\Godot\repo` is a stale 4.5.1 checkout.
- The core (the format, its parser and writer, the `.tscn` conversion) references no Godot assembly; the addon, the CLI and the language server are hosts over it.
- A conversion is proven by a round trip on real scenes: `.tscn` to `.xscn` and back loads as the same scene in Godot.

## Workflow

- The repo is public at `github.com/leftos/godot-xaml`; a landed item is committed on `main` and pushed at the push points of the `orchestrator` skill.
- godot-mcp (`X:\dev\godot-mcp`) is a partner project: whenever this work could use a godot-mcp tool, or a change to one (`.xscn` in `preview_scene`, scene inspection or editing over `.xscn`), the need goes to godot-mcp's repo inbox as a request, through the `cross-repo-coordination` skill, and the matching issue here names it. Its agents build the tool; this repo never copies or forks godot-mcp code.
