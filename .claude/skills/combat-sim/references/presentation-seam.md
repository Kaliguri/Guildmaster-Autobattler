# Развязка sim → presentation

Читать, когда задача трогает мост между боем и его отображением/фидбэком. Скилл держит
КОНТРАКТ развязки; сам визуальный полиш (частицы, шейк-кривые, spritework) — за Максом.

## Направление зависимости — одностороннее

`Presentation` ссылается на `Combat` и СМОТРИТ на него; `Combat` на `Presentation` не
смотрит НИКОГДА. Проверяй это при каждой правке: если для боевой фичи «понадобилось»
дёрнуть View из sim — фича спроектирована не туда.

## Двухступенчатый мост

1. **sim поднимает C#-события** на `CombatSimulation` (`OnUnitSpawned/Died`,
   `OnDamageDealt`, `OnHealed`, `OnAttackEvaded/Started/Interrupted`, `OnProjectileSpawned`,
   `OnBattleEnded/Reset`). Это внутрибоевой шов, ещё без движка.
2. **`Presentation/CombatPresenter.cs`** — единственный подписчик со стороны презентации:
   держит `Dictionary<int, UnitView>` (Id→View), спавнит/деспавнит views, интерполирует
   `PreviousPosition→Position` (60 fps рендер поверх 30 Гц сима), делает точечный per-hit
   фидбэк И **ретранслирует в MessagePipe** (`Presentation/Events/CombatEvents.cs`:
   `UnitSpawnedEvent`, `UnitDiedEvent`, `DamageDealtEvent`, `BattleEndedEvent`).

Audio/UI/gamefeel слушают **MessagePipe**, а не sim напрямую — так они не зависят от боевого
ядра. Новый потребитель боевых событий (звук, HUD, аналитика) подписывается на MessagePipe,
не на `CombatSimulation`.

## Global-feel и per-hit фидбэк — контур `gamefeel-vfx`, не combat-sim

Политику значимости (`CombatFeelDirector`: kill-slowmo, shake, финишер) и точечный per-hit
фидбэк (hitstop, искры, цифры) держит скилл **`gamefeel-vfx`**. combat-sim здесь отвечает лишь
за то, что этот слой ПОТРЕБЛЯЕТ: боевое время (`TimeScaleService`, ниже) и шов sim→MessagePipe.
Если задача про то, КАК трясём/замедляем/спавним VFX — это `gamefeel-vfx`; если про то, кто
пишет `Time.timeScale` и как это не ломает детерминизм — combat-sim.

## `Time.timeScale` — пишет только `TimeScaleService`

`Game/Services/TimeScaleService.cs` — единственный писатель `Time.timeScale`. Компонует
`GameSpeed × Cinematic`, `Paused → 0`, всё на unscaled-времени, толкает FMOD-параметр питча.
**Детерминизм сима не трогает:** меняется лишь реальное время на тик, `ElapsedSeconds`
считается от `currentTick`. Никто другой в `Time.timeScale` не пишет.

## Config-SO фидбэка (значения — не хардкод)

Feel-параметры (hitstop, тайминги финишера, slowmo-факторы) — в
`Presentation/Design/CombatFeelConfig.cs`; палитра — `CombatColorPalette.cs`; бёрсты —
`PixelBurstPreset.cs`. Крутить фидбэк = править SO, не числа в коде.

## Граница «моё vs Макса»

- **Скилл (я):** контракт событий, кто на что подписан, где живёт global-feel и `timeScale`,
  проводка новых боевых событий в MessagePipe, что sim не зависит от презентации.
- **Макс:** визуальная приёмка и полиш — как именно выглядит бёрст, кривая шейка, спрайты,
  тайминг «на глаз». Я меняю визуал-параметры только по его ТЗ и показываю результат.
