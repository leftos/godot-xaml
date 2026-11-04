#requires -Version 7
<#
.SYNOPSIS
Tests godot/checks.ps1, godot/compare.ps1 and godot/export.ps1 against a stub Godot and a stub dotnet: a line the run
writes reaches the script's own output while Godot is still running, so the gate's stall watchdog sees the run progress.

.DESCRIPTION
The stub Godot exits 0 for --import. For checks.ps1's scene run it prints CHECK C1 PASS, for compare.ps1's it prints
COMPARE stub1 PASS, and for export.ps1's pack export it prints STUB export started. Each holds until the test writes
its release file, then prints the rest of its run: checks and compare end with a passing verdict, export runs its whole
preset list. The test waits for that first line on the script's stdout, fails at once if the script exits first, and
only then releases the stub. The one bound is a hang guard: a passing run never waits it out. Export's exit code is not
asserted, because a stub that omits the presets' expected errors leaves it exiting 1. Each case gets its own temp root,
a failing case does not stop the later ones, and the test prints CASE <name> PASS|FAIL <detail> per case and exits 0
only when every case passed.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/test-checks.log --slot light --timeout 900 --
  pwsh -NoProfile -File godot/test-checks.ps1
#>
[CmdletBinding()]
param([int] $HangGuardSeconds = 300)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Builds the stub Godot: imports exit 0, -Only ends any other kind of call, the run prints its first line and flushes it,
# holds until the test releases it, and runs the tail. The hold's own bound is past the test's hang guard, so only the
# test ends it.
function New-StubGodot {
    param(
        [Parameter(Mandatory)][string] $FirstLine,
        [Parameter(Mandatory)][string] $Tail,
        [string] $Only = ''
    )
    $template = @'
param()
$all = $args -join ' '
if ($all -match '--import') { exit 0 }
__ONLY__
[Console]::Out.WriteLine('__FIRST__')
[Console]::Out.Flush()
$release = $env:GX_TEST_RELEASE
$deadline = [DateTime]::UtcNow.AddSeconds([int]$env:GX_TEST_HOLD_SECONDS)
while (-not (Test-Path -LiteralPath $release) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
__TAIL__
exit 0
'@
    $template = $template.Replace('__ONLY__', $Only).Replace('__FIRST__', $FirstLine)
    return $template.Replace('__TAIL__', $Tail)
}

# Waits for the first line on the child's stdout; returns whether it arrived, the read still pending when it did not, and
# the failure that says why, with the lines printed before it.
function Wait-GodotLine {
    param(
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)][string] $FirstLine,
        [Parameter(Mandatory)][int] $GuardSeconds
    )
    $printed = [System.Collections.Generic.List[string]]::new()
    $read = $Process.StandardOutput.ReadLineAsync()
    $guard = [DateTime]::UtcNow.AddSeconds($GuardSeconds)
    while ($true) {
        $left = [int][Math]::Max(0, ($guard - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait($left)) {
            $detail = "hung: $FirstLine had not reached the script's output after $GuardSeconds s while the stub " +
                "Godot was still running; it had printed: $($printed -join ' | ')"
            return [pscustomobject]@{ Seen = $false; Read = $read; Printed = $printed; Detail = $detail }
        }
        $line = $read.Result
        if ($null -eq $line) {
            $exit = if ($Process.WaitForExit(5000)) { $Process.ExitCode } else { 'unknown' }
            return [pscustomobject]@{
                Seen = $false; Read = $null; Printed = $printed
                Detail = "the script ended before $FirstLine reached its output (exit $exit)"
            }
        }
        $printed.Add($line)
        if ($line -eq $FirstLine) {
            return [pscustomobject]@{ Seen = $true; Read = $null; Printed = $printed; Detail = $null }
        }
        $read = $Process.StandardOutput.ReadLineAsync()
    }
}

