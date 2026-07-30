# Общая обвязка запуска Unity из командной строки (dot-source: . "$PSScriptRoot/unity-cli.ps1").
#
# Зачем отдельным файлом: и тесты, и балансный стенд гоняются одним и тем же способом — batchmode с
# редактором той версии, что записана в проекте. Второй экземпляр Unity НЕ открывает уже открытый
# проект, а открытый редактор — норма при параллельной работе, поэтому здесь же живёт теневой проект:
# отдельная Library вне репозитория поверх junction-ссылок на живые Assets.

Set-StrictMode -Version Latest

function Get-UnityProjectVersion {
    <#
    .SYNOPSIS
    Версия редактора из ProjectSettings/ProjectVersion.txt — не хардкод, иначе прогон падает при апгрейде.
    #>
    param([Parameter(Mandatory)][string]$ProjectPath)

    $versionFile = Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt"
    if (-not (Test-Path $versionFile)) { throw "ProjectVersion.txt не найден: $versionFile" }

    $line = Get-Content $versionFile | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    $version = ($line -replace '^m_EditorVersion:\s*', '').Trim()
    if (-not $version) { throw "Не разобрана m_EditorVersion в $versionFile" }
    return $version
}

function Get-UnityEditorRoot {
    <#
    .SYNOPSIS
    Папка Editor установленного редактора той версии, что записана в проекте.

    .DESCRIPTION
    Единственный владелец пути к установке: кроме самого Unity.exe оттуда берётся компилятор Roslyn
    (scripts/compile-check.ps1 собирает сборки без редактора). Два места, знающие раскладку Unity Hub,
    разъехались бы на первом же нестандартном пути установки.
    #>
    param([Parameter(Mandatory)][string]$ProjectPath)

    $version = Get-UnityProjectVersion -ProjectPath $ProjectPath
    $root = "C:\Program Files\Unity\Hub\Editor\$version\Editor"
    if (-not (Test-Path $root)) {
        throw "Unity $version не найден: $root. Поставь эту версию через Unity Hub или поправь путь в scripts/unity-cli.ps1."
    }
    return $root
}

function Get-UnityExePath {
    param([Parameter(Mandatory)][string]$ProjectPath)

    $exe = Join-Path (Get-UnityEditorRoot -ProjectPath $ProjectPath) "Unity.exe"
    if (-not (Test-Path $exe)) {
        throw "Unity.exe не найден: $exe"
    }
    return $exe
}

function Test-UnityProjectLocked {
    <#
    .SYNOPSIS
    Открыт ли проект в редакторе. Unity держит Temp/UnityLockfile захваченным на всё время сессии,
    поэтому файл проверяется попыткой открыть его на запись, а не просто на существование: после
    аварийного завершения редактора lockfile остаётся лежать, но никем не удерживается.
    #>
    param([Parameter(Mandatory)][string]$ProjectPath)

    $lock = Join-Path $ProjectPath "Temp/UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }

    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'Write', 'None')
        $stream.Dispose()
        return $false
    } catch {
        return $true
    }
}

