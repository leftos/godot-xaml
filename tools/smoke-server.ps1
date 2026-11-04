#requires -Version 7.3

<#
.SYNOPSIS
Launches a language server executable and drives one session over stdio, failing on the first thing that is wrong.

.DESCRIPTION
Starts the executable with stdin, stdout and stderr redirected and speaks LSP's Content-Length framing to it:
initialize (serverInfo.name is godot-xaml, the version is not unknown, and the capabilities hold hoverProvider,
completionProvider and documentFormattingProvider), initialized (the server's "started" log must follow), a
$/cancelRequest for an id the server never saw (its id is read as StreamJsonRpc's RequestId, which must not fail),
textDocument/didOpen of a scene with a Label
without a Name (a publishDiagnostics with a GX code proves the embedded metadata loaded; its keys must be camelCase:
uri, diagnostics, range, severity, code), a hover on the Label tag (a description beyond the signature proves the
embedded class reference loaded), a completion inside the tag (it must return items), shutdown and exit.

The process must exit with code 0 within the timeout, stderr must hold no exception text and no transport warning or error
(a godot-xaml.jsonrpc Warning or Error line), and stdout must hold nothing but framed messages. Every read waits at
most -TimeoutSeconds; on any failure the process is killed and the script exits 1.

.PARAMETER Exe
The server executable to launch.

.PARAMETER TimeoutSeconds
The longest any single read, and the exit, may take.

.EXAMPLE
pwsh -NoProfile -File tools/smoke-server.ps1 -Exe .tmp/publish/win-x64/GodotXaml.LanguageServer.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exePath = (Resolve-Path -LiteralPath $Exe).Path
$uri = 'untitled:Smoke.xscn'
# A Label without a Name: GX0104 on the second line.
$scene = "<Control Name=`"Root`">`n  <Label />`n</Control>"

$info = [System.Diagnostics.ProcessStartInfo]::new($exePath)
$info.UseShellExecute = $false
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($info)
$stdin = $process.StandardInput.BaseStream
$stdout = $process.StandardOutput.BaseStream
$stderrRead = $process.StandardError.ReadToEndAsync()

# The bytes read from stdout and not yet parsed into a message.
$pending = [System.Collections.Generic.List[byte]]::new()
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Fail([string] $message) {
    throw "smoke-server: $message"
}

function Send([hashtable] $message) {
    $body = $utf8.GetBytes(($message | ConvertTo-Json -Depth 20 -Compress))
    $header = [System.Text.Encoding]::ASCII.GetBytes("Content-Length: $($body.Length)`r`n`r`n")
    $stdin.Write($header, 0, $header.Length)
    $stdin.Write($body, 0, $body.Length)
    $stdin.Flush()
}

# Reads more bytes into $pending; false at the end of the stream. Fails when nothing comes within the timeout.
function Fill([string] $waitingFor) {
    $buffer = [byte[]]::new(65536)
    $read = $stdout.ReadAsync($buffer, 0, $buffer.Length)
    if (-not $read.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        Fail "nothing on stdout within $TimeoutSeconds s while waiting for $waitingFor"
    }
    if ($read.Result -eq 0) {
        return $false
    }
    $pending.AddRange([System.ArraySegment[byte]]::new($buffer, 0, $read.Result))
    return $true
}

function IndexOfHeaderEnd {
    for ($i = 0; $i + 3 -lt $pending.Count; $i++) {
        if ($pending[$i] -eq 13 -and $pending[$i + 1] -eq 10 -and $pending[$i + 2] -eq 13 -and $pending[$i + 3] -eq 10) {
            return $i
        }
    }
    return -1
}

# The next framed message as a JsonElement, or $null at the end of the stream with nothing pending.
function Read-Message([string] $waitingFor) {
    while (($end = IndexOfHeaderEnd) -lt 0) {
        if (-not (Fill $waitingFor)) {
            if ($pending.Count -gt 0) {
                Fail "stdout ended inside a header: '$($utf8.GetString($pending.ToArray()))'"
            }
            return $null
        }
    }
    $headerText = [System.Text.Encoding]::ASCII.GetString($pending.ToArray(), 0, $end)
    $length = -1
    foreach ($line in $headerText -split "`r`n") {
        if ($line -match '^Content-Length: (\d+)$') {
            $length = [int] $Matches[1]
        }
        elseif ($line -notmatch '^Content-Type: ') {
            Fail "stdout holds something that is not a framed message: '$line'"
        }
    }
    if ($length -lt 0) {
        Fail "a message header has no Content-Length: '$headerText'"
    }
    $start = $end + 4
    while ($pending.Count -lt $start + $length) {
        if (-not (Fill $waitingFor)) {
            Fail "stdout ended inside a message body"
        }
    }
    $body = $utf8.GetString($pending.ToArray(), $start, $length)
    $pending.RemoveRange(0, $start + $length)
    $message = [System.Text.Json.JsonDocument]::Parse($body).RootElement
    $out = [System.Text.Json.JsonElement]::new()
    if ($message.TryGetProperty('error', [ref] $out)) {
        Fail "the server answered with an error: $body"
    }
    return $message
}

function Has([System.Text.Json.JsonElement] $element, [string] $name) {
    $out = [System.Text.Json.JsonElement]::new()
    return $element.ValueKind -eq 'Object' -and $element.TryGetProperty($name, [ref] $out)
}

# Reads messages until one passes the test, and returns it; the others (logs, earlier notifications) are skipped.
function Wait-Message([string] $waitingFor, [scriptblock] $test) {
    while ($true) {
        $message = Read-Message $waitingFor
        if ($null -eq $message) {
            Fail "stdout ended while waiting for $waitingFor"
        }
        if (& $test $message) {
            return $message
        }
    }
}

function Wait-Response([int] $id, [string] $method) {
    $message = Wait-Message "the $method response" {
        param($m)
        (Has $m 'id') -and $m.GetProperty('id').ValueKind -eq 'Number' -and $m.GetProperty('id').GetInt32() -eq $id
    }
    if (-not (Has $message 'result')) {
        Fail "the $method response has no result: $($message.GetRawText())"
    }
    return $message.GetProperty('result')
}

try {
    Send @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ processId = $PID; rootUri = $null; capabilities = @{} } }
    $init = Wait-Response 1 'initialize'
    $serverInfo = $init.GetProperty('serverInfo')
    $name = $serverInfo.GetProperty('name').GetString()
    $version = $serverInfo.GetProperty('version').GetString()
    if ($name -ne 'godot-xaml') {
        Fail "serverInfo.name is '$name', not 'godot-xaml'"
    }
    if ([string]::IsNullOrEmpty($version) -or $version -eq 'unknown') {
        Fail "serverInfo.version is '$version'"
    }
    $capabilities = $init.GetProperty('capabilities')
    foreach ($capability in 'hoverProvider', 'completionProvider', 'documentFormattingProvider') {
        if (-not (Has $capabilities $capability)) {
            Fail "the capabilities have no $capability`: $($capabilities.GetRawText())"
        }
    }
    Write-Host "initialize: $name $version"

    Send @{ jsonrpc = '2.0'; method = 'initialized'; params = @{} }
    $started = Wait-Message 'the started log' {
        param($m)
        (Has $m 'method') -and $m.GetProperty('method').GetString() -eq 'window/logMessage' -and
        $m.GetProperty('params').GetProperty('message').GetString() -match 'started'
    }
    Write-Host "initialized: $($started.GetProperty('params').GetProperty('message').GetString())"

    # No request 99 is in flight; the server reads the id all the same, and the stderr check below sees a failed read.
    Send @{ jsonrpc = '2.0'; method = '$/cancelRequest'; params = @{ id = 99 } }
    Send @{
        jsonrpc = '2.0'
        method = 'textDocument/didOpen'
        params = @{ textDocument = @{ uri = $uri; languageId = 'xscn'; version = 1; text = $scene } }
    }
    $published = Wait-Message 'publishDiagnostics' {
        param($m)
        (Has $m 'method') -and $m.GetProperty('method').GetString() -eq 'textDocument/publishDiagnostics'
    }
    $parameters = $published.GetProperty('params')
    foreach ($key in 'uri', 'diagnostics') {
        if (-not (Has $parameters $key)) {
            Fail "publishDiagnostics has no camelCase '$key': $($parameters.GetRawText())"
        }
    }
    if ($parameters.GetProperty('uri').GetString() -ne $uri) {
        Fail "publishDiagnostics is for '$($parameters.GetProperty('uri').GetString())', not '$uri'"
    }
    $diagnostics = @($parameters.GetProperty('diagnostics').EnumerateArray())
    if ($diagnostics.Count -eq 0) {
        Fail "publishDiagnostics holds no diagnostic: $($parameters.GetRawText())"
    }
    $diagnostic = $diagnostics[0]
    foreach ($key in 'range', 'severity', 'code') {
        if (-not (Has $diagnostic $key)) {
            Fail "the diagnostic has no camelCase '$key': $($diagnostic.GetRawText())"
        }
    }
    $code = $diagnostic.GetProperty('code').GetString()
    if ($code -notmatch '^GX\d{4}$') {
        Fail "the diagnostic's code is '$code', not a GX code"
    }
    Write-Host "publishDiagnostics: $code"

    # Line 1 is "  <Label />": character 4 is inside the tag's name, character 9 is where an attribute name goes.
    Send @{
        jsonrpc = '2.0'; id = 2; method = 'textDocument/hover'
        params = @{ textDocument = @{ uri = $uri }; position = @{ line = 1; character = 4 } }
    }
    $hover = Wait-Response 2 'hover'
    if ($hover.ValueKind -ne 'Object' -or -not (Has $hover 'contents')) {
        Fail "the hover on the Label tag is empty: $($hover.GetRawText())"
    }
    $markdown = $hover.GetProperty('contents').GetProperty('value').GetString()
    $prose = @($markdown -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch '^```' -and $_ -notmatch 'class Label' })
    if ($markdown -notmatch 'class Label' -or $prose.Count -eq 0) {
        Fail "the hover on the Label tag has no class description: '$markdown'"
    }
    Write-Host "hover: $($prose[0])"

    Send @{
        jsonrpc = '2.0'; id = 3; method = 'textDocument/completion'
        params = @{ textDocument = @{ uri = $uri }; position = @{ line = 1; character = 9 } }
    }
    $completion = Wait-Response 3 'completion'
    $items = if ($completion.ValueKind -eq 'Object' -and (Has $completion 'items')) { $completion.GetProperty('items').GetArrayLength() } else { 0 }
    if ($items -eq 0) {
        Fail "the completion in the Label tag returned no items: $($completion.GetRawText())"
    }
    Write-Host "completion: $items items"

    Send @{ jsonrpc = '2.0'; id = 4; method = 'shutdown' }
    $shutdown = Wait-Response 4 'shutdown'
    if ($shutdown.ValueKind -ne 'Null') {
        Fail "the shutdown result is not null: $($shutdown.GetRawText())"
    }
    Send @{ jsonrpc = '2.0'; method = 'exit' }

    # Whatever the server still writes must be framed messages too.
    while ($null -ne (Read-Message 'the end of stdout')) { }
    if (-not $process.WaitForExit([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        Fail "the server did not exit within $TimeoutSeconds s of exit"
    }
    if ($process.ExitCode -ne 0) {
        Fail "the server exited with $($process.ExitCode), not 0"
    }
    if (-not $stderrRead.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        Fail "stderr did not close within $TimeoutSeconds s"
    }
    $stderr = $stderrRead.Result
    if ($stderr -match '(?im)godot-xaml\.jsonrpc (Warning|Error)|unhandled exception|\w*Exception:|^\s+at \S+\(') {
        Fail "stderr holds a transport warning or error, or exception text:`n$stderr"
    }
    Write-Host "smoke-server: passed, exit code 0 ($exePath)"
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $process.Dispose()
}