# Releases the stub, ends the child if it overruns the guard, and returns its exit status with its whole stdout: the lines
# read while waiting, then the rest, which is read concurrently so a run printing past the pipe's buffer never blocks.
function Complete-GodotRun {
    param(
        [Parameter(Mandatory)] $Process,
        [Parameter(Mandatory)] $Wait,
        [Parameter(Mandatory)][string] $Release,
        [Parameter(Mandatory)][int] $GuardSeconds
    )
    $rest = $null
    $notEnded = $false
    # With the awaited line seen nothing reads the stream, so the rest is read from now on, before the release: a run
    # prints several KB more and would block on a full pipe while this waits for its exit.
    if ($Wait.Seen) { $rest = $Process.StandardOutput.ReadToEndAsync() }
    New-Item -ItemType File -Force $Release | Out-Null
    if (-not $Process.WaitForExit($GuardSeconds * 1000)) {
        $notEnded = $true
        $Process.Kill($true)
        $null = $Process.WaitForExit()
    }
    $tail = ''
    if ($null -ne $rest) {
        $tail = $rest.GetAwaiter().GetResult()
    } else {
        # The hung path's line read is still pending; awaiting it finishes the drain, and ReadToEnd takes what is past it.
        $pending = if ($null -ne $Wait.Read) { $Wait.Read.GetAwaiter().GetResult() } else { $null }
        if ($null -ne $pending) { $tail = $pending + "`n" + $Process.StandardOutput.ReadToEnd() }
    }
    $printed = @($Wait.Printed)
    $text = if ($printed.Count -gt 0) { ($printed -join "`n") + "`n" + $tail } else { $tail }
    return [pscustomobject]@{ Text = $text; Exit = $Process.ExitCode; Hung = $notEnded }
}

# Records what the case's -Expect names: a clean exit, or a full export run of seven export calls and one DONE line.
function Add-ExpectationFailures {
    param(
        [Parameter(Mandatory)][string] $Expect,
        [Parameter(Mandatory)][string] $Script,
        [Parameter(Mandatory)] $Run,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Stderr,
        [Parameter(Mandatory)] $Failures
    )
    if ($Expect -eq 'CleanExit') {
        if ($Run.Exit -ne 0) {
            $Failures.Add("$Script exited $($Run.Exit) for a stub run where every expected line passed: $Stderr")
        }
        return
    }
    $lines = @($Run.Text -split '\r?\n')
    $started = @($lines | Where-Object { $_ -eq 'STUB export started' }).Count
    if ($started -ne 7) {
        $Failures.Add("$Script printed STUB export started $started time(s) where its seven exports call Godot once each")
    }
    $done = @($lines | Where-Object { $_ -match '^DONE \d+/\d+$' }).Count
    if ($done -ne 1) {
        $Failures.Add("$Script printed $done DONE line(s) where a run reaching its end prints exactly one")
    }
}

# Removes a case's temp root; a failure is a warning, so it can neither abort the later cases nor hide this one's result.
function Remove-TempRoot {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    } catch {
        [Console]::Error.WriteLine("test-checks.ps1: could not remove ${Path}: $($_.Exception.Message)")
    }
}

# Prints the case's one line and returns whether it passed; a case that does not assert the exit code says so in a FAIL.
function Write-CaseResult {
    param(
        [Parameter(Mandatory)][string] $Case,
        [Parameter(Mandatory)] $Failures,
        [AllowEmptyString()][string] $Note = ''
    )
    if ($Failures.Count -eq 0) {
        [Console]::Out.WriteLine("CASE $Case PASS")
        return $true
    }
    $detail = if ($Note) { "$($Failures -join '; '); $Note" } else { $Failures -join '; ' }
    [Console]::Out.WriteLine("CASE $Case FAIL $detail")
    return $false
}

