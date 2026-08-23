/* Боевой HUD: три раскладки.

   Источник — `Art_Dev/UI Refs/_teardowns/03-combat-hud-tooltips.md`. Главное число разбора: у
   Guildrun, ближайшего жанрового соседа, ПОД ИНТЕРФЕЙСОМ 16–17% ПЛОЩАДИ КАДРА, игре остаётся 83%.
   Это и есть мера, которой стоит мерить любой вариант ниже.

   Второе наблюдение оттуда же: верхняя полоса Guildrun НЕ сплошная плита. Непрозрачна только
   центральная часть с треком узлов, остальная ширина — затемняющий скрим, сквозь который сцена
   читается (30–60 против 100+ рядом). То есть «полоса сверху» не обязана быть панелью.

   ОГОВОРКА ПРО СТЕК: наш боевой HUD собран на uGUI, а не на UI Toolkit. Раскладку это не меняет,
   но реализация чертежа пойдёт другим путём, чем у остальных экранов этого кластера. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Юнит на арене с полосой здоровья над ним. */
function unit(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number },
  width: number,
  height: number,
  hp = 1
): void {
  w.box(ctx, { x: at.x, y: at.y, w: 0.03, h: 0.075 }, width, height, { hollow: true, dashed: true });
  w.box(ctx, { x: at.x - 0.004, y: at.y - 0.022, w: 0.038, h: 0.012 }, width, height, { hollow: true });
  w.box(ctx, { x: at.x - 0.004, y: at.y - 0.022, w: 0.038 * hp, h: 0.012 }, width, height, { lit: true });
}

function arena(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const rows: Array<[number, number, number]> = [
    [0.36, 0.42, 1],
    [0.36, 0.56, 0.6],
    [0.6, 0.42, 0.8],
    [0.6, 0.56, 0.35]
  ];
  rows.forEach(([x, y, hp]) => unit(ctx, { x, y }, width, height, hp));
}

/* ---------- А: полная периферия (реф Guildrun) ---------- */

function drawFull(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  arena(ctx, width, height);

  // Верх: непрозрачна ТОЛЬКО центральная плита трека узлов, остальное — скрим.
  ctx.fillStyle = "rgba(12,12,14,0.45)";
  ctx.fillRect(0, 0, width, 0.058 * height);
  w.box(ctx, { x: 0.375, y: 0, w: 0.25, h: 0.058 }, width, height, { label: "трек узлов акта", size: 7 });
  w.text(ctx, "¤ 15", { x: 0.32, y: 0.03 }, width, height, { size: 8, color: w.WIRE.accent });
  w.box(ctx, { x: 0.47, y: 0.06, w: 0.06, h: 0.03 }, width, height, { label: "00:12", size: 7 });
  w.box(ctx, { x: 0.865, y: 0.007, w: 0.13, h: 0.028 }, width, height, { label: "скорость боя", size: 7 });

  // Лево: колонка предметов. Право: карточка выбранного юнита.
  w.box(ctx, { x: 0.004, y: 0.17, w: 0.026, h: 0.032 }, width, height, { label: "▤", size: 8 });
  for (let i = 0; i < 7; i++) {
    w.box(ctx, { x: 0.004, y: 0.208 + i * 0.05, w: 0.026, h: 0.044 }, width, height, {});
  }
  w.box(ctx, { x: 0.863, y: 0.199, w: 0.129, h: 0.417 }, width, height, {
    label: "карточка юнита",
    size: 8
  });

  // Низ: лента отряда и резерва.
  w.text(ctx, "ОТРЯД", { x: 0.2, y: 0.895 }, width, height, { size: 7, color: w.WIRE.dim });
  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.23 + i * 0.055, y: 0.87, w: 0.045, h: 0.08 }, width, height, { lit: i === 0 });
  }
  for (let i = 0; i < 2; i++) {
    w.box(ctx, { x: 0.48 + i * 0.055, y: 0.87, w: 0.045, h: 0.08 }, width, height, { dashed: true });
  }
  w.text(ctx, "РЕЗЕРВ", { x: 0.6, y: 0.895 }, width, height, { size: 7, color: w.WIRE.dim });
  w.text(ctx, "СЛОЖНОСТЬ: B", { x: 0.008, y: 0.93 }, width, height, { size: 7, color: w.WIRE.dim });
  w.box(ctx, { x: 0.865, y: 0.9, w: 0.13, h: 0.05 }, width, height, { label: "Отзыв", size: 8 });

  w.callout(
    ctx,
    { x: 0.86, y: 0.3 },
    { x: 0.78, y: 0.14 },
    "под UI 16–17% площади кадра",
    width,
    height,
    "right"
  );
}

