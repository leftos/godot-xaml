#requires -Version 7
<#
.SYNOPSIS
Tests godot/compare-project.ps1 against real Godot on a project it generates: the dumps of a project as written, converted
to .xscn and .xres and converted back are equal by relative path, and a changed value, a script that does not load and a
previous run's leftover each fail the run.

.DESCRIPTION
Builds a fresh project under .tmp/compare-project-test/<UTC yyyyMMdd-HHmmss>/project/: a project.godot that enables the
addon, a CompareProjectTest.csproj that references the repo's core by absolute path, the addon copied from
godot/addons/godot_xaml/, a C# script scripts/Mover.cs with one exported Speed, the fixtures groups.tscn,
connections.tscn, nested.tscn and material.tres from tests/fixtures/scenes/ copied into both a/ and b/ (so base names
repeat), c/scripted.tscn (a Mover with Speed = 3), c/instancer.tscn (an instance of res://a/groups.tscn) and a nested
project, nested/project.godot with nested/inner.tscn, which no dump may name. The CLI is built once in Release and its
GodotXaml.Cli.dll run with dotnet. Dumps go to dumps/<case> beside the project.

The cases run in order, each through compare-project.ps1: original (the dump exits 0 with DUMPED 10/10), forward
(convert --project, dump, every DUMP line names an .xscn or .xres, and the diff against original passes all 10 paths),
back (convert --project --back, the same with .tscn and .tres), broken (Speed = 4 in c/scripted.tscn: the diff exits 1
with one FAIL, c/scripted.scene.dump, naming Speed), engine-error (a scene whose script res://scripts/Missing.cs does
not exist: every file dumps, DUMPED 11/11, yet the dump exits 1 with an ENGINE dump line naming Missing.cs),
failed-stage (the diff against the engine-error stage exits 2, naming the stage's failed dump), only-in (that scene
replaced by d/extra.tscn: the diff exits 1 with the one FAIL only in right), collision (a/groups.xscn written beside
a/groups.tscn by a plain convert: the dump exits 1 with a DUMP ... FAIL collides with ... line, and the extra file is
removed afterwards), out-refusal (an -Out holding keep.txt: the dump exits 2, says it holds files that are not dumps,
and keep.txt stays) and leftover (a zz_godot_xaml_compare/
folder already in the project: the dump exits 2, says so, and leaves it there). Each child run's lines are printed as
they arrive, so the gate's stall watchdog sees the run progress.

A failing case does not stop the later ones; the test prints CASE <name> PASS|FAIL <detail> per case and exits 0 only
when every case passed. The project folder is removed at the end only when every case passed, so a failing run keeps it.
Godot is $env:GODOT_PATH, else $env:GODOT_BIN, as compare-project.ps1 reads it.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 900 --log .tmp/test-compare-project.log --slot heavy `
    --timeout 2400 -- pwsh -NoProfile -File godot/test-compare-project.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A child run's non-zero status is a case's to judge, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

$repo = Split-Path $PSScriptRoot -Parent
$compareProject = Join-Path $PSScriptRoot 'compare-project.ps1'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
$test = Join-Path $repo ".tmp/compare-project-test/$stamp"
$project = Join-Path $test 'project'
$dumps = Join-Path $test 'dumps'

# The ten dumps every stage of the generated project writes, by relative path.
$expectedDumps = @(
    foreach ($folder in 'a', 'b') {
        "$folder/connections.scene.dump"
        "$folder/groups.scene.dump"
        "$folder/material.resource.dump"
        "$folder/nested.scene.dump"
    }
    'c/instancer.scene.dump'
    'c/scripted.scene.dump'
)

# Writes text to a project file with LF line endings and no byte-order mark, creating its folder.
function Write-ProjectFile {
    param([Parameter(Mandatory)][string] $Relative, [Parameter(Mandatory)][string] $Text)
    $path = Join-Path $project $Relative
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    [IO.File]::WriteAllText($path, $Text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

# Generates the project: settings, csproj, addon, script and the ten scene and resource files.
function New-TestProject {
    Write-ProjectFile 'project.godot' @'
config_version=5

[application]

config/name="CompareProjectTest"
config/features=PackedStringArray("4.7", "C#", "Forward Plus")

[dotnet]

project/assembly_name="CompareProjectTest"

[editor_plugins]

enabled=PackedStringArray("res://addons/godot_xaml/plugin.cfg")

'@
    $core = Join-Path $repo 'src/GodotXaml.Core/GodotXaml.Core.csproj'
    Write-ProjectFile 'CompareProjectTest.csproj' @"
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>CompareProjectTest</RootNamespace>
    <!-- Godot.NET.Sdk adds GodotSharp as an implicit reference with its own version, which central package management
         refuses while the repo's Directory.Packages.props lists GodotSharp (NU1009). -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <!-- The addon's namespaces do not follow its folders. -->
    <NoWarn>`$(NoWarn);IDE0130</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="$core" />
  </ItemGroup>
</Project>

"@
    $addon = Join-Path $project 'addons/godot_xaml'
    New-Item -ItemType Directory -Force -Path $addon | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'addons/godot_xaml') -Force |
        Where-Object { $_.Name -notin @('bin', 'obj') } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $addon -Recurse }
    Write-ProjectFile 'scripts/Mover.cs' @'
