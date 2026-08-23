/* Экран настроек: три раскладки на выбор.

   Источник чисел — `Art_Dev/UI Refs/_teardowns/02-settings-pause.md`, тринадцать рефов. Оттуда же
   три факта, которые в чертежах соблюдены везде и потому не являются предметом выбора:

     · подпись СЛЕВА, виджет справа — во всех тринадцати рефах без единого исключения;
     · заголовка «Настройки» нет у девяти рефов из одиннадцати: экран уже назван табом, с которого
       на него пришли, и второе имя занимает строку зря;
     · индикатора «значение изменено, но не применено» нет НИ У КОГО (0 из 13) — если мы его
       заведём, это будет наше изобретение, а не перенятый приём.

   Что в игре сейчас: `SettingsScreen.uxml` — одна панель со строками подряд, без табов. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Строка опции: подпись слева, виджет справа. Виджет рисуется силуэтом — какой именно контрол там
 *  стоит, решается не раскладкой, а типом значения. */
function optionRow(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  label: string,
  kind: "slider" | "toggle" | "select",
  lit = false
): void {
  if (lit) w.box(ctx, r, width, height, { lit: true, hollow: false });
  w.text(ctx, label, { x: r.x + 0.012, y: r.y + r.h / 2 }, width, height, {
    size: 9,
    color: lit ? w.WIRE.text : w.WIRE.dim
  });

  // Правый край виджетов выровнен — так у пяти рефов; «плавающая» колонка значений оставлена
  // Factorio и Skul, и именно у них строки читаются хуже всего.
  const right = r.x + r.w - 0.012;
  if (kind === "slider") {
    const trackW = 0.14;
    const y = r.y + r.h / 2;
    ctx.strokeStyle = w.WIRE.line;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo((right - trackW) * width, y * height);
    ctx.lineTo(right * width, y * height);
    ctx.stroke();
    w.box(ctx, { x: right - trackW * 0.35, y: y - 0.012, w: 0.008, h: 0.024 }, width, height, { lit: true });
  } else if (kind === "toggle") {
    w.box(ctx, { x: right - 0.02, y: r.y + r.h / 2 - 0.017, w: 0.02, h: 0.034 }, width, height, {});
  } else {
    w.box(ctx, { x: right - 0.16, y: r.y + r.h / 2 - 0.02, w: 0.16, h: 0.04 }, width, height, {
      label: "‹  значение  ›",
      size: 8
    });
  }
}

const AUDIO: Array<[string, "slider" | "toggle" | "select"]> = [
  ["Общая громкость", "slider"],
  ["Музыка", "slider"],
  ["Эффекты", "slider"],
  ["Интерфейс", "slider"]
];

const VIDEO: Array<[string, "slider" | "toggle" | "select"]> = [
  ["Режим экрана", "select"],
  ["Разрешение", "select"],
  ["Кадров в секунду", "select"],
  ["Вертикальная синхронизация", "toggle"],
  ["Тряска экрана", "slider"],
  ["Вспышки при попадании", "toggle"]
];

/* ---------- А: горизонтальные табы, одна колонка ---------- */

function drawTabsTop(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  // Лента табов в верхней десятой части — у семи рефов из тринадцати она лежит на y 3–19%.
  const tabs = ["ЗВУК", "ЭКРАН", "УПРАВЛЕНИЕ", "ИГРА"];
  tabs.forEach((t, i) => {
    w.box(ctx, { x: 0.12 + i * 0.17, y: 0.08, w: 0.16, h: 0.06 }, width, height, {
      label: t,
      size: 9,
      lit: i === 1
    });
  });

  // Тело — одна колонка пар. Секции с заголовками: так делают пятеро, и это единственный способ
  // разложить полтора десятка опций без скролла.
  w.text(ctx, "ОТОБРАЖЕНИЕ", { x: 0.12, y: 0.2 }, width, height, { size: 8, color: w.WIRE.accent });
  VIDEO.forEach(([label, kind], i) => {
    optionRow(
      ctx,
      { x: 0.12, y: 0.235 + i * 0.075, w: 0.76, h: 0.065 },
      width,
      height,
      label,
      kind,
      i === 3
    );
  });

  w.box(ctx, { x: 0.12, y: 0.86, w: 0.14, h: 0.06 }, width, height, { label: "Назад", size: 9 });
  w.box(ctx, { x: 0.72, y: 0.86, w: 0.16, h: 0.06 }, width, height, { label: "Сброс", size: 9 });
  w.measure(ctx, { x: 0.12, y: 0.61, w: 0.76, h: 0.065 }, "76%", width, height);
}

