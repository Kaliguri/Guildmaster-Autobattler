#Requires -Version 7.0
<#
.SYNOPSIS
    Карта журналов и реестров проекта: что живо, какого жанра, когда последняя запись.

.DESCRIPTION
    Журналов у нас больше десятка жанров и под сто пятьдесят файлов: решения (tech и ГД),
    реестры (техдолг, баланс), прогоны (Suno), инбокс Макса, развилки, расхождения замысла
    с кодом, бэклоги и журналы заходов. Держать этот список в голове нельзя, а рукописный
    реестр отстанет от диска в первую же неделю — поэтому список ГЕНЕРИРУЕТСЯ отсюда,
    а маршрут «жанр факта → дом» живёт в CLAUDE.md.

    Жанр выводится из расположения и имени (конвенция), дата — из frontmatter `updated`,
    если он есть, иначе из mtime файла.

.PARAMETER Check
    Гейт вместо карты. Ругается на: журналоподобный файл, не покрытый конвенцией (жанр не
    объявлен); брошенный журнал захода (worklog не тронут дольше -StaleDays); архив, в который
    всё-таки дописали.

.EXAMPLE
    ./scripts/journals.ps1
.EXAMPLE
    ./scripts/journals.ps1 -Check -StaleDays 21
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [int]$StaleDays = 14,
    # Показать журналы заходов поштучно, а не одной строкой-сводкой.
    [switch]$Worklogs
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Жанр — не украшение: он определяет, ЧТО в файл писать и кто его ведёт. Порядок важен,
# первое совпадение выигрывает, поэтому конкретные пути идут выше общих шаблонов.
$kinds = @(
    @{ Kind = 'tech-journal'; Match = 'docs/wiki/tech/00-meta/journal/*.md'; Owner = 'tech-scribe'; What = 'инженерные решения, append-only, запись = файл' }
    @{ Kind = 'gdd-adr'; Match = 'docs/wiki/gdd/00-meta/journal-adr.md'; Owner = 'gdd-scribe'; What = 'принятые ГД-решения со статусами' }
    @{ Kind = 'archive'; Match = 'docs/wiki/tech/00-meta/tech-changelog.md'; Owner = 'tech-scribe'; What = 'АРХИВ, не пополняется' }
    @{ Kind = 'registry'; Match = 'docs/wiki/tech/00-meta/tech-debt.md'; Owner = 'tech-scribe'; What = 'отложенный техдолг, пункт закрывается' }
    @{ Kind = 'registry'; Match = 'docs/balance-issues.md'; Owner = 'balance'; What = 'проблемы баланса, закрываются с вердиктом' }
    @{ Kind = 'run-log'; Match = 'docs/ost-run-log.md'; Owner = 'music'; What = 'прогоны Suno: seed, дельта, вердикт дословно' }
    @{ Kind = 'inbox-max'; Match = 'docs/wiki/gdd/00-meta/open.md'; Owner = '— (Макс)'; What = 'ЛИЧНЫЙ инбокс Макса, агент НЕ пишет' }
    @{ Kind = 'inbox-archive'; Match = 'docs/wiki/gdd/00-meta/inbox/*.md'; Owner = 'gdd-scribe'; What = 'разбор инбокса слово в слово + куда уехало' }
    @{ Kind = 'forks'; Match = 'docs/wiki/gdd/00-meta/open-forks.md'; Owner = 'gdd-scribe'; What = 'развилки и числа без вердикта' }
    @{ Kind = 'forks-closed'; Match = 'docs/wiki/gdd/00-meta/closed-forks.md'; Owner = 'gdd-scribe'; What = 'закрытые развилки: почему выбрали так' }
    @{ Kind = 'drift'; Match = '*implementation-status.md'; Owner = 'gdd-scribe'; What = 'чем код расходится с замыслом' }
    @{ Kind = 'ideas'; Match = '*draft-ideas.md'; Owner = 'gdd-scribe'; What = 'идеи без вердикта' }
    @{ Kind = 'backlog'; Match = '*backlog-*.md'; Owner = 'gdd-scribe / gamefeel-vfx'; What = 'отложенное по подсистеме' }
    @{ Kind = 'worklog'; Match = 'docs/*-progress.md'; Owner = 'ведущий заход'; What = 'журнал захода, временный: вырос в ТЗ — уехал' }
)

