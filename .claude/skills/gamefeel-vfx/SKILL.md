---
name: gamefeel-vfx
description: >-
  Рабочий контур джуса и визуального фидбэка (gamefeel / VFX) Guildmaster — слой
  «сочности» поверх боя: политика значимости (CombatFeelDirector — что достойно
  slowmo/shake), per-hit фидбэк презентера (hitstop, вспышка, сплющивание,
  боевые цифры), пиксельные VFX (CombatVfx/PixelBurst), death-shatter, screen
  shake за IScreenShake, LitMotion-твины, feel-конфиги (CombatFeelConfig,
  CombatColorPalette, PixelBurstPreset) и ЦЕЛЕВОЙ шов префаб-VFX (SO→префаб→пул→
  точка-сокет). Используй ВСЕГДА, когда задача касается фидбэка/джуса: gamefeel,
  juice, сочность, VFX, партиклы, VFX Graph, ShaderGraph-эффект, screen shake,
  тряска, hitstop, hit-stop, slowmo, замедление на добивание, финишер, вспышка,
  сплющивание/squash, damage numbers/боевые цифры, FloatingText, PixelBurst,
  DeathShatter, muzzle/искры/пыль, CombatFeelDirector, CombatFeelConfig,
  PixelBurstPreset, IScreenShake, точки-сокеты (ShotPoint/HitPoint/FeetPoint),
  пул VFX, спавн эффекта по боевому событию, или когда правишь что-либо под
  Assets/_Project/Scripts/Presentation (визуал-компоненты и Design/*Feel*/*Palette*)
  и Game/Services/CombatFeelDirector. Срабатывай, даже если слова «gamefeel» нет,
  но по сути правится визуальный/тактильный фидбэк боя. НЕ применять к: боевому
  времени и его контракту (TimeScaleService, GameSpeed/пауза/хрономант — это
  combat-sim; здесь только ПОТРЕБИТЕЛЬ через Cinematic-API), звуку (IAudioService,
  FMOD, стингеры, микс — это скилл audio), поведению эффектов и sim-логике
  (combat-sim), ОПРЕДЕЛЕНИЮ VfxData/пресетов как контента-SO (id, баланс — это
  data-authoring; здесь спавн-механика и пул), боевому uGUI-HUD (Image.Filled) и
  рантайм-UITK-экранам (uitk).
---

# Gamefeel & VFX — рабочий контур Guildmaster

Этот скилл — процедура, а не справка. Он превращает разрозненные правила «сочности»
в чеклист, который прогоняется на КАЖДОЙ фидбэк-задаче. Цель — чтобы джус и VFX
оставались развязанными от боевого ядра (смотрят на sim, не мутируют его),
data-driven (значения и формы — в SO, эффекты — в префабах), а новый фидбэк ложился
в готовые швы, а не рядом с ними.

**Роль на этом слое:** я держу КОНТРАКТ фидбэка (кто на что подписан, где живёт
global-feel, как эффект попадает на экран, какие швы). Визуальную приёмку и полиш —
как именно выглядит бёрст, кривая шейка, тайминг «на глаз», сам арт — делает Макс.
Я кручу визуал-параметры только по его ТЗ и показываю результат.

## Прежде всего: карта фидбэк-слоя

Слой уже построен и живёт в презентации. Ничего не изобретай — читай, продолжай,
встраивайся в существующие швы.

| Что | Где |
|---|---|
| Режиссёр значимости (global-feel: kill-slowmo, heavy-shake, финишер) | `Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs` |
| Мост sim→presentation + per-hit фидбэк (hitstop, цифры, спавн VFX) | `Assets/_Project/Scripts/Presentation/CombatPresenter.cs` |
| Тряска камеры за интерфейсом | `Assets/_Project/Scripts/Presentation/Camera/IScreenShake.cs`, `ScreenShake.cs`, `NullScreenShake.cs` |
| Пул + спавн пиксельных VFX-брызгов | `Assets/_Project/Scripts/Presentation/CombatVfx.cs` |
| Один брызг (кодовый меш, placeholder) | `Assets/_Project/Scripts/Presentation/PixelBurst.cs`, `PixelBurstMesh.cs` |
| Разлёт спрайта на осколки при смерти | `Assets/_Project/Scripts/Presentation/DeathShatter.cs`, `ShatterMesh.cs` |
| Всплывающие боевые цифры (урон/хил/evade) | `Assets/_Project/Scripts/Presentation/FloatingText.cs` |
| Вспышка арены/фон | `Assets/_Project/Scripts/Presentation/CombatAreaFlash.cs` |
| Вид юнита: вспышка/сплющивание/hitstop/death-хуки, точки-сокеты | `Assets/_Project/Scripts/Presentation/UnitView.cs`, `UnitAnimation.cs` |
| Единый feel-конфиг (все impact-параметры + pixel-burst-пресеты) | `Assets/_Project/Scripts/Presentation/Design/CombatFeelConfig.cs` |
| Палитра боевого UI | `Assets/_Project/Scripts/Presentation/Design/CombatColorPalette.cs` |
| Пресет одного пиксель-брызга (Serializable, живёт в feel-конфиге) | `Assets/_Project/Scripts/Presentation/Design/PixelBurstPreset.cs` |
| Боевое время (ПОТРЕБЛЯЕМ, не владеем) — slowmo/пауза/скорость | `Assets/_Project/Scripts/Game/Services/TimeScaleService.cs` → скилл `combat-sim` |

