#requires -Version 7.3
<#
.SYNOPSIS
Tests tools/sign-macos.ps1's Sign and Notarise steps against stub codesign, xcrun, security and ditto commands, on
Windows.

.DESCRIPTION
Runs on Windows only: each stub is a .cmd shim, first on a temporary PATH, that runs one PowerShell stub, and a .cmd
shim resolves by bare name only on Windows. The stub appends the tool's name and arguments, as one JSON line, to a call
log, echoes its arguments to stderr (so a script that relays a tool's output unredacted leaks the secrets on its command
line, and the no-secret check catches it), and plays the tool's part for the case:
- GX_STUB_FAIL names one call that exits 1: codesign-sign (codesign's signing call), security-import or
  notarytool-log.
- codesign --verify exits GX_STUB_VERIFY_EXIT.
- xcrun notarytool submit prints a JSON submission whose status is GX_STUB_NOTARY_STATUS, or plain text when that is
  <nojson>, and exits GX_STUB_NOTARY_EXIT.
- security list-keychains prints one keychain, security find-identity prints two identities, one of them named by
  MACOS_SIGN_APP_IDENTITY, and ditto writes the zip it is asked for.

Every case gets its own temporary root, used as sign-macos.ps1's -TempRoot, and only the made-up secrets of the step it
runs, so a step that reads the other step's secrets fails.

The cases:
- SignAllToolsSucceed: the Sign calls come in order; security import grants -T /usr/bin/codesign and not -A; codesign
  signs with the identity's SHA-1 and --keychain names the keychain the run created; the search list is restored.
- NotariseAllToolsSucceed: ditto writes -Zip, notarytool submit sends that zip with --wait, --timeout 30m and
  --output-format json, and the zip is left in place.
- SignMissingSecretFailsBeforeAnyTool: one Sign secret is empty, the script fails naming it and calls no tool.
- NotariseTwoMissingSecretsBothNamed: two Notarise secrets are empty, the failure names both and no tool runs.
- SignFailedVerifyFails: codesign --verify fails, the script fails.
- SignFailedCodesignFails: codesign's signing call fails, the script fails before the verify.
- SignFailedImportFails: security import fails, the script fails before codesign.
- InvalidNotarisationFetchesTheLog: notarytool reports Invalid, the script fails with "notarisation returned Invalid"
  and calls notarytool log for the submission.
- AcceptedWithNonZeroExitFails: notarytool reports Accepted but exits 1, the script fails.
- NoJsonFails: notarytool prints no JSON, the script fails with "notarisation returned no status".

Every case asserts that the run did not hang (a run past -GuardSeconds is killed and fails as a hang), that the
temporary root is empty after the run, that no secret value, encoded or decoded, appears in the script's output, and
that a run that created a keychain deleted it once. Every failing Sign case after the search list was set asserts that
the list was restored before the keychain was deleted. The test prints CASE <name> PASS|FAIL <detail> per case and
exits 0 only when every case passed.

.PARAMETER GuardSeconds
How long one run of sign-macos.ps1 may take before it is killed and its case fails as a hang.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/test-sign.log --slot light --timeout 900 --
  pwsh -NoProfile -File tools/test-sign-macos.ps1
#>
[CmdletBinding()]
param([int] $GuardSeconds = 600)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'test-sign-macos.ps1 runs on Windows only: its stub tools are .cmd shims.'
}

$signScript = Join-Path $PSScriptRoot 'sign-macos.ps1'

$p12Content = 'fake-p12-content-SECRETP12'
$p8Content = 'fake-p8-content-SECRETP8'
$secrets = [ordered]@{
    MACOS_DEVID_APP_CERT_P12_BASE64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($p12Content))
    MACOS_DEVID_CERT_PASSWORD = 'pw-SECRETPASSWORD-41'
    MACOS_SIGN_APP_IDENTITY = 'Developer ID Application: Secret Tester (SECRETTEAM)'
    MACOS_NOTARY_API_KEY_P8_BASE64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($p8Content))
    MACOS_NOTARY_API_KEY_ID = 'SECRETKEY9'
    MACOS_NOTARY_API_ISSUER_ID = '5ec2e7aa-0000-4000-8000-5ec2e7155ae0'
}
$stepSecrets = @{
    Sign = @('MACOS_DEVID_APP_CERT_P12_BASE64', 'MACOS_DEVID_CERT_PASSWORD', 'MACOS_SIGN_APP_IDENTITY')
    Notarise = @('MACOS_NOTARY_API_KEY_P8_BASE64', 'MACOS_NOTARY_API_KEY_ID', 'MACOS_NOTARY_API_ISSUER_ID')
}
$secretTexts = @($secrets.Values) + @($p12Content, $p8Content)
$identitySha = '0123456789ABCDEF0123456789ABCDEF01234567'
$loginKeychain = '/Users/stub/Library/Keychains/login.keychain-db'

