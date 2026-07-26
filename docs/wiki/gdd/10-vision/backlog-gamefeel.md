---
title: "Vision - Gamefeel Backlog"
order: 20
status: draft
updated: 2026-07-18
---
> [!note] Границы дока
> Это **банк идей подачи**, питающий [[visual-direction|Vision - Visual Direction]] и столп
> «Ощущение важнее верности пикселю». Технические детали (какой шейдер, какой пасс, сколько
> стоит на GPU) приведены **справочно**, чтобы идея не оказалась невыполнимой; канон
> реализации — за тех-викой, а не за этим доком. Решено оставить в ГДД 2026-07-26.


# Gamefeel-бэклог (тактильно-временной слой)

> Каталог тактильного/временно́го фидбэка: hitstop, shake, slowmo, squash, тайм-рампы.
> Собран ресёрчем 2026-07-18. **Идеи** (`proposed`). Зонтик —
> [[visual-direction|Vision - Visual Direction]] (трек 5). Ядро уже богатое (см. трек 5),
> здесь — принципы из ресёрча + новые докрутки. Легенда: **[новое]** / **[углубл.]** / **[принцип]**.

## Принципы (из практиков)

- **Blend & layer, no single hero (Vlambeer/Joonas Turner):** ни один эффект не «делает» фидбэк —
  вес удара рождается связкой звук+shake+партиклы+hitstop НА ОДНО событие. Проектировать удар как
  СЛОЙ, не как один эффект. **[принцип]** — Nuclear Throne explosions breakdown, Dead Cells «What the F?!».
- **Pavlovian reward (Hades):** каждый удар — микро-награда; отделять фидбэк-действия игрока от
  ситуативных cue (у нас игрок не бьёт, но «клиент наблюдает» — награда за наблюдаемый удачный размен). **[принцип]**
- **Feel — цель дизайна звука, не полиш в конце (Turner, GDC):** тактильные решения принимаются на
  этапе дизайна события, а не «подмешиваются» потом. **[принцип]**

## Каталог (новые докрутки поверх записанного трека 5)

| Приём | Что даёт | Реализация | Статус |
|---|---|---|---|
| **Layered impact «one event → N слоёв»** | Единая точка, где на боевое событие сходятся shake+flash+партиклы+hitstop+звук | CombatFeelDirector/CombatPresenter оркестрация | [углубл.] |
| **Directional hit-nudge** | Цель на хитстопе уезжает от удара и возвращается | LitMotion (unscaled) | [углубл.] (в треке 5) |
| **Tiered hitstop по урону** | Крупный удар морозит дольше | CombatFeelConfig кривая | [углубл.] (в треке 5) |
| **First-blood / last-enemy тайм-рампы** | Акцент на первую кровь и добивание последнего врага | TimeScaleService Cinematic | [углубл.] (в треке 5) |
| **Anticipation-замах + attacker lunge** | Микро-оттяг перед ударом + рывок к цели в импакт | LitMotion (unscaled) | [углубл.] (в треке 5) |

> Большинство тактильных идей уже занесены в [[visual-direction|Visual Direction]] трек 5
> (impact-frame, anticipation, lunge, hit-nudge, tiered hitstop, first-blood, last-enemy ramp,
> trauma-shake, camera-punch). Этот док — их дом-каталог + принципы из ресёрча, без дублей.

## Связи

- Партиклы/шейдеры удара — [[backlog-vfx-particles-shaders|VFX-бэклог]].
- Звуковой слой удара (impact transient/body/tail, DSP на slowmo) — [[backlog-audio-sfx|Аудио-бэклог]].
- Боевое время (TimeScaleService) — контур `combat-sim`; feel только потребляет Cinematic-API.
