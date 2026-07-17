# Симуляция и детерминизм

Читать перед правкой любой sim-системы. Всё здесь — про то, почему бой воспроизводим и
как не сломать это одной строкой.

## Тик: фиксированный шаг, фиксированный порядок

- Частота — `SimConstants.TickRate = 30` Гц; шаг — `TickDelta = 1/30` сек. Это
  ЕДИНСТВЕННЫЙ «dt» детерминированного шага. Никаких `Time.deltaTime` внутри `Combat`.
- `CombatSimulation.Tick(float dt)` вызывается всегда с `TickDelta` и прогоняет системы в
  ЖЁСТКОМ порядке:

  `ApplyCommands → Brain → Ability → Movement → Displacement → Separation →
  SpatialHashRebuild → AutoAttack → Projectiles → Regen → Effects → DrainEvents →
  Death → CheckOutcome → currentTick++`

  Порядок — часть контракта детерминизма (напр. Separation после Displacement, разбор
  событий `DrainEvents` перед `Death`). **Не переставляй и не вставляй систему в середину
  без явного решения** — это меняет исход боя и рвёт replay. Новую систему добавляй
  осознанно в правильную точку и прогоняй checksum/replay.
- AI считается реже сима: `AiTickRate = 10` Гц, `AiTickInterval = 3` тика — но это
  оптимизация внутри детерминированного шага, не отдельные часы.

## Реалтайм-драйвер (единственный мост к wall-clock)

`Game/Services/CombatLoopService.cs` — accumulator-паттерн: копит `Time.deltaTime`, гонит
фикс-шаги `Tick(TickDelta)`, кап догоняющих тиков `MaxCatchUpTicksPerFrame = 5`
(анти-«спираль смерти» — излишек долга отбрасывается). Простаивает при `Outcome != Ongoing`.
**Это единственное место во всём бою, которое знает про реальное время.** Sim-время —
`ElapsedSeconds = currentTick * TickDelta`; пауза/slowmo его не искажают.

## Что запрещено в `Combat` (источники недетерминизма)

- `UnityEngine.Random` и любой свой `System.Random` — RNG только `ICombatContext.Rng`
  (`IRngService`, детерминированный, сидированный на старте боя).
- `Time.*`, `DateTime.Now`, `Stopwatch` — время только через тики/`TickDelta`.
- Недетерминированный порядок: обход `Dictionary`/`HashSet` как источник порядка,
  нестабильная сортировка, `float`-хеши. Итерируй по индексируемым спискам
  (`RuntimeUnit.ActiveEffects` опрашивается по индексу — это ради детерминизма).
- Любая привязка к кадрам/платформе/потоку.

## Шов мутации: `ICombatContext`

Единственная точка входа для мутаций мира из систем и компонентов эффектов
(`Assets/_Project/Scripts/Combat/ICombatContext.cs`). Реализует `CombatSimulation`.

- Мутирующие: `DealDamage(in DamageRequest)`, `Heal(target, amount, source)`,
  `SpawnProjectile(in ProjectileSpawn)`, `ApplyEffect(target, EffectData, source)`,
  `Dispel(in DispelRequest)`, `Displace(in DisplaceRequest)`.
- Запросы (не мутируют): `QueryUnitsInRadius/InLine(..., TargetFilter, requestingTeam)`.
- Fire-and-forget в презентацию (НЕ мутируют, НЕ влияют на детерминизм):
  `ReportAreaHit`, `NotifyAttackStarted/Interrupted`.
- Только чтение: `Rng`, `CurrentTick`, `ArmorK`, `Tuning`.

**Правило:** способность/эффект трогает мир ТОЛЬКО через контекст. Прямая запись в
`RuntimeUnit` в обход — молчаливый рассинхрон и «эффект сработал, событие не поднялось».

## Значения — из данных, не хардкод

Структурные/детерминизм-константы (`TickRate` и производные, капы, `MinWindupTicks`,
`AttackReachTolerance`) живут в `SimConstants`. Балансные ручки (разделение тел, снаряды,
`KiteFleeFactor`, `GlobalSearchRadius`, …) переехали в `SimTuningConfig` → снапшот
`SimTuning`, запечённый на старте боя и доступный как `ICombatContext.Tuning`. Новую
балансную величину клади в `SimTuning`, не магическим числом в системе.

## Детерминизм на практике

`CombatSimulation.ComputeChecksum()` сворачивает состояние в хеш; `Net.SimSyncProbe`
сверяет его между клиентами (host-authoritative кооп; probe сейчас припаркован, но шов
живой). **Правишь sim-систему → прогони replay/checksum-проверку** (тот же вход → тот же
хеш через N тиков). Это ловит недетерминизм до того, как он всплывёт рассинхроном в коопе.
