#requires -Version 7

<#
.SYNOPSIS
The prek hook `docs-lint`: runs the user-level docs-lint tool's fast checks over this repo when the machine has
it, and passes when it does not.

.DESCRIPTION
The tool lives at `$env:CLAUDE_CONFIG_DIR\tools\docs-lint`, or the user's `~/.claude\tools\docs-lint` when that
variable is unset, and checks the tracked docs for dead links, dead anchors and glossary order. uv does not expand
`~`, so the hook resolves the path here rather than in `.pre-commit-config.yaml`.

A clone on a machine without it skips the check and says so on standard error, one line,
`docs-lint: <path> not found; no docs check on this machine`, so a skipped check is never read as a passed one.

Usage: pwsh -NoProfile -File tools/docs-lint.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolPath = Join-Path ($env:CLAUDE_CONFIG_DIR ?? (Join-Path $HOME '.claude')) 'tools/docs-lint'

if (-not (Test-Path -LiteralPath $toolPath)) {
    [Console]::Error.WriteLine("docs-lint: $toolPath not found; no docs check on this machine")
    exit 0
}
uv run --project $toolPath python -m docs_lint . --fast
exit ([int]$LASTEXITCODE)
