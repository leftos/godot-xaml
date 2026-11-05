#requires -Version 7

<#
.SYNOPSIS
Builds, tests and format-checks the GodotXaml solution and the VS Code extension, every step run through tools/gate.ps1.

.DESCRIPTION
Every verb but help runs its command through the repo's gate wrapper, tools/gate.ps1, in a pwsh of its own: the wrapper
hands the words to the installed gate.exe, which writes the command's whole output to the log under .tmp/ and enforces
the ceiling, and on a machine without gate.exe it runs the command directly. The script exits with the gated command's
status. The pre-push hooks in .pre-commit-config.yaml call build, godot-build, godot-export-build and vscode-check, so
the same commands run there; pre-commit runs csharpier on the staged files itself.

  help                prints this usage; the verb a run without one uses
  build               dotnet build GodotXaml.slnx in Release, warnings as errors; .tmp/build.log, ceiling 600 s
  godot-build         dotnet build godot/GodotXaml.Addon.csproj in Debug, warnings as errors; .tmp/godot-build.log, ceiling 600 s
  godot-export-build  dotnet build godot/GodotXaml.Addon.csproj in ExportRelease, warnings as errors; .tmp/godot-export-build.log, ceiling 600 s
  test                build, then dotnet test of the solution in Release without building it again; .tmp/test.log, ceiling 600 s
  format-check        dotnet tool restore, then dotnet csharpier check on the repo, changing nothing; .tmp/format.log
  format              dotnet tool restore, then dotnet csharpier format on the repo; .tmp/format.log
  vscode-install      pnpm install of editors/vscode with the frozen lockfile; .tmp/vscode-install.log, ceiling 600 s
  vscode-lint         oxlint over editors/vscode, warnings failing it; .tmp/vscode-lint.log, ceiling 300 s
  vscode-format-check oxfmt check of editors/vscode/src, changing nothing; .tmp/vscode-format-check.log, ceiling 300 s
  vscode-check        lint, format check, type check and build of editors/vscode, then its VSIX packaged into .tmp/vscode/;
                      .tmp/vscode-check.log and .tmp/vscode-package.log, ceiling 600 s each
  vscode-format       oxfmt rewriting editors/vscode/src; .tmp/vscode-format.log, ceiling 300 s

.PARAMETER Verb
One of help, build, godot-build, godot-export-build, test, format-check, format, vscode-install, vscode-lint,
vscode-format-check, vscode-check or vscode-format; help when it is not given.

.EXAMPLE
./run.ps1 build

.EXAMPLE
./run.ps1 test
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'help', 'build', 'godot-build', 'godot-export-build', 'test', 'format-check', 'format',
        'vscode-install', 'vscode-lint', 'vscode-format-check', 'vscode-check', 'vscode-format')]
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
    Write-Host '  help                prints this usage'
    Write-Host '  build               dotnet build GodotXaml.slnx -c Release -warnaserror (.tmp/build.log)'
    Write-Host '  godot-build         dotnet build godot/GodotXaml.Addon.csproj -c Debug -warnaserror (.tmp/godot-build.log)'
    Write-Host '  godot-export-build  dotnet build godot/GodotXaml.Addon.csproj -c ExportRelease -warnaserror (.tmp/godot-export-build.log)'
    Write-Host '  test                build, then dotnet test the solution -c Release --no-build (.tmp/test.log)'
    Write-Host '  format-check        dotnet csharpier check . (.tmp/format.log)'
    Write-Host '  format              dotnet csharpier format . (.tmp/format.log)'
    Write-Host '  vscode-install      pnpm --dir editors/vscode install --frozen-lockfile (.tmp/vscode-install.log)'
    Write-Host '  vscode-lint         pnpm --dir editors/vscode run lint (.tmp/vscode-lint.log)'
    Write-Host '  vscode-format-check pnpm --dir editors/vscode run format:check (.tmp/vscode-format-check.log)'
    Write-Host '  vscode-check        pnpm --dir editors/vscode run check, then run package (.tmp/vscode-check.log, .tmp/vscode-package.log)'
    Write-Host '  vscode-format       pnpm --dir editors/vscode run format (.tmp/vscode-format.log)'
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

