# Заход: фигура человека тира 128 → риг

Временный журнал живого захода. Когда риг соберётся и заработает, ценное уезжает в
`docs/wiki/tech/00-meta/journal/` и в размерную сетку, а этот файл удаляется.

Источник фигуры — `Aseprite/Bone Animations/Pixel Hero - Parts - Workbench.aseprite`,
группа `Human (128x128)`. Замеры: фигура 46×127, подошва на y=152, макушка черепа 25.

## Фаза 1 — экспорт частей (СДЕЛАНО)

```powershell
& "C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe" -b `
  "C:\My Projects\Guildmaster-Autobattler\Aseprite\Bone Animations\Pixel Hero - Parts - Workbench.aseprite" `
  --script-param group="Human (128x128)" --script-param subdir="Human 128" `
  --script "Aseprite\scripts\export_bone_parts.lua"
```

18 PNG в `Assets/_Project/Art/Sprites/Bone Animations/Human 128/`, масштаб ×10 nearest.

Что пришлось починить в экспортёре по дороге, каждое — грабли на будущее:

- **Расширения Aseprite роняют batch.** Установленное расширение `pixellab` падает на
  `dlg` = nil, когда UI недоступен, и убивает весь запуск. Лечится подменой `APPDATA` на пустую
  папку: чистый профиль не грузит расширений.
- **`app.alert` без UI молчит.** Скрипт выходил через `return app.alert(...)` и выглядел как
  «сработал, но ничего не сделал», код возврата 0. Заменено на `fail()`, который печатает.
- **Путь файла в CLI приходит относительным** — поиск корня проекта уходил в пустоту. Достраиваем
  от `app.fs.currentPath`; `app.fs.isAbsolutePath` использовать НЕЛЬЗЯ, этого поля нет в части
  версий API и обращение роняет скрипт.
- **Имена частей левой и правой конечности совпадают** (`Leg (Down)` в обеих группах), и второй
  PNG молча затирал первый: было 18 экспортов и 12 файлов. Теперь при коллизии дописывается имя
  родительской группы (`Leg (Right) - Leg (Down).png`), у файлов без коллизий имя не меняется.
- **Фильтр по группе обязателен:** без `--script-param group` верстак выгружает и снятые болванки,
  и реф-борды.

## Фаза 2 — импорт и риг (В РАБОТЕ)

Порядок шагов:

1. Настройки импорта спрайтов — рядом лежат `BonePartSprite.preset` и `BoneParts.spriteatlasv2`,
   проверить пивоты и PPU (арт ×10, `spritePixelsToUnits` у старых частей 1000).
2. Иерархия рига по конвенции `RigNaming`: `Rotation Point (…)` для суставов,
   `Visual Part (…)` для арта. Кисть — `Visual Part (Hand_Palm)` на существующем
   `Rotation Point (Grip)`, новой кости не нужно.
3. Пивоты в замеренные суставы (от подошвы): голеностоп 8, колено 32, таз 64, плечи 102,
   локоть 76, запястье 54, основание шеи 103.
4. Ордера по новой раскладке — см. журнал
   `2026-08-01-draw-order-is-keyed-per-renderer.md`: дальняя рука −12…−9, дальняя нога −8…−6,
   ближняя нога −5…−3, шея −1, торс 0, голова 4-5, ближняя рука 6-8, меч 9, ладонь 10, щит 11.
5. Перемерить `RigProfile` (текущий снят со старой болванки, где голова 90×130).

## Что остаётся за Максом

Три стыка резаны встык вместо перекрытия 6 px: плечо (`Shoulder`→`Top`), локоть (`Top`→`Down`),
голеностоп (`Leg (Down)`→`Boot`). Плюс сама ладонь ещё не нарисована — сейчас `Hand (Palm)` это
7×9 заготовка. Проверять командой:

```powershell
python scripts/aseprite_parts.py check "Aseprite/Bone Animations/Pixel Hero - Parts - Workbench.aseprite" "Human (128x128)" --chain "Hand (Shoulder)->Hand (Top)" "Hand (Top)->Hand (Down)" "Leg (Down)->Boot"
```
