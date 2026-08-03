<#
.SYNOPSIS
    Снимок стендов Лаборатории картинкой: один стенд вместо целой страницы.

.DESCRIPTION
    Зачем отдельный скрипт. Показать картинку стенда в чате стоило полутора минут: браузер поднимал
    ВЕСЬ раздел (сорок с лишним канвасов с процедурной отрисовкой), профиль грелся с нуля, а нужен
    был один стенд. Здесь три вещи, каждая режет своё:

      1. `?shot=<id>` в Лаборатории строит голую страницу из одних канвасов (см. main.ts) —
         рисуется только запрошенное;
      2. профиль браузера постоянный и лежит в %TEMP% — прогрев случается один раз в жизни машины;
      3. кадр обрезается по цветовой метке, поэтому размер стенда знать не нужно.

    Сервер поднимается сам, если порт свободен, и гасится после съёмки. Уже поднятый lab-serve.ps1
    переиспользуется как есть — это заодно единственный способ снять то, что правится в watch-режиме.

    Совместимость: Windows PowerShell 5.1 (без ??, ?. и тернарника), файл с BOM — без него 5.1
    читает UTF-8 как ANSI и падает на первой кириллической строке.

.PARAMETER Stand
    Id стендов через запятую. Пусто — все рисующие стенды раздела -Page.

.PARAMETER Page
    Раздел Лаборатории (часть адреса после #/). Ускоряет поиск и нужен для съёмки раздела целиком.

.PARAMETER Scale
    Во сколько раз крупнее логического размера сцены. Мелкие детали на 1:1 не видны.

.PARAMETER Frame
    Кадр показа, 0..29. Снимок обязан быть воспроизводимым, поэтому часы стоят.

.EXAMPLE
    ./scripts/lab-shot.ps1 floor-time-sunset -Page floor -Scale 2

.EXAMPLE
    ./scripts/lab-shot.ps1 cloud-none,cloud-tint,cloud-ink -Page floor -Out Temp/clouds.png
#>
[CmdletBinding()]
param(
    # Массив, а не строка: без кавычек PowerShell сам разбирает `a,b,c` в список, и строковый
    # параметр на этом падает с невнятным «cannot convert value».
    [Parameter(Position = 0)][string[]]$Stand = @(),
    [string]$Page = '',
    [double]$Scale = 1,
    [int]$Frame = 0,
    [string]$Out = '',
    [int]$Port = 7400,
    [int]$Width = 1700,
    [int]$Height = 2600,
    [switch]$NoBuild,
    [switch]$NoCrop
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $repo 'docs/lab'
if (-not (Test-Path $labDir)) { throw "Не нашла docs/lab по пути $labDir" }

function Test-Port([int]$p) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $ok = $client.BeginConnect('127.0.0.1', $p, $null, $null).AsyncWaitHandle.WaitOne(300)
        return ($ok -and $client.Connected)
    } catch { return $false } finally { $client.Close() }
}

function Find-Browser {
    # Chrome и Edge — один движок, флаги общие. Edge держим фолбэком: на Windows он есть всегда.
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Google/Chrome/Application/chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Google/Chrome/Application/chrome.exe'),
        (Join-Path $env:LOCALAPPDATA 'Google/Chrome/Application/chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft/Edge/Application/msedge.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw 'Не нашла Chrome или Edge: снимать нечем.'
}

# --- сборка ---
if (-not $NoBuild) {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if (-not $node) { $node = Get-Command node -ErrorAction SilentlyContinue }
    if (-not $node) { throw 'Нужен Node.js в PATH: сайт собирается tsc.' }
    Push-Location $labDir
    try {
        & $node.Source (Join-Path $labDir 'node_modules/typescript/bin/tsc')
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript не собрался, снимать нечего.' }
    } finally { Pop-Location }
}

# --- сервер ---
$server = $null
if (-not (Test-Port $Port)) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
    if (-not $python) { throw 'Нужен Python 3 в PATH: сервер стоит на его стандартной библиотеке.' }
    $serverArgs = @(
        (Join-Path $PSScriptRoot 'lab_server.py'),
        '--port', $Port,
        '--root', $labDir,
        '--wiki', (Join-Path $repo 'docs/wiki'),
        '--theme', (Join-Path $repo 'Assets/_Project/UI/Theme'),
        '--balance', (Join-Path $repo 'BalanceReports/site/data.js')
    )
    $server = Start-Process -FilePath $python.Source -ArgumentList $serverArgs `
        -WindowStyle Hidden -PassThru
    $waited = 0
    while (-not (Test-Port $Port) -and $waited -lt 40) { Start-Sleep -Milliseconds 250; $waited++ }
    if (-not (Test-Port $Port)) { throw "Сервер не поднялся на порту $Port." }
}

try {
    # --- адрес ---
    $stands = ($Stand | Where-Object { $_ }) -join ','
    $query = "shot=$([Uri]::EscapeDataString($stands))&scale=$Scale&frame=$Frame"
    $url = "http://localhost:$Port/?$query"
    if ($Page) { $url += "#/$Page" }

    if (-not $Out) {
        $name = ''
        if ($Stand.Count -gt 0) { $name = $Stand[0] }
        if (-not $name) { $name = $Page }
        if (-not $name) { $name = 'shot' }
        $Out = Join-Path $repo "Temp/lab-shots/$name.png"
    } elseif (-not [System.IO.Path]::IsPathRooted($Out)) {
        $Out = Join-Path $repo $Out
    }
    $outDir = Split-Path -Parent $Out
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    if (Test-Path $Out) { Remove-Item $Out -Force }

    # --- съёмка ---
    # Профиль постоянный: свежий греется секундами и именно на этом сыпались прошлые попытки.
    $profileDir = Join-Path $env:TEMP 'guildmaster-lab-chrome'
    $browser = Find-Browser
    $browserArgs = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--no-first-run', '--no-default-browser-check', '--disable-extensions',
        '--force-device-scale-factor=1',
        "--user-data-dir=$profileDir",
        "--window-size=$Width,$Height",
        '--virtual-time-budget=8000',
        "--screenshot=$Out",
        $url
    )
    & $browser @browserArgs 2>$null | Out-Null
    if (-not (Test-Path $Out)) { throw "Браузер не создал снимок ($Out). Адрес: $url" }

    # --- кроп по метке ---
    # Страница в режиме съёмки красит фон магентой (shell.css): всё, что не она, — кадр.
    if (-not $NoCrop) {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
        if ($python) {
            $code = @'
import sys
from PIL import Image, ImageChops
path = sys.argv[1]
im = Image.open(path).convert("RGB")
box = ImageChops.difference(im, Image.new("RGB", im.size, (255, 0, 255))).getbbox()
if box:
    im.crop(box).save(path)
print("%dx%d" % Image.open(path).size)
'@
            $size = & $python.Source '-c' $code $Out
            Write-Host "снимок $size" -ForegroundColor DarkGray
        }
    }

    Write-Host $Out -ForegroundColor Yellow
} finally {
    if ($server) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}
