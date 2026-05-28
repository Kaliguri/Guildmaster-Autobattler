# Unity Test Runner — локальный запуск тестов через CLI
# Использование: .\scripts\run-tests.ps1 [-Mode EditMode|PlayMode|All]

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All"
)

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe"
$ProjectPath = $PSScriptRoot | Split-Path -Parent
$ResultsDir = Join-Path $ProjectPath "TestResults"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity не найден по пути: $UnityExe"
    Write-Host "Проверь путь в Unity Hub или обнови переменную `$UnityExe в этом скрипте."
    exit 1
}

if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir | Out-Null
}

function Run-Tests {
    param([string]$TestMode)

    $ResultsFile = Join-Path $ResultsDir "TestResults-$TestMode.xml"
    Write-Host ""
    Write-Host "==> Запуск $TestMode тестов..." -ForegroundColor Cyan

    & $UnityExe `
        -runTests `
        -testPlatform $TestMode `
        -projectPath $ProjectPath `
        -testResults $ResultsFile `
        -batchmode `
        -quit

    if ($LASTEXITCODE -eq 0) {
        Write-Host "==> $TestMode: PASSED" -ForegroundColor Green
    } else {
        Write-Host "==> $TestMode: FAILED (exit code $LASTEXITCODE)" -ForegroundColor Red
    }

    if (Test-Path $ResultsFile) {
        Write-Host "    Результаты: $ResultsFile"
    }

    return $LASTEXITCODE
}

$exitCodes = @()

if ($Mode -eq "EditMode" -or $Mode -eq "All") {
    $exitCodes += Run-Tests "EditMode"
}

if ($Mode -eq "PlayMode" -or $Mode -eq "All") {
    $exitCodes += Run-Tests "PlayMode"
}

Write-Host ""
if ($exitCodes -contains 1) {
    Write-Host "Некоторые тесты упали. Смотри XML-отчёты в: $ResultsDir" -ForegroundColor Red
    exit 1
} else {
    Write-Host "Все тесты прошли успешно." -ForegroundColor Green
    exit 0
}
