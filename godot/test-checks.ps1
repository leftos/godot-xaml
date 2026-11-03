#requires -Version 7
<#
.SYNOPSIS
Tests godot/checks.ps1 against a stub Godot and a stub dotnet: each line the scene run writes reaches checks.ps1's own
output while Godot is still running, so the gate's stall watchdog sees the checks progress.

.DESCRIPTION
The stub Godot exits 0 for --import; for the scene run it prints CHECK C1 PASS, then holds until the test writes its
release file, then prints the rest of a passing run and exits 0. The test waits for CHECK C1 PASS on checks.ps1's
stdout, fails at once if checks.ps1 exits first, and only then releases the stub. The one bound is a hang guard: a
passing run never waits it out. Prints CASE <name> PASS|FAIL <detail> and exits 0 only when every case passed.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/test-checks.log --slot light --timeout 900 --
  pwsh -NoProfile -File godot/test-checks.ps1
#>
[CmdletBinding()]
param([int] $HangGuardSeconds = 300)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$checks = Join-Path $PSScriptRoot 'checks.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("gx-test-checks-" + [guid]::NewGuid().ToString('N'))
$stubs = Join-Path $root 'bin'
$release = Join-Path $root 'release'
New-Item -ItemType Directory -Force $stubs | Out-Null

# A build that does nothing: checks.ps1 resolves dotnet from PATH, and this folder comes first.
Set-Content -LiteralPath (Join-Path $stubs 'dotnet.cmd') -Value "@exit /b 0`r`n" -NoNewline
$stubGodot = @'
param()
$all = $args -join ' '
if ($all -match '--import') { exit 0 }
[Console]::Out.WriteLine('CHECK C1 PASS')
[Console]::Out.Flush()
$release = $env:GX_TEST_RELEASE
# Held until the test releases it; the hold's own bound is past the test's hang guard, so only the test ends it.
$deadline = [DateTime]::UtcNow.AddSeconds([int]$env:GX_TEST_HOLD_SECONDS)
while (-not (Test-Path -LiteralPath $release) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
$ids = 2..68 | ForEach-Object { "C$_" }
foreach ($id in $ids) { [Console]::Out.WriteLine("CHECK $id PASS") }
[Console]::Out.WriteLine('DONE 68/68')
exit 0
'@
Set-Content -LiteralPath (Join-Path $stubs 'godot-stub.ps1') -Value $stubGodot
$godotCmd = "@pwsh -NoProfile -File `"%~dp0godot-stub.ps1`" %*`r`n@exit /b %ERRORLEVEL%`r`n"
Set-Content -LiteralPath (Join-Path $stubs 'godot.cmd') -Value $godotCmd -NoNewline

$psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
foreach ($a in @('-NoProfile', '-File', $checks)) { $psi.ArgumentList.Add($a) }
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.Environment['GODOT_PATH'] = Join-Path $stubs 'godot.cmd'
$psi.Environment['GODOT_BIN'] = ''
$psi.Environment['PATH'] = $stubs + [System.IO.Path]::PathSeparator + $env:PATH
$psi.Environment['GX_TEST_RELEASE'] = $release
$psi.Environment['GX_TEST_HOLD_SECONDS'] = [string]($HangGuardSeconds * 2)

$failures = [System.Collections.Generic.List[string]]::new()
$process = [System.Diagnostics.Process]::Start($psi)
$stderr = $process.StandardError.ReadToEndAsync()
try {
    # Waits on the line itself; the guard is the only bound, and the case fails at once when checks.ps1 ends first.
    $guard = [DateTime]::UtcNow.AddSeconds($HangGuardSeconds)
    $seen = $false
    $printed = [System.Collections.Generic.List[string]]::new()
    $read = $process.StandardOutput.ReadLineAsync()
    while (-not $seen) {
        $left = [int][Math]::Max(0, ($guard - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait($left)) {
            $failures.Add((
                "hung: CHECK C1 PASS had not reached checks.ps1's output after $HangGuardSeconds s while " +
                "the stub Godot was still running; it had printed: $($printed -join ' | ')"))
            break
        }
        $line = $read.Result
        if ($null -eq $line) {
            $failures.Add((
                "checks.ps1 ended before CHECK C1 PASS reached its output (exit " +
                "$(if ($process.WaitForExit(5000)) { $process.ExitCode } else { 'unknown' }))"))
            break
        }
        $printed.Add($line)
        if ($line -eq 'CHECK C1 PASS') {
            $seen = $true
            break
        }
        $read = $process.StandardOutput.ReadLineAsync()
    }
} finally {
    New-Item -ItemType File -Force $release | Out-Null
    # The rest of a run is about 1 KB, inside the pipe's buffer, so it is drained after the exit through the read still
    # pending, which keeps the stream to one reader.
    if (-not $process.WaitForExit($HangGuardSeconds * 1000)) {
        $failures.Add("hung: checks.ps1 had not ended $HangGuardSeconds s after the stub Godot was released")
        $process.Kill($true)
        $process.WaitForExit()
    }
    if ($null -ne $read.GetAwaiter().GetResult()) { $null = $process.StandardOutput.ReadToEnd() }
    $null = $stderr.Wait()
}

if ($failures.Count -eq 0 -and $process.ExitCode -ne 0) {
    $failures.Add("checks.ps1 exited $($process.ExitCode) for a stub run where every check passed: $($stderr.Result)")
}

Remove-Item -LiteralPath $root -Recurse -Force
$name = 'SceneLinesReachTheOutputWhileGodotRuns'
if ($failures.Count -eq 0) {
    [Console]::Out.WriteLine("CASE $name PASS")
    exit 0
}

[Console]::Out.WriteLine("CASE $name FAIL $($failures -join '; ')")
exit 1
