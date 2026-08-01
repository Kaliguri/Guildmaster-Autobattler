---
title: "Vision - Light, Post, Atmosphere & Transitions Backlog"
order: 21
status: draft
updated: 2026-07-18
---
> [!note] Границы дока
> Это **банк идей подачи**, питающий [[visual-direction|Vision - Visual Direction]] и столп
> «Ощущение важнее верности пикселю». Технические детали (какой шейдер, какой пасс, сколько
> стоит на GPU) приведены **справочно**, чтобы идея не оказалась невыполнимой; канон
> реализации — за тех-викой, а не за этим доком. Решено оставить в ГДД 2026-07-26.


# Свет + пост-обработка + атмосфера + переходы — бэклог

> Каталог техник и реализации в URP 2D. Собран ресёрчем 2026-07-18. **Идеи/реализация**
> (`proposed`). Зонтик — [[visual-direction|Vision - Visual Direction]] (треки 1–4).
> Легенда: **[новое]** / **[углубл.]** / **[готча]** / **[принцип]**.

## 1. 2D-свет (URP Light2D)

- **Типы Light2D:** Freeform (произвольный полигон), Sprite (форма спрайта), Spot/Point (конус:
  Inner/Outer Angle, Outer Radius, Falloff), Global (вся сцена). Global — **1 на blend style + sorting layer**. **[углубл.]**
- **Cost-рычаги [готча/принцип]:** Light Render Texture по умолчанию **0.5× экрана** (half-res —
  почти без артефактов); держать **1–2 blend styles** (каждый = отдельная RT); **normal-maps ОЧЕНЬ
  дорогие** — выключать, если глубина не нужна; **layer batching** (соседние sorting layers с
  одинаковым набором света батчатся). Есть Light Batching Debugger.
- **Light2D не физичен** — рисуем НАСТРОЕНИЕ, не симуляцию; свойства можно кейфреймить (day-night, вспышки). **[принцип]**
- **Painted-in shadows в спрайт** (16-bit приём Hollow Knight/Owlboy) — дешёвая глубина БЕЗ Light2D;
  комплемент, не замена. **[новое]**
- **Shader Graph / VFX Graph совместимы с Light2D** — VFX/death-вспышки могут быть источниками света. **[углубл.]**

## 2. Пост-обработка (совместимость со стилизованным пиксель-артом)

- **Pixel-art post-recipe [принцип]:** рендер в низком ВНУТРЕННЕМ res → эффект/outline в этом res
  (гарантирует 1px-края) → **sharp upscale** (кастом-шейдер: `fwidth()` + smoothstep на границах текселей,
  не bilinear). Референс-пайплайн: unity-isometric-pixel-pipeline (GitHub, Unity 6).
- **Bloom-готча [готча]:** источник `R8G8B8A8_UNORM` билинейно фильтруется в `R11G11B10_FLOAT` на
  prefilter-даунсэмпле → смаз/артефакты на пикселях. Знать при настройке нашего базового bloom; intensity 1–2, не задирать.
- **Fullscreen outline как Renderer Feature** — depth/normal/color edge-detection пост-проходом (Render
  Graph, Unity 6). Готовые: Daniel Ilett (Sample Buffer node), Alexander Ameye (Render Graph). **[углубл.]** (rim/outline записан)
- **4×4 Bayer ordered dithering** — разбить бандинг тонов/грейдинга. **[новое]**
- **Heat-haze / distortion** — copy screen ПОСЛЕ transparents + distortion-blit (иначе occlusion-баги). **[новое]**

## 3. Атмосфера и глубина

- **Parallax по Z + затемнение ближнего слоя** к краям кадра (кадрирование арены). **[углубл.]**
- **Ambient-партиклы близко к камере** — сильнее parallax; пыль/листья/споры. **[углубл.]**
- **Wind-sway с root-pinning [новое]:** displacement × UV.y (низ прибит, верх качается); шейдер батчит
  всю листву/флаги в один проход. Есть 2D-спрайтовая версия (NedMakesGames, 2D vegetation wind).
