<#
.SYNOPSIS
    Собрать Windows-плеер и выложить его в Steam с этой машины.

.DESCRIPTION
    Локальная альтернатива облачному пайплайну: сборка идёт через теневой проект (не мешая открытому
    редактору), заливка — через steamcmd, который уже залогинен на этой машине.

    Почему локально, а не в CI (решение Макса 03.08.2026): выкладка редкая и всегда при человеке,
    минуты Actions на приватном репозитории считаются, а лицензия Unity и вход в Steam здесь уже есть.
    Облачный CI при этом остаётся и отвечает на ДРУГОЙ вопрос — «собирается ли то, что лежит в
    репозитории». Локальная сборка на него ответить не может: она берёт файлы с диска, где лежит и
    незакоммиченное (ровно так 02.08.2026 в ветке несколько дней жили два файла, без которых чистый
    клон не собирался).

.PARAMETER Login
    Логин Steam-аккаунта, которым заливаем. Пароль не нужен: вход берётся из config.vdf, созданного
    scripts/steam-credentials.ps1.

.PARAMETER Branch
    Ветка Steam, на которой билд станет текущим. Боевую (default/public) скрипт не примет.

.PARAMETER Version
    Версия сборки. По умолчанию берётся из тега на HEAD, а если его нет — из ProjectSettings плюс
    короткий хеш коммита.

.PARAMETER Preview
    Прогнать SteamPipe вхолостую: он посчитает и покажет, что бы залил, но ничего не отправит.

.PARAMETER SkipBuild
    Не пересобирать, залить то, что уже лежит в Build/. Для повторной попытки после сбоя сети.

.PARAMETER Force
    Собрать, несмотря на незакоммиченные .cs рядом. По умолчанию скрипт на них останавливается.

.EXAMPLE
    ./scripts/steam-publish.ps1 -Login alebardium_ci
    ./scripts/steam-publish.ps1 -Login alebardium_ci -Preview
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Login,

    [string] $Branch = 'dev_happy_guildmasters',

    [string] $Version,

    [switch] $Preview,

    [switch] $SkipBuild,

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/unity-cli.ps1"

$repoRoot  = Split-Path $PSScriptRoot -Parent
$appId     = '3259720'
$depotId   = '3259721'   # депо по умолчанию — AppId + 1
$buildRoot = Join-Path $repoRoot 'Build\StandaloneWindows64'
$steamRoot = Join-Path $env:LOCALAPPDATA 'Alebardium\steamcmd'
$steamExe  = Join-Path $steamRoot 'steamcmd.exe'

# ── Проверки, каждая до долгой работы ──────────────────────────────────────────

if ($Branch -in @('default', 'public', '')) {
    throw "Ветка '$Branch' — боевая. В прод переводит человек кнопкой в партнёрке, а не скрипт."
}

if (-not (Test-Path $steamExe)) {
    throw "SteamCMD не найден ($steamExe). Сначала: ./scripts/steam-credentials.ps1 -Login $Login"
}

$vdfPath = Join-Path $steamRoot 'config\config.vdf'
if (-not (Test-Path $vdfPath) -or (Get-Content -Raw $vdfPath) -notmatch '"Accounts"') {
    throw "В config.vdf нет входа. Сначала: ./scripts/steam-credentials.ps1 -Login $Login"
}

# Незакоммиченный .cs — это код, которого нет в репозитории. Собрать его можно, но выложить игрокам
# значит отдать сборку, которую нельзя воспроизвести ни из одного коммита.
$dirty = git -C $repoRoot status --porcelain -- '*.cs' | Where-Object { $_ -match '^\?\?' }
if ($dirty -and -not $Force) {
    Write-Host "Незакоммиченные исходники:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "  $_" }
    throw "Собранное отсюда нельзя воспроизвести из репозитория. Закоммить их или гони с -Force."
}

# ── Версия: владелец — тег ─────────────────────────────────────────────────────

$settings = Join-Path $repoRoot 'ProjectSettings\ProjectSettings.asset'
# ЯКОРЬ НА НАЧАЛО СТРОКИ ОБЯЗАТЕЛЕН. Без него первым совпадением идёт `visionOSBundleVersion: 1.0`
# двумя строками выше — Unity держит там версии для платформ, которые мы не собираем, и они не
# меняются никогда. Именно отсюда в Steam уехали сборки «1.0-dev.<sha>» при bundleVersion 0.1.0:
# скрипт читал версию visionOS и был уверен, что читает нашу (поймано 05.08.2026).
$bundle = (Select-String -Path $settings -Pattern '^\s*bundleVersion:\s*(.+)' |
           Select-Object -First 1).Matches.Groups[1].Value.Trim()

if (-not $Version) {
    $tag = git -C $repoRoot describe --tags --exact-match HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $Version = $tag -replace '^v', ''
    }
    else {
        $sha  = (git -C $repoRoot rev-parse --short=7 HEAD).Trim()
        $Version = "$bundle-dev.$sha"
    }
    $global:LASTEXITCODE = 0
}

