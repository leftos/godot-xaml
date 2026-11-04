#requires -Version 7
<#
.SYNOPSIS
Exports the godot/ project headless and checks that its .xscn and .xres ship as Godot binary.

.DESCRIPTION
Builds the godot/ project as a full rebuild (--no-incremental), imports it headless (which registers the addon's global
classes and enables its editor plugin), then imports it again, then exports each preset of godot/export_presets.cfg to
.tmp/export/ and checks it:

  EXPORT all           "Windows Desktop" (all resources): no godot-xaml error. Then PROBE P1..P8 on its pack.
  EXPORT selected      "Windows Selected" (selected scenes, no include filter): the loader-drop error naming the
                       include filter.
  EXPORT exclude       "Windows Exclude" (all resources, exclude filter naming XresFormatLoader.cs): the loader-drop
                       error naming the exclude filter.
  EXPORT resources     "Windows Resources" (selected resources, the loaders' .cs ticked): no godot-xaml error.
  EXPORT broken        "Windows Broken" (selected resources: broken.xscn, notxml.xres): an error naming each file and
                       no loader-drop error. Then PROBE B1..B2: neither file nor its .remap is in the pack.
  EXPORT dependencies  "Windows Dependencies" (selected scenes: parent.xscn, include filter set): no godot-xaml error.
                       Then PROBE D1..D2: parent.xscn and its dependency child.xscn are remapped to binary in the pack.
  EXPORT customized    "Windows Customized" (customized, addons/godot_xaml/ removed): the loader-drop error naming
                       the include filter.

The PROBE lines come from tools/export-probe, a GDScript-only project with no addon, run against the pack.

A godot-xaml export error is a message, not an exit status: Godot's headless --export-pack exits 0 when an export
plugin adds an error message (editor/editor_node.cpp, the export_defer branch), so an "ERROR: godot-xaml:" line is what
marks an export as failed.

Godot's lines are printed as Godot writes them, so the gate's stall watchdog sees the run progress. The documented
command passes --stall 600 because a run starved by outside load can go two minutes without enough CPU to count as
progress.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. Prints EXPORT <id> PASS|FAIL <detail> and PROBE <id> PASS|FAIL <detail>
lines and DONE <passed>/<total>; exits 0 only when every expected line passed.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-export.log --slot heavy --timeout 1800 `
    -- pwsh -NoProfile -File godot/export.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'export.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary'
}

$project = $PSScriptRoot
$repo = Split-Path -Parent $project
$probe = Join-Path $repo 'tools/export-probe'
$exportDir = Join-Path $repo '.tmp/export'
$sceneUid = (Get-Content -LiteralPath (Join-Path $project 'checks/cases/scene.xscn.uid') -TotalCount 1).Trim()
$pluginError = 'ERROR: godot-xaml:'
$dropError = 'the export would drop the godot-xaml loaders'
$includeAdvice = "add addons/godot_xaml/*.cs to the preset's include filter"
$excludeAdvice = "from the preset's exclude filter"

$results = [System.Collections.Generic.List[string]]::new()
$expected = [System.Collections.Generic.List[string]]::new()
function Write-Result {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Line)
    $expected.Add($Id)
    $results.Add($Line)
    [Console]::Out.WriteLine($Line)
}

# Exports one preset to .tmp/export/<id>.pck; returns the pack path, the godot-xaml error lines and the exit status.
function Invoke-Export {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Preset)
    $pack = Join-Path $exportDir "$Id.pck"
    if (Test-Path -LiteralPath $pack) {
        Remove-Item -LiteralPath $pack
    }
    $run = Invoke-GodotStreaming -Godot $godot -Name "export $Id" -Arguments @(
        '--headless', '--path', $project, '--export-pack', $Preset, $pack)
    $errors = @($run.Lines | Where-Object { $_.Contains($pluginError) })
    return [pscustomobject]@{ Id = $Id; Pack = $pack; Errors = $errors; Exit = $run.Exit }
}

# An export expected to succeed: Godot exits 0, prints no godot-xaml error and writes the pack.
function Test-CleanExport {
    param([Parameter(Mandatory)] $Export)
    $id = "EXPORT $($Export.Id)"
    $line = if ($Export.Exit -ne 0) { "$id FAIL godot exited $($Export.Exit)" }
    elseif ($Export.Errors.Count -gt 0) { "$id FAIL $($Export.Errors[0])" }
    elseif (-not (Test-Path -LiteralPath $Export.Pack)) { "$id FAIL no pack at $($Export.Pack)" }
    else { "$id PASS" }
    Write-Result $id $line
}

# An export expected to report errors: every needle is in some godot-xaml error, and no error holds a forbidden text.
function Test-FailedExport {
    param([Parameter(Mandatory)] $Export, [Parameter(Mandatory)][string[]] $Needles, [string[]] $Forbidden = @())
    $id = "EXPORT $($Export.Id)"
    $missing = @($Needles | Where-Object { $needle = $_; -not ($Export.Errors | Where-Object { $_.Contains($needle) }) })
    $unwanted = @($Export.Errors | Where-Object { $errorLine = $_; $Forbidden | Where-Object { $errorLine.Contains($_) } })
    $line = if ($missing.Count -gt 0) { "$id FAIL no godot-xaml error with: $($missing[0])" }
    elseif ($unwanted.Count -gt 0) { "$id FAIL unexpected error: $($unwanted[0])" }
    else { "$id PASS" }
    Write-Result $id $line
}

# Runs the probe on a pack in one mode and records one line per expected PROBE id.
function Test-Probe {
    param([Parameter(Mandatory)] $Export, [Parameter(Mandatory)][string] $Mode, [Parameter(Mandatory)][string[]] $Ids)
    if (-not (Test-Path -LiteralPath $Export.Pack)) {
        $Ids | ForEach-Object { Write-Result "PROBE $_" "PROBE $_ FAIL no pack to probe" }
        return
    }
    # The probe also prints "Failed to add a custom resource loader/saver ... does not inherit" errors; they are
    # expected: the pack's global class list names the addon's C# classes, which a GDScript-only project cannot run.
    $arguments = @('--headless', '--path', $probe, '--script', 'res://probe.gd', '--', $Export.Pack, $Mode, $sceneUid)
    $run = Invoke-GodotStreaming -Godot $godot -Name "probe $($Export.Id)" -Arguments $arguments
    foreach ($id in $Ids) {
        $line = $run.Lines | Where-Object { $_ -match "^PROBE $id (PASS|FAIL)" } | Select-Object -First 1
        Write-Result "PROBE $id" $(if ($line) { $line } else { "PROBE $id FAIL the probe printed no line for it" })
    }
}

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

New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

$all = Invoke-Export 'all' 'Windows Desktop'
Test-CleanExport $all
Test-Probe $all 'main' @('P1', 'P2', 'P3', 'P4', 'P5', 'P6', 'P7', 'P8')

Test-FailedExport (Invoke-Export 'selected' 'Windows Selected') @($dropError, $includeAdvice)
Test-FailedExport (Invoke-Export 'exclude' 'Windows Exclude') @($dropError, $excludeAdvice, 'XresFormatLoader.cs')
Test-CleanExport (Invoke-Export 'resources' 'Windows Resources')

$broken = Invoke-Export 'broken' 'Windows Broken'
Test-FailedExport $broken @("$pluginError res://checks/broken.xscn:", "$pluginError res://checks/cases/notxml.xres:") @($dropError)
Test-Probe $broken 'broken' @('B1', 'B2')

$dependencies = Invoke-Export 'dependencies' 'Windows Dependencies'
Test-CleanExport $dependencies
Test-Probe $dependencies 'dependencies' @('D1', 'D2')

Test-FailedExport (Invoke-Export 'customized' 'Windows Customized') @($dropError, $includeAdvice)

$passed = @($results | Where-Object { $_ -match '^(EXPORT|PROBE) \S+ PASS$' }).Count
[Console]::Out.WriteLine("DONE $passed/$($expected.Count)")
exit $(if ($passed -eq $expected.Count) { 0 } else { 1 })
