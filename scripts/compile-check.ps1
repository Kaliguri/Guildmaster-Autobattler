# Проверка компиляции C# БЕЗ запуска Unity.
#
# Зачем: цикл «сохранил скрипт → редактор снова готов» стоит около пятнадцати секунд, из которых сама
# компиляция занимает миллисекунды — всё остальное это перезагрузка домена и проход asset pipeline.
# Агенту в параллельной сессии нужен ответ ровно на один вопрос: «код компилируется?». Здесь он
# получает его за пару секунд, не трогая редактор Макса и не морозя ему тулинг ошибкой компиляции.
#
# Как: компилятор Roslyn лежит в самой установке редактора, а готовые команды компиляции (со всеми
# ссылками, дефайнами и анализаторами) Unity уже написала в Library/Bee/artifacts/*.dag/*.rsp.
# Скрипт берёт их, подменяет выход на временную папку вне репозитория и запускает csc.
#
# Использование:
#   ./scripts/compile-check.ps1                      # только сборки, задетые правками (git)
#   ./scripts/compile-check.ps1 -All                 # все наши сборки
#   ./scripts/compile-check.ps1 -Assembly Guildmaster.Combat,Guildmaster.Data
#   ./scripts/compile-check.ps1 -Meta                # заодно завести .meta новым .cs

param(
    # Явный список сборок. Пусто = определить по изменённым файлам, либо все при -All.
    [string[]]$Assembly,

    # Проверить все сборки, у которых есть .rsp.
    [switch]$All,

    # Сгенерировать .meta для .cs, у которых его нет (иначе Unity не увидит тип, и SerializeReference
    # даст null-компонент — готча, на которой уже спотыкались).
    [switch]$Meta,

    # Не останавливаться на первой сборке с ошибками.
    [switch]$KeepGoing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectPath = $PSScriptRoot | Split-Path -Parent
. "$PSScriptRoot/unity-cli.ps1"   # версия редактора и путь к установке — общий владелец

# ---------------------------------------------------------------------------------------------
# Окружение: компилятор и папка артефактов Unity
# ---------------------------------------------------------------------------------------------

function Get-RoslynCscPath {
    $csc = Join-Path (Get-UnityEditorRoot -ProjectPath $ProjectPath) "Data/DotNetSdkRoslyn/csc.dll"
    if (-not (Test-Path $csc)) { throw "Компилятор Roslyn не найден: $csc" }
    return $csc
}

function Get-BeeDagDir {
    <#
    .SYNOPSIS
    Папка артефактов Bee с ответными файлами (.rsp) компиляции. Их пишет Unity; без неё проверять нечем.
    #>
    $artifacts = Join-Path $ProjectPath "Library/Bee/artifacts"
    if (-not (Test-Path $artifacts)) {
        throw "Нет Library/Bee/artifacts — проект ни разу не собирался редактором. Открой Unity один раз."
    }

    $dag = Get-ChildItem -LiteralPath $artifacts -Directory -Filter "*.dag" |
        Where-Object { Get-ChildItem -LiteralPath $_.FullName -Filter "*.rsp" -File | Select-Object -First 1 } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $dag) { throw "В Library/Bee/artifacts нет ни одного .rsp. Открой Unity, чтобы она собрала скрипты." }
    return $dag.FullName
}

# ---------------------------------------------------------------------------------------------
# Карта сборок проекта
# ---------------------------------------------------------------------------------------------

