#requires -Version 7
<#
.SYNOPSIS
Writes tests/fixtures/scenes/ from a Godot 4.7 binary, or tests/fixtures/tscn48/ from a Godot 4.8 binary with -FourEight.

.DESCRIPTION
Runs generate.gd headless over this folder's project, which builds each scene and resource case
in code, saves it with Godot's text saver and copies the saved bytes to the output folder.
Exits with Godot's exit code.

.PARAMETER Godot
Path to a Godot 4.7 binary, or a 4.8 binary with -FourEight (the console build on Windows). Falls back to $env:GODOT_BIN.

.PARAMETER OutDir
Folder the fixtures are written to. Defaults to the repo's tests/fixtures/scenes/ folder, or tests/fixtures/tscn48/ with -FourEight.

.PARAMETER FourEight
Writes the Godot 4.8 cases (a scene's own properties and embedded scenes) instead of the 4.7 ones, to tests/fixtures/tscn48/ unless -OutDir is given.

.EXAMPLE
pwsh -NoProfile -File tools/scene-fixtures/generate.ps1 -Godot F:/Godot/Godot_console.exe
#>
[CmdletBinding()]
param(
    [string]$Godot,
    [string]$OutDir,
    [switch]$FourEight
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
if (-not $Godot) { $Godot = $env:GODOT_BIN }
if (-not $Godot) { throw 'generate.ps1: pass -Godot <path to a Godot 4.7 binary (or 4.8 with -FourEight)> or set GODOT_BIN' }
$fixtures = $FourEight ? 'tscn48' : 'scenes'
if (-not $OutDir) { $OutDir = Join-Path $repo 'tests' 'fixtures' $fixtures }
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$null = New-Item -ItemType Directory -Force -Path $OutDir

$godotArgs = @('--headless', '--path', (Join-Path $repo 'tools' 'scene-fixtures'), '--script', 'res://generate.gd', '--', '--out-dir', $OutDir)
if ($FourEight) { $godotArgs += '--four-eight' }
& $Godot @godotArgs
exit $LASTEXITCODE
