/* Создать игру и лобби: три раскладки.

   Источник — `Art_Dev/UI Refs/_teardowns/06-entry-service-coop.md` §2, пять рефов. Три факта
   оттуда, общих для всех и потому не выносимых на выбор:

     · СВОБОДНЫЙ СЛОТ ПОКАЗАН КНОПКОЙ «+», а не пустой рамкой под будущего игрока. Так у обоих
       рефов серии Risk of Rain; пустых слотов не рисует никто.
     · КНОПКА СТАРТА ЖИВЁТ В ПАНЕЛИ НАСТРОЕК, а не в панели игроков — у обеих игр серии.
     · МЕТКА ХОСТА — КОРОНА НА ПОРТРЕТЕ или в строке игрока. Ни у кого нет отдельной подписи
       «хост»: роль показана значком, а не словом.

   Что в игре сейчас: `NewGameScreen.uxml` — экран «Создать игру», где выбирается режим, дом и
   открытость для друзей. Лобби как отдельной панели у нас нет; вход в кооп идёт рукопожатием
   через Steam. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

const OPTIONS: Array<[string, string]> = [
  ["Режим", "‹ Кампания ›"],
  ["Дом", "‹ Чёрный шип ›"],
  ["Видимость", "‹ Для друзей ›"],
  ["Лимит игроков", "‹ 2 ›"],
  ["Пароль", "поле ввода"]
];

/** Строка опции сессии: подпись слева, виджет справа, без фоновой плашки. */
function optionRow(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  label: string,
  value: string
): void {
  w.text(ctx, label, { x: r.x + 0.01, y: r.y + r.h / 2 }, width, height, { size: 9, color: w.WIRE.dim });
  w.box(ctx, { x: r.x + r.w - 0.155, y: r.y + r.h / 2 - 0.021, w: 0.155, h: 0.042 }, width, height, {
    label: value,
    size: 8
  });
}

/** Строка игрока: портрет, ник, корона у хоста. */
function player(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  name: string,
  host = false
): void {
  w.box(ctx, r, width, height, {});
  w.box(ctx, { x: r.x + 0.004, y: r.y + 0.006, w: 0.03, h: r.h - 0.012 }, width, height, {
    hollow: true,
    dashed: true
  });
  w.text(ctx, name, { x: r.x + 0.042, y: r.y + r.h / 2 }, width, height, { size: 9 });
  if (host) {
    w.text(ctx, "корона", { x: r.x + r.w - 0.01, y: r.y + r.h / 2 }, width, height, {
      size: 7,
      align: "right",
      color: w.WIRE.accent
    });
  }
}

/* ---------- А: настройки слева, лобби панелью справа (реф Risk of Rain 2) ---------- */

function drawSideBySide(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  w.text(ctx, "СОЗДАТЬ ИГРУ", { x: 0.05, y: 0.07 }, width, height, { size: 12 });

  // Панель настроек 26.5% ширины — числа RoR2; старт стоит ПОСЛЕДНЕЙ СТРОКОЙ внутри неё.
  const settings: w.Rect = { x: 0.05, y: 0.12, w: 0.4, h: 0.72 };
  w.box(ctx, settings, width, height, {});
  w.text(ctx, "НАСТРОЙКИ СЕССИИ", { x: 0.07, y: 0.17 }, width, height, { size: 9, color: w.WIRE.accent });
  OPTIONS.forEach(([label, value], i) => {
    optionRow(ctx, { x: 0.07, y: 0.21 + i * 0.075, w: 0.36, h: 0.065 }, width, height, label, value);
  });
  w.box(ctx, { x: 0.07, y: 0.73, w: 0.36, h: 0.07 }, width, height, {
    label: "НАЧАТЬ ИГРУ",
    size: 10,
    lit: true
  });

  // Панель лобби 19.9% в правом верхнем углу.
  const lobby: w.Rect = { x: 0.75, y: 0.12, w: 0.2, h: 0.36 };
  w.box(ctx, lobby, width, height, {});
  w.text(ctx, "ЛОББИ  1/2", { x: 0.77, y: 0.16 }, width, height, { size: 9 });
  player(ctx, { x: 0.765, y: 0.19, w: 0.17, h: 0.05 }, width, height, "xGaida", true);
  // Свободное место — кнопка «+», а не рамка под будущего игрока.
  w.box(ctx, { x: 0.765, y: 0.25, w: 0.03, h: 0.045 }, width, height, { label: "+", size: 12 });
  w.box(ctx, { x: 0.765, y: 0.33, w: 0.17, h: 0.045 }, width, height, { label: "Пригласить", size: 8 });
  w.box(ctx, { x: 0.765, y: 0.385, w: 0.17, h: 0.045 }, width, height, { label: "Покинуть", size: 8 });

  // Подсказка к сфокусированной строке — в пустоте справа от панели настроек, как у рефа.
  w.text(ctx, "подсказка к строке, на которой стоит фокус", { x: 0.48, y: 0.25 }, width, height, {
    size: 8,
    color: w.WIRE.dim
  });
  w.measure(ctx, settings, "40%", width, height, "x", "before");
}