/* ---------- Б: вертикальные табы слева ---------- */

function drawTabsSide(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  // Колонка табов 13.9–31.5% — числа Hades II, единственного рефа с вертикальными табами.
  const tabs = ["ЗВУК", "ЭКРАН", "УПРАВЛЕНИЕ", "ИГРА", "ЯЗЫК", "ДОСТУПНОСТЬ"];
  tabs.forEach((t, i) => {
    w.box(ctx, { x: 0.139, y: 0.16 + i * 0.09, w: 0.176, h: 0.075 }, width, height, {
      label: t,
      size: 9,
      lit: i === 0
    });
  });

  AUDIO.forEach(([label, kind], i) => {
    optionRow(ctx, { x: 0.35, y: 0.16 + i * 0.085, w: 0.53, h: 0.07 }, width, height, label, kind, i === 1);
  });
  w.text(ctx, "СМЕШЕНИЕ", { x: 0.35, y: 0.53 }, width, height, { size: 8, color: w.WIRE.accent });
  optionRow(ctx, { x: 0.35, y: 0.555, w: 0.53, h: 0.07 }, width, height, "Приглушать в фоне", "toggle");
  optionRow(ctx, { x: 0.35, y: 0.64, w: 0.53, h: 0.07 }, width, height, "Речь блипами", "toggle");

  // Панель подсказок управления внизу — приём Hades II и CotDG: клавиша плюс глагол.
  w.box(ctx, { x: 0.139, y: 0.86, w: 0.74, h: 0.06 }, width, height, { hollow: true });
  w.text(ctx, "[Esc] выход    [R] сброс    [мышь] выбрать", { x: 0.16, y: 0.89 }, width, height, {
    size: 8,
    color: w.WIRE.dim
  });
  w.measure(ctx, { x: 0.139, y: 0.16, w: 0.176, h: 0.075 }, "17.6%", width, height, "x", "before");
}

/* ---------- В: без табов, две колонки ---------- */

function drawTwoColumns(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.65);

  // Разделитель ровно по середине — так у Skul; заголовки секций в обеих колонках на одной высоте,
  // от этого колонки читаются как одна система, а не как два списка рядом.
  ctx.strokeStyle = w.WIRE.line;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(0.5 * width, 0.16 * height);
  ctx.lineTo(0.5 * width, 0.8 * height);
  ctx.stroke();

  w.text(ctx, "ЗВУК", { x: 0.08, y: 0.17 }, width, height, { size: 8, color: w.WIRE.accent });
  AUDIO.forEach(([label, kind], i) => {
    optionRow(ctx, { x: 0.08, y: 0.2 + i * 0.075, w: 0.39, h: 0.065 }, width, height, label, kind);
  });
  w.text(ctx, "ИГРА", { x: 0.08, y: 0.53 }, width, height, { size: 8, color: w.WIRE.accent });
  optionRow(ctx, { x: 0.08, y: 0.56, w: 0.39, h: 0.065 }, width, height, "Язык", "select");
  optionRow(ctx, { x: 0.08, y: 0.635, w: 0.39, h: 0.065 }, width, height, "Скорость боя", "select");

  w.text(ctx, "ЭКРАН", { x: 0.53, y: 0.17 }, width, height, { size: 8, color: w.WIRE.accent });
  VIDEO.slice(0, 4).forEach(([label, kind], i) => {
    optionRow(ctx, { x: 0.53, y: 0.2 + i * 0.075, w: 0.39, h: 0.065 }, width, height, label, kind);
  });
  w.text(ctx, "ДЖУС", { x: 0.53, y: 0.53 }, width, height, { size: 8, color: w.WIRE.accent });
  optionRow(ctx, { x: 0.53, y: 0.56, w: 0.39, h: 0.065 }, width, height, "Тряска экрана", "slider");
  optionRow(ctx, { x: 0.53, y: 0.635, w: 0.39, h: 0.065 }, width, height, "Вспышки", "toggle");

  w.box(ctx, { x: 0.08, y: 0.86, w: 0.14, h: 0.06 }, width, height, { label: "Назад", size: 9 });
  w.box(ctx, { x: 0.78, y: 0.86, w: 0.14, h: 0.06 }, width, height, { label: "Сброс", size: 9 });
  w.measure(ctx, { x: 0.08, y: 0.71, w: 0.39, h: 0.065 }, "39%", width, height);
}

