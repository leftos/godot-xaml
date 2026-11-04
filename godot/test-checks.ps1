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
asserted, because a stub that omits the presets' expected errors leaves it exiting 1. Each case gets its own temp root
and runs its script on a copy of the tree that script reads (godot/, tests/fixtures/ and the two tools/ projects, with
the Godot cache and build output left out), then asserts the run left the repo's own godot/cases/, fixture scripts and
.tmp/export/ exactly as they were. The assertion snapshots paths a real run also writes, so a real compare or export
run in the same checkout while the test runs fails a case. A failing case does not stop the later ones; the test prints
CASE <name> PASS|FAIL <detail> per case and exits 0 only when every case passed.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/test-checks.log --slot light --timeout 900 --
  pwsh -NoProfile -File godot/test-checks.ps1
#>
[CmdletBinding()]
param([int] $HangGuardSeconds = 300)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent

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

# Copies a folder tree, leaving out the named folders at any depth: a Godot project's own cache and a build's output.
function Copy-Tree {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Skip
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($Skip -contains $item.Name) {
            continue
        }
        $target = Join-Path $Destination $item.Name
        if ($item.PSIsContainer) {
            Copy-Tree $item.FullName $target $Skip
        }
        else {
            Copy-Item -LiteralPath $item.FullName -Destination $target
        }
    }
}

# Lays out a throwaway copy of the tree the scripts read and returns its godot/ folder. Every path a script uses
# derives from its own $PSScriptRoot, so the copied script relocates the whole run: godot/, the fixtures and the two
# tools/ projects the compare and export runs read.
function New-RepoCopy {
    param([Parameter(Mandatory)][string] $Repo, [Parameter(Mandatory)][string] $Destination)
    $skip = @('.godot', 'bin', 'obj')
    foreach ($relative in @('godot', 'tests/fixtures', 'tools/scene-fixtures', 'tools/export-probe')) {
        $source = Join-Path $Repo $relative
        if (-not (Test-Path -LiteralPath $source)) {
            throw "test-checks.ps1: $source is missing, so the copy cannot run $relative"
        }
        Copy-Tree -Source $source -Destination (Join-Path $Destination $relative) -Skip $skip
    }
    return (Join-Path $Destination 'godot')
}

# The number of CHECK lines the checks scene is expected to print, read from the script under test's own
# $expectedChecks line; $null when the line is not there.
function Get-ExpectedChecks {
    param([Parameter(Mandatory)][string] $ScriptPath)
    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        return $null
    }
    foreach ($line in Get-Content -LiteralPath $ScriptPath) {
        $match = [regex]::Match($line, '^\$expectedChecks\s*=\s*(\d+)\s*$')
        if ($match.Success) {
            return [int]$match.Groups[1].Value
        }
    }
    return $null
}

# One line describing a file a run must not rewrite: its path, size, creation and last-write time, and content hash.
function Get-FileState {
    param([Parameter(Mandatory)][System.IO.FileInfo] $File)
    $hash = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
    return "$($File.FullName) $($File.Length) created $($File.CreationTimeUtc.Ticks) written $($File.LastWriteTimeUtc.Ticks) sha256 $hash"
}

# What a case's run must leave alone in the real tree: each watched folder's own times, every file under the two
# folders a real run rewrites, and the fixture scripts a compare run replaces, each with its path, size, creation and
# last-write time and content hash, or an entry saying it is absent. godot/ is watched for its own times because a
# compare run replaces its fixture scripts in place: copying keeps their last-write time, the content is unchanged, and
# NTFS restores the creation time of a name re-created in the same folder within seconds, so only the folder shows it.
function Get-RealState {
    param([Parameter(Mandatory)][string] $Repo)
    $state = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in @('godot', 'godot/cases', '.tmp/export')) {
        $folder = Join-Path $Repo $relative
        if (-not (Test-Path -LiteralPath $folder)) {
            $state.Add("$folder <absent>")
            continue
        }
        $folderItem = Get-Item -LiteralPath $folder
        $state.Add("$folder <present> created $($folderItem.CreationTimeUtc.Ticks) written $($folderItem.LastWriteTimeUtc.Ticks)")
    }
    foreach ($relative in @('godot/cases', '.tmp/export')) {
        $folder = Join-Path $Repo $relative
        if (-not (Test-Path -LiteralPath $folder)) {
            continue
        }
        foreach ($file in @(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName)) {
            $state.Add((Get-FileState -File $file))
        }
    }
    foreach ($name in @('holder.gd', 'holder.gd.uid', 'quoted.gd', 'quoted.gd.uid', 'tagged.gd', 'tagged.gd.uid')) {
        $file = Join-Path $Repo "godot/$name"
        if (Test-Path -LiteralPath $file) {
            $state.Add((Get-FileState -File (Get-Item -LiteralPath $file)))
        } else {
            $state.Add("$file <absent>")
        }
    }
    return $state
}

