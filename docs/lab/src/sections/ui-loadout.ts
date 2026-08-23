/* Сбор отряда: три раскладки.

   Рефа класса нет — ни в одном из семи разобранных. Зато есть своя история: экран переделывался
   04.08.2026, и уроки той переделки живут в памяти агента (`loadout-screen-redesign`). Ближайшие
   родственники по форме — лавка (сетка предметов и панель деталей) и экран выбора гильдии (список
   слева, панель справа).

   Отличие от них, решающее форму: здесь игрок не выбирает ОДНО из нескольких, а СОБИРАЕТ состав —
   четыре «Сосуда», у каждого Реликвия, предметы и профиль поведения. То есть экран отвечает сразу
   на четыре вопроса, и раскладка решает, какой из них главный.

   Что в игре сейчас: `LoadoutScreen.uxml` — вкладки (реликвии, предметы, улучшения, AI), сетка
   слева, панель детали справа, кнопки принятия внизу. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Слот отряда: тело, имя, метка Реликвии. */
function slot(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  opts: { lit?: boolean; empty?: boolean; name?: string } = {}
): void {
  w.box(ctx, r, width, height, { lit: opts.lit, dashed: opts.empty });
  if (opts.empty) {
    w.text(ctx, "пусто", { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
      align: "center",
      size: 8,
      color: w.WIRE.dim
    });
    return;
  }
  w.box(ctx, { x: r.x + 0.008, y: r.y + 0.018, w: r.w - 0.016, h: r.h * 0.52 }, width, height, {
    hollow: true,
    dashed: true,
    label: "тело",
    size: 7
  });
  w.text(ctx, opts.name ?? "имя", { x: r.x + r.w / 2, y: r.y + r.h * 0.68 }, width, height, {
    align: "center",
    size: 8
  });
  w.text(ctx, "◆ реликвия", { x: r.x + r.w / 2, y: r.y + r.h * 0.82 }, width, height, {
    align: "center",
    size: 7,
    color: w.WIRE.accent
  });
}

/* ---------- А: вкладки, сетка и панель детали (то, что в игре) ---------- */

function drawTabs(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);

  const panel: w.Rect = { x: 0.06, y: 0.07, w: 0.88, h: 0.86 };
  w.box(ctx, panel, width, height, {});
  w.text(ctx, "СБОР ОТРЯДА", { x: 0.5, y: 0.115 }, width, height, { align: "center", size: 11 });

  ["РЕЛИКВИИ", "ПРЕДМЕТЫ", "УЛУЧШЕНИЯ", "ПОВЕДЕНИЕ"].forEach((t, i) => {
    w.box(ctx, { x: 0.08 + i * 0.15, y: 0.155, w: 0.14, h: 0.05 }, width, height, {
      label: t,
      size: 8,
      lit: i === 0
    });
  });

  // Сетка слева: карточки предметов рядами.
  for (let i = 0; i < 12; i++) {
    w.box(
      ctx,
      { x: 0.08 + (i % 4) * 0.115, y: 0.24 + Math.floor(i / 4) * 0.165, w: 0.1, h: 0.145 },
      width,
      height,
      { lit: i === 0 }
    );
  }
  // Панель детали справа.
  const detail: w.Rect = { x: 0.56, y: 0.24, w: 0.36, h: 0.5 };
  w.box(ctx, detail, width, height, { hollow: true });
  w.text(ctx, "НАЗВАНИЕ", { x: 0.58, y: 0.285 }, width, height, { size: 10 });
  for (let i = 0; i < 5; i++) {
    w.text(ctx, "строка описания и статов", { x: 0.58, y: 0.33 + i * 0.035 }, width, height, {
      size: 7,
      color: w.WIRE.dim
    });
  }
  w.box(ctx, { x: 0.58, y: 0.63, w: 0.32, h: 0.06 }, width, height, { label: "Взять", size: 9, lit: true });

  w.box(ctx, { x: 0.75, y: 0.85, w: 0.17, h: 0.06 }, width, height, { label: "В бой", size: 9, lit: true });
  w.callout(
    ctx,
    { x: 0.3, y: 0.6 },
    { x: 0.3, y: 0.78 },
    "состава отряда на экране не видно",
    width,
    height
  );
  // Под панелью детали: над ней стоит лента вкладок.
  w.measure(ctx, detail, "36%", width, height);
}

/* ---------- Б: отряд сверху, инвентарь снизу ---------- */

