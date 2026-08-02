# Запуск тестов Unity локально через CLI
# Использование: ./scripts/run-tests.ps1
# Опции: ./scripts/run-tests.ps1 -Mode EditMode|PlayMode|All
#        ./scripts/run-tests.ps1 -Where Shadow          # редактор открыт — гнать по теневому проекту
#        ./scripts/run-tests.ps1 -Filter Guildmaster.Tests.EditMode.Content.EncounterLayoutTests

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Mode = "All",

    [ValidateSet("Auto", "Direct", "Shadow")]
    [string]$Where = "Auto",

    [string]$Filter,

    # Сколько ждать освобождения теневого проекта, если в нём уже гоняет другая сессия.
    # 0 = не ждать вовсе (упасть сразу, прежнее поведение).
    [int]$WaitMinutes = 20
)

Set-StrictMode -Version Latest

# Версия редактора, путь к Unity, детект открытого проекта и теневой проект — в общей обвязке:
# ими пользуется и балансный стенд (scripts/balance-headless.ps1), а два владельца одного способа
# запуска разъезжаются на первом же апгрейде Unity.
. "$PSScriptRoot/unity-cli.ps1"

$ProjectPath = $PSScriptRoot | Split-Path -Parent
$ResultsDir = Join-Path $ProjectPath "TestResults"
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

$locked = Test-UnityProjectLocked -ProjectPath $ProjectPath
$effectiveWhere = if ($Where -eq "Auto") { if ($locked) { "Shadow" } else { "Direct" } } else { $Where }

if ($effectiveWhere -eq "Direct" -and $locked) {
    Write-Error "Проект открыт в редакторе — Direct невозможен. Запусти с -Where Shadow (своя Library) или закрой Unity."
    exit 1
}

$TargetProject = $ProjectPath
if ($effectiveWhere -eq "Shadow") {
    Write-Host "Готовлю теневой проект (первый раз — полный импорт, это минуты)..." -ForegroundColor Cyan
    $TargetProject = Initialize-UnityShadowProject -ProjectPath $ProjectPath
    Wait-ForShadowProject -ShadowPath $TargetProject -TimeoutMinutes $WaitMinutes
}

Write-Host "Режим запуска: $effectiveWhere ($TargetProject)" -ForegroundColor DarkGray

Show-WorkingTreeWarning -ProjectPath $ProjectPath

function Get-TestRunCounts {
    <#
    .SYNOPSIS
    Сколько тестов реально прогналось по файлу результатов NUnit. Нет файла или он битый — считаем, что
    прогона не было: это и есть защита от «зелёного» прогона, который не запускался.
    #>
    param([Parameter(Mandatory)][string]$ResultsFile)

    $empty = [pscustomobject]@{ Total = 0; Passed = 0; Failed = 0 }
    if (-not (Test-Path $ResultsFile)) { return $empty }

    try {
        [xml]$xml = Get-Content $ResultsFile -Raw
        $run = $xml.'test-run'
        return [pscustomobject]@{
            Total  = [int]$run.total
            Passed = [int]$run.passed
            Failed = [int]$run.failed
        }
    } catch {
        return $empty
    }
}

function Run-Tests($testMode) {
    $resultsFile = Join-Path $ResultsDir "TestResults-$testMode.xml"
    $logFile = Join-Path $env:TEMP "guildmaster-tests/$testMode.log"
    Write-Host "Running $testMode tests..." -ForegroundColor Cyan

    $extra = @("-runTests", "-testPlatform", $testMode, "-testResults", $resultsFile)
    if ($Filter) { $extra += @("-testFilter", $Filter) }

    if (Test-Path $resultsFile) { Remove-Item $resultsFile -Force }

    $code = Invoke-UnityBatch -ProjectPath $TargetProject -LogFile $logFile -ExtraArgs $extra

    # Нулевой код сам по себе НЕ значит «тесты прошли»: Unity возвращает 0 и когда прогон не состоялся
    # вовсе (не собрался фильтр, редактор умер до запуска раннера). Верим только файлу результатов.
    $counts = Get-TestRunCounts -ResultsFile $resultsFile
    if ($code -eq 0 -and $counts.Total -gt 0 -and $counts.Failed -eq 0) {
        Write-Host "$testMode tests PASSED ($($counts.Passed) из $($counts.Total))" -ForegroundColor Green
        return 0
    }

    if ($counts.Total -eq 0) {
        Write-Host "${testMode}: прогон НЕ состоялся — нет результатов в $resultsFile (код выхода $code)." -ForegroundColor Red
    } else {
        Write-Host "$testMode tests FAILED ($($counts.Failed) из $($counts.Total), код $code)" -ForegroundColor Red
        Write-Host "Результаты: $resultsFile"
    }

    Show-UnityLogTail -LogFile $logFile
    if ($code -ne 0) { return $code }
    return 1
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