# Names the real-tree entries that differ between two states, or returns $null when the run left every one as it was.
function Get-RealStateChange {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Before,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $After
    )
    $changes = @(Compare-Object -ReferenceObject $Before -DifferenceObject $After)
    if ($changes.Count -eq 0) {
        return $null
    }
    return (@($changes | ForEach-Object {
                if ($_.SideIndicator -eq '=>') { "now $($_.InputObject)" } else { "was $($_.InputObject)" }
            }) -join '; ')
}

# Runs one script against its stub Godot and waits for the first line; prints the case's result and returns whether it
# passed.
function Test-LinesReachTheOutputWhileGodotRuns {
    param(
        [Parameter(Mandatory)][string] $Case,
        [Parameter(Mandatory)][string] $Repo,
        [Parameter(Mandatory)][string] $Script,
        [Parameter(Mandatory)][string] $StubGodot,
        [Parameter(Mandatory)][string] $FirstLine,
        [Parameter(Mandatory)][ValidateSet('CleanExit', 'FullExport')][string] $Expect,
        [switch] $ChecksCount
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
    $stateBefore = Get-RealState -Repo $Repo
    try {
        # The script runs on a copy of the tree it reads, so the run cannot rewrite the repo's own files.
        $copyProject = New-RepoCopy -Repo $Repo -Destination (Join-Path $root 'repo')
        if ($ChecksCount) {
            $expected = Get-ExpectedChecks -ScriptPath (Join-Path $copyProject $Script)
            if ($null -eq $expected) {
                $failures.Add("$Script has no '`$expectedChecks = <n>' line, so the stub's CHECK tail cannot be sized")
                $expected = 0
            }
            elseif ($expected -lt 1) {
                $failures.Add("$Script's '`$expectedChecks' line is $expected, below the 1 the stub's CHECK tail needs")
            }
            $StubGodot = $StubGodot.Replace('__EXPECTED_CHECKS__', "$expected")
        }
        New-Item -ItemType Directory -Force $stubs | Out-Null
        # A build that does nothing: each script resolves dotnet from PATH, and this folder comes first.
        Set-Content -LiteralPath (Join-Path $stubs 'dotnet.cmd') -Value "@exit /b 0`r`n" -NoNewline
        Set-Content -LiteralPath (Join-Path $stubs 'godot-stub.ps1') -Value $StubGodot
        $godotCmd = "@pwsh -NoProfile -File `"%~dp0godot-stub.ps1`" %*`r`n@exit /b %ERRORLEVEL%`r`n"
        Set-Content -LiteralPath (Join-Path $stubs 'godot.cmd') -Value $godotCmd -NoNewline

        $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
        foreach ($a in @('-NoProfile', '-File', (Join-Path $copyProject $Script))) { $psi.ArgumentList.Add($a) }
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

    $stateChange = Get-RealStateChange -Before $stateBefore -After (Get-RealState -Repo $Repo)
    if ($null -ne $stateChange) {
        $failures.Add("the run rewrote the real tree (godot/cases/, .tmp/export/, the fixture scripts): $stateChange")
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
$ids = 2..__EXPECTED_CHECKS__ | ForEach-Object { "C$_" }
foreach ($id in $ids) { [Console]::Out.WriteLine("CHECK $id PASS") }
[Console]::Out.WriteLine('DONE __EXPECTED_CHECKS__/__EXPECTED_CHECKS__')
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
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'SceneLinesReachTheOutputWhileGodotRuns' -Repo $repo -Script 'checks.ps1' `
        -StubGodot $checksStub -FirstLine 'CHECK C1 PASS' -Expect 'CleanExit' -ChecksCount)
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'CompareLinesReachTheOutputWhileGodotRuns' -Repo $repo -Script 'compare.ps1' `
        -StubGodot $compareStub -FirstLine 'COMPARE stub1 PASS' -Expect 'CleanExit')
    (Test-LinesReachTheOutputWhileGodotRuns -Case 'ExportLinesReachTheOutputWhileGodotRuns' -Repo $repo -Script 'export.ps1' `
        -StubGodot $exportStub -FirstLine 'STUB export started' -Expect 'FullExport')
)
exit $(if ($results -contains $false) { 1 } else { 0 })
