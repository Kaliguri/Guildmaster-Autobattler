#requires -Version 7
<#
.SYNOPSIS
  Stat "database" tool for our ScriptableObject data (relics + enemies).
  Reads/edits the `_stats` list (StatType -> Value) directly in the .asset YAML, no Unity required.

  Stat build formula (mirrors Combat/Stats.cs):
    final = (baseTerm + SUM Flat) * (1 + SUM PercentAdd) * PROD(1 + PercentMult)
    baseTerm = Override(if present) else StatsConfig default (else natural default)
  So `Override` (op 3) = absolute authoring: the relic states the real value; the config default is only a
  fallback for unspecified stats + a GD starting point for new SOs.

.EXAMPLES
  ./scripts/statdb.ps1 list                       # effective values table
  ./scripts/statdb.ps1 get IronSpearman MaxHP
  ./scripts/statdb.ps1 set IronSpearman MaxHP 2200   # writes an Override entry (op 3)
  ./scripts/statdb.ps1 scale MaxHP 10 -DryRun        # scales the stored value in place
  ./scripts/statdb.ps1 migrate 10 -DryRun            # delta(Flat)->Override(absolute), x10 on magnitude stats

.NOTES
  Editing .asset on disk while Unity has that asset open in an inspector can race. Prefer edits with the
  asset not selected, then let Unity reimport (focus the editor).
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'get', 'set', 'scale', 'migrate')][string]$Command = 'list',
    [Parameter(Position = 1)][string]$A1,
    [Parameter(Position = 2)][string]$A2,
    [Parameter(Position = 3)][string]$A3,
    [string[]]$Stats,
    [string[]]$Only,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# --- StatType enum (source of truth: Assets/_Project/Scripts/Data/Stats/StatType.cs) ---
$StatNames = @(
    'MaxHP', 'HpRegenFlat', 'HpRegenPct', 'PhysArmor', 'MagicArmor', 'DamageTakenEff', 'HealShieldTakenEff',
    'AutoAttackDamage', 'AttackSpeed', 'AttackRange', 'AbilityPower', 'PhysPen', 'PhysPenPct', 'MagicPen',
    'MagicPenPct', 'DamageDealtEff', 'Lifesteal', 'HealShieldDealtEff', 'ProjectileSpeed', 'ProjectilePierce',
    'MoveSpeed', 'Size', 'ApplyBuffEff', 'ApplyDebuffEff', 'ReceiveBuffEff', 'ReceiveDebuffEff', 'CooldownEff',
    'MaxResource', 'StartResource', 'ResourceGainEff'
)
$NameToId = @{}
for ($i = 0; $i -lt $StatNames.Count; $i++) { $NameToId[$StatNames[$i]] = $i }

# Stats whose natural default is 1.0 (PercentMult effectiveness family + Size); everything else 0.
$NaturalOneIds = @(5, 6, 15, 17, 21, 22, 23, 24, 25, 26, 29)

# Magnitude stats that get the uniform scale in `migrate` (HP + auto-attack damage). Ratio/timing/economy
# stats (armor, speeds, ranges, resource) are magnitude-independent -> converted to absolute, NOT scaled.
$MagnitudeIds = @(0, 7)

function Resolve-StatId([string]$name) {
    if ($NameToId.ContainsKey($name)) { return $NameToId[$name] }
    $n = 0
    if ([int]::TryParse($name, [ref]$n) -and $n -ge 0 -and $n -lt $StatNames.Count) { return $n }
    throw "Unknown stat '$name'. Known: $($StatNames -join ', ')"
}

# --- Paths / discovery ---------------------------------------------------------------
$root = Split-Path -Parent $PSScriptRoot
$dirs = @(
    (Join-Path $root 'Assets/_Project/ScriptableObjects/Relics'),
    (Join-Path $root 'Assets/_Project/ScriptableObjects/Enemies')
) | Where-Object { Test-Path $_ }
$cfgPath = Join-Path $root 'Assets/_Project/ScriptableObjects/Configs/StatsConfig.asset'

function Get-Assets { Get-ChildItem -Path $dirs -Filter *.asset -File | Sort-Object Name }

