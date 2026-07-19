**Аудит:** sim→presentation · data-контракты · persist battle flow · статы/урон  
**Модель:** Cursor Grok 4.5  
**Дата:** 2026-07-19  
**Пара к:** [[cursor-grok-4.5|combat-ядро]] (тот же день)  
**Scope:** Presentation-мост (не UITK), `Data/**`, `Game/Flow/**` + `EncounterLoader`, `Stats`/`DamagePipeline`/`Heal`/`Shield`  
**Вне scope:** UITK-экраны, навигатор, TestZone как UX

Все пункты — **`proposed`** (Cursor Grok 4.5).

---

## Вердикт

Четыре слоя в целом **здоровые и согласованные**: Combat headless, презентация читает, persist-мир через `IBattleSession`, статы/пайплайн урона чистые. Ломается не архитектура, а **швы и контракты**: interpolation alpha, cancel без reset, heal+damage XOR, щиты без HealShield*, negative armor в доке ≠ в коде.

| Зона | Зрелость | Главный риск |
|---|---|---|
| 1. sim→presentation | 7.5/10 | alpha не из аккумулятора |
| 2. data-контракты | 7/10 | Ability heal/damage mode-dependent |
| 3. persist flow | 7/10 | cancel → грязная арена |
| 6. stats/damage | 8/10 | док/код по shred и щитам |

---

# 1. Sim → presentation

## Как есть

```
CombatSimulation (C# events)
  → CombatPresenter (views, numbers, local hitstop)
      → MessagePipe (частично: spawn/die/damage/battle end)
          → CombatFeelDirector → TimeScaleService / shake
  → AudioPresenter (часто напрямую с C#)
CombatLoopService → Tick(TickDelta); accumulator НЕ отдаётся презентеру
```

Combat asmdef чист. `UnitView` не пишет в сим. Это правильно.

## Хорошо

- Односторонний поток; `animator.fireEvents = false`
- Space-пауза парит sim + `TimeScaleService`
- Feel: локальный hitstop в presenter, cinematic kill — в director
- `OnBattleReset` до чистки состояния

## Findings

### P1

**P-1. Interpolation alpha = `deltaTime / TickDelta`, не доля тика**  
`CombatPresenter.Update`: alpha ~0.5@60fps, ~1@30fps — зависит от FPS, не от `_accumulator / TickDelta` в `CombatLoopService`. То же улетает в `ProjectileView`.  
**Предложение:** опубликовать `InterpolationAlpha` из loop (или callback); presenter не выдумывает alpha.

**P-2. Presentational API на `ICombatContext`**  
`NotifyAttackStarted` / `NotifyAttackInterrupted` / `ReportAreaHit` — fire-and-forget, сим не портят, но мутационный шов раздут «под View».  
**Предложение:** `ICombatPresentationSink` / только C#-events на sim; context = мутации.

### P2

| ID | Находка | Предложение |
|---|---|---|
| P-3 | Две шины наружу: полный C# + урезанный MessagePipe (нет Heal/Evade/Attack/Projectile/Reset) | Либо дописать MP, либо явно: «C# = presentation truth; MP = Game subset» |
| P-4 | Dual pause: Space парит оба; Deployment/dev/`PauseCommand` часто только sim | Фасад `PauseBattle()` = sim + TimeScale |
| P-5 | `CombatStatusOverlay` рисует `u.Position`, спрайт — `_renderPosition` | Одна «render pose» |
| P-6 | `BattleInputController.Start` форсит `InputContext.Combat` на весь persist-скоуп | Контекст = Combat только в Fighting (как Phase) |

### P3

Hardcoded `"evade"` / имена; `OnAbilityCast` на AbilitySystem, не на CombatSimulation; `OnAreaHit` в стороне от presenter.

---

# 2. Data / content contracts

## Хорошо

