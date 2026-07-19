**Аудит:** боевая симуляция / эффекты / combat-логика  
**Модель:** Cursor Grok 4.5  
**Дата:** 2026-07-19  
**Scope:** `Assets/_Project/Scripts/Combat/**`, `Core/Simulation/SimConstants.cs`, драйвер `CombatLoopService` (только как граница `Time.deltaTime`)  
**Вне scope:** UI, UITK, Deployment/TestZone, Presentation-визуал, MessagePipe-экраны

Все пункты — **`proposed`** (Cursor Grok 4.5), не канон проекта.

---

## Вердикт

Ядро **здоровое**: фиксированный тик 30 Гц, headless Combat, FIFO реакты, integer attack timing, порядок систем осмысленный и задокументирован в XML. Переписывать `CombatSimulation` не нужно.

Главные проблемы сейчас — **граничные контракты эффектов/урона**, а не архитектура: убийство до `DeathSystem`, гейт «прямой удар», null-source в пайплайне, лаг контроля на том же тике что Apply. Это точечные фиксы + тесты.

**Зрелость combat-ядра:** ~8/10 архитектура, ~6.5/10 граничные контракты эффектов.

---

## Тик (как в коде)

`CombatSimulation.Tick` (~230–265):

1. early-out если `Outcome != Ongoing`
2. `FlushPendingSpawns`
3. snapshot паузы → `ApplyDueCommands`
4. если пауза «стояла до команд» → опц. `currentTick++` при непустой command-queue → return
5. **Brain → Ability → Movement → Displacement → Separation → SpatialHash.Rebuild → AutoAttack → Projectiles → Regen → Effects → DrainEventQueue → Death → CheckOutcome**
6. `currentTick++`

Пауза, применённая этим тиком, действует со **следующего** — текущий досимулировывается. Это правильно и совпадает с докой.

---

## Что сделано правильно (не ломать)

| Область | Почему |
|---|---|
| Headless `Guildmaster.Combat` | Нет `Time.deltaTime` / Presentation / VContainer внутри ядра |
| Порядок систем | Brain→move→spatial→AA→proj→regen→effects→events→death — осмысленный |
| Regen перед Effects + гард `CurrentHP > 0` | Не воскрешает из летального DoT задним числом |
| FIFO event queue + cap | Нет рекурсии шипов; truncate детерминирован |
| `DamageSourceKind` / `IsDirectHit` | Правильный шов для «только удар» vs Periodic/Reactive |
| `ArmorThornsComponent` гейтит `IsDirectHit` | Эталон того, как должны вести себя «ударные» реактивы |
| Potency snapshot + `IStackableComponent` | Закрывает старые B1–B3 restack-баги |
| Integer attack timing (`AwayFromZero`) | Детерминизм АА |
| AI stagger `Id % interval` | Стабильный 10 Гц без thrash |
| Displacement = эффект (KnockUp) + `DisplacedTicksRemaining` | Единый `EffectExpired` для комбо монаха |
| Тесты на щит/додж/displace/armor-thorns | Уже ловят регрессии жёстких мест |

---

## Findings

### P0 — баг поведения контента

#### C-1. `IgnitionComponent`: награда за убийство никогда не срабатывает

`OnApply` после `DealDamage` проверяет `target.IsDead`.  
`DamagePipeline` только роняет `CurrentHP`; `IsDead` ставит **`DeathSystem` в конце тика**.

```csharp
ctx.Combat.DealDamage(...);
// ...
if (target.IsDead) RewardKill(caster, ctx); // всегда false в этом тике
```

При этом `DealDamage` уже умеет `result.KilledTarget` (`CurrentHP <= 0`) и даже кладёт `UnitKilled` в очередь — Ignition этим не пользуется.

**Предложение:** брать `DamageResult` из DealDamage (если API отдаёт) или `target.CurrentHP <= 0f`. Тест: детонация с HP цели ≤ remaining burn → баф/хил кастеру.

---

### P1 — корректность контрактов

#### C-2. `RecomputeControl` не зовётся в `Apply`

`Apply` добавляет эффект + `OnApply`, но `RecomputeControl` — только в конце `EffectSystem.Tick`, плюс `Remove`/`Dispel`.  
Стан/рут из Ability (до Movement/AutoAttack) оставляет `CanAct`/`CanMove` **true до конца тика**.  
Displacement обходит это через мгновенный `DisplacedTicksRemaining`; обычный Control — нет. У AA в комментариях есть «1-tick CanAct lag» (Effects после AA) — Ability→Movement ещё хуже и недокументирован.

**Предложение:** `RecomputeControl(target)` в конце `Apply` (и после успешного stacking-path, если там может появиться Control).

#### C-3. Зомби на том же тике: `HP ≤ 0`, но `!IsDead`

Death последний. AA/Ability/Movement гейтят только `IsDead`. Юнит A обнуляет HP B в середине списка → B ещё кастует/бьёт в этом тике. Regen уже умнее (`CurrentHP <= 0`).