# StatsConfig defaults (format: '- Stat: N' then 'Value: V', no Op line).
function Get-BaseDefaults {
    $d = @{}
    if (Test-Path $cfgPath) {
        $cfg = Get-Content -Raw $cfgPath
        foreach ($m in [regex]::Matches($cfg, '- Stat: (\d+)\s*\r?\n\s*Value: (-?[0-9.eE+]+)')) {
            $d[[int]$m.Groups[1].Value] = [double]$m.Groups[2].Value
        }
    }
    return $d
}
function Get-BaseDefault($defaults, [int]$id) {
    if ($defaults.ContainsKey($id)) { return $defaults[$id] }
    if ($NaturalOneIds -contains $id) { return 1.0 }
    return 0.0
}

# One relic/enemy stat entry: '- Stat: N', 'Op: X', 'Value: V' (fixed order in our serialization).
function Get-StatBlockRegex([int]$id) { "(- Stat: $id(?=\r?\n)\s*\r?\n\s*Op: \d+\s*\r?\n\s*Value: )(-?[0-9.eE+]+)" }

function Read-StatEntry([string]$text, [int]$id) {
    $m = [regex]::Match($text, "- Stat: $id(?=\r?\n)\s*\r?\n\s*Op: (\d+)\s*\r?\n\s*Value: (-?[0-9.eE+]+)")
    if (-not $m.Success) { return $null }
    return [pscustomobject]@{ Op = [int]$m.Groups[1].Value; Value = [double]$m.Groups[2].Value }
}

# Effective stored contribution as a base term: Override -> value; Flat -> default + value.
function Read-Effective([string]$text, [int]$id, $defaults) {
    $e = Read-StatEntry $text $id
    if ($null -eq $e) { return $null }
    if ($e.Op -eq 3) { return $e.Value }                       # Override = absolute
    return (Get-BaseDefault $defaults $id) + $e.Value           # Flat delta over default
}

function Write-StatValue([string]$text, [int]$id, [double]$value) {
    $vs = $value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return [regex]::Replace($text, (Get-StatBlockRegex $id), "`${1}$vs")
}

function Match-Filter([string]$name, [string[]]$only) {
    if (-not $only -or $only.Count -eq 0) { return $true }
    foreach ($o in $only) { if ($name -like "*$o*") { return $true } }
    return $false
}
function Get-Eol([string]$t) { if ($t -match "\r\n") { "`r`n" } else { "`n" } }
function Fmt([double]$v) { ([math]::Round($v, 4)).ToString([System.Globalization.CultureInfo]::InvariantCulture) }