/* ---------- Б: минимум — полосы и одна панель управления ---------- */

function drawMinimal(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  arena(ctx, width, height);

  // Всё, что нужно во время боя: скорость и пауза. Остальное показывается по требованию.
  w.box(ctx, { x: 0.86, y: 0.02, w: 0.13, h: 0.05 }, width, height, { label: "скорость · пауза", size: 7 });
  w.box(ctx, { x: 0.02, y: 0.02, w: 0.09, h: 0.05 }, width, height, { label: "00:12", size: 8 });
  w.text(ctx, "¤ 15", { x: 0.13, y: 0.045 }, width, height, { size: 8, color: w.WIRE.accent });

  w.callout(
    ctx,
    { x: 0.5, y: 0.66 },
    { x: 0.56, y: 0.8 },
    "состав и предметы — по клавише, поверх боя",
    width,
    height
  );
  w.text(ctx, "под UI меньше 3% площади", { x: 0.5, y: 0.93 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.accent
  });
}

/* ---------- В: нижняя лента отряда и правая колонка ---------- */

function drawBottomBar(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  arena(ctx, width, height);

  // Одна плотная лента снизу вместо периферии по четырём сторонам.
  w.box(ctx, { x: 0, y: 0.82, w: 1, h: 0.18 }, width, height, {});
  for (let i = 0; i < 4; i++) {
    const x = 0.03 + i * 0.16;
    w.box(ctx, { x, y: 0.845, w: 0.15, h: 0.13 }, width, height, { lit: i === 0 });
    w.text(ctx, ["Ирма", "Кай", "Дан", "Лея"][i]!, { x: x + 0.075, y: 0.875 }, width, height, {
      align: "center",
      size: 8
    });
    w.box(ctx, { x: x + 0.01, y: 0.895, w: 0.13, h: 0.014 }, width, height, { hollow: true });
    w.box(ctx, { x: x + 0.01, y: 0.895, w: 0.13 * [1, 0.6, 0.8, 0.35][i]!, h: 0.014 }, width, height, {
      lit: true
    });
    for (let k = 0; k < 3; k++) {
      w.box(ctx, { x: x + 0.01 + k * 0.03, y: 0.918, w: 0.026, h: 0.04 }, width, height, {});
    }
  }
  w.box(ctx, { x: 0.7, y: 0.845, w: 0.13, h: 0.06 }, width, height, { label: "скорость", size: 7 });
  w.box(ctx, { x: 0.7, y: 0.915, w: 0.13, h: 0.06 }, width, height, { label: "пауза", size: 7 });
  w.box(ctx, { x: 0.85, y: 0.845, w: 0.13, h: 0.13 }, width, height, { label: "трек узлов", size: 7 });
  w.text(ctx, "00:12   ·   ¤ 15", { x: 0.5, y: 0.05 }, width, height, {
    align: "center",
    size: 9,
    color: w.WIRE.accent
  });
  w.measure(ctx, { x: 0, y: 0.82, w: 1, h: 0.18 }, "18% высоты", width, height, "x", "before");
}

