#requires -Version 7.3
<#
.SYNOPSIS
Builds the three files a godot-xaml release ships, under .tmp/release/<version>/.

.DESCRIPTION
Checks that the version in its five homes equals -Version: VersionPrefix in Directory.Build.props, version in
godot/addons/godot_xaml/plugin.cfg, the GodotXaml.Core PackageReference in README.md, the godot-xaml-addon-<version>.zip
name in README.md's install steps, and every docs link at the release tag in godot/addons/godot_xaml/AGENTS.md. Then
it deletes and recreates .tmp/release/<version>/, packs GodotXaml.Core and GodotXaml.Cli there, and zips the addon
there as godot-xaml-addon-<version>.zip, with entries under addons/godot_xaml/, dotfiles and .uid files included, and
any bin/ or obj/ left out. The zip's plugin.cfg entry carries the packaged version.

The script exits 0 only when the folder holds exactly the two packages and the zip, and prints their names and sizes.
It tags nothing and publishes nothing.

Without -Local it first refuses a -Version whose tag v<Version> is already on origin, which covers the minutes before
nuget.org lists a pushed version, then asks nuget.org whether GodotXaml.Core or GodotXaml.Cli already has -Version and
refuses to pack when one does, since NuGet keeps the first package it restores under a version in the shared packages
folder. With -Local the packages and the zip carry <Version>-local.<UTC stamp>, under
.tmp/release/<Version>-local.<UTC stamp>/, and both checks are skipped. With -CheckOnly it runs both checks, prints that
<Version> is free to release, and packs nothing.

.PARAMETER Version
The release version, as major.minor.patch.

.PARAMETER Local
Packs <Version>-local.<UTC stamp> for trying an install on this machine, and skips the tag and nuget.org checks.

.PARAMETER CheckOnly
Checks that <Version> is free to release, against the version homes, the tag on origin and nuget.org, and packs nothing.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/pack.log --slot heavy --timeout 900 -- pwsh -NoProfile -File tools/pack-release.ps1 -Version 0.1.0

.EXAMPLE
pwsh -NoProfile -File tools/pack-release.ps1 -Version 0.1.0 -Local

.EXAMPLE
pwsh -NoProfile -File tools/pack-release.ps1 -Version 0.1.1 -CheckOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [switch] $Local,

    [switch] $CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A step's non-zero status is this script's to report, with the step's name, not a bare throw.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$addonFolder = Join-Path $repoRoot 'godot/addons/godot_xaml'

function Get-VersionMismatch {
    param(
        [Parameter(Mandatory)][string] $VersionHome,
        [Parameter(Mandatory)][string] $Pattern,
        [switch] $AllMatches
    )
    $text = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $VersionHome)
    if ($AllMatches) {
        $matches = [regex]::Matches($text, $Pattern)
        if ($matches.Count -eq 0) {
            return "${VersionHome}: no version found (expected to match $Pattern)"
        }
        # Every occurrence must agree with -Version, so a file naming it more than once cannot pass on its first match.
        foreach ($match in $matches) {
            if ($match.Groups[1].Value -ne $Version) {
                return "${VersionHome}: $($match.Groups[1].Value)"
            }
        }
        return $null
    }
    $found = [regex]::Match($text, $Pattern)
    if (-not $found.Success) {
        return "${VersionHome}: no version found (expected to match $Pattern)"
    }
    $value = $found.Groups[1].Value
    if ($value -ne $Version) {
        return "${VersionHome}: $value"
    }
    return $null
}

