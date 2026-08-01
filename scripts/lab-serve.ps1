<#
.SYNOPSIS
    Поднимает локальную Лабораторию: docs/lab на http://localhost:7400 и открывает браузер.

.DESCRIPTION
    Сайт написан так, чтобы открываться и двойным кликом по docs/lab/index.html, но полный набор
    возможностей требует http: указатель по ГДД читается через fetch, а браузеры запрещают fetch по
    file:// (CORS), и localStorage там же бывает недоступен — настройки показа забудутся.

    Сервер — http.server из стандартной библиотеки Python плюс один свой обработчик: /api/gdd-index
    строит указатель по docs/wiki на лету. Именно на лету, а не файлом на диске: сохранённый
    указатель — вторая копия оглавления вики, и она начнёт врать в тот день, когда кто-то
    переименует заметку. Ничего не устанавливается, ничего не коммитится.

    Сайт написан на TypeScript и собирается tsc в docs/lab/dist. Скрипт собирает его сам перед
    стартом, чтобы «поднять лабораторию» осталось одной командой, а не тремя.

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

if (-not (Test-Path $labDir)) { throw "Не нашла docs/lab по пути $labDir" }

$python = (Get-Command python -ErrorAction SilentlyContinue) ?? (Get-Command py -ErrorAction SilentlyContinue)
if (-not $python) { throw 'Нужен Python 3 в PATH: сервер стоит на его стандартной библиотеке.' }

# --- сборка TypeScript ---
# Зависимость ровно одна (typescript), поэтому ставим молча при первом запуске: спрашивать
# «а вы установили пакеты?» у инструмента, который должен подниматься одной командой, — глупо.
$npm = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npm) { throw 'Нужен Node.js с npm в PATH: сайт собирается tsc.' }

if (-not (Test-Path (Join-Path $labDir 'node_modules'))) {
    Write-Host 'Первый запуск: ставлю typescript...' -ForegroundColor DarkGray
    Push-Location $labDir
    try { & npm install --no-audit --no-fund | Out-Null } finally { Pop-Location }
}

Push-Location $labDir
try {
    if ($Watch) {
        Write-Host 'tsc в режиме наблюдения: правка .ts пересобирается сама' -ForegroundColor DarkGray
        Start-Process -FilePath 'npx' -ArgumentList 'tsc', '--watch', '--preserveWatchOutput' `
            -WorkingDirectory $labDir -WindowStyle Minimized
        Start-Sleep -Seconds 3   # первая сборка: иначе браузер откроется на пустом dist
    } else {
        & npx tsc
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript не собрался — сайт поднимать нечем.' }
    }
} finally { Pop-Location }

$server = Join-Path $PSScriptRoot 'lab_server.py'
$url = "http://localhost:$Port/"

Write-Host "Лаборатория: $url" -ForegroundColor Yellow
Write-Host "корень $labDir, вики $repo/docs/wiki" -ForegroundColor DarkGray
Write-Host 'остановить — Ctrl+C' -ForegroundColor DarkGray

if (-not $NoBrowser) {
    Start-Job -ScriptBlock {
        param($u)
        Start-Sleep -Milliseconds 700
        Start-Process $u
    } -ArgumentList $url | Out-Null
}

& $python.Source $server --port $Port --root $labDir --wiki (Join-Path $repo 'docs/wiki')
