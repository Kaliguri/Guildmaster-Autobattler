/* Экран привала: три раскладки.

   Рефа этого класса, как и у сундука, НЕТ ни одного. Ближайшие соседи — событие (плашки выбора) и
   лавка (трата ресурса), и раскладки ниже растут из них.

   Отличие привала от обоих, которое и решает форму: у события выбор ОДИН и он необратим, в лавке
   выбор МНОГОКРАТНЫЙ и ограничен деньгами, а привал — это многократный выбор, ограниченный
   бюджетом действий И адресованный конкретным людям («полечить ЕГО», «тренировать ЕЁ»). Ни
   событие, ни лавка адресата не имеют.

   Что в игре сейчас: `CampScreen.uxml` — тело текста, строка бюджета и список действий; это
   раскладка события, у которой отобрали иллюстрацию. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Боец: рамка, полоса здоровья, имя. Тела нужны там, где действие адресуется человеку. */
function fighter(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  opts: { lit?: boolean; hp?: number; name?: string } = {}
): void {
  w.box(ctx, r, width, height, { lit: opts.lit });
  w.box(ctx, { x: r.x + 0.008, y: r.y + 0.02, w: r.w - 0.016, h: r.h * 0.5 }, width, height, {
    hollow: true,
    dashed: true,
    label: "тело",
    size: 7
  });
  const hp = opts.hp ?? 1;
  const barY = r.y + r.h * 0.6;
  w.box(ctx, { x: r.x + 0.008, y: barY, w: r.w - 0.016, h: 0.018 }, width, height, { hollow: true });
  w.box(ctx, { x: r.x + 0.008, y: barY, w: (r.w - 0.016) * hp, h: 0.018 }, width, height, { lit: true });
  w.text(ctx, opts.name ?? "имя", { x: r.x + r.w / 2, y: r.y + r.h * 0.82 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.dim
  });
}

/** Действие с ценой в бюджете. Цена пишется на самой плашке — приём кнопки реролла из лавки. */
function action(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  title: string,
  cost: string,
  lit = false
): void {
  w.box(ctx, r, width, height, { lit });
  w.text(ctx, title, { x: r.x + 0.014, y: r.y + r.h / 2 }, width, height, { size: 9 });
  w.text(ctx, cost, { x: r.x + r.w - 0.014, y: r.y + r.h / 2 }, width, height, {
    size: 9,
    align: "right",
    color: w.WIRE.accent
  });
}

/* ---------- А: как событие — текст и список действий ---------- */

function drawAsEvent(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.55);

  const panel: w.Rect = { x: 0.28, y: 0.1, w: 0.44, h: 0.8 };
  w.box(ctx, panel, width, height, {});
  w.text(ctx, "ПРИВАЛ", { x: 0.5, y: 0.15 }, width, height, { align: "center", size: 11 });
  // Бюджет — единственная величина, за которой игрок следит весь экран, поэтому он в шапке.
  w.text(ctx, "Действий осталось: 2", { x: 0.5, y: 0.2 }, width, height, {
    align: "center",
    size: 9,
    color: w.WIRE.accent
  });

  for (let i = 0; i < 3; i++) {
    w.text(ctx, "строка текста привала", { x: 0.31, y: 0.26 + i * 0.034 }, width, height, {
      size: 8,
      color: w.WIRE.dim
    });
  }
  [
    ["Перевязать раны", "1 действие"],
    ["Наточить оружие", "1 действие"],
    ["Разослать разведку", "1 действие"],
    ["Отдых до утра", "все"]
  ].forEach(([t, c], i) => {
    action(ctx, { x: 0.31, y: 0.4 + i * 0.1, w: 0.38, h: 0.08 }, width, height, t!, c!, i === 0);
  });
  w.callout(
    ctx,
    { x: 0.7, y: 0.45 },
    { x: 0.73, y: 0.33 },
    "адресата нет: действие относится к отряду",
    width,
    height
  );
  w.measure(ctx, panel, "44%", width, height, "x", "before");
}

/* ---------- Б: отряд телами, действие адресуется бойцу ---------- */

