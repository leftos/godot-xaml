#requires -Version 7.3

<#
.SYNOPSIS
Publishes the language server as a NativeAOT executable for one runtime identifier, smoke-tests it when the identifier is
this machine's, and zips it.

.DESCRIPTION
Runs dotnet publish on src/GodotXaml.LanguageServer with -c Release -r <Rid> -p:PublishAot=true into -Out (deleted and
recreated first), failing when the publish fails or prints any warning. NativeAOT does not cross-compile across
operating systems, so a Windows machine publishes win-x64 and win-arm64, and so on.

-Out must be a folder under .tmp/publish/ of the repository; any other folder is refused, so the script never deletes
anything outside it. The publish folder may hold the executable, its symbols (.pdb, .dbg, .dSYM) and the assemblies'
XML docs; any other file fails the run, since the executable alone would no longer be all a user needs.

When <Rid> is this machine's runtime identifier it runs tools/smoke-server.ps1 on the executable, and writes the zip only
when that passes; for any other identifier it writes the zip and says it was not smoke-tested. The zip is
godot-xaml-ls-<version>-<Rid>.zip beside -Out, holding only the executable (the version is VersionPrefix in
Directory.Build.props; on Linux and macOS the entry keeps the executable bit). It prints the executable's and the zip's
sizes.

.PARAMETER Rid
The runtime identifier: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64 or osx-arm64.

.PARAMETER Out
The publish folder, under .tmp/publish/ of the repository; .tmp/publish/<Rid>/ when not given.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/publish.log --slot heavy --timeout 1500 --
    pwsh -NoProfile -File tools/publish-server.ps1 -Rid win-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Rid,
    [string] $Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $root '.tmp' 'publish'))
if (-not $Out) {
    $Out = Join-Path $publishRoot $Rid
}
$Out = [System.IO.Path]::GetFullPath($Out, (Get-Location).Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$comparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
if (-not $Out.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "publish-server: -Out must be a folder under $publishRoot, not $Out."
}

[xml] $props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { '' }
if (-not $version) {
    throw 'publish-server: Directory.Build.props has no VersionPrefix.'
}

if (Test-Path -LiteralPath $Out) {
    Remove-Item -LiteralPath $Out -Recurse -Force
}

$project = Join-Path $root 'src' 'GodotXaml.LanguageServer' 'GodotXaml.LanguageServer.csproj'
Write-Host "publish-server: publishing $Rid to $Out"
$output = & dotnet publish $project -c Release -r $Rid -p:PublishAot=true -o $Out 2>&1 | ForEach-Object {
    Write-Host $_
    "$_"
}
if ($LASTEXITCODE -ne 0) {
    throw "publish-server: dotnet publish failed with exit code $LASTEXITCODE."
}
# Matches the compiler's "warning CS0000" and ILC's "Trim analysis warning IL0000" alike.
$warnings = @($output | Where-Object { $_ -match '\bwarning\s+[A-Z]+\d+' })
if ($warnings.Count -gt 0) {
    throw "publish-server: the publish printed $($warnings.Count) warning line(s):`n$($warnings -join "`n")"
}

$windowsRid = $Rid.StartsWith('win-')
$exeName = if ($windowsRid) { 'GodotXaml.LanguageServer.exe' } else { 'GodotXaml.LanguageServer' }
$exe = Join-Path $Out $exeName
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "publish-server: the publish made no $exeName in $Out."
}
$extra = @(
    Get-ChildItem -LiteralPath $Out -Force |
        Where-Object { $_.Name -ne $exeName -and $_.Name -notmatch '\.(pdb|dbg|dSYM|xml)$' }
)
if ($extra.Count -gt 0) {
    throw "publish-server: the publish folder holds files besides the executable and its symbols: $($extra.Name -join ', ')"
}
$exeSize = (Get-Item -LiteralPath $exe).Length
Write-Host ('publish-server: {0} {1:N1} MB ({2} bytes)' -f $exe, ($exeSize / 1MB), $exeSize)

$hostRid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
$smoked = $hostRid -eq $Rid
if ($smoked) {
    & (Join-Path $PSScriptRoot 'smoke-server.ps1') -Exe $exe
}

$zip = Join-Path (Split-Path -Parent $Out) "godot-xaml-ls-$version-$Rid.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive, $exe, $exeName, [System.IO.Compression.CompressionLevel]::Optimal)
    if (-not $windowsRid) {
        # A Unix mode in the high 16 bits: a regular file, rwxr-xr-x, so unzip keeps it executable.
        $entry.ExternalAttributes = 0x81ED -shl 16
    }
}
finally {
    $archive.Dispose()
}
$zipSize = (Get-Item -LiteralPath $zip).Length
Write-Host ('publish-server: {0} {1:N1} MB ({2} bytes)' -f $zip, ($zipSize / 1MB), $zipSize)
if (-not $smoked) {
    Write-Host "publish-server: not smoke-tested, this machine is $hostRid and the build is $Rid."
}