# The one stub every shim runs: it logs the call, echoes it to stderr and plays the tool's part for the case.
$stubBody = @'
$tool = $args[0]
$rest = @($args | Select-Object -Skip 1)
Add-Content -LiteralPath $env:GX_STUB_LOG -Value ([ordered]@{ tool = $tool; args = $rest } | ConvertTo-Json -Compress -Depth 4)
[Console]::Error.WriteLine("stub $tool $($rest -join ' ')")
$call = $tool
if ($rest.Count -ge 1) { $call += '-' + $rest[0] }
if ($tool -eq 'xcrun' -and $rest.Count -ge 2) { $call = 'notarytool-' + $rest[1] }
if ($tool -eq 'codesign') { $call = if ($rest -contains '--verify') { 'codesign-verify' } else { 'codesign-sign' } }
if ($env:GX_STUB_FAIL -and $env:GX_STUB_FAIL -eq $call) { exit 1 }
switch ($call) {
    'codesign-verify' { exit [int]$env:GX_STUB_VERIFY_EXIT }
    'notarytool-submit' {
        if ($env:GX_STUB_NOTARY_STATUS -eq '<nojson>') {
            [Console]::Out.WriteLine('Error: HTTP status code: 401. Unable to authenticate.')
        } else {
            [Console]::Out.WriteLine('{"id":"stub-submission-1","status":"' + $env:GX_STUB_NOTARY_STATUS + '","message":"Processing complete"}')
        }
        exit [int]$env:GX_STUB_NOTARY_EXIT
    }
    'notarytool-log' { [Console]::Out.WriteLine('{"status":"Invalid","issues":[{"message":"stub issue"}]}') }
    'security-list-keychains' {
        if ($rest -notcontains '-s') { [Console]::Out.WriteLine('    "/Users/stub/Library/Keychains/login.keychain-db"') }
    }
    'security-find-identity' {
        [Console]::Out.WriteLine('  1) FEDCBA9876543210FEDCBA9876543210FEDCBA98 "Apple Development: Someone Else (OTHERTEAM)"')
        [Console]::Out.WriteLine('  2) 0123456789ABCDEF0123456789ABCDEF01234567 "' + $env:MACOS_SIGN_APP_IDENTITY + '"')
        [Console]::Out.WriteLine('     2 valid identities found')
    }
    'ditto--c' { Set-Content -LiteralPath $rest[-1] -Value 'zip' -NoNewline }
}
exit 0
'@

# Writes the stub and one .cmd shim per tool into a folder that goes first on the case's PATH.
function New-StubFolder {
    param([Parameter(Mandatory)][string] $Path)
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Content -LiteralPath (Join-Path $Path 'stub.ps1') -Value $stubBody
    foreach ($tool in @('codesign', 'xcrun', 'security', 'ditto')) {
        $shim = "@pwsh -NoProfile -File `"%~dp0stub.ps1`" $tool %*`r`n@exit /b %ERRORLEVEL%`r`n"
        Set-Content -LiteralPath (Join-Path $Path "$tool.cmd") -Value $shim -NoNewline
    }
}

# Runs a script in a child pwsh with the stub folder first on PATH and the given environment; returns its exit code, or
# 'hung' when it ran past -GuardSeconds, and its stdout and stderr together.
function Invoke-Child {
    param(
        [Parameter(Mandatory)][string] $Script,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Stubs,
        [Parameter(Mandatory)][hashtable] $Environment
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    foreach ($a in @('-NoProfile', '-File', $Script) + $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.Environment['PATH'] = $Stubs + [System.IO.Path]::PathSeparator + $env:PATH
    foreach ($name in $secrets.Keys) { $psi.Environment[$name] = '' }
    foreach ($key in $Environment.Keys) { $psi.Environment[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($GuardSeconds * 1000)) {
        $process.Kill($true)
        $null = $process.WaitForExit()
        return [pscustomobject]@{ Exit = 'hung'; Text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    }
    $process.WaitForExit()
    return [pscustomobject]@{
        Exit = $process.ExitCode
        Text = $stdout.GetAwaiter().GetResult() + "`n" + $stderr.GetAwaiter().GetResult()
    }
}

