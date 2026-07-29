# Прогон балансного стенда без открытого редактора.
#
#   ./scripts/balance-headless.ps1 -Title "TTK-проход" -Summary "BAL-005, вариант 1"
#   ./scripts/balance-headless.ps1 -Benches encounters,dps
#   ./scripts/balance-headless.ps1 -Mode Shadow        # принудительно теневой проект
#
# Прогон — мини-коммит: с -Title скрипт сам ставит маркер (scripts/balance-run.py), и снимки перестают
# быть безымянными. Без -Title маркер не ставится, и снимки достанутся предыдущему прогону — так что
# либо название, либо осознанное «доснять к тому же прогону».
#
# Режимы: Direct — редактор закрыт, гоним прямо по репозиторию. Shadow — редактор открыт, гоним по
# теневому проекту (своя Library, живые Assets через junction; см. scripts/unity-cli.ps1). Auto
# выбирает сам по тому, держит ли кто-то lockfile.

param(
    [string]$Benches = "all",
    [string]$Title,
    [string]$Summary = "",
    [ValidateSet("Auto", "Direct", "Shadow")][string]$Mode = "Auto",
    [switch]$OpenSite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. "$PSScriptRoot/unity-cli.ps1"

$ProjectPath = $PSScriptRoot | Split-Path -Parent
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $env:TEMP "guildmaster-balance-headless/round_$stamp.log"

# 1. Маркер прогона — до бенчей, иначе снимки останутся безымянными.
if ($Title) {
    $py = @("python", "python3", "py") | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $py) { throw "Python не найден — маркер прогона не поставить. Поставь Python или запусти без -Title." }
    & $py (Join-Path $PSScriptRoot "balance-run.py") start $Title $Summary
    if ($LASTEXITCODE -ne 0) { throw "Не удалось открыть прогон (balance-run.py вернул $LASTEXITCODE)" }
}

# 2. Где гоняем.
$locked = Test-UnityProjectLocked -ProjectPath $ProjectPath
$effectiveMode = if ($Mode -eq "Auto") { if ($locked) { "Shadow" } else { "Direct" } } else { $Mode }

if ($effectiveMode -eq "Direct" -and $locked) {
    throw "Проект открыт в редакторе — Direct невозможен (второй Unity не откроет тот же проект). Запусти с -Mode Shadow."
}

$target = $ProjectPath
if ($effectiveMode -eq "Shadow") {
    Write-Host "Готовлю теневой проект (первый раз — полный импорт, это минуты)..." -ForegroundColor Cyan
    $target = Initialize-UnityShadowProject -ProjectPath $ProjectPath
}

Write-Host "Режим: $effectiveMode" -ForegroundColor Cyan
Write-Host "Проект: $target"
Write-Host "Бенчи: $Benches"
Write-Host "Лог: $logFile"

# 3. Круг.
$started = Get-Date
$code = Invoke-UnityBatch -ProjectPath $target -LogFile $logFile -ExtraArgs @(
    "-executeMethod", "Guildmaster.Balance.Editor.BalanceCli.Run",
    "-benches", $Benches
)
$elapsed = (Get-Date) - $started

Write-Host ""
Write-Host ("Прогон занял {0:mm\:ss}" -f $elapsed)

# Пути записанных отчётов вытаскиваем из лога: в batchmode консоль редактора больше нигде не видна.
if (Test-Path $logFile) {
    $written = Select-String -Path $logFile -Pattern '^(CSV|MD):\s' | ForEach-Object { $_.Line.Trim() }
    if ($written) {
        Write-Host "Записано:" -ForegroundColor DarkGray
        $written | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    }
}

if ($code -ne 0) {
    Write-Host "Круг НЕ прогнан целиком (код $code)." -ForegroundColor Red
    Show-UnityLogTail -LogFile $logFile
    exit $code
}

Write-Host "Круг прогнан целиком." -ForegroundColor Green
$site = Join-Path $ProjectPath "BalanceReports/site/index.html"
Write-Host "Сайт отчётов: $site"
if ($OpenSite -and (Test-Path $site)) { Start-Process $site }

exit 0
