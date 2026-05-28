# Запуск тестов Unity локально через CLI
# Использование: ./scripts/run-tests.ps1
# Опции: ./scripts/run-tests.ps1 -Mode EditMode|PlayMode|All

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All"
)

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe"
$ProjectPath = $PSScriptRoot | Split-Path -Parent
$ResultsDir = Join-Path $ProjectPath "TestResults"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity not found at: $UnityExe"
    Write-Host "Update the path in scripts/run-tests.ps1 to match your Unity installation."
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
