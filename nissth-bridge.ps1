# nissth-bridge — unified cross-binding dispatcher (Phase 08) — PowerShell launcher.
# Resolves Tools/nissth-bridge/dispatcher.js relative to this launcher so it
# works from any cwd. No build step required — dispatcher.js is plain JS.
#
# Per-binding launchers under Bindings/<stack>/scripts/nissth-bridge remain
# as escape hatches; they're not expected to be on PATH alongside this one.
#
# See CLAUDE.md §11.5 + §11.15 and Tools/nissth-bridge/README.md.

$ErrorActionPreference = 'Stop'
$dispatcher = Join-Path $PSScriptRoot 'Tools\nissth-bridge\dispatcher.js'
$dispatcher = [System.IO.Path]::GetFullPath($dispatcher)

if (-not (Test-Path -LiteralPath $dispatcher -PathType Leaf)) {
    Write-Error "nissth-bridge: dispatcher not found at $dispatcher. Are you running this from a Nissth repo root?"
    exit 3
}

& node $dispatcher @args
exit $LASTEXITCODE