# The Godot addon project in ExportRelease, the configuration a game export builds, where code under #if TOOLS is absent,
# so a non-TOOLS file reaching an editor-only symbol fails here instead of in a user's export.
function Invoke-GodotExportBuildVerb {
    return Invoke-Gated -Log '.tmp/godot-export-build.log' -Slot 'heavy' -TimeoutSeconds 600 -Command @(
        'dotnet', 'build', 'godot/GodotXaml.Addon.csproj', '-c', 'ExportRelease', '-warnaserror')
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

# The VS Code extension's packages installed from its lockfile, which the lockfile alone decides.
function Invoke-VscodeInstallVerb {
    return Invoke-Gated -Log '.tmp/vscode-install.log' -Slot 'light' -TimeoutSeconds 600 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'install', '--frozen-lockfile')
}

# Whether the VS Code extension's packages are installed; a missing install is reported with the verb that makes it.
function Test-VscodeInstalled {
    if (Test-Path (Join-Path $PSScriptRoot 'editors/vscode/node_modules')) {
        return $true
    }
    Write-Host 'run.ps1: editors/vscode/node_modules is missing; run ./run.ps1 vscode-install first.'
    return $false
}

# oxlint over the VS Code extension, a warning failing it as an error does; the pre-commit hook's command.
function Invoke-VscodeLintVerb {
    if (-not (Test-VscodeInstalled)) {
        return 1
    }
    return Invoke-Gated -Log '.tmp/vscode-lint.log' -Slot 'light' -TimeoutSeconds 300 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'run', 'lint')
}

# oxfmt's check of the VS Code extension's sources, changing nothing; the pre-commit hook's command.
function Invoke-VscodeFormatCheckVerb {
    if (-not (Test-VscodeInstalled)) {
        return 1
    }
    return Invoke-Gated -Log '.tmp/vscode-format-check.log' -Slot 'light' -TimeoutSeconds 300 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'run', 'format:check')
}

# The VS Code extension linted, format-checked, type-checked and built, then packaged into .tmp/vscode/.
function Invoke-VscodeCheckVerb {
    if (-not (Test-VscodeInstalled)) {
        return 1
    }
    $status = Invoke-Gated -Log '.tmp/vscode-check.log' -Slot 'light' -TimeoutSeconds 600 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'run', 'check')
    if ($status -ne 0) {
        return $status
    }
    return Invoke-Gated -Log '.tmp/vscode-package.log' -Slot 'light' -TimeoutSeconds 600 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'run', 'package')
}

# The VS Code extension's sources rewritten by oxfmt.
function Invoke-VscodeFormatVerb {
    if (-not (Test-VscodeInstalled)) {
        return 1
    }
    return Invoke-Gated -Log '.tmp/vscode-format.log' -Slot 'light' -TimeoutSeconds 300 -Command @(
        'pnpm', '--dir', 'editors/vscode', 'run', 'format')
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# The verb runs from the script's own folder, which the caller keeps: the caller's location is given back afterwards
# whether the script was started as its own process or dot-run from an interactive pwsh.
Push-Location $PSScriptRoot
try {
    $status = switch ($Verb) {
        'build' { Invoke-BuildVerb }
        'godot-build' { Invoke-GodotBuildVerb }
        'godot-export-build' { Invoke-GodotExportBuildVerb }
        'test' { Invoke-TestVerb }
        'format-check' { Invoke-FormatVerb }
        'format' { Invoke-FormatVerb -Fix }
        'vscode-install' { Invoke-VscodeInstallVerb }
        'vscode-lint' { Invoke-VscodeLintVerb }
        'vscode-format-check' { Invoke-VscodeFormatCheckVerb }
        'vscode-check' { Invoke-VscodeCheckVerb }
        'vscode-format' { Invoke-VscodeFormatVerb }
        default { Show-Help; 0 }
    }
}
finally {
    Pop-Location
}
exit $status
