#requires -Version 7
<#
.SYNOPSIS
Runs the addon's editor checks in a windowed Godot 4.7 editor.

.DESCRIPTION
Recreates <repo>/.tmp/editor-checks, copies godot/ (without the headless checks in checks/ and compare.ps1's copies), src/
and metadata/ into its run/ folder, overlays the editor_checks plugin and the fixtures of tools/editor-checks, and enables
the plugin in the copy, so the tracked files stay untouched and nothing
needs restoring. It then builds the copy as a full rebuild (--no-incremental), imports it headless twice, and starts the
editor twice with the plugin's user arguments: once with --xscn-editor-checks=run, and once with
--xscn-editor-checks=reopen, for the check that needs a fresh editor.

The plugin writes its CHECK, NOTE and DONE lines to results/results.txt under that folder, with a screenshot per check and
copies of the saved scenes. The script echoes results.txt and prints EDITOR-CHECKS passed <p>, failed <f>, expected <n>.

Each editor's error output is echoed as it arrives and scanned: any line starting with ERROR: fails the run, and the
script lists each one as ENGINE-ERROR editor-<mode> <line> after that editor exits. The exit errors come after the
plugin's DONE, so only the script sees them.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. The script opens editor windows, so it runs through the gate, which routes it
to a builder's hidden desktop. It exits 0 only when every step exited 0, neither editor printed an ERROR: line, no check
failed and every expected check passed.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --no-markers --log .tmp/editor-checks.log --slot heavy --timeout 1200 --
  pwsh -NoProfile -File godot/editor-checks.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'editor-checks.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 .NET console binary'
}

$project = $PSScriptRoot
$repo = Split-Path $project -Parent
$root = Join-Path $repo '.tmp/editor-checks'
$run = Join-Path $root 'run'
$results = Join-Path $root 'results'
$resultsFile = Join-Path $results 'results.txt'
$runProject = Join-Path $run 'godot'
$outArg = '--out=' + $results.Replace('\', '/') + '/'
$sources = Join-Path $repo 'tools/editor-checks'
$expectedChecks = 12
$editorCeilingSeconds = @{ run = 600; reopen = 180 }

function Invoke-Step {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Command)
    [Console]::Out.WriteLine("STEP $Name")
    & $Command | Out-Host
    $code = $LASTEXITCODE
    [Console]::Out.WriteLine("STEP $Name exit $code")
    return $code -eq 0
}

function Copy-Tree {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Skip,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $TopSkip
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($Skip -contains $item.Name -or $TopSkip -contains $item.Name) {
            continue
        }
        $target = Join-Path $Destination $item.Name
        if ($item.PSIsContainer) {
            Copy-Tree $item.FullName $target $Skip @()
        }
        else {
            Copy-Item -LiteralPath $item.FullName -Destination $target
        }
    }
}

function Set-RunProjectSettings {
    param([Parameter(Mandatory)][string] $Path)
    $text = [IO.File]::ReadAllText($Path)
    $enabledLine = '(?m)^enabled=PackedStringArray\((.*?)\)(\r?)$'
    if ($text -notmatch $enabledLine) {
        throw "editor-checks.ps1: $Path has no editor_plugins/enabled line"
    }
    $text = $text -replace $enabledLine, 'enabled=PackedStringArray($1, "res://addons/editor_checks/plugin.cfg")$2'
    [IO.File]::WriteAllText($Path, $text)
}

