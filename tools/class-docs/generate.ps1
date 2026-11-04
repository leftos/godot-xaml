#requires -Version 7

<#
.SYNOPSIS
Writes a Godot version's class reference as the language server's hover docs file.

.DESCRIPTION
Reads a Godot source tree's doc/classes and modules/*/doc_classes XML and writes one JSON file of Markdown per
Godot version, keyed by class and by member, signal, constant and theme item name, with Godot's BBCode converted
to Markdown. Only the classes the metadata file lists are written, plus @GlobalScope, whose constants are the
global enums. Keys are sorted ordinally, lines end in LF and a final newline closes the file, so the same source
gives the same bytes.

.PARAMETER GodotSource
The root of a Godot source tree, e.g. F:\Godot\src-4.7.2: its doc/classes and its modules/*/doc_classes are read.

.PARAMETER Metadata
The metadata file whose classes are written, e.g. metadata/godot-4.7.json.

.PARAMETER GodotVersion
The full Godot version the docs come from, e.g. 4.7.2, named in the file's header.

.PARAMETER Out
The JSON file to write, e.g. metadata/docs/godot-4.7.json.

.EXAMPLE
pwsh -NoProfile -File tools/class-docs/generate.ps1 -GodotSource F:\Godot\src-4.7.2 `
    -Metadata metadata/godot-4.7.json -GodotVersion 4.7.2 -Out metadata/docs/godot-4.7.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GodotSource,

    [Parameter(Mandatory)]
    [string]$Metadata,

    [Parameter(Mandatory)]
    [string]$GodotVersion,

    [Parameter(Mandatory)]
    [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Godot's license, verbatim from the engine's LICENSE.txt. doc/classes/*.xml falls under COPYRIGHT.txt's default
# Files: * entry (Expat, i.e. this MIT text), which asks that the notice be included in every copy of the work; the
# docs file is one, so it carries the notice in its license field, and metadata/docs/LICENSE-godot.txt carries the
# same text beside it.
$godotLicense = @'
Copyright (c) 2014-present Godot Engine contributors (see AUTHORS.md).
Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@

# The manual's URL base for this Godot minor: Godot's text writes links against $DOCS_URL.
function Get-DocsUrlBase([string]$Version) {
    $parts = $Version.Split('.')
    if ($parts.Count -lt 3) {
        throw "GodotVersion '$Version' is not a full major.minor.patch version, e.g. 4.7.2."
    }
    return "https://docs.godotengine.org/en/$($parts[0]).$($parts[1])"
}

# The longest run of backticks in a text, which a code fence has to be longer than.
function Get-BacktickRun([string]$Text) {
    $longest = 0
    foreach ($run in [regex]::Matches($Text, '`+')) {
        if ($run.Length -gt $longest) { $longest = $run.Length }
    }
    return $longest
}

# An inline code span, fenced one backtick longer than the longest run inside it; empty text is nothing at all.
function Add-InlineCode([string]$Content) {
    $content = $Content.Trim()
    if ($content -eq '') { return '' }
    $fence = '`' * ((Get-BacktickRun $content) + 1)
    if ($content.StartsWith('`') -or $content.EndsWith('`')) {
        return "$fence $content $fence"
    }
    return "$fence$content$fence"
}

# A fenced code block around the body, whose lines keep their indentation; the language names the fence when given.
function Add-CodeBlock([string]$Body, [string]$Language = '') {
    $fence = '`' * [Math]::Max(3, (Get-BacktickRun $Body) + 1)
    $lines = @($Body.Split("`n") | ForEach-Object { $_.TrimEnd() })
    return "$fence$Language`n" + ($lines -join "`n") + "`n$fence"
}

# Bold or italic text as Markdown emphasis: whitespace inside the markers moves outside them, and a run across a
# line break is closed and reopened on each line, since each line is a paragraph.
function Convert-Emphasis([string]$Text, [string]$Tag, [string]$Marker) {
    $pattern = "(?s)\[$Tag\](.*?)\[/$Tag\]"
    return [regex]::Replace($Text, $pattern, {
            param($match)
            $parts = $match.Groups[1].Value -split '\r?\n'
            $marked = foreach ($part in $parts) {
                $core = $part.Trim()
                if ($core -eq '') { continue }
                $lead = $part.Substring(0, $part.Length - $part.TrimStart().Length)
                $trail = $part.Substring($part.TrimEnd().Length)
                $lead + $Marker + $core + $Marker + $trail
            }
            return (@($marked) -join "`n")
        })
}

# Godot's inline BBCode as Markdown: code spans, emphasis, references, the tags Markdown has no form for, and the
# manual's address.
function Convert-InlineBbCode([string]$Text) {
    $spans = [System.Collections.Generic.List[string]]::new()
    foreach ($tag in 'code', 'kbd') {
        $Text = [regex]::Replace($Text, "(?s)\[$tag(?:[ =][^\]]*)?\](.*?)\[/$tag\]", {
                param($match)
                $markdown = Add-InlineCode -Content $match.Groups[1].Value
                if ($markdown -eq '') { return '' }
                $spans.Add($markdown)
                return "$([char]1)C$($spans.Count - 1)$([char]1)"
            })
    }

    # $DOCS_URL is the manual's address, except inside code, which is left as it was written.
    $Text = $Text.Replace('$DOCS_URL', $docsUrlBase)

    # A bare [url] is an autolink, and a literal * in the text is escaped, so that the emphasis below is the only
    # emphasis in the Markdown.
    $Text = [regex]::Replace($Text, '(?s)\[url\](.*?)\[/url\]', '<$1>')
    $Text = $Text.Replace('*', [string][char]5)
    $Text = Convert-Emphasis $Text 'b' '**'
    $Text = Convert-Emphasis $Text 'i' '*'

    # Tags Markdown has no form for: dropped, the text kept.
    $dropped = 'u|s|center|color|font|font_size|outline_size|outline_color|bgcolor|fgcolor'
    $dropped += '|hint|indent|p|dropcap|ul|ol|table|cell|section|br'
    $Text = [regex]::Replace($Text, "(?s)\[($dropped)(?:[ =][^\]]*)?\](.*?)\[/\1\]", '$2')

    # [lb] and [rb] are the brackets themselves, escaped so that they open no Markdown link.
    $Text = $Text.Replace('[lb]', [string][char]3).Replace('[rb]', [string][char]4)

    # A reference is inline code with the Godot name written after the tag; what is left in brackets is a class.
    $references = 'member|method|signal|constant|enum|theme_item|annotation|constructor|operator|param'
    $Text = [regex]::Replace($Text, "\[(?:$references)\s+([^\[\]]+)\]", '`$1`')
    $Text = [regex]::Replace($Text, '\[([A-Za-z_@][^\[\]\s=]*)\]', '`$1`')

    # An unpaired tag goes too.
    $known = 'b|i|code|kbd|url|codeblock|codeblocks|csharp|gdscript|' + $dropped
    $Text = [regex]::Replace($Text, "\[/?(?:$known)(?:[ =][^\]]*)?\]", '')

    for ($i = 0; $i -lt $spans.Count; $i++) {
        $Text = $Text.Replace("$([char]1)C$i$([char]1)", $spans[$i])
    }
    $Text = $Text.Replace([string][char]5, '\*')
    return $Text.Replace([string][char]3, '\[').Replace([string][char]4, '\]')
}

# Godot's BBCode as Markdown: blocks and links kept aside, inline BBCode converted, one paragraph per line.
function Convert-BbCodeToMarkdown([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $lines = $Text -split "`r?`n"

    # Godot writes its XML text indented with tabs: drop the tabs every text line shares.
    $common = [int]::MaxValue
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $tabs = 0
        while ($tabs -lt $line.Length -and $line[$tabs] -eq "`t") { $tabs++ }
        if ($tabs -lt $common) { $common = $tabs }
    }
    if ($common -eq [int]::MaxValue) {
        return ''
    }
    $dedented = foreach ($line in $lines) {
        if ($line.Length -le $common) { '' } else { $line.Substring($common) }
    }
    $text = $dedented -join "`n"

    # Code blocks first: their content is not BBCode. Each becomes a fence, kept aside behind a marker line so that
    # the paragraph pass keeps it whole.
    $blocks = [System.Collections.Generic.List[string]]::new()
    $csharp = '(?s)\[csharp(?:\s[^\]]*)?\](.*?)\[/csharp\]'
    $text = [regex]::Replace($text, '(?s)\[codeblocks\](.*?)\[/codeblocks\]', {
            param($match)
            $part = [regex]::Match($match.Groups[1].Value, $csharp)
            if (-not $part.Success) { return "`n" }
            $body = $part.Groups[1].Value.Trim([char[]]@("`r", "`n"))
            $blocks.Add((Add-CodeBlock -Body $body -Language 'csharp'))
            return "`n@@BLOCK$($blocks.Count - 1)@@`n"
        })
    $codeblock = '(?s)\[codeblock(?:\s+lang=([\w+-]+))?(?:\s+[^\]]*)?\](.*?)\[/codeblock\]'
    $text = [regex]::Replace($text, $codeblock, {
            param($match)
            $body = $match.Groups[2].Value.Trim([char[]]@("`r", "`n"))
            $blocks.Add((Add-CodeBlock -Body $body -Language $match.Groups[1].Value))
            return "`n@@BLOCK$($blocks.Count - 1)@@`n"
        })

    # A link with a target: its text is converted like any other text, and the whole link is kept aside so that the
    # paragraph pass does not split it.
    $links = [System.Collections.Generic.List[string]]::new()
    $text = [regex]::Replace($text, '(?s)\[url=([^\]]+)\](.*?)\[/url\]', {
            param($match)
            $target = $match.Groups[1].Value.Replace('$DOCS_URL', $docsUrlBase)
            $links.Add('[' + (Convert-InlineBbCode $match.Groups[2].Value) + '](' + $target + ')')
            return "$([char]2)L$($links.Count - 1)$([char]2)"
        })

    $text = Convert-InlineBbCode $text

    # One paragraph per line, as Markdown needs; a line keeps its own spaces, which the conversions move around.
    $paragraphs = @(foreach ($line in ($text -split "`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $line
        })
    $markdown = $paragraphs -join "`n`n"

    # Put the code back, then the links: their text is Markdown already, and nothing is converted around them.
    for ($i = 0; $i -lt $blocks.Count; $i++) {
        $markdown = $markdown.Replace("@@BLOCK$i@@", $blocks[$i])
    }
    for ($i = 0; $i -lt $links.Count; $i++) {
        $markdown = $markdown.Replace("$([char]2)L$i$([char]2)", $links[$i])
    }
    return $markdown
}

# An element's text as Markdown, or an empty string when the element is absent.
function Get-DocsText($Parent, [string]$Name) {
    $element = $Parent.SelectSingleNode($Name)
    if ($null -eq $element) {
        return ''
    }
    return Convert-BbCodeToMarkdown $element.InnerText
}

# The deprecated and experimental attributes of an element, as Markdown; a bare attribute writes an empty note.
function Add-Notes($Entry, $Element) {
    foreach ($name in 'deprecated', 'experimental') {
        if ($Element.HasAttribute($name)) {
            $Entry[$name] = Convert-BbCodeToMarkdown $Element.GetAttribute($name)
        }
    }
}

# A class's members, signals, constants or theme items by name, each with its Markdown description; a constant
# keeps the enum it belongs to.
function Get-DocEntries($Class, [string]$ContainerName, [string]$EntryName) {
    $entries = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $container = $Class.SelectSingleNode($ContainerName)
    if ($null -eq $container) {
        return , $entries
    }
    foreach ($element in $container.SelectNodes($EntryName)) {
        $entry = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
        $entry['description'] = Convert-BbCodeToMarkdown $element.InnerText
        if ($EntryName -eq 'constant' -and $element.HasAttribute('enum')) {
            $entry['enum'] = $element.GetAttribute('enum')
        }
        Add-Notes $entry $element
        $entries[$element.GetAttribute('name')] = $entry
    }
    return , $entries
}

# One class's hover docs from its doc file.
function Read-ClassDocs([string]$Path) {
    $source = [System.Xml.XmlDocument]::new()
    try {
        $source.LoadXml((Get-Content -LiteralPath $Path -Raw))
    }
    catch [System.Xml.XmlException] {
        throw "The doc file '$Path' is not well-formed XML: $($_.Exception.Message)"
    }
    $class = $source.class

    $entry = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $entry['brief'] = Get-DocsText $class 'brief_description'
    $entry['description'] = Get-DocsText $class 'description'
    Add-Notes $entry $class
    $entry['members'] = (Get-DocEntries $class 'members' 'member')
    $entry['signals'] = (Get-DocEntries $class 'signals' 'signal')
    $entry['constants'] = (Get-DocEntries $class 'constants' 'constant')
    $entry['themeItems'] = (Get-DocEntries $class 'theme_items' 'theme_item')
    return , $entry
}

# The class name a doc file declares, from its <class> element.
function Get-ClassName([string]$Path) {
    $match = [regex]::Match((Get-Content -LiteralPath $Path -Raw), '<class\s[^>]*\bname="([^"]+)"')
    if (-not $match.Success) {
        throw "The doc file '$Path' declares no <class name=...> element."
    }
    return $match.Groups[1].Value
}

# Every class doc file under a Godot source tree, by class name: doc/classes and each modules/*/doc_classes.
function Get-ClassFiles([string]$GodotSource) {
    $root = (Resolve-Path -LiteralPath $GodotSource).Path
    $main = Join-Path $root 'doc/classes'
    if (-not (Test-Path -LiteralPath $main)) {
        throw "The Godot source '$root' has no doc/classes folder."
    }
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $main -Filter '*.xml' -File) {
        $paths.Add($file.FullName)
    }
    $modules = Join-Path $root 'modules'
    if (Test-Path -LiteralPath $modules) {
        foreach ($module in Get-ChildItem -LiteralPath $modules -Directory) {
            $folder = Join-Path $module.FullName 'doc_classes'
            if (-not (Test-Path -LiteralPath $folder)) { continue }
            foreach ($file in Get-ChildItem -LiteralPath $folder -Filter '*.xml' -File) {
                $paths.Add($file.FullName)
            }
        }
    }
    $files = [System.Collections.Generic.SortedDictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $paths) {
        $name = Get-ClassName $path
        if ($files.ContainsKey($name)) {
            throw "Two doc files name the class '$name': '$($files[$name])' and '$path'."
        }
        $files[$name] = $path
    }
    return , $files
}

