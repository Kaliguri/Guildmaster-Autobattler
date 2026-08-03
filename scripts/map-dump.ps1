<#
.SYNOPSIS
    Дампит пачку карт акта в JSON для стенда Лаборатории. Unity для этого не нужен.

.DESCRIPTION
    Гоняет НАСТОЯЩИЙ MapGenerator вне редактора: генератор и его конфиг — чистый C# без единого
    обращения к движку, поэтому их можно собрать обычным dotnet и выполнить за пару секунд.

    Числа берутся из ActConfig.asset, а не из дефолтов кода: играет ассет. Карты пишутся пачкой
    (сиды идут подряд от -Seed), потому что у статического сайта нет кнопки «сгенерируй ещё», а
    шестьдесят вариантов на глаз от неё неотличимы. Сид каждой карты лежит в дампе — увидел кривую,
    назвал число, воспроизвёл в редакторе.

    Совместимость: Windows PowerShell 5.1 (никаких операторов 7-й версии), файл с BOM — иначе 5.1
    читает UTF-8 как ANSI и ломается на кириллице.

.PARAMETER Count
    Сколько карт сгенерировать. По умолчанию 60.

.PARAMETER Seed
    Сид первой карты; дальше подряд. По умолчанию 1000 — чтобы набор был воспроизводим.

.PARAMETER Out
    Куда писать, относительно корня репозитория. По умолчанию docs/lab/data/act-maps.json.

.EXAMPLE
    ./scripts/map-dump.ps1

.EXAMPLE
    ./scripts/map-dump.ps1 -Count 200 -Seed 5000
#>
[CmdletBinding()]
param(
    [int]$Count = 60,
    [uint64]$Seed = 1000,
    [string]$Out = 'docs/lab/data/act-maps.json'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/unity-cli.ps1"   # владелец пути к установке редактора

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'Нужен .NET SDK в PATH: дампер собирается им.' }

# Managed-сборки редактора нужны ровно ради UnityEngine.CoreModule: RunState.cs (там же, где MapNode)
# держит Vector2 в слоте ростера. Заглушку вместо неё не подставляем — подделка типа разъезжается
# с настоящим молча.
$editorRoot = Get-UnityEditorRoot -ProjectPath $repo
$managed = Join-Path $editorRoot 'Data/Managed'
if (-not (Test-Path (Join-Path $managed 'UnityEngine/UnityEngine.CoreModule.dll'))) {
    throw "Не нашла UnityEngine.CoreModule.dll в $managed"
}

# Ссылки на Guildmaster.Core/Data берутся из Library/ScriptAssemblies: их собирает редактор, и они
# нужны только ради SaveSchema, ContentIds и IRngService. Сам генератор компилируется ИЗ ИСХОДНИКОВ
# (см. MapDump.csproj) — иначе правка MapGenerator.cs не доехала бы до стенда до ближайшего reload.
$scriptAssemblies = Join-Path $repo 'Library/ScriptAssemblies'
if (-not (Test-Path (Join-Path $scriptAssemblies 'Guildmaster.Core.dll'))) {
    throw "Не нашла Library/ScriptAssemblies/Guildmaster.Core.dll. Открой проект в Unity хотя бы раз — сборки берутся оттуда."
}

$csproj = Join-Path $repo 'tools/MapDump/MapDump.csproj'
$props = @(
    "-p:ProjectRoot=$repo",
    "-p:UnityManagedDir=$managed"
)

Write-Host 'Собираю дампер...' -ForegroundColor DarkGray
& $dotnet.Source build $csproj -c Release --nologo -v quiet @props | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Дампер не собрался.' }

$exe = Join-Path $repo 'Temp/MapDump/bin/Release/net8.0/MapDump.dll'
if (-not (Test-Path $exe)) { throw "Сборка прошла, а MapDump.dll не нашёлся: $exe" }

& $dotnet.Source $exe --project $repo --out $Out --count $Count --seed $Seed
if ($LASTEXITCODE -ne 0) { throw 'Дамп не состоялся.' }
