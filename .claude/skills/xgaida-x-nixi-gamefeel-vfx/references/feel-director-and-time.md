# Политика значимости и боевое время

Читать перед правкой global-feel (slowmo, screen shake, финишер). Скилл держит политику
«что достойно момента» и КОНТРАКТ с боевым временем; сам визуальный полиш (глубина slowmo,
кривая шейка на глаз) — за Максом.

## `CombatFeelDirector` — единственное место global-feel

`Game/Services/CombatFeelDirector.cs` — одна политика значимости поверх боевых событий.
Подписан на MessagePipe (`DamageDealtEvent`, `BattleEndedEvent`) — развязка от симуляции,
тот же приём, что у `AudioPresenter`. Плюс на `CombatSimulation.OnBattleReset` (C#-событие)
для сброса. Решает три момента:

- **Добивающий удар** (`e.Result.KilledTarget`) → `CinematicPulse` (kill-slowmo) не чаще
  `KillSlowCooldown` (на толпе киллов — на unscaled-времени) + `Shake(KillShake)`.
- **Тяжёлый не-добивающий удар** → только тряска, по доле `TotalDamage / MaxHP` цели выше
  `HeavyHitFrac`, интенсивность лерпом `HeavyShakeMin..Max`.
- **Конец боя** (`BattleEndedEvent`) → финишер-таймлайн из `CinematicSegment[]` +
  `Shake(BattleEndShake)`.

«Момента» криты в модели нет — значимость = `KilledTarget` и доля урона. Всё остальное
(царапина, тик DoT) global-feel НЕ трогает.

## Стык с боевым временем — я потребитель, не владелец

`TimeScaleService` принадлежит `combat-sim` (единственный писатель `Time.timeScale`,
композит `GameSpeed × Cinematic`, пауза, шов под геймплейный хрономант). Я его ДЁРГАЮ через
Cinematic-API и не пишу в `Time.timeScale` сам:

- `CinematicPulse(factor, hold, release, curve)` — мгновенно уйти в slowmo, держать, вернуться
  по кривой. Момент килла.
- `PlayCinematicSequence(CinematicSegment[])` — многоступенчатая концовка (пауза → slowmo
  смерти → сильное slowmo разлёта → возврат).

`CinematicSegment` (тип на шве, живёт в `TimeScaleService.cs`, им владеет combat-sim) я
конструирую из `CombatFeelConfig`:

```
new CinematicSegment(0f,                         cfg.FinisherPause)                       // 1: полный стоп
new CinematicSegment(cfg.FinisherDeathFactor,    cfg.FinisherDeathDuration)               // 2: death slowmo
new CinematicSegment(cfg.FinisherShatterFactor,  cfg.FinisherShatterDuration)             // 3: shatter slowmo
new CinematicSegment(1f, cfg.FinisherReturn, ramp: true, curve: cfg.FinisherReturnCurve)  // 4: возврат
```

Всё на unscaled-времени — иначе slowmo тормозил бы собственный отпуск. Детерминизм sim не
трогается: меняется лишь сколько реального времени приходится на тик (`ElapsedSeconds`
считается от `currentTick`, это забота combat-sim).

## `IScreenShake` — тряска за интерфейсом

`Presentation/Camera/IScreenShake.cs`: `Shake(intensity 0..1)` (удары складываются вверх —
берётся максимум), `ResetShake()`. За интерфейсом, чтобы бой не зависел от конкретного рига. **Заглушки `NullScreenShake` НЕТ** — она удалена
2026-07-26 в заходе по фолбэкам: тихий no-op делал отсутствие тряски неотличимым от рабочей тряски.
Реализацию регистрирует `WorldLifetimeScope` (`.AsSelf().As<IScreenShake>()` на камера-риге); нет рига —
это громкий отказ разводки, а не «молча без тряски». Форма тряски (смещение как доля
`orthoSize`, крен, частота, затухание) — в `CombatFeelConfig` (`Shake*`), интенсивность по
событию — там же (`KillShake`/`HeavyShake*`/`BattleEndShake`). FeelDirector передаёт только
интенсивность; форму знает `ScreenShake`.

## Сброс по `OnBattleReset` (dev-R) — обязателен

Перезапуск боя на месте должен снять застрявший global-feel, иначе новый бой идёт в
замедлении и первый килл не «щёлкает»:

```
_time.Reset();            // снять slowmo/пульс/секвенцию, timeScale → GameSpeed×1
_shake.ResetShake();      // убрать остаточную тряску
_lastKillSlowmo = float.NegativeInfinity;  // сбросить кулдаун kill-slowmo
```

`TimeScaleService.Reset()` НЕ трогает `GameSpeed`/паузу игрока (его выбор переживает рестарт) —
это забота combat-sim, я лишь вызываю `Reset()`. Презентер сбрасывает свои виды отдельно (он в
другой сборке и до `TimeScaleService`/шейка не дотянется без цикла asmdef).

## Антипаттерны

- **Global-feel из `UnitView`/точечного фидбэка.** Тряска/slowmo/время — только через
  FeelDirector. Вьюха знает лишь свой локальный hitstop/вспышку.
- **Прямая запись `Time.timeScale`.** Только `TimeScaleService` (combat-sim). Из джуса — через
  Cinematic-API.
- **Slowmo/финишер на scaled-времени.** Замедление, считающее себя на scaled dt, застрянет.
  Всё режиссёрское время — unscaled (это уже реализовано в `TimeScaleService`).
- **Хардкод факторов/таймингов.** Всё из `CombatFeelConfig`. Новый feel-параметр — новое поле
  в конфиге, не число в коде.
