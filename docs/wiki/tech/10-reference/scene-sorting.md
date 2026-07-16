---
title: "Reference - Scene & Sorting"
order: 60
status: needs_review
updated: 2026-07-16
---

# 17. Структура сцены и порядок отрисовки (конвенции)

Как устроена иерархия боевой сцены и как назначается порядок отрисовки спрайтов/мировых UI. Цель — чтобы «что за что и где» читалось с одного взгляда, а глубина отрисовки не зависела от случая.

Актуально для `Assets/_Project/Scenes/BattleScene.unity`. Настроено в ветке `feat/sprite-sorting-and-scene-tidy` (2026-07-10).

---

## 1. Иерархия сцены

Корень сцены группируется пустышками-«папками». Живые системы помечаются `[Brackets]`.

```
=== SYSTEMS ===        DI-скоупы и презентеры
    [Root]             RootLifetimeScope   (сессионный DI)
    [Combat]           CombatLifetimeScope (DI одного боя)
    [Presenter]        CombatPresenter
=== CAMERA ===
    [Camera]           CameraModeController
    Main Camera        Camera + CinemachineBrain + FMOD listener
    CM Action / CM Overview / CM Dev    Cinemachine-камеры (3 режима, вики «16»)
    CombatFocusTarget  цель слежения камеры
=== ARENA ===
    Arena Layout       ArenaLayoutAuthoring (данные арены, вики «15»)
    Arena Ground (Temp)  временная подложка пола (SpriteRenderer)
=== UI ===
    EventSystem
=== DEV ===            дев-инструменты
    [DebugDraw]        CombatDebugDraw + CombatAreaFlash
    [DebugView]        CombatUnitDebugView
    [DevCommands]      GuildmasterCommands
Quantum Console        (в корне — умеет DontDestroyOnLoad себя)
```

**Правила:**

- **Папки** — пустые GameObject в позиции `(0,0,0)`, identity-rotation, scale `1`. Только для визуальной группировки. Имя в стиле `=== NAME ===`.
- **`[Brackets]`** — живые системные объекты (DI-скоупы, презентеры, дев-инструменты). Отличаются от папок-разделителей.
- **Не давать объектам имя, совпадающее с именем сцены** (`BattleScene` → путаница; переименовано в `Arena Layout`).
- **Объекты с `DontDestroyOnLoad`** (напр. Quantum Console) держать в корне — их нельзя парентить, иначе Unity не сохранит их между сценами.

**Почему безопасно парентить DI-скоупы:** VContainer определяет родительский скоуп **не** по transform-иерархии (в отличие от Zenject), а через `parentReference` / `VContainerSettings`. `RegisterComponentInHierarchy<T>()` сканирует всю сцену рекурсивно — дети папок находятся. Поэтому группировка по папкам на DI не влияет.

---

## 2. Порядок отрисовки (2D sorting)

Порядок для `SpriteRenderer` и world-space `Canvas` определяется тремя уровнями, **именно в этом приоритете**:

1. **Sorting Layer** — грубые полосы. `Project Settings → Tags and Layers → Sorting Layers`.
2. **Order in Layer** (`int`) — тонкая сортировка внутри слоя. На компоненте рендерера/Canvas.
3. **Позиция по оси сортировки** — тай-брейк при равных слое+order (см. §3, Y-сортировка).

### Стек слоёв (задний → передний)

| Sorting Layer | Что здесь | Примеры |
|---|---|---|
| `Default` | системный, не использовать для контента | — |
| `Background` | пол/подложка арены | Arena Ground |
| `GroundFX` | телеграфы AoE, декали под ногами | (на будущее) |
| `Units` | спрайты юнитов | UnitView |
| `OverheadFX` | искры удара, снаряды, статусы над головой | (на будущее) |
| `WorldUI` | мировые HP-бары, имена, damage numbers | HealthBar, NameLabel, FloatingText |

Экранный uGUI (Screen Space Overlay) рисуется поверх всего этого отдельно — в стек не входит.

---

## 3. Y-сортировка (глубина по экрану)

Чтобы юнит ниже по экрану перекрывал того, кто выше (естественная глубина):

`Project Settings → Graphics → Transparency Sort Mode = Custom Axis`, ось `(0, 1, 0)`.

Тогда всё на одном слое (`Units`) авто-сортируется по Y без ручного Z/Order. Держать юнитов на `Z = 0`.

> **На будущее (по мере появления арта):** для сортировки «по ногам» ставить пивот спрайта в основание и `Sprite Sort Point = Pivot` на `SpriteRenderer` (сейчас `Center`).

---

## 4. Назначение слоёв по префабам

| Префаб | Компонент | Sorting Layer | Order |
|---|---|---|---|
| `UnitView.prefab` | SpriteRenderer | `Units` | 0 |
| `UI/HealthBar.prefab` | Canvas (world-space) | `WorldUI` | 0 |
| `UI/NameLabel.prefab` | Canvas (world-space) | `WorldUI` | 1 |
| `UI/FloatingText.prefab` | Canvas (world-space) | `WorldUI` | 10 |

> HP-бар/имя/damage numbers — это **world-space uGUI `Canvas`** (`Image.Filled`), не спрайты. Порядок задаётся на `Canvas` (`sortingLayerName` / `sortingOrder`), и они варятся в той же системе сортировки, что и `SpriteRenderer`.

---

## 5. Открытый вопрос: dev-инструменты и билд

Группа `=== DEV ===` **пока НЕ помечена тегом `EditorOnly`**, хотя логически должна вырезаться из билда.

Причина: `CombatLifetimeScope.RegisterPresentation()` регистрирует `CombatDebugDraw` и `CombatAreaFlash` через `RegisterComponentInHierarchy<>()` **безусловно**. Если пометить `[DebugDraw]` как `EditorOnly`, в билде объект исчезнет, а регистрация будет его искать → исключение при старте боя.

**Что нужно сделать (отдельная задача):** развязать debug-draw с обязательной регистрацией — например, регистрировать эти компоненты только если они найдены (как уже сделано для камеры: `if (FindFirstObjectByType<...>() != null)`), либо вынести дев-презентацию в отдельный опциональный скоуп. После этого `[DebugDraw]`/`[DebugView]`/`[DevCommands]` можно честно пометить `EditorOnly`.