- `ContentDefinition` + `domain.name`, flat `ContentDatabase`, Doctor + EditMode на id/dupes/null SerializeReference
- `UnitData` → Relic/Enemy; эффекты через `[SerializeReference]` marker в Data → runtime в Combat
- `ArmorConstantK` из `StatsConfig` в DI — старый dual-authoring C3 **закрыт**

## Findings

### P0

**D-1. Ability heal+damage: XOR vs оба — зависит от TargetMode**  
`ApplyToTarget`: `IsHeal` → **только heal**, damage drop.  
`ApplyAllWithTag`: damage **и** heal (если AreaRadius).  
Druid `spore_burst` живёт за счёт mass-path. Те же поля на Self/Nearest → урон молча пропадает.  
**Предложение:** валидатор «heal+damage только для AllEnemiesWithTag» **или** унифицировать ApplyToTarget.

### P1

| ID | Находка | Предложение |
|---|---|---|
| D-2 | Нет semantic OnValidate: polarity↔Buff/Debuff tag, ResourceType vs cost/onHit, dual heal+damage | Расширить `ContentValidationService` + EditMode |
| D-3 | Тройная модель баффа: `EffectPolarity` + `EffectTag.Buff/Debuff` + `DispelTargetPolarity` | CI cross-check polarity↔tag |
| D-4 | `AbilityData` вне реестра: bare ids (`steel_whirl`), нет `ability.*` / loc discipline | Хотя бы `ability.*` + uniq, даже nested |

### P2

| ID | Находка | Предложение |
|---|---|---|
| D-5 | `TargetingMode` (AI) vs `AbilityTargetMode` (cast) — split ок; XML TargetingMode врёт про AbilityData | Починить док |
| D-6 | `ScalableValue` (эффекты) vs сырые поля Ability | Решить: ability → ScalableValue или оставить два языка осознанно |
| D-7 | Legacy `RelicData._tags` + canonical `InfoTags` | Выпилить string[] когда UI на InfoTags |
| D-8 | Changelog/wiki всё ещё помечают dual armorK / ability domain — дрейф | Синхрон tech-scribe |

### P3

`VesselData` скелет; inline legacy `AIProfile` HideInInspector; Ability CD float vs AA ticks.

---

# 3. Persist battle / run flow

## Как есть (happy path)

```
Boot: World + Battle loaded once
RunPartyReady → WorldStageController → RosterDeployer.PlaceParty (paused)
BattleFlow.RequestLaunch → BattleBootstrap.LaunchBattle
  → SpawnEnemies + pause + RequestDeployment
  → Deployment → StartCombat (Fighting)
BattleEnded → ReportOutcome → (retries) → RequestReset → DeployParty + Phase.None
```

Шов `IBattleSession` + единый `RosterDeployer` — сильная сторона.

## Findings

### P0

**F-1. Cancel mid-battle без reset**  
`BattleFlow.Run`: `await WaitOutcomeAsync(ct)` → при cancel исключение; `RequestReset()` **после** await без `try/finally`. Арена остаётся Fighting (враги, фаза, input).  
**Предложение:** `try { … } finally { RequestReset(); }`.

**F-2. `LaunchBattle` может стакать врагов**  
Если party жив — только `SpawnEnemies`, без очистки team 1. Happy path ок (reset всегда); после F-1 / dirty launch — дубли врагов.  
**Предложение:** всегда `DeployParty()` или явный clear team 1 перед spawn.

### P1

| ID | Находка | Предложение |
|---|---|---|
| F-3 | `SavedPosition` не пишется после drag — reset возвращает стартовый строй | Write-back позиций в RunState на StartCombat / exit deploy |
| F-4 | Loadout в бою не трогает RunState (hub — трогает) — reset откатывает релики | Durable equip или явный «только превью» |
| F-5 | `ResetToWorld` не сбрасывает `InputContext` (Combat остаётся) | Сброс в None / Deployment по фазе |
| F-6 | Restart token тратится **до** успешного `RequestRestart` | Spend после true / rollback при false |

