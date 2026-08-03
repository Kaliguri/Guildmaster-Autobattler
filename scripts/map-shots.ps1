<#
.SYNOPSIS
    Снимки карты акта: по одной картинке на каждый сид в каждом профиле.

.DESCRIPTION
    Пайплайн разговора о карте (заказ Макса 2026-08-02): три сида на СТАРОМ профиле и те же три на
    НОВОМ — шесть картинок за раз. Сиды одни и те же в обоих профилях намеренно: иначе сравнение
    сравнивает не правки, а удачу расклада.

    Дамп берётся готовый и НЕ пересобирается: снимок обязан показывать ровно то, что сейчас на
    сайте. Тронул генератор или конфиг — сначала ./scripts/map-dump.ps1, потом сюда.

    Совместимость: Windows PowerShell 5.1, файл с BOM (см. lab-serve.ps1).

.PARAMETER Seeds
    Какие сиды снимать. Пусто — три случайных из дампа.

.PARAMETER Profiles
    Какие профили сравнивать. Пусто — первые два из дампа (asset и следующий).

.PARAMETER View
    all — весь акт целиком (композиция, силуэт, места под имена зон).
    frame — рабочий кадр камеры на floorsInView этажей: только здесь виден настоящий воздух между
    узлами, потому что игрок смотрит карту именно так, а не общим планом.

.EXAMPLE
    ./scripts/map-shots.ps1

.EXAMPLE
    ./scripts/map-shots.ps1 -Seeds 1004,1017,1042
#>
[CmdletBinding()]
param(
    [string]$Seeds = '',
    [string]$Profiles = '',
    [int]$Count = 3,
    [ValidateSet('all', 'frame')]
    [string]$View = 'all',
    [string]$Out = 'Temp/map-shots'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
if (-not $python) { throw 'Нужен Python 3 в PATH: рендер снимков стоит на PIL.' }

$dump = Join-Path $repo 'docs/lab/data/act-maps.json'
if (-not (Test-Path $dump)) { throw "Нет дампа: $dump. Сначала ./scripts/map-dump.ps1" }

$args = @(
    (Join-Path $PSScriptRoot 'map_shots.py'),
    '--dump', $dump,
    '--out', (Join-Path $repo $Out),
    '--count', $Count,
    '--view', $View
)
if ($Seeds)    { $args += @('--seeds', $Seeds) }
if ($Profiles) { $args += @('--profiles', $Profiles) }

& $python.Source @args
if ($LASTEXITCODE -ne 0) { throw 'Снимки не сделались.' }
