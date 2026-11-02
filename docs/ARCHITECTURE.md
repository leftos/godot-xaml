# godot-xaml — architecture

godot-xaml lets a Godot 4.7+ .NET project keep its scenes and resources as XAML-style `.xscn` and `.xres` files that Godot opens, edits and saves directly. There is no code yet: the planned components and their order are in [`plans/v1-design.md`](plans/v1-design.md), and the settled choices in [`DECISIONS.md`](DECISIONS.md). Terms used in a project sense are in the glossary in [`README.md`](README.md).

The rule that shapes it: one C# core, with no Godot dependency, owns the format; the Godot addon, the CLI and the language server are thin hosts over it.

## Task Index

| Task | Files, in order | Deep doc |
|---|---|---|
| Change a format rule | [`DECISIONS.md`](DECISIONS.md) → [`plans/v1-design.md`](plans/v1-design.md) | — |

## Layers

None yet. Each component gets an entry here in the commit that adds its project.

## Integration Footguns

None yet.

## Test locations

None yet.

## Deep docs

None yet.