/* ---------- Б: два таба в одной ленте, лобби свёрнуто в угол (реф RoR Returns) ---------- */

function drawTabs(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  // «Создать» и «Присоединиться» — не два экрана, а два таба одной ленты.
  w.box(ctx, { x: 0.03, y: 0.05, w: 0.04, h: 0.055 }, width, height, { label: "‹", size: 10 });
  w.box(ctx, { x: 0.12, y: 0.05, w: 0.17, h: 0.055 }, width, height, { label: "СОЗДАТЬ", size: 9, lit: true });
  w.box(ctx, { x: 0.31, y: 0.05, w: 0.21, h: 0.055 }, width, height, { label: "ПРИСОЕДИНИТЬСЯ", size: 9 });

  // Лобби свёрнуто в правый верхний угол одной плашкой: портрет, слово, стрелка.
  w.box(ctx, { x: 0.795, y: 0.046, w: 0.175, h: 0.054 }, width, height, {});
  w.box(ctx, { x: 0.8, y: 0.052, w: 0.028, h: 0.042 }, width, height, { hollow: true, dashed: true });
  w.text(ctx, "Лобби", { x: 0.84, y: 0.073 }, width, height, { size: 9, color: w.WIRE.accent });
  w.text(ctx, "▾", { x: 0.955, y: 0.073 }, width, height, { size: 9, align: "right" });
  w.callout(
    ctx,
    { x: 0.79, y: 0.073 },
    { x: 0.7, y: 0.16 },
    "развернётся списком по клику",
    width,
    height,
    "right"
  );

  const settings: w.Rect = { x: 0.028, y: 0.2, w: 0.37, h: 0.75 };
  w.box(ctx, settings, width, height, {});
  w.text(ctx, "НАСТРОЙКИ ИГРЫ", { x: 0.048, y: 0.245 }, width, height, { size: 9, color: w.WIRE.accent });
  OPTIONS.forEach(([label, value], i) => {
    optionRow(ctx, { x: 0.048, y: 0.29 + i * 0.075, w: 0.33, h: 0.065 }, width, height, label, value);
  });
  w.box(ctx, { x: 0.038, y: 0.865, w: 0.35, h: 0.06 }, width, height, {
    label: "НАЧАТЬ ИГРУ",
    size: 10,
    lit: true
  });
  w.measure(ctx, settings, "37%", width, height, "x", "before");
}

/* ---------- В: развилка карточками, лобби следующим шагом (реф STS2) ---------- */

function drawFork(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  w.text(ctx, "ИГРАТЬ ВМЕСТЕ", { x: 0.5, y: 0.15 }, width, height, { align: "center", size: 12 });

  // Две крупные карточки: сначала решается роль, настройки и лобби идут следующим экраном.
  [
    ["СОЗДАТЬ", "вы хост: правила ваши"],
    ["ПРИСОЕДИНИТЬСЯ", "к другу из списка Steam"]
  ].forEach(([title, sub], i) => {
    const x = 0.26 + i * 0.26;
    w.box(ctx, { x, y: 0.28, w: 0.22, h: 0.4 }, width, height, { lit: i === 0 });
    w.box(ctx, { x: x + 0.02, y: 0.32, w: 0.18, h: 0.18 }, width, height, {
      hollow: true,
      dashed: true,
      label: "знак",
      size: 8
    });
    w.text(ctx, title!, { x: x + 0.11, y: 0.55 }, width, height, { align: "center", size: 10 });
    w.text(ctx, sub!, { x: x + 0.11, y: 0.6 }, width, height, {
      align: "center",
      size: 7,
      color: w.WIRE.dim
    });
  });

  w.callout(
    ctx,
    { x: 0.5, y: 0.7 },
    { x: 0.56, y: 0.79 },
    "настройки и лобби — следующим экраном",
    width,
    height
  );
  w.box(ctx, { x: 0.03, y: 0.87, w: 0.12, h: 0.055 }, width, height, { label: "Назад", size: 9 });
  w.measure(ctx, { x: 0.26, y: 0.28, w: 0.22, h: 0.4 }, "22%", width, height, "x", "before");
}

