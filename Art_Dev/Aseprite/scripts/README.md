# Aseprite scripts — Guildmaster

Скрипты для пайплайна арта. Хранятся в репо; в Aseprite их нужно один раз подключить.

## Установка

1. В Aseprite: **File → Scripts → Open Scripts Folder**
   (обычно `%AppData%\Aseprite\scripts\`).
2. Скопируй сюда содержимое этой папки **или** сделай directory junction:

```powershell
# из корня репо
$src = (Resolve-Path ".\Aseprite\scripts").Path
$dst = Join-Path $env:APPDATA "Aseprite\scripts\Guildmaster"
cmd /c mklink /J "$dst" "$src"
```

3. **File → Scripts → Rescan Scripts Folder**
4. Запуск из **File → Scripts → Guildmaster → …**

`.aseprite` для PNG-экспорта в Unity должен быть **сохранён** внутри репозитория (чтобы найти `Assets/_Project`).

---

## Конвенция слоёв (оба скрипта)

| Имя | Поведение |
|---|---|
| Обычный image-слой | Экспортируется как есть |
| Группа без префикса (`Arm`, `Leg`) | Дети / группа сохраняются |
| Группы **`Arm` / `Leg`** (только PSD) | Дублируются → `Arm (left)` + `Arm (right)` (и то же для Leg). Копии одинаковые — раскладку/зеркала в Photoshop |
| Группа `@Sword` | Видимые дети → один слой `Sword` |
| `#Guide`, `_ref` | Пропуск / удаление |

---

## Export Bone PSD → Photoshop → Unity rig

**Скрипт:** `export_bone_psd.lua`

Для костяной анимации (Character Rig). Пишет **`.psd`** (не `.psb` — Aseprite PSB не умеет).

1. Клон спрайта: дропает `#`/`_`, flatten `@Group`.
2. Полный canvas (без trim).
3. Nearest scale **×10 по умолчанию** (как у PNG-экспорта; в диалоге можно сменить).
4. PSD через vendored [Tsukina Export as PSD](https://github.com/Tsukina-7mochi/aseprite-scripts/tree/master/psd) (`vendor/export_as_psd.lua`).

**Дальше руками:**

1. Открыть `.psd` в Photoshop.
2. При необходимости поправить раскладку / Image Size.
3. **Save As → Large Document Format (.psb)**, Maximize Compatibility.
4. В Unity: Multiple + Mosaic + **Character Rig** + **Use Layer Grouping** → Skinning Editor.

```powershell
& "C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe" -b `
  "Aseprite\Bone Animations\Bone Animation Sprites - Standart.aseprite" `
  --script "Aseprite\scripts\export_bone_psd.lua" `
  --script-param "out=Aseprite\Bone Animations\Bone Animation Sprites - Standart.psd"
```

---

## Export Bone Parts → Unity PNG parts

**Скрипт:** `export_bone_parts.lua`

Отдельный путь: trim → nearest ×10 → PNG в  
`Assets/_Project/Art/Sprites/Bone Animations/<имя>/`  
(для каталога частей / свопа, **не** для Character Rig).

Unity: preset `BonePartSprite` (PPU 1000, Point) + `BonePartSpritePostprocessor`.

```powershell
& "C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe" -b `
  "Aseprite\Bone Animations\Bone Animation Sprites - Standart.aseprite" `
  --script "Aseprite\scripts\export_bone_parts.lua"
```
