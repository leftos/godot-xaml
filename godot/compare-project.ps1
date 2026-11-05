#requires -Version 7
<#
.SYNOPSIS
Dumps every scene and resource of a whole Godot project as Godot 4.7 loads them, or compares two such dumps by relative
path, so a project can be compared as written, converted to .xscn and .xres, and converted back.

.DESCRIPTION
Dump (-Project <root holding project.godot> -Out <folder>) empties -Out (creating it when missing), then installs the
compare scene into the project: godot/compare/Compare.cs is copied to <Project>/zz_godot_xaml_compare/Compare.cs and a
Compare.tscn naming res://zz_godot_xaml_compare/Compare.cs is written beside it. It refuses with exit 2, changing
nothing, when <Project>/project.godot is missing, when zz_godot_xaml_compare/ is already there (a previous run's
leftover to look at), when the project holds more than one *.csproj, when -Out is the project or a folder holding it,
or when -Out holds anything but *.dump files, folders of them and a stage.status (so -Out . or -Out $HOME is never
emptied). Then it builds the project's one *.csproj in Debug (a project without one skips the build), imports the
project headless twice, and runs the compare scene headless with --dump-tree=<Out>: every .tscn, .xscn, .tres and .xres
under res:// (but res://addons, dot folders, folders holding a .gdignore or their own project.godot, bin and obj beside
a .csproj, links and the compare folder) is dumped to <Out>/<relative path without extension>.scene.dump or
.resource.dump, with a DUMP <relative path> OK|FAIL <reason> line per file and DUMPED <ok>/<total>. A file that does not
load writes a dump holding LOAD FAIL <reason>; a second file with the same dump path (a/x.tscn beside a/x.xscn) fails
as collides with <first relative path>. The compare folder is removed afterwards, whatever happened. Every Godot line of
the import, reimport and dump steps that Harness.psm1's Get-EngineErrorKind classes (ERROR, SCRIPT-ERROR, SHADER-ERROR,
FATAL) is printed again at the end as ENGINE <step> <line>, since a scene whose script does not load still loads, with
no script. A dump exits 0 only when every step exited 0, no engine line was classed and the scene printed DUMPED n/n
with n above 0; else 1. The dumps stay in -Out either way, and the run writes <Out>/stage.status last: exit <code> on its
first line, then each ENGINE line.

A hard kill (a gate timeout, a closed console) skips the cleanup and can leave zz_godot_xaml_compare/ in the target
project, where the project's own build compiles its Compare.cs until it is removed; the next dump refuses until then.

Diff (-Left <folder> -Right <folder>) refuses with exit 2 when a folder does not exist, or when either folder's
stage.status is missing or records an exit other than 0 (a stage whose dump failed is never compared). It pairs every
*.dump under the two folders by its path relative to its folder (case-sensitive, / separators) and prints, in ordinal
order of that path, COMPARE <relative path> PASS when the two files are equal after normalising line endings,
COMPARE <relative path> FAIL left|right did not load: <reason> when either side's dump is a LOAD FAIL line,
COMPARE <relative path> FAIL line <N>: left `<text>` right `<text>` at the first differing line (<end> past a file's
last line), or COMPARE <relative path> FAIL only in left|only in right. It ends with DONE <passed>/<total> and exits 0
only when there was at least one pair and every pair passed.

Godot's lines are printed as Godot writes them, so the gate's stall watchdog sees the run progress. Godot is
$env:GODOT_PATH, else $env:GODOT_BIN; the diff needs no Godot.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --stall 900 --log .tmp/compare-project.log --slot light `
    --timeout 2400 -- pwsh -NoProfile -File godot/compare-project.ps1 -Project <root> -Out .tmp/dumps/original

.EXAMPLE
pwsh -NoProfile -File godot/compare-project.ps1 -Left .tmp/dumps/original -Right .tmp/dumps/back
#>
[CmdletBinding(DefaultParameterSetName = 'Dump')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Dump')][string] $Project,
    [Parameter(Mandatory, ParameterSetName = 'Dump')][string] $Out,
    [Parameter(Mandatory, ParameterSetName = 'Diff')][string] $Left,
    [Parameter(Mandatory, ParameterSetName = 'Diff')][string] $Right
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force

# The folder the compare scene is installed into; its name sorts last and is not a dot folder, so Godot scans it.
$installName = 'zz_godot_xaml_compare'
# The file a dump run writes into -Out last: its exit code, then its ENGINE lines.
$statusName = 'stage.status'
# The first words of the dump a file that did not load writes.
$loadFail = 'LOAD FAIL '

# Prints a refusal on stderr and exits 2.
function Stop-Refused {
    param([Parameter(Mandatory)][string] $Message)
    [Console]::Error.WriteLine("compare-project.ps1: $Message")
    exit 2
}

# Every *.dump under folder, keyed by its path relative to the folder with / separators, compared case-sensitively.
function Get-Dumps {
    param([Parameter(Mandatory)][string] $Folder)
    $dumps = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    Get-ChildItem -LiteralPath $Folder -Recurse -File -Force |
        Where-Object { $_.Name.EndsWith('.dump', [StringComparison]::Ordinal) } |
        ForEach-Object { $dumps[[IO.Path]::GetRelativePath($Folder, $_.FullName).Replace('\', '/')] = $_.FullName }
    return , $dumps
}

# A dump's lines with its line endings normalised.
function Get-DumpLines {
    param([Parameter(Mandatory)][string] $Path)
    return , ([IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Split("`n"))
}

