# Launcher for the nissth-bridge Postgres binding (PowerShell).
# Resolves the dist/cli/index.js path relative to this script so the launcher
# works from any cwd. Build the binding first with `npm run build`.

$ErrorActionPreference = 'Stop'
$cli = Join-Path $PSScriptRoot '..\dist\cli\index.js'
$cli = [System.IO.Path]::GetFullPath($cli)

if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    Write-Error "nissth-bridge: CLI not built at $cli. Build the binding first: cd `"$PSScriptRoot\..`" ; npm run build"
    exit 3
}

& node $cli @args
exit $LASTEXITCODE