using Godot;

namespace CompareProjectTest;

/// <summary>A node with one exported value, for the compare's scripted scene.</summary>
public partial class Mover : Node
{
    /// <summary>A value the scene stores.</summary>
    [Export]
    public int Speed { get; set; }
}

'@
    foreach ($folder in 'a', 'b') {
        foreach ($fixture in 'groups.tscn', 'connections.tscn', 'nested.tscn', 'material.tres') {
            $text = [IO.File]::ReadAllText((Join-Path $repo "tests/fixtures/scenes/$fixture"))
            Write-ProjectFile "$folder/$fixture" $text
        }
    }
    Write-ProjectFile 'c/scripted.tscn' @'
[gd_scene format=3]

[ext_resource type="Script" path="res://scripts/Mover.cs" id="1_mover"]

[node name="Root" type="Node" unique_id=1201]

[node name="Mover" type="Node" parent="." unique_id=1202]
script = ExtResource("1_mover")
Speed = 3

'@
    # A folder holding its own project.godot is another project, which the dump walk must not enter.
    Write-ProjectFile 'nested/project.godot' "config_version=5`n"
    Write-ProjectFile 'nested/inner.tscn' ([IO.File]::ReadAllText((Join-Path $repo 'tests/fixtures/scenes/groups.tscn')))
    Write-ProjectFile 'c/instancer.tscn' @'
[gd_scene format=3]

[ext_resource type="PackedScene" path="res://a/groups.tscn" id="1_groups"]

[node name="Root" type="Node" unique_id=1301]

[node name="Groups" parent="." unique_id=1302 instance=ExtResource("1_groups")]

'@
}

# Runs one child command, printing each line as it arrives; returns the lines and the exit status.
function Invoke-Child {
    param([Parameter(Mandatory)][string] $File, [Parameter(Mandatory)][string[]] $Arguments)
    $lines = @(& $File @Arguments 2>&1 | ForEach-Object {
            $line = "$_"
            [Console]::Out.WriteLine($line)
            $line
        })
    return [pscustomobject]@{ Lines = $lines; Exit = $LASTEXITCODE }
}

# Runs compare-project.ps1 with the given arguments in a pwsh of its own.
function Invoke-CompareProject {
    param([Parameter(Mandatory)][string[]] $Arguments)
    return Invoke-Child -File 'pwsh' -Arguments (@('-NoProfile', '-File', $compareProject) + $Arguments)
}

# Dumps the project into dumps/<stage>; returns the run.
function Invoke-Dump {
    param([Parameter(Mandatory)][string] $Stage)
    return Invoke-CompareProject @('-Project', $project, '-Out', (Join-Path $dumps $Stage))
}

# Diffs dumps/original against dumps/<stage>; returns the run.
function Invoke-Diff {
    param([Parameter(Mandatory)][string] $Stage)
    return Invoke-CompareProject @('-Left', (Join-Path $dumps 'original'), '-Right', (Join-Path $dumps $Stage))
}

