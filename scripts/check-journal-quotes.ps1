#Requires -Version 7.0
<#
.SYNOPSIS
    Сверяет цитаты Макса в журналах решений с архивом диалогов — дословно.

.DESCRIPTION
    В журналах цитата стоит как доказательство: «решили так, потому что Макс сказал так».
    Доказательство обязано совпадать с источником посимвольно. Замер 31.07.2026 показал,
    что цитаты в ГД-журнале есть почти везде (65 записей из 66 за неделю), но часть из них
    ПРИЧЁСАНА: выброшено начальное «Нет,», исправлена опечатка, отрезан хвост без многоточия.
    Смысл при этом держится — теряется то, что фраза была возражением, и теряется его голос.

    Скрипт вытаскивает цитаты из журналов, ищет их в авторских репликах архива и печатает
    расхождения: что в документе против того, что было сказано.

    Гейтом в CI это стать не может: архив диалогов живёт в личном окружении агента
    (~/.claude/projects), в раннере его нет. Прогон локальный, руками.

.PARAMETER Since
    Проверять записи не старше этой даты (ГГГГ-ММ-ДД). По умолчанию — вся история.

.PARAMETER Fix
    Не правит файлы, а печатает готовые пары «было / стало» для ручной замены:
    молча переписывать журнал скриптом нельзя, это второй способ исказить запись.

.EXAMPLE
    ./scripts/check-journal-quotes.ps1 -Since 2026-07-26
