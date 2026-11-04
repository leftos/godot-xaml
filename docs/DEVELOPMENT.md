# Development

How to build, test and check godot-xaml on a development machine, from the repo root. Installing godot-xaml in a game project is in [`../README.md`](../README.md); terms used in a project sense are in the glossary in [`README.md`](README.md).

## Toolchain

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.401 (`global.json`, `rollForward: latestFeature`) | `global.json` sets the test runner to Microsoft.Testing.Platform, so `run.ps1 test` runs `dotnet test --solution GodotXaml.slnx` |
| Godot | 4.7.2 .NET, the console build on Windows (`Godot_console.exe`) | the GUI build loses Godot's output and exit code; the addon project builds on `Godot.NET.Sdk/4.7.2`. Which script reads which variable is under [The Godot binary](#the-godot-binary) |
| Godot source | 4.7.2 | `F:\Godot\src-4.7.2`: engine behaviour is cited from it. `F:\Godot\repo` is a stale 4.5.1 checkout; never cite it |
| CSharpier | 1.3.0, a local tool (`.config/dotnet-tools.json`) | `dotnet tool restore`; `run.ps1 format-check` and `format` restore it first |
| gdformat, gdlint | gdtoolkit, on `PATH` | the pre-commit hooks for the GDScript under `tools/` |
| uv | any | runs `tools/linelength.py` for the C# line-length hook |
| prek | any | `prek install` once per clone installs the pre-commit and pre-push hooks |
| gate | the installed `gate.exe` | `tools/gate.ps1` hands every word to it; on a machine without one it prints `gate: no gate.exe found; running the command without slots or watchdog` and runs the command directly, with no log, slot or ceiling |

### The Godot binary

| Script | Godot it runs |
|---|---|
| `godot/checks.ps1`, `godot/compare.ps1`, `godot/export.ps1`, `godot/editor-checks.ps1` | `$env:GODOT_PATH`, else `$env:GODOT_BIN`; with neither set it stops with `GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary` |
| `tools/metadata-dump/dump.ps1`, `tools/value-fixtures/generate.ps1`, `tools/scene-fixtures/generate.ps1` | `-Godot`, else `$env:GODOT_BIN`; they never read `GODOT_PATH` |

## run.ps1

`run.ps1` builds, tests and format-checks the solution. Every verb but `help` runs its command through `tools/gate.ps1` in a pwsh of its own, with the log under `.tmp/`, and the script exits with the gated command's status.

Call it bare, `./run.ps1 <verb>` or `pwsh -NoProfile -File run.ps1 <verb>`, never wrapped in another gate call: wrapped, an outer slot holds the machine for the whole run while the inner gates do the work.

| Verb | Runs | Log | Slot, ceiling |
|---|---|---|---|
| `help` | prints the usage; the verb a run without one uses | none | none |
| `build` | `dotnet build GodotXaml.slnx -c Release -warnaserror` | `.tmp/build.log` | heavy, 600 s |
| `godot-build` | `dotnet build godot/GodotXaml.Addon.csproj -c Debug -warnaserror`, the addon project the solution leaves out because its build needs the Godot SDK | `.tmp/godot-build.log` | heavy, 600 s |
| `test` | `build`, then `dotnet test --solution GodotXaml.slnx -c Release --no-build`, so the runner never builds a configuration of its own | `.tmp/build.log`, then `.tmp/test.log` | heavy, 600 s each |
| `format-check` | `dotnet tool restore`, then `dotnet csharpier check .`, changing nothing | `.tmp/format.log` | light, 300 s |
| `format` | `dotnet tool restore`, then `dotnet csharpier format .` | `.tmp/format.log` | light, 300 s |

To run one test class after a build, go through the gate: `tools/gate.ps1 run` with `dotnet test --project tests/GodotXaml.Core.Tests/GodotXaml.Core.Tests.csproj -c Release --no-build --filter-class "<namespace>.<Class>"`.

## Hooks

`.pre-commit-config.yaml` installs both hook types (`prek install`); `prek run --all-files` runs the pre-commit hooks over the whole repo.

