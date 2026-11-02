#requires -Version 7
<#
.SYNOPSIS
Writes metadata/godot-<major>.<minor>.json from a Godot 4.7 binary.

.DESCRIPTION
Runs Godot's --dump-extension-api into <repo>/.tmp/metadata-dump/, then runs dump.gd headless over this
folder's project, which combines ClassDB with that extension API file and writes the metadata JSON.
Exits with Godot's exit code.

.PARAMETER Godot
Path to a Godot 4.7 binary (the console build on Windows). Falls back to $env:GODOT_BIN.

.PARAMETER OutDir
Folder the metadata JSON is written to. Defaults to the repo's metadata/ folder.

.EXAMPLE
pwsh -NoProfile -File tools/metadata-dump/dump.ps1 -Godot F:/Godot/Godot_console.exe
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
if (-not $Godot) { throw 'dump.ps1: pass -Godot <path to a Godot 4.7 binary> or set GODOT_BIN' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'metadata' }
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$null = New-Item -ItemType Directory -Force -Path $OutDir

$work = Join-Path $repo '.tmp' 'metadata-dump'
$null = New-Item -ItemType Directory -Force -Path $work
$api = Join-Path $work 'extension_api.json'
if (Test-Path -LiteralPath $api) { Remove-Item -LiteralPath $api }

# Godot writes extension_api.json into its working directory (main/main.cpp, generate_extension_json_file).
Push-Location -LiteralPath $work
try {
    & $Godot --headless --dump-extension-api
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) { exit $code }
if (-not (Test-Path -LiteralPath $api)) { throw "dump.ps1: $Godot --dump-extension-api wrote no $api" }

& $Godot --headless --path (Join-Path $repo 'tools' 'metadata-dump') --script res://dump.gd -- --api $api --out-dir $OutDir
exit $LASTEXITCODE
