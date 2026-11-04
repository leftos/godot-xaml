#requires -Version 7

<#
.SYNOPSIS
Checks the converter and the writer in tools/class-docs/generate.ps1.

.DESCRIPTION
Dot-sources generate.ps1 (its main body does not run when it is dot-sourced), runs one case per rule the converter
and the writer follow, prints "CHECK <name> PASS" or "CHECK <name> FAIL <detail>" for each, then
"DONE <passed>/<total>", and exits 1 when any case failed. The file-level cases build a small Godot source tree
and metadata file under .tmp/class-docs-check.

.EXAMPLE
pwsh -NoProfile -File tools/class-docs/check.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'generate.ps1') -GodotSource '.' -Metadata '.' -GodotVersion '4.7.2' -Out '.tmp/unused.json'

$fence = '```'
$tempRoot = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '../..') '.tmp/class-docs-check'))

# Writes a fixture file, making its folder.
function Add-Fixture([string]$Path, [string]$Text) {
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

$buttonXml = @(
    '<?xml version="1.0" encoding="UTF-8" ?>'
    '<class name="Button" inherits="BaseButton" api_type="core">'
    '<brief_description>A button.</brief_description>'
    '<description>[b]Bold[/b] text.</description>'
    '<members>'
    '<member name="text" type="String" setter="set_text" getter="get_text">The button''s text.</member>'
    '</members>'
    '</class>'
) -join "`n"

$globalXml = @(
    '<?xml version="1.0" encoding="UTF-8" ?>'
    '<class name="@GlobalScope" api_type="core">'
    '<brief_description>Global scope.</brief_description>'
    '<description>Global constants.</description>'
    '<constants>'
    '<constant name="SIDE_LEFT" value="0" enum="Side">The left side.</constant>'
    '</constants>'
    '</class>'
) -join "`n"

$csgXml = @(
    '<?xml version="1.0" encoding="UTF-8" ?>'
    '<class name="CSGBox3D" inherits="CSGPrimitive3D" api_type="core">'
    '<brief_description>A CSG box.</brief_description>'
    '<description>A box.</description>'
    '</class>'
) -join "`n"

$metadataJson = '{"classes":{"Button":{},"CSGBox3D":{}}}'

Add-Fixture (Join-Path $tempRoot 'godot/doc/classes/@GlobalScope.xml') $globalXml
Add-Fixture (Join-Path $tempRoot 'godot/doc/classes/Button.xml') $buttonXml
Add-Fixture (Join-Path $tempRoot 'godot/modules/csg/doc_classes/CSGBox3D.xml') $csgXml
Add-Fixture (Join-Path $tempRoot 'meta.json') $metadataJson

$dupeTree = Join-Path $tempRoot 'dupe'
Add-Fixture (Join-Path $dupeTree 'doc/classes/Button.xml') $buttonXml
Add-Fixture (Join-Path $dupeTree 'modules/x/doc_classes/Button.xml') $buttonXml

$badTree = Join-Path $tempRoot 'bad'
$badButton = Join-Path $badTree 'doc/classes/Button.xml'
Add-Fixture (Join-Path $badTree 'doc/classes/@GlobalScope.xml') $globalXml
Add-Fixture $badButton '<class name="Button"><description>oops'
Add-Fixture (Join-Path $tempRoot 'bad.json') $metadataJson

$noGlobalTree = Join-Path $tempRoot 'noglobal'
Add-Fixture (Join-Path $noGlobalTree 'doc/classes/Button.xml') $buttonXml
Add-Fixture (Join-Path $tempRoot 'noglobal.json') $metadataJson

$noClassTree = Join-Path $tempRoot 'noclass'
Add-Fixture (Join-Path $noClassTree 'doc/classes/@GlobalScope.xml') $globalXml
$noClassMetadata = Join-Path $tempRoot 'noclass.json'
Add-Fixture $noClassMetadata '{"classes":{}}'

$noClassesMetadata = Join-Path $tempRoot 'noclasses.json'
Add-Fixture $noClassesMetadata '{"godot":"4.7.2"}'

$cases = @(
    @{ Name = 'bold'; Run = { Convert-BbCodeToMarkdown '[b]bold[/b]' }; Expected = '**bold**' },
    @{ Name = 'italic'; Run = { Convert-BbCodeToMarkdown '[i]italic[/i]' }; Expected = '*italic*' },
    @{ Name = 'emphasis whitespace'; Run = { Convert-BbCodeToMarkdown '[i] spaced [/i]' }; Expected = ' *spaced* ' },
    @{
        Name = 'emphasis across a line break'
        Run = { Convert-BbCodeToMarkdown "[b]one`ntwo[/b]" }
        Expected = "**one**`n`n**two**"
    },
    @{ Name = 'code'; Run = { Convert-BbCodeToMarkdown '[code]true[/code]' }; Expected = '`true`' },
    @{ Name = 'code with a backtick'; Run = { Convert-BbCodeToMarkdown '[code]a`b[/code]' }; Expected = '``a`b``' },
    @{ Name = 'code skip-lint'; Run = { Convert-BbCodeToMarkdown '[code skip-lint]x[/code]' }; Expected = '`x`' },
    @{ Name = 'empty code'; Run = { Convert-BbCodeToMarkdown 'x[code]   [/code]y' }; Expected = 'xy' },
    @{ Name = 'kbd'; Run = { Convert-BbCodeToMarkdown '[kbd]Ctrl[/kbd]' }; Expected = '`Ctrl`' },
    @{ Name = 'code inside bold'; Run = { Convert-BbCodeToMarkdown '[b]a [code]c[/code][/b]' }; Expected = '**a `c`**' },
    @{ Name = 'literal asterisk'; Run = { Convert-BbCodeToMarkdown 'a * b' }; Expected = 'a \* b' },
    @{ Name = 'asterisk inside bold'; Run = { Convert-BbCodeToMarkdown '[b]Linux/*BSD:[/b]' }; Expected = '**Linux/\*BSD:**' },
    @{
        Name = 'dropped tags'
        Run = {
            Convert-BbCodeToMarkdown (@(
                    '[u]u[/u] [s]s[/s] [center]c[/center] [color=red]col[/color]',
                    '[font=Arial]f[/font] [font_size=20]fs[/font_size] [outline_size=2]os[/outline_size]',
                    '[outline_color=red]oc[/outline_color] [bgcolor=black]bg[/bgcolor] [fgcolor=white]fg[/fgcolor]',
                    '[hint=Use]h[/hint] [indent]ind[/indent] [p]p[/p] [dropcap]d[/dropcap]',
                    '[ul]ul[/ul] [ol]ol[/ol] [table=2]t[/table] [cell]cell[/cell]'
                ) -join ' ')
        }
        Expected = 'u s c col f fs os oc bg fg h ind p d ul ol t cell'
    },
    @{ Name = 'stray closing tag'; Run = { Convert-BbCodeToMarkdown 'x [/b] y' }; Expected = 'x  y' },
    @{ Name = 'lb and rb'; Run = { Convert-BbCodeToMarkdown '[lb]key[rb]' }; Expected = '\[key\]' },
    @{ Name = 'lb and rb before a bracket'; Run = { Convert-BbCodeToMarkdown '[lb]x[rb](y)' }; Expected = '\[x\](y)' },
    @{
        Name = 'url with a target'
        Run = { Convert-BbCodeToMarkdown '[url=https://godotengine.org]Godot[/url]' }
        Expected = '[Godot](https://godotengine.org)'
    },
    @{
        Name = 'url without a target'
        Run = { Convert-BbCodeToMarkdown '[url]https://godotengine.org[/url]' }
        Expected = '<https://godotengine.org>'
    },
    @{
        Name = 'url target with formatting'
        Run = { Convert-BbCodeToMarkdown '[url=https://godotengine.org][b]Godot[/b][/url]' }
        Expected = '[**Godot**](https://godotengine.org)'
    },
    @{
        Name = 'url text with a reference and code'
        Run = { Convert-BbCodeToMarkdown '[url=X][member A.b] and [code]c[/code][/url]' }
        Expected = '[`A.b` and `c`](X)'
    },
    @{
        Name = 'DOCS_URL'
        Run = { Convert-BbCodeToMarkdown 'See $DOCS_URL/tutorials/ui/index.html' }
        Expected = 'See https://docs.godotengine.org/en/4.7/tutorials/ui/index.html'
    },
    @{
        Name = 'DOCS_URL inside code'
        Run = { Convert-BbCodeToMarkdown '[code]$DOCS_URL/x[/code]' }
        Expected = '`$DOCS_URL/x`'
    },
    @{ Name = 'class reference'; Run = { Convert-BbCodeToMarkdown 'A [Button] and an [int]' }; Expected = 'A `Button` and an `int`' },
    @{ Name = 'member reference'; Run = { Convert-BbCodeToMarkdown '[member Button.text]' }; Expected = '`Button.text`' },
    @{ Name = 'method reference'; Run = { Convert-BbCodeToMarkdown '[method Node.add_child]' }; Expected = '`Node.add_child`' },
    @{ Name = 'signal reference'; Run = { Convert-BbCodeToMarkdown '[signal BaseButton.pressed]' }; Expected = '`BaseButton.pressed`' },
    @{
        Name = 'constant reference'
        Run = { Convert-BbCodeToMarkdown '[constant BaseButton.ACTION_MODE_BUTTON_PRESS]' }
        Expected = '`BaseButton.ACTION_MODE_BUTTON_PRESS`'
    },
    @{ Name = 'enum reference'; Run = { Convert-BbCodeToMarkdown '[enum BaseButton.DrawMode]' }; Expected = '`BaseButton.DrawMode`' },
    @{ Name = 'theme item reference'; Run = { Convert-BbCodeToMarkdown '[theme_item font_color]' }; Expected = '`font_color`' },
    @{ Name = 'annotation reference'; Run = { Convert-BbCodeToMarkdown '[annotation @export]' }; Expected = '`@export`' },
    @{ Name = 'constructor reference'; Run = { Convert-BbCodeToMarkdown '[constructor Basis]' }; Expected = '`Basis`' },
    @{ Name = 'operator reference'; Run = { Convert-BbCodeToMarkdown '[operator String.+]' }; Expected = '`String.+`' },
    @{ Name = 'param'; Run = { Convert-BbCodeToMarkdown '[param toggled_on]' }; Expected = '`toggled_on`' },
    @{
        Name = 'codeblocks with csharp'
        Run = {
            Convert-BbCodeToMarkdown (@(
                    'Before', '[codeblocks]', '[gdscript]', 'print(1)', '[/gdscript]',
                    '[csharp]', 'GD.Print(1);', '[/csharp]', '[/codeblocks]', 'After'
                ) -join "`n")
        }
        Expected = "Before`n`n" + $fence + "csharp`nGD.Print(1);`n" + $fence + "`n`nAfter"
    },
    @{
        Name = 'codeblocks without csharp'
        Run = {
            Convert-BbCodeToMarkdown (@(
                    'Before', '[codeblocks]', '[gdscript]', 'print(1)', '[/gdscript]', '[/codeblocks]', 'After'
                ) -join "`n")
        }
        Expected = "Before`n`nAfter"
    },
    @{
        Name = 'codeblocks with a csharp argument'
        Run = {
            Convert-BbCodeToMarkdown (@(
                    '[codeblocks]', '[csharp skip-lint]', 'GD.Print(1);', '[/csharp]', '[/codeblocks]'
                ) -join "`n")
        }
        Expected = $fence + "csharp`nGD.Print(1);`n" + $fence
    },
    @{
        Name = 'codeblocks with a gdscript argument'
        Run = {
            Convert-BbCodeToMarkdown (@(
                    '[codeblocks]', '[gdscript skip-lint]', 'print(1)', '[/gdscript]',
                    '[csharp]', 'GD.Print(1);', '[/csharp]', '[/codeblocks]'
                ) -join "`n")
        }
        Expected = $fence + "csharp`nGD.Print(1);`n" + $fence
    },
    @{
        Name = 'codeblock'
        Run = { Convert-BbCodeToMarkdown (@('[codeblock]', 'var x = 1', '[/codeblock]') -join "`n") }
        Expected = $fence + "`nvar x = 1`n" + $fence
    },
    @{
        Name = 'codeblock with a language'
        Run = { Convert-BbCodeToMarkdown (@('[codeblock lang=gdscript]', 'var x = 1', '[/codeblock]') -join "`n") }
        Expected = $fence + "gdscript`nvar x = 1`n" + $fence
    },
    @{
        Name = 'codeblock with an argument'
        Run = { Convert-BbCodeToMarkdown (@('[codeblock skip-lint]', '[b]x[/b]', '[/codeblock]') -join "`n") }
        Expected = $fence + "`n[b]x[/b]`n" + $fence
    },
    @{
        Name = 'codeblock with backticks in its body'
        Run = { Convert-BbCodeToMarkdown (@('[codeblock]', 'a ``` b', '[/codeblock]') -join "`n") }
        Expected = ('`' * 4) + "`na " + ('`' * 3) + " b`n" + ('`' * 4)
    },
    @{
        Name = 'BBCode inside a code block'
        Run = { Convert-BbCodeToMarkdown (@('[codeblock]', '[b]x[/b] [code]y[/code] [member a.b]', '[/codeblock]') -join "`n") }
        Expected = $fence + "`n[b]x[/b] [code]y[/code] [member a.b]`n" + $fence
    },
    @{
        Name = 'a paragraph per line'
        Run = { Convert-BbCodeToMarkdown "`n`t`t`tFirst line.`n`t`t`tSecond line.`n`t`t" }
        Expected = "First line.`n`nSecond line."
    }
)

$godotTree = Join-Path $tempRoot 'godot'
$metadataPath = Join-Path $tempRoot 'meta.json'
$outPath = Join-Path $tempRoot 'out.json'

$cases += @(
    @{
        Name = 'deprecated without a message'
        Run = {
            $entry = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
            $element = [xml]'<member deprecated="" experimental="Use [member sync_mode] instead." />'
            Add-Notes $entry $element.DocumentElement
            'deprecated=<{0}> experimental=<{1}>' -f $entry['deprecated'], $entry['experimental']
        }
        Expected = 'deprecated=<> experimental=<Use `sync_mode` instead.>'
    },
    @{
        Name = 'class files from a Godot source'
        Run = { (Get-ClassFiles $godotTree).Keys -join ',' }
        Expected = '@GlobalScope,Button,CSGBox3D'
    },
    @{
        Name = 'duplicate class files'
        Run = {
            try {
                $null = Get-ClassFiles $dupeTree
                'no error thrown'
            }
            catch { $_.Exception.Message }
        }
        Match = @("Two doc files name the class 'Button'")
    },
    @{
        Name = 'docs file'
        Run = {
            $null = Write-DocsFile -GodotSource $godotTree -Metadata $metadataPath -GodotVersion '4.7.2' -Out $outPath
            $docs = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($outPath))
            $classes = $docs.RootElement.GetProperty('classes')
            $names = @($classes.EnumerateObject() | ForEach-Object { $_.Name }) -join ','
            $text = $classes.GetProperty('Button').GetProperty('members').GetProperty('text')
            $names + '|' + $text.GetProperty('description').GetString() + '|' +
            $docs.RootElement.GetProperty('source').GetString()
        }
        Expected = '@GlobalScope,Button,CSGBox3D|The button''s text.|Converted from Godot 4.7.2 doc/classes and modules/*/doc_classes'
    },
    @{
        Name = 'metadata without classes'
        Run = {
            try {
                $null = Write-DocsFile -GodotSource $godotTree -Metadata $noClassesMetadata -GodotVersion '4.7.2' -Out $outPath
                'no error thrown'
            }
            catch { $_.Exception.Message }
        }
        Match = @($noClassesMetadata, "has no 'classes' object")
    },
    @{
        Name = 'malformed doc file'
        Run = {
            try {
                $null = Write-DocsFile -GodotSource $badTree -Metadata $metadataPath -GodotVersion '4.7.2' -Out $outPath
                'no error thrown'
            }
            catch { $_.Exception.Message }
        }
        Match = @($badButton, 'is not well-formed XML')
    },
    @{
        Name = 'no global scope'
        Run = {
            try {
                $null = Write-DocsFile -GodotSource $noGlobalTree -Metadata $metadataPath -GodotVersion '4.7.2' -Out $outPath
                'no error thrown'
            }
            catch { $_.Exception.Message }
        }
        Match = @('has no doc/classes/@GlobalScope.xml')
    },
    @{
        Name = 'no class written'
        Run = {
            try {
                $null = Write-DocsFile -GodotSource $noClassTree -Metadata $noClassMetadata -GodotVersion '4.7.2' -Out $outPath
                'no error thrown'
            }
            catch { $_.Exception.Message }
        }
        Match = @('No class was written')
    },
    @{
        Name = 'serializer'
        Run = {
            $inner = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
            $inner['d'] = 'D'
            $inner['c'] = 'C'
            $outer = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
            $outer['a'] = 'A'
            $outer['B'] = 'B'
            $outer['inner'] = $inner
            ConvertTo-DocsJson $outer
        }
        Expected = @(
            '{'
            '  "B": "B",'
            '  "a": "A",'
            '  "inner": {'
            '    "c": "C",'
            '    "d": "D"'
            '  }'
            '}'
            ''
        ) -join "`n"
    }
)

$passed = 0
foreach ($case in $cases) {
    try {
        $actual = [string](& $case['Run'])
    }
    catch {
        $actual = "threw: $($_.Exception.Message)"
    }
    if ($case.ContainsKey('Match')) {
        $wanted = $case['Match']
        $ok = $true
        foreach ($piece in $wanted) {
            if (-not $actual.Contains($piece)) { $ok = $false }
        }
    }
    else {
        $wanted = $case['Expected']
        $ok = $actual -ceq $wanted
    }
    if ($ok) {
        $passed++
        Write-Host "CHECK $($case['Name']) PASS"
    }
    else {
        Write-Host "CHECK $($case['Name']) FAIL expected <$wanted> got <$actual>"
    }
}

$total = $cases.Count
Write-Host "DONE $passed/$total"
if ($passed -ne $total) {
    exit 1
}
