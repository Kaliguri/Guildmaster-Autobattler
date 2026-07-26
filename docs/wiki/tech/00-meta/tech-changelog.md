---
title: "Meta - Tech Changelog & Decisions"
order: 20
status: living
updated: 2026-07-26
---

**Статус:** Ready — живой документ, обновлять при изменениях

---

> Что мы сознательно отложили, что недавно починили (и почему), и какие вопросы открыты. Живая страница: сюда сваливаем технические решения и долг, чтобы они не терялись и взрывались не внезапно, а по плану.
>
> Связано с [[tech/20-explanation/index|Explanation - Code Map]], [[tech/20-explanation/netcode|Explanation - Netcode]].

---

## 1. Зафиксированные решения

| Дата | Решение | Где подробно |
|---|---|---|
| 2026-06-19 | Сетевая модель — **host-authoritative** (не lockstep) | [[tech/20-explanation/netcode|Explanation - Netcode]] |
| 2026-06-19 | `SimSyncProbe` запаркован в `Net/_Parked/` (lockstep-инструмент) | [[tech/20-explanation/netcode|Explanation - Netcode]] |
| 2026-06-19 | **«Эффекты кормят статы»**: Lifesteal/реген — статы-накопители; временные баффы = эффект со `StatModifierComponent`. `LifestealComponent` избыточен | [[tech/20-explanation/data-stats-damage|Explanation - Data, Stats, Damage]] §4, [[tech/20-explanation/effects-abilities|Explanation - Effects & Abilities]] |
| 2026-06-19 | Анимация юнитов — **пиксель-кадровая** (sprite sheets), не скелетная | [[tech/20-explanation/presentation|Explanation - Presentation]] |
| 2026-06-19 | SO-слой — **простой сейчас, моддинг потом** (прямые ссылки, id по мере нужды; UGC — post-festival) | [[tech/40-planning/steam-workshop|Reference - Steam Workshop]] |
| — | Симуляция отделена от презентации (чистый C#-сим, read-only визуал) | [[tech/20-explanation/simulation|Explanation - Simulation & Tick]], [[tech/20-explanation/presentation|Explanation - Presentation]] |
| — | Никаких синглтонов — только DI через VContainer | [[tech/20-explanation/di-events|Explanation - DI & Events]] |
| — | Потенция эффекта — снимок при наложении, не пересчитывается при стаке | [[tech/20-explanation/effects-abilities|Explanation - Effects & Abilities]] §4.2 |
| 2026-07-10 | **Смещение — это эффект** (тег `KnockUp`, `Neutral` → длительность не скейлится; траекторию всё равно ведёт `DisplacementSystem`). Единый шов **`EffectExpired`** вместо `UnitDisplaced` — реактивы фильтруют по тегам эффекта + команде юнита | §2.6 |
| 2026-07-11 | **Контент-каркас (Фаза 4)**: `ContentDefinition`-база + стабильные id `domain.name`, `ContentDatabase` **единым плоским списком** (не per-domain), `StatsConfig` с базой статов + реликвии как Flat-диффы, `IAudioService` → `Core.Audio`, часть тайминг-констант оставлена в `SimConstants` | §2.7, [[tech/10-reference/data-layer|Reference - Data Layer]] §12 |
| 2026-07-17 | **SimBench (инструменты баланса)**: headless-стенд поверх боевого ядра (editor-only, `Guildmaster.Balance[.Editor]`), метрики через outward-events, бенчи процедурные, экспорт CSV/MD. Причина: ядро уже headless-совместимо (даром не использовать грех), нужен стартовый баланс как доп-инфо для ГД. Стенд **не решает** баланс, сочетания не ловит. **Регрессия баланса в CI — вырезана** (наблюдательность на Максе). Оговорка: бой сейчас RNG-free → Monte-Carlo по сидам вырожден, закладываем шов на будущее | [[tech/40-planning/simbench|Planning - SimBench Balance Harness]] |
| 2026-07-17 | **`DamageResult.SourceKind`** — эхо `DamageRequest.SourceKind` в результате урона, чтобы метрики/презентация раскладывали урон по источнику (авто/абилка/DoT), НЕ меняя сигнатуру `OnDamageDealt`. Причина: шов sim→presentation неизменен (правило 1 combat-sim), одна точка конструкции `DamageResult` в `DamagePipeline`. Combat 217/217 зелёные | [[tech/40-planning/simbench|Planning - SimBench Balance Harness]] |
| 2026-07-17 | **`ContentEditService`** (`Guildmaster.Data.Editor`) — write-сторона баланса: правка ЗНАЧЕНИЙ контент-SO через SerializedObject+Undo, change-log в `BalanceReports/`. Сосед `ContentCrudService` (тот — жизненный цикл ассета/id, этот — значения). Причина: у БД-тулинга не было безопасной правки значений; SimBench(read)+ContentEditService(write) = петля баланса | `.claude/skills/xgaida-x-nixi-balance/DRAFT.md` |
| 2026-07-18 | **2D-свет — направление зафиксировано (`planned`)**: динамический `Light2D` на URP 2D Renderer (уже в проекте), иногда тёмные сцены для читаемости, объём через normal-карты как secondary textures. Тулинг normal-карт — **Laigter** (opensource, CLI-батч + пресеты; принято Максом), чтобы НЕ рисовать normal руками на каждый кадр; Aseprite — запасной точечный путь, 3D→bake отложен. Причина: инфра под свет (`Light2D`) уже даром стоит, ручная отрисовка normal на анимацию нежизнеспособна. Детали (палитра, тёмные арены, нужны ли normal на всех) — обговариваются | [[tech/40-planning/lighting-2d|Planning - 2D Lighting]] |
| 2026-07-19 | **UI-реворк — стек-навигатор + ввод = f(верх стека, фаза)**: `UiNavigator` (типы экранов Page/Modal/Sheet + 8 слоёв-контейнеров) стал единственным владельцем видимости и ввода. `GameplaySuppressed`/`Context` не мутируются, а вычисляются; смену фазы навигатор ловит событием `IBattleClock.PhaseChanged` (**K8 закрыт**: `DeploymentController`/`BattleInputController` больше не зовут `SetContext`, только `SetPhase`). Растворены мёртвые слои `IRunTopBar`/`RunTopBarView` (легаси-панель) и `IMenuRouter` (никем не потреблялся как абстракция). Причина: у UI не было единого владельца состояния — «что показано/что глушится/что поверх» складывалось из ~17 переменных в 5 подсистемах, каждая точечная правка ломала соседнюю (регрессии раунда 5). EditMode 418/418 | [[tech/10-reference/ui-navigation|Reference - UI Navigation]] |
| 2026-07-26 | **Звук: пайплайн из одной карты + микс в FMOD**. `scripts/audio/audio_map.py` — единственный источник правды (ключ → категория → сэмплы); из него генерируются нормализованные исходники (−23 dB RMS активной части, потолок −1 dBTP), `manifest.json`, `populate.js`, банки и `AudioCatalog`. В FMOD заведены шины `bus:/SFX/{Combat,UI,Ambient,Stingers}` + `bus:/Music`, категорийные offset'ы, рандомизация питча/громкости и voice-макросы (max instances / stealing / cooldown / priority), плюс глобальный параметр `TimeScale` с кривой питча боевой шины. **Причина:** слайдеры «Музыка» и «Звук» писали в несуществующие шины (все события роутились в Master), разброс громкости сэмплов достигал 19.3 dB внутри одного мульти-инструмента, а параметр `TimeScale`, который игра шлёт с самого начала, никто не принимал — слоумо было не слышно. Событий 31 → 109, EditMode 542/542 | [[tech/10-reference/audio-inventory|Reference - Audio Inventory]], [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]] |
| 2026-07-26 | **Звук вне боя: два новых шва вместо вызовов по экранам**. `UiSoundSystem` слушает корень UITK-панели (клик, наведение, табы, тумблеры, слайдер, скролл), `RunAudioPresenter` живёт в root-скоупе и ведёт экраны, карту, переходы, исход и музыку по существующим MessagePipe-сообщениям и `IBattleClock.PhaseChanged`. `EffectSystem` получил `OnEffectApplied`/`OnEffectEnded` с определением эффекта (старое `OnEffectExpired` с одними тегами не тронуто — на нём висят боевые реактивы). Килл-стингер переехал в `CombatFeelDirector` под тот же кулдаун, что и слоумо. **Причина:** `AudioPresenter` жил только в боевом скоупе и умирал вместе с боевой сценой, поэтому меню, карта, награда и весь UI были немыми; а базового класса кнопки в проекте нет, и озвучивать клики пришлось бы сотней вызовов по экранам | [[tech/10-reference/audio-inventory|Reference - Audio Inventory]] |
| 2026-07-26 | **Отбор сэмплов автоматизирован там, где это возможно, и честно ограничен там, где нет**. `audit_samples.py` ловит технический брак числами; `clap_pick.py` (CLAP, локально) подбирает кандидатов по описанию и сверяет назначенные сэмплы со смыслом ключа; `freesound_fetch.py` тянет CC0-кандидатов; генерация — `sfx_generate.py` (ElevenLabs) и `sfx_generate_local.py` (Stable Audio Open). **Причина:** у контура нет ушей, а проверяемое числами проверять надо — первый прогон аудита нашёл 26 сэмплов с 30–80 мс тишины в начале (звук опаздывал за ударом) и 5 за пиковым потолком, всё это теперь чинится в самой нормализации. **Оговорка (замерено):** CLAP ненадёжен на коротких сухих one-shot — корреляция длительности со скором +0.47, поэтому сэмплы короче 0.6 с помечаются, а не оцениваются | [[tech/40-planning/sfx-round-2|Planning - SFX Round 2]] §8a |
| 2026-07-26 | **Фолбэки: три полосы, и граница по источнику отказа**. Фолбэк допустим только там, где отказ приходит ИЗВНЕ игры (диск, ОС, локаль игрока, банк FMOD); всё, что внутри нашего авторства — конфиг, ссылка сцены, DI-регистрация, id контента, лок-ключ — фолбэка не имеет: значение есть, либо это баг разводки, громкий и пойманный guard-тестом до запуска. Оставшийся фолбэк обязан деградировать в «явно неправильно», а не в правдоподобное. Разнесено по коду: 14 гардов `MenuRouter` идут через `CannotShow` (`LogError` вместо молчания), обязательные UXML — в реестре `SceneWiringTests`; новый `ScopeWiring.Require`/`Optional` вместо асимметрии скоупов; удалены `NullScreenShake`, `UnityAudioService`, дефолт `beat = null` в `ActRunner`, ветки `Shader.Find` в обоих барах; включена fallback-локаль (`m_UseFallback: 1` + `FallbackLocale` en→ru); `ClassBaseline`, `RunStateService`, `NodeResolver` перестали подставлять молча. **Причина:** тихий фолбэк на собственное авторское значение делает баг разводки невидимым — в шипящейся `CoreScene` не был назначен `_campScreen`, и обязательный узел привала (этажи 8 и 13 каждого акта) молча засчитывал сам себя; там же награда резолвилась в `Skip`, а главное меню отвечало `Quit`, то есть игра закрывалась сама, без единой строки в логе. **Две оговорки, добытые заходом:** громкий отказ ≠ отказ выполнить шаг (незавершённый колбэк вешает петлю забега навсегда, что для игрока хуже пропущенного экрана — поэтому лог + шаг завершается, а блокирует guard-тест ДО запуска); уровень сигнала выбирается по тому, где стоит настоящий гейт (`LogError` в `ClassBaseline` уронил 9 тестов, строящих юнита без каскада намеренно, и понижен до warning, раз `Require` сделал случай невозможным в игре). EditMode 598/598 | [[tech/20-explanation/di-events\|Explanation - DI & Events]] §1.3, `docs/fallback-audit.md`, правило — `.cursor/rules/project-context.mdc` |
| 2026-07-26 | **Сцены: три persist вместо «сцена на бой»**. Состав сцен зафиксирован — `CoreScene` (вход + сессионный DI + рантайм-UI), `WorldScene` (камера-риг, арена, карта, фон меню), `CombatSystemsScene` (бывшая `BattleScene`: боевой скоуп, презентер, дев-инструменты). Обе аддитивные грузятся ОДИН раз на буте и не выгружаются; цепочка скоупов `Root → World → Combat`. Снят legacy-путь «загрузить сцену на бой → выгрузить после» (`GameFlow.BootAsync`/`OnBattleEndedAsync`, флаг `_legacyBattleScene`, `ISceneLoader.UnloadBattleAsync`), вместе с ним из `BattleFlow`/`NodeResolver` ушла мёртвая зависимость от загрузчика. Снесены `BootScene` (пустышка вне билда) и `UiGallery` с её UXML (вторая витрина компонентов, разошедшаяся с кодовой). **Причина:** имя сцены обещало то, чего нет — бой это команда в живую симуляцию, а не загрузка; два сосуществующих способа жизни одной сцены плодили путаницу, а выгрузка сцены на узел прямо противоречит persist-миру (отряд, камера и арена обязаны пережить узел). EditMode 534/534 | [[tech/10-reference/scenes|Reference - Scenes]] |
| 2026-07-26 | **Направленный импакт-фидбэк: право у прямого попадания, направление у снаряда**. Искры, отброс тела, выпад атакующего и пыль под ногами рисуются только на `DamageResult.IsDirectHit` (авто-атака или способность); тик яда, горение и ответка шипов получают лишь цифру и вспышку. Дальний удар берёт направление из траектории снаряда, а не из вектора «стрелок → цель». **Причина:** у периодического урона нет ни автора в моменте, ни стороны, а бьёт он так же часто, как удары — искры на нём читались как удар, которого не было (отдельной глупостью был выпад: отравитель дёргался к жертве через полкарты на каждом тике). Вектор «стрелок → цель» врёт ровно в тех случаях, ради которых считается: Следопыт стреляет на ходу и за время полёта смещается, цель уходит вбок. **Готча:** снаряд к моменту события урона уже мёртв (сим ставит точку удара и `IsAlive=false` в том же тике) — направление приходится помнить заранее, по цели, и чистить на сбросе боя. Свой язык для DoT — открытый дизайн-вопрос, отложен сознательно. EditMode 638/639 (падение `MirrorMatchTests` — из параллельной сессии по балансу) | [[tech/20-explanation/presentation\|Explanation - Presentation]] §2.1 |
| 2026-07-26 | **Цвет VFX: яркость щедро, насыщенность как валюта**. Цвет боевых эффектов живёт на юните двумя полями — `_vfxColor` (главный: тело снаряда, след, контур каста) и `_vfxPalette` (диапазон случайного разброса частиц). Норма авторинга: насыщенность 70–90%, ни один канал не в нуле, яркость выше порога bloom (главный ×2.0, насыщенный конец палитры ×2.2, пересвет — смесь с белым 75% и ×2.6). Ростер раскрашен: герои разведены по спектру, враги — единая красная семья. **Причина:** «максимально ярко И максимально насыщенно» — конфликт в одном пикселе (чистый канал не имеет градаций, форма исчезает), а базовая планка на максимуме съедает шкалу значимости, которой мы уже говорим: автоатака тише, каст ярче, смерть пробивает экран. Внешний канон (стайлгайд League) требует того же — прочь от 100% и 0% насыщенности. **Готча:** и частицы, и шейдер осколков читают у палитры ТОЛЬКО концы (`Evaluate(0/1)`) как границы `MinMaxGradient` — промежуточные ключи не работают; `Color.white` в главном поле означает «не задан» и молча уводит эффекты в тинт тела | [[gdd/10-vision/vfx-color\|Vision - VFX Color]], [[tech/10-reference/vfx-color\|Reference - VFX Color]] |
| 2026-07-26 | **Сохранения: иерархия профиль → гильдия → забег, две шкалы версий, Auto-Cloud**. Профиль — мета аккаунта (до 4, переключаемый: соло / игры с друзьями), внутри — до 8 гильдий, в гильдии — не более одного активного забега; лимиты в `GameConfig`. Слот сохранения отдельной сущностью не заводится: слот и есть гильдия. Версия игры (SemVer) и `schemaVersion` (своя у каждого типа файла) разведены — на загрузку смотрит только вторая, первая пишется для багрепортов. Steam Cloud — Auto-Cloud по маске `Saves/**/*.json`; настройки разделены на синхронизируемые `prefs` и локальные `machine`. Выход посреди боя откатывает к началу узла (снапшот живой симуляции отклонён: бой детерминирован саб-сидом, ретрай = тот же бой). Ломать `run` при мажорных правках можно, `profile` мигрируется всегда. В коопе пишет забег только хост, клиент — свой профиль. **Причина:** durable-состояние жило одним файлом `run` без меты вовсе, `SchemaVersion` писался и не читался, а `SettingsService` вёл вторую запись на диск мимо `ISaveService` — без атомарности, `.bak` и версии. **Готча, найденная заходом:** `persistentDataPath` завязан на `productName`, а имя игры недавно меняли — смена после релиза убивает и сейвы, и маску Auto-Cloud. **Дрейф док↔код:** доки заявляли три точки автосейва, в коде `Autosave()` зовётся из девяти мест — фактическая политика «после каждого значимого изменения», доку привести к коду | [[tech/40-planning/save-system\|Planning - Save System]] |

---

## 2. Changelog ревизии 2026-06-19 (что починили)

Критический разбор кода выявил ряд проблем; ниже — что исправлено в этой ревизии. Тесты при этом **не писали** (по договорённости — оценим по факту), но компиляция зелёная, существующие тесты обновлены под новый конструктор.

| # | Проблема | Исправление | Файлы |
|---|---|---|---|
| ① | Аккумулятор боевого цикла без потолка → «спираль смерти» на хитче | Кап `MaxCatchUpTicksPerFrame = 5`, сброс долга при упоре | `SimConstants`, `CombatLoopService` |
| ② | `Lifesteal`/`HpRegenFlat`/`HpRegenPct` помечены `[Ф1]`, но ни один код их не читал | Вампиризм в `DealDamage`; новый `RegenSystem` в тике | `CombatSimulation`, `RegenSystem` (new) |
| ③ | Пустой бой (0 юнитов) крутится вечно вхолостую | Guard `_hasSpawned` вместо проверки `_units.Count == 0` | `CombatSimulation` |
| ④ | Диспел последнего контроль-эффекта → флаги `CanAct/Move/Cast` залипают навсегда | `RecomputeControl` в `Dispel`/`Remove` | `EffectSystem` |
| ⑤ | Мгновенный эффект со stateful-компонентом течёт (вечный бафф) | Instant → после `OnApply` сразу `OnExpire` | `EffectSystem` |
| ⑥ | `Heal` не клал событие `Healed` → on-heal реактивы мертвы | Enqueue `CombatEvent.Healed` по фактически вылеченному | `CombatSimulation` |
| ⑦ | Неясность семантики снимка потенции при стаке | Зафиксировано правило комментарием (поведение не менялось) | `EffectSystem` |
| ⑨ | Ручной DI-бойлерплейт (10 `Resolve` на конструктор) | `WithParameter("armorK", …)` + авторазрешение | `CombatLifetimeScope` |
| — | `LifestealComponent` дублирует стат-путь под моделью B | Помечен баннером «избыточен, см. 07 §3.7» (удаление — позже) | `LifestealComponent` |

> ⑧ (сетевая модель) — не «фикс», а решение: см. [[tech/20-explanation/netcode|Explanation - Netcode]].

---

## 2.1 Changelog 2026-07-09 — Светлый пастырь (§10.1, шаг 6)

Первый срез шага 6 (первая играбельная Обычная реликвия): хилер «Светлый пастырь». Затянуты движковые расширения §9.2 (хил-АА снарядом) и §9.10 (глобальная одиночная лечащая активка). **144/144 EditMode зелёные** (+8 `ShepherdSliceTests`).

| Что | Где |
|---|---|
| Флаг полезной нагрузки хил-снаряда `IsHeal`; ветка `ProjectileSystem.ApplyHit` лечит вместо урона | `Projectile`, `ProjectileSpawn`, `ProjectileSystem`, `CombatSimulation` |
| Хил-режим авто-атаки: гейт замаха и снапшот по `AutoAttackTarget` (раненый союзник), спавн tracking-хил-снаряда, payload = **сырое** `AutoAttackDamage` | `AutoAttackSystem` |
| Активка «Длань жизни»: цель `LowestHpAlly` (глобально), хил `HealFlat + HealPctTargetMissingHp × недостающее`, условие `AllyTargetHpBelowPct` (блок D), блок E — свой низкий HP разворачивает лечащую активку на себя | `AbilityData`, `AbilityTargetMode`, `CastCondition`, `AbilitySystem` |
| Контент + харнесс: `LightShepherd.asset` (провизорные статы), команда `gm_spawn_shepherd`, ссылка в `BattleScene` | `GuildmasterCommands`, ScriptableObjects, `BattleScene` |

> **Решение по эффективностям (важно на будущее):** `ctx.Heal` уже домножает на **обе** эффективности (`HealShieldDealtEff` источника × `HealShieldTakenEff` цели). Поэтому доставка (снаряд/активка) кладёт **сырое** значение — не домноженное. Формула «amount = AA × DealtEff» из §9.2 буквально дала бы двойной учёт dealtEff. `NoRangeLimit`-поле НЕ вводили: гейта дальности в `AbilitySystem` нет, `LowestHpAlly` и так глобален (мёртвое поле не заводим).

---

## 2.2 Changelog 2026-07-09 — Криомант (§10.2, шаг 6)

Второй срез шага 6: контроллер «Криомант». Затянуты §9.1 (on-hit эффекты авто-атаки) и §9.10 (масс-каст по тегу + конверсия тега). **150/150 EditMode зелёные** (+7 `CryomancerSliceTests`).

| Что | Где |
|---|---|
| On-hit эффекты авто-атаки (§9.1): `RelicData.AutoAttackEffects[]`, едут на снаряде (`OnHitEffects`), накладываются в `ProjectileSystem.ApplyHit`; симметрично реализован и мили single/Line | `RelicData`, `ProjectileSpawn`, `Projectile`, `ProjectileSystem`, `AutoAttackSystem`, `CombatSimulation` |
| Масс-каст по тегу (§9.10): `AbilityTargetMode.AllEnemiesWithTag`, `AbilityData.TriggerTag`/`ConsumesTriggerTag`, `CastCondition.EnemiesWithTagCount`; `ApplyAllWithTag` бьёт всех тегнутых глобально + `ctx.Dispel` тега (конверсия «Заморозки» в стан) | `AbilityData`, `CastCondition`, `AbilitySystem` |
| Инверсия приоритета `TargetingMode.PreferUntagged` (ветка-зеркало PreferTagged) — Криомант не добивает уже замороженного | `TargetingMode`, `ProfileBrain` |
| Контент + харнесс: `Cryomancer.asset`, `Effects/Frozen.asset`, `Effects/IceChainsStun.asset`, команда `gm_spawn_cryomancer`, ссылка в `BattleScene` | `GuildmasterCommands`, ScriptableObjects, `BattleScene` |

---

## 2.3 Changelog 2026-07-09 — Защитник / Следопыт / Убийца / Монах + dev-VFX (§10.3–10.6, закрытие шага 6)

Оставшиеся 4 Обычных реликвии + движковые расширения §9 (E1–E10 полностью) + dev-подсветка боя. **171/171 EditMode зелёные** (+7 Defender, +5 Ranger, +5 Assassin, +5 Monk слайс-тестов).

| Реликвия | Движок (§9) | Ключевое |
|---|---|---|
| §10.3 Защитник | §9.3 pre-damage хук | `IPreDamageComponent` + `EffectSystem.RunPreDamage` перед `DamagePipeline.Execute`; «Оплот» = `BulwarkComponent` (щит `20+15%·недост.HP` до удара, внутр. КД в `RuntimeEffect.ReactiveReadyTick`) + `MissingHpShieldComponent`; ульта — стан+`−30% DamageDealtEff`. Спайк S5 влит в срез-тесты. |
| §10.4 Следопыт | §9.5/9.7/9.8 | Кайт/отступление в `MovementSystem` (полоса `[AttackRange×0.6, R]`); `RelicData.CanAttackWhileMoving`+`MovingAttackSpeedPenaltyPct`; `MarkTransferComponent` (перенос метки при смерти носителя через `CombatEvent.UnitDied`, диспатч мёртвому). |
| §10.5 Убийца | §9.6/9.4/9.3 | `RuntimeUnit.EmpowerDamageMult` (усиленный первый удар, снимает `Stealth`); `DodgeComponent` — negate входящего с зарядами (`RuntimeEffect.ChargeReadyTicks`, `PreDamageResult.Negated`); `StealthComponent` + `CombatEvent.UnitKilled` (рестелс по своему убийству). |
| §10.6 Монах | §9.9/9.6 | `DisplaceRequest`+`DisplacementSystem`+`ICombatContext.Displace` (детерм. перемещение, «ядро» по врагам на линии, `DisplacedTicksRemaining` стан-в-полёте); `VortexEntryComponent` (телепорт-в-спину ×2 на `UnitDisplaced`, без КД). |

**dev-VFX батч (презентация, чтение сима, интерфейс не расширялся кроме fire-and-forget `OnAttackEvaded`):** цифры урона по `DamageResult` (синий `-N` щит, оранжевый `-N` HP, сплит с задержкой, `+N` хил, «evade» при полном негейте); `FloatingText` запулен (`ObjectPool`). Новый `CombatStatusOverlay` (Shapes-кольца: метка/стан/щит/Frozen/Empower, создаётся в рантайме); инвиз ассасина — альфа спрайта в `UnitView`; линия толчка Монаха — `ReportAreaHit`. `gm_toggle_status`, F5 = быстрый рестарт (`gm_restart`, new Input System).

> **Отложенные упрощения (к правкам — след. сессия):** блок C (своя цель ульты) сведён к `CurrentTarget`; кайт по `AttackRange`, а не `Kite.FleeDist/FallbackDist`; заряды реактивно в `DodgeComponent`, а не generic `AbilityData.Charges`; у Монаха нет рывка-в-сторону перед толчком и умного выбора точки. Спрайты реликвий не назначены. Баланс-числа провизорные. Кольца-цвета — константы в коде. Осталось §11 (закрытие Ф3: roadmap §3, ретро, синхрон AI-словаря 0.3).

> **Решение по гейту масс-каста:** при нуле тегнутых врагов способность НЕ кастуется (трактуется как «нет валидной цели») — не жжёт КД/ману в пустоту даже под панику блока E. Условие `EnemiesWithTagCount ≥ X` слоится сверху. **Провизорно** (баланс Ф4/Ф8): `Frozen` = `StackRule.Refresh` (повторная АА обновляет 4с, не копит слоу), X = 2 замороженных, статы (HP 120 / урон 12 / MoveSpeed 2.8). Спрайт не назначен.

---

## 2.4 Внешний аудит — 09–10.07.2026 (5 моделей)

Пять независимых моделей (Claude Opus 4.8, Codex 5.3, Composer, Cursor Grok 4.5, GPT-5.6 Sol) прогнали аудит `Assets/_Project/Scripts` на `dev` HEAD `fd858f8`. Исходники аудитов — `docs/audits/` (архив снимка). **Консенсус 5/5:** фундамент здоровый, ядро переписывать не надо, риски — на швах (seed, netcode, презентация), а не в архитектуре.

> **Важно про снимок:** аудиты читали коммит `fd858f8`, но рабочее дерево на момент сведения уже ушло вперёд (незакоммичены арена + движение: `CombatSimulation`, `MovementSystem`, `DisplacementSystem`, `RelicData`, `CombatLifetimeScope`). Часть находок про «нет арены / `±200` хардкод» **уже неактуальна** — арена закодирована (`ArenaLayoutData`/`ArenaBounds`/`DeploymentService`).

Дедуплицированный реестр находок с проверкой по коду — §3.8 ниже.

---

## 2.5 Changelog 2026-07-10 — Разбор аудита: фиксы B1–B7 + I1

Закрыты все подтверждённые баги реестра §3.8 (P0-корректность + P1) плюс дешёвый инфра-долг I1. **187/187 EditMode зелёные** (+3 регресс-теста: щит-рестак, дож-рестак, кайт по профилю). Каждая находка сверена по коду перед правкой — аудиты читали снимок `fd858f8`, рабочее дерево ушло вперёд.

| ID | Было | Стало | Файлы |
|---|---|---|---|
| B1/B2/B3 | Рестак стака слепо звал `OnExpire→OnApply` с **новым** `Stacks`: щит пере-вычитался (`Mathf.Max` съедал частично израсходованный пул), заряды Доджа бесплатно перезаряжались | Новый опциональный шов `IStackableComponent.OnStacksChanged(prev, ctx)`; `Reapply` диспатчит в него, иначе прежний дефолт (keyed-снятие вроде `StatModifier` не трогаем). Щит правит вклад **дельтой** `Potency×(new−prev)`; Додж — no-op (заряды не сбрасываются) | `IRuntimeEffectComponent`, `EffectSystem`, `ShieldComponent`, `DodgeComponent` |
| B4 | `MoveKite` игнорировал `Kite.FleeDist/FallbackDist`, использовал магию `AttackRange×0.6 / AttackRange` | Полоса кайта = `[FleeDist, FallbackDist]` из профиля; фолбэк на радиус атаки при пустом/битом контенте | `MovementSystem` |
| B5 | Broad-phase линии брал радиус `length` → цель у дальнего угла (`√(length²+halfWidth²) > length`) отсекалась до narrow-phase | Радиус `length + halfWidth` (консервативный супермножество) | `CombatSimulation.QueryUnitsInLine` |
| B6 | Кап дренажа `MaxEventsPerDrain=512` делал молчаливый `Clear()` — исход тихо зависел от «уложились ли» | `Debug.LogError` (тик + число сброшенных) перед `Clear()` — dev-сигнал, не молчаливая потеря | `CombatSimulation.DrainEventQueue` |
| B7 | `IronSpearman`: `_resourceType: None`, но копит (`_resourceOnHit:5`, `MaxResource:30`) и тратит (активка 30) ресурс — противоречивый контракт | `_resourceType: Rage` (build-and-spend). **Провизорно** — баланс Ф4/Ф8 | `IronSpearman.asset` |
| I1 | `run-tests.ps1` хардкодил Unity `6000.0.23f1` → падал `Unity not found` на `6000.4.8f1` | Версия парсится из `ProjectSettings/ProjectVersion.txt` | `run-tests.ps1` |

> **По B1/B2/B3:** полный вынос per-component state из общего `RuntimeEffect` (крайняя форма B3) НЕ делался — для корректности хватает дельта-шва. Поля состояния (`ChargeReadyTicks`, `PendingShield`) остаются на `RuntimeEffect`. Дефолтный `OnExpire→OnApply` сохранён для keyed-компонентов (снятие по ключу-эффекту идемпотентно).
> **По B7:** ни `AutoAttackSystem.GainResourceOnHit`, ни `AbilitySystem` сейчас не гейтят на `ResourceType` (проверяют только `ResourceOnHit`/`CurrentResource`), поэтому правка не меняет рантайм — чинит контракт данных на будущее (UI ресурс-бара, валидатор C2). Направление (Rage, не Mana) — по модели «копится от ударов, тратится на ульту».

---

## 2.6 Changelog 2026-07-10 — Смещение-как-эффект, переработка монаха, презентация снаряда, батч UX

Ветка `fix/combat-camera-ai-mana`. **190/190 EditMode зелёные** (+3: репозиция у стены ×2, полная цепочка монаха). Крупное решение — смещение переехало в систему эффектов.

**Решение: смещение — это ЭФФЕКТ, а не жёсткое состояние.** Раньше полёт жил в `DisplacementSystem` как хард-стейт (`DisplacedTicksRemaining`) с бесшовным событием `UnitDisplaced`. Теперь:
- `ctx.Displace` вешает на цель системный эффект-маркер «в полёте» (строится в коде `EffectData.CreateRuntime`, тег `KnockUp | Control`, `ControlComponent(act/move/cast)`, `Unremovable`, **`Neutral` → длительность не скейлится `ReceiveDebuffEff`** — единственное исключение смещения). Траекторию/«ядро»/тайминг по-прежнему интегрирует `DisplacementSystem` в своей фазе тика (позицию нельзя выразить пассивным компонентом; per-cast динамика не влезает в shared-компонент SO).
- В конце полёта `DisplacementSystem.OnDisplacementEnded` → `EffectSystem.RemoveByTag(target, KnockUp)` → единый **`EffectExpired(unit, source, tags)`** (заменил `UnitDisplaced`). Носитель-получатель = источник эффекта; реактив фильтрует по тегам эффекта + команде юнита, на котором он закончился.
- `EffectExpired` — общий шов «эффект закончился»: будущее «Заморозка истекла → осколки» ложится сюда же. Заведён по факту (монах), не про запас.

| Что | Где |
|---|---|
| Событие `EffectExpired` (+ `CombatEventData.Tags`), убрано `UnitDisplaced`; `EffectSystem.OnEffectExpired` + `RemoveByTag`; фабрика `EffectData.CreateRuntime`; публичный ctor `ControlComponent` | `CombatEvent`, `EffectSystem`, `EffectData`, `ControlComponent`, `CombatSimulation` |
| **Монах §10.6 — правильная цепочка: рывок → фиксация → отбрасывание → телепорт.** Активка «Шквальный толчок» = гейт `EnemiesInRadius(6)` + **рывок** самого монаха (self-displacement) к цели + **фиксация** цели оцепенением (`VortexHold`, `preventMove`). Приземление рывка (`WhirlDashLandingComponent`, реагирует на `EffectExpired`/`KnockUp` где смещённый = сам носитель) → **отбрасывание** ближайшего врага «от себя вперёд» (в строй) + «ядро». Задетые «ядром» получают урон И слабо цепляются отбрасыванием (`DisplaceRequest.ChainDistance/ChainTicks`, cannonball:false → без каскада) — что тоже триггерит телепорт; цепь слабая, чтобы финальный телепорт сел на исходную цель. Конец отбрасывания (`VortexEntryComponent`, смещённый = враг) → **телепорт в спину + усиление ×2**. Разведение фаз — по команде смещённого, без искусственных флагов | `AbilitySystem`, `VortexEntryComponent`, `WhirlDashLandingComponent` (new), `WhirlMonk.asset`, `VortexHold.asset` (new), `VortexDashLanding.asset` (new) |
| Монаху дана стартовая мана (`StartResource 0→30`) — «толчок» стал инструментом захода, а не поздней опцией | `WhirlMonk.asset` |
| **Презентация снаряда** (её не было): `CombatSimulation.OnProjectileSpawned`; `ProjectileView` следует за ссылкой на симовый `Projectile` интерполяцией + поворот по скорости; тинт = цвет юнита-источника. **Синхрон импакта:** при попадании сим ставит `Position` = точка удара и `IsAlive=false` в тот же тик, что и `OnDamageDealt` → вид снапается в точку удара и гаснет (визуальный импакт = реальный эффект). Bullet на слое `OverheadFX` | `CombatSimulation`, `ProjectileView` (new), `CombatPresenter`, `Bullet.prefab`, `BattleScene` |
| Батч UX: экшн-камера плавнее (снап на 1-м кадре + сглаживание разброса + дедзона зума); репозиция у стены (кайт/отступление, упёршись в стену, скользят вдоль неё к центру); мана-бар (`ManaBarView`, зеркало HP); гейт ввода при открытой QFSW-консоли (пан/зум/Space/Tab/R глушатся, **F5 живёт**) | `CombatFocusTarget`, `CameraModeController`, `MovementSystem`, `ManaBarView` (new), `IInputService`/`InputService`, `GuildmasterCommands` |

> **Почему смещение НЕ «тикается» внутри общего эффект-тика:** `EffectSystem.Tick` идёт поздней фазой; если двигать полёт там, сместится момент обновления позиции летящих относительно авто-атак/снарядов (детерминизм, ловят two-run тесты). Поэтому `DisplacementSystem` сохраняет свою фазу (после Movement, до перестройки spatial hash) и лишь читает/снимает эффект-маркер.

**Follow-up (тот же день):**
- **R = рестарт боя НА МЕСТЕ, без перезагрузки сцены** (убран костыль: static-флаг `_replayLastBattleOnStart` + `SceneManager.LoadScene`). `CombatSimulation.ResetBattle()` чистит юнитов/снаряды/очереди и возвращает `Ongoing` + событие `OnBattleReset` (презентер снимает виды). `CombatLoopService` теперь живёт весь скоуп и тикает только при `Ongoing` (иначе простаивает) — поэтому после `ResetBattle` тик сам возобновляется. Камера/арена не трогаются. F5 остаётся полным релоадом сцены. | `CombatSimulation`, `CombatLoopService`, `CombatPresenter`, `GuildmasterCommands` |
  - **Аудит отката (что забывали чистить, раз релоад сцены больше не «чинит» всё за нас):** (1) `RuntimeUnitFactory._nextId` — счётчик Id фабрики уезжал от схемы Id болванчиков (`Units.Count+1`) → **коллизия Id** → в презентере два юнита на один ключ, вид одного (монаха) осиротевал и висел на месте (HP-бар не реагировал), хотя сим шёл верно. Добавлен `ResetIds()`, зовётся в R. (2) `DisplacementSystem._active` — незавершённые полёты держали ссылки на удалённых юнитов; добавлен `Clear()` в `ResetBattle`. Прочие системы stateless (только переиспользуемые буферы) — чистить нечего.
- **Тюнинг:** MoveSpeed всех реликвий ×0.6 (бегали слишком быстро) + болванчики 3→1.8; `gm_spawn_monk` — интервал болванчиков 0.7→1.6 и параметр урона `enemyDamage` (дефолт 5, бой дольше). | 7 relic `.asset`, `GuildmasterCommands` |
- **Монах — заход/бросок:** монах бьёт ТОЛЬКО прямо от себя → позицию рывка выбираем так, чтобы «монах → цель» смотрела в ближайшего ДРУГОГО врага («наковальню», в поиске исключается сама цель), и бросок летит в него, а не в пустоту. Толкаемая цель запоминается (`RuntimeUnit.PendingEngageTarget`) на касте — приземление толкает именно её, а не «ближайшего» (тот мог разъехаться). | `AbilitySystem`, `WhirlDashLandingComponent`, `RuntimeUnit` |
- **Dev-оверлеи (Shapes) прятались за спрайтом арены:** зоны ударов (`CombatAreaFlash`) и кольца статусов (`CombatStatusOverlay`) рисовались на слое сортировки `Default` (самый нижний), арена — на `Background` → перекрывала. Заведён верхний слой `DevOverlay` (TagManager), оверлеи вешаются на него (`SortingLayerID` по имени, ленивый резолв). | `TagManager.asset`, `CombatAreaFlash`, `CombatStatusOverlay` |

---

## 2.7 Changelog 2026-07-11 — Фаза 4 «Контент-каркас» (пакеты 0–7)

Ветка `feat/content-framework` (13 коммитов над `dev`). Построен весь слой данных по [[tech/10-reference/data-layer|Reference - Data Layer]] §10, порядок 0→7 пройден целиком. **221/221 EditMode зелёные.** Дизайн-файл 13 переведён в статус «Реализовано» (+§12 отступления).

| Пакет | Что | Ключевое |
|---|---|---|
| **0** `9e62302c` | Фикс-лист §4.2: один источник tuning | `armorK` из `StatsConfig`, `DefaultPassiveThreshold`/`GlobalSearchRadius`/снарядные консты, деспаун снаряда привязан к границам арены, мёртвые `DamageNumber*` удалены |
| **1** `2669c8d7` | `ContentDefinition` + жизненный цикл id + реестр + валидация | База с `_id` (+Odin id-drawer), `ContentDomains.MakeId`, `IContentDatabase`/`ContentDatabase` в DI, Relic/Vessel/Effect → `ContentDefinition` (убран `DisplayNameKey`), 20 ассетов получили канон-id + folder-per-type, `ContentValidationTests` (§8 правила 1,2,3,5) |
| **2** `15770403` | `SimTuningConfig`-снапшот + `GameConfig` + бейк-шов | Read-only снапшот тюнинга через `ICombatContext.Tuning`; `gm_tuning_rebake`; `ConfigValidationTests`. Checksum не изменился |
| **3** `c855c3fb`…`6e393d35` | Иерархия `UnitData` → `RelicData`/`EnemyData` + мета-типы + база статов | 7 новых SO-типов, `TagData`/`TraitData`/`ConsequenceData`/`AIPresetData` (+миграция inline-AI 7 ассетов), `RuntimeUnit.Relic`→`Unit` переименование (~15 мест), `StatsConfig.Defaults` = база + реликвии как Flat-диффы, глоссарий GDD Perk→Trait/+Consequence |
| **4** `dd6d69a4` (+сцена `6173f89e`) | `UnitVisual` → Unity Animation: клипы-слоты, `AnimatorController` + override, бейк маркеров | Клип = единственный источник; кадр/хит атаки **выводятся из клипа** в рантайме, `fireEvents = false`; `AbilityData.VisualSlot`; data-driven `RelicData._visual`; `EnemyData` `enemy.training_dummy`; NPE-фикс `MovementSystem` (Ai может быть null). **Визуальный гейт принят Максом вживую** |
| **5** `c725ce3a` | Контент-менеджер §9 | `OdinMenuEditorWindow` (дерево по доменам) + GUI-free `ContentCrudService` (Create/Duplicate с регеном id/Delete под `FindUsages`/Sync) — шов на замену IMGUI→UITK |
| **6** `e5c0cca1` | Localization EN/RU + Addressables-плумбинг | Addressables 2.3.1→2.3.16; таблицы `Content`(+`UI`-stub), Locales en/ru; `ILocalizationService` (Core) + `LocalizationService` (Game); лок-помощник Name/Desc в инспекторе + create-missing-keys; валидация ключей §8.4 (7 реликвий name+desc, 13 эффектов name) |
| **7** `cd70c751` | `AudioCatalog` + резолвер (FMOD-шов) | `AudioAction`-enum, `AudioCatalog` SO, `AudioResolver` (`{id}.{action}`→дефолт действия→тишина+лог, чисто-тестируемый), `AudioPresenter` (MessagePipe DamageDealt→hit / UnitDied→death). Звука ещё нет — `IAudioService` = Debug.Log-stub |

**Отступления от дизайна** (полная таблица — [[tech/10-reference/data-layer|Reference - Data Layer]] §12): (1) `ContentDatabase` единым плоским списком, не per-domain §3.6; (2) `DisplayNameKey` убран — имя только через лок-ключи; (3) `StatsConfig` получил базу статов, реликвии переписаны как Flat-диффы (числа провизорные, баланс за Максом); (4) `IAudioService` переехал в `Core.Audio` (зеркалит `ILocalizationService`); (5) `AiTickRate`/`MaxAttackAnimTicks`/`MinWindupTicks` оставлены в `SimConstants` (структурные/тайминговые, не балансные ручки); (6) деспаун снаряда по `CameraZone`, не по краю арены.

> **NB по контенту:** база = 28 записей (7 реликвий + 13 эффектов + 7 AI-пресетов + 1 болванчик `enemy.training_dummy`). `VesselData`/`TraitData`/`ItemData`-ассетов ещё нет — типы готовы, наполнение по мере строительства флоу. EN-переводы реликвий — черновик (Макс полирует позже). FMOD-звук и VFX-каталог — Фаза 7/9. Опциональный хвост Фазы 4: перевод реликвий с общего temp-визуала (`MedievalWarrior`) на свои по мере готовности арта (Pack 2/3 нарезаны).

---

## 3. Открытый техдолг (сознательно отложено)

Эти пункты **не баги прямо сейчас** — это запланированный долг. Чинить, когда дойдёшь до соответствующей фазы, с тестом на руках.

### 3.1 ⑩ Разбухание `CombatSimulation` (god-object)
`CombatSimulation` совмещает: тик-цикл, реализацию `ICombatContext`, очередь команд, хаб C#-событий, фабрику снарядов, расчёт checksum (~340+ строк). Пока читаемо, но это место будет пухнуть. **Когда дойдёт** (а не сейчас — иначе преждевременная абстракция): вынести очередь команд и checksum в отдельные классы. Приоритет — низкий, до появления реальной боли.

### 3.2 Аллокации в `Stats.RebuildCache`
Три `new float[StatCount]` на каждую пересборку кэша статов. В Фазе 1 пересборка одноразовая (модификаторы ставятся раз при создании юнита) — неважно. **В Фазе 2** эффекты будут часто менять статы → пересборки участятся → перевести на переиспользуемые инстанс-буферы. Триггер: подключение стат-меняющих эффектов в контент.

### 3.3 Грубый `alpha` интерполяции презентации
`CombatPresenter.Update` берёт `alpha = Time.deltaTime / TickDelta` — это не корректная доля интерполяции (правильная — `accumulator / TickDelta` из `CombatLoopService`). Движение сглажено, но не идеально по фазе. **Фикс:** пробросить дробную часть аккумулятора из loop в презентер. Косметика, не срочно.

### 3.4 Таргетинг O(n²)
`TargetingSystem` ищет ближайшего врага полным перебором. На десятках юнитов это дешевле запроса к `SpatialHash` — осознанно. **Когда** число юнитов вырастет (Фаза 3) — перевести на `SpatialHash.QueryRadius` (TODO уже стоит в коде).

### 3.5 Валидация контента эффектов
Правка ⑤ убрала утечку мгновенных эффектов рантайм-способом (OnApply→OnExpire). Но лучше бы ещё **запрещать на этапе авторинга** ставить stateful-компоненты на мгновенный эффект (валидация в `EffectData`/редакторе). Аналогично — предупреждать о бессмысленных комбинациях. Задача редакторного тулинга (Фаза 4, Odin-авторинг).

### 3.6 Вампиризм применяется ко всему урону
Правка ② вешает lifesteal на **любой** нанесённый урон (включая DoT/шипы), т.к. у урона пока нет тегов источника. **Сузить** до авто-атак/способностей — когда появятся теги источника урона (Фаза 2).

### 3.7 Примирить `LifestealComponent` под моделью B
Реактивный `LifestealComponent` лечит напрямую, минуя стат — под моделью «эффекты кормят статы» он избыточен и создаёт риск двойного учёта (стат-путь + компонент на одном юните). Пока **только помечен** баннером (на нём держится `ReactiveEffectTests`). **Когда** начнётся авторинг контента: удалить компонент, заменить вампирик-баффы на `StatModifierComponent(+Lifesteal)`, мигрировать тест (оставить в нём `ThornsComponent` как пример реактивности). Триггер: первый реальный контент с вампиризмом.

---

### 3.8 Реестр внешнего аудита (2026-07-10)

Сведено из пяти аудитов (`docs/audits/`), дедуплицировано, каждый пункт сверен с рабочим деревом. Статусы: ✅ подтверждён по коду · 🔍 правдоподобно, нужна проверка · ⚠️ устарел/неверен на текущем дереве · 🔗 уже описан выше.

**Подтверждённые баги (это НЕ отложенный долг — реальная корректность):** — B1–B7 **исправлены 2026-07-10**, см. [[#2.5 Changelog 2026-07-10 — Разбор аудита фиксы B1–B7 I1|§2.5]].

| ID | Баг | Где | Статус | Приоритет | Нашёл |
|---|---|---|---|---|---|
| B1 | Рестак щита пере-вычитает: `Reapply` зовёт `OnExpire` с **новым** `Stacks`, `Mathf.Max(0,…)` маскирует, но съедает вклад **второго** источника щита | `EffectSystem.cs:375-406`, `ShieldComponent.cs:27` | ✅ исправлен §2.5 | P0 | Claude Opus (только она) |
| B2 | Рестак Dodge бесплатно перезаряжает все заряды негейта: `OnApply` безусловно `new int[maxCharges]`, `Reapply` его зовёт. Нюанс: только `Stack`/`StackAndRefresh`, чистый `Refresh` не триггерит | `DodgeComponent.cs:27`, `EffectSystem.cs:389-406` | ✅ исправлен §2.5 | P0 | Claude Opus |
| B3 | Корень B1/B2: `Reapply` = слепой `OnExpire→OnApply` ради смены числа стаков; per-component state живёт в общем `RuntimeEffect`. Фикс — `OnStacksChanged(old,new)` + вынести state | `EffectSystem.cs:389-406`, `RuntimeEffect` | ✅ исправлен §2.5 (дельта-шов; вынос state отложен) | P0 | Claude Opus |
| B4 | Kite-поля мёртвые: `MoveKite` использует магическое `range*0.6f` + `AttackRange`, а `Kite.FleeDist`/`FallbackDist` из `AIProfile` нигде не читаются. Данные выглядят рабочими — но no-op | `MovementSystem.cs:85-100`, `AIProfile` | ✅ исправлен §2.5 | P1 | GPT-5.6 |
| B5 | Broad-phase линейного запроса теряет цели в дальних углах: радиус `length` вместо `length+halfWidth` → юнит на `(≈length, ≈halfWidth)` отсекается до narrow-phase | `CombatSimulation.QueryUnitsInLine` (~`:295`) | ✅ исправлен §2.5 | P1 | Claude Opus |
| B6 | `DrainEventQueue` после капа `MaxEventsPerDrain=512` делает silent `Clear()` — исход боя зависит от «уложились ли в 512». На текущем контенте 512/тик недостижимо → future-major, не critical-сейчас. Фикс: dev-assert + лог | `CombatSimulation.cs:441` | ✅ исправлен §2.5 | P1 | Grok, GPT |
| B7 | Iron Spearman: `_resourceType: None`, но `_resourceOnHit:5`, `MaxResource:30`, активка стоит 30 — противоречивый контракт ресурса | `IronSpearman.asset` | ✅ исправлен §2.5 (Rage) | P1 | GPT-5.6 |

**Консистентность данных (ретрофит дёшев, пока ассетов мало):**

| ID | Находка | Статус | Приоритет |
|---|---|---|---|
| C1 | Seed боя через `UnityEngine.Random`+`UtcNow` (`CombatLifetimeScope.cs:113-117`); нарушает «рандом только через `IRngService`»; блокирует репро/реплей. `gm_rng_seed` — stub (только лог) | ✅ (TODO в коде) | P1 |
| C2 | Нет `OnValidate`/content-validator на SO — битый контент проходит молча (Kite `Fallback>Flee`, resource/cost, диапазоны 0..1, uniq id) | ✅ | P1 (расширяет [[#3.5 Валидация контента эффектов]]) |
| C3 | `armorK` в двух местах: сериализован на `CombatLifetimeScope` (`:24`) и в `StatsConfig`; оба =100, ошибка скрыта | ✅ | P2 |
| C4 | buff/debuff смоделирован трижды (`EffectPolarity` + `EffectTag.Buff/Debuff` + `DispelTargetPolarity`) — два источника правды на ассете | ✅ | P2 |
| C5 | Два enum таргетинга (`AbilityTargetMode` vs `TargetingMode`) + устаревший докблок | ✅ | P2 |
| C6 | Две модели скейла: `ScalableValue` (эффекты) vs сырые `_damageMultiplier/_healFlat` (активки) — payload активок не переиспользует шов | ✅ | P2 |

**Гигиена / мёртвый код / швы:**

| ID | Находка | Статус | Приоритет |
|---|---|---|---|
| H1 | Мёртвый `DamageNumber`+`DamageNumberSpawner` (внутри — set через рефлексию, докстринг врёт про LitMotion); живой путь — `FloatingText`+пул | ✅ | P2 |
| H2 | `ISimEvent` — маркер без реализаций (`CombatEventData` его не реализует) | ✅ | P2 |
| H3 | Двойной канал наружу: C#-events + MessagePipe; контракты событий объявлены в тяжёлом `Guildmaster.Presentation` → вынести в нейтральный слой | ✅ | P2 |
| H4 | `alpha` интерполяции frame-based | 🔗 [[#3.3 Грубый alpha интерполяции презентации]] (подтверждён 4/5) | P1 |
| H5 | `Stats.RebuildCache` — 3 массива на пересборку | 🔗 [[#3.2 Аллокации в Stats.RebuildCache]] (подтверждён) | P2 |
| H6 | `GlobalMessagePipe.SetProvider` — мёртвая глобальная поверхность (никто не читает) | ✅ | P2 |
| H7 | `RuntimeUnit` — открытый мешок mutable-полей во view (инвариант «view только читает» держится на дисциплине) | ✅ | P3 |
| H8 | `MarkTransferComponent` аллоцирует `new List<RuntimeUnit>` на каждую смерть носителя | ✅ | P2 (по касанию Следопыта) |
| H9 | Cooldown активок во float-секундах (`AbilitySystem`), а не int-тиках — дрейф из принятой tick-дисциплины | ✅ | P3 |
| H10 | ~1 ГБ мусорных worktrees в `.claude/worktrees/` (detached, от прошлых сессий) | 🔍 | P2 |

**Инфраструктура / инструменты:**

| ID | Находка | Статус | Приоритет |
|---|---|---|---|
| I1 | `run-tests.ps1` хардкодит Unity `6000.0.23f1`, проект на `6000.4.8f1` → локальный прогон падает `Unity not found`. Читать версию из `ProjectVersion.txt` | ✅ исправлен §2.5 | P0 (5 мин) |
| I2 | Билд первой сценой ставит `CoreScene` (`EditorBuildSettings`), GPT утверждает что она пустая, а root — в `BattleScene` → boot-flow не замкнут. Запуск из `BattleScene` — осознанный dev-harness | ✅ снят 2026-07-26: `CoreScene` держит `[Root]` и `[Bootstrap]`, обе игровые сцены грузятся с бута — flow замкнут ([[tech/10-reference/scenes|Reference - Scenes]]) | P3 |

**Устаревшее в аудитах / неверное на текущем дереве:**

| ID | Клейм | Реальность |
|---|---|---|
| X1 | «EditMode красный, падает `SerializeReferenceSpikeTests` из-за пути `Assets/Tests/EditMode`» (GPT, Codex) | ✅ **GPT/Codex правы на `fd858f8`** — тест молча падал (путь стал stale после переноса в `143535d`). Починен в незакоммиченной арена-работе (`:20` теперь `Assets/_Project/Tests/…`, suite 184/184). Т.е. старый клейм «171/171 green» был неверен; текущее рабочее дерево зелёное |
| X2 | «Арены нет в коде, `±200` хардкод, spawn из кода» (Composer) | ⚠️ Арена закодирована в рабочем дереве (uncommitted): `ArenaLayoutData`/`ArenaBounds`/`DeploymentService` |
| X3 | `§3.4` этого дока ссылается на `TargetingSystem` | ⚠️ Класс удалён; таргетинг теперь в `ProfileBrain`. Дока отстаёт (общая заметка GPT: часть вики устарела — tick order без Brain/Displacement, путь тестов `Assets/Tests`) |

**Сетевой долг** (host-auth vs lockstep-наследие `NetworkCommandRelay`, authority-гейт тика, полный порядок команд `(tick,player,seq)`, float-checksum) — все 5 подтвердили; относится к [[#4. Главная будущая таска (вынесено отдельно)]], не трогаем в одиночке.

---

## 4. Главная будущая таска (вынесено отдельно)

> 🎯 **Сетевой кооп — host-authoritative (Фаза MP).** Репликация состояния юнитов через NGO, клиент рендерит из реплик-состояния, гейт тика на хосте, релей команд для полного ввода, синхрон паузы, late-join/реконнект, сид боя от хоста. Полный состав — [[tech/20-explanation/netcode|Explanation - Netcode]] §4. Сейчас не трогаем (в одиночке сеть спит).

---

## 5. Как пополнять этот документ

- Принял техническое решение с развилкой → строка в §1.
- Починил что-то заметное → строка в §2 (changelog с датой).
- Заметил долг, который не чинишь сейчас → пункт в §3 с триггером «когда чинить».
- Не превращай в свалку мелочей: сюда — то, что переживёт текущую сессию и важно будущему-тебе.