### P2

Split phase ownership (DeploymentController / Bootstrap / UnbindClock); Launch не ставит Phase сам (ждёт Free listener); Fixed `DeploymentMode` игнорируется на run-path; мало EditMode на Bootstrap transitions; мёртвый `ISceneLoader` в BattleFlow.

### P3

Legacy `TryConsumePending`; двойной pause/flush на launch; XML «скоуп = один бой» при persist на сессию.

---

# 6. Stats / damage pipeline

## Pipeline (код)

```
Factory: kit Override → vessel → items → passives → HP=Max
DealDamage → PreDamage → Pipeline:
  raw × DealtEff
  !True → armor by school, pen% then flat, Max(0,effArmor), × K/(K+eff)
  × Affinity(creature)
  × TakenEff → shield → HP
  Lifesteal(stat): Heal(HpDamage × Lifesteal)
Heal: × DealtEff × TakenEff → clamp MaxHP
```

## Хорошо

Dirty cache, Override authoring, AffinityTable + тесты, ArmorK из StatsConfig, Heal efficiencies, чистый `DamagePipeline`.

## Findings

### P0 (контракт док↔код / feature silent wrong)

**S-1. Negative armor / shred**  
Планинг/GDD: при `effArmor < 0` → `mult = 2 − K/(K − effArmor)`.  
Код: `Max(0, effArmor)` → shred в минус **no-op**. Тест `EffectiveArmor_ClampedAtZero` фиксирует код.  
**Предложение:** либо формула из плана, либо поправить GDD/planning и оставить clamp.

**S-2. Щиты игнорируют `HealShieldDealt/TakenEff`**  
`Heal` умножает; `ShieldComponent` / `MissingHpShield` кладут сырое. GDD говорит общий scaling.  
**Предложение:** те же eff на apply щита **или** явный док «щит только ScalableValue».

### P1

| ID | Находка | Предложение |
|---|---|---|
| S-3 | Два lifesteal: стат по **HpDamage**; компонент по **TotalDamage** | Убить компонент (§3.7); тест на стат-путь |
| S-4 | `AttackSpeedMin/Max` в StatsConfig не применяются в runtime | Clamp в `Stats.Get` или `AttackTiming` |
| S-5 | Таблицы клампов в planning не в `RebuildCache` | Применить или убрать из доки |
| S-6 | GDD affinity (2026-07-15) vs код `AffinityTable` | Канон = код (`combat-model`); поправить GDD |

### P2

`RegenSystem` пишет HP в обход Heal (нет eff, нет `Healed`); `StatType` HealShield* ещё помечен Ф2; `DamagePipeline` NRE на null Source; allocs в RebuildCache; `StartResource` не clamp к Max.

### P3

XML «lifesteal Фаза 2»; нет теста Override last-wins; Magic→Elemental в старых доках.

---

## Сводный приоритет (`proposed`)

| # | ID | Зона | Что |
|---|---|---|---|
| 1 | F-1 | persist | `try/finally RequestReset` |
| 2 | F-2 | persist | clear enemies before spawn |
| 3 | P-1 | presentation | alpha из accumulator |
| 4 | D-1 | data | Ability heal+damage contract |
| 5 | S-1 / S-2 | stats | shred + shield eff — решить док или код |
| 6 | F-3…F-6 | persist | durable positions/equip, input, restart spend |
| 7 | P-3 / P-4 | presentation | bus policy + pause façade |
| 8 | D-2… | data | semantic validation |

---

## Подпись

**Автор:** Cursor Grok 4.5  
**Тип:** review слоёв 1 / 2 / 3 / 6 (не UI)  
**Статус пунктов:** все `proposed`  
**Сверка:** ключевые P0/P1 прочитаны по коду (`CombatPresenter.Update`, `BattleFlow.Run`, `LaunchBattle`, `ApplyToTarget`, `DamagePipeline`, `ShieldComponent`, `BattleInputController`)
