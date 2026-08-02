<#
.SYNOPSIS
  Проверяет целостность внутренних ссылок Obsidian-vault (docs/wiki).

.DESCRIPTION
  Ловит битые вики-ссылки [[target|alias]] и относительные markdown-ссылки
  [text](target) после переносов/переименований. Резолв по правилам Obsidian:
    - markdown [..](path) — относительно ПАПКИ файла-источника; выход за корень
      vault резолвится от корня РЕПО (ссылки на docs/, Assets/ проверяются);
    - wiki [[a/b/c]] с '/' — partial-path: существует файл, чей путь от vault
      равен или оканчивается на a/b/c(.md);
    - wiki [[name]] без '/' — короткая ссылка по имени файла.
  Якорь [[док#Заголовок]] проверяется по заголовкам файла-цели (нормализация:
  регистр, дефисы и подчёркивания приравнены к пробелам, markdown-разметка снята).
  Блочные якоря [[док^id]] не проверяются.
  Код внутри fenced (```...```) и inline (`...`) НЕ парсится (Obsidian их не
  рендерит как ссылки). Внешние (http/mailto/obsidian) и чистые якоря (#..)
  пропускаются.

  Отдельно репортит НЕОДНОЗНАЧНЫЕ короткие ссылки — когда [[name]] совпадает
  с несколькими файлами vault и Obsidian выбирает цель непредсказуемо.
  Это предупреждение, оно не роняет сборку.

  Exit 0 — битых нет; exit 1 — есть (список). Гоняется локально (Windows/pwsh)
  и в CI (ubuntu/pwsh). Ведётся скиллом tech-scribe.

.PARAMETER VaultPath
  Путь к корню vault. По умолчанию docs/wiki.

.PARAMETER StrictAmbiguous
  Считать неоднозначные короткие ссылки ошибкой (по умолчанию — предупреждение).
#>
[CmdletBinding()]
param(
    [string]$VaultPath = "docs/wiki",
    [switch]$StrictAmbiguous
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $VaultPath)) {
    Write-Error "Vault не найден: $VaultPath"
    exit 2
}

$vaultRoot = (Resolve-Path $VaultPath).Path
$repoRoot  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# --- Индексы существующих файлов ---
$allFiles = Get-ChildItem -Path $vaultRoot -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]\.obsidian[\\/]' }

$relPaths = [System.Collections.Generic.List[string]]::new()          # relpath от vault, .md убран
$relPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$byName   = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$nameTargets = @{}                                                    # короткое имя -> список relpath

foreach ($f in $allFiles) {
    $rel = $f.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $relNoExt = $rel -replace '\.md$', ''
    $relPaths.Add($relNoExt)
    [void]$relPathSet.Add($relNoExt)
    [void]$byName.Add($f.Name)
    $nameNoExt = $f.Name -replace '\.md$', ''
    [void]$byName.Add($nameNoExt)
    foreach ($key in @($f.Name, $nameNoExt)) {
        $k = $key.ToLowerInvariant()
        if (-not $nameTargets.ContainsKey($k)) { $nameTargets[$k] = [System.Collections.Generic.List[string]]::new() }
        if (-not $nameTargets[$k].Contains($relNoExt)) { $nameTargets[$k].Add($relNoExt) }
    }
}

# Приводит заголовок и якорь к общему ключу: регистр, дефисы/подчёркивания как
# пробелы, снятая markdown-разметка. Иначе '#Школа-vs-сродство' не сойдётся с
# заголовком '## Школа vs сродство'.
function Get-AnchorKey {
    param([string]$text)
    $t = [System.Uri]::UnescapeDataString($text)
    $t = [regex]::Replace($t, '\[\[([^\]\|]+)(?:\|([^\]]*))?\]\]', { param($m) if ($m.Groups[2].Success) { $m.Groups[2].Value } else { $m.Groups[1].Value } })
    $t = [regex]::Replace($t, '\[([^\]]*)\]\([^)]*\)', '$1')
    # Obsidian выбрасывает из якоря пунктуацию (':', '+', '#', ...), поэтому
    # сравниваем только по буквам и цифрам — иначе рабочая ссылка, сгенерированная
    # самим Obsidian, читается как битая.
    $t = [regex]::Replace($t, '[^\p{L}\p{N}]+', ' ')
    return $t.Trim().ToLowerInvariant()
}

