#requires -Version 7.3

<#
.SYNOPSIS
Installs the godot-xaml Claude Code skill user-level, at <SkillsRoot>\godot-xaml.

.DESCRIPTION
Copies the skill folder into <InstallRoot>\versions\<Version>\skill and links that copy to <SkillsRoot>\godot-xaml as a
directory junction, so every repo on the machine reads the one copy, and the copy keeps the version it was installed at.

The skill ships without the repo around it, so each Markdown file is checked and rewritten first. A file holding an
absolute Windows drive path is refused, with its file and line, and nothing is installed. A Markdown link to a file
outside the skill folder is rewritten to its page at https://github.com/leftos/godot-xaml/blob/v<Version>/, its #anchor
kept, and a link that resolves outside the repo is refused. A link between the skill's own files, an http://, https://
or mailto: URL, and a #anchor are left as they are.

Every file is written UTF-8 without a BOM and with LF line endings. The junction is created when missing, left alone
when it already points at the new copy, replaced when it is a junction pointing elsewhere, and refused when it is
anything else: a real folder is never deleted.

.PARAMETER Version
The release version the rewritten links point at, as major.minor.patch.

.PARAMETER Source
The skill folder to install, agents/skill/godot-xaml in this checkout.

.PARAMETER InstallRoot
The folder the versioned copies go under, %LOCALAPPDATA%\godot-xaml.

.PARAMETER SkillsRoot
The folder the skill is linked into, ~/.claude/skills.

.EXAMPLE
pwsh -NoProfile -File tools/install-skill.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [string] $Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'agents/skill/godot-xaml'),
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'godot-xaml'),
    [string] $SkillsRoot = (Join-Path $HOME '.claude/skills')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$blobBase = "https://github.com/leftos/godot-xaml/blob/v$Version"
# An absolute Windows drive path: a letter, a colon and a slash not preceded by a letter or digit, so an https:// URL and
# a res:// path are not hits.
$absolutePathPattern = '(?i)(?<![A-Za-z0-9])[A-Z]:[\\/]'
$linkPattern = '\]\(([^)]+)\)'

# A path made absolute against a base folder, without a trailing separator.
function Get-FullFolder {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Base
    )
    return [System.IO.Path]::GetFullPath($Path, $Base).TrimEnd('\', '/')
}

# Whether $Path is strictly inside $Folder.
function Test-InsideFolder {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Folder
    )
    return $Path.StartsWith($Folder + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

# Every file under the skill: its path relative to the skill, its full path, and its text when it is Markdown, else $null.
function Get-ShippedFile {
    param([Parameter(Mandatory)] [string] $SourcePath)
    $files = foreach ($file in Get-ChildItem -LiteralPath $SourcePath -File -Recurse | Sort-Object FullName) {
        $relative = [System.IO.Path]::GetRelativePath($SourcePath, $file.FullName).Replace('\', '/')
        $text = if ($file.Extension -eq '.md') { [System.IO.File]::ReadAllText($file.FullName) } else { $null }
        [pscustomobject]@{ Path = $relative; FullName = $file.FullName; Text = $text }
    }
    return @($files)
}

# One line for the first absolute drive path in each Markdown file that holds one, or nothing when every file is clean.
function Get-AbsolutePathRefusal {
    param([Parameter(Mandatory)] [object[]] $Files)
    $refusals = @()
    foreach ($file in $Files) {
        if ($null -eq $file.Text) {
            continue
        }
        $lines = $file.Text -split "`r?`n"
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $match = [regex]::Match($lines[$index], $absolutePathPattern)
            if (-not $match.Success) {
                continue
            }
            $refusals += "$($file.Path):$($index + 1): an absolute path ($($match.Value)) " +
                'would ship with the skill; name it relatively or by URL.'
            break
        }
    }
    return $refusals
}

# One link target rewritten for the shipped copy: kept as it is when it is a URL, an anchor, or a file of the skill's
# own, else pointed at the file's page on GitHub at the release tag, its anchor kept.
function Convert-LinkTarget {
    param(
        [Parameter(Mandatory)] [string] $Target,
        [Parameter(Mandatory)] [string] $Folder,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [int] $LineNumber
    )
    if ($Target -match '^(?i)(https?://|mailto:)' -or $Target.StartsWith('#')) {
        return $Target
    }
    $anchor = ''
    $hash = $Target.IndexOf('#')
    if ($hash -ge 0) {
        $anchor = $Target.Substring($hash)
        $Target = $Target.Substring(0, $hash)
    }
    $resolved = [System.IO.Path]::GetFullPath($Target, $Folder)
    if (Test-InsideFolder -Path $resolved -Folder $SourcePath) {
        return "$Target$anchor"
    }
    if (-not (Test-InsideFolder -Path $resolved -Folder $repoRoot)) {
        throw "install-skill.ps1: ${RelativePath}:$LineNumber links to $Target, which is outside this repo."
    }
    $relative = [System.IO.Path]::GetRelativePath($repoRoot, $resolved).Replace('\', '/')
    return "$blobBase/$relative$anchor"
}

# One Markdown line with every link target rewritten for the shipped copy.
function Convert-MarkdownLine {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Line,
        [Parameter(Mandatory)] [string] $Folder,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [int] $LineNumber
    )
    $result = [System.Text.StringBuilder]::new()
    $at = 0
    foreach ($match in [regex]::Matches($Line, $linkPattern)) {
        [void]$result.Append($Line.Substring($at, $match.Index - $at))
        $target = Convert-LinkTarget -Target $match.Groups[1].Value -Folder $Folder -SourcePath $SourcePath `
            -RelativePath $RelativePath -LineNumber $LineNumber
        [void]$result.Append("]($target)")
        $at = $match.Index + $match.Length
    }
    [void]$result.Append($Line.Substring($at))
    return $result.ToString()
}

# The text of one Markdown file with every link target rewritten for the shipped copy.
function Convert-SkillText {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Folder,
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $RelativePath
    )
    $lines = $Text -split "`r?`n"
    $rewritten = for ($index = 0; $index -lt $lines.Count; $index++) {
        Convert-MarkdownLine -Line $lines[$index] -Folder $Folder -SourcePath $SourcePath `
            -RelativePath $RelativePath -LineNumber ($index + 1)
    }
    return ($rewritten -join "`n")
}

# One shipped file written at its path under the copy folder, UTF-8 without a BOM and with LF line endings.
function Write-ShippedFile {
    param(
        [Parameter(Mandatory)] [string] $Folder,
        [Parameter(Mandatory)] [object] $File
    )
    $target = Join-Path $Folder $File.Path
    [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target))
    if ($null -eq $File.Text) {
        Copy-Item -LiteralPath $File.FullName -Destination $target -Force
        return
    }
    [System.IO.File]::WriteAllText($target, $File.Text, [System.Text.UTF8Encoding]::new($false))
}

