# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# godot-xaml

XAML-style `.xscn` scenes and `.xres` resources for Godot 4.7+ .NET, which the Godot editor opens, edits and saves through a C# addon, with a language server and a live preview for Visual Studio and VS Code. This file is a router: each line names the document that owns a question.

## Start here

- `docs/README.md`: the map and the glossary.
- The plan lives in Linear: every task is a Linear issue in team GXAML, grouped into projects worked in order (the `godot-xaml-nextup` profile names the order). `docs/plans/MAIN.md` is a generated snapshot of it, never edited by hand.

  The operations (**add**, **land**, **triage** and the rest) are in `~/.claude/docs/plan-operations.md`. A design for open work stays in `docs/plans/<name>.md`, linked from its issue; once its last step lands it moves to `docs/plans/archive/`, after its rulings are in `docs/DECISIONS.md`.
- `docs/DECISIONS.md`: the owner's decisions and the Godot source facts behind them; read it before reversing one.
- `docs/plans/v1-design.md`: version 1's components, order of work and open questions.
- `README.md`: the user-facing page; a change to what the project does or how it installs updates it in the same commit.

## Architecture

- `docs/ARCHITECTURE.md`: the components and where to start for a change. Read it before exploring the code; a new component or a change to a layer's boundary updates it in the same commit.

## Non-negotiables

- Behaviour of Godot is cited from its 4.7.2 source or docs, never assumed: the source is `F:\Godot\src-4.7.2`; `F:\Godot\repo` is a stale 4.5.1 checkout.
- The core (the format, its parser and writer, the `.tscn` conversion) references no Godot assembly; the addon, the CLI and the language server are hosts over it.
- A conversion is proven by a round trip on real scenes: `.tscn` to `.xscn` and back loads as the same scene in Godot.
- Commit messages go in a file (`git commit -F .tmp/msg-<slug>.txt -- <paths>`, a file new to each commit, written in an earlier turn than the commit); enumerate files, never `git add -A`; never `--amend` or `--no-verify`.
- Temporary files go in the untracked `.tmp/`.

## Workflow

- Source and test edits go to the `implementer` agent with a brief naming the files, the change and a proving command; the main session owns docs, plans and commits.
- The repo is public at `github.com/leftos/godot-xaml`; a landed item is committed on `main` and pushed at the push points of the `orchestrator` skill.
- godot-mcp (`X:\dev\godot-mcp`) is a partner project: whenever this work could use a godot-mcp tool, or a change to one (`.xscn` in `preview_scene`, scene inspection or editing over `.xscn`), the need goes to godot-mcp's repo inbox as a request, through the `cross-repo-coordination` skill, and the matching issue here names it. Its agents build the tool; this repo never copies or forks godot-mcp code.
