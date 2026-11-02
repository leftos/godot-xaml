#requires -Version 7.3
<#
.SYNOPSIS
Builds the three files a godot-xaml release ships, under .tmp/release/<version>/.

.DESCRIPTION
Checks that the version in its three homes equals -Version: VersionPrefix in Directory.Build.props, version in
godot/addons/godot_xaml/plugin.cfg, and the GodotXaml.Core PackageReference in README.md. Then it deletes and recreates
.tmp/release/<version>/, packs GodotXaml.Core and GodotXaml.Cli there, and zips the addon there as
godot-xaml-addon-<version>.zip, with entries under addons/godot_xaml/, dotfiles and .uid files included, and any bin/ or
obj/ left out.

The script exits 0 only when the folder holds exactly the two packages and the zip, and prints their names and sizes.
It tags nothing and publishes nothing.

.PARAMETER Version
The release version, as major.minor.patch.

.EXAMPLE
pwsh -NoProfile -File tools/gate.ps1 run --log .tmp/pack.log --slot heavy --timeout 900 -- pwsh -NoProfile -File tools/pack-release.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
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
        [Parameter(Mandatory)][string] $Pattern
    )
    $text = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $VersionHome)
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
    ) | Where-Object { $_ }
    if ($mismatches) {
        throw "pack-release.ps1: these version homes differ from $Version; set them, then pack again:`n  $($mismatches -join "`n  ")"
    }
}

function Invoke-Pack {
    param(
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string] $Output
    )
    & dotnet pack (Join-Path $repoRoot $Project) -c Release -o $Output
    if ($LASTEXITCODE -ne 0) {
        throw "pack-release.ps1: dotnet pack $Project exited $LASTEXITCODE"
    }
}

function New-AddonZip {
    param([Parameter(Mandatory)][string] $Path)
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
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "addons/godot_xaml/$relative")
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-ReleaseFolder {
    param([Parameter(Mandatory)][string] $Folder)
    $expected = @("GodotXaml.Cli.$Version.nupkg", "GodotXaml.Core.$Version.nupkg", "godot-xaml-addon-$Version.zip") | Sort-Object
    $files = @(Get-ChildItem -LiteralPath $Folder -Force | Sort-Object Name)
    $actual = @($files | ForEach-Object Name)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "pack-release.ps1: $Folder holds [$($actual -join ', ')], expected exactly [$($expected -join ', ')]"
    }
    foreach ($file in $files) {
        [Console]::Out.WriteLine("$($file.Name) $($file.Length) bytes")
    }
}

Assert-VersionHomes

$folder = Join-Path $repoRoot ".tmp/release/$Version"
if (Test-Path -LiteralPath $folder) {
    Remove-Item -LiteralPath $folder -Recurse
}
[void](New-Item -ItemType Directory -Path $folder)

Invoke-Pack -Project 'src/GodotXaml.Core/GodotXaml.Core.csproj' -Output $folder
Invoke-Pack -Project 'src/GodotXaml.Cli/GodotXaml.Cli.csproj' -Output $folder
New-AddonZip -Path (Join-Path $folder "godot-xaml-addon-$Version.zip")
Assert-ReleaseFolder -Folder $folder