# Why a dump run did not dump all ten files cleanly, or an empty string.
function Get-DumpFailure {
    param([Parameter(Mandatory)] $Run)
    if ($Run.Exit -ne 0) {
        return "the dump exited $($Run.Exit)"
    }
    if (@($Run.Lines | Where-Object { $_ -ceq 'DUMPED 10/10' }).Count -ne 1) {
        return 'the dump printed no DUMPED 10/10 line'
    }
    return Get-NestedFailure $Run
}

# Why a dump run entered the nested project, or an empty string.
function Get-NestedFailure {
    param([Parameter(Mandatory)] $Run)
    $nested = @($Run.Lines | Where-Object { $_ -cmatch '^DUMP nested/' })
    return $(if ($nested.Count -eq 0) { '' } else { "the dump entered the nested project: $($nested -join ' | ')" })
}

# Why a stage's DUMP lines are not all of the given converted extensions: the two named files must be there and no
# DUMP line may name a file of the other side's extensions.
function Get-ExtensionFailure {
    param(
        [Parameter(Mandatory)] $Run,
        [Parameter(Mandatory)][string] $Scene,
        [Parameter(Mandatory)][string] $Resource,
        [Parameter(Mandatory)][string] $Other
    )
    foreach ($wanted in "DUMP a/groups.$Scene OK", "DUMP a/material.$Resource OK") {
        if ($Run.Lines -cnotcontains $wanted) {
            return "the dump printed no $wanted line"
        }
    }
    $wrong = @($Run.Lines | Where-Object { $_ -cmatch "^DUMP \S+\.($Other) " })
    return $(if ($wrong.Count -eq 0) { '' } else { "the dump still names $($wrong -join ' | ')" })
}

# Why a diff run did not pass all ten paths, or an empty string.
function Get-DiffFailure {
    param([Parameter(Mandatory)] $Run)
    if ($Run.Exit -ne 0) {
        return "the diff exited $($Run.Exit)"
    }
    $missing = @($expectedDumps | Where-Object { $Run.Lines -cnotcontains "COMPARE $_ PASS" })
    if ($missing.Count -gt 0) {
        return "no PASS line for $($missing -join ', ')"
    }
    if ($Run.Lines -cnotcontains 'DONE 10/10') {
        return 'the diff printed no DONE 10/10 line'
    }
    return ''
}

# Converts the project with the CLI (--back when asked); returns why it failed, or an empty string.
function Invoke-Convert {
    param([switch] $Back)
    $arguments = @($cliDll, 'convert', '--project', $project, '--nobackup')
    if ($Back) {
        $arguments += '--back'
    }
    $run = Invoke-Child -File 'dotnet' -Arguments $arguments
    return $(if ($run.Exit -eq 0) { '' } else { "convert exited $($run.Exit)" })
}

# A converted stage: convert, dump into dumps/<stage>, diff against the original.
function Test-ConvertedStage {
    param([Parameter(Mandatory)][string] $Stage, [switch] $Back)
    $failure = Invoke-Convert -Back:$Back
    if ($failure) {
        return $failure
    }
    $run = Invoke-Dump $Stage
    $failure = Get-DumpFailure $run
    if (-not $failure) {
        $failure = if ($Back) { Get-ExtensionFailure $run 'tscn' 'tres' 'xscn|xres' }
        else { Get-ExtensionFailure $run 'xscn' 'xres' 'tscn|tres' }
    }
    if ($failure) {
        return $failure
    }
    return Get-DiffFailure (Invoke-Diff $Stage)
}

# The broken stage: one changed value fails exactly its own dump, and the failure names the value.
function Test-Broken {
    $scripted = Join-Path $project 'c/scripted.tscn'
    $text = [IO.File]::ReadAllText($scripted)
    if (-not $text.Contains('Speed = 3')) {
        return 'c/scripted.tscn holds no Speed = 3 line'
    }
    [IO.File]::WriteAllText($scripted, $text.Replace('Speed = 3', 'Speed = 4'), [Text.UTF8Encoding]::new($false))
    try {
        return Get-BrokenFailure
    } finally {
        # The later cases compare against the original again, so the changed value goes back.
        [IO.File]::WriteAllText($scripted, $text, [Text.UTF8Encoding]::new($false))
    }
}

