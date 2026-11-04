#requires -Version 7
<#
.SYNOPSIS
Runs the addon's headless checks in Godot 4.7.

.DESCRIPTION
Builds the godot/ project as a full rebuild (--no-incremental), imports it headless (which registers the addon's global
classes), then imports it again, then runs res://checks/Checks.tscn headless. The scene prints CHECK <id> PASS or CHECK
<id> FAIL <detail> per check and DONE <passed>/<total>.

The scene's lines are printed as Godot writes them, so the gate's stall watchdog sees the checks progress. The
documented command passes --stall 600 because a run starved by outside load can go two minutes without enough CPU to
count as progress.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. The script exits 0 only when every step exited 0, no check failed, every
expected check passed, the scene printed DONE with all of them and Godot printed no Fatal error line.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-checks.log --slot light --timeout 1500 --
  pwsh -NoProfile -File godot/checks.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'checks.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary'
}

$project = $PSScriptRoot
$expectedChecks = 137

$built = Invoke-Step 'build' { dotnet build (Join-Path $project 'GodotXaml.Addon.csproj') -c Debug -warnaserror --no-incremental }
if (-not $built) {
    exit 1
}

$imported = Invoke-Step 'import' { & $godot --headless --path $project --import }
if (-not $imported) {
    exit 1
}

# The first scan of a fresh project runs before the C# global classes register, so .xscn and .xres files get no uid
# (docs/DECISIONS.md, "The addon registers from C#, as global classes"); the second scan has the classes.
$reimported = Invoke-Step 'reimport' { & $godot --headless --path $project --import }
if (-not $reimported) {
    exit 1
}

$run = Invoke-GodotStreaming -Godot $godot -Name checks -Arguments @(
    '--headless', '--path', $project, '--scene', 'res://checks/Checks.tscn', '--quit-after', '600')
$lines = $run.Lines
$checksExit = $run.Exit

$passes = @($lines | Where-Object { $_ -match '^CHECK \S+ PASS$' }).Count
$fails = @($lines | Where-Object { $_ -match '^CHECK \S+ FAIL' }).Count
$done = @($lines | Where-Object { $_ -eq "DONE $expectedChecks/$expectedChecks" }).Count
$allPassed = $checksExit -eq 0 -and -not $run.Fatal -and $fails -eq 0 -and $passes -eq $expectedChecks -and $done -eq 1
[Console]::Out.WriteLine("CHECKS passed $passes, failed $fails, expected $expectedChecks")
exit $(if ($allPassed) { 0 } else { 1 })
