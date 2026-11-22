#requires -Version 7
<#
.SYNOPSIS
What the Godot harness scripts share: one named step, and a Godot run whose lines are echoed as they arrive.

.DESCRIPTION
godot/checks.ps1, compare.ps1, export.ps1, editor-checks.ps1 and compare-project.ps1 import this module. Invoke-Step
prints STEP <name>, runs the step's scriptblock and prints STEP <name> exit <code>, returning whether it exited 0.
Invoke-GodotStreaming does the same around a Godot run and returns the lines it printed with its exit status, so a
caller judges a run from the same lines the reader sees. A .NET finalizer crash at Godot's exit prints a Fatal error
line and can still exit 0, so a run that prints one has failed: Invoke-GodotStreaming prints FATAL <name> <line> and
returns that line as Fatal, which every caller fails the run over. Invoke-GodotStep runs one Godot step through
Invoke-GodotStreaming and returns whether it exited 0 with no Fatal error line, which the import and reimport steps use.
Get-EngineErrorKind names the line kinds that fail a run (ERROR, SCRIPT-ERROR, SHADER-ERROR, FATAL), which
Invoke-GodotStreaming and editor-checks.ps1 share. A run that starts a Godot editor (an import, an export flag, -e or
--editor) leaves the project folder as it found it: the editor's C# tools rewrite the project's csproj Sdk to the
engine's own and keep the old file in the .old family, so Invoke-GodotStreaming writes each csproj and backup back and
deletes only the backups the run created. A run a gate timeout or stall kill stops skips that restore, so a csproj it
rewrote is restored by hand then.

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

# Runs Godot, echoes every line as Godot prints it, and returns the lines with its exit status. A run that starts a
# Godot editor leaves the project folder as it found it: the editor's C# tools rewrite the csproj's Sdk to the engine's
# own and keep the old file in the .old family, so every csproj and backup the folder held is put back.
function Invoke-GodotStreaming {
    param(
        [Parameter(Mandatory)][string] $Godot,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string[]] $Arguments
    )
    $snapshot = $null
    if (Test-EditorRunArguments -Arguments $Arguments) {
        $snapshot = Get-CsprojSnapshot -Arguments $Arguments
    }
    try {
        [Console]::Out.WriteLine("STEP $Name")
        # Each line is printed as it arrives, so the gate's stall watchdog sees the run progress.
        $lines = @(& $Godot @Arguments 2>&1 | ForEach-Object {
                $line = "$_"
                [Console]::Out.WriteLine($line)
                $line
            })
        $code = $LASTEXITCODE
        [Console]::Out.WriteLine("STEP $Name exit $code")
    } finally {
        if ($null -ne $snapshot) {
            Restore-CsprojSnapshot -Snapshot $snapshot
        }
    }
    # The .NET stack of a finalizer crash arrives on stderr, which the 2>&1 above merges into the lines.
    $fatal = $lines | Where-Object { (Get-EngineErrorKind -Line $_) -eq 'FATAL' } | Select-Object -First 1
    if ($null -ne $fatal) {
        [Console]::Out.WriteLine("FATAL $Name $fatal")
    }
    return [pscustomobject]@{ Lines = $lines; Exit = $code; Fatal = $fatal }
}

# Whether a Godot run is an editor run, whose C# tools rewrite the project's csproj: the import and export flags and
# the editor ones open an editor (main.cpp), while a --scene run does not. Matched exactly, as Godot matches them.
function Test-EditorRunArguments {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments)
    foreach ($editor in @('--import', '--export-release', '--export-debug', '--export-pack', '--export-patch', '-e', '--editor')) {
        if ($Arguments -ccontains $editor) {
            return $true
        }
    }
    return $false
}

