---
title: "Vision - UI Juice Backlog"
order: 19
status: draft
updated: 2026-07-18
---
> [!note] Границы дока
> Это **банк идей подачи**, питающий [[visual-direction|Vision - Visual Direction]] и столп
> «Ощущение важнее верности формату». Технические детали (какой шейдер, какой пасс, сколько
> стоит на GPU) приведены **справочно**, чтобы идея не оказалась невыполнимой; канон
> реализации — за тех-викой, а не за этим доком. Решено оставить в ГДД 2026-07-26.


# UI-juice бэклог

> Каталог приёмов сочности интерфейса: боевой HUD, damage numbers, HP-бары, экраны,
> моменты-презентации. Собран ресёрчем 2026-07-18. Все записи — **идеи** (`proposed`).
> Зонтик — [[visual-direction|Vision - Visual Direction]] (трек 5 «UI-микроанимации»).
> Легенда: **[новое]** / **[углубл.]** / **[принцип]**.

## Принципы (из ресёрча)

- **Async, non-blocking feedback (Slay the Spire):** UI-анимации НИКОГДА не гейтят ввод/бой —
  fire-and-forget. Наши LitMotion-твины HUD должны стрелять и забываться, а не задерживать
  боевой поток. **[принцип]**
- **Anchor-иерархия важности:** один элемент — главный в кадре в любой момент; остальное
  подаётся относительно него (эхо аудио-anchor). **[принцип]**

## Каталог

| Приём | Что даёт | Инструмент | Статус | Источник |
|---|---|---|---|---|
| **Motion-streaks на элементах** | След за движущимся элементом (карта/иконка) читает поток с одного взгляда | LitMotion + trail/ghost | [новое] | Slay the Spire UI breakdown |
| **Punch-scale на нажатии/событии** | Сквиш-масштаб кнопки/иконки при действии | LitMotion (ease) | [углубл.] (icon-pop) | Juice it or Lose it |
| **Counter roll-up / bar drain-curve** | Число «докручивается», бар утекает по кривой, а не скачком | LitMotion | [новое] | GameJuice library |
| **Hover/press микро-моция** | Микро-масштаб/тень на наведение-нажатие — app-shell живее | LitMotion / USS transition | [новое] | GameJuice library |
| **HP-бар: ghost/lag + heal-наплыв** | «Призрачный» след урона + зелёная волна хила снизу | LitMotion (два слоя бара) | [углубл.] (уже в треке 5) | Game UI Database (Enemy Health) |
| **Reveal-секвенс наград/лут** | Ступенчатое проявление + фанфары (Hades boon-reveal) | LitMotion таймлайн | [новое] | Game UI Database (Hades) |
| **Boss-intro title card** | Диагональная лента + имя + эпитет + портрет на старте боя с боссом | UITK/uGUI + LitMotion | [углубл.] (трек 5) | The King is Watching |

## Референс-каталоги (не приёмы — источники для дальнейшей добычи)

- **GameJuice library** — браузерный каталог juice-эффектов по категориям.
- **Game UI Database** — галерея HUD-решений (Enemy Health, Player Vitals, Hades).
- **«Juice it or Lose it»** (Jonasson/Purho) — корневой доклад, откуда растёт весь список.

## Связи

- Damage-number дугой, combo-каунтер, screen-flash — [[visual-direction|Visual Direction]] трек 5.
- Тактильный feel HUD (hitstop на баре) — [[backlog-gamefeel|Gamefeel-бэклог]].
