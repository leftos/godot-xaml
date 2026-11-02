#requires -Version 7
<#
.SYNOPSIS
Runs the xscn spike's editor checks E1 to E8 in a windowed Godot 4.7 editor.

.DESCRIPTION
Builds the spike, imports it headless, then starts the editor twice with the editor_checks plugin's user arguments: once with --xscn-editor-checks=run (checks E1 to E7) and once with --xscn-editor-checks=reopen (check E8).

The plugin writes its CHECK, NOTE and DONE lines to <repo>/.tmp/editor-checks/results.txt, with a screenshot per check and copies of the saved scenes. The script empties that folder first, backs up scenes/ and XscnFormatSaver.cs, and restores them at the end, then compares the spike's files by hash with how they started and writes RESTORE OK or RESTORE FAIL <files>.

Godot is $env:GODOT_PATH, else $env:GODOT_BIN. The script opens editor windows, so it runs through the gate, which routes it to the build box's desktop. It exits 0 only when every step and every check passed and the restore is byte-identical.

.EXAMPLE
pwsh -NoProfile -File C:/Users/lefto/.claude/tools/gate.ps1 run --log .tmp/editor-checks.log --slot heavy --timeout 1200 -- pwsh -NoProfile -File spikes/xscn-loader/editor-checks.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$godot = if ($env:GODOT_PATH) { $env:GODOT_PATH } elseif ($env:GODOT_BIN) { $env:GODOT_BIN } else {
    throw 'editor-checks.ps1: GODOT_PATH or GODOT_BIN must name a Godot 4.7 console binary'
}

$spike = $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$results = Join-Path $repo '.tmp/editor-checks'
$backup = Join-Path $results 'backup'
$resultsFile = Join-Path $results 'results.txt'
$scenes = Join-Path $spike 'scenes'
$saver = Join-Path $spike 'addons/xscn_spike/XscnFormatSaver.cs'
$outArg = '--out=' + $results.Replace('\', '/')
$excludedTopLevel = @('.godot', 'bin', 'obj')
$expectedChecks = 8
$editorCeilingSeconds = @{ run = 600; reopen = 180 }

function Write-Result {
    param([Parameter(Mandatory)][string] $Line)
    [Console]::Out.WriteLine($Line)
    [IO.File]::AppendAllText($resultsFile, "$Line`n")
}

function Get-SpikeHash {
    [OutputType([hashtable])]
    param()
    $hashes = @{}
    foreach ($item in Get-ChildItem -LiteralPath $spike -Force) {
        if ($excludedTopLevel -contains $item.Name) {
            continue
        }
        $files = if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force } else { @($item) }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($spike, $file.FullName).Replace('\', '/')
            $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $hashes
}

function Invoke-Step {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Command)
    [Console]::Out.WriteLine("STEP $Name")
    & $Command | Out-Host
    $code = $LASTEXITCODE
    Write-Result "STEP $Name exit $code"
    return $code -eq 0
}

function Invoke-Editor {
    param([Parameter(Mandatory)][string] $Mode)
    $ceiling = $editorCeilingSeconds[$Mode]
    [Console]::Out.WriteLine("STEP editor-$Mode")
    $info = [Diagnostics.ProcessStartInfo]::new($godot)
    $info.UseShellExecute = $false
    foreach ($argument in @('--path', $spike, '--editor', '--', "--xscn-editor-checks=$Mode", $outArg)) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit($ceiling * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            Write-Result "STEP editor-$Mode killed: still running after the $ceiling s hang guard"
            return $false
        }
        $process.WaitForExit()
        Write-Result "STEP editor-$Mode exit $($process.ExitCode)"
        return $process.ExitCode -eq 0
    }
    finally {
        $process.Dispose()
    }
}

function Restore-Spike {
    param([Parameter(Mandatory)][hashtable] $Before)
    try {
        Copy-Item -Path (Join-Path $backup 'scenes/*') -Destination $scenes -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $backup 'XscnFormatSaver.cs') -Destination $saver -Force
        foreach ($name in @('Moved.xscn', 'Moved.xscn.uid')) {
            $moved = Join-Path $scenes $name
            if (Test-Path -LiteralPath $moved) {
                Remove-Item -LiteralPath $moved -Force
            }
        }
        $after = Get-SpikeHash
        $paths = @($Before.Keys) + @($after.Keys) | Sort-Object -Unique
        $differing = @($paths | Where-Object { -not ($Before.ContainsKey($_) -and $after.ContainsKey($_) -and $Before[$_] -eq $after[$_]) })
    }
    catch {
        Write-Result "RESTORE FAIL $($_.Exception.Message)"
        return $false
    }
    if ($differing.Count -eq 0) {
        Write-Result 'RESTORE OK'
        return $true
    }
    Write-Result "RESTORE FAIL $($differing -join ' ')"
    return $false
}

if (Test-Path -LiteralPath $results) {
    Get-ChildItem -LiteralPath $results -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$before = Get-SpikeHash
Copy-Item -LiteralPath $scenes -Destination (Join-Path $backup 'scenes') -Recurse
Copy-Item -LiteralPath $saver -Destination (Join-Path $backup 'XscnFormatSaver.cs')

$stepsPassed = $false
$restored = $false
try {
    $prepared = (Invoke-Step 'build' { dotnet build (Join-Path $spike 'XscnSpike.sln') -c Debug }) -and
        (Invoke-Step 'import' { & $godot --headless --path $spike --import })
    if ($prepared) {
        $ran = Invoke-Editor 'run'
        $reopened = Invoke-Editor 'reopen'
        $stepsPassed = $ran -and $reopened
    }
}
finally {
    $restored = Restore-Spike $before
}

$lines = @(Get-Content -LiteralPath $resultsFile)
[Console]::Out.WriteLine('--- results.txt')
$lines | ForEach-Object { [Console]::Out.WriteLine($_) }
$passes = @($lines | Where-Object { $_ -match '^CHECK \S+ PASS$' }).Count
$fails = @($lines | Where-Object { $_ -match '^CHECK \S+ FAIL' }).Count
$allPassed = $stepsPassed -and $restored -and $fails -eq 0 -and $passes -eq $expectedChecks
exit $(if ($allPassed) { 0 } else { 1 })
