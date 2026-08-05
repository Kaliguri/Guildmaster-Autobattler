/* Главное меню: шесть раскладок, снятых с четырнадцати рефов.

   Источник чисел — `Art_Dev/UI Refs/_teardowns/01-main-menu.md`. Оттуда же четыре факта, общие ДЛЯ
   ВСЕХ четырнадцати; они соблюдены в каждом чертеже и предметом выбора не являются:

     · один вертикальный список, без вложенности на первом экране;
     · фон занимает весь кадр — ни один реф не рисует меню на плоской заливке;
     · вывеска и список не пересекаются, между ними зазор от 6 до 100 px;
     · «Выход» всегда последний пункт.

   Что в игре сейчас — вариант А: колонка у левой кромки поверх живого боя, пункты пластинами.
   Решение от 04.08.2026, журнал ADR «главное меню сходит с панели». Здесь оно стоит наравне с
   остальными: Макс пересматривает расстановку. */

import * as w from "./ui-wire.js";
import type { SectionDef } from "../types.js";

const ITEMS = ["СОЗДАТЬ ИГРУ", "ПРИСОЕДИНИТЬСЯ", "ПРОФИЛЬ", "НАСТРОЙКИ", "ВЫХОД"];

/** Вывеска: надстрочник, крупное слово, метка стадии. Рисуется силуэтом — предмет выбора здесь
 *  место и размер, а не гарнитура. */
function wordmark(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  align: CanvasTextAlign = "left"
): void {
  w.box(ctx, r, width, height, { hollow: true, dashed: true });
  const cx = align === "center" ? r.x + r.w / 2 : r.x + 0.012;
  w.text(ctx, "HAPPY", { x: cx, y: r.y + r.h * 0.28 }, width, height, {
    size: 8,
    align,
    color: w.WIRE.dim
  });
  w.text(ctx, "GUILDMASTERS", { x: cx, y: r.y + r.h * 0.6 }, width, height, { size: 14, align });
  w.text(ctx, "DEMO", { x: cx, y: r.y + r.h * 0.87 }, width, height, {
    size: 8,
    align,
    color: w.WIRE.dim
  });
}

/** Пункт меню. Плашка — шесть рефов из четырнадцати, голый текст — восемь. */
function item(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  label: string,
  opts: { plate?: boolean; lit?: boolean; align?: CanvasTextAlign } = {}
): void {
  if (opts.plate) {
    w.box(ctx, r, width, height, { lit: opts.lit, label, size: 9 });
    return;
  }
  const align = opts.align ?? "left";
  const x = align === "center" ? r.x + r.w / 2 : r.x;
  w.text(ctx, label, { x, y: r.y + r.h / 2 }, width, height, {
    size: 10,
    align,
    color: opts.lit ? w.WIRE.text : w.WIRE.dim
  });
  if (opts.lit) {
    // Выбранный пункт СДВИГАЕТСЯ вбок — приём №1 разбора: 9 Kings +13px, CotDG +24px.
    // Маркер ставится от КРАЯ блока, а не от точки текста: у центрированного пункта точка текста
    // лежит в середине слова, и маркер печатался прямо поверх букв.
    const markerX = align === "center" ? r.x - 0.012 : x - 0.022;
    w.text(ctx, "‹", { x: markerX, y: r.y + r.h / 2 }, width, height, {
      size: 10,
      color: w.WIRE.accent
    });
  }
}

function versionStamp(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.text(ctx, "0.0.4-dev.06a603d", { x: 0.008, y: 0.955 }, width, height, {
    size: 7,
    color: w.WIRE.dim
  });
}

/* ---------- А: колонка у левой кромки, пункты пластинами (то, что в игре) ---------- */

function drawLeftPlates(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.35);
  // Вуаль под колонкой — локальное затемнение, не заливка кадра: так делают пятеро рефов из восьми,
  // кладущих пункты прямо на фон.
  const grad = ctx.createLinearGradient(0, 0, width * 0.85, 0);
  grad.addColorStop(0, "rgba(10,10,12,0.75)");
  grad.addColorStop(1, "rgba(10,10,12,0)");
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, width, height);

  wordmark(ctx, { x: 0.07, y: 0.24, w: 0.21, h: 0.15 }, width, height);
  ITEMS.forEach((label, i) => {
    item(ctx, { x: 0.088, y: 0.45 + i * 0.075, w: 0.167, h: 0.06 }, width, height, label, {
      plate: true,
      lit: i === 0
    });
  });
  versionStamp(ctx, width, height);
  w.measure(ctx, { x: 0.088, y: 0.75, w: 0.167, h: 0.06 }, "16.7%", width, height);
}

