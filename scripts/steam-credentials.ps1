<#
.SYNOPSIS
    Готовит учётные данные аккаунта-билдера для выкладки в Steam из CI.

.DESCRIPTION
    Делает три шага, которые иначе делаются руками и путаются: ставит SteamCMD (если его нет),
    логинит им указанный аккаунт и кодирует получившийся config.vdf в base64 для секрета
    STEAM_CONFIG_VDF.

    Зачем вообще config.vdf: у аккаунта включён Steam Guard, и CI не может ввести код из почты.
    Поэтому вход делается ОДИН раз здесь, руками, а CI дальше живёт файлом сессии.

    SteamCMD ставится ВНЕ репозитория (по умолчанию в LocalAppData): это чужой бинарник в сотню
    мегабайт, которому в истории проекта делать нечего.

.PARAMETER Login
    Логин аккаунта-билдера. Не основной аккаунт: его данные лягут в секреты ПУБЛИЧНОГО репозитория,
    поэтому у билдера должно быть ровно два права в партнёрке (Edit App Metadata, Publish App
    Changes To Steam).

.PARAMETER ToolsRoot
    Куда ставить SteamCMD. По умолчанию %LOCALAPPDATA%\Alebardium\steamcmd.

.EXAMPLE
    ./scripts/steam-credentials.ps1 -Login alebardium_ci
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Login,

    [string] $ToolsRoot = (Join-Path $env:LOCALAPPDATA 'Alebardium\steamcmd')
)

$ErrorActionPreference = 'Stop'

$zipUrl  = 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
$exePath = Join-Path $ToolsRoot 'steamcmd.exe'
$vdfPath = Join-Path $ToolsRoot 'config\config.vdf'

# ── 1. SteamCMD ────────────────────────────────────────────────────────────────

if (-not (Test-Path $exePath)) {
    Write-Host "SteamCMD не найден — ставлю в $ToolsRoot" -ForegroundColor Cyan
    Write-Host "Источник: $zipUrl (официальный CDN Valve)"

    if (-not (Test-Path $ToolsRoot)) { New-Item -ItemType Directory -Path $ToolsRoot -Force | Out-Null }

    $zipPath = Join-Path $ToolsRoot 'steamcmd.zip'
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $ToolsRoot -Force
    Remove-Item $zipPath -Force

    if (-not (Test-Path $exePath)) { throw "steamcmd.exe не появился в $ToolsRoot — распаковка не удалась" }
    Write-Host "Готово." -ForegroundColor Green
}
else {
    Write-Host "SteamCMD уже стоит: $exePath" -ForegroundColor DarkGray
}

# ── 2. Вход ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Логинюсь как '$Login'." -ForegroundColor Cyan
Write-Host "Дальше SteamCMD спросит пароль и код Steam Guard — вводи прямо в это окно."
Write-Host "Первый запуск сперва обновит сам себя; это нормально и занимает минуту."
Write-Host ""

& $exePath "+login" $Login "+quit"

if (-not (Test-Path $vdfPath)) {
    throw @"
config.vdf не появился ($vdfPath) — значит вход не прошёл.
Обычные причины: неверный пароль, не введён код Steam Guard, аккаунт без доступа к приложению.
Попробуй запустить руками и посмотреть, что он говорит:
  & "$exePath" +login $Login
"@
}

# ── 3. Base64 для секрета ──────────────────────────────────────────────────────

# Через .NET, а не certutil: тот добавляет заголовки и переносы строк, а секрету нужна ОДНА строка.
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($vdfPath))
$outPath = Join-Path $ToolsRoot 'config_base64.txt'
[IO.File]::WriteAllText($outPath, $base64, [Text.UTF8Encoding]::new($false))

try {
    Set-Clipboard -Value $base64
    $clip = 'и скопировано в буфер обмена'
}
catch {
    $clip = '(в буфер положить не вышло — открой файл и скопируй сам)'
}

Write-Host ""
Write-Host "ГОТОВО $clip" -ForegroundColor Green
Write-Host "Файл: $outPath"
Write-Host ""
Write-Host "Дальше — в GitHub: Settings -> Secrets and variables -> Actions" -ForegroundColor Cyan
Write-Host "  secret   STEAM_USERNAME    = $Login"
Write-Host "  secret   STEAM_CONFIG_VDF  = содержимое буфера (одна длинная строка)"
Write-Host "  variable STEAM_APP_ID      = 3259720"
Write-Host ""
Write-Host "После того как вставишь секрет — УДАЛИ файл: в нём живой ключ сессии." -ForegroundColor Yellow
Write-Host "  Remove-Item '$outPath'"
