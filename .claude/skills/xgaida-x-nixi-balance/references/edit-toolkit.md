# Инструмент правок

Write-сторона петли. Read даёт таблицы, которые некуда применить; write без read — правки вслепую.

## ContentEditService — одна правка

Editor-only, статические методы, каждый возвращает `Change` (было → стало → применилась ли).
Namespace `Guildmaster.Data.Editor`, зовётся из `execute_code`.

| Метод | Что правит |
|---|---|
| `LoadAll<T>()` · `Resolve<T>(idOrName)` | выборка ассетов (по id, запасной путь — по имени) |
| `ScaleStat(unit, StatType, factor)` | умножить значение стата в `_stats` |
| `SetStat(unit, stat, ModifierOp, value)` | задать стат абсолютным значением |
| `SetFloat(asset, propertyPath, value)` · `AddFloat(asset, path, delta)` | любое float-поле по пути сериализации |
| `AddAbilityCooldown(unit, abilityId, delta)` | кулдаун способности |
| `SetEffectComponentFloat(effect, fieldName, value)` | поле компонента эффекта ПО ИМЕНИ, без знания индекса в массиве |
| `Save()` | `AssetDatabase.SaveAssets()` в конце |
| `WriteChangeLog(changes, title)` | `BalanceReports/balance_changes_*.md` — аудит «что крутили» |

Всё идёт через `SerializedObject` + `Undo`. Ручная правка YAML и точечный `execute_code` по полю —
запрещены: правка теряется при domain reload и не попадает в аудит.

## ContentCohorts — кого правим

Балансная правка почти никогда не касается одного кита: подтягивают когорту целиком, иначе внутри
роли расползается разнобой.

```csharp
ContentCohorts.OfClass(UnitClass.Tank)          // все Танки — и реликвии, и враги
ContentCohorts.OfAttackType(AttackType.Melee)   // все ближники
ContentCohorts.OfSchool(DamageSchool.Magic)     // все маги
ContentCohorts.OfCreatureType(CreatureType.Undead)
ContentCohorts.WithIdPrefix("enemy.goblin")     // по id, не по имени ассета
ContentCohorts.Where<RelicData>(r => r.Abilities.Length > 1)
```

`AllUnits()` намеренно объединяет реликвии и врагов: класс, оружие и статы у них общие, поэтому
классовая правка обязана задевать обе стороны — подтянуть Танков только у игрока значит сломать
бой, а не починить роль.

## ContentEditBatch — пакет с откатом

Пресет описывает набор правок; применяется целиком, откатывается целиком. Файлы живут в
`BalanceReports/presets/` (версионируются — история попыток ценнее самих чисел).

```json
{
  "title": "BAL-001 вариант 2: Друид становится Дальником",
  "edits": [
    { "op": "scaleStat", "asset": "Druid", "stat": "Power", "factor": 0.58 },
    { "op": "setStat", "asset": "BaseRelic", "stat": "MaxHP", "modOp": "Override", "value": 2000 },
    { "op": "setValue", "asset": "BulwarkShield", "path": "_baseDuration", "value": 0.5 },
    { "op": "addValue", "asset": "Ranger", "path": "_movingAttackSpeedPenaltyPct", "delta": 0.1 },
    { "op": "addCooldown", "asset": "Defender", "ability": "ability.bulwark", "delta": 1.0 },
    { "op": "setEffectField", "asset": "BulwarkShield", "field": "_internalCooldownSeconds", "value": 5 },
    { "op": "removeStat", "asset": "Druid", "stat": "MaxResource" },
    { "op": "scaleStat", "cohort": { "class": "Tank" }, "stat": "MaxHP", "factor": 1.1 }
  ]
}
```

Операций ровно семь, и имена у них такие: `scaleStat`, `setStat`, `removeStat`, `setValue`,
`addValue`, `addCooldown`, `setEffectField`. Неизвестная операция не применяется — только пишет
предупреждение в консоль, так что опечатка выглядит как «пакет применился, но ничего не изменил».

Применение:

```csharp
var root = System.IO.Directory.GetParent(Application.dataPath).FullName;
Guildmaster.Data.Editor.ContentEditBatch.Apply(
    System.IO.Path.Combine(root, "BalanceReports", "presets", "bal-001-variant-2.json"));
```

Рядом появляется `*.undo.json` — обратный пресет из абсолютных значений «как было», в обратном
порядке. Откат = применение этого файла; никакой скрытой машинерии и никакой веры в то, что с тех
пор ничего не трогали.

**Ключи когорт:** `class`, `attackType`, `school`, `creatureType`, `idPrefix`. Когорта
разворачивается в конкретные ассеты в момент применения, и обратный пресет содержит поимённый
список: состав когорты со временем меняется, а откат обязан вернуть ровно то, что тронули.

## Порядок

1. Правки утверждены Максом → собрать пресет с говорящим `title` (номер записи реестра внутри).
2. Применить, проверить `Change`-строки: `SKIP` значит, что поле не нашлось — молча это не проходит.
3. Объявить новый прогон (`balance-run.py start`), прогнать полный круг.
4. Занести сдвиг в реестр: что стало с целевой проблемой и не появилось ли новых.
