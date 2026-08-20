<#
.SYNOPSIS
    Поднимает локальную Лабораторию: docs/lab на http://localhost:7400 и открывает браузер.

.DESCRIPTION
    Сайт собирается tsc из docs/lab/src и раздаётся по http. Через http, а не с диска, потому что
    ES-модули и fetch по file:// запрещены CORS-политикой браузера, а указателю по ГДД нужен и тот,
    и другой.

    Сервер — http.server из стандартной библиотеки Python плюс один свой обработчик: /api/gdd-index
    строит указатель по docs/wiki на лету. Именно на лету, а не файлом на диске: сохранённый
    указатель — вторая копия оглавления вики, и она начнёт врать в тот день, когда кто-то
    переименует заметку.

    Совместимость: скрипт держится Windows PowerShell 5.1 — именно в нём консоль открывается по
    умолчанию. Поэтому здесь нет операторов 7-й версии (??, ?., тернарника), а файл сохранён с BOM:
    без него 5.1 читает UTF-8 как ANSI и ломается на первой же кириллической строке.

    Повторный запуск безопасен: если порт уже слушают, скрипт НЕ поднимает второй сервер (он бы и
    не поднялся — порт занят, а падение выглядело бы как поломка сайта), а просто открывает браузер
    на нужном разделе уже работающей Лаборатории.

.PARAMETER Page
    Раздел, на котором открыть браузер: часть адреса после #/ — например `meta-unlocks`. Пусто —
    главная. Открывать сайт файлом с диска нельзя: по file:// браузер запрещает ES-модули и fetch,
    и раздел просто не загрузится.

.PARAMETER Port
    Порт. По умолчанию 7400.

.PARAMETER NoBrowser
    Не открывать браузер.

.PARAMETER NoWatch
    Собрать один раз и не следить за правками. По умолчанию tsc держится в режиме наблюдения:
    правка .ts пересобирается сама, дальше F5 в браузере. Наблюдение — именно поведение по
    умолчанию, потому что забытый watcher выглядит как «правка не подхватилась» и стоит получаса
    поисков не там.

.EXAMPLE
    ./scripts/lab-serve.ps1

.EXAMPLE
    ./scripts/lab-serve.ps1 meta-unlocks
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Page = '',
    [int]$Port = 7400,
    [switch]$NoBrowser,
    [switch]$NoWatch
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $repo 'docs/lab'
$wikiDir = Join-Path $repo 'docs/wiki'
$themeDir = Join-Path $repo 'Assets/_Project/UI/Theme'
$balanceData = Join-Path $repo 'BalanceReports/site/data.js'

if (-not (Test-Path $labDir)) { throw "Не нашла docs/lab по пути $labDir" }

# Адрес раздела. Ведущие #/ и / отрезаем, чтобы принимался и `meta-unlocks`, и `#/meta-unlocks`.
$url = "http://localhost:$Port/"
if ($Page) { $url = $url + '#/' + $Page.TrimStart('#', '/') }

# --- уже поднята? ---
# Второй сервер на тот же порт не встанет, и падение читалось бы как поломка сайта. Поэтому
# занятый порт — это не ошибка, а «всё уже готово, открываю нужную страницу».
$alive = $false
try {
    $probe = New-Object System.Net.Sockets.TcpClient
    $probe.Connect('127.0.0.1', $Port)
    $alive = $true
    $probe.Close()
} catch {
    $alive = $false
}

if ($alive) {
    Write-Host "Лаборатория уже поднята: $url" -ForegroundColor Yellow

    # Наблюдение НЕ подразумевается: сервер мог поднять lab-shot.ps1 (он делает это сам ради
    # съёмки) или прежний запуск с -NoWatch. Тогда правка .ts не попадёт в dist, и это выглядит
    # как «сайт показывает вчерашнее» — самая дорогая готча этого инструмента. Поэтому спрашиваем
    # у системы, а не предполагаем.
    $watching = Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*tsc*' -and $_.CommandLine -like '*--watch*' }

    if ($watching) {
        Write-Host 'watcher на месте: правку .ts пересоберёт сам, в браузере F5' -ForegroundColor DarkGray
    } else {
        Write-Host 'ВНИМАНИЕ: сервер поднят БЕЗ наблюдения — правки .ts в dist не попадут.' -ForegroundColor Red
        Write-Host 'собираю один раз сейчас; для постоянного наблюдения останови сервер и запусти скрипт заново' -ForegroundColor DarkGray
        Push-Location $labDir
        try {
            $nodeNow = Get-Command node.exe -ErrorAction SilentlyContinue
            if (-not $nodeNow) { $nodeNow = Get-Command node -ErrorAction SilentlyContinue }
            if ($nodeNow) {
                & $nodeNow.Source (Join-Path $labDir 'node_modules/typescript/bin/tsc')
                if ($LASTEXITCODE -ne 0) { Write-Host 'tsc не собрался: сайт покажет прежний dist.' -ForegroundColor Red }
            }
        } finally { Pop-Location }
    }

    if (-not $NoBrowser) { Start-Process $url }
    return
}

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
if (-not $python) { throw 'Нужен Python 3 в PATH: сервер стоит на его стандартной библиотеке.' }

# --- сборка TypeScript ---
# Зависимость ровно одна (typescript), поэтому ставим молча при первом запуске: спрашивать
# «а вы установили пакеты?» у инструмента, который должен подниматься одной командой, — глупо.
$npm = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (-not $npm) { $npm = Get-Command npm -ErrorAction SilentlyContinue }
if (-not $npm) { throw 'Нужен Node.js с npm в PATH: сайт собирается tsc.' }

$tsc = Join-Path $labDir 'node_modules/typescript/bin/tsc'
if (-not (Test-Path (Join-Path $labDir 'node_modules'))) {
    Write-Host 'Первый запуск: ставлю typescript...' -ForegroundColor DarkGray
    Push-Location $labDir
    try {
        & $npm.Source install --no-audit --no-fund | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'npm install не прошёл.' }
    } finally { Pop-Location }
}

# Зовём tsc напрямую через node, а не через npx: npx.cmd в 5.1 приходится запускать оболочкой,
# и код возврата теряется — сборка молча падает, а сервер поднимается на старом dist.
$node = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $node) { $node = Get-Command node -ErrorAction SilentlyContinue }
if (-not $node) { throw 'Нужен Node.js в PATH.' }

Push-Location $labDir
try {
    if ($NoWatch) {
        & $node.Source $tsc
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript не собрался, сайт поднимать нечем.' }
    } else {
        Write-Host 'tsc в режиме наблюдения: правка .ts пересобирается сама' -ForegroundColor DarkGray
        Start-Process -FilePath $node.Source -ArgumentList $tsc, '--watch', '--preserveWatchOutput' `
            -WorkingDirectory $labDir -WindowStyle Minimized
        Start-Sleep -Seconds 4   # первая сборка: иначе браузер откроется на пустом dist
    }
} finally { Pop-Location }

$server = Join-Path $PSScriptRoot 'lab_server.py'

Write-Host "Лаборатория: $url" -ForegroundColor Yellow
Write-Host "корень $labDir, вики $wikiDir" -ForegroundColor DarkGray
Write-Host 'остановить: Ctrl+C' -ForegroundColor DarkGray

if (-not $NoBrowser) {
    Start-Job -ScriptBlock {
        param($u)
        Start-Sleep -Milliseconds 700
        Start-Process $u
    } -ArgumentList $url | Out-Null
}

& $python.Source $server --port $Port --root $labDir --wiki $wikiDir --theme $themeDir --balance $balanceData
