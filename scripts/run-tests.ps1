# Запуск тестов Unity локально через CLI
# Использование: ./scripts/run-tests.ps1
# Опции: ./scripts/run-tests.ps1 -Mode EditMode|PlayMode|All

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All"
)

$ProjectPath = $PSScriptRoot | Split-Path -Parent
$ResultsDir = Join-Path $ProjectPath "TestResults"

# Версия редактора берётся из проекта (ProjectSettings/ProjectVersion.txt), а не хардкодится —
# иначе локальный прогон падает при апгрейде Unity (тех-долг 07 §3.8 I1).
$VersionFile = Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt"
if (-not (Test-Path $VersionFile)) {
    Write-Error "ProjectVersion.txt not found at: $VersionFile"
    exit 1
}

$versionLine = Get-Content $VersionFile | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$UnityVersion = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
if (-not $UnityVersion) {
    Write-Error "Could not parse m_EditorVersion from: $VersionFile"
    exit 1
}

$UnityExe = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity $UnityVersion not found at: $UnityExe"
    Write-Host "Install $UnityVersion via Unity Hub, or update the path in scripts/run-tests.ps1."
    exit 1
}

New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

function Run-Tests($testMode) {
    $resultsFile = Join-Path $ResultsDir "TestResults-$testMode.xml"
    Write-Host "Running $testMode tests..." -ForegroundColor Cyan

    & $UnityExe `
        -runTests `
        -testPlatform $testMode `
        -projectPath $ProjectPath `
        -testResults $resultsFile `
        -batchmode `
        -quit

    if ($LASTEXITCODE -eq 0) {
        Write-Host "$testMode tests PASSED" -ForegroundColor Green
    } else {
        Write-Host "$testMode tests FAILED (exit code: $LASTEXITCODE)" -ForegroundColor Red
    }
    return $LASTEXITCODE
}

$exitCode = 0

if ($Mode -eq "EditMode" -or $Mode -eq "All") {
    $result = Run-Tests "EditMode"
    if ($result -ne 0) { $exitCode = $result }
}

if ($Mode -eq "PlayMode" -or $Mode -eq "All") {
    $result = Run-Tests "PlayMode"
    if ($result -ne 0) { $exitCode = $result }
}

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "All tests passed." -ForegroundColor Green
} else {
    Write-Host "Some tests failed." -ForegroundColor Red
}

exit $exitCode