# Runs one script against its stub Godot and waits for the first line; prints the case's result and returns whether it
# passed.
function Test-LinesReachTheOutputWhileGodotRuns {
    param(
        [Parameter(Mandatory)][string] $Case,
        [Parameter(Mandatory)][string] $Script,
        [Parameter(Mandatory)][string] $StubGodot,
        [Parameter(Mandatory)][string] $FirstLine,
        [Parameter(Mandatory)][ValidateSet('CleanExit', 'FullExport')][string] $Expect
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("gx-test-checks-" + [guid]::NewGuid().ToString('N'))
    $stubs = Join-Path $root 'bin'
    $release = Join-Path $root 'release'
    $failures = [System.Collections.Generic.List[string]]::new()
    $process = $null
    $stderr = $null
    $stderrText = $null
    $wait = $null
    $run = $null
    try {
        New-Item -ItemType Directory -Force $stubs | Out-Null
        # A build that does nothing: each script resolves dotnet from PATH, and this folder comes first.
        Set-Content -LiteralPath (Join-Path $stubs 'dotnet.cmd') -Value "@exit /b 0`r`n" -NoNewline
        Set-Content -LiteralPath (Join-Path $stubs 'godot-stub.ps1') -Value $StubGodot
        $godotCmd = "@pwsh -NoProfile -File `"%~dp0godot-stub.ps1`" %*`r`n@exit /b %ERRORLEVEL%`r`n"
        Set-Content -LiteralPath (Join-Path $stubs 'godot.cmd') -Value $godotCmd -NoNewline

        $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
        foreach ($a in @('-NoProfile', '-File', (Join-Path $PSScriptRoot $Script))) { $psi.ArgumentList.Add($a) }
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.Environment['GODOT_PATH'] = Join-Path $stubs 'godot.cmd'
        $psi.Environment['GODOT_BIN'] = ''
        $psi.Environment['PATH'] = $stubs + [System.IO.Path]::PathSeparator + $env:PATH
        $psi.Environment['GX_TEST_RELEASE'] = $release
        $psi.Environment['GX_TEST_HOLD_SECONDS'] = [string]($HangGuardSeconds * 2)

        $process = [System.Diagnostics.Process]::Start($psi)
        $stderr = $process.StandardError.ReadToEndAsync()
        $wait = Wait-GodotLine -Process $process -FirstLine $FirstLine -GuardSeconds $HangGuardSeconds
    } finally {
        if ($null -ne $process) {
            # A throw before the wait returned still releases and stops the child.
            if ($null -eq $wait) {
                $wait = [pscustomobject]@{ Seen = $false; Read = $null; Printed = @(); Detail = $null }
            }
            $run = Complete-GodotRun -Process $process -Wait $wait -Release $release -GuardSeconds $HangGuardSeconds
        }
        if ($null -ne $stderr) {
            $null = $stderr.Wait()
            $stderrText = $stderr.Result
        }
        Remove-TempRoot -Path $root
    }

    if ($null -ne $run) {
        if ($null -ne $wait.Detail) { $failures.Add($wait.Detail) }
        if ($run.Hung) { $failures.Add("hung: $Script had not ended $HangGuardSeconds s after the stub Godot was released") }
        Add-ExpectationFailures -Expect $Expect -Script $Script -Run $run -Stderr $stderrText -Failures $failures
    }
    $note = if ($Expect -eq 'FullExport') {
        "$Script's exit code is not asserted: the stub Godot does not emulate every expected error, so the script " +
            'exits 1 by design'
    } else {
        ''
    }
    return (Write-CaseResult -Case $Case -Failures $failures -Note $note)
}

$checksTail = @'
$ids = 2..69 | ForEach-Object { "C$_" }
foreach ($id in $ids) { [Console]::Out.WriteLine("CHECK $id PASS") }
[Console]::Out.WriteLine('DONE 69/69')
'@

$compareTail = @'
$pairs = [int]([regex]::Match($all, '--expected-pairs=(\d+)').Groups[1].Value)
if ($pairs -ge 2) {
    foreach ($n in 2..$pairs) { [Console]::Out.WriteLine("COMPARE stub$n PASS") }
}
[Console]::Out.WriteLine("DONE $pairs/$pairs")
'@

$exportTail = @'
Set-Content -LiteralPath $args[-1] -Value '' -NoNewline
'@

$checksStub = New-StubGodot -FirstLine 'CHECK C1 PASS' -Tail $checksTail
$compareStub = New-StubGodot -FirstLine 'COMPARE stub1 PASS' -Tail $compareTail
$exportStub = New-StubGodot -FirstLine 'STUB export started' -Tail $exportTail `
    -Only 'if ($all -notmatch ''--export-pack'') { exit 0 }'

$results = @(
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'SceneLinesReachTheOutputWhileGodotRuns' -Script 'checks.ps1' `
        -StubGodot $checksStub -FirstLine 'CHECK C1 PASS' -Expect 'CleanExit')
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'CompareLinesReachTheOutputWhileGodotRuns' -Script 'compare.ps1' `
        -StubGodot $compareStub -FirstLine 'COMPARE stub1 PASS' -Expect 'CleanExit')
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'ExportLinesReachTheOutputWhileGodotRuns' -Script 'export.ps1' `
        -StubGodot $exportStub -FirstLine 'STUB export started' -Expect 'FullExport')
)
exit $(if ($results -contains $false) { 1 } else { 0 })
