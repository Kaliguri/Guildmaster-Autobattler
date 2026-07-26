---
title: "Reference - Scenes"
order: 58
status: ready
updated: 2026-07-26
---

# Сцены проекта: что где живёт

Карта сцен «как есть» на 2026-07-26. Отвечает на вопрос «что за что отвечает и почему их
столько»: сцен в проекте **пять**, из них три в билде, одна — дев-стенд, одна — личная песочница.

Порядок отрисовки и конвенции иерархии — отдельно, в [[tech/10-reference/scene-sorting|Scene & Sorting]].

---

## 1. Сводка

| Сцена | В билде | Когда грузится | Что держит |
|---|---|---|---|
| `CoreScene` | ✅ первая | стартовая | сессионный DI, рантайм-UI, бут |
| `WorldScene` | ✅ | аддитивно на буте, **не выгружается** | камера-риг, арена, карта акта, фон меню |
| `CombatSystemsScene` | ✅ | аддитивно на буте, **не выгружается** | боевой DI-скоуп, презентер боя, дев-инструменты |
| `UiPreview` | ❌ | вручную, меню `Alebardium/UI Preview` | хост дев-стенда экранов |
| `<Имя> Scene For Tests` | ❌ | вручную | личная песочница разработчика, **вне git** |

Обе игровые сцены грузит `SceneLoader` в `GameBootstrap.StartBootAsync` — один раз за сессию.
Выгрузки в `ISceneLoader` **нет вовсе**: бой это команда в живую симуляцию
(`IBattleSession.RequestLaunch`), а не загрузка сцены на каждый узел.

---

## 2. `CoreScene` — вход и сессионный слой

Первая в `EditorBuildSettings`, живёт до конца сессии.

```
[Root]              RootLifetimeScope   — сессионный DI (см. [[tech/20-explanation/di-events|DI & Events]])
[Bootstrap]         GameBootstrap       — точка входа: грузит мир, потом боевые системы
UI Root             UIDocument + UiRootBootstrap — весь рантайм-UI (UI Toolkit)
Background Camera   заливка кадра цветом ink; culling mask пуст
EventSystem
Audio Listener
Vfx_HitSpark ×2     пул-заготовки VFX
```

Смотреть в этой сцене вне play-режима нечего: мира и арены здесь нет, они приезжают
аддитивно на старте.

## 3. `WorldScene` — персистентный мир

Аддитивная, грузится первой из двух и живёт всю сессию: вне боя показывает арену
(карта, инвентарь), в бою тот же риг переиспользуется. Скоуп `[World]`
(`WorldLifetimeScope`) — дочерний к `RootLifetimeScope`.

```
[World]                 WorldLifetimeScope (parent: RootLifetimeScope)
=== CAMERA ===
    [Camera]            CameraModeController — 4 режима, кламп из данных арены
    Main Camera         Camera + CinemachineBrain + FMOD listener
    CM Action / CM Overview / CM Map / CM Dev    Cinemachine-камеры режимов
    CombatFocusTarget   цель слежения боевой камеры
=== ARENA ===
    Arena Layout        ArenaLayoutAuthoring — данные арены (зоны, кламп)
    LAYER 1             тайлсет пола (+ grayscale-дубль под тест-зону)
    Arena Ground (Temp) временная подложка пола
Test Zone Arena Skin    свап цветного/серого пола по тумблеру тест-зоны
=== MAP ===             world-слой карты акта (WorldMapView) + Map Post FX
=== MENU BACKDROP ===   стол за главным меню и экранами (MenuBackdropView)
```

## 4. `CombatSystemsScene` — боевые системы

Бывшая `BattleScene`; переименована 2026-07-26, потому что имя обещало «сцену боя», а сцена
держит **системы** и живёт всю сессию. Скоуп `[Combat]` (`CombatLifetimeScope`) — дочерний к
`WorldLifetimeScope`, поэтому камера и арена резолвятся из предка, без дублей `Main Camera`.

```
=== SYSTEMS ===
    [Combat]            CombatLifetimeScope (parent: WorldLifetimeScope)
    [Presenter]         CombatPresenter — мост sim → визуал
=== DEV ===
    [DebugDraw]         CombatDebugDraw + CombatAreaFlash
    [DebugView]         CombatUnitDebugView
    [DevCommands]       GuildmasterCommands (Quantum Console)
    Dev Encounter Panel F2-панель запуска боёв
=== UI ===
Global Post FX Volume
```

## 5. `UiPreview` — дев-стенд экранов

Внутри только `UI Preview Host` (+ камера). Открывается пунктами меню
`Alebardium/UI Preview/*` (`UiPreviewMenu`): меню кладёт id экрана в `SessionState`, грузит
сцену **single** и входит в play; `UiPreviewHost` собирает экран через `UiPreviewCatalog` на
стендовых данных — без бута, боя и забега.

Витрина компонентов живёт **здесь** (`UiPreviewCatalog.BuildGallery`) и больше нигде: вторая,
на UXML в отдельной сцене, снесена 2026-07-26 — витрина, чьё содержимое копия компонентов в
разметке, умеет с ними разойтись, и расходилась.

---

## 6. Чего в проекте больше нет

| Что | Почему снесено |
|---|---|
| `BootScene` | пустышка с одной камерой; не в билде, ни одной ссылки в коде — точка входа это `CoreScene` |
| `UiGallery` (+ `UiGalleryScreen.uxml/.uss`) | вторая витрина компонентов, разошедшаяся с кодовой |
| демо-сцены паков (Honeti, Cainos, Shapes) | ~29 МБ в каждом клоне; паки на свои демо не ссылаются |

Личные песочницы (`* Scene For Tests.unity`) игнорируются гитом: содержимое меняется под
сиюминутный опыт владельца и мержится хуже всего.