function drawRoster(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.45);

  w.text(ctx, "ПРИВАЛ", { x: 0.08, y: 0.11 }, width, height, { size: 12 });
  w.text(ctx, "Действий осталось: 2", { x: 0.92, y: 0.11 }, width, height, {
    align: "right",
    size: 10,
    color: w.WIRE.accent
  });

  // Четыре бойца телами в ряд — привал это работа с людьми, а не с меню.
  const cw = 0.17;
  const gap = 0.035;
  const left = (1 - (cw * 4 + gap * 3)) / 2;
  for (let i = 0; i < 4; i++) {
    fighter(ctx, { x: left + i * (cw + gap), y: 0.22, w: cw, h: 0.34 }, width, height, {
      lit: i === 1,
      hp: [1, 0.4, 0.75, 0.9][i],
      name: ["Ирма", "Кай", "Дан", "Лея"][i]
    });
  }

  // Действия — под выбранным бойцом, а не общим списком: адресат виден без чтения.
  const sel = left + (cw + gap);
  w.box(ctx, { x: sel - 0.06, y: 0.6, w: cw + 0.12, h: 0.24 }, width, height, { hollow: true });
  w.text(ctx, "ЧТО СДЕЛАТЬ С КАЕМ", { x: sel + cw / 2, y: 0.64 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.accent
  });
  [
    ["Перевязать", "1"],
    ["Тренировать", "1"]
  ].forEach(([t, c], i) => {
    action(ctx, { x: sel - 0.05, y: 0.68 + i * 0.07, w: cw + 0.1, h: 0.06 }, width, height, t!, c!, i === 0);
  });
  w.callout(
    ctx,
    { x: sel + cw + 0.07, y: 0.72 },
    { x: 0.8, y: 0.62 },
    "полоса говорит, кому нужнее",
    width,
    height
  );
  w.measure(ctx, { x: left, y: 0.22, w: cw, h: 0.34 }, "17%", width, height, "x", "before");
}

/* ---------- В: ряд карточек-действий ---------- */

function drawCards(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.55);

  w.text(ctx, "ПРИВАЛ", { x: 0.5, y: 0.1 }, width, height, { align: "center", size: 12 });
  w.text(ctx, "Действий осталось: 2", { x: 0.5, y: 0.145 }, width, height, {
    align: "center",
    size: 9,
    color: w.WIRE.accent
  });

  // Те же карточки, что на экране награды: привал читается как ещё один выбор из трёх.
  const cw = 0.181;
  const gap = 0.028;
  const left = (1 - (cw * 3 + gap * 2)) / 2;
  ["ПЕРЕВЯЗАТЬ", "НАТОЧИТЬ", "РАЗВЕДКА"].forEach((t, i) => {
    const x = left + i * (cw + gap);
    w.box(ctx, { x, y: 0.22, w: cw, h: 0.5 }, width, height, { lit: i === 0 });
    w.box(ctx, { x: x + 0.008, y: 0.24, w: cw - 0.016, h: 0.16 }, width, height, {
      hollow: true,
      dashed: true,
      label: "знак",
      size: 8
    });
    w.text(ctx, t, { x: x + cw / 2, y: 0.43 }, width, height, { align: "center", size: 9 });
    for (let k = 0; k < 2; k++) {
      w.text(ctx, "что делает", { x: x + 0.012, y: 0.48 + k * 0.032 }, width, height, {
        size: 7,
        color: w.WIRE.dim
      });
    }
    w.text(ctx, "1 действие", { x: x + cw / 2, y: 0.66 }, width, height, {
      align: "center",
      size: 9,
      color: w.WIRE.accent
    });
  });

  // Отряд лентой внизу: адресат выбирается вторым шагом, после действия.
  w.text(ctx, "К КОМУ ПРИМЕНИТЬ", { x: 0.5, y: 0.78 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.dim
  });
  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.39 + i * 0.06, y: 0.81, w: 0.05, h: 0.08 }, width, height, { lit: i === 1 });
  }
  w.measure(ctx, { x: left, y: 0.22, w: cw, h: 0.5 }, "18.1%", width, height, "x", "before");
}