# The JSON text of a value: two-space indent, keys in ordinal order, LF lines and a final newline.
function ConvertTo-DocsJson($Value) {
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $json = [System.Text.Json.JsonSerializer]::Serialize($Value, $Value.GetType(), $options)
    $json = $json -replace "`r`n", "`n"
    if (-not $json.EndsWith("`n")) {
        $json += "`n"
    }
    return $json
}

# The docs file, written: a Godot source tree's class reference as Markdown, by class and by member name.
function Write-DocsFile([string]$GodotSource, [string]$Metadata, [string]$GodotVersion, [string]$Out) {
    $script:docsUrlBase = Get-DocsUrlBase $GodotVersion
    $root = (Resolve-Path -LiteralPath $GodotSource).Path
    $metadataPath = (Resolve-Path -LiteralPath $Metadata).Path
    if (-not (Test-Path -LiteralPath (Join-Path $root 'doc/classes/@GlobalScope.xml'))) {
        throw "The Godot source '$root' has no doc/classes/@GlobalScope.xml; -GodotSource is a source tree's root."
    }

    try {
        $metadataDocument = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($metadataPath))
    }
    catch {
        throw "The metadata file '$metadataPath' is not valid JSON: $($_.Exception.Message)"
    }

    $names = [System.Collections.Generic.List[string]]::new()
    try {
        $metadataClasses = [System.Text.Json.JsonElement]::new()
        $hasClasses = $metadataDocument.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object
        if ($hasClasses) {
            $hasClasses = $metadataDocument.RootElement.TryGetProperty('classes', [ref]$metadataClasses)
        }
        if (-not $hasClasses -or $metadataClasses.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "The metadata file '$metadataPath' has no 'classes' object."
        }
        foreach ($property in $metadataClasses.EnumerateObject()) {
            $names.Add($property.Name)
        }
    }
    finally {
        $metadataDocument.Dispose()
    }
    $names.Add('@GlobalScope')

    $files = Get-ClassFiles $root
    $classes = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $written = 0
    $undocumented = 0
    foreach ($name in $names) {
        if (-not $files.ContainsKey($name)) {
            if ($name -ne '@GlobalScope') { $undocumented++ }
            continue
        }
        $classes[$name] = (Read-ClassDocs $files[$name])
        if ($name -ne '@GlobalScope') { $written++ }
    }
    if ($written -eq 0) {
        throw "No class was written: no doc file under '$root' names a class the metadata lists."
    }

    $docs = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
    $docs['format'] = 1
    $docs['godot'] = $GodotVersion
    $docs['license'] = $godotLicense
    $docs['source'] = "Converted from Godot $GodotVersion doc/classes and modules/*/doc_classes"
    $docs['classes'] = $classes

    $outFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
    $outDir = Split-Path -Parent $outFull
    if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
        New-Item -ItemType Directory -Path $outDir | Out-Null
    }
    [System.IO.File]::WriteAllText($outFull, (ConvertTo-DocsJson $docs), [System.Text.UTF8Encoding]::new($false))

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $outRelative = [System.IO.Path]::GetRelativePath($repoRoot, $outFull).Replace('\', '/')
    return "Wrote $outRelative`: $($classes.Count) classes, $undocumented metadata classes have no doc file."
}

# The manual's address base, which Convert-InlineBbCode substitutes for $DOCS_URL.
$docsUrlBase = Get-DocsUrlBase $GodotVersion

# Dot-sourced by check.ps1, which runs the converter and the writer alone.
if ($MyInvocation.InvocationName -ne '.') {
    Write-Host (Write-DocsFile -GodotSource $GodotSource -Metadata $Metadata -GodotVersion $GodotVersion -Out $Out)
}