# The calls the stubs logged, oldest first.
function Get-Calls {
    param([Parameter(Mandatory)][string] $Log)
    if (-not (Test-Path -LiteralPath $Log)) { return @() }
    return @(Get-Content -LiteralPath $Log | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
}

# A short name for a call: the tool and its subcommand, with -s marking a list-keychains call that sets the list.
function Get-CallName {
    param([Parameter(Mandatory)] $Call)
    $callArgs = @($Call.args)
    $name = $Call.tool
    if ($callArgs.Count -ge 1) { $name += " $($callArgs[0])" }
    if ($Call.tool -eq 'xcrun' -and $callArgs.Count -ge 2) { $name += " $($callArgs[1])" }
    if ($Call.tool -eq 'security' -and $callArgs.Count -ge 1 -and $callArgs[0] -eq 'list-keychains' -and $callArgs -contains '-s') {
        $name += ' -s'
    }
    return $name
}

# The value after a flag in an argument list, or $null when the flag is absent.
function Get-FlagValue {
    param([Parameter(Mandatory)][AllowEmptyCollection()] $Arguments, [Parameter(Mandatory)][string] $Flag)
    $at = [array]::IndexOf(@($Arguments), $Flag)
    if ($at -lt 0 -or $at + 1 -ge @($Arguments).Count) { return $null }
    return @($Arguments)[$at + 1]
}

function Remove-TempRoot {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    } catch {
        [Console]::Error.WriteLine("test-sign-macos.ps1: could not remove ${Path}: $($_.Exception.Message)")
    }
}

function Write-CaseResult {
    param([Parameter(Mandatory)][string] $Case, [Parameter(Mandatory)] $Failures)
    if ($Failures.Count -eq 0) {
        [Console]::Out.WriteLine("CASE $Case PASS")
        return $true
    }
    [Console]::Out.WriteLine("CASE $Case FAIL $($Failures -join '; ')")
    return $false
}