const section: SectionDef = {
  id: "ui-camp",
  title: "Привал",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Рефа класса нет, поэтому раскладки растут из соседей — события и лавки. Но привал отличается " +
    "от обоих одним: его действие адресовано конкретному человеку. Ни у события, ни у лавки " +
    "адресата нет, и именно он решает форму экрана.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Чем привал не является",
      lede:
        "Соблазн собрать его как событие велик — оно уже написано. Но события выбор один и " +
        "необратим, а привал это несколько трат ограниченного бюджета, причём адресных."
    },
    {
      kind: "table",
      head: ["", "Событие", "Лавка", "Привал"],
      rows: [
        ["Сколько выборов", "один", "сколько хватит денег", "сколько хватит действий"],
        ["Ограничитель", "нет", "золото", "бюджет действий"],
        ["Адресат", "нет", "отряд целиком", "конкретный боец"],
        ["Обратимость", "нет", "продажа возможна", "нет"],
        ["Что видно на экране", "текст и варианты", "товары и кошелёк", "люди, их состояние и бюджет"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Приём из лавки, годный сюда без изменений:</b> цена пишется прямо на кнопке действия " +
        "(«REROLL - 7» у Brotato). Тогда «сколько это стоит» не требует ни подсказки, ни памяти — " +
        "а на привале бюджет мал, и каждая трата ощутима."
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede:
        "Выбран вариант А (Макс, 05.08.2026): привал остаётся раскладкой события — текст и список " +
        "действий. Доли экрана — 1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "as-event",
          status: "accepted",
          decision: "2026-08-05",
          title: "А · Как событие: текст и список",
          tag: "то, что в игре",
          note:
            "Панель с текстом и списком действий, бюджет строкой в шапке. Адресата у действия нет: " +
            "«перевязать раны» относится к отряду вообще.",
          facts: [
            ["панель", "44% x 80%"],
            ["действие", "38% x 8%"],
            ["бюджет", "строкой в шапке"],
            ["адресат", "отсутствует"]
          ],
          verdict:
            "Дешевле всех: экран уже написан, и это буквально событие с другим заголовком. Цена — привал перестаёт быть про людей и становится ещё одним списком, а состояние отряда на нём не видно вовсе.",
          size: [480, 270],
          draw: drawAsEvent
        },
        {
          id: "roster",
          status: "rejected",
          title: "Б · Отряд телами, действие адресное",
          tag: "своё",
          note:
            "Четыре бойца телами в ряд с полосами здоровья; действия раскрываются под выбранным и " +
            "прямо называют, к кому применяются.",
          facts: [
            ["боец", "17% x 34%"],
            ["зазор", "3.5%"],
            ["действия", "под выбранным"],
            ["состояние", "полосой здоровья"]
          ],
          verdict:
            "Единственный вариант, где видно, кому помощь нужнее, — а это и есть решение привала. Цена — экран становится самым сложным из пяти этого захода и требует тел бойцов, которых на других экранах забега нет.",
          size: [480, 270],
          draw: drawRoster
        },
        {
          id: "camp-cards",
          status: "rejected",
          title: "В · Ряд карточек-действий",
          tag: "как экран награды",
          note:
            "Те же карточки, что на награде, и лента отряда внизу: сначала выбирается действие, " +
            "потом адресат.",
          facts: [
            ["карточка", "18.1% x 50%"],
            ["цена", "строкой на карточке"],
            ["отряд", "лентой внизу"],
            ["шагов", "2: что, затем кому"]
          ],
          verdict:
            "Единообразие с наградой: игрок узнаёт форму и не учится заново. Цена — два шага вместо одного, и состояние бойцов в ленте из плиток по 5% ширины толком не показать.",
          size: [480, 270],
          draw: drawCards
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> сколько действий в бюджете · адресные ли действия вообще (от этого зависит " +
        "выбор между А и Б) · показываем ли раны и усталость числом или полосой · можно ли " +
        "потратить всё на одного бойца · уходит ли привал в тот же экран, что событие, раз " +
        "механически это узел карты того же рода."
    }
  ]
};

export default section;
