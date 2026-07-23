# Промпты генерации персонажей (Nano Banana Pro)

Рабочий файл под пайплайн «нейросеть → части → скелет в Unity 2D Animation».
Правится по ходу итераций. Промпты — на английском, модели с ним точнее.

**Как пользоваться:** пасс А и пасс Б идут **в одной сессии** подряд, чтобы модель
помнила персонажа по тегу. Между ними можно вставлять короткие правки (см. низ файла).

---

## Пасс А — эталон

Персонаж целиком. Отвечает только за **ракурс и стиль**. Резать его не будем,
поэтому перекрытия тут допустимы.

```
Game asset: a single fantasy warrior character, full body.

IDENTITY TAG: "Vanguard-01" — use this name in all following requests.

VIEW: three-quarter view from a slightly elevated camera, as in a top-down
tactical RPG.

BODY ROTATION: the character's body is rotated 60 degrees to the right,
away from the camera. We see mostly his right side, with only part of the
chest still visible. This is NOT a front-facing view.

ASYMMETRY (important): the near shoulder is clearly closer to the camera
than the far shoulder. The far arm is partially hidden behind the torso.
The stance is asymmetric — weight on one leg, one foot ahead of the other.
The head faces the same direction as the body.

POSE: neutral idle stance, standing, relaxed. Calm and static — not an
action pose.

POSTURE: upright and straight. Head held up, spine straight. Not hunched,
not crouching, not leaning forward. Keep the body rotation described above
— straightening the posture must NOT turn the body toward the camera.

WEAPON: sword held down and away from the body, not crossing the legs
or the torso.

SINGLE FIGURE: output exactly ONE character in the frame. Do not produce
a turnaround, a character sheet, or multiple views. One figure only.

STYLE: stylized game character art. Bold black outline, flat colors, no
gradients. Bold simple shapes, strong readable silhouette, large clear
masses. Minimal small detail. Limited color palette.

LIGHTING: flat even lighting, no cast shadows, no rim light.

BACKGROUND: solid flat magenta (#FF00FF), empty.

NEGATIVE: no front-facing pose, no symmetric stance, no arms hanging
identically on both sides, no full side profile, no action pose, no
turnaround, no multiple views, no character sheet, no hunched posture,
no text, no watermark, no background elements, no photorealism.
```

**Ручка угла:** `60 degrees` → пробовать 45 / 60 / 75, выбирать по силуэту
в реальном масштабе на арене, а не по большой картинке.

---

## Пасс Б — разборка на части

Следом, в той же сессии. Вот это уже режется и идёт в PSB.

```
Now output "Vanguard-01" as an EXPLODED VIEW for skeletal rigging.

Show the SAME character, in the SAME three-quarter angle and the SAME
pose, but with his body parts pulled apart from one another, floating in
place with visible gaps between them. A disassembled figure — the parts
stay roughly where they belong, just separated.

CRITICAL — ORIENTATION: every part keeps the EXACT orientation it has on
the character. Do NOT re-orient parts, do NOT rotate them, do NOT redraw
them as standalone items or inventory icons. This is a dissection of one
character, not an item catalog.

CRITICAL — COMPLETENESS: draw every part complete and whole, including
areas normally hidden behind the body, the cloak or the armor.

CRITICAL — SEGMENTATION: limbs must be SPLIT AT THE JOINTS, not drawn as
whole limbs. An arm is three separate parts (upper arm, forearm, hand),
a leg is two (thigh, shin with foot). Split at elbow, wrist and knee.

CRITICAL — EQUIPMENT: weapons and shield are SEPARATE objects, never
attached to the body. Hands are drawn EMPTY, closed in a fist, gripping
nothing. The sword is drawn complete, including the full grip and pommel
that would be hidden inside the hand.

PARTS (18 total, all separated):
head, torso, pelvis,
near upper arm, near forearm, near hand (empty fist),
far upper arm, far forearm, far hand (empty fist),
near thigh, near shin with foot,
far thigh, far shin with foot,
cloak, sword, shield.

Keep the exact same style, colors and proportions as the character above.

BACKGROUND: solid flat magenta (#FF00FF). No shadows, no labels, no text,
no grid, no cells.
```

