# case-tables

Writes `src/GodotXaml.Core/Names/GodotCaseTables.g.cs`: Godot's case tables as C# spans, parsed from the engine's `core/string/ucaps.h`. `GodotCase` maps a code point's case with them, as Godot's `String::to_upper` and `to_lower` do, so C# names for non-ASCII letters match what Godot's generator makes.

## Run

```
pwsh -NoProfile -File tools/case-tables/generate.ps1 -Ucaps <godot source>/core/string/ucaps.h -Out src/GodotXaml.Core/Names/GodotCaseTables.g.cs
```

`generate.ps1` takes `-Ucaps` and `-Out`, and optional `-GodotVersion` and `-UnicodeVersion`, which only name the versions in the generated file's header. It prints the pair count of each table and throws when a table's count differs from `LTU_LEN` or `UTL_LEN` in the same file, or when a table's keys are not strictly ascending (the engine binary-searches them). The same `ucaps.h` always gives the same bytes. Run it on a Godot bump and commit the result; never edit the generated file.

## Output

The file declares `GodotCaseTables.UpperFrom`, `UpperTo`, `LowerFrom` and `LowerTo`, carries `ucaps.h`'s MIT notice, and is written with LF line endings. `.csharpierignore` excludes it from csharpier.