# The first differing line of two dumps, worded as Compare.cs's FirstDifference words it, or an empty string.
function Get-FirstDifference {
    param([Parameter(Mandatory)][string] $LeftPath, [Parameter(Mandatory)][string] $RightPath)
    $leftLines = Get-DumpLines $LeftPath
    $rightLines = Get-DumpLines $RightPath
    for ($line = 0; $line -lt [Math]::Max($leftLines.Count, $rightLines.Count); $line++) {
        $fromLeft = if ($line -lt $leftLines.Count) { $leftLines[$line] } else { '<end>' }
        $fromRight = if ($line -lt $rightLines.Count) { $rightLines[$line] } else { '<end>' }
        if (-not [string]::Equals($fromLeft, $fromRight, [StringComparison]::Ordinal)) {
            return "line $($line + 1): left ``$fromLeft`` right ``$fromRight``"
        }
    }
    return ''
}

# A pair's verdict: FAIL when either side did not load (its dump is a LOAD FAIL line, so two failed loads never pass),
# else PASS or FAIL at the first differing line.
function Get-PairVerdict {
    param([Parameter(Mandatory)][string] $LeftPath, [Parameter(Mandatory)][string] $RightPath)
    foreach ($side in @(@('left', $LeftPath), @('right', $RightPath))) {
        $first = (Get-DumpLines $side[1])[0]
        if ($first.StartsWith($loadFail, [StringComparison]::Ordinal)) {
            return "FAIL $($side[0]) did not load: $($first.Substring($loadFail.Length))"
        }
    }
    $difference = Get-FirstDifference $LeftPath $RightPath
    return $(if ($difference.Length -eq 0) { 'PASS' } else { "FAIL $difference" })
}

# The exit code a stage folder's stage.status records on its first line, or 'unknown, no stage.status' without one.
function Get-StageExit {
    param([Parameter(Mandatory)][string] $Folder)
    $status = Join-Path $Folder $statusName
    if (-not (Test-Path -LiteralPath $status -PathType Leaf)) {
        return "unknown, no $statusName"
    }
    $first = @(Get-Content -LiteralPath $status -TotalCount 1)
    return $(if ($first.Count -eq 1 -and $first[0] -cmatch '^exit (-?\d+)$') { $Matches[1] } else { "unknown, $statusName unreadable" })
}

# The diff mode: COMPARE lines per relative path, DONE, and the exit status.
function Invoke-Diff {
    $leftRoot = [IO.Path]::GetFullPath($Left, $PWD.ProviderPath)
    $rightRoot = [IO.Path]::GetFullPath($Right, $PWD.ProviderPath)
    foreach ($folder in $leftRoot, $rightRoot) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            Stop-Refused "$folder is not a folder"
        }
        $stageExit = Get-StageExit $folder
        if ($stageExit -ne '0') {
            Stop-Refused "$folder is a stage whose dump failed (exit $stageExit); fix the dump first"
        }
    }
    $leftDumps = Get-Dumps $leftRoot
    $rightDumps = Get-Dumps $rightRoot
    $union = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $union.UnionWith([string[]]@($leftDumps.Keys))
    $union.UnionWith([string[]]@($rightDumps.Keys))
    [string[]] $paths = [Linq.Enumerable]::ToArray($union)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $passed = 0
    foreach ($path in $paths) {
        $verdict = if (-not $rightDumps.ContainsKey($path)) { 'FAIL only in left' }
        elseif (-not $leftDumps.ContainsKey($path)) { 'FAIL only in right' }
        else { Get-PairVerdict $leftDumps[$path] $rightDumps[$path] }
        $passed += [int]($verdict -ceq 'PASS')
        [Console]::Out.WriteLine("COMPARE $path $verdict")
    }
    [Console]::Out.WriteLine("DONE $passed/$($paths.Count)")
    return $(if ($paths.Count -gt 0 -and $passed -eq $paths.Count) { 0 } else { 1 })
}

# Runs one Godot step, keeping each line Get-EngineErrorKind classes as an ENGINE line; returns whether it exited 0 with
# no Fatal error line, and the lines.
function Invoke-CheckedGodot {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][string[]] $Arguments)
    $run = Invoke-GodotStreaming -Godot $godot -Name $Name -Arguments $Arguments
    foreach ($line in $run.Lines) {
        if ($null -ne (Get-EngineErrorKind -Line $line)) {
            $engineLines.Add("ENGINE $Name $line")
        }
    }
    return [pscustomobject]@{ Ok = $run.Exit -eq 0 -and $null -eq $run.Fatal; Lines = $run.Lines }
}

