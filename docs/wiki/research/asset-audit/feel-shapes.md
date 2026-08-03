---
title: "Аудит - Feel и Shapes"
status: ready
updated: 2026-07-20
---

> Приложение к [[tech/10-reference/asset-inventory|Reference - Asset Inventory]]. Собрано разведкой 2026-07-20.

## Feel и Shapes (ресурсы, не код)

Аудит от 2026-07-20, ветка `feat/act-map-overhaul`. Смотрели только СОДЕРЖИМОЕ (текстуры, материалы, шейдеры, префабы, шрифты), код обоих пакетов вне зоны.

Размеры на диске: `Assets/Feel/MMTools` — 42 МБ, `Assets/Feel/MMFeedbacks` — 5.3 МБ, `Assets/Shapes` — 11 МБ.
Важно: папки `FeelDemos`, `FeelDemosURP`, `FeelDemosHDRP` в проекте **пустые** (0 байт) — демо-сцены и их VFX-префабы при импорте отброшены. Значит готовых «эффектов из коробки» у Feel в проекте нет: есть сырьё (текстуры) и шейдеры, а не собранные партикл-системы.

---

### 1. Feel / MMTools — где что лежит

| Категория | Путь | Состав |
|---|---|---|
| Шумы | `Assets/Feel/MMTools/Accessories/MMVFX/MMNoise/` | 13 grayscale-текстур (512² и 1024²) |
| Градиентные рампы | `Assets/Feel/MMTools/Accessories/MMVFX/MMRamps/` | 8 штук, 256×256 |
| Кисти (мазки) | `Assets/Feel/MMTools/Accessories/MMVFX/MMBrushes/` | 6 штук, 512×512, tileable |
| Спрайты частиц | `Assets/Feel/MMTools/Accessories/MMVFX/MMParticles/` | 16 штук, 512×512, чёрный фон + белая форма |
| Bloom dirt (грязь линзы) | `Assets/Feel/MMTools/Accessories/MMVFX/MMBloomDirt/` | 4 штуки, 1920×1080 и 3840×2160, ЦВЕТНЫЕ |
| Ripple (искажение) | `Assets/Feel/MMTools/Accessories/MMVFX/MMRipple/` | 11 normal-map + 11 материалов + `MMRipple.prefab` |
| Палитра | `Assets/Feel/MMTools/Accessories/MMVFX/MMPalette/MMLowPolyPalette.png` | 512², атлас цветовых плашек для low-poly UV-развёрток |
| Шейдеры | `Assets/Feel/MMTools/Accessories/MMShaders/` | 15 `.shader` |
| Конус света | `Assets/Feel/MMTools/Accessories/MMVision/` | `MMConeOfLight.shader` + `ConeOfVisionAlpha.png` |
| Прототип-текстуры | `Assets/Feel/MMTools/Accessories/MMPrototypeTextures/` | 20 «крестики/точки/рамка/квадраты» ×5 цветов + 21 материал (BRP!) + чекер + пластик PBR |
| GUI-мелочь | `Assets/Feel/MMTools/Accessories/MMGUI/` | `MMFaderRoundMask.png`, `MMTools_GUI_1x1.png`, 2 материала маски для круглого фейдера |
| Прочее (служебное) | `MMSceneLoading/Sprites`, `MMAchievements/Sprites`, `MMDebugMenu/Sprites` | лоадер, ачивка, дебаг-меню; в игру не годятся |

Полезных VFX-**префабов** в Feel практически нет — единственный `MMVFX/MMRipple/MMRipple.prefab` (плоскость с ripple-материалом). Остальные префабы — это UI дебаг-меню, floating text (`MMFeedbacks/MMFeedbacks/MMFloatingText/Prefabs/*`, есть пиксельный вариант) и болванки Volume для URP/HDRP/PPv2 (`MMFeedbacks/MMFeedbacksForThirdParty/URP/Resources/MMDefaultURPVolume.prefab`).

---

### 2. Таблица шумов

Все — 8-битный PNG, `colortype=2` (RGB-контейнер), но фактически **grayscale**: R=G=B. У шумов в импорте `sRGBTexture: 0` (линейные — правильно для масок).

Столбец «tileable» — по замеру шва: сравнивал разницу крайних столбцов/строк против разницы двух далёких столбцов внутри картинки.