# The assertions every case shares: no hang, the temporary root is empty, no secret reached the output, and a run that
# created a keychain deleted the same one, once.
function Add-CommonFailures {
    param(
        [Parameter(Mandatory)] $Run,
        [Parameter(Mandatory)][AllowEmptyCollection()] $Calls,
        [Parameter(Mandatory)][string] $TempRoot,
        [Parameter(Mandatory)] $Failures
    )
    if ($Run.Exit -eq 'hung') {
        $Failures.Add("sign-macos.ps1 hung: it was still running after $GuardSeconds s and was killed")
    }
    $left = @(Get-ChildItem -LiteralPath $TempRoot -Force -Recurse -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        $Failures.Add("the run left files in its temporary root: $($left.FullName -join ', ')")
    }
    foreach ($secret in $secretTexts) {
        if ($Run.Text.Contains($secret)) {
            $Failures.Add("the output holds a secret value ($($secret.Substring(0, 6))...)")
        }
    }
    $created = @($Calls | Where-Object { $_.tool -eq 'security' -and @($_.args)[0] -eq 'create-keychain' })
    if ($created.Count -gt 0) {
        $keychain = @($created[0].args)[-1]
        $deleted = @($Calls | Where-Object {
                $_.tool -eq 'security' -and @($_.args)[0] -eq 'delete-keychain' -and @($_.args)[-1] -eq $keychain
            })
        if ($deleted.Count -ne 1) {
            $Failures.Add("the keychain $keychain was deleted $($deleted.Count) time(s) where once is right")
        }
        if (-not $keychain.StartsWith($TempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $Failures.Add("the keychain $keychain is not under the temporary root $TempRoot")
        }
    }
}

# A failing run: it exited non-zero (a hang is reported by Add-CommonFailures, not counted as a failure here) and its
# output holds the expected error text.
function Add-FailedRunFailures {
    param([Parameter(Mandatory)] $Run, [Parameter(Mandatory)][string] $Expected, [Parameter(Mandatory)] $Failures)
    if ($Run.Exit -eq 'hung') { return }
    if ($Run.Exit -eq 0) { $Failures.Add('sign-macos.ps1 exited 0') }
    if (-not $Run.Text.Contains($Expected)) { $Failures.Add("the output lacks '$Expected': $($Run.Text)") }
}

# A Sign run that failed after the search list was set: the list was restored, and before the keychain was deleted.
function Add-RestoreFailures {
    param([Parameter(Mandatory)][AllowEmptyCollection()] $Calls, [Parameter(Mandatory)] $Failures)
    $names = @($Calls | ForEach-Object { Get-CallName $_ })
    $set = [array]::IndexOf($names, 'security list-keychains -s')
    $restore = [array]::LastIndexOf($names, 'security list-keychains -s')
    $delete = [array]::IndexOf($names, 'security delete-keychain')
    if ($set -lt 0 -or $restore -le $set) {
        $Failures.Add("the search list was not restored: [$($names -join ' | ')]")
    } elseif ($delete -lt 0 -or $delete -lt $restore) {
        $Failures.Add("the keychain was not deleted after the search list was restored: [$($names -join ' | ')]")
    } elseif (@($Calls[$restore].args)[-1] -ne $loginKeychain) {
        $Failures.Add("the search list was restored to [$(@($Calls[$restore].args) -join ' ')], not to the list it held")
    }
}

# Runs one step of sign-macos.ps1 for one case and hands its run, calls and paths to the case's own assertions.
function Invoke-SignCase {
    param(
        [Parameter(Mandatory)][string] $Case,
        [Parameter(Mandatory)][ValidateSet('Sign', 'Notarise')][string] $Step,
        [Parameter(Mandatory)][scriptblock] $Assert,
        [string[]] $MissingSecrets = @(),
        [string] $Fail = '',
        [int] $VerifyExit = 0,
        [string] $NotaryStatus = 'Accepted',
        [int] $NotaryExit = 0
    )
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('gx-test-sign-' + [guid]::NewGuid().ToString('N'))
    $failures = [System.Collections.Generic.List[string]]::new()
    try {
        $stubs = Join-Path $root 'bin'
        $tempRoot = Join-Path $root 'temp'
        $log = Join-Path $root 'calls.jsonl'
        New-StubFolder -Path $stubs
        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        $exe = Join-Path $root 'GodotXaml.LanguageServer'
        Set-Content -LiteralPath $exe -Value 'exe' -NoNewline
        $zip = Join-Path $root 'out' 'godot-xaml-ls-9.8.7-osx-arm64.zip'
        $environment = @{
            GX_STUB_LOG = $log
            GX_STUB_FAIL = $Fail
            GX_STUB_VERIFY_EXIT = "$VerifyExit"
            GX_STUB_NOTARY_STATUS = $NotaryStatus
            GX_STUB_NOTARY_EXIT = "$NotaryExit"
        }
        foreach ($name in $stepSecrets[$Step]) {
            if ($MissingSecrets -notcontains $name) { $environment[$name] = $secrets[$name] }
        }
        $arguments = @('-Step', $Step, '-Exe', $exe, '-TempRoot', $tempRoot)
        if ($Step -eq 'Notarise') { $arguments += @('-Zip', $zip) }
        $run = Invoke-Child -Script $signScript -Arguments $arguments -Stubs $stubs -Environment $environment
        $calls = @(Get-Calls -Log $log)
        Add-CommonFailures -Run $run -Calls $calls -TempRoot $tempRoot -Failures $failures
        & $Assert $run $calls ([pscustomobject]@{ Exe = $exe; Zip = $zip; TempRoot = $tempRoot }) $failures
    } catch {
        $failures.Add("the case threw: $($_.Exception.Message)")
    } finally {
        Remove-TempRoot -Path $root
    }
    return (Write-CaseResult -Case $Case -Failures $failures)
}

$signOrder = @(
    'security create-keychain'
    'security set-keychain-settings'
    'security unlock-keychain'
    'security import'
    'security set-key-partition-list'
    'security list-keychains'
    'security list-keychains -s'
    'security find-identity'
    'codesign --force'
    'codesign --verify'
    'security list-keychains -s'
    'security delete-keychain'
)

$signAllSucceed = {
    param($run, $calls, $paths, $failures)
    if ($run.Exit -ne 0) { $failures.Add("sign-macos.ps1 -Step Sign exited $($run.Exit) where every tool succeeded: $($run.Text)") }
    $names = @($calls | ForEach-Object { Get-CallName $_ })
    if (($names -join ' | ') -ne ($signOrder -join ' | ')) {
        $failures.Add("the calls were [$($names -join ' | ')] where [$($signOrder -join ' | ')] is the order")
        return
    }
    $keychain = @($calls[0].args)[-1]
    $import = @($calls[3].args)
    if ($import[1] -notlike '*.p12' -or -not $import[1].StartsWith($paths.TempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("security import takes $($import[1]), not a .p12 under the temporary root")
    }
    if ((Get-FlagValue $import '-T') -ne '/usr/bin/codesign') { $failures.Add('security import does not grant -T /usr/bin/codesign') }
    if ($import -contains '-A') { $failures.Add('security import grants -A, every application') }
    if ((Get-FlagValue $import '-k') -ne $keychain) { $failures.Add('security import does not import into the created keychain') }
    $find = @($calls[7].args)
    if ($find -notcontains '-v' -or (Get-FlagValue $find '-p') -ne 'codesigning' -or $find[-1] -ne $keychain) {
        $failures.Add("security find-identity is not -v -p codesigning on the created keychain: [$($find -join ' ')]")
    }
    $sign = @($calls[8].args)
    foreach ($w in @('--force', '--options', 'runtime', '--timestamp')) {
        if ($sign -notcontains $w) { $failures.Add("codesign's signing call lacks $w") }
    }
    if ((Get-FlagValue $sign '--sign') -ne $identitySha) {
        $failures.Add("codesign signs with $(Get-FlagValue $sign '--sign'), not the identity's SHA-1 $identitySha")
    }
    if ((Get-FlagValue $sign '--keychain') -ne $keychain) {
        $failures.Add("codesign's --keychain is $(Get-FlagValue $sign '--keychain'), not the created keychain $keychain")
    }
    if ($sign[-1] -ne $paths.Exe) { $failures.Add("codesign signs $($sign[-1]) where $($paths.Exe) is the executable") }
    $verify = @($calls[9].args)
    if ($verify -notcontains '--strict' -or $verify[-1] -ne $paths.Exe) { $failures.Add("codesign --verify is not strict on $($paths.Exe)") }
    if (@($calls[6].args)[4] -ne $keychain) { $failures.Add('the search list does not put the created keychain first') }
    Add-RestoreFailures -Calls $calls -Failures $failures
}

$notariseAllSucceed = {
    param($run, $calls, $paths, $failures)
    if ($run.Exit -ne 0) { $failures.Add("sign-macos.ps1 -Step Notarise exited $($run.Exit) where every tool succeeded: $($run.Text)") }
    $names = @($calls | ForEach-Object { Get-CallName $_ })
    if (($names -join ' | ') -ne 'ditto -c | xcrun notarytool submit') {
        $failures.Add("the calls were [$($names -join ' | ')] where [ditto -c | xcrun notarytool submit] is the order")
        return
    }
    $ditto = @($calls[0].args)
    foreach ($w in @('-c', '-k', '--keepParent')) {
        if ($ditto -notcontains $w) { $failures.Add("ditto lacks $w") }
    }
    if ($ditto[-2] -ne $paths.Exe) { $failures.Add("ditto zips $($ditto[-2]) where $($paths.Exe) is the executable") }
    if ($ditto[-1] -ne $paths.Zip) { $failures.Add("ditto writes $($ditto[-1]) where -Zip is $($paths.Zip)") }
    $submit = @($calls[1].args)
    if ($submit[2] -ne $paths.Zip) { $failures.Add("notarytool submits $($submit[2]) where the zip is $($paths.Zip)") }
    foreach ($pair in @(@('--key-id', $secrets.MACOS_NOTARY_API_KEY_ID), @('--issuer', $secrets.MACOS_NOTARY_API_ISSUER_ID))) {
        if ((Get-FlagValue $submit $pair[0]) -ne $pair[1]) { $failures.Add("notarytool submit lacks $($pair[0]) <its secret>") }
    }
    $key = Get-FlagValue $submit '--key'
    if (-not $key -or -not $key.StartsWith($paths.TempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add('notarytool submit does not take a --key file under the temporary root')
    }
    if ($submit -notcontains '--wait') { $failures.Add('notarytool submit does not --wait') }
    if ((Get-FlagValue $submit '--timeout') -ne '30m') { $failures.Add('notarytool submit does not pass --timeout 30m') }
    if ((Get-FlagValue $submit '--output-format') -ne 'json') { $failures.Add('notarytool submit does not pass --output-format json') }
    if (-not (Test-Path -LiteralPath $paths.Zip)) { $failures.Add("the notarised zip $($paths.Zip) is gone") }
}

$noToolCalled = {
    param($calls, $failures)
    if ($calls.Count -gt 0) {
        $failures.Add("a tool was called with a secret missing: $(@($calls | ForEach-Object { Get-CallName $_ }) -join ' | ')")
    }
}

$signMissingSecret = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'MACOS_DEVID_CERT_PASSWORD' -Failures $failures
    & $noToolCalled $calls $failures
}

$notariseTwoMissing = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'MACOS_NOTARY_API_KEY_ID' -Failures $failures
    Add-FailedRunFailures -Run $run -Expected 'MACOS_NOTARY_API_ISSUER_ID' -Failures $failures
    & $noToolCalled $calls $failures
}

$signFailedVerify = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'codesign --verify failed' -Failures $failures
    Add-RestoreFailures -Calls $calls -Failures $failures
}