**Слой (asmdef):** всё это — `Guildmaster.Presentation` (визуал-компоненты) и
`Guildmaster.Game` (сервисы-режиссёры, сшивка через VContainer + MessagePipe).
`Presentation` ссылается на `Combat` и СМОТРИТ на него; `Combat` на презентацию не
смотрит НИКОГДА. `CombatFeelDirector` живёт в `Game`, потому что ему нужны и
`CombatSimulation` (события), и `TimeScaleService`/`IScreenShake` — их `Presentation`
не тянет (иначе цикл asmdef).

## Четыре правила, нарушение которых = переделка (HARD)

Каждое закрывает конкретный способ, которым фидбэк незаметно загнивает. Пойми
«почему» — тогда не придётся заучивать «нельзя».

1. **Все VFX — префабы. И точка.** Конечная форма любого боевого VFX (партиклы /
   VFX Graph / ShaderGraph-материал) — самодостаточный префаб, лежащий ссылкой в SO,
   который презентер спавнит в мировой точке-сокете через `ObjectPool` по боевому
   событию. Эталон уже в проекте: `UnitData.ViewPrefab` — «визуал/анимация/размер
   настроены ПРЯМО в нём, префаб самодостаточен» (`CombatPresenter.HandleUnitSpawned`).
   Так же спавнятся `_bulletPrefab`, `_floatingTextPrefab`.
   *Почему:* префаб — единственная форма, где художник собирает эффект без кода, а код
   про эффект знает ровно одно: «заспавнить в точке». Кодовый меш плодит визуал в C#,
   который нельзя приёмить глазами и нельзя отдать художнику.
   *Долг:* `PixelBurst`/`DeathShatter`/`CombatStatusOverlay` сейчас строятся кодом —
   это **placeholder**, подлежащий миграции в префаб, а НЕ образец для нового VFX.
   Новый эффект кодовым мешем не делаем даже «на время».
   *Целевой шов* (`VfxData` SO → префаб → пул-спавнер) — см. границу с data-authoring
   ниже; пока не построен, но проектируем под него.

2. **Global-feel только в `CombatFeelDirector`; per-hit — в презентере.** Глобальные
   эффекты значимости (kill-slowmo, heavy-shake, финишер-таймлайн, тряска) решает ОДНО
   место — `CombatFeelDirector`, по политике «что достойно момента». Точечный фидбэк
   пары «источник+цель» (hitstop, вспышка, сплющивание, искры, цифры) — в
   `CombatPresenter`. Не размазывай глобальный feel по `UnitView` и не дёргай глобальную
   тряску/время из точечного фидбэка.
   *Почему:* значимость — это политика («килл щёлкает, царапина нет»). Размажешь по
   вьюхам — потеряешь единую точку, где её крутят, и получишь slowmo на каждый тик DoT.

3. **Значения и формы — из feel-SO, не хардкод.** Тайминги, факторы, цвета, кривые —
   в `CombatFeelConfig` (+ `CombatColorPalette`, `PixelBurstPreset`). Потребители тянут
   значения ОТТУДА (`UnitView`, `CombatPresenter`, `ScreenShake`, `CombatFeelDirector`),
   а не из чисел в коде. Крутить фидбэк = править SO.
   *Почему:* джус настраивается итерациями «на глаз» — это работа Макса в инспекторе.
   Число в коде = правка кода на каждый чих баланса фидбэка и недоступно дизайнеру.

4. **Presentation читает sim только через события/MessagePipe и не влияет на
   детерминизм.** Фидбэк СМОТРИТ на бой: подписка на C#-события `CombatSimulation`
   (презентер) или на MessagePipe (`CombatFeelDirector`, как `AudioPresenter`). Никогда
   не пишет в `RuntimeUnit`/sim и ничего не решает за бой. Всё визуальное живёт на
   `Time.deltaTime`/твинах/unscaled, не на sim-тике.
   *Почему:* как только фидбэк мутирует мир или влияет на порядок тика — рушится и
   детерминизм (кооп-checksum), и headless-тестируемость боя. Это инвариант `combat-sim`,
   здесь он в силе как граница.

## Граница со смежными скиллами (режем чётко)

Фидбэк стоит на стыке трёх скиллов. На каждом стыке — взаимная ссылка «см. другой
скилл», а не спор за файл.

- **combat-sim** владеет **боевым временем**: `TimeScaleService` (единственный писатель
  `Time.timeScale`, композит `GameSpeed × Cinematic`, пауза, шов под геймплейный
  хрономант) и шов sim→MessagePipe. `CombatFeelDirector` (мой) — **потребитель**: дёргает
  `TimeScaleService.CinematicPulse`/`PlayCinematicSequence` и `IScreenShake.Shake`. Тип
  `CinematicSegment` живёт на шве в `TimeScaleService` (им владеет combat-sim), FeelDirector
  его конструирует. Инвариант «`timeScale` пишет только `TimeScaleService`» — за combat-sim.
