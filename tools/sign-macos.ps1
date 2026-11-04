#requires -Version 7.3

<#
.SYNOPSIS
Signs a macOS executable with the Developer ID Application certificate (-Step Sign), or zips and notarises it
(-Step Notarise), so a downloaded copy is not blocked by Gatekeeper.

.DESCRIPTION
Follows the owner's yaat signing pipeline (yaat's docs/macos-code-signing.md) for a bare executable instead of a .app,
in two steps the release workflow runs with a smoke test of the signed executable between them.

-Step Sign -Exe <path>:
1. Imports the Developer ID Application .p12 into a temporary keychain with a fresh random password, granting
   /usr/bin/codesign alone access to the key (security import -T /usr/bin/codesign, not -A, which the security man page
   calls "Allow any application to access the imported key without warning (insecure, not recommended!)"), and puts
   that keychain first on the user's keychain search list.
2. Finds the identity named by MACOS_SIGN_APP_IDENTITY with security find-identity -v -p codesigning <keychain>, and
   signs with its SHA-1: codesign --force --options runtime --timestamp --keychain <keychain> --sign <SHA-1> <Exe>. Then
   codesign --verify --strict.
3. In a finally block: restores the keychain search list, deletes the temporary keychain and deletes the .p12. Each
   cleanup action that fails writes a warning, so it never hides the error that stopped the run.

-Step Notarise -Exe <path> -Zip <path>:
1. Zips the executable to -Zip with ditto -c -k --keepParent, the layout tools/publish-server.ps1's zip has: the
   executable alone, with its mode.
2. Submits the zip with xcrun notarytool submit --key <.p8> --key-id --issuer --wait --timeout 30m --output-format json.
   Any status but Accepted, or Accepted with a non-zero exit, fails the run, after printing Apple's log through
   notarytool log (a failing notarytool log writes a warning).
3. In a finally block: deletes the .p8, and deletes -Zip when the run failed, so no un-notarised zip is left under the
   release's name. Notarisation does not change the executable, so the zip submitted is the zip that ships.