$signFailedCodesign = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'codesign failed' -Failures $failures
    if (@($calls | Where-Object { (Get-CallName $_) -eq 'codesign --verify' }).Count -gt 0) {
        $failures.Add('the script verified after the signing call failed')
    }
    Add-RestoreFailures -Calls $calls -Failures $failures
}

$signFailedImport = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'security import failed' -Failures $failures
    if (@($calls | Where-Object { $_.tool -eq 'codesign' }).Count -gt 0) {
        $failures.Add('the script called codesign after security import failed')
    }
    if (@($calls | Where-Object { (Get-CallName $_) -eq 'security delete-keychain' }).Count -ne 1) {
        $failures.Add('the keychain was not deleted after security import failed')
    }
}

$invalidNotarisation = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'notarisation returned Invalid' -Failures $failures
    $logCalls = @($calls | Where-Object { (Get-CallName $_) -eq 'xcrun notarytool log' })
    if ($logCalls.Count -ne 1) {
        $failures.Add("notarytool log was called $($logCalls.Count) time(s) where once is right")
    } elseif (@($logCalls[0].args)[2] -ne 'stub-submission-1') {
        $failures.Add("notarytool log asked for $(@($logCalls[0].args)[2]), not the submission stub-submission-1")
    }
}

$acceptedNonZero = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'exited 1 after reporting Accepted' -Failures $failures
}

