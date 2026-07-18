<#
.SYNOPSIS
  Проверяет целостность внутренних ссылок Obsidian-vault (docs/wiki).

.DESCRIPTION
  Ловит битые вики-ссылки [[target|alias]] и относительные markdown-ссылки
  [text](target) после переносов/переименований. Резолв по правилам Obsidian:
    - markdown [..](path) — относительно ПАПКИ файла-источника; выход за vault
      (../ за корень) считается внешней ссылкой и пропускается;
    - wiki [[a/b/c]] с '/' — partial-path: существует файл, чей путь от vault
      равен или оканчивается на a/b/c(.md);
    - wiki [[name]] без '/' — короткая ссылка по имени файла.
  Код внутри fenced (```...```) и inline (`...`) НЕ парсится (Obsidian их не
  рендерит как ссылки). Внешние (http/mailto/obsidian), чистые якоря (#..)
  и абсолютные пути пропускаются.

  Exit 0 — битых нет; exit 1 — есть (список). Гоняется локально (Windows/pwsh)
  и в CI (ubuntu/pwsh). Ведётся скиллом tech-scribe.

.PARAMETER VaultPath
  Путь к корню vault. По умолчанию docs/wiki.
#>
[CmdletBinding()]
param(
    [string]$VaultPath = "docs/wiki"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $VaultPath)) {
    Write-Error "Vault не найден: $VaultPath"
    exit 2
}

$vaultRoot = (Resolve-Path $VaultPath).Path

# --- Индексы существующих файлов ---
$allFiles = Get-ChildItem -Path $vaultRoot -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]\.obsidian[\\/]' }

$relPaths = [System.Collections.Generic.List[string]]::new()          # relpath от vault, .md убран
$relPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$byName   = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $relNoExt = $rel -replace '\.md$', ''
    $relPaths.Add($relNoExt)
    [void]$relPathSet.Add($relNoExt)
    [void]$byName.Add($f.Name)
    $nameNoExt = $f.Name -replace '\.md$', ''
    [void]$byName.Add($nameNoExt)
}

# Нормализует путь (разрешает . и ..). Возвращает $null, если уходит выше корня.
function Resolve-RelPath {
    param([string]$baseDir, [string]$rel)
    $combined = if ($baseDir) { "$baseDir/$rel" } else { $rel }
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($seg in ($combined -split '/')) {
        if ($seg -eq '' -or $seg -eq '.') { continue }
        if ($seg -eq '..') {
            if ($out.Count -eq 0) { return $null }   # ушли выше корня vault
            $out.RemoveAt($out.Count - 1)
        }
        else { $out.Add($seg) }
    }
    return ($out -join '/')
}

# Проверяет markdown-ссылку (относительно папки источника).
function Test-MdLink {
    param([string]$target, [string]$sourceRelDir)
    $t = ($target -split '#', 2)[0].Trim()
    if ([string]::IsNullOrWhiteSpace($t)) { return $true }
    if ($t -match '^(https?:|mailto:|obsidian:|#)') { return $true }
    if ($t -match '^[a-zA-Z]:[\\/]' -or $t.StartsWith('/')) { return $true }
    $t = [System.Uri]::UnescapeDataString($t).Replace('\', '/')
    if ($t.EndsWith('/')) {                       # ссылка на директорию
        $resolvedDir = Resolve-RelPath $sourceRelDir $t.TrimEnd('/')
        if ($null -eq $resolvedDir) { return $true }
        return (Test-Path (Join-Path $vaultRoot $resolvedDir) -PathType Container)
    }
    $resolved = Resolve-RelPath $sourceRelDir $t
    if ($null -eq $resolved) { return $true }   # вне vault — не наша забота
    $resolvedNoExt = $resolved -replace '\.md$', ''
    return $relPathSet.Contains($resolvedNoExt)
}

# Проверяет wiki-ссылку (partial-path или короткое имя).
function Test-WikiLink {
    param([string]$target)
    $t = ($target -split '#', 2)[0]
    $t = ($t -split '\^', 2)[0]
    $t = $t.Trim()
    if ([string]::IsNullOrWhiteSpace($t)) { return $true }
    $t = [System.Uri]::UnescapeDataString($t).Replace('\', '/')
    $tNoExt = $t -replace '\.md$', ''

    if ($t -match '/') {
        if ($relPathSet.Contains($tNoExt)) { return $true }
        $suffix = "/$tNoExt"
        foreach ($rp in $relPaths) { if ($rp.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) { return $true } }
        return $false
    }
    else {
        if ($byName.Contains($tNoExt)) { return $true }
        if ($byName.Contains($t)) { return $true }
        return $false
    }
}

# Вырезает fenced и inline код (Obsidian не рендерит там ссылки).
function Remove-Code {
    param([string]$text)
    $t = [regex]::Replace($text, '(?s)```.*?```', '')
    $t = [regex]::Replace($t, '`[^`]*`', '')
    return $t
}

$broken = [System.Collections.Generic.List[object]]::new()
$mdFiles = $allFiles | Where-Object { $_.Extension -eq '.md' }

$wikiRe = [regex]'\[\[([^\]\|]+)(?:\|[^\]]*)?\]\]'
$mdRe   = [regex]'(?<!\!)\[[^\]]*\]\(([^)]+)\)'

foreach ($f in $mdFiles) {
    $rel = $f.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $srcDir = if ($rel -match '/') { $rel -replace '/[^/]+$', '' } else { '' }
    $raw = Get-Content -LiteralPath $f.FullName -Raw
    if ([string]::IsNullOrEmpty($raw)) { continue }
    $text = Remove-Code $raw
    # Внутри Markdown-таблиц пайп алиаса экранируется: [[target\|alias]]. Obsidian трактует \| как |,
    # поэтому снимаем экранирование до парсинга, иначе '\' прилипает к target и ссылка ложно-битая.
    $text = $text.Replace('\|', '|')

    foreach ($m in $wikiRe.Matches($text)) {
        $target = $m.Groups[1].Value
        if (-not (Test-WikiLink $target)) {
            $broken.Add([pscustomobject]@{ File = $rel; Link = "[[$target]]" })
        }
    }
    foreach ($m in $mdRe.Matches($text)) {
        $target = $m.Groups[1].Value
        if (-not (Test-MdLink $target $srcDir)) {
            $broken.Add([pscustomobject]@{ File = $rel; Link = "($target)" })
        }
    }
}

$mdCount = ($mdFiles | Measure-Object).Count
if ($broken.Count -eq 0) {
    Write-Host "OK: битых внутренних ссылок нет ($mdCount .md проверено)."
    exit 0
}
else {
    Write-Host "БИТЫЕ ССЫЛКИ ($($broken.Count)):" -ForegroundColor Red
    foreach ($b in $broken) { Write-Host "  $($b.File)  ->  $($b.Link)" }
    exit 1
}