# A junction made at $Link, its parent folder created when missing.
function New-SkillJunction {
    param(
        [Parameter(Mandatory)] [string] $Target,
        [Parameter(Mandatory)] [string] $Link
    )
    [void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Link))
    [void](New-Item -ItemType Junction -Path $Link -Target $Target)
}

# A junction's target made absolute against the link's folder, without the \??\ prefix a junction can carry.
function Get-JunctionTarget {
    param(
        [Parameter(Mandatory)] [string] $Link,
        [Parameter(Mandatory)] [string] $Target
    )
    $plain = $Target -replace '^(\\\?\?\\|\\\\\?\\)', ''
    if (-not [System.IO.Path]::IsPathFullyQualified($plain)) {
        $plain = Join-Path (Split-Path -Parent $Link) $plain
    }
    return [System.IO.Path]::GetFullPath($plain).TrimEnd('\', '/')
}

# $Link pointing at $Target: created when missing, left alone when it already points there, replaced when it is a
# junction pointing elsewhere, and refused when it is anything else.
function Set-SkillJunction {
    param(
        [Parameter(Mandatory)] [string] $Target,
        [Parameter(Mandatory)] [string] $Link
    )
    $info = [System.IO.DirectoryInfo]::new($Link)
    if ([int]$info.Attributes -eq -1) {
        New-SkillJunction -Target $Target -Link $Link
        return
    }
    if ($info.LinkType -ne 'Junction') {
        throw "install-skill.ps1: $Link exists and is not a junction; move it away and run again."
    }
    if ((Get-JunctionTarget -Link $Link -Target ([string]$info.LinkTarget)) -eq $Target) {
        return
    }
    [System.IO.Directory]::Delete($Link)
    New-SkillJunction -Target $Target -Link $Link
}

$sourcePath = Get-FullFolder -Path $Source -Base $PWD.ProviderPath
$installRoot = Get-FullFolder -Path $InstallRoot -Base $PWD.ProviderPath
$skillsRoot = Get-FullFolder -Path $SkillsRoot -Base $PWD.ProviderPath

if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'SKILL.md') -PathType Leaf)) {
    throw "install-skill.ps1: $sourcePath\SKILL.md is missing."
}

$files = @(Get-ShippedFile -SourcePath $sourcePath)
$refusals = @(Get-AbsolutePathRefusal -Files $files)
if ($refusals.Count -gt 0) {
    throw "install-skill.ps1: these lines would ship an absolute path with the skill:`n  $($refusals -join "`n  ")"
}

foreach ($file in $files) {
    if ($null -eq $file.Text) {
        continue
    }
    $file.Text = Convert-SkillText -Text $file.Text -Folder $sourcePath -SourcePath $sourcePath -RelativePath $file.Path
}

$destination = Join-Path (Join-Path $installRoot 'versions') (Join-Path $Version 'skill')
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}
[void](New-Item -ItemType Directory -Force -Path $destination)
foreach ($file in $files) {
    Write-ShippedFile -Folder $destination -File $file
}

$link = Join-Path $skillsRoot 'godot-xaml'
Set-SkillJunction -Target $destination -Link $link
[Console]::Out.WriteLine("installed godot-xaml skill $Version to $destination")
[Console]::Out.WriteLine("linked $link -> $destination")
