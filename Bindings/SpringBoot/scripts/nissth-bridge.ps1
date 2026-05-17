# nissth-bridge.ps1 — PowerShell launcher for the Nissth Diagnostic Bridge CLI (spring-boot binding).
# Resolves its own location to find the binding's jar; sets NISSTH_REPO_ROOT so reports
# land under <repo-root>\AgentReports\Bridge\. Forwards all args to the jar.

$ErrorActionPreference = 'Stop'

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$BindingRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$RepoRoot    = (Resolve-Path (Join-Path $BindingRoot '..\..')).Path
$Jar         = Join-Path $BindingRoot 'target\nissth-bridge-0.1.0.jar'

if (-not (Test-Path $Jar)) {
    Write-Error "$Jar not found. Build it first: cd `"$BindingRoot`"; .\mvnw.cmd clean package -DskipTests"
    exit 3
}

if (-not $env:NISSTH_REPO_ROOT) {
    $env:NISSTH_REPO_ROOT = $RepoRoot
}

# -Dfile.encoding=UTF-8 ensures the CLI's PrintStream output matches the report contents (UTF-8).
& java -Dfile.encoding=UTF-8 -jar $Jar @args
exit $LASTEXITCODE