| Файл | Путь (`Assets/Feel/MMTools/Accessories/MMVFX/MMNoise/`) | Разрешение | Что на нём | Tileable | Где применим |
|---|---|---|---|---|---|
| MMCloudsNoise | `MMCloudsNoise.png` | 512² | Клубящиеся кучевые облака, мягкие пухлые комки, средний тон тёмный (~0.35), высокая деталь. Классический «дым/облака» | да (шов 2.2 против базы 25) | **УЖЕ В ДЕЛЕ** — фактура бумаги карты. Дым, дымка, потёки |
| MMPerlinNoise | `MMPerlinNoise.png` | 512² | Не «классический перлин»: мелкое высокочастотное зерно, почти белый шум с лёгким размытием, контраст высокий | да (5.0 / 47) | Плёночное зерно, шершавость бумаги, dissolve с грубым краем |
| MMSimplexNoise | `MMSimplexNoise.png` | 512² | Мягкий низкочастотный fBm — плавные пятна средней частоты, ровный серый средний тон. Самый «спокойный» из набора | да (0.8 / 55) | Дымка, медленное колыхание, вариация освещения, маска пятен |
| MMFlowNoise | `MMFlowNoise.png` | 512² | Волокнистые горизонтальные волны-струи, направленный «поток» слева направо. Светлый средний тон | да (3.2 / 49) | **Разводы бумаги/водяные знаки, потоки дыма, ленты пыли** |
| MMFireNoise | `MMFireNoise.png` | 1024² | Пузырчато-комковатый fBm, светлые прожилки-«перемычки» между тёмными кляксами. Плотный, детальный | да (2.3 / 58) | Огонь, пар, пузырящаяся масса, тяжёлые кляксы |
| MMFireDirectionalNoise | `MMFireDirectionalNoise.png` | 1024² | Резко-контрастные диагональные завихрения, «взбитая» турбулентность с направлением 45°. Почти чёрно-белый | да (4.5 / 88) | Резкий дым, ветер, полосы турбулентности |
| MMFireDirectionalAltNoise | `MMFireDirectionalAltNoise.png` | 1024² | Тот же тип, но низкоконтрастный (база разницы всего 15) — мягкий вариант | да | Слабое колыхание там, где «резкий» вариант слишком грубый |
| MMCellNoise | `MMCellNoise.png` | 1024² | Клеточная структура: светлые пузыри-ячейки, разделённые ЧЁРНЫМИ перемычками (инверсный воронуа). Похоже на пену/мыльные пузыри | да (4.6 / 62) | Трещины, кракелюр, чешуя, пена |
| MMVoronoiNoise | `MMVoronoiNoise.png` | 1024² | Классический воронуа: чёрные ядра ячеек, БЕЛЫЕ границы, плавный градиент к центру | да (1.4 / 67) | Магический круг, каустика, кристаллы, паутина трещин |
| MMBrushNoise | `MMBrushNoise.png` | 512² | Тёмные диагональные облака с рваной «мазанной» кромкой, средний тон очень тёмный (~0.2) | да (2.0 / 41) | Чернильные потёки, тени, маска затемнения |
| MMBlueNoise | `MMBlueNoise.png` | 512² | Синий шум: равномерно распределённые точки без кластеров, средний серый | по построению да; попиксельный шов метрику ломает | **Дизеринг, стохастическая прозрачность, сэмплинг тени** |
| MMBayerNoise | `MMBayerNoise.png` | 512² | Регулярная матрица Байера (упорядоченный дизер), крупная шашка ~32 px/клетка | по построению да | **Ordered dithering** — ретро-градиенты, пиксельная растяжка тона |
| MMWhiteNoise | `MMWhiteNoise.png` | 512² | Чистый белый шум, попиксельно | по построению | TV-помехи, зерно, случайные сиды |

Единственный шум с чёткой «тканой» структурой в наборе — `MMFlowNoise`; отдельной текстуры «холст/бумага» в Feel НЕТ.

---

### 3. Таблица рамп

Все 256×256, RGB, `sRGBTexture: 1`. Градиент строго по горизонтали (X = t), высота — просто протяжка. Пригодны как lookup-текстура для gradient map / toon-рампы / раскраски партиклов.

