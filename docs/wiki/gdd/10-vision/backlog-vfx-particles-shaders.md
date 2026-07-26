---
title: "Vision - VFX Backlog (Particles & Shaders)"
order: 18
status: draft
updated: 2026-07-18
---
> [!note] Границы дока
> Это **банк идей подачи**, питающий [[visual-direction|Vision - Visual Direction]] и столп
> «Ощущение важнее верности пикселю». Технические детали (какой шейдер, какой пасс, сколько
> стоит на GPU) приведены **справочно**, чтобы идея не оказалась невыполнимой; канон
> реализации — за тех-викой, а не за этим доком. Решено оставить в ГДД 2026-07-26.


# VFX-бэклог: Particle System + Shader Graph / VFX Graph

> Каталог приёмов боевого VFX (эффекты в мире: удары, касты, смерти, статусы, снаряды,
> щиты). Собран ресёрчем 2026-07-18. Все записи — **идеи на рассмотрение** (`proposed`),
> не принятый дизайн. Зонтик — [[visual-direction|Vision - Visual Direction]] (трек 5/6).
> Легенда статуса: **[новое]** — нет в бэклоге; **[углубл.]** — реализация записанного;
> **[готча]** — подводный камень нашего стека.

## Архитектурное правило (из ресёрча)

**Массовое/частое → VFX Graph (GPU); точечное/редкое → Particle System.** При толпе юнитов
GPU-партиклы VFX Graph дешевле, чем Particle System на каждого. Gabriel Aguiar — эталонный
автор рецептов боевого VFX (импакты/снаряды/muzzle/ауры/dissolve в обоих инструментах, URP).

## Каталог

| Приём | Что даёт | Инструмент | Стоимость | Статус | Источник |
|---|---|---|---|---|---|
| **Screen-space shockwave-дисторсия** | Радиальный ГЕОМЕТРИЧЕСКИЙ варп кадра от удара/AoE/смерти (не цвет — реальное искажение) | Shader Graph fullscreen (есть 2D-версия), радиус ← LitMotion | Дёшево (1 blit/full-screen pass) | [новое] | Game Dev Bill; Cyanilux; YT 2D Renderer shockwave |
| **VFX Graph hit/impact/muzzle** | GPU-партиклы удара/каста/выстрела; flipbook muzzle-flash | VFX Graph | Дёшево при объёме (GPU) | [новое, инстр.] | Gabriel Aguiar |
| **Forcefield / energy-shield** | Fresnel-контур щита на защищённом юните + ripple-пульс ИЗ точки попадания | Shader Graph (Fresnel, Scene Depth, custom ripple) | Средне | [новое] | Cyanilux (Forcefield); Daniel Ilett (Energy Shield) |
| **Emissive burn-edge dissolve** | Бегущий светящийся край растворения; reverse-dissolve = спавн-телеграф врага | Shader Graph (noise threshold + emissive ramp) | Дёшево | [углубл.] (dissolve записан) | Daniel Ilett |
| **Particle-пресеты искр/пыли/угольков** | Мелкие акценты hit/death; 2×2–4×4 яркая текстура, конус ОТ нормали удара, alpha fade | Particle System | Очень дёшево | [углубл.] (pixel-burst) | Game Dev Cheat Sheet |
| **Projectile-таксономия** | Чеклист архетипов снаряда (arcing/homing/beam/spread/charged), каждый с парой muzzle+impact | VFX Graph / PS | — (дизайн) | [новое] | Gabriel Aguiar (Unique Projectiles) |
| **Vertex-displacement на попадание** | Меш-деформация спрайта в точке удара («вдавливание») | Shader Graph (vertex) | Дёшево | [углубл.] (vertex-wobble) | Harry Alisavakis (ShaderQuest) |
| **GPU-трейлы снарядов** | Гладкий изогнутый след | VFX Graph / PS trails | +draw calls | [углубл.] (motion trails) | Gamine AI guide |

## Готчи реализации

- **Пул для частых импакт-VFX обязателен** — sub-emitter budget 5–20 частиц, <2 уровня вложения.
- **PS vs VFX Graph по стоимости** — считать per-effect при боевом объёме частиц.

## Связи

- **Заказ 2026-07-26 «свечение и осколки»** (смерть по рефу SAO, осколки от урона, свечение
  оружия/слешей/снарядов, огоньки павших, бесформенная маска арены) — рабочий разбор в
  `docs/vfx-glow-and-shards-plan.md`. Это уже не банк идей, а принятый вектор.
- Тактильная сторона (hitstop/shake/slowmo) — [[backlog-gamefeel|Gamefeel-бэклог]].
- Экранные эффекты, совместимость с пиксель-артом, переходы — [[backlog-atmosphere-light-post|Свет/пост/атмосфера]].
- Death-shatter/SAO-смерть/статус-оверлеи — [[visual-direction|Visual Direction]] трек 6.