/* ---------- Б: колонка на вертикальной плите (реф Curse of the Dead Gods) ---------- */

function drawSlab(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  // Плита x 5.5–47.8% во всю высоту, непрозрачная. Правый край у рефа рваный (силуэт), здесь ровный:
  // рваность — это уже визуал, а не расстановка.
  w.box(ctx, { x: 0, y: 0, w: 0.478, h: 1 }, width, height, { lit: false });
  // Орнаментальная полоса по левому краю плиты — 22px у рефа, у нас место под неё.
  w.box(ctx, { x: 0.055, y: 0, w: 0.012, h: 1 }, width, height, { dashed: true, hollow: true });

  wordmark(ctx, { x: 0.102, y: 0.117, w: 0.34, h: 0.16 }, width, height);
  ITEMS.forEach((label, i) => {
    item(ctx, { x: 0.116, y: 0.36 + i * 0.048, w: 0.3, h: 0.045 }, width, height, label, {
      lit: i === 1
    });
  });
  w.callout(
    ctx,
    { x: 0.48, y: 0.5 },
    { x: 0.56, y: 0.44 },
    "правый край плиты: у рефа рваный силуэт",
    width,
    height
  );
  versionStamp(ctx, width, height);
  w.measure(ctx, { x: 0, y: 0.36, w: 0.478, h: 0.3 }, "47.8%", width, height, "x", "before");
}

/* ---------- В: список по центру экрана (рефы Eldest Souls, RoR Returns) ---------- */

function drawCenter(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.5);

  wordmark(ctx, { x: 0.37, y: 0.076, w: 0.26, h: 0.105 }, width, height, "center");
  ITEMS.forEach((label, i) => {
    // Ритм рваный намеренно: увеличенный зазор делит игровые пункты и служебные — приём №2 разбора
    // (Eldest Souls 81px против 41, REPO 64 против 48).
    const gap = i >= 2 ? 0.03 : 0;
    item(ctx, { x: 0.397, y: 0.28 + i * 0.057 + gap, w: 0.205, h: 0.05 }, width, height, label, {
      plate: true,
      lit: i === 0
    });
  });
  versionStamp(ctx, width, height);
  w.measure(ctx, { x: 0.397, y: 0.28, w: 0.205, h: 0.05 }, "20.5%", width, height, "x", "before");
}

/* ---------- Г: центр левой половины, крупная вывеска (реф Hades II) ---------- */

function drawHalf(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.3);

  // Вывеска занимает 37.5% высоты — самая крупная на всех рефах.
  wordmark(ctx, { x: 0.059, y: 0.118, w: 0.402, h: 0.375 }, width, height, "center");
  // Три пункта вместо пяти: всё прочее уезжает на следующий экран. Центр списка x≈26%.
  ["ИГРАТЬ", "НАСТРОЙКИ", "ВЫХОД"].forEach((label, i) => {
    item(ctx, { x: 0.16, y: 0.58 + i * 0.081, w: 0.2, h: 0.07 }, width, height, label, {
      align: "center",
      lit: i === 0
    });
  });
  w.callout(
    ctx,
    { x: 0.37, y: 0.7 },
    { x: 0.47, y: 0.78 },
    "«Профиль» и «Присоединиться» уходят вглубь",
    width,
    height
  );
  versionStamp(ctx, width, height);
  w.measure(ctx, { x: 0.059, y: 0.118, w: 0.402, h: 0.375 }, "40.2%", width, height, "x", "before");
}

/* ---------- Д: меню внизу слева, вывеска в верхнем углу (реф Mewgenics) ---------- */

function drawBottomLeft(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.3);

  // Вывеска вплотную к верхнему углу: у рефа 14px от кромки, то есть 1.9% высоты.
  wordmark(ctx, { x: 0.018, y: 0.019, w: 0.348, h: 0.12 }, width, height);
  ITEMS.forEach((label, i) => {
    item(ctx, { x: 0.034, y: 0.5 + i * 0.094, w: 0.24, h: 0.08 }, width, height, label, {
      lit: i === 0
    });
  });
  versionStamp(ctx, width, height);
  w.callout(
    ctx,
    { x: 0.28, y: 0.55 },
    { x: 0.4, y: 0.5 },
    "кегль пункта вдвое крупнее обычного",
    width,
    height
  );
  w.measure(ctx, { x: 0.034, y: 0.5, w: 0.24, h: 0.08 }, "24%", width, height, "x", "before");
}