function Get-AsmdefMap {
    <#
    .SYNOPSIS
    Все сборки проекта: имя → { Dir, Name, References }. Ссылки приводятся к именам (asmdef разрешает
    писать их и как GUID, и это не редкость у вендорных пакетов).
    #>
    $byName = @{}
    $guidToName = @{}

    $files = Get-ChildItem -LiteralPath (Join-Path $ProjectPath "Assets") -Recurse -Filter "*.asmdef" -File

    foreach ($file in $files) {
        $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $name = $json.name

        $refs = @()
        if ($json.PSObject.Properties.Name -contains "references" -and $json.references) { $refs = @($json.references) }

        $byName[$name] = [pscustomobject]@{
            Name       = $name
            Dir        = $file.DirectoryName
            RawRefs    = $refs
            References = @()
        }

        # guid из .meta — чтобы позже развернуть ссылки вида "GUID:0123abcd..."
        $meta = "$($file.FullName).meta"
        if (Test-Path $meta) {
            $line = Get-Content -LiteralPath $meta | Where-Object { $_ -match '^guid:\s*([0-9a-f]+)' } | Select-Object -First 1
            if ($line -and $line -match '^guid:\s*([0-9a-f]+)') { $guidToName[$Matches[1]] = $name }
        }
    }

    foreach ($asm in $byName.Values) {
        $resolved = @()
        foreach ($raw in $asm.RawRefs) {
            if ($raw -like "GUID:*") {
                $guid = $raw.Substring(5)
                if ($guidToName.ContainsKey($guid)) { $resolved += $guidToName[$guid] }
            }
            else { $resolved += $raw }
        }
        $asm.References = $resolved
    }

    return $byName
}

function Get-AssemblySources {
    <#
    .SYNOPSIS
    Исходники сборки — с ДИСКА, а не из .rsp. Именно это делает проверку устойчивой к новым файлам:
    список внутри .rsp зафиксирован на момент последней сборки редактором, и свежесозданный агентом
    файл в него не попадает — то есть не проверялся бы вовсе.
    Вложенные сборки исключаются: их файлы принадлежат другому asmdef.
    #>
    param([Parameter(Mandatory)][string]$Dir)

    $nested = Get-ChildItem -LiteralPath $Dir -Recurse -Filter "*.asmdef" -File |
        Where-Object { $_.DirectoryName -ne $Dir } |
        ForEach-Object { $_.DirectoryName }

    Get-ChildItem -LiteralPath $Dir -Recurse -Filter "*.cs" -File |
        Where-Object {
            $path = $_.DirectoryName
            -not ($nested | Where-Object { $path -eq $_ -or $path.StartsWith("$_$([IO.Path]::DirectorySeparatorChar)") })
        } |
        ForEach-Object { $_.FullName }
}

function Get-ChangedAssemblies {
    <#
    .SYNOPSIS
    Сборки, задетые правками рабочего дерева (изменённые, новые, ещё не добавленные в индекс).
    #>
    param([Parameter(Mandatory)][hashtable]$Map)

    Push-Location $ProjectPath
    try {
        $changed = @(git status --porcelain --untracked-files=all) |
            ForEach-Object { ($_ -replace '^..\s+', '') -replace '^.*? -> ', '' } |
            Where-Object { $_ -like "*.cs" } |
            ForEach-Object { Join-Path $ProjectPath ($_.Trim('"')) }
    }
    finally { Pop-Location }

    $hit = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($file in $changed) {
        # ближайший asmdef вверх по дереву и есть владелец файла
        $best = $null
        foreach ($asm in $Map.Values) {
            if ($file.StartsWith("$($asm.Dir)$([IO.Path]::DirectorySeparatorChar)")) {
                if (-not $best -or $asm.Dir.Length -gt $best.Dir.Length) { $best = $asm }
            }
        }
        if ($best) { [void]$hit.Add($best.Name) }
    }
    return @($hit)
}

