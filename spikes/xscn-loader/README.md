# xscn-loader spike

A throwaway Godot 4.7 .NET project that measures whether a C# `ResourceFormatLoader` and `ResourceFormatSaver`, registered only as `[GlobalClass]` `[Tool]` classes, let Godot load, instance, run and save a `.xscn` scene. The `.xscn` here is a minimal XML of nodes (one element per node, named after its Godot class, with Godot property names as attributes holding `var_to_str` text), not the real format. The findings go to `docs/DECISIONS.md`, and this folder is deleted once the real addon exists.

## Build

From the repo root:

```
pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/build.log --slot heavy --timeout 600 -- dotnet build spikes/xscn-loader/XscnSpike.sln -c Debug
```

## Headless checks

Run from the repo root after the build, in this order. `F:/Godot/Godot_console.exe` is Godot 4.7.2 .NET.

1. Import, which fills `.godot/` and writes the `.uid` sidecars:

   ```
   pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/import.log --slot light --timeout 300 -- F:/Godot/Godot_console.exe --headless --path spikes/xscn-loader --import
   ```

2. Run `Main.xscn` as the scene, as F6 does; the log shows `XSCN MAIN READY Hello from xscn`:

   ```
   pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/run-main.log --slot light --timeout 120 -- F:/Godot/Godot_console.exe --headless --path spikes/xscn-loader --scene res://scenes/Main.xscn --quit-after 10
   ```

3. Run the checks C1 to C4 (load, instance in a `.tscn`, edit and save round trip, save reproduces the file); each prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>`, and the run exits 0 only when all pass:

   ```
   pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/check.log --slot light --timeout 120 -- F:/Godot/Godot_console.exe --headless --path spikes/xscn-loader --scene res://tests/Check.tscn
   ```

## Editor checks

The editor checks E1 to E8 run in a windowed editor, driven by the `addons/editor_checks` plugin. The plugin does nothing unless the editor was started with the user argument `--xscn-editor-checks=<mode>`, so an ordinary editor session is unaffected. A second user argument, `--out=<absolute folder>`, names where the results go.

- Mode `run`: E1 the Open and Save dialogs list `xscn`; E2 open `Main.xscn`; E3 edit and save it, still XML; E4 run it as the current scene; E5 instance it into `Host.tscn` and save; E6 change a C# file, run (which builds), stop and save `Main.xscn` again after the assembly reload; E7 rename `Main.xscn` to `Moved.xscn` with its `.uid` and rescan, and `Host.tscn` resolves it by uid (an approximation of the FileSystem dock's rename, which has no script API).
- Mode `reopen`: E8 open `Main.xscn` in a freshly started editor.

Each check prints `CHECK <id> PASS` or `CHECK <id> FAIL <detail>` and saves a screenshot of the editor; `NOTE <id>` lines say how a check measured what it did. Every wait is bounded at 60 s and fails as `timed out waiting for <what>`. The run ends with `DONE <passed>/<total>` and the editor quits with exit code 0 only when all passed.

`editor-checks.ps1` runs the lot: it builds the spike, imports it, then starts the editor in mode `run` and again in mode `reopen`. Results go to `<repo>/.tmp/editor-checks/`: `results.txt`, `<id>.png` per check, and `E3-Main.xscn`, `E5-Host.tscn` and `E6-Main.xscn`, the files as the editor saved them. The checks edit the spike's scenes and `XscnFormatSaver.cs`, so the script backs them up first, restores them at the end, and writes `RESTORE OK` only when every spike file is byte-identical to how it started.

Godot is `$env:GODOT_PATH`, else `$env:GODOT_BIN`. The editor opens windows, so run it through the gate, which routes it to the build box's desktop (`.gate/desktop.toml`), from the repo root:

```
pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/editor-checks.log --slot heavy --timeout 1200 -- pwsh -NoProfile -File spikes/xscn-loader/editor-checks.ps1
```