/* ---------- Е: два списка — игровой слева, служебный у правого края (реф Brotato) ---------- */

function drawTwoLists(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.4);

  wordmark(ctx, { x: 0.44, y: 0.086, w: 0.5, h: 0.15 }, width, height, "center");

  ["СОЗДАТЬ ИГРУ", "ПРИСОЕДИНИТЬСЯ", "ПРОФИЛЬ", "НАСТРОЙКИ", "ВЫХОД"].forEach((label, i) => {
    item(ctx, { x: 0.016, y: 0.52 + i * 0.069, w: 0.14, h: 0.058 }, width, height, label, {
      plate: true,
      lit: i === 0
    });
  });

  // Вторичный список у правой кромки с выключкой вправо. Внешние ссылки живут здесь, а не в общем
  // столбце: у рефа это Модификации / Рассылка / Сообщество.
  ["DISCORD", "ВИКИ", "АВТОРЫ"].forEach((label, i) => {
    item(ctx, { x: 0.84, y: 0.46 + i * 0.083, w: 0.14, h: 0.06 }, width, height, label, {
      align: "right"
    });
  });
  w.callout(
    ctx,
    { x: 0.83, y: 0.5 },
    { x: 0.66, y: 0.42 },
    "цена приёма: два ритма читаются как две системы",
    width,
    height,
    "right"
  );
  versionStamp(ctx, width, height);
  w.measure(ctx, { x: 0.016, y: 0.52, w: 0.14, h: 0.058 }, "14%", width, height, "x", "before");
}