| Файл | Путь (`.../MMVFX/MMRamps/`) | Разрешение | Что на нём | Tileable | Где применим |
|---|---|---|---|---|---|
| MMRamp0 | `MMRamp0.png` | 256² | Тёмно-сливовый/винный → лососевый → кремово-бежевый. **Тёплая пергаментная гамма** | по X нет (края разные) | **Ремап карты: тени-чернила → бумага.** Gradient map для сепии |
| MMRamp1 | `MMRamp1.png` | 256² | Голубой → синий → чёрный провал → оранжевый → жёлто-белый. Резкий hot/cold | нет | Огонь/лёд, дуальный урон, тепловая карта |
| MMRamp2 | `MMRamp2.png` | 256² | Синий-стальной → тёмная середина → бледно-жёлтый → белый. «Ночь → свеча» | нет | Ночная сцена с тёплым источником; годится под «тёмный стол + лампа» |
| MMRamp3 | `MMRamp3.png` | 256² | 5 ступеней чистого серого от чёрного до белого (постеризация) | нет | Toon-рампа, ступенчатая тень, квантование тона |
| MMRamp4 | `MMRamp4.png` | 256² | Ровно половина чёрная, половина белая — жёсткий порог | нет | Cutout-маска, hard toon, wipe-переход |
| MMRamp5 | `MMRamp5.png` | 256² | 5 ступеней светлого серого (0.55 → 1.0), без чёрного | нет | Мягкая toon-рампа, лёгкое затенение |
| MMRamp6 | `MMRamp6.png` | 256² | Почти чёрный фиолетовый (долгая полка) → розовато-серый → светло-серый | нет | Глубокая тень с фиолетовым подтоном; атмосферный туман |
| MMRamp7 | `MMRamp7.png` | 256² | Полная радуга HSV | нет | Дебаг-визуализация, радужный/магический эффект |

---

### 4. Кисти и частицы

**Кисти** (`.../MMVFX/MMBrushes/MMBrush0..5.png`, 512², grayscale, ВСЕ tileable — швы 0.8–11 против базы 31–78):
- `MMBrush0` — постеризованные «континенты», крупные плоские пятна с рваной каёмкой, 5–6 тональных ступеней.
- `MMBrush1` — самый «сухая кисть»: диагональные щетинистые мазки с рваными царапинами и резким контрастом. Ближе всего к чернилам по сухой бумаге.
- `MMBrush4` — мягкие дымные клубы с зубчатым краем, низкий контраст (база 31 — самая ровная из шести).
- Остальные — вариации того же: постеризованный мазок разной крупности.

Все шесть — по сути стилизованные «нарисованные» шумы. Именно они, а не MMNoise, дают рукотворный/чернильный характер.

**Частицы** (`.../MMVFX/MMParticles/`, все 512², белая форма на ЧЁРНОМ фоне — то есть под аддитивный бленд, не под альфу):
`BlackSun`, `Bolt`, `Dust`, `Eclipse`, `Explosion`, `Flare`, `Flash`, `Gamma`, `Hit`, `Jab`, `Light`, `Slash`, `Smoke`, `Star`, `Storm`, `Whirlwind`.

Смотрел лично:
- `MMParticlesDust` — очень мягкое, почти неразличимое размытое пятно (пик всего ~0.25 яркости). Классическая пылинка/мягкий пуфф.
- `MMParticlesSmoke` — рваный клуб дыма с языками, средняя яркость ~0.3, ассиметричный.
- `MMParticlesLight` — жёсткий белый диск + широкое гало + рассеянные искры вокруг. Готовый «ореол лампы».
- `MMParticlesFlare` — точечный блик с длинной горизонтальной анаморфной чертой (J.J.-Abrams-flare).

**Bloom dirt** (`.../MMVFX/MMBloomDirt/MMBloomDirt1..4.png`) — единственные ЦВЕТНЫЕ текстуры набора, 1920×1080 и 3840×2160. Смотрел `MMBloomDirt3`: боке-кружки, царапины, пылинки и хроматические ободки — фотореалистичная грязь на линзе. Кладутся в URP Bloom → Dirtiness Texture.

---

### 5. Шейдеры Feel: что живо в URP 2D, что мёртво

Ключевой критерий — `#pragma surface` (surface shader = только Built-in RP, в URP не собирается) против vert/frag-пасса. Все vert/frag тут на `UnityCG.cginc` + `CGPROGRAM` — в URP формально компилируются (Unity держит совместимость), но освещения/теней URP не получают: это unlit-эффекты, что для 2D как раз нормально.

**Бесполезны (surface shaders, Built-in only):**