# Why the broken stage's dump or diff did not fail exactly c/scripted.scene.dump over Speed, or an empty string.
function Get-BrokenFailure {
    $failure = Get-DumpFailure (Invoke-Dump 'broken')
    if ($failure) {
        return $failure
    }
    $run = Invoke-Diff 'broken'
    $fails = @($run.Lines | Where-Object { $_ -cmatch '^COMPARE \S+ FAIL ' })
    if ($run.Exit -ne 1) {
        return "the diff exited $($run.Exit), not 1"
    }
    if ($fails.Count -ne 1 -or -not $fails[0].StartsWith('COMPARE c/scripted.scene.dump FAIL ', [StringComparison]::Ordinal)) {
        return "expected one FAIL for c/scripted.scene.dump, got $($fails.Count): $($fails -join ' | ')"
    }
    if ($fails[0] -cnotmatch 'Speed') {
        return "the FAIL line does not name Speed: $($fails[0])"
    }
    return ''
}

# The engine-error stage: a scene whose script does not exist fails the dump with an ENGINE line.
function Test-EngineError {
    Write-ProjectFile 'd/missing_script.tscn' @'
[gd_scene format=3]

[ext_resource type="Script" path="res://scripts/Missing.cs" id="1_missing"]

[node name="Root" type="Node" unique_id=1401]
script = ExtResource("1_missing")

'@
    $run = Invoke-Dump 'engine-error'
    if ($run.Exit -ne 1) {
        return "the dump exited $($run.Exit), not 1"
    }
    if ($run.Lines -cnotcontains 'DUMPED 11/11') {
        return 'the dump printed no DUMPED 11/11 line, so the ENGINE check was not what failed it'
    }
    if (@($run.Lines | Where-Object { $_ -cmatch '^ENGINE dump .*Missing\.cs' }).Count -eq 0) {
        return 'the dump printed no ENGINE dump line naming Missing.cs'
    }
    return ''
}

# The only-in stage: a scene only the new stage has fails as only in right, and nothing else fails.
function Test-OnlyIn {
    Remove-Item -LiteralPath (Join-Path $project 'd/missing_script.tscn')
    Write-ProjectFile 'd/extra.tscn' @'
[gd_scene format=3]

[node name="Root" type="Node" unique_id=1501]

'@
    $run = Invoke-Dump 'only-in'
    if ($run.Exit -ne 0 -or $run.Lines -cnotcontains 'DUMPED 11/11') {
        return "the dump exited $($run.Exit) or printed no DUMPED 11/11 line"
    }
    $diff = Invoke-Diff 'only-in'
    $fails = @($diff.Lines | Where-Object { $_ -cmatch '^COMPARE \S+ FAIL' })
    if ($diff.Exit -ne 1) {
        return "the diff exited $($diff.Exit), not 1"
    }
    if ($fails.Count -ne 1 -or $fails[0] -cne 'COMPARE d/extra.scene.dump FAIL only in right') {
        return "expected only COMPARE d/extra.scene.dump FAIL only in right, got $($fails -join ' | ')"
    }
    return ''
}

# The collision stage: a/groups.xscn beside a/groups.tscn shares its dump path, so the second fails and the run exits 1.
function Test-Collision {
    $extras = @('a/groups.xscn', 'a/groups.xscn.uid') | ForEach-Object { Join-Path $project $_ }
    try {
        $convert = Invoke-Child -File 'dotnet' -Arguments @($cliDll, 'convert', (Join-Path $project 'a/groups.tscn'))
        if ($convert.Exit -ne 0 -or -not (Test-Path -LiteralPath $extras[0])) {
            return "the plain convert exited $($convert.Exit) or wrote no a/groups.xscn"
        }
        $run = Invoke-Dump 'collision'
        if ($run.Exit -ne 1) {
            return "the dump exited $($run.Exit), not 1"
        }
        $wanted = @('DUMP a/groups.xscn FAIL collides with a/groups.tscn', 'DUMP a/groups.tscn FAIL collides with a/groups.xscn')
        if (@($run.Lines | Where-Object { $_ -cin $wanted }).Count -ne 1) {
            return 'the dump printed no collides with line for a/groups'
        }
        return ''
    } finally {
        # The later cases see the project as it was.
        foreach ($extra in $extras) {
            if (Test-Path -LiteralPath $extra) {
                Remove-Item -LiteralPath $extra
            }
        }
    }
}

