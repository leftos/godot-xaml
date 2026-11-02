#requires -Version 7
<#
.SYNOPSIS
Writes tests/fixtures/scenes/ from a Godot 4.7 binary.

.DESCRIPTION
Runs generate.gd headless over this folder's project, which builds each scene and resource case
in code, saves it with Godot's text saver and copies the saved bytes to the output folder.
Exits with Godot's exit code.

.PARAMETER Godot
Path to a Godot 4.7 binary (the console build on Windows). Falls back to $env:GODOT_BIN.

.PARAMETER OutDir
Folder the fixtures are written to. Defaults to the repo's tests/fixtures/scenes/ folder.

.EXAMPLE
pwsh -NoProfile -File tools/scene-fixtures/generate.ps1 -Godot F:/Godot/Godot_console.exe
#>
[CmdletBinding()]
param(
    [string]$Godot,
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
if (-not $Godot) { $Godot = $env:GODOT_BIN }
if (-not $Godot) { throw 'generate.ps1: pass -Godot <path to a Godot 4.7 binary> or set GODOT_BIN' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'tests' 'fixtures' 'scenes' }
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$null = New-Item -ItemType Directory -Force -Path $OutDir

& $Godot --headless --path (Join-Path $repo 'tools' 'scene-fixtures') --script res://generate.gd -- --out-dir $OutDir
exit $LASTEXITCODE
