#requires -Version 7

<#
.SYNOPSIS
Builds, tests and format-checks the GodotXaml solution, every step run through tools/gate.ps1.

.DESCRIPTION
Every verb but help runs its command through the repo's gate wrapper, tools/gate.ps1, in a pwsh of its own: the wrapper
hands the words to the installed gate.exe, which writes the command's whole output to the log under .tmp/ and enforces
the ceiling, and on a machine without gate.exe it runs the command directly. The script exits with the gated command's
status. The hooks in .pre-commit-config.yaml call build, godot-build and format-check, so the same commands run there.

  help          prints this usage; the verb a run without one uses
  build         dotnet build GodotXaml.slnx in Release, warnings as errors; .tmp/build.log, ceiling 600 s
  godot-build   dotnet build godot/GodotXaml.Addon.csproj in Debug, warnings as errors; .tmp/godot-build.log, ceiling 600 s
  test          build, then dotnet test of the solution in Release without building it again; .tmp/test.log, ceiling 600 s
  format-check  dotnet tool restore, then dotnet csharpier check on the repo, changing nothing; .tmp/format.log
  format        dotnet tool restore, then dotnet csharpier format on the repo; .tmp/format.log

.PARAMETER Verb
One of help, build, godot-build, test, format-check or format; help when it is not given.

.EXAMPLE
./run.ps1 build

.EXAMPLE
./run.ps1 test
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('help', 'build', 'godot-build', 'test', 'format-check', 'format')]
    [string]$Verb = 'help'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A gated command's non-zero status is this script's to report, not a throw.
$PSNativeCommandUseErrorActionPreference = $false

$gateWrapper = Join-Path $PSScriptRoot 'tools/gate.ps1'
$logDir = Join-Path $PSScriptRoot '.tmp'
# The child pwsh every gated call starts: the wrapper reads its words from its process argv, which only a pwsh started
# with -File names. The host's own executable is not pwsh under a PowerShell SDK host, so pwsh is looked up on PATH.
$pwshCommand = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pwshCommand) {
    throw 'run.ps1: pwsh is not on PATH; install PowerShell 7 and run this script again.'
}
$pwshPath = $pwshCommand.Source

# The usage, printed for help and for a run with no verb.
function Show-Help {
    Write-Host 'usage: ./run.ps1 <verb>'
    Write-Host '  help          prints this usage'
    Write-Host '  build         dotnet build GodotXaml.slnx -c Release -warnaserror (.tmp/build.log)'
    Write-Host '  godot-build   dotnet build godot/GodotXaml.Addon.csproj -c Debug -warnaserror (.tmp/godot-build.log)'
    Write-Host '  test          build, then dotnet test the solution -c Release --no-build (.tmp/test.log)'
    Write-Host '  format-check  dotnet csharpier check . (.tmp/format.log)'
    Write-Host '  format        dotnet csharpier format . (.tmp/format.log)'
}

# One command under the gate: the log, the slot and the ceiling as given, and the command's own status returned.
function Invoke-Gated {
    param(
        [Parameter(Mandatory)] [string]$Log,
        [Parameter(Mandatory)] [ValidateSet('heavy', 'light')] [string]$Slot,
        [Parameter(Mandatory)] [int]$TimeoutSeconds,
        [Parameter(Mandatory)] [string[]]$Command
    )
    Write-Host "$($Command -join ' ')  (log: $Log, slot: $Slot, ceiling: ${TimeoutSeconds}s)"
    & $pwshPath -NoProfile -File $gateWrapper run --log $Log --slot $Slot --timeout $TimeoutSeconds -- @Command | Out-Host
    return $LASTEXITCODE
}

# The solution built in Release, warnings as errors.
function Invoke-BuildVerb {
    return Invoke-Gated -Log '.tmp/build.log' -Slot 'heavy' -TimeoutSeconds 600 -Command @(
        'dotnet', 'build', 'GodotXaml.slnx', '-c', 'Release', '-warnaserror')
}

# The Godot addon project, which the solution leaves out because its build needs the Godot SDK, in Debug, warnings as errors.
function Invoke-GodotBuildVerb {
    return Invoke-Gated -Log '.tmp/godot-build.log' -Slot 'heavy' -TimeoutSeconds 600 -Command @(
        'dotnet', 'build', 'godot/GodotXaml.Addon.csproj', '-c', 'Debug', '-warnaserror')
}

# The build, then the solution's tests against that build, so the runner never builds a configuration of its own.
function Invoke-TestVerb {
    $status = Invoke-BuildVerb
    if ($status -ne 0) {
        return $status
    }
    return Invoke-Gated -Log '.tmp/test.log' -Slot 'heavy' -TimeoutSeconds 600 -Command @(
        'dotnet', 'test', '--solution', 'GodotXaml.slnx', '-c', 'Release', '--no-build')
}

# CSharpier over the repo, checking or rewriting it, after the tool manifest is restored.
function Invoke-FormatVerb {
    param([switch]$Fix)
    & dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) {
        return $LASTEXITCODE
    }
    $action = if ($Fix) { 'format' } else { 'check' }
    return Invoke-Gated -Log '.tmp/format.log' -Slot 'light' -TimeoutSeconds 300 -Command @('dotnet', 'csharpier', $action, '.')
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# The verb runs from the script's own folder, which the caller keeps: the caller's location is given back afterwards
# whether the script was started as its own process or dot-run from an interactive pwsh.
Push-Location $PSScriptRoot
try {
    $status = switch ($Verb) {
        'build' { Invoke-BuildVerb }
        'godot-build' { Invoke-GodotBuildVerb }
        'test' { Invoke-TestVerb }
        'format-check' { Invoke-FormatVerb }
        'format' { Invoke-FormatVerb -Fix }
        default { Show-Help; 0 }
    }
}
finally {
    Pop-Location
}
exit $status
