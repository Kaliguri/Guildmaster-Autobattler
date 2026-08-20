<#
.SYNOPSIS
    Быстрый прогон EditMode-тестов БЕЗ редактора: секунды вместо минут.

.DESCRIPTION
    Гоняет тесты как обычный .NET-код: сборки уже собраны Roslyn'ом (compile-check.ps1), раннер
    грузит их и зовёт методы с [Test] напрямую. Запуск Unity в batchmode стоит порядка полуминуты
    оверхеда — лицензия, импорт, domain reload, компиляция, — при том что сами тесты считаются
    секунды.

    ЧТО ЭТО НЕ ЗАМЕНЯЕТ. Тесты, которым нужен живой движок (CreateInstance, Debug.Log, загрузка
    ассетов), здесь падают на нативных вызовах. Такие НЕ считаются ни успехом, ни провалом: они
    выводятся отдельной строкой «нужен редактор». Полный прогон остаётся за run-tests.ps1 и за CI —
    этот скрипт отвечает на вопрос «я не сломала логику», а не «всё зелено».

    Пустой прогон считается ПРОВАЛОМ (код 2), как и в run-tests.ps1: опечатка в фильтре не должна
    выглядеть успехом.

    Совместимость: Windows PowerShell 5.1, файл с BOM.

.PARAMETER Filter
    Подстрока полного имени теста (Namespace.Class.Method), без учёта регистра.

.PARAMETER Assemblies
    Какие тестовые сборки гонять. По умолчанию обе: Guildmaster.Tests.EditMode, Guildmaster.Balance.Tests.

.PARAMETER NoBuild
    Не звать compile-check: гонять по тому, что уже лежит в его выходной папке.

.EXAMPLE
    ./scripts/fast-tests.ps1

.EXAMPLE
    ./scripts/fast-tests.ps1 -Filter Ember
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [string[]]$Assemblies = @('Guildmaster.Tests.EditMode', 'Guildmaster.Balance.Tests'),
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/unity-cli.ps1"   # владелец пути к установке редактора

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'Нужен .NET SDK в PATH: раннер собирается им.' }

# Сборки берутся у compile-check — единственного владельца команд компиляции. Своя компиляция здесь
# завела бы второй набор дефайнов и ссылок, и «быстрый» прогон проверял бы не тот код.
$binDir = Join-Path $env:LOCALAPPDATA 'Guildmaster-CompileCheck'

if (-not $NoBuild) {
    Write-Host 'Собираю сборки (compile-check)...' -ForegroundColor DarkGray
    & "$PSScriptRoot/compile-check.ps1" -Assembly $Assemblies | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Не компилируется — прогон не имеет смысла. Подробности: ./scripts/compile-check.ps1' -ForegroundColor Red
        exit 2
    }
}

foreach ($name in $Assemblies) {
    $dll = Join-Path $binDir "$name.dll"
    if (-not (Test-Path $dll)) { throw "Нет собранной $name.dll. Прогони ./scripts/compile-check.ps1 -Assembly $name" }
}

# Где искать всё остальное: Managed редактора (UnityEngine и mscorlib-совместимые), кэш пакетов
# (nunit.framework) и собранные редактором сборки на случай, если чего-то нет у compile-check.
$editorRoot = Get-UnityEditorRoot -ProjectPath $repo
$probe = @(
    (Join-Path $editorRoot 'Data/Managed'),
    (Join-Path $repo 'Library/PackageCache'),
    (Join-Path $repo 'Library/ScriptAssemblies')
) -join ';'

$csproj = Join-Path $repo 'tools/FastTests/FastTests.csproj'
& $dotnet.Source build $csproj -c Release --nologo -v quiet "-p:ProjectRoot=$repo" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Раннер не собрался.' }

$exe = Join-Path $repo 'Temp/FastTests/bin/Release/net8.0/FastTests.dll'
if (-not (Test-Path $exe)) { throw "Сборка прошла, а FastTests.dll не нашёлся: $exe" }

# Каждая сборка — свой процесс. Дело не в изоляции ради чистоты: движковые объекты зовут логгер из
# ФИНАЛИЗАТОРА, а исключение в потоке сборщика мусора поймать нельзя — оно убивает процесс целиком.
# Логгер мы глушим, но если такое всё же случится, потерять надо один набор, а не весь прогон.
# Тесты, которые вне редактора ЗАПУСКАЮТСЯ, но меряют не то. Список ведётся руками и печатается
# поимённо: каждая строка — обязательство объяснить, почему тест здесь не судья.
#   CaptureTick_DoesNotAllocate — считает аллокации через GC; вне Unity сборщик другой, и число
#   говорит о .NET, а не о нашем кольцевом буфере.
$editorOnly = @(
    'CaptureTick_DoesNotAllocate'
) -join ','

$worst = 0
$ranSomething = $false
foreach ($name in $Assemblies) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan

    $runnerArgs = @('--bin', $binDir, '--assemblies', $name, '--probe', $probe, '--editor-only', $editorOnly)
    if ($Filter) { $runnerArgs += @('--filter', $Filter) }

    & $dotnet.Source $exe @runnerArgs
    $code = $LASTEXITCODE

    if ($code -eq 3) {
        # Пусто в ОДНОЙ сборке — обычное дело при фильтре. Бедой это становится, только если пусто
        # везде, и решается это ниже, когда видны все наборы.
        continue
    }

    $ranSomething = $true

    # Процесс умер не своей смертью (финализатор, StackOverflow) — это не «тесты упали», а
    # «прогон не состоялся»: сказать надо разными словами, иначе пойдёшь чинить не то.
    if ($code -gt 3) {
        Write-Host "$name — прогон ОБОРВАЛСЯ (код $code): процесс умер, отчёт неполный." -ForegroundColor Red
    }
    if ($code -gt $worst) { $worst = $code }
}

Write-Host ""
if (-not $ranSomething) {
    Write-Host "Прогон НЕ состоялся: ни одного теста не запущено ни в одной сборке (опечатка в фильтре?)." -ForegroundColor Red
    exit 2
}

if ($worst -eq 0) {
    Write-Host "Быстрый прогон зелёный. Полный — ./scripts/run-tests.ps1 и CI." -ForegroundColor Green
}
exit $worst
