# Aseprite scripts — Guildmaster

Скрипты для пайплайна арта. Хранятся в репо; в Aseprite их нужно один раз подключить.

## Установка

1. В Aseprite: **File → Scripts → Open Scripts Folder**
   (обычно `%AppData%\Aseprite\scripts\`).
2. Скопируй сюда `export_bone_parts.lua` **или** сделай directory junction / symlink на эту папку:

```powershell
# из корня репо (PowerShell от админа не нужен для junction в AppData)
$src = (Resolve-Path ".\Aseprite\scripts").Path
$dst = Join-Path $env:APPDATA "Aseprite\scripts\Guildmaster"
cmd /c mklink /J "$dst" "$src"
```

3. **File → Scripts → Rescan Scripts Folder**
4. Запуск: **File → Scripts → Guildmaster → export_bone_parts**
   (или просто `export_bone_parts`, если файл лежит в корне scripts).

`.aseprite` должен быть **сохранён** внутри репозитория (например `Aseprite/Bone Animations/…`), чтобы скрипт нашёл `Assets/_Project`.

## Export Bone Parts

Экспорт частей для Unity 2D bone / Skinning:

1. Trim по непрозрачным пикселям слоя (не по размеру canvas).
2. Nearest-neighbor upscale (по умолчанию ×10).
3. PNG с заменой в:

`Assets/_Project/Art/Sprites/Bone Animations/<имя-файла-без-расширения>/`

Unity импортирует их через preset `BonePartSprite` (Point, Uncompressed, PPU = 100×scale → 1000 при ×10).
Автоприменение на первом импорте — `BonePartSpritePostprocessor` + запись в Preset Manager.
Повторный экспорт PNG сохраняет `.meta` (GUID, пивоты, ручные правки).

### Конвенция слоёв / групп

| Имя | Поведение |
|---|---|
| Обычный image-слой | Один PNG с тем же именем (`Head.png`, `Leg (Top).png`) |
| Группа без префикса (`Arm`, `Leg`) | Рекурсия: экспортируются дочерние слои |
| Группа `@Sword` | Все **видимые** дочерние слои сливаются в один `Sword.png` |
| `#Guide`, `_ref` | Пропуск (гайды / референсы) |

Новый обвес без правки скрипта: заведи группу `@Bow` / `@Axe` / `@Cloak`.

### CLI (smoke / batch)

```powershell
& "C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe" -b `
  "Aseprite\Bone Animations\Bone Animation Sprites - Standart.aseprite" `
  --script "Aseprite\scripts\export_bone_parts.lua"
```

В batch-режиме диалог не показывается — scale = 10.