| Шейдер | Почему |
|---|---|
| `MMStandardEmission.shader` | чистый `#pragma surface`, только BRP |
| `MMStochastic.shader` | `#pragma surface surf Standard` |
| `MMWorldspace.shader` | `#pragma surface surf Lambert` |
| `MMToon.shader`, `MMAdvancedToon.shader` | Amplify-генерённые surface + доп. пассы; в URP розовые |
| `MMMatcap.shader`, `MMControlledEmission.shader`, `MM2DReflection.shader` | surface-основа (несмотря на имя «2D») |
| `MMVFX.shader` | флагманский Amplify VFX-шейдер, но 2 surface-пасса → BRP only |

**Применимы в URP 2D (unlit vert/frag, без surface):**

| Шейдер | Что делает | Оговорка |
|---|---|---|
| `MMBoilingLine.shader` | «кипящая» дрожащая линия/контур, теги `Transparent`, `CanUseSpriteAtlas=True` | прямо про 2D-спрайты, самый уместный |
| `MMUINoAlpha.shader` | UI-шейдер, игнорирующий альфу | uGUI, не UITK |
| `MMZTestAlways.shader` / `MMZTestAlwaysAdditive.shader` | рисование поверх всего (ZTest Always), второй — аддитивно | удобно для оверлеев поверх карты |
| `MMConeOfLight.shader` (`MMVision/`) | конус света, `Blend DstColor One` (умножение+добавка), ZTest Always | **прямой кандидат под луч лампы над картой** |
| `MMSkybox.shader` | скайбокс | 2D не нужен |
| `MMRipple.shader` | искажение экрана нормалью | **`GrabPass` — в URP НЕ РАБОТАЕТ.** Весь набор `MMRipple/*.mat` + `MMRipple.prefab` фактически мёртв; сами normal-map'ы (11 штук: круги, спираль, пила, розетка, квадрат, волны, пузырь) можно переиспользовать в своём URP-шейдере через `_CameraOpaqueTexture` |

Отдельно: 21 материал `MMPrototypeTextures/MMBRP_Materials/*.mat` — на Built-in Standard, в URP розовые. Сами PNG прототип-текстур пригодны, материалы — на выброс.

---

### 6. Shapes — что там кроме кода

| Категория | Путь | Что это |
|---|---|---|
| Сгенерённые материалы | `Assets/Shapes/Shaders/Generated Materials/` | **341 материал** (682 файла с .meta): для каждого примитива (Disc, Line 2D/3D, Polyline 2D, Rect, Regular Polygon, Triangle, Quad, Polygon, Torus, Sphere, Cone, Cuboid, Texture) × 11 блендов (Opaque, Transparent, Additive, Multiplicative, Subtractive, Screen, Lighten, Darken, ColorBurn, ColorDodge, LinearBurn) × keyword-варианты (INNER_RADIUS, SECTOR, CAP_ROUND/SQUARE, JOIN_MITER/BEVEL/ROUND, CORNER_RADIUS, BORDERED) |
| Сгенерённые шейдеры | `Assets/Shapes/Shaders/Generated Shaders/` | парные шейдеры к материалам |
| Ядро шейдеров | `Assets/Shapes/Shaders/Core/*.cginc` + `Shapes.cginc`, `Shapes Math.cginc`, `DashUtils.cginc`, `FillUtils.cginc` | процедурный SDF-рендер фигур с антиалиасингом; правится под свои эффекты |
| Шрифт | `Assets/Shapes/Textures/Inconsolata/Inconsolata-SemiBold.ttf` + `Inconsolata-SemiBold SDF.asset` (+ OFL-лицензия) | готовый TMP SDF-ассет моноширинного шрифта. Годится под дебаг/цифры, но **под правило проекта (Static-bake EN+RU) его надо перебейкивать — кириллицы в нём наверняка нет** |
| Меш-примитивы | `Assets/Shapes/Models/shapes_primitives.fbx` | болванки под 3D-фигуры |
| Конфиг | `Assets/Shapes/Resources/Shapes Config.asset`, `Shapes Assets.asset`, `Shapes Import State.asset` | глобальные настройки (в т.ч. выбор рендер-пайплайна) |
| Иконки редактора | `Assets/Shapes/Textures/*.png` (~30 шт: icon-disc, line-cap-*, line-dash-*, rect-style-*, regpol-N) | только инспектор, в игру не годятся |
| Сэмплы | `Assets/Shapes/Samples/` — 6 сцен (Shapes Gallery, FPS HUD, Color Picker, IMCanvas, Procedural Tree, Spinning Color Discs) + `Enemy.prefab`, `Capsules.mat`, `Ground.mat` | Gallery полезна как визуальный каталог возможностей; остальное — 3D-демки |

