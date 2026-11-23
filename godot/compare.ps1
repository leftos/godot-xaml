#requires -Version 7
<#
.SYNOPSIS
Compares each scene fixture with its converted .xscn and each .tres fixture with its converted .xres, as Godot 4.7 or 4.8
loads them.

.DESCRIPTION
Asks Godot for its version (--version) and stops when the first line it prints does not start with major.minor. Copies
the 4.7 set, the fixtures (tests/fixtures/scenes/*.tscn and *.tres, and the hand-written tests/fixtures/stored/*.tscn)
and their converted goldens (tests/fixtures/converted/*.xscn and *.xres), into godot/cases/, recreated on each run.
Under Godot 4.8 or newer it also copies the 4.8 set: each golden in tests/fixtures/converted48/ with its source of the
same stem in tests/fixtures/tscn48/, whose stems differ from the 4.7 set's. A tscn48 source is left out only when the
script's exclusion list names it with its reason; any other source without a golden, a golden without its source, or
an empty 4.8 set stops the run with a compare.ps1 message, and so does a missing tests/fixtures/converted48 or
tests/fixtures/tscn48 folder. It prints COMPARE set 4.7, or COMPARE set 4.7+4.8 (<k> 4.8 pairs), before the copy. It
copies the scripts the fixtures name (holder.gd, quoted.gd and tagged.gd from tools/scene-fixtures/, replacing the
previous copies) into godot/, where the fixtures' res:// paths point. Then builds the godot/ project as a full rebuild
(--no-incremental), imports it headless (which registers the addon's loader), then imports it again, and runs
res://compare/Compare.tscn headless. The expected pair count is the number of source files it copies (the .tscn and
.tres files in tests/fixtures/scenes, the .tscn files in tests/fixtures/stored and, under 4.8, the 4.8 set's sources),
passed to the scene as --expected-pairs=<n>. The scene prints COMPARE <name> PASS or COMPARE <name> FAIL <detail> per
fixture and DONE <passed>/<total>; each side's dump is left in godot/cases/<name>.tscn.dump and <name>.xscn.dump, or
<name>.tres.dump and <name>.xres.dump.

Godot's lines are printed as Godot writes them, so the gate's stall watchdog sees the run progress. The documented
command passes --stall 600 because a run starved by outside load can go two minutes without enough CPU to count as
progress.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. The script exits 0 only when every step exited 0, no fixture failed, every
expected fixture passed, the scene printed DONE with all of them and Godot printed no Fatal error line.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 600 --log .tmp/godot-compare.log --slot light --timeout 1500 `
    -- pwsh -NoProfile -File godot/compare.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'compare.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 or 4.8 .NET console binary'
}

# The engine's major.minor, from the start of the first line --version prints (4.8.beta2.mono.official.cafc17a19 is 4.8).
$versionLine = "$(@(& $godot --version 2>&1) | Select-Object -First 1)"
if ($versionLine -notmatch '^(\d+)\.(\d+)') {
    [Console]::Error.WriteLine("compare.ps1: the Godot version `"$versionLine`" does not start with <major>.<minor>")
    exit 1
}
$fourEight = [int]$Matches[1] -gt 4 -or ([int]$Matches[1] -eq 4 -and [int]$Matches[2] -ge 8)

$project = $PSScriptRoot
$repo = Split-Path $project -Parent
$cases = Join-Path $project 'cases'
# The expected count is the fixtures themselves: the same .tscn and .tres globs the copy step below copies.
$expectedFixtures = @(Get-ChildItem -File -Path @(
        (Join-Path $repo 'tests/fixtures/scenes/*.tscn'),
        (Join-Path $repo 'tests/fixtures/scenes/*.tres'),
        (Join-Path $repo 'tests/fixtures/stored/*.tscn')
    )).Count
if ($expectedFixtures -eq 0) {
    [Console]::Error.WriteLine('compare.ps1: no fixtures in tests/fixtures/scenes')
    exit 1
}
# The tscn48 sources that are no conversion pair, each with the reason it has no golden in converted48.
$fourEightExcluded = @{
    'blend2d_scripted.tres' = 'it holds a Godot 4.8 triangles order for the core tests, not a conversion pair'
}
# The 4.8 set, under Godot 4.8 or newer: each golden of converted48 with its source of the same stem in tscn48, each
# source counted as an expected pair. A tscn48 source the exclusions do not name without its golden, a golden without
# its source, or no golden at all stops the run, so it never runs the set short.
$fourEightPairs = @()
if ($fourEight) {
    $goldenExtension = @{ '.tscn' = '.xscn'; '.tres' = '.xres' }
    $sourceExtension = @{ '.xscn' = '.tscn'; '.xres' = '.tres' }
    foreach ($folder in @('converted48', 'tscn48')) {
        if (-not (Test-Path -LiteralPath (Join-Path $repo "tests/fixtures/$folder") -PathType Container)) {
            [Console]::Error.WriteLine("compare.ps1: tests/fixtures/$folder is missing, under a Godot 4.8 or newer")
            exit 1
        }
    }
    $problems = [System.Collections.Generic.List[string]]::new()
    $sources = @(Get-ChildItem -File -Path @(
            (Join-Path $repo 'tests/fixtures/tscn48/*.tscn'),
            (Join-Path $repo 'tests/fixtures/tscn48/*.tres')
        ) | Sort-Object Name)
    foreach ($source in $sources) {
        $golden = Join-Path $repo "tests/fixtures/converted48/$($source.BaseName)$($goldenExtension[$source.Extension])"
        if (-not $fourEightExcluded.ContainsKey($source.Name) -and -not (Test-Path -LiteralPath $golden)) {
            $problems.Add("compare.ps1: tests/fixtures/tscn48/$($source.Name) has no golden in tests/fixtures/converted48")
        }
    }
    $fourEightPairs = @(Get-ChildItem -File -Path @(
            (Join-Path $repo 'tests/fixtures/converted48/*.xscn'),
            (Join-Path $repo 'tests/fixtures/converted48/*.xres')
        ) | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                Golden = $_.FullName
                Source = Join-Path $repo "tests/fixtures/tscn48/$($_.BaseName)$($sourceExtension[$_.Extension])"
            }
        })
    foreach ($pair in $fourEightPairs) {
        if (-not (Test-Path -LiteralPath $pair.Source)) {
            $problems.Add("compare.ps1: tests/fixtures/converted48/$($pair.Name) has no source in tests/fixtures/tscn48")
        }
    }
    if ($fourEightPairs.Count -eq 0) {
        $problems.Add('compare.ps1: no 4.8 pairs in tests/fixtures/converted48, under a Godot 4.8 or newer')
    }
    if ($problems.Count -gt 0) {
        foreach ($problem in $problems) { [Console]::Error.WriteLine($problem) }
        exit 1
    }
    $expectedFixtures += $fourEightPairs.Count
}
[Console]::Out.WriteLine($(if ($fourEight) { "COMPARE set 4.7+4.8 ($($fourEightPairs.Count) 4.8 pairs)" } else { 'COMPARE set 4.7' }))
# The scripts the fixtures name by res:// path, copied from the generator's project; .gitignore lists each copy.
$fixtureScripts = @('holder.gd', 'quoted.gd', 'tagged.gd')

[Console]::Out.WriteLine('STEP copy')
if (Test-Path $cases) {
    Remove-Item -LiteralPath $cases -Recurse -Force
}
New-Item -ItemType Directory -Path $cases | Out-Null
Copy-Item -Path (Join-Path $repo 'tests/fixtures/scenes/*.tscn') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/scenes/*.tres') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/stored/*.tscn') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/converted/*.xscn') -Destination $cases
Copy-Item -Path (Join-Path $repo 'tests/fixtures/converted/*.xres') -Destination $cases
foreach ($pair in $fourEightPairs) {
    Copy-Item -LiteralPath $pair.Golden -Destination $cases
    Copy-Item -LiteralPath $pair.Source -Destination $cases
}
foreach ($script in $fixtureScripts) {
    foreach ($copy in @((Join-Path $project $script), (Join-Path $project "$script.uid"))) {
        if (Test-Path -LiteralPath $copy) {
            Remove-Item -LiteralPath $copy -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $repo "tools/scene-fixtures/$script") -Destination $project
}
[Console]::Out.WriteLine('STEP copy exit 0')

$built = Invoke-Step 'build' { dotnet build (Join-Path $project 'GodotXaml.Addon.csproj') -c Debug -warnaserror --no-incremental }
if (-not $built) {
    exit 1
}

$imported = Invoke-GodotStep -Godot $godot -Name import -Arguments @('--headless', '--path', $project, '--import')
if (-not $imported) {
    exit 1
}

# The first scan of a fresh project runs before the C# global classes register, so .xscn and .xres files get no uid
# (docs/DECISIONS.md, "The addon registers from C#, as global classes"); the second scan has the classes.
$reimported = Invoke-GodotStep -Godot $godot -Name reimport -Arguments @('--headless', '--path', $project, '--import')
if (-not $reimported) {
    exit 1
}

$run = Invoke-GodotStreaming -Godot $godot -Name compare -Arguments @(
    '--headless', '--path', $project, '--scene', 'res://compare/Compare.tscn',
    '--', "--expected-pairs=$expectedFixtures")
$lines = $run.Lines
$compareExit = $run.Exit

$passes = @($lines | Where-Object { $_ -match '^COMPARE \S+ PASS$' }).Count
$fails = @($lines | Where-Object { $_ -match '^COMPARE \S+ FAIL' }).Count
$done = @($lines | Where-Object { $_ -eq "DONE $expectedFixtures/$expectedFixtures" }).Count
$allPassed = $compareExit -eq 0 -and -not $run.Fatal -and $fails -eq 0 -and $passes -eq $expectedFixtures -and $done -eq 1
[Console]::Out.WriteLine("COMPARE passed $passes, failed $fails, expected $expectedFixtures")
exit $(if ($allPassed) { 0 } else { 1 })