$noJson = {
    param($run, $calls, $paths, $failures)
    Add-FailedRunFailures -Run $run -Expected 'notarisation returned no status' -Failures $failures
}

$results = @(
    (Invoke-SignCase -Case 'SignAllToolsSucceed' -Step Sign -Assert $signAllSucceed)
    (Invoke-SignCase -Case 'NotariseAllToolsSucceed' -Step Notarise -Assert $notariseAllSucceed)
    (Invoke-SignCase -Case 'SignMissingSecretFailsBeforeAnyTool' -Step Sign -Assert $signMissingSecret `
        -MissingSecrets @('MACOS_DEVID_CERT_PASSWORD'))
    (Invoke-SignCase -Case 'NotariseTwoMissingSecretsBothNamed' -Step Notarise -Assert $notariseTwoMissing `
        -MissingSecrets @('MACOS_NOTARY_API_KEY_ID', 'MACOS_NOTARY_API_ISSUER_ID'))
    (Invoke-SignCase -Case 'SignFailedVerifyFails' -Step Sign -Assert $signFailedVerify -VerifyExit 1)
    (Invoke-SignCase -Case 'SignFailedCodesignFails' -Step Sign -Assert $signFailedCodesign -Fail 'codesign-sign')
    (Invoke-SignCase -Case 'SignFailedImportFails' -Step Sign -Assert $signFailedImport -Fail 'security-import')
    (Invoke-SignCase -Case 'InvalidNotarisationFetchesTheLog' -Step Notarise -Assert $invalidNotarisation -NotaryStatus 'Invalid')
    (Invoke-SignCase -Case 'AcceptedWithNonZeroExitFails' -Step Notarise -Assert $acceptedNonZero -NotaryExit 1)
    (Invoke-SignCase -Case 'NoJsonFails' -Step Notarise -Assert $noJson -NotaryStatus '<nojson>')
)
exit $(if ($results -contains $false) { 1 } else { 0 })