Материалы Shapes брать вручную не надо — компоненты выбирают их сами по бленду и keywords.

---

### 7. Что уже используется в `Assets/_Project`

Проверял двумя способами: перекрёстная сверка guid'ов из всех `.mat/.prefab/.unity/.asset` проекта против `.meta` обоих пакетов, плюс grep по коду.

**Из Feel — ровно ОДИН боевой ресурс:**
- `Assets/Feel/MMTools/Accessories/MMVFX/MMNoise/MMCloudsNoise.png` (guid `75b254a55f7e3f347ac8f2b5db9b11d9`) — прописан в `_MainTex` материала `Assets/_Project/Art/Shaders/MapBackdrop.mat`. **Это и есть искомый шум задника карты.**
  Шейдер `SH_Map_Backdrop.shader` объявляет одну текстуру `_MainTex ("Шум (grayscale)", 2D)`, а `MapBackdropCommon.hlsl` сэмплит её **четыре раза с разным тайлингом и сдвигом**, выжимая из одного шума всю фактуру листа:
  - `i.uv * _WeaveTiling` (2.6) → крупная «ткань» бумаги;
  - `i.uv * _GrainTiling` (18) + сдвиг (0.37, 0.61) → мелкое зерно;
  - `i.uv * _EdgeNoiseScale` (9) и `* 9 * 2.7` + сдвиг (0.13, 0.71) → рваная кромка листа.
  Текущие значения материала: `_BaseColor` (0.80, 0.73, 0.56), `_EdgeColor` (0.40, 0.32, 0.20), `_StainColor` (0.63, 0.55, 0.38), `_WeaveStrength` 0.62, `_GrainStrength` 0.18, `_EdgeBurn` 0.75, `_EdgeRagged` 0.05, `_Vignette` 0.3.
- Ещё всплыл `MMTools/Foundation/MMAchievements/Sprites/AchievementIcon.png` — служебная иконка, скорее случайная ссылка, не контент.
- **Кода Feel/MoreMountains в `_Project` нет вообще** (`grep -rl "MoreMountains" Assets/_Project --include=*.cs` — пусто). Feel в проекте живёт как склад текстур, а не как система фидбэков.

**Из Shapes — рабочий инструмент презентационного слоя,** `using Shapes` в пяти файлах:
- `Assets/_Project/Scripts/Presentation/Map/WorldMapView.cs` — пути карты строятся ЦЕПОЧКАМИ `Disc`-точек (пунктир Shapes на кривой «рвал ритм»), есть пул `_dotPool` на 256, префаб точки `_dotPrefab`, пешка игрока — тоже `Disc`. В комментариях зафиксированы две готчи: Shapes рисуется на sorting layer Default (самый низ) и допускает лишь ОДИН ShapeRenderer на GameObject, поэтому глубина решается Z, а не слоями.
- `Assets/_Project/Scripts/Presentation/Map/MapNodeView.cs` — подложка узла как Shapes-фигура, цвет из палитры карты.
- `Assets/_Project/Scripts/Presentation/CombatAreaFlash.cs` — вспышки зон боя: раздельные пулы `Disc` и `Line`, форма фиксируется при создании.
- `Assets/_Project/Scripts/Presentation/CombatStatusOverlay.cs` — кольца статусов, пул `Disc` с `DiscType.Ring`, `DiscGeometry.Flat2D`.
- `Assets/_Project/Scripts/Presentation/DeploymentView.cs` — рамки зон расстановки (`Line`, Flat2D, Square caps) и круги-опоры под юнитами (`Disc`).
- В сериализованных ассетах закреплены `Disc Transparent.mat` и `Disc Transparent (INNER_RADIUS).mat` + `Shapes Assets.asset`.

---

### 8. Под карту акта: что закрывается без покупок

Целевая картинка: лист старой бумаги на тёмном столе под лампой, тёплые чернильно-пергаментные тона; в планах — пыль в луче света, дымка, чернильные потёки.