const section: SectionDef = {
  id: "ui-hud",
  title: "Боевой HUD",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Единственный экран, который меряется не долями блоков, а долей КАДРА: у ближайшего жанрового " +
    "соседа под интерфейсом 16–17% площади, игре остаётся 83%. Три раскладки ниже отличаются " +
    "прежде всего этим числом.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Сколько кадра занимает интерфейс",
      lede:
        "Разбор считает площадь по описанному прямоугольнику каждого блока. Числа ниже — оттуда, " +
        "и они задают шкалу для наших вариантов."
    },
    {
      kind: "table",
      head: ["Блок у Guildrun", "Доля кадра", "Замечание"],
      rows: [
        ["Карточка выбранного юнита", "5.37%", "самый крупный элемент HUD"],
        ["Лента отряда и резерва внизу", "4.44% по прямоугольнику", "реальная закраска около 1.3%"],
        ["Плита трека узлов сверху", "1.49%", "единственная непрозрачная часть верхней полосы"],
        ["Колонка предметов слева", "0.89%", "семь слотов вертикально"],
        ["Блок скорости боя", "0.65%", "тумблер авто и три шеврона"],
        ["ИТОГО под UI", "16–17%", "игре остаётся 83%"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Верхняя полоса — не панель.</b> У Guildrun непрозрачна только центральная плита с " +
        "треком узлов; остальная ширина — затемняющий скрим, сквозь который сцена видна (значения " +
        "пикселей 30–60 против 100+ рядом). Это прямо применимо к нам: полосу можно завести, не " +
        "отрезая от кадра ленту сплошной заливки."
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede: "Все три ждут вердикта. Доли — от кадра 1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "hud-full",
          status: "waiting",
          title: "А · Полная периферия",
          tag: "реф Guildrun",
          note:
            "Интерфейс по всем четырём сторонам: трек узлов и таймер сверху, предметы слева, " +
            "карточка юнита справа, лента отряда снизу.",
          facts: [
            ["под UI", "16–17% кадра"],
            ["карточка юнита", "12.9% x 41.7%"],
            ["лента отряда", "58.2% ширины"],
            ["верхняя полоса", "скрим, не плита"]
          ],
          verdict:
            "Всё под рукой и ничего не нужно вызывать — для игры с паузой это весомо. Цена — периферия по четырём сторонам сужает арену, а у нас бой идёт на всём поле, а не на гексах.",
          size: [480, 270],
          draw: drawFull
        },
        {
          id: "minimal",
          status: "waiting",
          title: "Б · Минимум на экране",
          tag: "своё",
          note:
            "Во время боя видны только полосы над юнитами, таймер, золото и управление скоростью. " +
            "Состав и предметы вызываются клавишей поверх боя.",
          facts: [
            ["под UI", "меньше 3%"],
            ["постоянных блоков", "3"],
            ["остальное", "по вызову"],
            ["арена", "почти весь кадр"]
          ],
          verdict:
            "Бой видно целиком — и это главное, ради чего у нас вообще живой мир за интерфейсом. Цена — всё, что вызывается, легко не найти: игрок может не узнать, что предметы вообще есть.",
          size: [480, 270],
          draw: drawMinimal
        },
        {
          id: "hud-bottom",
          status: "waiting",
          title: "В · Лента отряда снизу",
          tag: "своё",
          note:
            "Вместо периферии по четырём сторонам — одна плотная лента внизу: четыре бойца с " +
            "полосами и слотами, управление и трек узлов там же.",
          facts: [
            ["лента", "100% x 18%"],
            ["карточка бойца", "15% ширины"],
            ["слотов на бойца", "3"],
            ["верх кадра", "свободен"]
          ],
          verdict:
            "Отдаёт интерфейсу нижнюю полосу целиком и оставляет верх кадра чистым — композиция боя не режется с четырёх сторон. Цена — 18% высоты это больше, чем весь HUD рефа, и арена уезжает вверх.",
          size: [480, 270],
          draw: drawBottomBar
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> где живёт карточка выбранного юнита и нужна ли она постоянно · " +
        "показываем ли трек узлов акта прямо в бою · сколько слотов предметов у бойца видно без " +
        "наведения · и отдельным заходом — тултипы: их разбор в том же файле, и там четыре рефа " +
        "против трёх у самого HUD."
    }
  ]
};

export default section;