- **pre-commit**, scoped to the staged files:
  - `mixed-line-ending --fix=lf` (not `.bat`/`.cmd`), `trailing-whitespace` (not `.md`), `end-of-file-fixer`, and `check-added-large-files` raised to 4096 KB for the metadata file. `tests/fixtures/` is left out of the whitespace and line-ending hooks, since it holds Godot's exact bytes.
  - The fast docs-lint on `.md` files (`tools/docs-lint.ps1`), which says so on a machine without the tool and passes.
  - `dotnet csharpier check` on the staged C# files, and the 150-character line check (`tools/linelength.py`), which catches the comments CSharpier does not wrap.
  - `gdformat --check` and `gdlint` on the GDScript under `tools/metadata-dump/`, `tools/value-fixtures/`, `tools/scene-fixtures/`, `tools/export-probe/` and `tools/editor-checks/`.
  - The Agent Mail lease guard (`tools/agent-mail-guard.ps1`).
- **pre-push**: `run.ps1 build`, then `run.ps1 godot-build`, when the push touches a `.cs`, `.csproj`, `.props`, `.slnx` or `.editorconfig` file or `global.json`.
- **By hand**: no hook runs a test suite, so `run.ps1 test` runs by hand, and so does `run.ps1 format-check` for the whole repo.

## Godot-side harnesses

Each script builds the `godot/` project as a full rebuild (`--no-incremental`, so a source restored with an older write time is rebuilt), imports it headless, which registers the addon's global classes, then imports it again, then runs its scene, export or editor check, and exits 0 only when every step exited 0, no streamed Godot call printed a line starting `Fatal error.`, nothing failed and every expected line passed. `checks.ps1`, `compare.ps1`, `export.ps1` and `editor-checks.ps1` share `godot/Harness.psm1`: `Invoke-Step` and `Invoke-GodotStreaming`, which writes each Godot line as it is emitted, prints `STEP <name>` and `STEP <name> exit <code>` around it, and prints `FATAL <name> <line>` for a `Fatal error.` line (`checks.ps1` and `compare.ps1` fail the run on one, `export.ps1` fails the export or probe whose call printed it; the import and reimport steps are not checked yet) (`editor-checks.ps1` keeps its own process handling for the editor). Each runs through the gate with `--no-markers`, as its help gives it; for the checks, because the expected load failure prints Godot's `ERROR:` line.

### Headless addon checks

`godot/checks.ps1` runs `res://checks/Checks.tscn` headless (`godot/checks/Checks.cs`), which prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` per check and `DONE <passed>/<total>`.

```
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-checks.log --slot light --timeout 1500 -- pwsh -NoProfile -File godot/checks.ps1
```

`--stall 600` because a run starved by outside load can go two minutes without enough CPU for the gate to count it as progress. The streaming means the gate's log shows the `CHECK` lines live. `pwsh -NoProfile -File godot/test-checks.ps1 [-HangGuardSeconds <n>]` tests that, and the same for `compare.ps1` and `export.ps1`, against a stub Godot, running each case from a throwaway copy of the project so the real `godot/cases/` and `.tmp/export/` stay untouched (a real compare or export run in the same checkout during the test fails a case; its gated command is in `docs/ARCHITECTURE.md`, Test locations).

When the gate reports STALLED:

- It writes `<log>.stall.txt` beside the log, with every thread's state: `Ready` means starved, `Waiting UserRequest` means an idle wait. It is the only record that tells the two apart, so copy it into the issue before the worktree is removed.
- To place a stalled Godot, read its CPU from the gate journal's `children` (`%LOCALAPPDATA%\gate\v1\events.jsonl`) and compare it with the idle milestones of the current checks scene: about 0.47 s of CPU at `CHECK C1`, about 1.84 s at C68, about 1.92 s at `DONE`.
- To starve a short-lived Godot with the suspend rig, freeze `pwsh` too (`-Freeze pwsh,Godot,Godot_console`), so `Godot_console` and `Godot` are frozen from birth.
- `compare.ps1` and `export.ps1` print each of Godot's lines as it arrives, like `checks.ps1`, so their gate logs show them live.

A Godot disposable left to the finalizer can crash the process at exit: the run ends with 0xC0000005 or 0xC000001D, or even exit 0 with a `Fatal error.` line and a `*.Finalize()` stack on stderr; the harnesses fail a run on that line whatever the exit code. Counting the entries `Godot.DisposablesTracker.OtherInstances` gains across an operation, once a GC finds them unreachable, measures what it leaves to the finalizer, as C72 does.

The checks also run on Linux under WSL, which proves the Unix temp folder rule. Use Godot 4.7.2 .NET for Linux x86_64 from the official release and a Linux SDK matching the feature band in `global.json`, or a scratch copy's `global.json` lowered to the installed SDK. Copy the tree into the Linux file system first: git in WSL cannot read a worktree's Windows `.git` path.

### Scene and resource comparison

`godot/compare.ps1` copies each scene and `.tres` fixture and its converted `.xscn` or `.xres` golden into `godot/cases/` (recreated each run, gitignored), runs `res://compare/Compare.tscn` headless and prints `COMPARE <name> PASS|FAIL` per fixture and `DONE <passed>/<total>`. Each side's dump is left beside the case as `<name>.tscn.dump` and `<name>.xscn.dump`, or `<name>.tres.dump` and `<name>.xres.dump`.