**Пыль в луче света**
- Спрайт частицы: `MMParticles/MMParticlesDust.png` (мягкая размытая пылинка, ровно то что надо) — на ParticleSystem с аддитивным блендом, редкий спавн, медленный дрейф вверх-вбок. Для более заметных пылинок — `MMParticlesStar.png`, для блика — `MMParticlesFlare.png`.
- Сам конус света: `MMVision/MMConeOfLight.shader` — unlit vert/frag, `Blend DstColor One`, ZTest Always, есть `_Contrast` и `_Color`. Единственный шейдер Feel, прямо решающий «луч». Плюс маска `MMVision/ConeOfVisionAlpha.png`.
- Ореол лампы над листом: `MMParticles/MMParticlesLight.png` (диск + гало + искры) — статичным квадом на аддитиве.
- Грязь в свете лампы: `MMBloomDirt/MMBloomDirt3.png` (1920×1080) или `MMBloomDirt1/2/4` (4K) → в URP Volume Bloom → Dirtiness Texture. Даёт «объектив запылён» бесплатно, без частиц. Оговорка: текстуры цветные с хроматическими ободками и явно фотографические — под стилизованный пиксель-арт могут быть слишком «фотошными», силу держать низкой.

**Дымка / атмосфера вокруг листа**
- `MMNoise/MMSimplexNoise.png` — самый спокойный низкочастотный fBm, tileable, ровно под медленно ползущую дымку двумя слоями с разной скоростью.
- `MMNoise/MMFlowNoise.png` — направленные волокна-струи, tileable; хорош если дымка должна тянуться в одну сторону (сквозняк над столом).
- `MMNoise/MMCloudsNoise.png` — тот же, что уже в задник; переиспользование даст визуальное единство, но и риск «одинаковой» фактуры на листе и в воздухе.
- Для «дизера» дымки, если хочется ретро-растра вместо гладкого градиента: `MMNoise/MMBayerNoise.png` (упорядоченный дизер, крупная матрица) или `MMNoise/MMBlueNoise.png` (равномерный, без узора). В прошлой итерации карты дизерная фаза уже была — эти текстуры дают её без процедурного кода.

**Чернильные потёки**
- `MMBrushes/MMBrush1.png` — сухая щетинистая кисть с рваными царапинами, самая «чернильная» из шести. Как маска альфы или множитель поверх листа.
- `MMBrushes/MMBrush0.png` — постеризованные плоские пятна с рваным краем: кляксы/размывы.
- `MMNoise/MMBrushNoise.png` — тёмные диагональные облака с мазаной кромкой (средний тон ~0.2), готовая маска затемнения/потёка.
- `MMNoise/MMCellNoise.png` — чёрные перемычки между светлыми ячейками = кракелюр/трещины на старой бумаге, если лист захочется состарить сильнее.
- Все кисти tileable, так что тянутся по большому листу без швов.

**Тёплая тональная гамма**
- `MMRamps/MMRamp0.png` — сливовая тень → лососевый → кремовая бумага. Прямая gradient-map под наш чернильно-пергаментный тон: скармливать grayscale-результат как X-координату.
- `MMRamps/MMRamp2.png` — сталь-синий → тёплый жёлто-белый: буквально «тёмный стол → пятно лампы».
- `MMRamps/MMRamp6.png` — глубокая фиолетово-чёрная тень с долгой полкой: под затемнение за пределами луча.

**Векторная часть — Shapes, уже в деле**
Дополнительно к текущим точкам путей: `Disc` с блендом Multiplicative/ColorBurn (материалы уже сгенерены) даст «чернила, впитавшиеся в бумагу», а не наклейку поверх — под подложки узлов и обводки это точнее, чем Transparent. Кольцевой `Disc` (`DiscType.Ring`) с ColorBurn — печать/штамп на листе.

**Чего в Feel/Shapes НЕТ и придётся делать самим**
- Текстуры собственно бумаги/холста/волокна (только `MMFlowNoise` косвенно).
- Готовых собранных VFX-префабов (демо-папки пусты) — любую систему частиц собирать с нуля из спрайтов.
- Vignette/градиент виньетки — но это уже решено внутри `SH_Map_Backdrop` параметром `_Vignette`.
- Пост-эффект искажения (`MMRipple` мёртв в URP из-за GrabPass) — если захочется «дрожащего воздуха» над лампой, нужен свой шейдер на `_CameraOpaqueTexture`, зато 11 готовых normal-map из `MMVFX/MMRipple/` можно взять как источник искажения.
