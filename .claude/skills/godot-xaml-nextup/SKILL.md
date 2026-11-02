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

None yet: the first C# project adds its build, test and format commands here, and the matching prek hooks, in the same commit.