The ticket is not stapled, because a bare executable cannot hold one. Apple's "Customizing the notarization workflow"
(https://developer.apple.com/documentation/security/customizing-the-notarization-workflow): "Although tickets are
created for standalone binaries, it's not currently possible to staple tickets to them." Gatekeeper finds the ticket
online instead: "After notarization completes, the next time any user attempts to run your app on macOS 10.14 or later,
Gatekeeper finds the ticket online."

The executable is signed with no entitlements. The hardened runtime, which notarisation requires, takes an exception
entitlement only to switch off a protection the code trips; Apple's DTS guidance ("Resolving Hardened Runtime
Incompatibilities", https://developer.apple.com/forums/thread/707743) names generating executable code on the fly as the
case for com.apple.security.cs.allow-jit and warns "Don't apply a hardened runtime exception entitlement without first
understanding what the actual problem is." The four cs.* keys yaat's doc calls "required for any notarized .NET app" are
for a JIT-compiled .NET app; a NativeAOT executable has no JIT (Microsoft Learn, "Native AOT deployment": "Native AOT
apps don't use a just-in-time (JIT) compiler when the application runs").

Each step reads three of the six secrets yaat's doc names for its Application path, from environment variables of the
same names, and fails naming every one that is empty before it runs any tool:
- Sign: MACOS_DEVID_APP_CERT_P12_BASE64 (the .p12, base64), MACOS_DEVID_CERT_PASSWORD (its password) and
  MACOS_SIGN_APP_IDENTITY (the certificate's name).
- Notarise: MACOS_NOTARY_API_KEY_P8_BASE64 (the App Store Connect API key, base64), MACOS_NOTARY_API_KEY_ID and
  MACOS_NOTARY_API_ISSUER_ID.
A value that is not valid base64 fails naming its variable. The decoded .p12 and .p8 go to a working folder under
-TempRoot and, on Linux and macOS, are made mode 0600 before their bytes are written.

The script never prints a value. Every tool's output is printed with each secret value replaced by ***, and a failing
tool is named by its step and exit code, never by its arguments. The certificate password does reach security import's
command line, as -P: the security man page gives -P as the only non-interactive form ("-P passphrase  Specify the
unwrapping passphrase immediately. The default is to obtain a secure passphrase via GUI."), and the key ID and issuer
reach notarytool's. The runner's log does not echo a command line, and GitHub masks the values wherever they are
printed: "GitHub Actions automatically redacts the contents of all GitHub secrets that are printed to workflow logs"
("Using secrets in GitHub Actions").

.PARAMETER Step
Sign or Notarise.

.PARAMETER Exe
The macOS executable to sign or notarise.

.PARAMETER Zip
The zip -Step Notarise writes, submits and leaves in place: the release zip, godot-xaml-ls-<version>-<rid>.zip.

.PARAMETER TempRoot
The folder the script makes its own working folder in, for the keychain, the .p12 and the .p8: RUNNER_TEMP when set,
else the system's temporary folder.

.EXAMPLE
pwsh -NoProfile -File tools/sign-macos.ps1 -Step Sign -Exe .tmp/sign/osx-arm64/GodotXaml.LanguageServer

.EXAMPLE
pwsh -NoProfile -File tools/sign-macos.ps1 -Step Notarise -Exe .tmp/sign/osx-arm64/GodotXaml.LanguageServer
    -Zip .tmp/publish/godot-xaml-ls-0.1.0-osx-arm64.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Sign', 'Notarise')] [string] $Step,
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Zip,
    [string] $TempRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Every native call's exit code is checked by hand, so a non-zero exit is not an error record of its own.
$PSNativeCommandUseErrorActionPreference = $false

if ($Step -eq 'Notarise' -and -not $Zip) {
    throw 'sign-macos: -Step Notarise needs -Zip, the zip to write and submit.'
}

$secretNames = if ($Step -eq 'Sign') {
    @('MACOS_DEVID_APP_CERT_P12_BASE64', 'MACOS_DEVID_CERT_PASSWORD', 'MACOS_SIGN_APP_IDENTITY')
} else {
    @('MACOS_NOTARY_API_KEY_P8_BASE64', 'MACOS_NOTARY_API_KEY_ID', 'MACOS_NOTARY_API_ISSUER_ID')
}
$missing = @($secretNames | Where-Object { -not [Environment]::GetEnvironmentVariable($_) })
if ($missing.Count -gt 0) {
    throw "sign-macos: these environment variables are not set: $($missing -join ', ')."
}
$secret = @{}
foreach ($name in $secretNames) {
    $secret[$name] = [Environment]::GetEnvironmentVariable($name)
}

$exePath = (Resolve-Path -LiteralPath $Exe).Path
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "sign-macos: $exePath is not a file."
}
if (-not $TempRoot) {
    $TempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
}

# A tool's output line with every secret value of this step replaced by ***.
function Protect-Text {
    param([AllowEmptyString()] [string] $Text)
    foreach ($value in $secret.Values) {
        $Text = $Text.Replace($value, '***')
    }
    return $Text
}

# Runs one tool and prints its stdout (unless -Quiet) and stderr with the secrets masked. Returns its exit code and its
# stdout lines. A failure names the step and the exit code, never the arguments, and throws unless -NoThrow.
function Invoke-Tool {
    param(
        [Parameter(Mandatory)] [string] $Step,
        [Parameter(Mandatory)] [string] $Tool,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [switch] $Quiet,
        [switch] $NoThrow
    )
    $all = @(& $Tool @Arguments 2>&1)
    $code = $LASTEXITCODE
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $all) {
        if ($item -is [System.Management.Automation.ErrorRecord]) {
            Write-Host (Protect-Text "$item")
        } else {
            $out.Add("$item")
            if (-not $Quiet) { Write-Host (Protect-Text "$item") }
        }
    }
    if ($code -ne 0 -and -not $NoThrow) {
        throw "sign-macos: $Step failed with exit code $code."
    }
    return [pscustomobject]@{ Exit = $code; Out = $out.ToArray() }
}

# Decodes one base64 secret into a file that, on Linux and macOS, is mode 0600 before its bytes go in.
function Write-SecretFile {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Name)
    try {
        $bytes = [Convert]::FromBase64String($secret[$Name])
    } catch {
        throw "sign-macos: $Name is not valid base64."
    }
    if (-not $IsWindows) {
        [System.IO.File]::WriteAllBytes($Path, [byte[]]@())
        [System.IO.File]::SetUnixFileMode($Path, [System.IO.UnixFileMode]'UserRead, UserWrite')
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

# Runs one cleanup action, turning its failure into a warning so it never hides the error that stopped the run.
function Invoke-Cleanup {
    param([Parameter(Mandatory)] [string] $What, [Parameter(Mandatory)] [scriptblock] $Action)
    try {
        & $Action
    } catch {
        Write-Warning "sign-macos: $What failed: $($_.Exception.Message)"
    }
}

$work = Join-Path $TempRoot ('godot-xaml-sign-' + [guid]::NewGuid().ToString('N'))

function Invoke-Sign {
    $keychain = Join-Path $work 'signing.keychain-db'
    $p12 = Join-Path $work 'devid_app.p12'
    $keychainCreated = $false
    $previousKeychains = @()
    $searchListSet = $false
    try {
        Write-SecretFile -Path $p12 -Name 'MACOS_DEVID_APP_CERT_P12_BASE64'
        # Generated fresh on every run, so it is not a secret.
        $keychainPassword = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24))

        Write-Host 'sign-macos: importing the Developer ID Application certificate into a temporary keychain'
        $keychainCreated = $true
        Invoke-Tool 'security create-keychain' 'security' @('create-keychain', '-p', $keychainPassword, $keychain) | Out-Null
        Invoke-Tool 'security set-keychain-settings' 'security' @('set-keychain-settings', '-lut', '21600', $keychain) | Out-Null
        Invoke-Tool 'security unlock-keychain' 'security' @('unlock-keychain', '-p', $keychainPassword, $keychain) | Out-Null
        Invoke-Tool 'security import' 'security' @(
            'import', $p12, '-P', $secret.MACOS_DEVID_CERT_PASSWORD, '-T', '/usr/bin/codesign', '-t', 'cert', '-f', 'pkcs12',
            '-k', $keychain) | Out-Null
        # Without a partition list codesign raises a password prompt that hangs a headless runner.
        Invoke-Tool 'security set-key-partition-list' 'security' @(
            'set-key-partition-list', '-S', 'apple-tool:,apple:', '-k', $keychainPassword, $keychain) -Quiet | Out-Null
        $listed = (Invoke-Tool 'security list-keychains' 'security' @('list-keychains', '-d', 'user') -Quiet).Out
        $previousKeychains = @($listed | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
        # Set first, so a list-keychains -s that fails part-way is still undone.
        $searchListSet = $true
        Invoke-Tool 'security list-keychains -s' 'security' (
            @('list-keychains', '-d', 'user', '-s', $keychain) + $previousKeychains) | Out-Null

        $identities = (Invoke-Tool 'security find-identity' 'security' @(
                'find-identity', '-v', '-p', 'codesigning', $keychain) -Quiet).Out
        $found = @($identities |
                ForEach-Object { [regex]::Match($_, '^\s*\d+\)\s+([0-9A-Fa-f]{40})\s+"(.*)"\s*$') } |
                Where-Object { $_.Success -and $_.Groups[2].Value -eq $secret.MACOS_SIGN_APP_IDENTITY })
        if ($found.Count -ne 1) {
            throw "sign-macos: the temporary keychain holds $($found.Count) valid codesigning identities named by MACOS_SIGN_APP_IDENTITY, where one is right."
        }
        $sha = $found[0].Groups[1].Value

        Write-Host "sign-macos: signing $exePath"
        Invoke-Tool 'codesign' 'codesign' @(
            '--force', '--options', 'runtime', '--timestamp', '--keychain', $keychain, '--sign', $sha, $exePath) | Out-Null
        Invoke-Tool 'codesign --verify' 'codesign' @('--verify', '--strict', '--verbose=2', $exePath) | Out-Null
        Write-Host "sign-macos: $exePath is signed"
    }
    finally {
        if ($searchListSet -and $previousKeychains.Count -gt 0) {
            Invoke-Cleanup 'restoring the keychain search list' {
                Invoke-Tool 'security list-keychains -s (restore)' 'security' (
                    @('list-keychains', '-d', 'user', '-s') + $previousKeychains) | Out-Null
            }
        }
        if ($keychainCreated) {
            Invoke-Cleanup 'deleting the temporary keychain' {
                Invoke-Tool 'security delete-keychain' 'security' @('delete-keychain', $keychain) | Out-Null
            }
        }
    }
}

function Invoke-Notarise {
    $p8 = Join-Path $work 'notary_key.p8'
    $zipPath = [System.IO.Path]::GetFullPath($Zip, (Get-Location).Path)
    $notarised = $false
    try {
        Write-SecretFile -Path $p8 -Name 'MACOS_NOTARY_API_KEY_P8_BASE64'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $zipPath) | Out-Null
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Invoke-Tool 'ditto' 'ditto' @('-c', '-k', '--keepParent', $exePath, $zipPath) | Out-Null

        Write-Host 'sign-macos: submitting to Apple for notarisation and waiting for the result'
        $notaryAuth = @(
            '--key', $p8, '--key-id', $secret.MACOS_NOTARY_API_KEY_ID, '--issuer', $secret.MACOS_NOTARY_API_ISSUER_ID)
        $submit = Invoke-Tool 'notarytool submit' 'xcrun' (
            @('notarytool', 'submit', $zipPath) + $notaryAuth + @('--wait', '--timeout', '30m', '--output-format', 'json')) -NoThrow
        $result = $null
        try {
            $result = ($submit.Out -join "`n") | ConvertFrom-Json -ErrorAction Stop
        } catch {
            $result = $null
        }
        $status = if ($result -and $result.PSObject.Properties['status']) { "$($result.status)" } else { '' }
        $id = if ($result -and $result.PSObject.Properties['id']) { "$($result.id)" } else { '' }
        if ($status -ne 'Accepted') {
            if ($id) {
                Write-Host "sign-macos: Apple's log for submission ${id}:"
                $log = Invoke-Tool 'notarytool log' 'xcrun' (@('notarytool', 'log', $id) + $notaryAuth) -NoThrow
                if ($log.Exit -ne 0) {
                    Write-Warning "sign-macos: notarytool log failed with exit code $($log.Exit), so Apple's log is not shown."
                }
            }
            $shown = if ($status) { $status } else { 'no status' }
            throw "sign-macos: notarisation returned $shown (notarytool exit code $($submit.Exit)), not Accepted."
        }
        if ($submit.Exit -ne 0) {
            throw "sign-macos: notarytool submit exited $($submit.Exit) after reporting Accepted."
        }
        $notarised = $true
        Write-Host "sign-macos: $zipPath is notarised (submission $id); a bare executable cannot be stapled."
    }
    finally {
        if (-not $notarised -and (Test-Path -LiteralPath $zipPath)) {
            Invoke-Cleanup 'deleting the un-notarised zip' { Remove-Item -LiteralPath $zipPath -Force }
        }
    }
}

try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    if (-not $IsWindows) {
        [System.IO.File]::SetUnixFileMode($work, [System.IO.UnixFileMode]'UserRead, UserWrite, UserExecute')
    }
    if ($Step -eq 'Sign') { Invoke-Sign } else { Invoke-Notarise }
}
finally {
    if (Test-Path -LiteralPath $work) {
        Invoke-Cleanup 'deleting the working folder with the key files' {
            Remove-Item -LiteralPath $work -Recurse -Force
        }
    }
}