function Get-BuildOrder {
    <#
    .SYNOPSIS
    Топологический порядок: сборка компилируется после тех своих ссылок, которые тоже пересобираются.
    Ссылки вне списка целей остаются на артефактах Unity — их пересобирать незачем.
    #>
    param([Parameter(Mandatory)][string[]]$Targets, [Parameter(Mandatory)][hashtable]$Map)

    $order = [System.Collections.Generic.List[string]]::new()
    $state = @{}   # 1 = в обходе, 2 = уложена

    function Visit([string]$name) {
        if ($state.ContainsKey($name) -and $state[$name] -eq 2) { return }
        if ($state.ContainsKey($name) -and $state[$name] -eq 1) { return }  # цикл asmdef Unity не допустит
        $state[$name] = 1
        if ($Map.ContainsKey($name)) {
            foreach ($ref in $Map[$name].References) {
                if ($Targets -contains $ref) { Visit $ref }
            }
        }
        $state[$name] = 2
        $order.Add($name)
    }

    foreach ($t in $Targets) { Visit $t }
    return $order
}

# ---------------------------------------------------------------------------------------------
# Компиляция одной сборки
# ---------------------------------------------------------------------------------------------

function Invoke-AssemblyCompile {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$DagDir,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][hashtable]$Map,
        [Parameter(Mandatory)][string]$Csc,
        [AllowEmptyCollection()][string[]]$RebuiltNames = @()
    )

    $rsp = Join-Path $DagDir "$Name.rsp"
    if (-not (Test-Path $rsp)) {
        return [pscustomobject]@{ Name = $Name; Skipped = $true
            Reason = "нет $Name.rsp — сборка незнакома редактору (новый asmdef или новый пакет). Открой Unity один раз." }
    }

    $lines = Get-Content -LiteralPath $rsp
    $options = [System.Collections.Generic.List[string]]::new()

    foreach ($line in $lines) {
        if (-not $line.StartsWith("-")) { continue }          # исходники берём с диска, не отсюда
        if ($line.StartsWith("-refout:")) { continue }         # ref-сборка нам не нужна
        if ($line.StartsWith("-out:")) {
            $options.Add("-out:`"$(Join-Path $OutDir "$Name.dll")`"")
            continue
        }
        if ($line.StartsWith("-r:")) {
            # ссылку на сборку, которую мы только что пересобрали, подменяем на свежую
            $refPath = $line.Substring(3).Trim('"')
            $refName = [IO.Path]::GetFileNameWithoutExtension($refPath) -replace '\.ref$', ''
            if ($RebuiltNames -contains $refName) {
                $options.Add("-r:`"$(Join-Path $OutDir "$refName.dll")`"")
            }
            else { $options.Add($line) }
            continue
        }
        $options.Add($line)
    }

    foreach ($src in (Get-AssemblySources -Dir $Map[$Name].Dir)) { $options.Add("`"$src`"") }

    $tempRsp = Join-Path $OutDir "$Name.check.rsp"
    Set-Content -LiteralPath $tempRsp -Value $options -Encoding utf8

    $sw = [Diagnostics.Stopwatch]::StartNew()
    Push-Location $ProjectPath   # пути внутри .rsp относительны корню проекта
    try {
        $output = & dotnet $Csc "@$tempRsp" 2>&1
    }
    finally { Pop-Location }
    $sw.Stop()

    $errors = @($output | Where-Object { $_ -match '\): error [A-Z]+\d+' -or $_ -match '^error [A-Z]+\d+' })
    $warnings = @($output | Where-Object { $_ -match '\): warning [A-Z]+\d+' })

    return [pscustomobject]@{
        Name     = $Name
        Skipped  = $false
        Reason   = ""
        Errors   = $errors
        Warnings = $warnings
        Seconds  = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    }
}

# ---------------------------------------------------------------------------------------------
# .meta для новых скриптов
# ---------------------------------------------------------------------------------------------

function Add-MissingScriptMeta {
    <#
    .SYNOPSIS
    Заводит .meta новым .cs. Без него Unity не считает файл скриптом: тип не появляется, а поле
    SerializeReference молча становится null-компонентом.
    #>
    param([Parameter(Mandatory)][hashtable]$Map)

    $created = 0
    foreach ($asm in $Map.Values) {
        if (-not $asm.Dir.StartsWith((Join-Path $ProjectPath "Assets\_Project"))) { continue }  # только наш код

        foreach ($src in (Get-AssemblySources -Dir $asm.Dir)) {
            $meta = "$src.meta"
            if (Test-Path $meta) { continue }

            $guid = [guid]::NewGuid().ToString("N")
            $body = @"
fileFormatVersion: 2
guid: $guid
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
            Set-Content -LiteralPath $meta -Value $body -Encoding utf8NoBOM
            Write-Host "  .meta заведён: $($src.Substring($ProjectPath.Length + 1))" -ForegroundColor DarkGray
            $created++
        }
    }
    if ($created -gt 0) { Write-Host "Новых .meta: $created" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------------------------------------
# Прогон
# ---------------------------------------------------------------------------------------------

$csc = Get-RoslynCscPath
$dagDir = Get-BeeDagDir
$map = Get-AsmdefMap

if ($Meta) { Add-MissingScriptMeta -Map $map }

# Свежесть .rsp: состав ссылок фиксируется редактором, и после установки пакета он врёт.
$manifest = Join-Path $ProjectPath "Packages/manifest.json"
$rspSample = Get-ChildItem -LiteralPath $dagDir -Filter "*.rsp" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ((Get-Item $manifest).LastWriteTimeUtc -gt $rspSample.LastWriteTimeUtc) {
    Write-Host "ВНИМАНИЕ: manifest.json новее команд компиляции — состав пакетов мог измениться. Ссылки берутся устаревшие; открой Unity, чтобы обновить." -ForegroundColor Yellow
}

if ($Assembly) { $targets = @($Assembly) }
elseif ($All) {
    $targets = @(Get-ChildItem -LiteralPath $dagDir -Filter "*.rsp" -File |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) } |
        Where-Object { $map.ContainsKey($_) -and $map[$_].Dir.StartsWith((Join-Path $ProjectPath "Assets\_Project")) })
}
else { $targets = @(Get-ChangedAssemblies -Map $map) }

if (-not $targets -or $targets.Count -eq 0) {
    Write-Host "Изменённых .cs нет — проверять нечего." -ForegroundColor DarkGray
    exit 0
}

foreach ($t in $targets) {
    if (-not $map.ContainsKey($t)) { throw "Сборка '$t' не найдена среди asmdef проекта." }
}

$order = Get-BuildOrder -Targets $targets -Map $map
$outDir = Join-Path $env:LOCALAPPDATA "Guildmaster-CompileCheck"   # вне репозитория: артефакты Unity не трогаем
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Проверяю: $($order -join ', ')" -ForegroundColor Cyan

$rebuilt = [System.Collections.Generic.List[string]]::new()
$failed = 0
$totalSeconds = 0.0

foreach ($name in $order) {
    $result = Invoke-AssemblyCompile -Name $name -DagDir $dagDir -OutDir $outDir -Map $map -Csc $csc -RebuiltNames $rebuilt

    if ($result.Skipped) {
        Write-Host "  ПРОПУЩЕНА $name — $($result.Reason)" -ForegroundColor Yellow
        continue
    }

    $totalSeconds += $result.Seconds

    if ($result.Errors.Count -gt 0) {
        $failed++
        Write-Host "  ОШИБКИ $name ($($result.Seconds) с):" -ForegroundColor Red
        foreach ($e in $result.Errors) { Write-Host "    $e" -ForegroundColor Red }
        if (-not $KeepGoing) { break }
    }
    else {
        $rebuilt.Add($name)
        $warn = if ($result.Warnings.Count -gt 0) { " (предупреждений: $($result.Warnings.Count))" } else { "" }
        Write-Host "  OK $name — $($result.Seconds) с$warn" -ForegroundColor Green
    }
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "Компиляция не прошла: сборок с ошибками — $failed. Общее время $([math]::Round($totalSeconds,1)) с." -ForegroundColor Red
    exit 1
}

Write-Host "Компилируется. Сборок: $($rebuilt.Count), общее время $([math]::Round($totalSeconds,1)) с." -ForegroundColor Green
exit 0