```
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-compare.log --slot light --timeout 1500 -- pwsh -NoProfile -File godot/compare.ps1
```

### Export proof

`godot/export.ps1` exports each preset of `godot/export_presets.cfg` to `.tmp/export/`, then runs `tools/export-probe/`, a GDScript-only project with no addon, against the packs, and prints `EXPORT <id> PASS|FAIL`, `PROBE <id> PASS|FAIL` and `DONE <passed>/<total>`. Godot's headless `--export-pack` exits 0 when an export plugin reports an error, so an `ERROR: godot-xaml:` line is what marks an export as failed.

```
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-export.log --slot heavy --timeout 1800 -- pwsh -NoProfile -File godot/export.ps1
```

### Editor checks

`godot/editor-checks.ps1` copies `godot/`, `src/` and `metadata/` into `.tmp/editor-checks/run/` with the checks plugin and fixtures of `tools/editor-checks/`, so the tracked files stay untouched, then starts a windowed editor twice (`--xscn-editor-checks=run`, then `reopen`).

The plugin writes its results to `.tmp/editor-checks/results/results.txt`, with a screenshot per check, and the script prints `EDITOR-CHECKS passed <p>, failed <f>, expected <n>`. The script also echoes each editor's error output live and fails the run on any engine line starting with `ERROR: `, `SCRIPT ERROR: ` or `SHADER ERROR: ` (case-sensitive; warnings stay informational), even when every check passed and the editor exited 0: after that editor's exit line it prints `STEP editor-<mode> engine errors: <n>` and one `ENGINE-ERROR editor-<mode> <kind> <line>` per case, `<kind>` being `ERROR`, `SCRIPT-ERROR` or `SHADER-ERROR`. It scans rather than the plugin logging, because the errors at the editor's exit come after the plugin's `DONE`. Run it after a change to the addon's editor behaviour.

Two traps in `tools/editor-checks/addons/editor_checks/editor_checks.gd`. A wait on `EditorFileSystem.is_scanning()` going false ends before the main thread applies a full scan after the first one (`is_scanning()` is false in the frame whose node pass applies it), so wait one more frame; the first scan and a changes scan stay true until after the apply. And a helper there must not reuse an `EditorPlugin` virtual's name (`_clear`, `_edit`, `_handles`): a mismatched signature fails the plugin's parse ("The function signature doesn't match the parent"), and a matching one parses and silently overrides the virtual.

```
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --log .tmp/editor-checks.log --slot heavy --timeout 1200 -- pwsh -NoProfile -File godot/editor-checks.ps1
```

The script opens editor windows, so the gate routes it to a builder's hidden desktop. `.gate/desktop.toml` holds the hidden-desktop pattern, and the same exact pattern must be under `repos` and `desktop` in this PC's gate `remote.json` (`gate remote add`), or the run is not routed. A routed run makes `.tmp/editor-checks/` on the builder.

## The metadata file