function drawSquadFirst(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);

  w.text(ctx, "СБОР ОТРЯДА", { x: 0.06, y: 0.08 }, width, height, { size: 11 });

  // Четыре слота отряда крупно сверху — экран открывается тем, ЧТО собрано, а не тем, что лежит.
  const cw = 0.19;
  const gap = 0.03;
  const left = (1 - (cw * 4 + gap * 3)) / 2;
  for (let i = 0; i < 4; i++) {
    slot(ctx, { x: left + i * (cw + gap), y: 0.14, w: cw, h: 0.36 }, width, height, {
      lit: i === 1,
      empty: i === 3,
      name: ["Ирма", "Кай", "Дан"][i]
    });
  }

  // Инвентарь снизу лентой, с фильтром по типу.
  ["ВСЁ", "РЕЛИКВИИ", "ПРЕДМЕТЫ", "ПОВЕДЕНИЕ"].forEach((t, i) => {
    w.box(ctx, { x: 0.06 + i * 0.11, y: 0.55, w: 0.1, h: 0.045 }, width, height, {
      label: t,
      size: 7,
      lit: i === 0
    });
  });
  for (let i = 0; i < 16; i++) {
    w.box(
      ctx,
      { x: 0.06 + (i % 8) * 0.107, y: 0.62 + Math.floor(i / 8) * 0.125, w: 0.095, h: 0.11 },
      width,
      height,
      { lit: i === 2 }
    );
  }
  w.box(ctx, { x: 0.8, y: 0.87, w: 0.14, h: 0.06 }, width, height, { label: "В бой", size: 9, lit: true });
  w.callout(
    ctx,
    { x: 0.5, y: 0.51 },
    { x: 0.6, y: 0.545 },
    "перетаскивание сверху вниз и обратно",
    width,
    height
  );
  w.measure(ctx, { x: left, y: 0.14, w: cw, h: 0.36 }, "19%", width, height);
}

/* ---------- В: боец слева, его снаряжение справа ---------- */

function drawPerFighter(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);

  w.text(ctx, "СБОР ОТРЯДА", { x: 0.06, y: 0.08 }, width, height, { size: 11 });

  // Слева колонка бойцов — та же раскладка, что на экранах профиля и гильдии.
  for (let i = 0; i < 4; i++) {
    const r: w.Rect = { x: 0.06, y: 0.16 + i * 0.19, w: 0.29, h: 0.16 };
    w.box(ctx, r, width, height, { lit: i === 1, dashed: i === 3 });
    if (i === 3) {
      w.text(ctx, "пустой слот", { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
        align: "center",
        size: 8,
        color: w.WIRE.dim
      });
    } else {
      w.box(ctx, { x: r.x + 0.01, y: r.y + 0.015, w: 0.05, h: r.h - 0.03 }, width, height, {
        hollow: true,
        dashed: true
      });
      w.text(ctx, ["Ирма", "Кай", "Дан"][i]!, { x: r.x + 0.075, y: r.y + 0.045 }, width, height, { size: 9 });
      w.text(ctx, "◆ реликвия · 2 предмета", { x: r.x + 0.075, y: r.y + 0.085 }, width, height, {
        size: 7,
        color: w.WIRE.dim
      });
      w.text(ctx, "держит строй", { x: r.x + 0.075, y: r.y + 0.12 }, width, height, {
        size: 7,
        color: w.WIRE.dim
      });
    }
  }

  // Справа — всё про выбранного: снаряжение, поведение, статы.
  const right: w.Rect = { x: 0.39, y: 0.16, w: 0.55, h: 0.62 };
  w.box(ctx, right, width, height, {});
  w.text(ctx, "КАЙ", { x: 0.41, y: 0.205 }, width, height, { size: 11 });
  w.text(ctx, "СНАРЯЖЕНИЕ", { x: 0.41, y: 0.26 }, width, height, { size: 8, color: w.WIRE.accent });
  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.41 + i * 0.09, y: 0.285, w: 0.08, h: 0.11 }, width, height, { lit: i === 0 });
  }
  w.text(ctx, "ПОВЕДЕНИЕ", { x: 0.41, y: 0.44 }, width, height, { size: 8, color: w.WIRE.accent });
  ["держать строй", "беречь себя", "давить"].forEach((t, i) => {
    w.box(ctx, { x: 0.41 + i * 0.13, y: 0.465, w: 0.12, h: 0.05 }, width, height, {
      label: t,
      size: 7,
      lit: i === 0
    });
  });
  w.text(ctx, "СТАТЫ", { x: 0.41, y: 0.56 }, width, height, { size: 8, color: w.WIRE.accent });
  for (let i = 0; i < 4; i++) {
    w.text(ctx, "стат", { x: 0.41, y: 0.595 + i * 0.04 }, width, height, { size: 7, color: w.WIRE.dim });
    w.text(ctx, "+12", { x: 0.63, y: 0.595 + i * 0.04 }, width, height, {
      size: 8,
      align: "right",
      color: w.WIRE.accent
    });
  }
  w.box(ctx, { x: 0.78, y: 0.85, w: 0.16, h: 0.06 }, width, height, { label: "В бой", size: 9, lit: true });
  w.measure(ctx, right, "55%", width, height);
}