**Предложение (`proposed`):** ранний out `CurrentHP <= 0` в AA/Ability (и опц. Movement), **или** осознанно оставить «последний выдох» и зафиксировать в доке. Сейчас — молчаливое поведение без контракта.

#### C-4. Классический `ThornsComponent` не гейтит прямой удар

`ArmorThornsComponent` — `if (!e.IsDirectHit) return`.  
`ThornsComponent.OnEvent` отражает **любой** `DamageTaken`, включая `Periodic`. Отражение идёт как `Reactive` (петли thorns↔thorns нет), но DoT→thorns расходится с моделью «шипы на удар».

**Предложение:** тот же гард `IsDirectHit`, что у ArmorThorns. Обновить тест/док.

#### C-5. `BulwarkComponent` тоже без гейта прямого удара

`RunPreDamage` вызывается на каждый `DealDamage`. При `PassiveTrigger.AnyHit` тик DoT может поднять Оплот (с ICD). Dodge уже гейтит авто-атакой.

**Предложение:** в `OnPreDamage` — `if (!incoming.IsDirectHit) return` (или явно только AutoAttack, если так по ГДД).

#### C-6. `DamagePipeline` требует ненулевой `Source`

```csharp
damage *= req.Source.Stats.Get(StatType.DamageDealtEff);
```

`PeriodicDamageComponent` передаёт `ctx.Source` — может быть null (эффект без источника / истекший кастер). → NRE.

**Предложение:** `Source == null` → dealtEff/pen = нейтральные дефолты (1 / 0). То же для affinity-path.

#### C-7. Общий пул `CurrentShield` между эффектами

Несколько щитов пишут в один float. Expire щита A делает `Max(0, shield - amountA)` и может съесть «чужой» остаток щита B. Restack одного эффекта починен (`IStackableComponent`); multi-source — нет.

**Предложение:** per-effect вклад в `RuntimeEffect` + сумма в `CurrentShield`, либо документированный «один щит на юнита» до рефактора. Триггер: два одновременных щит-эффекта в контенте.

---

### P2 — гигиена / перф / долг

| ID | Находка | Предложение |
|---|---|---|
| C-8 | `AbilitySystem.NearestEnemyTo`, `MarkTransfer`, `WhirlDashLanding` — `new List<>` на каст/событие | Переиспользовать буфер системы / SpatialHash |
| C-9 | `Stats.RebuildCache` — несколько `new float[StatCount]` | Инстанс-буферы (уже в tech-changelog §3.2) |
| C-10 | Free-fly снаряды: O(projectiles × units) без хэша | Ок на фестиваль; потом broad-phase |
| C-11 | Ability CD в float-секундах, АА в int-тиках | Два часов; при желании CD тоже в тиках |
| C-12 | `LifestealComponent` + стат `Lifesteal` = двойной путь | Уже в долге §3.7; удалить компонент при первом реальном вампирик-контенте |
| C-13 | `CombatEvent.EffectApplied` нигде не enqueue | Либо начать слать, либо выкинуть из enum |
| C-14 | `ComputeChecksum` тонкий (tick/RNG/id/pos/HP/phase) | Ок для coarse probe; для MP — шире (shield, effects, projectiles) |
| C-15 | Dual truth displacement: int + KnockUp effect | Хрупко, но задокументировано; не трогать без нужды |
| C-16 | `StackRule.None` на reapply — silent no-op | Ок; предупреждение в валидации контента |
| C-17 | Lifesteal на **весь** урон в `DealDamage` | Уже §3.6; сузить по `SourceKind` когда появятся теги |

---

### P3 — будущее

- Brain не выбирает способность — AbilitySystem берёт «первую готовую».
- `DisplaceKind.Pull` / `Teleport` — stubs.
- Cap очереди 512: при hit — deterministic truncate + log; на текущем контенте недостижимо.
- Полный AI spell selection / threat — пост-festival.

---

## Приоритет внедрения (`proposed`)

1. **C-1** Ignition kill — одна проверка, ломает карточку Огненного мечника  
2. **C-4 + C-5** гейт `IsDirectHit` на Thorns/Bulwark — выравнивание с ArmorThorns  
3. **C-6** null-safe Source в DamagePipeline  
4. **C-2** RecomputeControl on Apply  
5. **C-3** контракт зомби-тика (док или early-out)  
6. **C-7…** по мере контента / перфа

---

## Подпись

**Автор:** Cursor Grok 4.5  
**Тип:** независимый code-review combat-ядра (эффекты, урон, тик, системы)  
**Статус пунктов:** все `proposed`  
**Сверка:** находки прочитаны по живому коду (`IgnitionComponent`, `ThornsComponent`, `BulwarkComponent`, `EffectSystem.Apply`, `DamagePipeline`, `CombatSimulation.Tick`); предыдущий UI/TestZone-отчёт снят как мимо scope