- **God-rays как Shader Graph** (Cyanilux: Scene Depth + radial sampling; density/decay/samples — рычаги стоимости). **[углубл.]**
- **Fake DoF = aerial perspective [новое]:** реальный post-DoF мылит всё — вместо него ПРЕ-блюренные
  слои по художественной дистанции + fade цвета к горизонту (fog). URP 2D частично хранит depth (2024+).

## 4. Погода

- **2D-дождь на Particle System [углубл.]:** горизонтальный Line-эмиттер, Gravity 1, ~250 частиц, lifetime 3с,
  Color/Size-over-Lifetime fade, Collision 2D для брызг/накопления. Дёшево, пиксель-friendly baseline.
- **VFX Graph погода [новое]:** снегопад/буря на GPU (~50k частиц, velocity field под ветер) — масштаб, недостижимый на PS.
- **Rain-on-lens droplets [новое]:** экранные капли на «линзе» — Fullscreen Shader Graph пост-проход.
- **Weather как связанный state [новое]:** Wind/Temperature/Rain/Fog reactive (дождь реагирует на темп/ветер →
  Rain/Storm/Snow/Blizzard). Data-driven стейт-машина под биом-погоду. Референс: Sema's 2D Weather; Production-Ready Weather (Shader Graph 17 subgraphs, puddle-accumulation).

## 5. Переходы между аренами (SAO-dissolve и родня)

- **Путь реализации [углубл.]:** Fullscreen Master Stack (Shader Graph) + **Full Screen Pass Renderer Feature**
  на URP Renderer Data; параметр 0..1 (0=видно игру, 1=закрыто), анимируется LitMotion. Варианты формы: dissolve,
  iris/circle (0..1=радиус), hex-grid, pixelate. Готовый open-source: hunterdyar/Unity-Transition-Effects.
- **Unity 6 Render Graph [готча]:** `OnCameraSetup/Execute/OnCameraCleanup` устарели → `RecordRenderGraph()`;
  использовать **Blitter API** (не `CommandBuffer.Blit`); read≠write одной текстуры → ping-pong RTHandle.
- **2D Renderer half-screen баг [готча]:** Fullscreen-Shader-Graph материал на 2D Renderer покрывает ПОЛ-экрана
  (UV/`_BlitScaleBias` pitfall) — обработать, иначе wipe клипается посередине. **Прямо наш стек.**
- **Injection point** переключает, где композится переход (After Rendering Post Processing = дефолт).

## 6. Визуал узлов карты (заявка Макса, 2026-07-29)

Не боевой визуал, а **подача узлов** — то, что игрок видит между боями. Макс отметил, что это «тех-часть
и лёгкий арт», то есть делается силами кода и небольшой графики, без художника.

| Узел | Чего хочется | Что в этом дорогого |
|---|---|---|
| **Магазин** | 3–6 **левитирующих предметов**, которые игроки крутят в 3D как карточки; кручение **синхронизировано** между игроками | сеть: это первый интерактив, который надо синхронизировать вне боя |
| **Кемп** | красивый костёр с VFX пламени и дыма | чистая графика, дешёво |
| **Сундук** | эффектное открытие с кучей эффектов | чистая графика, дешёво |

**Почему магазин важнее, чем выглядит.** Синхронное кручение предмета — **со-присутственный** момент:
все четверо видят, как кто-то рассматривает вещь. У нас таких моментов мало (бой автономен, вся
агентность в подготовке), и они делают кооп живым дешевле, чем любая боевая механика. Стоит закладывать
не как украшение, а как **кооп-фичу**.

## Связи

- Экранные боевые эффекты (shockwave-дисторсия) — [[backlog-vfx-particles-shaders|VFX-бэклог]].
- SAO-эстетика перехода как посев мета-нарратива — [[visual-direction|Visual Direction]] трек 4 + [[open-forks|мета-нарратив]].