# --- Индекс заголовков (для проверки якорей) ---
$headingsByRel = @{}
$headingRe = [regex]'(?m)^\s{0,3}#{1,6}\s+(.+?)\s*#*\s*$'
foreach ($f in ($allFiles | Where-Object { $_.Extension -eq '.md' })) {
    $rel = $f.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $relNoExt = $rel -replace '\.md$', ''
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $raw = Get-Content -LiteralPath $f.FullName -Raw
    if (-not [string]::IsNullOrEmpty($raw)) {
        $body = [regex]::Replace($raw, '(?s)```.*?```', '')
        foreach ($h in $headingRe.Matches($body)) { [void]$set.Add((Get-AnchorKey $h.Groups[1].Value)) }
    }
    $headingsByRel[$relNoExt] = $set
}

# Нормализует путь (разрешает . и ..). Отрицательная глубина = выход за корень:
# возвращаем строку с ведущими '../', чтобы вызывающий добрал её от корня репо.
function Resolve-RelPath {
    param([string]$baseDir, [string]$rel)
    $combined = if ($baseDir) { "$baseDir/$rel" } else { $rel }
    $out = [System.Collections.Generic.List[string]]::new()
    $up = 0
    foreach ($seg in ($combined -split '/')) {
        if ($seg -eq '' -or $seg -eq '.') { continue }
        if ($seg -eq '..') {
            if ($out.Count -eq 0) { $up++ }
            else { $out.RemoveAt($out.Count - 1) }
        }
        else { $out.Add($seg) }
    }
    if ($up -gt 0) { return ('../' * $up) + ($out -join '/') }
    return ($out -join '/')
}

# Проверяет якорь в файле-цели. Возвращает $null при успехе, иначе причину.
function Test-Anchor {
    param([string]$relNoExt, [string]$anchor)
    if ([string]::IsNullOrWhiteSpace($anchor)) { return $null }
    if ($anchor.StartsWith('^')) { return $null }             # блочный якорь не проверяем
    if (-not $headingsByRel.ContainsKey($relNoExt)) { return $null }
    $key = Get-AnchorKey $anchor
    if ($headingsByRel[$relNoExt].Contains($key)) { return $null }
    return "нет заголовка '#$anchor'"
}

# Проверяет markdown-ссылку (относительно папки источника).
function Test-MdLink {
    param([string]$target, [string]$sourceRelDir)
    $parts = $target -split '#', 2
    $t = $parts[0].Trim()
    $anchor = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '' }
    if ([string]::IsNullOrWhiteSpace($t)) { return $null }
    if ($t -match '^(https?:|mailto:|obsidian:|#)') { return $null }
    if ($t -match '^[a-zA-Z]:[\\/]' -or $t.StartsWith('/')) { return $null }
    $t = [System.Uri]::UnescapeDataString($t).Replace('\', '/')

    if ($t.EndsWith('/')) {                       # ссылка на директорию
        $resolvedDir = Resolve-RelPath $sourceRelDir $t.TrimEnd('/')
        $probe = if ($resolvedDir.StartsWith('..')) {
            Join-Path $vaultRoot $resolvedDir
        } else { Join-Path $vaultRoot $resolvedDir }
        if (Test-Path $probe -PathType Container) { return $null }
        return "нет каталога"
    }

    $resolved = Resolve-RelPath $sourceRelDir $t
    if ($resolved.StartsWith('..')) {
        # Ссылка уходит выше vault — резолвим от корня репо (docs/, Assets/ и т.п.).
        $abs = [System.IO.Path]::GetFullPath((Join-Path $vaultRoot $resolved))
        if (-not $abs.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null                          # вне репозитория — не наша забота
        }
        if ((Test-Path -LiteralPath $abs) -or (Test-Path -LiteralPath "$abs.md")) { return $null }
        return "нет файла вне vault"
    }

    $resolvedNoExt = $resolved -replace '\.md$', ''
    if (-not $relPathSet.Contains($resolvedNoExt)) { return "нет файла" }
    return (Test-Anchor $resolvedNoExt $anchor)
}