# The out-refusal stage: an -Out holding a file that is not a dump is refused, and the file stays.
function Test-OutRefusal {
    $out = Join-Path $dumps 'out-refusal'
    $keep = Join-Path $out 'keep.txt'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    [IO.File]::WriteAllText($keep, "kept`n")
    $run = Invoke-CompareProject @('-Project', $project, '-Out', $out)
    if ($run.Exit -ne 2) {
        return "the dump exited $($run.Exit), not 2"
    }
    if (@($run.Lines | Where-Object { $_.Contains('holds files that are not dumps') }).Count -eq 0) {
        return 'the refusal did not say holds files that are not dumps'
    }
    if (-not (Test-Path -LiteralPath $keep)) {
        return 'keep.txt is gone'
    }
    return ''
}

# The failed-stage diff: a stage whose dump failed is refused, never compared.
function Test-FailedStage {
    $run = Invoke-Diff 'engine-error'
    if ($run.Exit -ne 2) {
        return "the diff exited $($run.Exit), not 2"
    }
    if (@($run.Lines | Where-Object { $_.Contains('is a stage whose dump failed (exit 1)') }).Count -eq 0) {
        return 'the diff did not say the engine-error stage failed (exit 1)'
    }
    return ''
}

# The leftover stage: a compare folder already in the project refuses the run and is left as it was.
function Test-Leftover {
    $leftover = Join-Path $project 'zz_godot_xaml_compare'
    New-Item -ItemType Directory -Path $leftover | Out-Null
    $run = Invoke-Dump 'leftover'
    if ($run.Exit -ne 2) {
        return "the dump exited $($run.Exit), not 2"
    }
    if (-not (Test-Path -LiteralPath $leftover)) {
        return 'the leftover folder is gone'
    }
    if (@($run.Lines | Where-Object { $_.Contains('zz_godot_xaml_compare already exists') }).Count -eq 0) {
        return 'the refusal did not say zz_godot_xaml_compare already exists'
    }
    return ''
}

$results = [Collections.Generic.List[bool]]::new()

# Runs one case and prints its CASE line; a case that throws fails with the exception's text.
function Invoke-Case {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Body)
    try {
        $failure = "$(& $Body)"
    } catch {
        $failure = "threw: $($_.Exception.Message)"
    }
    $results.Add($failure.Length -eq 0)
    [Console]::Out.WriteLine($(if ($failure.Length -eq 0) { "CASE $Name PASS" } else { "CASE $Name FAIL $failure" }))
}

New-TestProject
[Console]::Out.WriteLine("TEST project $project")

[Console]::Out.WriteLine('STEP cli')
dotnet build (Join-Path $repo 'src/GodotXaml.Cli/GodotXaml.Cli.csproj') -c Release | Out-Host
$cliBuilt = $LASTEXITCODE
[Console]::Out.WriteLine("STEP cli exit $cliBuilt")
$cliDll = Join-Path $repo 'src/GodotXaml.Cli/bin/Release/net10.0/GodotXaml.Cli.dll'
if ($cliBuilt -ne 0 -or -not (Test-Path -LiteralPath $cliDll)) {
    [Console]::Error.WriteLine("test-compare-project.ps1: the CLI did not build to $cliDll")
    exit 1
}

Invoke-Case 'original' { Get-DumpFailure (Invoke-Dump 'original') }
Invoke-Case 'forward' { Test-ConvertedStage 'forward' }
Invoke-Case 'back' { Test-ConvertedStage 'back' -Back }
Invoke-Case 'broken' { Test-Broken }
Invoke-Case 'engine-error' { Test-EngineError }
Invoke-Case 'failed-stage' { Test-FailedStage }
Invoke-Case 'only-in' { Test-OnlyIn }
Invoke-Case 'collision' { Test-Collision }
Invoke-Case 'out-refusal' { Test-OutRefusal }
Invoke-Case 'leftover' { Test-Leftover }

$allPassed = $results -notcontains $false
if ($allPassed) {
    Remove-Item -LiteralPath $test -Recurse -Force
} else {
    [Console]::Out.WriteLine("TEST kept $test")
}
exit $(if ($allPassed) { 0 } else { 1 })