# Snapshots the project folder's csproj files and the backups it already holds, for the run about to rewrite them;
# returns $null when the folder itself is missing. Godot takes the folder from --path, else from the working directory.
function Get-CsprojSnapshot {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments)
    $index = [array]::IndexOf($Arguments, '--path') + 1
    $directory = (Get-Location).ProviderPath
    if ($index -ge 1 -and $index -lt $Arguments.Count) {
        $directory = $Arguments[$index]
    }
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        return $null
    }
    $files = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '*.csproj' -File -Force)) {
        $files[$file.FullName] = [IO.File]::ReadAllBytes($file.FullName)
    }
    $backups = @{}
    foreach ($backup in @(Get-ChildItem -LiteralPath $directory -Filter '*.csproj.old*' -File -Force)) {
        $backups[$backup.FullName] = [IO.File]::ReadAllBytes($backup.FullName)
        Write-Host "WARNING leftover $($backup.Name): an earlier run may have been killed before it restored the csproj"
    }
    return [pscustomobject]@{ Directory = $directory; Files = $files; Backups = $backups }
}

# Puts every snapshotted csproj and backup back whose bytes the run changed, printing each csproj it restored, and
# deletes only the backups the run created.
function Restore-CsprojSnapshot {
    param([Parameter(Mandatory)] $Snapshot)
    foreach ($name in @($Snapshot.Files.Keys)) {
        $current = $null
        if (Test-Path -LiteralPath $name) {
            $current = [IO.File]::ReadAllBytes($name)
        }
        if (Test-SameCsprojBytes -Left $Snapshot.Files[$name] -Right $current) {
            continue
        }
        [IO.File]::WriteAllBytes($name, $Snapshot.Files[$name])
        Write-Host "RESTORED $(Split-Path -Path $name -Leaf)"
    }
    foreach ($name in @($Snapshot.Backups.Keys)) {
        $current = $null
        if (Test-Path -LiteralPath $name) {
            $current = [IO.File]::ReadAllBytes($name)
        }
        if (Test-SameCsprojBytes -Left $Snapshot.Backups[$name] -Right $current) {
            continue
        }
        [IO.File]::WriteAllBytes($name, $Snapshot.Backups[$name])
    }
    foreach ($backup in @(Get-ChildItem -LiteralPath $Snapshot.Directory -Filter '*.csproj.old*' -File -Force)) {
        if ($Snapshot.Backups.ContainsKey($backup.FullName)) {
            continue
        }
        Remove-Item -LiteralPath $backup.FullName -Force
    }
}

# Whether two byte arrays read from a csproj are the same, a missing file (null) or an empty one counting by its bytes.
function Test-SameCsprojBytes {
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][byte[]] $Left,
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][byte[]] $Right
    )
    if ($null -eq $Left -or $null -eq $Right) {
        return $false
    }
    return [Convert]::ToBase64String($Left) -ceq [Convert]::ToBase64String($Right)
}

# Runs one Godot step through Invoke-GodotStreaming; returns whether it exited 0 and printed no Fatal error line.
function Invoke-GodotStep {
    param(
        [Parameter(Mandatory)][string] $Godot,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string[]] $Arguments
    )
    $run = Invoke-GodotStreaming -Godot $Godot -Name $Name -Arguments $Arguments
    return $run.Exit -eq 0 -and $null -eq $run.Fatal
}

# Names the line that fails a run, or $null for a line that leaves it alone. Godot prints a line starting with ERROR:,
# SCRIPT ERROR: or SHADER ERROR: (core/io/logger.h, error_type_string), then a "   at:" line; a warning starts with
# WARNING: and stays informational. A .NET finalizer crash prints a "Fatal error." line on the same error output.
function Get-EngineErrorKind {
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Line)
    if ($Line -cmatch '^Fatal error\.') {
        return 'FATAL'
    }
    if ($Line -cmatch '^(ERROR|SCRIPT ERROR|SHADER ERROR): ') {
        switch ($Matches[1]) {
            'SCRIPT ERROR' { return 'SCRIPT-ERROR' }
            'SHADER ERROR' { return 'SHADER-ERROR' }
            default { return 'ERROR' }
        }
    }
    return $null
}

Export-ModuleMember -Function Invoke-Step, Invoke-GodotStreaming, Invoke-GodotStep, Get-EngineErrorKind