const section: SectionDef = {
  id: "ui-loadout",
  title: "Сбор отряда",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Единственный экран забега, где игрок принимает решения, — и единственный без рефов класса. " +
    "Он отвечает сразу на четыре вопроса: кто идёт, с какой Реликвией, с какими предметами и с " +
    "каким поведением. Раскладка решает, какой из четырёх главный.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Чем он не похож на соседей",
      lede:
        "Лавка и награда предлагают выбрать ОДНО из нескольких. Здесь игрок СОБИРАЕТ состав, и " +
        "правильность сборки видна только целиком."
    },
    {
      kind: "table",
      head: ["Вопрос экрана", "Где ответ виден сейчас", "Замечание"],
      rows: [
        ["Кто идёт в бой", "нигде на этом экране", "состав отряда виден только в бою"],
        ["Какая Реликвия у кого", "во вкладке «Реликвии»", "связь «человек ↔ Реликвия» не показана"],
        ["Какие предметы", "во вкладке «Предметы»", "то же самое"],
        ["Какое поведение", "во вкладке «Поведение»", "профиль AI — решение, которое видно только в бою"],
        ["Хватает ли собранного", "нигде", "нет ответа на «готов ли я»"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Принцип из ГД-канона, который этот экран обязан обслуживать:</b> «решения вне боя " +
        "обязаны быть видны в бою». Обратное тоже верно — если игрок не видит на сборе, что именно " +
        "он собрал, слой подготовки обесценивается ещё до боя. Отсюда вопрос к раскладке: показан " +
        "ли на экране СОСТАВ, а не только склад."
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
          id: "tabs",
          status: "waiting",
          title: "А · Вкладки, сетка и деталь",
          tag: "то, что в игре",
          note:
            "Четыре вкладки по типам, сетка карточек слева, панель детали справа. Экран отвечает " +
            "«что у меня есть», а не «что я собрал».",
          facts: [
            ["панель", "88% ширины"],
            ["вкладок", "4"],
            ["карточка", "10% x 14.5%"],
            ["деталь", "36% ширины"]
          ],
          verdict:
            "Вмещает любой объём инвентаря и растёт вкладками. Цена — состава отряда на экране нет вовсе: игрок собирает вслепую и проверяет себя уже в бою.",
          size: [480, 270],
          draw: drawTabs
        },
        {
          id: "squad",
          status: "waiting",
          title: "Б · Отряд сверху, склад снизу",
          tag: "своё",
          note:
            "Четыре слота отряда крупно в верхней половине, инвентарь лентой снизу с фильтром по " +
            "типу. Экран открывается тем, что собрано.",
          facts: [
            ["слот отряда", "19% x 36%"],
            ["инвентарь", "две строки по восемь"],
            ["фильтр", "четырьмя чипами"],
            ["движение", "перетаскиванием"]
          ],
          verdict:
            "Отвечает на «готов ли я» с первого взгляда: пустой слот виден сразу. Цена — инвентарь ужимается до плиток по 9.5% ширины, и описание предмета в них не помещается, придётся тултипом.",
          size: [480, 270],
          draw: drawSquadFirst
        },
        {
          id: "per-fighter",
          status: "waiting",
          title: "В · Боец слева, его снаряжение справа",
          tag: "как профиль и гильдия",
          note:
            "Колонка бойцов слева, справа всё про выбранного: снаряжение, поведение, статы. Тот же " +
            "приём, что на экранах профиля и гильдии.",
          facts: [
            ["строка бойца", "29% x 16%"],
            ["панель", "55% ширины"],
            ["поведение", "чипами"],
            ["связь", "человек ↔ его вещи"]
          ],
          verdict:
            "Единственный, где видно, ЧТО У КОГО: связь «человек — Реликвия — поведение» показана прямо, а не собирается в голове. Третий экран подряд с этой раскладкой — игрок уже её знает. Цена — общий склад приходится показывать отдельно, и «переложить предмет от Кая к Ирме» становится двумя действиями.",
          size: [480, 270],
          draw: drawPerFighter
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> показываем ли состав отряда на этом экране вообще · где живёт профиль " +
        "поведения — здесь или отдельным экраном · нужна ли кнопка «готов» с проверкой, что все " +
        "слоты заполнены · как выглядит экран в коопе, где отряд общий, а собирают двое."
    }
  ]
};

export default section;