const section: SectionDef = {
  id: "ui-mainmenu",
  title: "Главное меню",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Шесть раскладок по четырнадцати рефам. Вариант А — то, что стоит в игре сейчас; остальные пять " +
    "показывают, чем именно расстановка могла бы быть другой и чем каждая из них платит. Числа — " +
    "доли экрана 1920x1080 из разбора <code>_teardowns/01-main-menu.md</code>.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "common",
      title: "Что делают ВСЕ четырнадцать",
      lede:
        "Эти четыре вещи не являются предметом выбора: они соблюдены в каждом чертеже ниже, потому " +
        "что их не нарушает ни один реф класса."
    },
    {
      kind: "table",
      head: ["Правило", "Подтверждение"],
      rows: [
        ["Один вертикальный список, без вложенности", "14 из 14"],
        ["Фон занимает весь кадр", "14 из 14; плоской заливки нет ни у кого"],
        ["Вывеска и список не пересекаются", "зазор от 6 px (Guildrun) до 100 px (Eldest Souls)"],
        ["«Выход» — последний пункт", "14 из 14, даже когда перекрашен (Factorio)"],
        ["Меню в левой трети", "10 из 14 — не правило, но сильное большинство"],
        ["Версия мелким кеглем в углу", "13 из 14; нет только у Eldest Souls"]
      ]
    },
    {
      kind: "head",
      id: "layouts",
      title: "Шесть раскладок",
      lede:
        "А стоит в игре с 04.08.2026 и здесь участвует наравне с прочими. Остальные ждут вердикта."
    },
    {
      kind: "stands",
      items: [
        {
          id: "left-plates",
          status: "note",
          tag: "сейчас в игре",
          title: "А · Колонка у левой кромки, пластины",
          note:
            "Пункты пластинами у левой кромки поверх живого боя, под колонкой локальная вуаль. " +
            "Правые две трети кадра остаются игре.",
          facts: [
            ["отступ слева", "7%"],
            ["пластина", "16.7% ширины"],
            ["шаг", "6.3%"],
            ["затемнение", "локальное, под колонкой"]
          ],
          verdict:
            "Кадр остаётся игрой, а не подложкой под интерфейс. Цена — правая половина ничем не занята, и композиция держится только на том, что происходит в бою.",
          decision: "2026-08-04 (2)",
          size: [480, 270],
          draw: drawLeftPlates
        },
        {
          id: "slab",
          status: "waiting",
          title: "Б · Колонка на вертикальной плите",
          tag: "реф Curse of the Dead Gods",
          note:
            "Под меню — непрозрачная плита во всю высоту экрана, с орнаментальной полосой по левому " +
            "краю. Фон виден справа от неё в полную яркость.",
          facts: [
            ["плита", "до 47.8% ширины"],
            ["текст пункта", "с 11.6%"],
            ["шаг", "4.8% — самый плотный"],
            ["фон", "не затемняется вовсе"]
          ],
          verdict:
            "Даёт вывеске и пунктам гарантированную подложку: яркость под текстом больше не зависит от того, что творится в бою. Цена — половина кадра закрыта наглухо, и это ровно та «отдельная программа», от которой мы уходили.",
          size: [480, 270],
          draw: drawSlab
        },
        {
          id: "center",
          status: "waiting",
          title: "В · Список по центру",
          tag: "рефы Eldest Souls, RoR Returns",
          note:
            "Вывеска сверху по центру, список под ней. Ритм рваный: увеличенный зазор делит игровые " +
            "пункты и служебные вместо разделительной линии.",
          facts: [
            ["список", "20.5% ширины"],
            ["центр", "50% экрана"],
            ["шаг", "5.7%"],
            ["разрыв групп", "×1.5 от шага"]
          ],
          verdict:
            "Симметрия читается спокойнее всего и не спорит с тем, что происходит по краям кадра. Цена — центр это ровно то место, куда мы поставили бой: список ляжет поверх дерущихся.",
          size: [480, 270],
          draw: drawCenter
        },
        {
          id: "half",
          status: "waiting",
          title: "Г · Центр левой половины, крупная вывеска",
          tag: "реф Hades II",
          note:
            "Вывеска на 37.5% высоты — самая крупная на всех рефах, под ней ТРИ пункта. Всё " +
            "остальное уезжает на следующий экран.",
          facts: [
            ["вывеска", "40.2% ширины, 37.5% высоты"],
            ["пунктов", "3"],
            ["центр списка", "26% экрана"],
            ["шаг", "8.1% — самый широкий"]
          ],
          verdict:
            "Меню перестаёт быть списком и становится обложкой: игра называет себя громче, чем предлагает действия. Цена — «Присоединиться» и «Профиль» прячутся на шаг вглубь, а кооп у нас именно та вещь, которую надо показывать первой.",
          size: [480, 270],
          draw: drawHalf
        },
        {
          id: "bottom",
          status: "waiting",
          title: "Д · Меню внизу слева, вывеска в углу",
          tag: "рефы Mewgenics, Brotato",
          note:
            "Вывеска вплотную к верхнему углу, список прижат к низу. Верхняя половина кадра " +
            "полностью отдана картинке.",
          facts: [
            ["вывеска", "от 1.9% высоты"],
            ["список", "с 50% высоты"],
            ["шаг", "9.4% — крупный кегль"],
            ["отступ слева", "3.4%"]
          ],
          verdict:
            "Диагональ «вывеска сверху — список снизу» открывает середину кадра целиком: бой видно лучше, чем в любом другом варианте. Цена — при крупном кегле пункт спорит с вывеской за первый взгляд (у Mewgenics отношение 2:1, худшее на всех рефах).",
          size: [480, 270],
          draw: drawBottomLeft
        },
        {
          id: "two-lists",
          status: "waiting",
          title: "Е · Два списка: игровой и служебный",
          tag: "реф Brotato",
          note:
            "Игровые пункты слева внизу, внешние ссылки — у правой кромки с выключкой вправо. " +
            "Правая часть кадра перестаёт пустовать.",
          facts: [
            ["левый список", "14% ширины"],
            ["правый", "выключка к 98%"],
            ["шаг слева", "6.9%"],
            ["шаг справа", "8.3%"]
          ],
          verdict:
            "Единственный вариант, который занимает правую сторону кадра чем-то осмысленным. Цена названа прямо в разборе: у рефа два ритма и два кегля, и колонки перестают читаться как одна система; плюс правый низ — угол, куда Steam кладёт свою всплывашку.",
          size: [480, 270],
          draw: drawTwoLists
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Чего избегать — из раздела «чего избегать» разбора:</b> служебная строка в самом заметном " +
        "месте (Guildrun держит версию в верхнем центре) · пустая нижняя половина (RoRR: список " +
        "кончается на 58% высоты) · тёмный текст на неподготовленном ярком арте (Roboquest теряет " +
        "нижние пункты) · перегруженные углы (AoW4 держит пять служебных блоков вокруг списка из " +
        "восьми пунктов)."
    }
  ]
};

export default section;