# ГЕЙТ РАСХОЖДЕНИЯ (05.08.2026). Версию релиза назначает тег, а dev-сборка берёт за основу
# bundleVersion — и если эти двое разъехались, номер молча врёт всем сразу. Ровно это и случилось:
# теги стояли на v0.0.3, bundleVersion — на 0.1.0, а каждая выкладка по кнопке уезжала как
# «0.1.0-dev.<sha>», из-за чего номер выглядел застывшим и не совпадал ни с одним тегом.
#
# Проверка механическая, потому что «не забыть поднять версию» проигрывает на третий раз — то же
# рассуждение, по которому владельцем версии вообще стал тег, а не память человека. Обойти можно
# явным -Version: аварийная выкладка не должна упираться в номер.
$lastTag = git -C $repoRoot tag --list 'v*' --sort=-v:refname | Select-Object -First 1
$global:LASTEXITCODE = 0
if ($lastTag -and -not $PSBoundParameters.ContainsKey('Version')) {
    $lastTagVersion = $lastTag -replace '^v', ''
    if ($bundle -ne $lastTagVersion) {
        Write-Host ""
        Write-Host "Версия разъехалась:" -ForegroundColor Yellow
        Write-Host "  bundleVersion (ProjectSettings): $bundle"
        Write-Host "  последний тег:                   $lastTag"
        Write-Host ""
        throw "Приведи bundleVersion к $lastTagVersion либо поставь тег на новую версию. Аварийная выкладка — с явным -Version."
    }
}

Write-Host ""
Write-Host "Версия:  $Version" -ForegroundColor Cyan
Write-Host "Ветка:   $Branch"
Write-Host "AppId:   $appId (депо $depotId)"
if ($Preview) { Write-Host "Режим:   ПРЕДПРОСМОТР — ничего не отправится" -ForegroundColor Yellow }
Write-Host ""

# ── Сборка ─────────────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Host "Собираю плеер (теневой проект, редактор не трогаем)..." -ForegroundColor Cyan

    $shadow = Initialize-UnityShadowProject -ProjectPath $repoRoot
    Wait-ForShadowProject -ShadowPath $shadow

    if (Test-Path $buildRoot) { Remove-Item $buildRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

    $exePath = Join-Path $buildRoot 'HappyGuildmasters.exe'
    $logFile = Join-Path $env:TEMP 'guildmaster-build\player.log'

    $code = Invoke-UnityBatch -ProjectPath $shadow -LogFile $logFile -ExtraArgs @(
        '-quit', '-nographics',
        '-executeMethod', 'Guildmaster.Build.Editor.PlayerBuilder.Windows64',
        '-buildOutput', $exePath,
        '-buildVersion', $Version
    )

    if ($code -ne 0) {
        Show-UnityLogTail -LogFile $logFile -Lines 40
        throw "Сборка не удалась (код $code). Полный лог: $logFile"
    }

    if (-not (Test-Path $exePath)) { throw "Сборка отчиталась успехом, но $exePath не появился." }
    Write-Host "Собрано: $exePath" -ForegroundColor Green
}

# steam_appid.txt рядом с exe ОТКЛЮЧАЕТ проверку владения — игра пойдёт у кого угодно. В репозитории
# он лежит в корне (нужен редактору) и в билд попасть не должен, но проверяем: заметить это по игре
# нельзя никак.
$leaked = Get-ChildItem -Path $buildRoot -Filter 'steam_appid.txt' -Recurse -ErrorAction SilentlyContinue
if ($leaked) { throw "steam_appid.txt попал в билд ($($leaked[0].FullName)) — он снимает проверку владения." }

# ── Скрипты SteamPipe ──────────────────────────────────────────────────────────

$work = Join-Path $env:TEMP "guildmaster-steam\$Version"
New-Item -ItemType Directory -Force -Path $work | Out-Null

$depotOut = Join-Path $work 'depot_windows.vdf'
$appOut   = Join-Path $work 'app_build.vdf'

(Get-Content -Raw (Join-Path $repoRoot 'steam\depot_windows.template.vdf')).
    Replace('{{DEPOT_ID}}', $depotId).
    Replace('{{CONTENT_ROOT}}', $buildRoot) | Set-Content -Path $depotOut -Encoding UTF8

(Get-Content -Raw (Join-Path $repoRoot 'steam\app_build.template.vdf')).
    Replace('{{APP_ID}}', $appId).
    Replace('{{DESCRIPTION}}', "$Version (local)").
    Replace('{{BUILD_OUTPUT}}', (Join-Path $work 'output')).
    Replace('{{CONTENT_ROOT}}', $buildRoot).
    Replace('{{BRANCH}}', $(if ($Preview) { '' } else { $Branch })).
    Replace('{{PREVIEW}}', $(if ($Preview) { '1' } else { '0' })).
    Replace('{{DEPOT_ID}}', $depotId).
    Replace('{{DEPOT_SCRIPT}}', $depotOut) | Set-Content -Path $appOut -Encoding UTF8

# ── Заливка ────────────────────────────────────────────────────────────────────

Write-Host ""
# Замер 03.08.2026: плеер ~230 МБ. Прежняя оценка «больше гигабайта» была взята из комментария в
# ci.yml и оказалась завышенной впятеро — на глаз такие вещи оценивать нельзя.
Write-Host "Заливаю в Steam. Первый раз едет целиком (~230 МБ), дальше — только изменения." -ForegroundColor Cyan

& $steamExe "+login" $Login "+run_app_build" $appOut "+quit"
if ($LASTEXITCODE -ne 0) {
    throw @"
steamcmd вернул $LASTEXITCODE.
Частые причины: истёк вход (перезапусти ./scripts/steam-credentials.ps1 -Login $Login),
у аккаунта нет прав Publish App Changes на приложение $appId,
либо номер депо не $depotId — проверь в партнёрке.
"@
}

Write-Host ""
if ($Preview) {
    Write-Host "ПРЕДПРОСМОТР прошёл: SteamPipe посчитал содержимое и ничего не отправил." -ForegroundColor Green
}
else {
    Write-Host "Готово: версия $Version стоит на ветке '$Branch'." -ForegroundColor Green
    Write-Host "В боевую ветку это НЕ уехало — туда переводит человек в партнёрке."
}
