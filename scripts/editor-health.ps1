# Здоровье открытого редактора: во что обходится перезагрузка домена и не пора ли перезапустить Unity.
#
# Зачем: перезагрузка домена ДЕГРАДИРУЕТ в течение сессии. Замер 2026-07-31 по живому логу — первый
# reload сессии занял 1.0 секунды, последний (278-й) — 56.4 секунды; за сессию набежало 108 минут
# чистого ожидания. Проект тут ни при чём: на свежем редакторе домен грузится за секунду. Растёт
# что-то внутри самой сессии, и лечится это перезапуском, а не оптимизацией проекта.
#
# Скрипт читает лог редактора и говорит, где мы на этой кривой.
#
# Использование:
#   ./scripts/editor-health.ps1              # текущая сессия
#   ./scripts/editor-health.ps1 -Previous    # прошлая (Editor-prev.log)
#   ./scripts/editor-health.ps1 -Phases      # + разбивка последнего reload по фазам

param(
    # Смотреть прошлую сессию вместо текущей.
    [switch]$Previous,

    # Показать, на что ушло время в последней перезагрузке.
    [switch]$Phases,

    # Порог совета «перезапусти редактор», сек.
    [double]$RestartAdviceSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$logName = if ($Previous) { "Editor-prev.log" } else { "Editor.log" }
$logPath = Join-Path $env:LOCALAPPDATA "Unity/Editor/$logName"

if (-not (Test-Path $logPath)) { throw "Лог редактора не найден: $logPath" }

$lines = Get-Content -LiteralPath $logPath
$reloads = @()
$lastIndex = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^Domain Reload Profiling: (\d+)ms') {
        $reloads += [int]$Matches[1]
        $lastIndex = $i
    }
}

if ($reloads.Count -eq 0) {
    Write-Host "В логе нет ни одной перезагрузки домена ($logName)." -ForegroundColor DarkGray
    exit 0
}

$total = ($reloads | Measure-Object -Sum).Sum / 1000
$sorted = $reloads | Sort-Object
$median = $sorted[[int]($sorted.Count / 2)] / 1000
$head = if ($reloads.Count -ge 5) { ($reloads[0..4] | Measure-Object -Average).Average / 1000 } else { $reloads[0] / 1000 }
$tailCount = [Math]::Min(5, $reloads.Count)
$tail = ($reloads[($reloads.Count - $tailCount)..($reloads.Count - 1)] | Measure-Object -Average).Average / 1000

Write-Host "Перезагрузок домена: $($reloads.Count)   ($logName)" -ForegroundColor Cyan
Write-Host ("  суммарно ожидания: {0:N1} мин" -f ($total / 60))
Write-Host ("  медиана: {0:N1} с   максимум: {1:N1} с" -f $median, ($sorted[-1] / 1000))
Write-Host ("  первые пять: {0:N1} с   последние пять: {1:N1} с" -f $head, $tail)

if ($tail -gt $head * 2 -and $tail -gt $RestartAdviceSeconds) {
    $saved = ($tail - $head)
    Write-Host ""
    Write-Host ("ПЕРЕЗАПУСТИ РЕДАКТОР. Перезагрузка подорожала с {0:N1} до {1:N1} с — рестарт вернёт около {2:N0} секунд каждой следующей правке и окупится за пять." -f $head, $tail, $saved) -ForegroundColor Yellow
}
elseif ($tail -gt $RestartAdviceSeconds) {
    Write-Host ""
    Write-Host ("Перезагрузка стоит {0:N1} с — дорого, но роста по сессии не видно. Рестарт поможет мало." -f $tail) -ForegroundColor Yellow
}
else {
    Write-Host ""
    Write-Host ("Редактор в форме: последние перезагрузки по {0:N1} с." -f $tail) -ForegroundColor Green
}

if ($Phases -and $lastIndex -ge 0) {
    Write-Host ""
    Write-Host "На что ушла последняя перезагрузка:" -ForegroundColor Cyan
    for ($i = $lastIndex + 1; $i -lt [Math]::Min($lastIndex + 25, $lines.Count); $i++) {
        if ($lines[$i] -notmatch '^\s+\S') { break }   # блок кончился: строка без отступа
        Write-Host "  $($lines[$i])"
    }
}
