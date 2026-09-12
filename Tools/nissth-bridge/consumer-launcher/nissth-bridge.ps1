# nissth-bridge — consumer-side launcher (PowerShell 5.1+ / pwsh).
#
# Installed into a consumer project's root by `nissth-init` (Tools/nissth-init/),
# or copied by hand. Runs the Nissth dispatcher, which resolves the consumer's
# repo root via CLAUDE.md walk-up and then finds Bindings/ per CLAUDE.md §11.15.
#
# Dispatcher resolution order (mirrors the dispatcher's own framework-root order):
#   1. $env:NISSTH_FRAMEWORK_ROOT  — explicit; a Nissth checkout that contains Bindings/
#   2. <this dir>\Tools\Nissth     — git-submodule convention
#   3. $DefaultRoot                — filled in by `nissth-init --wiring local`;
#                                    empty on submodule installs
# The first candidate that holds Tools\nissth-bridge\dispatcher.js wins. If the
# env var was not set, it is set for this process to the chosen root so the
# dispatcher's own tier-1 resolution agrees with this launcher's choice.
#
# See <framework>\Tools\nissth-bridge\README.md and CLAUDE.md §11.15.

$ErrorActionPreference = 'Stop'
$DefaultRoot = ''

$candidates = @()
if ($env:NISSTH_FRAMEWORK_ROOT) { $candidates += $env:NISSTH_FRAMEWORK_ROOT }
$candidates += (Join-Path $PSScriptRoot 'Tools\Nissth')
if ($DefaultRoot) { $candidates += $DefaultRoot }

foreach ($root in $candidates) {
    $dispatcher = Join-Path $root 'Tools\nissth-bridge\dispatcher.js'
    if (Test-Path -LiteralPath $dispatcher -PathType Leaf) {
        if (-not $env:NISSTH_FRAMEWORK_ROOT) {
            $env:NISSTH_FRAMEWORK_ROOT = [System.IO.Path]::GetFullPath($root)
        }
        & node $dispatcher @args
        exit $LASTEXITCODE
    }
}

[Console]::Error.WriteLine("nissth-bridge: dispatcher not found. Tried:`n  " + ($candidates -join "`n  "))
[Console]::Error.WriteLine("Set NISSTH_FRAMEWORK_ROOT to a Nissth checkout, add the Tools\Nissth submodule (git submodule update --init), or re-run nissth-init --wiring local.")
exit 3