#>
[CmdletBinding()]
param(
    [string]$Since = '',
    [int]$MinLength = 25,
    [switch]$Fix
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$archive = Join-Path $env:USERPROFILE '.claude\projects'

if (-not (Test-Path $archive)) {
    Write-Host "Архив диалогов не найден: $archive — сверять не с чем." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------- нормализация

# Для ПОИСКА кандидата нормализуем агрессивно (кавычки, пробелы, ё, регистр, markdown),
# а сравниваем потом строго: иначе причёсанную цитату не найти вовсе, и расхождение
# останется невидимым — тем самым, из-за которого скрипт и написан.
function Get-SearchForm {
    param([string]$Text)
    $t = $Text
    $t = $t -replace '[«»„“”"‟]', '"'
    $t = $t -replace '[–—]', '-'
    $t = $t -replace '\*\*|__|\*|`', ''
    $t = $t -replace '\s+', ' '
    $t = $t.Replace('ё', 'е').Replace('Ё', 'Е')
    return $t.Trim().ToLower()
}

function Get-Plain {
    param([string]$Text)
    $t = $Text -replace '\*\*|__|`', ''
    return ($t -replace '\s+', ' ').Trim()
}

# Слова без пунктуации: по ним видно, ЧТО именно случилось с цитатой — выброшены слова,
# переставлены или переписаны. Сравнение по сырой строке этого не различает.
function Get-Words {
    param([string]$Text)
    $t = (Get-SearchForm $Text) -replace '[^\p{L}\p{Nd}]+', ' '
    return @($t.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
}

# Допуск на исправленную опечатку: «мелками» → «мелкими» это одна замена,
# «центральаня» → «центральная» — транспозиция. Больше двух правок = другое слово.
function Test-WordSame {
    param([string]$A, [string]$B)
    if ($A -eq $B) { return $true }
    $limit = if ([Math]::Min($A.Length, $B.Length) -ge 6) { 2 } else { 1 }
    if ([Math]::Abs($A.Length - $B.Length) -gt $limit) { return $false }

    $n = $A.Length; $m = $B.Length
    $prev = 0..$m
    for ($i = 1; $i -le $n; $i++) {
        $cur = @($i) + (1..$m | ForEach-Object { 0 })
        for ($j = 1; $j -le $m; $j++) {
            $cost = if ($A[$i - 1] -eq $B[$j - 1]) { 0 } else { 1 }
            $cur[$j] = [Math]::Min([Math]::Min($cur[$j - 1] + 1, $prev[$j] + 1), $prev[$j - 1] + $cost)
        }
        $prev = $cur
    }
    return $prev[$m] -le $limit
}

# Ищет слова цитаты в словах реплики. Возвращает вердикт: как именно цитата отличается.
function Get-QuoteVerdict {
    param([string[]]$Quote, [string[]]$Source, [bool]$HasEllipsis)

    if ($Quote.Count -eq 0) { return $null }

    # 1. Подряд, слово в слово (с допуском на опечатку) — расхождение только в пунктуации,
    #    кавычках, регистре или в правленой опечатке.
    for ($s = 0; $s -le $Source.Count - $Quote.Count; $s++) {
        $ok = $true
        $typos = 0
        for ($k = 0; $k -lt $Quote.Count; $k++) {
            if ($Quote[$k] -eq $Source[$s + $k]) { continue }
            if (Test-WordSame $Quote[$k] $Source[$s + $k]) { $typos++; continue }
            $ok = $false; break
        }
        if ($ok) { return @{ Kind = if ($typos -gt 0) { 'опечатка правлена' } else { 'пунктуация/регистр' }; Typos = $typos } }
    }

    # 2. Все слова идут в том же порядке, но с пропусками — из фразы выброшена середина.
    $si = 0; $matched = 0; $gaps = 0; $lastHit = -1
    foreach ($w in $Quote) {
        $found = -1
        for ($j = $si; $j -lt $Source.Count; $j++) {
            if ((Test-WordSame $w $Source[$j])) { $found = $j; break }
        }
        if ($found -lt 0) { break }
        if ($lastHit -ge 0 -and $found -gt $lastHit + 1) { $gaps++ }
        $lastHit = $found; $si = $found + 1; $matched++
    }
    if ($matched -eq $Quote.Count) {
        # Пропуск, помеченный многоточием, — законное сокращение, а не искажение: правило
        # запрещает выбрасывать середину МОЛЧА.
        if ($gaps -eq 0 -or $HasEllipsis) { return @{ Kind = 'сокращено многоточием'; Gaps = $gaps } }
        return @{ Kind = 'выброшено без многоточия'; Gaps = $gaps }
    }

    # 3. Слова есть, но не в том порядке — части фразы переставлены.
    $present = 0
    foreach ($w in $Quote) {
        foreach ($sw in $Source) { if (Test-WordSame $w $sw) { $present++; break } }
    }
    # Порог высокий намеренно. При мягком (0.6) скрипт «находил» источник в чужой реплике:
    # Макс часто вставляет в свой промпт мой предыдущий ответ целиком, и общих слов хватало
    # на ложное совпадение. Догадка в отчёте о точности цитат хуже честного «не найдено».
    if ($present -ge [Math]::Ceiling($Quote.Count * 0.92)) { return @{ Kind = 'порядок изменён' } }
    return $null
}

# ---------------------------------------------------------------- архив в память

Write-Host "Читаю архив реплик Макса…" -ForegroundColor DarkGray
$sw = [Diagnostics.Stopwatch]::StartNew()

$turns = [System.Collections.Generic.List[object]]::new()
$injected = 'Base directory for this skill:', '<task-notification>', '<local-command-',
'This session is being continued from a previous conversation', 'Primary Request and Intent',
'Caveat: The messages below were generated'

foreach ($f in (Get-ChildItem $archive -Recurse -Filter *.jsonl -File)) {
    foreach ($line in [IO.File]::ReadLines($f.FullName)) {
        if (-not $line.Contains('"type":"user"')) { continue }
        if ($line.Contains('"tool_result"')) { continue }
        try { $o = $line | ConvertFrom-Json } catch { continue }
        if ($o.isSidechain) { continue }

        # Реплика приходит строкой, но КАК ТОЛЬКО к сообщению приложен скриншот — массивом
        # блоков. Макс прикладывает их постоянно, так что игнорировать массив значило терять
        # почти половину его слов: первый прогон объявил 45 цитат «источник не найден» ровно
        # по этой причине, а не из-за искажения.
        $c = $o.message.content
        $raw = if ($c -is [string]) { $c }
        else { (@($c | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join "`n") }
        if (-not $raw) { continue }

        $skip = $false
        foreach ($inj in $injected) { if ($raw.Contains($inj)) { $skip = $true; break } }
        if ($skip) { continue }

        $plain = Get-Plain ($raw -replace '(?s)<system-reminder>.*?</system-reminder>', '')
        if ($plain.Length -lt 20) { continue }

        $turns.Add([pscustomobject]@{
                Plain  = $plain
                Search = Get-SearchForm $plain
                Words  = Get-Words $plain
                Date   = if ($o.timestamp) { ([datetime]$o.timestamp).ToLocalTime().ToString('yyyy-MM-dd') } else { '' }
            })
    }
}
$sw.Stop()

# Одна реплика живёт в нескольких файлах (resume и форки копируют её), для поиска это неважно.
Write-Host ("Реплик: {0} за {1:n1} сек" -f $turns.Count, $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray

# Обратный индекс «слово → номера реплик». Без него на каждую цитату приходится полный проход
# по пяти тысячам реплик со сравнением слов — минуты на одну запись журнала. С ним кандидаты
# отбираются пересечением постинг-листов самых РЕДКИХ слов цитаты: обычно остаётся один-два.
$index = @{}
for ($i = 0; $i -lt $turns.Count; $i++) {
    foreach ($w in ($turns[$i].Words | Sort-Object -Unique)) {
        if ($w.Length -lt 4) { continue }
        if (-not $index.ContainsKey($w)) { $index[$w] = [System.Collections.Generic.List[int]]::new() }
        $index[$w].Add($i)
    }
}
# .Count у хэш-таблицы врёт, если среди ключей есть слово «count» — а оно среди реплик есть.
Write-Host ("Индекс: {0} слов`n" -f $index.Keys.Count) -ForegroundColor DarkGray

function Get-Candidates {
    param([string[]]$Words)
    $rare = @($Words | Where-Object { $_.Length -ge 4 -and $index.ContainsKey($_) } |
        Sort-Object -Unique | Sort-Object { $index[$_].Count } | Select-Object -First 5)
    if ($rare.Count -eq 0) { return @() }

    $set = [System.Collections.Generic.HashSet[int]]::new($index[$rare[0]])
    foreach ($w in ($rare | Select-Object -Skip 1)) {
        $probe = [System.Collections.Generic.HashSet[int]]::new($set)
        $probe.IntersectWith($index[$w])
        if ($probe.Count -gt 0) { $set = $probe }
    }
    if ($set.Count -gt 0 -and $set.Count -le 400) { return @($set) }

    # Пересечение развалилось — а причина ровно та, из-за которой скрипт и написан: правленая
    # опечатка. Слово в доке («пиксель») в индексе есть от других реплик, а в нужной стоит
    # «писксель», поэтому пересечение по нему пусто, а поиск по нему одному ведёт не туда.
    # Поэтому здесь ОБЪЕДИНЕНИЕ по всем редким словам, а не самое редкое из них.
    $union = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($w in $rare) { $union.UnionWith($index[$w]) }
    return @($union)
}

# ---------------------------------------------------------------- цитаты из журналов

$sources = @()
$sources += Get-Item (Join-Path $repo 'docs\wiki\gdd\00-meta\journal-adr.md')
$sources += Get-ChildItem (Join-Path $repo 'docs\wiki\tech\00-meta\journal') -Filter *.md -File

# Цитата — текст в кавычках-лапках. Внутри могут быть прямые кавычки, поэтому берём жадно до «».
$quoteRx = [regex]'«([^»]{25,900})»'

# В кавычки у нас попадает не только речь Макса: заголовки записей, названия механик, термины
# («Ядро и кайма»), имена файлов. Проверять их бессмысленно — источника в архиве и не должно быть.
# Цитатой считаем то, что заявлено цитатой: слева стоит маркер цитирования.
$quoteMarkers = 'дословно', 'сказано', 'вердикт', 'просил', 'попросил', 'сформулировал',
'слова макса', 'макс:', 'решение макса', 'цитата', 'постановка'

function Test-IsClaimedQuote {
    param([string]$Section, [int]$Index)
    $from = [Math]::Max(0, $Index - 140)
    $before = $Section.Substring($from, $Index - $from).ToLower()
    foreach ($m in $quoteMarkers) { if ($before.Contains($m)) { return $true } }
    return $false
}

$checked = 0
$exact = 0
$problems = [System.Collections.Generic.List[object]]::new()

foreach ($src in $sources) {
    $text = Get-Content $src.FullName -Raw
    $rel = ($src.FullName.Substring($repo.Length).TrimStart('\') -replace '\\', '/')

    # Ограничение по дате: у tech-журнала дата в имени файла, у ADR — в заголовке секции.
    if ($Since) {
        if ($src.Name -match '^(\d{4}-\d{2}-\d{2})') {
            if ($Matches[1] -lt $Since) { continue }
        }
    }

    # Разбиваем ADR по секциям, чтобы знать дату записи и не проверять древние.
    $sections = if ($src.Name -eq 'journal-adr.md') { $text -split "(?m)^(?=## )" } else { , $text }

    foreach ($sec in $sections) {
        $secDate = ''
        if ($sec -match '^## (\d{4}-\d{2}-\d{2})') { $secDate = $Matches[1] }
        elseif ($sec -match '(?m)^date:\s*(\d{4}-\d{2}-\d{2})') { $secDate = $Matches[1] }
        if ($Since -and $secDate -and $secDate -lt $Since) { continue }

        $header = (($sec -split "`n")[0]).Trim()

        foreach ($m in $quoteRx.Matches($sec)) {
            $quote = Get-Plain $m.Groups[1].Value
            if ($quote.Length -lt $MinLength) { continue }
            if (-not (Test-IsClaimedQuote $sec $m.Index)) { continue }
            $checked++

            $needle = Get-SearchForm $quote
            $hit = $turns | Where-Object { $_.Search.Contains($needle) } | Select-Object -First 1
            if ($hit) { $exact++; continue }

            # Не совпало символ в символ — выясняем, что именно случилось, и по лучшему вердикту.
            # Порядок вердиктов от безобидного к серьёзному: одна и та же цитата может «почти
            # совпасть» с одной репликой и «пересказаться» из другой, брать надо лучший случай.
            $qw = Get-Words $quote
            $hasEll = ($quote.Contains('…') -or $quote.Contains('...'))
            $rank = @{ 'пунктуация/регистр' = 1; 'сокращено многоточием' = 2; 'опечатка правлена' = 3; 'выброшено без многоточия' = 4; 'порядок изменён' = 5 }
            $best = $null; $bestTurn = $null

            foreach ($ci in (Get-Candidates $qw)) {
                $t = $turns[$ci]
                $v = Get-QuoteVerdict $qw $t.Words $hasEll
                if (-not $v) { continue }
                if (-not $best -or $rank[$v.Kind] -lt $rank[$best.Kind]) { $best = $v; $bestTurn = $t }
                if ($best.Kind -eq 'пунктуация/регистр') { break }
            }

            $problems.Add([pscustomobject]@{
                    File    = $rel
                    Section = $header
                    Date    = $secDate
                    Quote   = $quote
                    Kind    = if ($best) { $best.Kind } else { 'источник не найден' }
                    Near    = $bestTurn
                })
        }
    }
}

# ---------------------------------------------------------------- отчёт

Write-Host ("Проверено цитат: {0}; совпали символ в символ: {1}`n" -f $checked, $exact) -ForegroundColor Cyan

Write-Host "Расхождения по классам:" -ForegroundColor Cyan
foreach ($g in ($problems | Group-Object Kind | Sort-Object Count -Descending)) {
    Write-Host ("  {0,-26} {1}" -f $g.Name, $g.Count)
}
Write-Host ''

# Серьёзные классы разбираются глазами: там смысл записи мог поехать.
# Реплика печатается окном вокруг совпадения: Макс нередко вставляет в промпт мой прошлый
# ответ целиком, и печать «сказано» полностью превращает отчёт в простыню на десятки экранов.
function Show-Window {
    param([string]$Source, [string]$Quote, [int]$Pad = 140)
    $words = Get-Words $Quote
    $anchor = ($words | Where-Object { $_.Length -ge 5 } | Select-Object -First 1)
    $idx = if ($anchor) { (Get-SearchForm $Source).IndexOf($anchor) } else { 0 }
    if ($idx -lt 0) { $idx = 0 }
    $from = [Math]::Max(0, $idx - $Pad)
    $len = [Math]::Min($Source.Length - $from, $Quote.Length + 2 * $Pad)
    $w = $Source.Substring($from, $len)
    if ($from -gt 0) { $w = '…' + $w }
    if ($from + $len -lt $Source.Length) { $w = $w + '…' }
    return $w
}

foreach ($kind in 'порядок изменён', 'выброшено без многоточия', 'опечатка правлена') {
    $group = @($problems | Where-Object Kind -eq $kind)
    if (-not $group) { continue }
    Write-Host ("== {0}: {1} ==" -f $kind, $group.Count) -ForegroundColor Yellow
    foreach ($p in $group) {
        Write-Host ("--- {0}" -f $p.Section) -ForegroundColor DarkYellow
        Write-Host ("    {0}" -f $p.File) -ForegroundColor DarkGray
        Write-Host ("    в доке:  «{0}»" -f $p.Quote)
        if ($p.Near) { Write-Host ("    сказано ({0}): {1}" -f $p.Near.Date, (Show-Window $p.Near.Plain $p.Quote)) -ForegroundColor Green }
        Write-Host ''
    }
}

$noSource = @($problems | Where-Object Kind -eq 'источник не найден')
if ($noSource) {
    Write-Host ("== Источник не найден: {0} ==" -f $noSource.Count) -ForegroundColor DarkYellow
    Write-Host "Либо цитата не Макса (моя реплика, термин, чужой текст), либо сессия не сохранилась." -ForegroundColor DarkGray
    foreach ($p in ($noSource | Select-Object -First 20)) {
        Write-Host ("  [{0}] «{1}»" -f $p.Date, ($p.Quote.Substring(0, [Math]::Min(100, $p.Quote.Length))))
    }
    Write-Host ''
}

if ($problems.Count -eq 0) {
    Write-Host "Все цитаты совпадают с архивом дословно." -ForegroundColor Green
    exit 0
}
exit 1