function Assert-VersionHomes {
    $mismatches = @(
        Get-VersionMismatch -VersionHome 'Directory.Build.props' -Pattern '<VersionPrefix>([^<]*)</VersionPrefix>'
        Get-VersionMismatch -VersionHome 'godot/addons/godot_xaml/plugin.cfg' -Pattern '(?m)^version="([^"]*)"'
        Get-VersionMismatch -VersionHome 'README.md' -Pattern '<PackageReference Include="GodotXaml\.Core" Version="([^"]*)" />'
        Get-VersionMismatch -VersionHome 'README.md' -AllMatches `
            -Pattern 'godot-xaml-addon-(\d+\.\d+\.\d+)\.zip'
        Get-VersionMismatch -VersionHome 'godot/addons/godot_xaml/AGENTS.md' -AllMatches `
            -Pattern 'godot-xaml/blob/v(\d+\.\d+\.\d+)/'
    ) | Where-Object { $_ }
    if ($mismatches) {
        throw "pack-release.ps1: these version homes differ from $Version; set them, then pack again:`n  $($mismatches -join "`n  ")"
    }
}

function Assert-VersionUnpublished {
    # Refuses a version whose tag is on origin: its packages are published or on their way, whatever nuget.org lists.
    $tag = "v$Version"
    $tagLine = @(& git -C $repoRoot ls-remote --tags origin "refs/tags/$tag" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "pack-release.ps1: could not ask origin whether tag $tag is pushed (git ls-remote --tags origin " +
            "refs/tags/$tag exited $LASTEXITCODE): $($tagLine -join ' ')"
    }
    if ($tagLine) {
        throw "pack-release.ps1: tag $tag is already pushed to origin, so the release's packages are published or on " +
            "their way and nuget.org may not list $Version yet; raise the version for a release."
    }
    # Refuses a version that nuget.org already publishes.
    foreach ($id in @('GodotXaml.Core', 'GodotXaml.Cli')) {
        $url = "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/index.json"
        try {
            $published = @((Invoke-RestMethod -Uri $url).versions)
        } catch {
            # A 404 is a package id nuget.org has never published.
            $response = $_.Exception.PSObject.Properties['Response']?.Value
            if ($response -and ([int] $response.StatusCode -eq 404)) {
                $published = @()
            } else {
                throw "pack-release.ps1: could not ask nuget.org whether $id $Version is published ($url): $($_.Exception.Message)"
            }
        }
        if ($published -contains $Version) {
            $refusal = "pack-release.ps1: $id $Version is already on nuget.org, and a local pack of it would take the " +
                "published package's place in the shared NuGet cache; raise the version for a release, or pack with " +
                "-Local to try an install."
            throw $refusal
        }
    }
}

function Invoke-Pack {
    param(
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string] $Output,
        [Parameter(Mandatory)][AllowEmptyString()][string] $VersionSuffix
    )
    $packArguments = @('pack', (Join-Path $repoRoot $Project), '-c', 'Release', '-o', $Output)
    if ($VersionSuffix) {
        $packArguments += @('--version-suffix', $VersionSuffix)
    }
    & dotnet @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "pack-release.ps1: dotnet pack $Project exited $LASTEXITCODE"
    }
}

function New-AddonZip {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $PackageVersion
    )
    # Force lists hidden files; Compress-Archive would skip dotfiles, so the zip is written entry by entry.
    $files = Get-ChildItem -LiteralPath $addonFolder -File -Recurse -Force | Sort-Object FullName
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath($addonFolder, $file.FullName).Replace('\', '/')
            $folders = @($relative.Split('/') | Select-Object -SkipLast 1)
            if (($folders -contains 'bin') -or ($folders -contains 'obj')) {
                continue
            }
            $entryName = "addons/godot_xaml/$relative"
            if ($relative -eq 'plugin.cfg') {
                # The entry carries the packaged version, so a -Local install does not report a release version; every
                # other byte, LF endings included, comes from the file.
                $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($file.FullName))
                $patched = [regex]::Replace($text, '(?m)^version="[^"]*"', "version=`"$PackageVersion`"")
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($patched)
                $stream = $archive.CreateEntry($entryName).Open()
                try {
                    $stream.Write($bytes, 0, $bytes.Length)
                } finally {
                    $stream.Dispose()
                }
                continue
            }
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName)
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-ReleaseFolder {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [Parameter(Mandatory)][string] $PackageVersion
    )
    $expected = @(
        "GodotXaml.Cli.$PackageVersion.nupkg"
        "GodotXaml.Core.$PackageVersion.nupkg"
        "godot-xaml-addon-$PackageVersion.zip"
    ) | Sort-Object
    $files = @(Get-ChildItem -LiteralPath $Folder -Force | Sort-Object Name)
    $actual = @($files | ForEach-Object Name)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "pack-release.ps1: $Folder holds [$($actual -join ', ')], expected exactly [$($expected -join ', ')]"
    }
    foreach ($file in $files) {
        [Console]::Out.WriteLine("$($file.Name) $($file.Length) bytes")
    }
}

# -CheckOnly checks that a version is free to release; -Local packs a throwaway build, so the two contradict.
if ($CheckOnly -and $Local) {
    throw "pack-release.ps1: -CheckOnly checks that $Version is free to release, so it cannot run with -Local, which " +
        "packs $Version-local.<stamp> for trying an install."
}

Assert-VersionHomes

if ($CheckOnly) {
    Assert-VersionUnpublished
    [Console]::Out.WriteLine("pack-release.ps1: $Version is free to release")
    exit 0
}

# A local pack carries <Version>-local.<stamp>; a release pack carries -Version.
$versionSuffix = if ($Local) { "local.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))" } else { '' }
$packageVersion = if ($Local) { "$Version-$versionSuffix" } else { $Version }

if (-not $Local) {
    Assert-VersionUnpublished
}

$folder = Join-Path $repoRoot ".tmp/release/$packageVersion"
if (Test-Path -LiteralPath $folder) {
    Remove-Item -LiteralPath $folder -Recurse
}
[void](New-Item -ItemType Directory -Path $folder)

Invoke-Pack -Project 'src/GodotXaml.Core/GodotXaml.Core.csproj' -Output $folder -VersionSuffix $versionSuffix
Invoke-Pack -Project 'src/GodotXaml.Cli/GodotXaml.Cli.csproj' -Output $folder -VersionSuffix $versionSuffix
New-AddonZip -Path (Join-Path $folder "godot-xaml-addon-$packageVersion.zip") -PackageVersion $packageVersion
Assert-ReleaseFolder -Folder $folder -PackageVersion $packageVersion