# Проверяет wiki-ссылку (partial-path или короткое имя).
# Возвращает $null при успехе, иначе причину.
function Test-WikiLink {
    param([string]$target, [string]$sourceRelNoExt)
    $parts = $target -split '#', 2
    $t = ($parts[0] -split '\^', 2)[0].Trim()
    $anchor = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '' }
    if ([string]::IsNullOrWhiteSpace($t)) {
        # [[#Заголовок]] — якорь внутри самого файла (оглавления страниц).
        return (Test-Anchor $sourceRelNoExt $anchor)
    }
    $t = [System.Uri]::UnescapeDataString($t).Replace('\', '/')
    $tNoExt = $t -replace '\.md$', ''

    if ($t -match '/') {
        if ($relPathSet.Contains($tNoExt)) { return (Test-Anchor $tNoExt $anchor) }
        $suffix = "/$tNoExt"
        foreach ($rp in $relPaths) {
            if ($rp.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
                return (Test-Anchor $rp $anchor)
            }
        }
        return "нет файла"
    }
    else {
        if (-not ($byName.Contains($tNoExt) -or $byName.Contains($t))) { return "нет файла" }
        $hits = $nameTargets[$tNoExt.ToLowerInvariant()]
        if ($null -ne $hits -and $hits.Count -ge 1) { return (Test-Anchor $hits[0] $anchor) }
        return $null
    }
}

# Возвращает список целей для короткой wiki-ссылки (для отчёта о неоднозначности).
function Get-ShortLinkTargets {
    param([string]$target)
    $t = (($target -split '#', 2)[0] -split '\^', 2)[0].Trim()
    if ([string]::IsNullOrWhiteSpace($t) -or $t -match '/') { return @() }
    $t = ([System.Uri]::UnescapeDataString($t) -replace '\.md$', '').ToLowerInvariant()
    if ($nameTargets.ContainsKey($t)) { return $nameTargets[$t] }
    return @()
}

# Вырезает fenced и inline код (Obsidian не рендерит там ссылки).
function Remove-Code {
    param([string]$text)
    $t = [regex]::Replace($text, '(?s)```.*?```', '')
    $t = [regex]::Replace($t, '`[^`]*`', '')
    return $t
}

$broken = [System.Collections.Generic.List[object]]::new()
$ambiguous = [System.Collections.Generic.List[object]]::new()
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
        $why = Test-WikiLink $target ($rel -replace '\.md$', '')
        if ($why) {
            $broken.Add([pscustomobject]@{ File = $rel; Link = "[[$target]]"; Why = $why })
        }
        else {
            $hits = Get-ShortLinkTargets $target
            if ($hits.Count -gt 1) {
                $ambiguous.Add([pscustomobject]@{ File = $rel; Link = "[[$target]]"; Targets = ($hits -join ', ') })
            }
        }
    }
    foreach ($m in $mdRe.Matches($text)) {
        $target = $m.Groups[1].Value
        $why = Test-MdLink $target $srcDir
        if ($why) {
            $broken.Add([pscustomobject]@{ File = $rel; Link = "($target)"; Why = $why })
        }
    }
}

$mdCount = ($mdFiles | Measure-Object).Count

if ($ambiguous.Count -gt 0) {
    Write-Host "НЕОДНОЗНАЧНЫЕ короткие ссылки ($($ambiguous.Count)) — Obsidian выберет цель непредсказуемо:" -ForegroundColor Yellow
    foreach ($a in $ambiguous) { Write-Host "  $($a.File)  ->  $($a.Link)  [$($a.Targets)]" }
    Write-Host ""
}

if ($broken.Count -eq 0) {
    Write-Host "OK: битых внутренних ссылок нет ($mdCount .md проверено)."
    if ($StrictAmbiguous -and $ambiguous.Count -gt 0) { exit 1 }
    exit 0
}
else {
    Write-Host "БИТЫЕ ССЫЛКИ ($($broken.Count)):" -ForegroundColor Red
    foreach ($b in $broken) { Write-Host "  $($b.File)  ->  $($b.Link)  — $($b.Why)" }
    exit 1
}
