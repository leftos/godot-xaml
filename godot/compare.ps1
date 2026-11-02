#requires -Version 7
<#
.SYNOPSIS
Compares each scene fixture with its converted .xscn as Godot 4.7 instantiates them.

.DESCRIPTION
Copies the scene fixtures (tests/fixtures/scenes/*.tscn and *.tres), their converted goldens
(tests/fixtures/converted/*.xscn) into godot/cases/, recreated on each run, and the scripts the fixtures name
(holder.gd, quoted.gd and tagged.gd from tools/scene-fixtures/, replacing the previous copies) into godot/, where the fixtures' res:// paths point. Then builds the godot/ project,
imports it headless (which registers the addon's loader) and runs res://compare/Compare.tscn headless. The scene prints
COMPARE <name> PASS or COMPARE <name> FAIL <detail> per fixture and DONE <passed>/<total>; each side's dump is left in
godot/cases/<name>.tscn.dump and <name>.xscn.dump.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. The script exits 0 only when every step exited 0, no fixture failed,
every expected fixture passed and the scene printed DONE with all of them.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --log .tmp/godot-compare.log --slot light --timeout 1500 -- pwsh -NoProfile -File godot/compare.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'compare.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary'
}

$project = $PSScriptRoot
$repo = Split-Path $project -Parent
$cases = Join-Path $project 'cases'
$expectedFixtures = 12
# The scripts the fixtures name by res:// path, copied from the generator's project; .gitignore lists each copy.
$fixtureScripts = @('holder.gd', 'quoted.gd', 'tagged.gd')

function Invoke-Step {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Command)
    [Console]::Out.WriteLine("STEP $Name")
    & $Command | Out-Host
    $code = $LASTEXITCODE
    [Console]::Out.WriteLine("STEP $Name exit $code")
    return $code -eq 0
}

[Console]::Out.WriteLine('STEP copy')
if (Test-Path $cases) {
    Remove-Item -LiteralPath $cases -Recurse -Force
}
New-Item -ItemType Directory -Path $cases | Out-Null
Copy-Item -Path (Join-Path $repo 'tests/fixtures/scenes/*.tscn') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/scenes/*.tres') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/converted/*.xscn') -Destination $cases
foreach ($script in $fixtureScripts) {
    foreach ($copy in @((Join-Path $project $script), (Join-Path $project "$script.uid"))) {
        if (Test-Path -LiteralPath $copy) {
            Remove-Item -LiteralPath $copy -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $repo "tools/scene-fixtures/$script") -Destination $project
}
[Console]::Out.WriteLine('STEP copy exit 0')

$built = Invoke-Step 'build' { dotnet build (Join-Path $project 'GodotXaml.Addon.csproj') -c Debug -warnaserror }
if (-not $built) {
    exit 1
}

$imported = Invoke-Step 'import' { & $godot --headless --path $project --import }
if (-not $imported) {
    exit 1
}

[Console]::Out.WriteLine('STEP compare')
$lines = @(& $godot --headless --path $project --scene 'res://compare/Compare.tscn' --quit-after 600 2>&1 | ForEach-Object { "$_" })
$compareExit = $LASTEXITCODE
$lines | ForEach-Object { [Console]::Out.WriteLine($_) }
[Console]::Out.WriteLine("STEP compare exit $compareExit")

$passes = @($lines | Where-Object { $_ -match '^COMPARE \S+ PASS$' }).Count
$fails = @($lines | Where-Object { $_ -match '^COMPARE \S+ FAIL' }).Count
$done = @($lines | Where-Object { $_ -eq "DONE $expectedFixtures/$expectedFixtures" }).Count
$allPassed = $compareExit -eq 0 -and $fails -eq 0 -and $passes -eq $expectedFixtures -and $done -eq 1
[Console]::Out.WriteLine("COMPARE passed $passes, failed $fails, expected $expectedFixtures")
exit $(if ($allPassed) { 0 } else { 1 })