# Copies Compare.cs into the project and writes its scene beside it, naming the script at its installed path.
function Install-CompareScene {
    [Console]::Out.WriteLine('STEP install')
    New-Item -ItemType Directory -Path $install | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'compare/Compare.cs') -Destination (Join-Path $install 'Compare.cs')
    $scene = "[gd_scene format=3]`n`n[ext_resource type=`"Script`" path=`"res://$installName/Compare.cs`" id=`"1_c0mp4`"]`n`n" +
        "[node name=`"Compare`" type=`"Node`"]`nscript = ExtResource(`"1_c0mp4`")`n"
    [IO.File]::WriteAllText((Join-Path $install 'Compare.tscn'), $scene, [Text.UTF8Encoding]::new($false))
    [Console]::Out.WriteLine('STEP install exit 0')
}

# The dump steps after install: build, import, reimport and dump; returns whether every step passed and the scene
# printed DUMPED n/n.
function Invoke-DumpSteps {
    if ($csprojs.Count -eq 1) {
        $csproj = $csprojs[0].FullName
        if (-not (Invoke-Step 'build' ({ dotnet build $csproj -c Debug }.GetNewClosure()))) {
            return $false
        }
    } else {
        [Console]::Out.WriteLine('STEP build skipped (no csproj)')
    }
    # The first scan of a fresh project runs before the C# global classes register, so .xscn and .xres files get no uid
    # (docs/DECISIONS.md, "The addon registers from C#, as global classes"); the second scan has the classes.
    foreach ($step in 'import', 'reimport') {
        if (-not (Invoke-CheckedGodot $step @('--headless', '--path', $projectRoot, '--import')).Ok) {
            return $false
        }
    }
    $run = Invoke-CheckedGodot 'dump' @(
        '--headless', '--path', $projectRoot, '--scene', "res://$installName/Compare.tscn", '--', "--dump-tree=$outRoot")
    $dumped = @($run.Lines | Where-Object { $_ -cmatch '^DUMPED (\d+)/(\d+)$' -and $Matches[1] -eq $Matches[2] -and [int]$Matches[1] -gt 0 })
    return $run.Ok -and $dumped.Count -eq 1
}

if ($PSCmdlet.ParameterSetName -eq 'Diff') {
    exit (Invoke-Diff)
}

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'compare-project.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary'
}

$projectRoot = [IO.Path]::GetFullPath($Project, $PWD.ProviderPath).TrimEnd('\', '/')
$outRoot = [IO.Path]::GetFullPath($Out, $PWD.ProviderPath).TrimEnd('\', '/')
$install = Join-Path $projectRoot $installName
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'project.godot') -PathType Leaf)) {
    Stop-Refused "$projectRoot holds no project.godot"
}
if (Test-Path -LiteralPath $install) {
    Stop-Refused "$install already exists, a previous run's leftover; look at it and remove it"
}
$csprojs = @(Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.csproj' |
        Where-Object { $_.Name.EndsWith('.csproj', [StringComparison]::Ordinal) })
if ($csprojs.Count -gt 1) {
    Stop-Refused "$projectRoot holds more than one csproj: $(($csprojs.Name | Sort-Object) -join ', ')"
}
# Emptying -Out must never reach the project itself.
$relativeProject = [IO.Path]::GetRelativePath($outRoot, $projectRoot)
if ($relativeProject -eq '.' -or -not ($relativeProject.StartsWith('..') -or [IO.Path]::IsPathRooted($relativeProject))) {
    Stop-Refused "-Out $outRoot is the project or holds it"
}

if (Test-Path -LiteralPath $outRoot) {
    # Only a previous stage's output is emptied: dumps, folders of dumps and its stage.status. A link counts as foreign.
    $foreign = @(Get-ChildItem -LiteralPath $outRoot -Recurse -Force | Where-Object {
            $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or (-not $_.PSIsContainer -and
                -not $_.Name.EndsWith('.dump', [StringComparison]::Ordinal) -and
                -not ($_.Name -ceq $statusName -and $_.DirectoryName -eq $outRoot))
        })
    if (-not (Test-Path -LiteralPath $outRoot -PathType Container) -or $foreign.Count -gt 0) {
        Stop-Refused "-Out $outRoot holds files that are not dumps; give an empty or dump-only folder"
    }
    Get-ChildItem -LiteralPath $outRoot -Force | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $outRoot | Out-Null
}

$engineLines = [Collections.Generic.List[string]]::new()
$passed = $false
try {
    Install-CompareScene
    $passed = Invoke-DumpSteps
} finally {
    # The compare folder, with any .uid Godot wrote beside its files, never outlives the run.
    if (Test-Path -LiteralPath $install) {
        Remove-Item -LiteralPath $install -Recurse -Force
    }
}

foreach ($line in $engineLines) {
    [Console]::Out.WriteLine($line)
}
$exitCode = if ($passed -and $engineLines.Count -eq 0) { 0 } else { 1 }
# Written last, so a stage a hard kill cut short has no status and the diff refuses it.
$status = @("exit $exitCode") + $engineLines
[IO.File]::WriteAllText((Join-Path $outRoot $statusName), ($status -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
exit $exitCode