# Файл выглядит журналом, если его имя обещает историю или реестр. Нужно, чтобы -Check
# заметил новый журнал, которому ещё не назначили жанр. Ключ ищется ЦЕЛЫМ сегментом имени:
# по подстроке «progress» ловится «meta-progression», которая журналом не является.
$journalish = 'journal', 'adr', 'decision', 'changelog', 'debt', 'issues', 'backlog',
'progress', 'log', 'status', 'diary', 'inbox', 'forks', 'ideas'

function Test-JournalishName {
    param([string]$Leaf)
    $name = [IO.Path]::GetFileNameWithoutExtension($Leaf).ToLower()
    foreach ($key in $journalish) {
        if ($name -match "(^|[-_])$([regex]::Escape($key))([-_]|$)") { return $true }
    }
    return $false
}

function Get-RelPath {
    param([string]$Full)
    return ($Full.Substring($repo.Length).TrimStart('\', '/') -replace '\\', '/')
}

function Get-KindFor {
    param([string]$Rel)
    foreach ($k in $kinds) {
        if ($Rel -like $k.Match) { return $k }
        # Шаблоны без слэша сопоставляются по имени файла в любом каталоге.
        if ($k.Match -notmatch '/' -and (Split-Path -Leaf $Rel) -like $k.Match) { return $k }
    }
    return $null
}

function Get-DocFacts {
    param([System.IO.FileInfo]$File)

    $updated = $null
    $status = ''
    $entries = 0
    $lineNo = 0
    $inFrontmatter = $false

    foreach ($line in [IO.File]::ReadLines($File.FullName)) {
        $lineNo++
        if ($lineNo -eq 1 -and $line.TrimStart([char]0xFEFF) -eq '---') { $inFrontmatter = $true; continue }
        if ($inFrontmatter) {
            if ($line -eq '---') { $inFrontmatter = $false; continue }
            if ($line -match '^updated:\s*(.+)$') { $updated = $Matches[1].Trim() }
            if ($line -match '^status:\s*(.+)$') { $status = $Matches[1].Trim() }
            continue
        }
        if ($line.StartsWith('## ')) { $entries++ }
    }

    $date = if ($updated) { $updated } else { $File.LastWriteTime.ToString('yyyy-MM-dd') }
    return [pscustomobject]@{ Updated = $date; Status = $status; Entries = $entries }
}

# ---------------------------------------------------------------- сбор

$all = Get-ChildItem (Join-Path $repo 'docs') -Recurse -Filter *.md -File
$rows = [System.Collections.Generic.List[object]]::new()
$unclaimed = [System.Collections.Generic.List[string]]::new()

foreach ($f in $all) {
    $rel = Get-RelPath $f.FullName
    $k = Get-KindFor $rel
    $leaf = (Split-Path -Leaf $rel).ToLower()

    if (-not $k) {
        if (Test-JournalishName $leaf) { $unclaimed.Add($rel) }
        continue
    }

    $facts = Get-DocFacts $f
    $rows.Add([pscustomobject]@{
            Kind    = $k.Kind
            Owner   = $k.Owner
            What    = $k.What
            Path    = $rel
            Updated = $facts.Updated
            Status  = $facts.Status
            Entries = $facts.Entries
            Age     = [int]((Get-Date) - $f.LastWriteTime).TotalDays
        })
}

# ---------------------------------------------------------------- вывод

if (-not $Check) {
    $order = 'tech-journal', 'gdd-adr', 'registry', 'run-log', 'forks', 'drift', 'ideas',
    'inbox-max', 'inbox-archive', 'backlog', 'worklog', 'archive'

    foreach ($kind in $order) {
        $group = @($rows | Where-Object Kind -eq $kind)
        if (-not $group) { continue }

        $what = $group[0].What
        $owner = $group[0].Owner
        Write-Host ("== {0}  [{1}]  {2}" -f $kind, $owner, $what) -ForegroundColor Cyan

        # Журнал-как-каталог и журналы заходов сворачиваются в сводку: поштучно они не читаются.
        if (($kind -eq 'tech-journal') -or ($kind -eq 'worklog' -and -not $Worklogs)) {
            $newest = ($group | Sort-Object Updated -Descending | Select-Object -First 1)
            Write-Host ("   файлов {0}, свежий {1} — {2}" -f $group.Count, $newest.Updated, $newest.Path)
            if ($kind -eq 'worklog') {
                $stale = @($group | Where-Object { $_.Age -gt $StaleDays })
                if ($stale) { Write-Host ("   не тронуты >{0} дн: {1}" -f $StaleDays, $stale.Count) -ForegroundColor DarkYellow }
                Write-Host "   поштучно: -Worklogs" -ForegroundColor DarkGray
            }
        }
        else {
            foreach ($r in ($group | Sort-Object Updated -Descending)) {
                $st = if ($r.Status) { " ($($r.Status))" } else { '' }
                Write-Host ("   {0,-11} записей {1,-4} {2}{3}" -f $r.Updated, $r.Entries, $r.Path, $st)
            }
        }
        Write-Host ''
    }

    Write-Host ("Итого: {0} файлов, {1} жанров. Маршрут «жанр факта → дом» — CLAUDE.md." -f $rows.Count, (@($rows.Kind | Sort-Object -Unique)).Count) -ForegroundColor DarkGray
    if ($unclaimed.Count -gt 0) {
        Write-Host ("Без жанра: {0} — прогони -Check" -f $unclaimed.Count) -ForegroundColor Yellow
    }
    return
}

# ---------------------------------------------------------------- гейт

$problems = 0

foreach ($u in $unclaimed) {
    Write-Host "БЕЗ ЖАНРА  $u" -ForegroundColor Yellow
    Write-Host "           похоже на журнал, но конвенция его не покрывает: назначь жанр в journals.ps1 или переименуй" -ForegroundColor DarkGray
    $problems++
}

foreach ($r in ($rows | Where-Object { $_.Kind -eq 'worklog' -and $_.Age -gt $StaleDays })) {
    Write-Host ("БРОШЕН?    {0} — не тронут {1} дн" -f $r.Path, $r.Age) -ForegroundColor Yellow
    Write-Host "           заход закрыт? решения переехали в журнал, файл — под удаление" -ForegroundColor DarkGray
    $problems++
}

# Архив можно править — ссылки в нём чинятся при переездах вики. Нельзя ДОПИСЫВАТЬ: это
# признак записи, которая должна была уйти в живой журнал. Разница читается по numstat
# последнего коммита: правка ссылки даёт «+1 -1», новая запись — «+N -0».
# Только жанр archive: `status: archive` во frontmatter носят и пополняемые по замыслу файлы —
# закрытые развилки дополняются при каждом закрытии, разбор инбокса создаётся файлом целиком.
foreach ($r in ($rows | Where-Object { $_.Kind -eq 'archive' })) {
    $stat = & git -C $repo log -1 --numstat --format='' -- $r.Path 2>$null | Select-Object -First 1
    if (-not $stat) { continue }
    $parts = $stat -split '\s+'
    if ($parts.Count -lt 2) { continue }
    $added = 0; $deleted = 0
    [void][int]::TryParse($parts[0], [ref]$added)
    [void][int]::TryParse($parts[1], [ref]$deleted)
    if ($added -gt $deleted) {
        Write-Host ("АРХИВ ДОПИСАН {0} — последний коммит +{1} -{2}" -f $r.Path, $added, $deleted) -ForegroundColor Yellow
        Write-Host "              архив не пополняется: запись должна была уйти в живой журнал" -ForegroundColor DarkGray
        $problems++
    }
}

if ($problems -eq 0) {
    Write-Host "Журналы в порядке: жанр объявлен у всех, брошенных заходов нет, архивы не тронуты." -ForegroundColor Green
    exit 0
}
Write-Host "`nПроблем: $problems" -ForegroundColor Yellow
exit 1