Подписи и сетку убрали намеренно: они переключают модель в режим
«каталог предметов», где каждая деталь рисуется в своём удобном ракурсе.

---

## Короткие правки (в той же сессии, между пассами)

Дешевле, чем перегенерировать с нуля — модель держит персонажа в памяти.

**Осанка и меч:**
```
Same character, same angle, same style. Fix the posture: stand upright,
head up, shoulders back, spine straight — a calm idle stance, not hunched.
Move the sword down and away from the body so it does not cross the legs.
One figure only.
```

**Довернуть корпус:**
```
Same character, same style. Rotate the body further to the right, to about
75 degrees. Push the near shoulder toward the camera and hide more of the
far arm behind the torso.
```

**Разделить ноги (они слипаются в один кусок):**
```
Redraw only the legs. Show four separate parts, pulled apart with gaps:
near thigh, near shin with foot, far thigh, far shin with foot.
Each one complete and whole, in the same orientation and style as before.
```

**Отделить оружие от кисти (меч прирастает к руке):**
```
Redraw only two parts, separated: the "near hand" and the "sword".

The hand must be EMPTY — closed in a fist, gripping nothing, with the
sword removed from it. Draw the fist complete and whole.

The sword must be a separate complete object, including the full grip
and pommel that were hidden inside the hand.

Same orientation, same style and colors as before.
```

Правило: **всё, что может меняться, — отдельная деталь** (оружие, щит, шлем).
Это точки свопа обвеса через Sprite Library, ради них вся затея со скелеткой.
Рукоять и навершие обязательно дорисовать целиком, иначе при смене оружия
вылезет дырка там, где их закрывал кулак.

**Переделать одну деталь после пасса Б:**
```
Redraw only the "far thigh" part, larger and complete, as if nothing
covered it. Same style and colors.
```

---

## Журнал итераций

| Дата | Что меняли | Результат |
|---|---|---|
| 2026-07-20 | Первая версия: строгий боковой профиль | Провал — модель дала 3/4, конечности перекрыты. Заодно выяснилось, что строгий профиль нам и не нужен: наша проекция — low top-down/east |
| 2026-07-20 | 3/4 + `south-east` | Слишком фронтально, симметричная стойка |
| 2026-07-20 | Градусы + блок ASYMMETRY | Ракурс поймали. Побочка — модель сделала turnaround из двух фигур |
| 2026-07-20 | + SINGLE FIGURE, POSTURE, WEAPON | `shoulders back` развернуло корпус обратно к камере — ракурс потеряли |
| 2026-07-20 | Пасс Б через сетку с подписями | Провал: детали в произвольных ракурсах (торс со спины, шлем в профиль). Сетка+подписи = жанр «каталог предметов» |
| 2026-07-20 | Пасс Б переписан на exploded view, `shoulders back` убрано | **Сработало.** Эталон — правильный разворот и осанка; разборка — части в едином ракурсе, скрытое дорисовано |

**Что всплыло на первом персонаже и уже вкачено в основной промпт пасса Б:**
конечности приходили цельными (не разбиты по суставам), ноги слипались в один
кусок, меч прирастал к кисти. Блоки SEGMENTATION и EQUIPMENT добавлены именно
поэтому — на следующем персонаже этих граблей быть не должно.

Если что-то из этого всё же вылезет — точечные правки ниже дожимают конкретную
деталь, не перегенерируя лист целиком.

---

## Что дальше по пайплайну

Полный маршрут: генерация → Palette Remapper (наша гамма) → нарезка на слои →
PSB (Save As Large Document Format, Maximize Compatibility) → импорт в Unity
(Multiple + Mosaic + Character Rig + Use Layer Grouping) → Skinning Editor
(кости у суставов, Auto Geometry как черновик, **не забыть Apply**) → idle.

Первого персонажа режем **руками** — чтобы понять, какая раскладка на части
реально нужна. Автотулы (See-through, ImageToLayers) — со второго.
