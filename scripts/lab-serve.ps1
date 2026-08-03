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

.PARAMETER Port
    Порт. По умолчанию 7400.

.PARAMETER NoBrowser
    Не открывать браузер.

.PARAMETER Watch
    Держать tsc в режиме наблюдения: правка .ts пересобирается сама, дальше F5 в браузере.

.EXAMPLE
    ./scripts/lab-serve.ps1

.EXAMPLE
    ./scripts/lab-serve.ps1 -Watch
#>
[CmdletBinding()]
param(
    [int]$Port = 7400,
    [switch]$NoBrowser,
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $repo 'docs/lab'
$wikiDir = Join-Path $repo 'docs/wiki'
$themeDir = Join-Path $repo 'Assets/_Project/UI/Theme'
$balanceData = Join-Path $repo 'BalanceReports/site/data.js'

if (-not (Test-Path $labDir)) { throw "Не нашла docs/lab по пути $labDir" }

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
    if ($Watch) {
        Write-Host 'tsc в режиме наблюдения: правка .ts пересобирается сама' -ForegroundColor DarkGray
        Start-Process -FilePath $node.Source -ArgumentList $tsc, '--watch', '--preserveWatchOutput' `
            -WorkingDirectory $labDir -WindowStyle Minimized
        Start-Sleep -Seconds 4   # первая сборка: иначе браузер откроется на пустом dist
    } else {
        & $node.Source $tsc
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript не собрался, сайт поднимать нечем.' }
    }
} finally { Pop-Location }

$server = Join-Path $PSScriptRoot 'lab_server.py'
$url = "http://localhost:$Port/"

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
