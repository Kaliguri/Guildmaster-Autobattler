<#
.SYNOPSIS
  Проверяет YAML-шапки документов Obsidian-vault (docs/wiki).

.DESCRIPTION
  Правило 4 ведения вики: мета живёт во frontmatter, а не в тексте и не в имени файла.
  Каждый док несёт:
    title    — по системе "<Кластер> - <Имя>", ВЕСЬ латиницей;
    order    — целое число (порядок в папке задаётся им, а не именем файла);
    status   — из закрытого списка draft|needs_review|ready|living|archive;
    updated  — дата YYYY-MM-DD.

  Проверяются только каталоги из -Include (по умолчанию gdd и tech). Шаблоны
  карточек (template-*) и служебные файлы Obsidian пропускаются.

  Exit 0 — нарушений нет; exit 1 — есть. С -ReportOnly всегда exit 0 (режим
  постепенного внедрения: смотрим объём, не роняя CI).

.PARAMETER VaultPath
  Путь к корню vault. По умолчанию docs/wiki.

.PARAMETER Include
  Подкаталоги vault под проверкой. По умолчанию gdd, tech.

.PARAMETER ReportOnly
  Печатать отчёт, но не падать.
#>
[CmdletBinding()]
param(
    [string]$VaultPath = "docs/wiki",
    [string[]]$Include = @("gdd", "tech"),
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $VaultPath)) {
    Write-Error "Vault не найден: $VaultPath"
    exit 2
}

$vaultRoot = (Resolve-Path $VaultPath).Path
# 'planned' — конвенция tech-вики (Diátaxis-кластер Planning), в ГДД не используется.
$validStatus = @('draft', 'needs_review', 'ready', 'living', 'archive', 'planned')

# Ярлыки кластеров: папки-кластеры ГДД, префиксы карточек контента и кластеры
# тех-вики (Diátaxis).
$validClusters = @(
    'Meta', 'Vision', 'Combat', 'Run', 'Content', 'Modes', 'Roster',
    'Effect', 'Item', 'Relic', 'Species', 'Faction', 'Enemies',
    'Bandits', 'Goblins', 'Golems', 'Beasts',
    'Reference', 'Explanation', 'How-to', 'Planning'
)

$issues = [System.Collections.Generic.List[object]]::new()

function Add-Issue {
    param([string]$file, [string]$field, [string]$why)
    $issues.Add([pscustomobject]@{ File = $file; Field = $field; Why = $why })
}

$files = Get-ChildItem -Path $vaultRoot -Recurse -File -Filter *.md |
    Where-Object { $_.FullName -notmatch '[\\/]\.obsidian[\\/]' } |
    Where-Object {
        $rel = $_.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $top = ($rel -split '/')[0]
        $Include -contains $top
    }

foreach ($f in $files) {
    $rel = $f.FullName.Substring($vaultRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $raw = Get-Content -LiteralPath $f.FullName -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        Add-Issue $rel '(файл)' 'пустой файл'
        continue
    }

    if ($raw -notmatch '(?s)^﻿?---\r?\n(.*?)\r?\n---') {
        Add-Issue $rel '(frontmatter)' 'нет YAML-шапки вообще'
        continue
    }
    $fm = $Matches[1]

    # --- title ---
    if ($fm -match '(?m)^title:\s*(.+?)\s*$') {
        $title = $Matches[1].Trim().Trim('"', "'")
        if ($title -match '[\p{IsCyrillic}]') {
            Add-Issue $rel 'title' "кириллица в title: «$title»"
        }
        elseif ($title -notmatch ' - ') {
            Add-Issue $rel 'title' "нет разделителя ' - ' по системе «<Кластер> - <Имя>»: «$title»"
        }
        else {
            $cluster = ($title -split ' - ', 2)[0].Trim()
            if ($validClusters -notcontains $cluster) {
                Add-Issue $rel 'title' "неизвестный кластер «$cluster» в «$title»"
            }
        }
    }
    else {
        Add-Issue $rel 'title' 'поле отсутствует'
    }

    # --- order ---
    if ($fm -match '(?m)^order:\s*(.+?)\s*$') {
        $order = $Matches[1].Trim()
        if ($order -notmatch '^-?\d+$') { Add-Issue $rel 'order' "не целое число: «$order»" }
    }
    else {
        Add-Issue $rel 'order' 'поле отсутствует'
    }

    # --- status ---
    if ($fm -match '(?m)^status:\s*(.+?)\s*$') {
        $status = $Matches[1].Trim().Trim('"', "'")
        if ($validStatus -notcontains $status) {
            Add-Issue $rel 'status' "вне списка ($($validStatus -join '|')): «$status»"
        }
    }
    else {
        Add-Issue $rel 'status' 'поле отсутствует'
    }

    # --- updated ---
    if ($fm -match '(?m)^updated:\s*(.+?)\s*$') {
        $updated = $Matches[1].Trim().Trim('"', "'")
        if ($updated -notmatch '^\d{4}-\d{2}-\d{2}$') {
            Add-Issue $rel 'updated' "не формат YYYY-MM-DD: «$updated»"
        }
    }
    else {
        Add-Issue $rel 'updated' 'поле отсутствует'
    }
}

$fileCount = ($files | Measure-Object).Count

if ($issues.Count -eq 0) {
    Write-Host "OK: шапки в порядке ($fileCount .md проверено)."
    exit 0
}

Write-Host "НАРУШЕНИЯ FRONTMATTER ($($issues.Count) в $fileCount .md):" -ForegroundColor Red
foreach ($g in ($issues | Group-Object Field | Sort-Object Count -Descending)) {
    Write-Host ""
    Write-Host "  --- $($g.Name): $($g.Count) ---" -ForegroundColor Yellow
    foreach ($i in $g.Group) { Write-Host "    $($i.File) — $($i.Why)" }
}

if ($ReportOnly) {
    Write-Host ""
    Write-Host "(-ReportOnly: не роняю сборку)" -ForegroundColor DarkGray
    exit 0
}
exit 1