const section: SectionDef = {
  id: "ui-lobby",
  title: "Создать игру и лобби",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Дверь в кооп. У нас она наполовину есть: экран «Создать игру» существует, а лобби как панели " +
    "нет — вход идёт рукопожатием через Steam. Пять рефов класса показывают три разных способа " +
    "свести настройки сессии и список игроков.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Что рефы делают одинаково",
      lede: "Три приёма, которые стоит взять независимо от раскладки."
    },
    {
      kind: "table",
      head: ["Приём", "Рефы", "Почему"],
      rows: [
        ["Свободное место — кнопка «+»", "RoR2 и RoR Returns", "пустых слотов не рисует никто: рамка под будущего игрока обещает то, чего нет"],
        ["Старт живёт в панели настроек", "обе игры серии", "начинает тот, кто правила и задал"],
        ["Хост помечен короной на портрете", "оба рефа", "роль показана значком, а не словом «хост»"],
        ["Вместимость — числом «1/18»", "RoR Returns", "а не количеством нарисованных слотов"],
        ["Правила совместной игры — среди настроек сессии", "RoR Returns: 3 из 9 опций", "голосование за правила, персональная сложность, запрет подбора чужого"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Наш случай уже, чем у рефов.</b> Лимит игроков у нас два, а не восемнадцать, и " +
        "приглашение идёт оверлеем Steam, а не кодом лобби. Значит панель игроков может быть " +
        "маленькой, а «скопировать идентификатор лобби» нам не нужен вовсе — эту строку из рефов " +
        "переносить не надо."
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede: "Все три ждут вердикта. Доли экрана — 1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "side",
          status: "waiting",
          title: "А · Настройки слева, лобби справа",
          tag: "реф Risk of Rain 2",
          note:
            "Панель настроек сессии слева, панель игроков в правом верхнем углу, подсказка к " +
            "сфокусированной строке — в пустоте между ними. Старт последней строкой в настройках.",
          facts: [
            ["панель настроек", "40% ширины"],
            ["панель лобби", "20% ширины"],
            ["старт", "внутри настроек"],
            ["свободное место", "кнопкой «+»"]
          ],
          verdict:
            "Всё на одном экране: и что за игра, и кто в ней. Цена — при двух игроках панель лобби почти пустая, а это самый заметный угол кадра.",
          size: [480, 270],
          draw: drawSideBySide
        },
        {
          id: "lobby-tabs",
          status: "waiting",
          title: "Б · Два таба, лобби свёрнуто в угол",
          tag: "реф RoR Returns",
          note:
            "«Создать» и «Присоединиться» — не два экрана, а два таба одной ленты. Лобби свёрнуто в " +
            "плашку в углу и разворачивается по клику.",
          facts: [
            ["лента табов", "y 5–10%"],
            ["панель настроек", "37% ширины"],
            ["лобби свёрнуто", "17.5% x 5.4%"],
            ["старт", "во всю ширину панели"]
          ],
          verdict:
            "Снимает развилку из главного меню: обе двери ведут на один экран, и переключиться можно не выходя. Цена — свёрнутое лобби прячет самое интересное (кто уже здесь) за клик.",
          size: [480, 270],
          draw: drawTabs
        },
        {
          id: "fork",
          status: "waiting",
          title: "В · Развилка карточками",
          tag: "реф Slay the Spire 2",
          note:
            "Сначала выбирается роль двумя крупными карточками, настройки и лобби идут следующим " +
            "экраном. Ближе всего к тому, что у нас в главном меню сейчас.",
          facts: [
            ["карточка", "22% x 40%"],
            ["шагов", "2"],
            ["настроек на первом экране", "0"],
            ["лобби", "следующим шагом"]
          ],
          verdict:
            "Самый простой первый экран: два больших решения и ничего лишнего. Цена — лишний шаг для хоста, который каждый раз идёт одним и тем же путём.",
          size: [480, 270],
          draw: drawFork
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> нужна ли панель лобби вообще при лимите в два игрока · остаётся ли выбор " +
        "дома на этом экране (сейчас он там) · показываем ли готовность каждого игрока и где · " +
        "переносим ли «правила совместной игры» в настройки сессии, как у RoR Returns."
    }
  ]
};

export default section;