- **audio** владеет звуком: `IAudioService`, FMOD, стингеры, микс. Пересечение —
  `TimeScaleService` толкает slowmo-питч через `IAudioService.SetGlobalParameter`
  (`AudioParameters.TimeScale`): вызов геймплейный (combat-sim), параметр — audio. Джус
  и звук на одно событие подписываются НЕЗАВИСИМО (оба слушают MessagePipe/sim), не через
  друг друга.
- **data-authoring** владеет **ОПРЕДЕЛЕНИЕМ** контента-SO. Целевой `VfxData` (id, ссылка
  на VFX-префаб, параметры, loc — если нужен) — как `EffectData`: определение за
  data-authoring. **Спавн-механика** (пул, точки-сокеты, презентер, привязка к событию) —
  моя. Ровно как эффект: определение — data-authoring, поведение/спавн — здесь.

## Целевой шов префаб-VFX (проектируем, пока не построен)

Сейчас пиксельные VFX — параметрические (`PixelBurstPreset` внутри `CombatFeelConfig`),
меш строится кодом. Это placeholder. `CombatFeelConfig` сам это признаёт: «VFX-секция
добавится, когда подключим партиклы (пока YAGNI)». Целевая форма — зеркало
`UnitData.ViewPrefab`:

1. `VfxData` SO (data-authoring): `id` (`domain.name`), ссылка на VFX-префаб, дефолт-точка
   спавна, время жизни/масштаб.
2. Спавнер-пул (здесь): `ObjectPool<T>` на префаб, `Get`→позиционировать в мировую
   точку-сокет→`Play`, авто-`Release` по завершению (как `CombatVfx`/`FloatingText` уже
   делают, но с префабом вместо кодового меша).
3. Презентер по боевому событию резолвит `VfxData` и просит спавнер проиграть в точке
   (`ShotPoint`/`HitPoint`/`FeetPoint` на `UnitView` уже есть).

Когда дойдём до реального VFX-контента — строим этот шов, а pixel-burst-путь мигрируем на
него. Детали и антипаттерны — `references/vfx-and-pooling.md`.

## Как я авторю фидбэк-код — ГИБРИД (файл + проверка через MCP)

1. **Пишу C#-файлы напрямую** (`Write`/`Edit`) — контролирую код и его слой (Presentation
   vs Game).
2. **Префабы и вложенные serialized-ref — только in-editor** (`execute_code` +
   `LoadPrefabContents`/`manage_prefabs`), НЕ правкой YAML руками (готча: nested-ref в
   префабе через hand-YAML ломается). Художественную начинку префаба (партикл-стек, шейдер,
   спрайты) кладёт Макс/художник — я собираю шов и вайринг.
3. **После C#-правок — `read_console`** (Unity MCP): дождаться компиляции, ноль ошибок.
4. **Визуальная приёмка — за Максом.** Не-визуально верю значениям/симу; «выглядит правильно»
   показываю Максу в play-mode, кривые/тайминги меняю по его ТЗ.

## Чеклист сдачи фидбэк-задачи

Прогнать перед тем, как сказать «готово»:

- [ ] Новый VFX — префаб (SO→префаб→пул→точка-сокет), не кодовый меш; кодовый путь не расширен
- [ ] Global-feel (slowmo/shake/финишер) — только в `CombatFeelDirector`; per-hit — в презентере
- [ ] Значения/формы — из `CombatFeelConfig`/палитры/пресета, не хардкод-числа в коде
- [ ] Presentation читает sim через события/MessagePipe; в `RuntimeUnit`/sim не пишет; детерминизм цел
- [ ] `timeScale` не трогаю напрямую — только через `TimeScaleService` (Cinematic-API); он за combat-sim
- [ ] VFX пулятся и корректно возвращаются (`Release`) при завершении и при `OnBattleReset`
- [ ] Стыки оформлены ссылкой (combat-sim / audio / data-authoring), спора за файл нет
- [ ] `read_console` чист (компиляция); визуальную приёмку показал Максу, параметры — по его ТЗ

## Справочные файлы (читать по надобности)

- `references/feel-director-and-time.md` — политика значимости, `CombatFeelDirector`
  (kill-slowmo/heavy-shake/финишер-таймлайн), стык с `TimeScaleService` (Cinematic-API,
  `CinematicSegment`), `IScreenShake`, сброс по `OnBattleReset`. Читать перед правкой
  global-feel.
- `references/vfx-and-pooling.md` — VFX-префаб шов (целевой), pixel-burst placeholder,
  `ObjectPool`, точки-сокеты, `DeathShatter`/`FloatingText`, `VfxData` (стык с
  data-authoring). Читать перед созданием/правкой VFX.
- `references/feedback-seam.md` — per-hit фидбэк в презентере (hitstop, вспышка, цифры),
  MessagePipe-развязка, feel-SO как единый источник, граница «мой контракт / визуал Макса».
  Читать перед правкой моста sim→фидбэк.