function Read-EditorErrors {
    param(
        [Parameter(Mandatory)][Diagnostics.Process] $Process,
        [Parameter(Mandatory)][DateTime] $Deadline
    )
    $errors = [System.Collections.Generic.List[string]]::new()
    $read = $Process.StandardError.ReadLineAsync()
    $emptySliceAfterExit = $false
    while ($true) {
        $left = [int][Math]::Max(0, ($Deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait([Math]::Min($left, 1000))) {
            if (-not $Process.HasExited) {
                $emptySliceAfterExit = $false
                if ($left -eq 0) {
                    return [pscustomobject]@{ Errors = $errors; TimedOut = $true }
                }
                continue
            }
            # A line printed before the editor's exit completes the read within a slice, so two empty slices after
            # the exit mean nothing more of its output is coming, even when another process holds the pipe open.
            if ($emptySliceAfterExit) {
                return [pscustomobject]@{ Errors = $errors; TimedOut = $false }
            }
            $emptySliceAfterExit = $true
            continue
        }
        $line = $read.Result
        if ($null -eq $line) {
            return [pscustomobject]@{ Errors = $errors; TimedOut = $false }
        }
        $emptySliceAfterExit = $false
        [Console]::Error.WriteLine($line)
        if ($line -cmatch '^ERROR: ') {
            $errors.Add($line)
        }
        $read = $Process.StandardError.ReadLineAsync()
    }
}

function Invoke-Editor {
    param([Parameter(Mandatory)][string] $Mode)
    $ceiling = $editorCeilingSeconds[$Mode]
    [Console]::Out.WriteLine("STEP editor-$Mode")
    $info = [Diagnostics.ProcessStartInfo]::new($godot)
    $info.UseShellExecute = $false
    # Standard output stays inherited; the error stream is captured so the engine's ERROR: lines can fail the run.
    $info.RedirectStandardError = $true
    # Godot writes UTF-8; naming it keeps text off the console code page.
    $info.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    foreach ($argument in @('--path', $runProject, '--editor', '--', "--xscn-editor-checks=$Mode", $outArg)) {
        $info.ArgumentList.Add($argument)
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($ceiling)
    $process = [Diagnostics.Process]::Start($info)
    try {
        $pumped = Read-EditorErrors -Process $process -Deadline $deadline
        if ($pumped.TimedOut) {
            $process.Kill($true)
            $process.WaitForExit()
            [Console]::Out.WriteLine("STEP editor-$Mode killed: still running after the $ceiling s hang guard")
        }
        else {
            $process.WaitForExit()
            [Console]::Out.WriteLine("STEP editor-$Mode exit $($process.ExitCode)")
        }
        $engineErrors = $pumped.Errors
        if ($engineErrors.Count -gt 0) {
            [Console]::Out.WriteLine("STEP editor-$Mode engine errors: $($engineErrors.Count)")
            foreach ($engineError in $engineErrors) {
                [Console]::Out.WriteLine("ENGINE-ERROR editor-$Mode $engineError")
            }
        }
        return (-not $pumped.TimedOut) -and $engineErrors.Count -eq 0 -and $process.ExitCode -eq 0
    }
    finally {
        $process.Dispose()
    }
}

if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $run, $results | Out-Null

# Left out at the project's top only: compare.ps1's gitignored copies (cases/ and the helper scripts), and the headless
# checks in checks/, which the editor never runs; a name skipped at every depth would also drop deeper folders so named.
$godotTopSkip = @('cases', 'checks', 'holder.gd', 'holder.gd.uid', 'quoted.gd', 'quoted.gd.uid', 'tagged.gd', 'tagged.gd.uid')
Copy-Tree $project $runProject @('.godot', 'bin', 'obj') $godotTopSkip
Copy-Tree (Join-Path $repo 'src') (Join-Path $run 'src') @('bin', 'obj') @()
Copy-Tree (Join-Path $repo 'metadata') (Join-Path $run 'metadata') @() @()
Copy-Tree (Join-Path $sources 'addons/editor_checks') (Join-Path $runProject 'addons/editor_checks') @() @()
Copy-Tree (Join-Path $sources 'fixtures') (Join-Path $runProject 'fixtures') @() @()
Set-RunProjectSettings (Join-Path $runProject 'project.godot')

# The first scan of a fresh project runs before the C# global classes register, so .xscn and .xres files get no uid
# (docs/DECISIONS.md, "The addon registers from C#, as global classes"); the second scan has the classes.
$prepared = (Invoke-Step 'build' { dotnet build (Join-Path $runProject 'GodotXaml.Addon.csproj') -c Debug -warnaserror --no-incremental }) -and
    (Invoke-Step 'import' { & $godot --headless --path $runProject --import }) -and
    (Invoke-Step 'reimport' { & $godot --headless --path $runProject --import })

$editorsPassed = $false
if ($prepared) {
    $ran = Invoke-Editor 'run'
    $reopened = Invoke-Editor 'reopen'
    $editorsPassed = $ran -and $reopened
}

$lines = if (Test-Path -LiteralPath $resultsFile) { @(Get-Content -LiteralPath $resultsFile) } else { @() }
[Console]::Out.WriteLine('--- results.txt')
$lines | ForEach-Object { [Console]::Out.WriteLine($_) }
$passes = @($lines | Where-Object { $_ -match '^CHECK \S+ PASS$' }).Count
$fails = @($lines | Where-Object { $_ -match '^CHECK \S+ FAIL' }).Count
$allPassed = $editorsPassed -and $fails -eq 0 -and $passes -eq $expectedChecks
[Console]::Out.WriteLine("EDITOR-CHECKS passed $passes, failed $fails, expected $expectedChecks")
exit $(if ($allPassed) { 0 } else { 1 })
