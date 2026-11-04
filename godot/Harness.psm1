#requires -Version 7
<#
.SYNOPSIS
What the Godot harness scripts share: one named step, and a Godot run whose lines are echoed as they arrive.

.DESCRIPTION
godot/checks.ps1, compare.ps1, export.ps1 and editor-checks.ps1 import this module. Invoke-Step prints STEP <name>, runs
the step's scriptblock and prints STEP <name> exit <code>, returning whether it exited 0. Invoke-GodotStreaming does the
same around a Godot run and returns the lines it printed with its exit status, so a caller judges a run from the same
lines the reader sees. A .NET finalizer crash at Godot's exit prints a Fatal error line and can still exit 0, so a run
that prints one has failed: Invoke-GodotStreaming prints FATAL <name> <line> and returns that line as Fatal, which every
caller fails the run over.

A scriptblock passed to Invoke-Step runs in the session state of the script that wrote it, so it reads that script's
variables ($godot, $project) as it does today.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is the caller's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

# Runs one step, printing its name, its output and its exit status; returns whether it exited 0.
function Invoke-Step {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Command)
    [Console]::Out.WriteLine("STEP $Name")
    & $Command | Out-Host
    $code = $LASTEXITCODE
    [Console]::Out.WriteLine("STEP $Name exit $code")
    return $code -eq 0
}

# Runs Godot, echoes every line as Godot prints it, and returns the lines with its exit status.
function Invoke-GodotStreaming {
    param(
        [Parameter(Mandatory)][string] $Godot,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string[]] $Arguments
    )
    [Console]::Out.WriteLine("STEP $Name")
    # Each line is printed as it arrives, so the gate's stall watchdog sees the run progress.
    $lines = @(& $Godot @Arguments 2>&1 | ForEach-Object {
            $line = "$_"
            [Console]::Out.WriteLine($line)
            $line
        })
    $code = $LASTEXITCODE
    [Console]::Out.WriteLine("STEP $Name exit $code")
    # The .NET stack of a finalizer crash arrives on stderr, which the 2>&1 above merges into the lines.
    $fatal = $lines | Where-Object { $_ -cmatch '^Fatal error\.' } | Select-Object -First 1
    if ($null -ne $fatal) {
        [Console]::Out.WriteLine("FATAL $Name $fatal")
    }
    return [pscustomobject]@{ Lines = $lines; Exit = $code; Fatal = $fatal }
}

Export-ModuleMember -Function Invoke-Step, Invoke-GodotStreaming