function Initialize-UnityShadowProject {
    <#
    .SYNOPSIS
    Теневой проект вне репозитория: своя Library, живые Assets через junction.

    .DESCRIPTION
    По умолчанию линкуются ВСЕ корневые папки репозитория, кроме перечисленных в -SkipFolders и тех,
    что зеркалятся копией. Именно все, а не только Assets: тесты и тулы читают файлы и вне Assets
    (например манифест FMOD живёт в «FMOD Project/» рядом с ним), и теневой проект с одной ссылкой на
    Assets ронял такой тест «отсутствием данных», которые в репозитории есть.

    Junction для папок не требует прав администратора; стенд через них читает ЖИВОЙ контент, включая
    правки SO, ещё не попавшие в git, и пишет отчёты сразу в репозиторий. Папки из -CopyFolders
    зеркалятся копией: в них Unity пишет сам (packages-lock.json, настройки проекта), и junction унёс
    бы этот мусор в репозиторий.

    Цена названа вслух: первый запуск — полный импорт проекта в отдельную Library, это минуты и
    гигабайты на диске. Дальше Library переиспользуется. Режим годится только для прогонов, которые
    НЕ сохраняют ассеты: две сессии Unity над одной папкой Assets на запись не рассчитаны.
    #>
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [string[]]$LinkFolders,
        [string[]]$CopyFolders = @("ProjectSettings", "Packages"),

        # Своё хозяйство редактора и результаты прогонов: у теневого проекта они обязаны быть отдельными,
        # иначе он полезет в чужую Library. .git не линкуем сознательно — Unity он не нужен, а рисковать
        # чужим репозиторием ради удобства незачем.
        [string[]]$SkipFolders = @("Library", "Temp", "Logs", "obj", "UserSettings", "TestResults",
            "Build", "Builds", ".git", ".vs", ".idea", "node_modules")
    )

    $name = Split-Path $ProjectPath -Leaf
    $shadow = Join-Path $env:LOCALAPPDATA "$name-UnityShadow"
    New-Item -ItemType Directory -Force -Path $shadow | Out-Null

    if (-not $LinkFolders) {
        $LinkFolders = Get-ChildItem -LiteralPath $ProjectPath -Directory -Force |
            Where-Object { $SkipFolders -notcontains $_.Name -and $CopyFolders -notcontains $_.Name } |
            ForEach-Object { $_.Name }
    }

    foreach ($folder in $LinkFolders) {
        $target = Join-Path $ProjectPath $folder
        if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force -Path $target | Out-Null }

        $link = Join-Path $shadow $folder
        if (Test-Path $link) {
            $item = Get-Item $link -Force
            # Ссылка на месте и ведёт куда надо — оставляем. Иначе сносим: подсунутая копия вместо
            # ссылки означала бы, что стенд мерит устаревший контент и молчит об этом.
            if ($item.LinkType -eq 'Junction' -and ($item.Target -contains $target)) { continue }
            Remove-Item $link -Recurse -Force
        }
        New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    }

    foreach ($folder in $CopyFolders) {
        $src = Join-Path $ProjectPath $folder
        if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $shadow $folder
        # robocopy: 0-7 — успех (8+ уже настоящие ошибки копирования).
        robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Не удалось зеркалить $folder в теневой проект (robocopy $LASTEXITCODE)" }
        $global:LASTEXITCODE = 0
    }

    return $shadow
}

function Invoke-UnityBatch {
    <#
    .SYNOPSIS
    Запустить Unity в batchmode, ДОЖДАТЬСЯ его и вернуть настоящий код выхода.

    .DESCRIPTION
    Через `& $exe` этого сделать нельзя: Unity.exe — GUI-приложение, и оператор вызова его не ждёт.
    Управление возвращается сразу, $LASTEXITCODE достаётся от предыдущей команды, и прогон, который ещё
    даже не начался, рапортует об успехе. Ровно этим болел прежний run-tests.ps1: «tests PASSED» при
    отсутствующем файле результатов. Поэтому — Start-Process -Wait -PassThru и ExitCode процесса.

    Аргументы склеиваются в строку с ручными кавычками: в путях проекта есть пробелы, а -ArgumentList
    массивом квотирует их не на всех версиях PowerShell одинаково.
    #>
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$LogFile,
        [string[]]$ExtraArgs = @()
    )

    $exe = Get-UnityExePath -ProjectPath $ProjectPath
    New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null

    $all = @("-batchmode", "-projectPath", $ProjectPath, "-logFile", $LogFile) + $ExtraArgs
    $line = ($all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '

    $proc = Start-Process -FilePath $exe -ArgumentList $line -PassThru -Wait
    return $proc.ExitCode
}

function Show-UnityLogTail {
    param(
        [Parameter(Mandatory)][string]$LogFile,
        [int]$Lines = 40
    )

    if (-not (Test-Path $LogFile)) {
        Write-Host "Лог не найден: $LogFile" -ForegroundColor Yellow
        return
    }

    Write-Host "--- хвост лога ($LogFile) ---" -ForegroundColor DarkGray
    Get-Content $LogFile -Tail $Lines | ForEach-Object { Write-Host $_ }
}