# --- Commands ------------------------------------------------------------------------
switch ($Command) {

    'list' {
        $defaults = Get-BaseDefaults
        $cols = if ($Stats) { $Stats } else { @('MaxHP', 'AutoAttackDamage', 'AttackSpeed', 'AttackRange', 'PhysArmor', 'MoveSpeed', 'MaxResource') }
        $ids = $cols | ForEach-Object { Resolve-StatId $_ }
        $rows = foreach ($a in Get-Assets) {
            $t = Get-Content -Raw $a.FullName
            $o = [ordered]@{ Asset = $a.BaseName }
            for ($i = 0; $i -lt $cols.Count; $i++) {
                $v = Read-Effective $t $ids[$i] $defaults
                $o[$cols[$i]] = if ($null -eq $v) { (Get-BaseDefault $defaults $ids[$i]) } else { $v }
            }
            [pscustomobject]$o
        }
        Write-Host "Effective values (base default + Flat, or Override). Base defaults fill unspecified stats." -ForegroundColor DarkGray
        $rows | Format-Table -AutoSize
    }

    'get' {
        if (-not $A1 -or -not $A2) { throw "Usage: get <assetPattern> <stat>" }
        $defaults = Get-BaseDefaults
        $id = Resolve-StatId $A2
        foreach ($a in Get-Assets | Where-Object { $_.BaseName -like "*$A1*" }) {
            $t = Get-Content -Raw $a.FullName
            $e = Read-StatEntry $t $id
            $eff = Read-Effective $t $id $defaults
            $raw = if ($null -eq $e) { "(from base)" } else { "op=$($e.Op) val=$($e.Value)" }
            Write-Host ("{0,-16} {1}: effective={2}  [{3}]" -f $a.BaseName, $A2, ($(if ($null -eq $eff) { Get-BaseDefault $defaults $id } else { $eff })), $raw)
        }
    }

    'set' {
        if (-not $A1 -or -not $A2 -or -not $A3) { throw "Usage: set <assetPattern> <stat> <value>  (writes an Override)" }
        $id = Resolve-StatId $A2
        $val = [double]$A3
        foreach ($a in Get-Assets | Where-Object { $_.BaseName -like "*$A1*" }) {
            $t = Get-Content -Raw $a.FullName
            $e = Read-StatEntry $t $id
            if ($null -eq $e) { Write-Host ("{0,-16} {1}: absent (add it in the SO first)" -f $a.BaseName, $A2) -ForegroundColor Yellow; continue }
            # Force op to Override (3) and set value.
            $nt = [regex]::Replace($t, "(- Stat: $id(?=\r?\n)\s*\r?\n\s*Op: )\d+(\s*\r?\n\s*Value: )-?[0-9.eE+]+", "`${1}3`${2}$(Fmt $val)")
            if ($DryRun) { Write-Host ("{0,-16} {1}: -> Override {2} (dry)" -f $a.BaseName, $A2, (Fmt $val)) }
            else { [System.IO.File]::WriteAllText($a.FullName, $nt); Write-Host ("{0,-16} {1}: -> Override {2}" -f $a.BaseName, $A2, (Fmt $val)) -ForegroundColor Green }
        }
    }

    'scale' {
        if (-not $A1 -or -not $A2) { throw "Usage: scale <stat> <factor> [-Only a,b] [-DryRun]" }
        $id = Resolve-StatId $A1
        $factor = [double]$A2
        foreach ($a in Get-Assets) {
            if (-not (Match-Filter $a.BaseName $Only)) { continue }
            $t = Get-Content -Raw $a.FullName
            $e = Read-StatEntry $t $id
            if ($null -eq $e) { continue }
            $new = [math]::Round($e.Value * $factor, 4)
            $nt = Write-StatValue $t $id $new
            if ($DryRun) { Write-Host ("{0,-16} {1}: {2} -> {3} (dry)" -f $a.BaseName, $A1, $e.Value, $new) }
            else { [System.IO.File]::WriteAllText($a.FullName, $nt); Write-Host ("{0,-16} {1}: {2} -> {3}" -f $a.BaseName, $A1, $e.Value, $new) -ForegroundColor Green }
        }
    }

    'migrate' {
        # Convert every relic/enemy `_stats` entry from Flat delta -> Override absolute (effective = default + delta).
        # Magnitude stats (MaxHP, AutoAttackDamage) additionally multiplied by <factor>.
        $factor = if ($A1) { [double]$A1 } else { 10.0 }
        $defaults = Get-BaseDefaults
        $blockRx = '(?m)^  _stats:\r?\n((?:  - Stat: \d+\r?\n    Op: \d+\r?\n    Value: -?[0-9.eE+]+\r?\n)+)'
        foreach ($a in Get-Assets) {
            if (-not (Match-Filter $a.BaseName $Only)) { continue }
            $t = Get-Content -Raw $a.FullName
            $mm = [regex]::Match($t, $blockRx)
            if (-not $mm.Success) { Write-Host ("{0,-16} no stats list, skipped" -f $a.BaseName) -ForegroundColor DarkGray; continue }
            $eol = Get-Eol $t
            $entries = [regex]::Matches($mm.Groups[1].Value, '- Stat: (\d+)\r?\n\s*Op: (\d+)\r?\n\s*Value: (-?[0-9.eE+]+)')
            $sb = [System.Text.StringBuilder]::new()
            [void]$sb.Append("  _stats:$eol")
            $report = @()
            foreach ($e in $entries) {
                $id = [int]$e.Groups[1].Value; $op = [int]$e.Groups[2].Value; $val = [double]$e.Groups[3].Value
                $eff = if ($op -eq 3) { $val } else { (Get-BaseDefault $defaults $id) + $val }
                if ($MagnitudeIds -contains $id) { $eff = $eff * $factor }
                $eff = [math]::Round($eff, 4)
                [void]$sb.Append("  - Stat: $id$eol    Op: 3$eol    Value: $(Fmt $eff)$eol")
                $report += ("{0}={1}" -f $StatNames[$id], (Fmt $eff))
            }
            $nt = $t.Remove($mm.Index, $mm.Length).Insert($mm.Index, $sb.ToString())
            $tag = if ($DryRun) { "(dry)" } else { "" }
            Write-Host ("{0,-16} {1} {2}" -f $a.BaseName, ($report -join '  '), $tag) -ForegroundColor $(if ($DryRun) { 'Gray' } else { 'Green' })
            if (-not $DryRun) { [System.IO.File]::WriteAllText($a.FullName, $nt) }
        }
    }
}
