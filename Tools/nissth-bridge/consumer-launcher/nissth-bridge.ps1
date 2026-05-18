# nissth-bridge — consumer-side launcher template (PowerShell).
#
# Copy this file to YOUR project's root (alongside your CLAUDE.md). It runs the
# dispatcher from the Tools/Nissth/ submodule. The dispatcher resolves your
# project root via CLAUDE.md walk-up, then looks for Bindings/ under the
# submodule (per CLAUDE.md §11.15 framework-root resolution order).
#
# Setup checklist (one-time, per consumer project):
#   1. git submodule add https://github.com/uKmaz/Nissth Tools/Nissth
#   2. Copy-Item Tools\Nissth\Tools\nissth-bridge\consumer-launcher\nissth-bridge .\nissth-bridge
#   3. Copy-Item Tools\Nissth\Tools\nissth-bridge\consumer-launcher\nissth-bridge.ps1 .\nissth-bridge.ps1
#   4. Copy CLAUDE.md, AGENTS.md, ImplementationPlans/_TEMPLATE.md, DBL/**/_TEMPLATE.md
#      from Tools\Nissth\ into your project root.
#   5. Initialize AgentReports\StatusUpdate.md with a Bootstrap entry (see CLAUDE.md §9.1).
#
# Override resolution (advanced): if you want to point at a Nissth checkout
# elsewhere, set $env:NISSTH_FRAMEWORK_ROOT before invoking.
#
# See Tools\Nissth\Tools\nissth-bridge\README.md and CLAUDE.md §11.15.

$ErrorActionPreference = 'Stop'
$dispatcher = Join-Path $PSScriptRoot 'Tools\Nissth\Tools\nissth-bridge\dispatcher.js'
$dispatcher = [System.IO.Path]::GetFullPath($dispatcher)

if (-not (Test-Path -LiteralPath $dispatcher -PathType Leaf)) {
    Write-Error "nissth-bridge: dispatcher not found at $dispatcher. Did you run 'git submodule update --init'? Or set NISSTH_FRAMEWORK_ROOT to a Nissth checkout."
    exit 3
}

& node $dispatcher @args
exit $LASTEXITCODE