`metadata/godot-<major>.<minor>.json` is regenerated on each new Godot minor version, with that version's console binary; a patch release keeps the API, so the file for its minor version stays. The format and what the dump reads are in [`../tools/metadata-dump/README.md`](../tools/metadata-dump/README.md).

```
pwsh -NoProfile -File tools/metadata-dump/dump.ps1 -Godot <path to a Godot 4.7 binary>
```

`-Godot` falls back to `$env:GODOT_BIN`, and `-OutDir` writes somewhere other than `metadata/`. The extension API file goes to `.tmp/metadata-dump/`, and the script exits with Godot's exit code. The run fails when Godot logs any error while it reads ClassDB, so a new error cannot hide; the one harmless error `OpenXRRenderModelManager` logs when ClassDB builds it is muted for that class alone.

Check the new file with the dump folder's `check.gd`, where `godot` is the Godot binary; it prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` per known fact and exits 1 when any failed:

```
godot --headless --path tools/metadata-dump --script res://check.gd -- --file <absolute path to the JSON>
```

A Godot bump also regenerates `src/GodotXaml.Core/Names/GodotCaseTables.g.cs` with `tools/case-tables/generate.ps1` ([`../tools/case-tables/README.md`](../tools/case-tables/README.md)).

## Language server executables

The language server ships as a NativeAOT executable per runtime identifier: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64 and osx-arm64. NativeAOT does not cross-compile across operating systems, so a Windows machine publishes only the `win-` ones. It needs the platform's native toolchain: the Visual Studio C++ tools on Windows, and clang and zlib on Linux.

```
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/publish.log --slot heavy --timeout 1500 -- pwsh -NoProfile -File tools/publish-server.ps1 -Rid win-x64
```

`tools/publish-server.ps1 -Rid <rid> [-Out <folder>]` publishes into `.tmp/publish/<rid>/` and fails on any publish warning. `-Out` must be a folder under `.tmp/publish/`; the script refuses any other. When the identifier is this machine's, it runs the smoke test on the executable first. It then writes `godot-xaml-ls-<version>-<rid>.zip` beside the publish folder, holding the executable alone, with the version from `VersionPrefix` in `Directory.Build.props`.

For another identifier it says the zip was not smoke-tested. `-HostRid <rid>` names the identifier to take as this machine's, which is how a test makes it smoke or skip.

A release's six zips are built by `.github/workflows/build-server.yml`, not on one machine: see the Language server entry of [`ARCHITECTURE.md`](ARCHITECTURE.md) for its jobs and the macOS signing. Its zips are unsigned from `publish-server.ps1`; the macOS ones that ship are the signed and notarised copies, rezipped with the executable at the zip's root. `gh workflow run build-server.yml --ref main` (no tag) is a dry run that builds, signs and smoke-tests all six and attaches nothing.

`tools/smoke-server.ps1 -Exe <path> [-TimeoutSeconds <n>]` launches an executable and drives one session over stdio: `initialize` (name, version, capabilities), `initialized` and the server's "started" log, a `$/cancelRequest`, a `didOpen` whose `publishDiagnostics` must carry a GX code under camelCase keys, a hover on a class tag with its description, a completion with items, then `shutdown` and `exit`. The process must exit 0 and write nothing to stdout but framed messages.

Its stderr must hold no exception text and no `godot-xaml.jsonrpc` warning or error. Every read is bounded, and the process is killed on a failure.

A change that adds a message type adds it to `Protocol/LspJsonContext.cs`. A change that adds an RPC method declares it on `ILanguageServerTarget`, whose generated shape is what registers it.

## Releases

The release procedure is the `prepare-release` skill, [`../.claude/skills/prepare-release/SKILL.md`](../.claude/skills/prepare-release/SKILL.md), invoked as `/prepare-release`. Its pack step runs `tools/pack-release.ps1 -Version <v>`, which builds the two NuGet packages and the addon zip under `.tmp/release/<v>/`; without `-Local` it refuses a version whose tag is on `origin` or which nuget.org lists, and with `-Local` it packs `<v>-local.<UTC stamp>` for trying an install on this machine, its zip's `plugin.cfg` naming that version, and asks neither. `-CheckOnly` runs the version-home and published checks and packs nothing.
