**Статус:** идея / backlog (фаза 1 пробовали — откатили)

---

## Зачем

Клипы из Aseprite-экспорта (`Attack.anim` и родня) тащат много ключей, которые **не меняют картинку**: константные нули, пустые заглушки Unity и дубликаты одной кривой в нескольких YAML-секциях. Файл раздувается (~6400 строк), в Animation window шум.

Аудит на `Attack.anim` (после leg-tune коммита `8eda4f00`):

| Секция | Всего | Живые | Мёртвые |
|---|---:|---:|---:|
| `m_EditorCurves` | 65 | 20 | 45 констант |
| `m_EulerEditorCurves` | 45 | 0 | **45 пустых** (`m_Curve: []`) |
| `m_EulerCurves` | 13 | 10 | 3 константы |
| `m_PositionCurves` | 8 | 5 | 3 константы |

~390 editor keyframes, из них ~270 — константы.

Скрипты аудита: `scripts/analyze-attack-anim-keys.py`, `scripts/analyze-attack-anim-dupes.py`.

---

## Фаза 1 — безопасная (пробовали, откатили)

Скрипт: `scripts/prune-attack-anim-phase1.py`

Удалить:

1. **Всю секцию `m_EulerEditorCurves`** — 45 пустых блоков-заглушек.
2. **Константные нулевые `m_EditorCurves`** — оси, которые весь клип на 0 (`localEulerAnglesRaw.x/y`, `m_LocalPosition.z`, нулевые позиции на `Leg (Right)/Rotation Point` и т.п.). Без кривой Unity берёт префаб.
3. **Константные блоки в `m_EulerCurves` / `m_PositionCurves`** — репоз-поза, уже зашитая в `BoneUnit_Standart.prefab` (например `Leg_Down` Z = −32.83°, нулевые `Rotation Point`).

**Не трогать в фазе 1:** 3 константные editor-кривые с ненулевым rest (`Leg_Down` left Z, `Arm_Down` left pos).

Ожидаемый эффект: ~6400 → ~2700 строк, **движение без изменений**. Прогон дал именно это; после визуальной проверки решили **откатить** — вернуться к чистке позже.

---

## Фаза 2 — после Play-теста фазы 1

Удалить **целиком** `m_EulerCurves` и `m_PositionCurves`: они на 100% дублируют `m_EditorCurves` (rotation Z и position X/Y). Канон для Unity — `m_EditorCurves`.

---

## Фаза 3 — по желанию

- Схлопнуть plateau-ключи Aseprite (одинаковые значения подряд, ~12 лишних на живых кривых).
- Решить по мелкому джусу: `Arm (Left)` root position, `Leg (Right)/Leg_Down` rotation.
- Убрать 3 константные ненулевые editor-кривые — только если клип всегда на `BoneUnit_Standart`.

---

## Когда делать

- После стабилизации экспортного пайплайна Aseprite (чтобы не чистить после каждого реэкспорта вручную).
- Лучше встроить prune в экспорт-скрипт или post-process шаг в `Aseprite/scripts/`.
- Перед merge: Play Mode на `BoneUnit_Standart` — Idle + Attack, сравнить покадрово с тегом до чистки.

---

## Связанные скрипты

| Скрипт | Назначение |
|---|---|
| `scripts/prune-attack-anim-phase1.py` | Фаза 1 |
| `scripts/tune-attack-leg-motion.py` | Смягчение ног в Attack (уже в `8eda4f00`) |
| `scripts/retarget-left-leg-anim.py` | Ретаргет left leg после репоза костей |
| `scripts/analyze-attack-anim-keys.py` | Аудит констант / пустых кривых |
| `scripts/analyze-attack-anim-dupes.py` | Аудит дубликатов секций |

---

## Фундамент Run + Avatar/Masks (сделано)

Канон: `docs/wiki/gdd/10-vision/character-animation.md`. В GDD клип локомоции зовётся **Walk**; на стенде ассет называется **`Run.anim`** — не переименовывали.

| Ассет | Роль |
|---|---|
| `BoneUnit_StandartAvatar.asset` | Generic Avatar (`AvatarBuilder.BuildGenericAvatar`) — без него маски молчат |
| `Mask_BaseBody.mask` | локомоция: все кости (ноги/корпус/голова/руки) |
| `Mask_Arms.mask` | только руки + дети |
| `BoneUnit_Standart.controller` | Base: Idle↔Run по `Speed`; Arms Override: Empty→Attack по Trigger `Attack` |
| `Run.anim` | loop 0.5 с: ноги ±15°, Leg_Down ±8°, body bob, arm swing |

**Готча:** Empty на слое Arms с `writeDefaults=true` затирает руки Base. Сейчас Empty = `writeDefaults=false`.

Проверка в Play: `Speed=1` → Run, ноги качаются; `Attack` на ходу → руки бьют, ноги продолжают цикл.

---

## Боевой просмотр (WIP)

`UnitView_BoneStandart.prefab` — боевой UnitView с nested `BoneVisual` + `BoneUnit_Combat.controller` (Idle/Run/Attack под `UnitView.Play`).

`BaseRelic.asset` снова на `UnitView_BaseRelic` (guid `b878aa196bd029646a3f3d292a658691`). Для smoke временно можно переключить `_viewPrefab` на `UnitView_BoneStandart` (guid `cd42c7a258093ac4691913f833656755`).

R&D `BoneUnit_Standart.controller` с масками не трогали.
