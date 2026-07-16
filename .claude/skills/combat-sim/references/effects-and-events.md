# Эффекты и события

Читать перед созданием эффекта. Здесь — контракт «как эффект устроен» и внутренняя шина
producer→consumer.

## Разрез: определение (Data) vs поведение (Combat)

- **Data (скилл data-authoring):** `EffectData` SO — `id` (`domain.name`), теги,
  `StackRule`/`MaxStacks`, баланс-цифры, loc, и состав компонентов через
  `[SerializeReference] IEffectComponent[]`. `IEffectComponent` — сериализационный
  якорь-маркер в Data.
- **Combat (этот скилл):** ПОВЕДЕНИЕ. Рантайм-хуки живут в контрактах
  `IRuntimeEffectComponent : IEffectComponent` — кросс-сборочный шов, где логика оперирует
  боевым состоянием (`EffectContext`).

## Экземпляр эффекта — `RuntimeEffect` (всё изменяемое здесь)

Компоненты **stateless** — экземпляр шарится между всеми носителями. Per-unit состояние
живёт в `RuntimeEffect`: `RemainingTicks` (`< 0` = перманентный), `Stacks`,
`ScaledPotency[]` (снимок потенции из статов источника при наложении; для DoT/HoT —
per-second rate, НЕ запечённый total), `PeriodicTicks[]`, `ChargeReadyTicks[]`.
**Никогда не держи изменяемое поле в компоненте** — это общий mutable-стейт и мгновенный
кросс-юнит-баг.

## Контракты компонентов (`IRuntimeEffectComponent.cs`)

- `IRuntimeEffectComponent` — база: `OnApply(in EffectContext)`, `OnExpire(in EffectContext)`.
  Дефолт рестака = `OnExpire→OnApply`, поэтому keyed-снятие обязано быть **идемпотентным**.
- `IPeriodicComponent` — `Interval` (сек) + `OnTick` (DoT/HoT/реген).
- `IReactiveComponent` — `Events` (маска `CombatEvent`) + `OnEvent(ctx, in CombatEventData)`.
  Реагирует пост-факт (после того как событие произошло).
- `IPreDamageComponent` — `OnPreDamage(in DamageRequest, PreDamageResult, in EffectContext)`:
  синхронный перехват ДО вычета HP. Успевает поднять щит («Оплот») или полностью отменить
  удар (`PreDamageResult.Negated`, «Изворотливость»). Опрос — по индексу `ActiveEffects`
  (детерминизм).
- `IStackableComponent` — `OnStacksChanged(previousStacks, in EffectContext)`: для
  компонентов с накопленным ВНЕШНИМ состоянием (пул щита, заряды), которые правят вклад
  дельтой. Компонентам с keyed-снятием (напр. `StatModifierComponent`) он НЕ нужен —
  хватает дефолта.
- `IScalablePotency` — объявляет `ScalableValue Potency`; `EffectSystem` резолвит её из
  статов источника ОДИН раз при наложении и кладёт снимок в `RuntimeEffect.ScaledPotency`.

**Рецепт нового эффекта-поведения:** stateless-класс в
`Combat/Effects/Components/`, реализующий нужный контракт; состояние — только в
`RuntimeEffect`/`EffectContext`; теги и `StackRule` заданы в `EffectData` (это уже
data-authoring). Пример-эталоны рядом: `ShieldComponent`, `PeriodicDamageComponent`,
`ThornsComponent`, `LifestealComponent`, `ControlComponent`.

## Жизненный цикл — `EffectSystem`

`Apply` (длительность через эфф-эффективности, снимок потенции, стакинг по `StackRule`,
маска тегов) → `Tick` (периодика → countdown → `Expire`) → `Dispel`/`RemoveByTag`.
`RunPreDamage` гоняет `IPreDamageComponent` перед `DamagePipeline.Execute`. Истечение →
`Expire` → teardown компонентов + поднимает `EffectExpired` источнику.

## Внутренняя шина: `CombatEvent` (producer→consumer)

`[Flags]`-enum для РЕАКТИВНЫХ компонентов (НЕ outward-события презентации, НЕ команды сим):
`DamageDealt`, `DamageTaken`, `Healed`, `UnitDied`, `EffectApplied`, `UnitKilled`
(доставляется убийце = `Source`), `EffectExpired` (доставляется источнику эффекта).

`CombatEventData` несёт `Type/Source/Target/Amount/Tags/SourceKind`. Гейты, которые надо
держать в голове:
- `SourceKind` (`IsAutoAttack`, `IsDirectHit`) — реактивы «на удар» фильтруют по нему,
  иначе тики DoT и их же ответка порождают новые срабатывания (бесконечный пинг-понг).
- `Tags` для `EffectExpired` — теги истёкшего эффекта; так «Вихревой заход» монаха ловит
  конец отбрасывания.

Диспатч — FIFO-очередь в `CombatSimulation.DrainEventQueue` (Stage 6), кап
`MaxEventsPerDrain = 512` (защита от пинг-понга реактивов).

## Смещение — это эффект

Knockback/dash/«ядро» оформлены как эффект с тегом `EffectTag.KnockUp`: у него НЕ скейлится
только длительность (Neutral), всё остальное идёт через систему эффектов, а конец полёта
поднимает единый `EffectExpired`. Механику полёта исполняет `Systems/DisplacementSystem.cs`
(двигает `Position` фикс-шагом N тиков, `DisplacedTicksRemaining` держит жёсткое оглушение
полёта). Локальное разведение тел — `Systems/SeparationSystem.cs` (детерминировано: пары по
`Id`, без RNG; летящие юниты исключены; тюнеры `gm_sep_*` из `SimTuning`).

## Зон/аур в коде пока НЕТ (но в GDD приняты)

Персистентных наземных зон (`ZoneSystem`) в коде нет. AoE сейчас — это разовые запросы
`QueryUnitsInRadius/InLine` в момент детонации + `ReportAreaHit` для dev-оверлея. Не выдумывай
«зону» на пустом месте — если нужен длящийся эффект по площади сегодня, это эффект на
пойманных запросом юнитах.

**Важно:** ground zones (persistent area effects) ПРИНЯТЫ в GDD как направление
(коммит `cb1d9b97`). Когда дойдёт до реализации — это будет новая sim-система (встроить в
фиксированный порядок `Tick`, детерминированно, тюнеры в `SimTuning`), а не костыль поверх
эффектов. До тех пор шва под них в коде нет.