const section: SectionDef = {
  id: "ui-settings",
  title: "Настройки",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Тринадцать рефов этого класса сходятся в двух вещах и расходятся во всём остальном. Сходятся: " +
    "подпись слева, виджет справа — без исключений; и заголовка у экрана нет, потому что он уже " +
    "назван табом, с которого пришли. Расходятся — в том, как разложены категории; отсюда три " +
    "раскладки ниже.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Что говорят рефы",
      lede:
        "Число опций решает раскладку сильнее вкуса: у кого их шесть — обходятся без табов, у кого " +
        "тридцать — делят на колонки или вкладки."
    },
    {
      kind: "table",
      head: ["Схема", "Рефы", "Опций видно сразу"],
      rows: [
        ["Горизонтальные табы", "AoW4, CotDG, Guildrun, RoR2, RoRR, Roboquest, StS2 — семь", "6–13, у пятерых со скроллом"],
        ["Вертикальные табы слева", "Hades II — один, 8 вкладок", "7"],
        ["Без табов, две колонки", "Factorio (30), Skul (15)", "всё сразу, скролла нет"],
        ["Секции с заголовками внутри вкладки", "AoW4, Factorio, RoRR, Roboquest, Skul — пять", "—"],
        ["Плоский список без секций", "CotDG, Guildrun, Hades II, RoR2, StS2 — пять", "—"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Ни один реф из тринадцати не показывает, что значение изменено и ещё не применено.</b> " +
        "Отложенное применение вообще есть только у AoW4 и Factorio; у остальных настройка вступает " +
        "в силу сразу. Это стоит держать в голове: наш экран настроек применяет всё немедленно, и " +
        "это не упущение, а норма класса."
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede:
        "Выбраны горизонтальные табы (Макс, 05.08.2026) — самая частая схема класса. Доли экрана — " +
        "1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "tabs-top",
          status: "accepted",
          decision: "2026-08-05",
          title: "А · Горизонтальные табы",
          tag: "семь рефов из тринадцати",
          note:
            "Лента табов сверху, под ней одна колонка пар «подпись — виджет» с заголовками секций. " +
            "Самая частая схема класса.",
          facts: [
            ["табы", "лента на y 8–14%"],
            ["колонка", "76% ширины"],
            ["виджеты", "правый край выровнен"],
            ["секции", "заголовком, без линеек"]
          ],
          verdict:
            "Знакомо всем и растёт вширь: новая вкладка не двигает раскладку. Цена — при четырёх опциях в разделе вкладка выглядит пустой, а у нас звук именно такой.",
          size: [480, 270],
          draw: drawTabsTop
        },
        {
          id: "tabs-side",
          status: "rejected",
          title: "Б · Вертикальные табы слева",
          tag: "реф Hades II",
          note:
            "Колонка вкладок слева, опции справа, внизу панель подсказок управления. " +
            "Единственный реф с этой схемой держит в ней восемь вкладок.",
          facts: [
            ["колонка табов", "17.6% ширины"],
            ["тело", "53% ширины"],
            ["вкладок помещается", "8 без скролла"],
            ["подсказки", "полосой внизу"]
          ],
          verdict:
            "Вмещает вдвое больше разделов и читается как оглавление. Цена — тело сужается, и длинная подпись опции упирается в виджет раньше, чем в вариантах А и В.",
          size: [480, 270],
          draw: drawTabsSide
        },
        {
          id: "two-col",
          status: "rejected",
          title: "В · Две колонки без табов",
          tag: "рефы Skul и Factorio",
          note:
            "Всё на одном экране: две колонки, разделитель по середине, заголовки секций в обеих " +
            "колонках на одной высоте. Ни табов, ни скролла.",
          facts: [
            ["колонка", "39% ширины каждая"],
            ["разделитель", "ровно 50%"],
            ["опций сразу", "12–15"],
            ["скролл", "не нужен"]
          ],
          verdict:
            "Ни одного лишнего клика: всё видно разом, и это честно отражает наш объём настроек. Цена — потолок близко: как только опций станет больше двадцати, колонки придётся резать вкладками, то есть переделывать экран.",
          size: [480, 270],
          draw: drawTwoColumns
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> где стоит «Назад» — у рефов единого места нет вовсе (низ-центр, низ-лево, " +
        "низ-право, левый край середины) · нужен ли «Сброс по умолчанию» отдельной кнопкой (есть у " +
        "семи из тринадцати, и нигде не выделен цветом) · показываем ли строку «нужен перезапуск» " +
        "(единственный реф — CotDG, красной строкой внизу) · попадают ли тумблеры джуса в настройки " +
        "или остаются девелоперскими."
    }
  ]
};

export default section;
