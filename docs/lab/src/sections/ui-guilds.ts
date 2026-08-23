/* Экран выбора гильдии: три раскладки на выбор.

   Отличие от профиля, из которого растёт вся развилка: гильдия И ЕСТЬ слот сохранения (ТЗ
   `save-system` §3), поэтому карточка дома обязана отвечать не «сколько наиграно», а «что там
   сейчас происходит» — идёт ли забег, кто в ростере, на каком возвышении дом. Пары «Начать /
   Продолжить» нет намеренно: игрок выбирает дом, а забег в нём либо идёт, либо начнётся.

   Что в игре сейчас: `GuildSelectScreen.uxml` — панель со списком строк, слоты показываются ВСЕ,
   включая пустые, число берётся из `GameConfig.MaxGuildsPerProfile`.

   Числа раскладок — из `Art_Dev/UI Refs/_teardowns/06-entry-service-coop.md`: §1 про ряды карточек,
   §2.1 про приём «список слева, подробности сфокусированной строки справа» (Risk of Rain 2). */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Строки, которыми дом описывает себя. Держим здесь одним списком, чтобы во всех трёх чертежах
 *  дом рассказывал о себе одинаково — иначе сравнивались бы не раскладки, а наборы полей. */
const HOUSE: Array<[string, string, string]> = [
  // третья колонка — короткая запись для УЗКОЙ карточки (21.7% ширины). Заведена не для красоты:
  // на полной записи строка вылезала за кромку, и чертёж показывал раскладку, которая не работает.
  ["Забег", "идёт · Акт I, узел 4", "идёт · Акт I, 4"],
  ["Возвышение", "2 из 3", "2 из 3"],
  ["Ростер", "11 живых · 3 павших", "11 живых · 3 †"],
  ["Обновлён", "3 авг, 19:24", "3 авг, 19:24"]
];

function houseCard(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  state: "run" | "idle" | "empty",
  name: string,
  /** Сколько строк описания рисовать. Меньше четырёх — когда под карточкой ещё стоит ростер
   *  телами: наложенный на текст ряд фигур читается как дефект чертежа, а не как раскладка. */
  rows = HOUSE.length,
  /** Узкая карточка берёт короткую запись значений. */
  narrow = false
): void {
  const lit = state === "run";
  w.box(ctx, r, width, height, { lit, dashed: state === "empty" });

  const head: w.Rect = { x: r.x + 0.008, y: r.y + 0.018, w: r.w - 0.016, h: 0.052 };
  w.box(ctx, head, width, height, {
    label: state === "empty" ? "СВОБОДНО" : name,
    size: 9,
    hollow: true,
    stroke: lit ? w.WIRE.lineLit : w.WIRE.line
  });

  if (state === "empty") {
    w.text(ctx, "основать дом", { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
      align: "center",
      size: 10,
      color: w.WIRE.dim
    });
    return;
  }

  // Метка «здесь идёт забег» — единственное, что отличает дома друг от друга с первого взгляда.
  // Стоит ПОД шапкой, а не в её правом углу: в углу она налезала на имя дома, а имя — то, по чему
  // игрок дом и узнаёт.
  if (state === "run") {
    w.box(ctx, { x: r.x + 0.008, y: r.y + 0.076, w: 0.05, h: 0.042 }, width, height, {
      label: "ЗАБЕГ",
      size: 7,
      stroke: w.WIRE.accent
    });
  }

  HOUSE.slice(0, rows).forEach(([label, full, short], i) => {
    let value = narrow ? short : full;
    if (state === "idle" && i === 0) value = "нет · дом ждёт";
    w.text(ctx, label, { x: r.x + 0.014, y: r.y + 0.155 + i * 0.085 }, width, height, {
      size: 8,
      color: w.WIRE.dim
    });
    w.text(ctx, value, { x: r.x + 0.014, y: r.y + 0.197 + i * 0.085 }, width, height, { size: 9 });
  });
}

/* ---------- А: ряд карточек, как на профиле ---------- */

function drawRow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.6);

  w.text(ctx, "ГИЛЬДИЯ", { x: 0.5, y: 0.13 }, width, height, { align: "center", size: 13 });
  w.text(
    ctx,
    "дом хранит забег, ростер и открытия",
    { x: 0.5, y: 0.175 },
    width,
    height,
    { align: "center", size: 8, color: w.WIRE.dim }
  );

  const cw = 0.217;
  const gap = 0.026;
  const total = cw * 3 + gap * 2;
  const left = (1 - total) / 2;
  const states: Array<"run" | "idle" | "empty"> = ["run", "idle", "empty"];
  const names = ["ЧЁРНЫЙ ШИП", "ТИХИЙ ДВОР", ""];
  for (let i = 0; i < 3; i++) {
    houseCard(
      ctx,
      { x: left + i * (cw + gap), y: 0.25, w: cw, h: 0.5 },
      width,
      height,
      states[i]!,
      names[i]!,
      HOUSE.length,
      true
    );
  }

  w.trash(ctx, { x: left + cw / 2 - 0.017, y: 0.772, w: 0.034, h: 0.061 }, width, height);
  w.box(ctx, { x: 0.02, y: 0.86, w: 0.09, h: 0.06 }, width, height, { label: "Назад", size: 9 });
  // Над рядом: под ним стоит кнопка удаления.
  w.measure(ctx, { x: left, y: 0.25, w: cw, h: 0.5 }, "21.7%", width, height, "x", "before");
}

/* ---------- Б: список слева, дом целиком справа ---------- */

function drawListDetail(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.55);

  w.text(ctx, "ГИЛЬДИЯ", { x: 0.06, y: 0.12 }, width, height, { size: 13 });

  // Список — узкая колонка: строка отвечает только «какой дом», подробности живут справа.
  const names = ["ЧЁРНЫЙ ШИП", "ТИХИЙ ДВОР", "свободный слот", "свободный слот"];
  names.forEach((name, i) => {
    const r: w.Rect = { x: 0.06, y: 0.21 + i * 0.115, w: 0.26, h: 0.095 };
    w.box(ctx, r, width, height, {
      lit: i === 0,
      dashed: i >= 2,
      label: name,
      sub: i === 0 ? "забег идёт" : i === 1 ? "дом ждёт" : undefined,
      size: 9
    });
  });

  // Панель дома: то, ради чего вариант и существует — здесь помещается и книга, и мемориал.
  const panel: w.Rect = { x: 0.37, y: 0.21, w: 0.57, h: 0.57 };
  w.box(ctx, panel, width, height, {});
  w.text(ctx, "ЧЁРНЫЙ ШИП", { x: 0.39, y: 0.26 }, width, height, { size: 12 });

  HOUSE.forEach(([label, value], i) => {
    w.text(ctx, label, { x: 0.39, y: 0.32 + i * 0.075 }, width, height, { size: 8, color: w.WIRE.dim });
    w.text(ctx, value, { x: 0.58, y: 0.32 + i * 0.075 }, width, height, { size: 9 });
  });

  // Ростер телами — то, чего в строке списка не показать никак.
  w.text(ctx, "РОСТЕР", { x: 0.39, y: 0.63 }, width, height, { size: 8, color: w.WIRE.dim });
  for (let i = 0; i < 8; i++) {
    w.box(ctx, { x: 0.39 + i * 0.045, y: 0.655, w: 0.036, h: 0.075 }, width, height, {
      dashed: i > 5
    });
  }
  w.callout(
    ctx,
    { x: 0.66, y: 0.69 },
    { x: 0.7, y: 0.755 },
    "пунктир — свободные места",
    width,
    height
  );

  w.box(ctx, { x: 0.37, y: 0.81, w: 0.2, h: 0.06 }, width, height, { label: "Войти в дом", lit: true, size: 9 });
  w.box(ctx, { x: 0.59, y: 0.81, w: 0.13, h: 0.06 }, width, height, { label: "Удалить", size: 9, stroke: w.WIRE.danger });
  w.box(ctx, { x: 0.81, y: 0.81, w: 0.13, h: 0.06 }, width, height, { label: "Назад", size: 9 });
  w.measure(ctx, panel, "57%", width, height, "x", "before");
}

/* ---------- В: две крупные карточки в ряд, остальные свёрнуты ---------- */

function drawFocus(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.5);

  w.text(ctx, "ГИЛЬДИЯ", { x: 0.5, y: 0.12 }, width, height, { align: "center", size: 13 });

  // Один дом раскрыт во всю ширину: пока домов один-два, ряд из четырёх карточек — это ряд из двух
  // карточек и двух дырок.
  const main: w.Rect = { x: 0.14, y: 0.2, w: 0.44, h: 0.55 };
  houseCard(ctx, main, width, height, "run", "ЧЁРНЫЙ ШИП", 2);
  w.text(ctx, "РОСТЕР", { x: 0.155, y: 0.545 }, width, height, { size: 8, color: w.WIRE.dim });
  for (let i = 0; i < 6; i++) {
    w.box(ctx, { x: 0.155 + i * 0.048, y: 0.565, w: 0.038, h: 0.075 }, width, height, { dashed: i > 3 });
  }
  w.text(ctx, "Обновлён · 3 авг, 19:24", { x: 0.155, y: 0.675 }, width, height, {
    size: 8,
    color: w.WIRE.dim
  });

  // Остальные дома — узкими корешками справа, как книги на полке.
  const spines = ["ТИХИЙ ДВОР", "свободно", "свободно"];
  spines.forEach((name, i) => {
    const r: w.Rect = { x: 0.62 + i * 0.085, y: 0.2, w: 0.075, h: 0.55 };
    w.box(ctx, r, width, height, { dashed: i > 0 });
    ctx.save();
    ctx.translate((r.x + r.w / 2) * width, (r.y + r.h / 2) * height);
    ctx.rotate(-Math.PI / 2);
    ctx.fillStyle = w.WIRE.dim;
    ctx.font = "9px ui-monospace, monospace";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(name, 0, 0);
    ctx.restore();
  });
  w.callout(
    ctx,
    { x: 0.86, y: 0.3 },
    { x: 0.9, y: 0.16 },
    "корешок: клик раскрывает",
    width,
    height,
    "right"
  );

  w.box(ctx, { x: 0.14, y: 0.79, w: 0.2, h: 0.06 }, width, height, { label: "Войти в дом", lit: true, size: 9 });
  w.box(ctx, { x: 0.36, y: 0.79, w: 0.12, h: 0.06 }, width, height, { label: "Удалить", size: 9, stroke: w.WIRE.danger });
  w.box(ctx, { x: 0.02, y: 0.86, w: 0.09, h: 0.06 }, width, height, { label: "Назад", size: 9 });
  w.measure(ctx, main, "44%", width, height, "x", "before");
}

const section: SectionDef = {
  id: "ui-guilds",
  title: "Выбор гильдии",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Дом — это и есть слот сохранения, поэтому карточка обязана отвечать «что там сейчас», а не " +
    "«сколько наиграно». Три раскладки: ряд карточек как на профиле, список с панелью дома и один " +
    "раскрытый дом при свёрнутых остальных.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Чем дом отличается от профиля",
      lede:
        "Профиль отвечает «кто я», дом — «где мой прогресс». Отсюда разный состав карточки и разная " +
        "цена ошибки: удалённый профиль стоит ника и открытий, удалённый дом — забега, ростера и " +
        "книги гильдии."
    },
    {
      kind: "table",
      head: ["Поле карточки дома", "Зачем", "Замечание"],
      rows: [
        ["Имя дома", "единственная именуемая вещь у игрока", "профиль носит номер, дом — имя"],
        ["Идёт ли забег", "решает, что случится по нажатию", "пары «Начать / Продолжить» нет: строка дома сама говорит, что внутри"],
        ["Возвышение", "текущее и максимальное в этом доме", "доступ живёт в профиле, значение — в доме"],
        ["Ростер", "сколько живых и сколько в мемориале", "мемориал — часть тона: погибшие остаются записью"],
        ["Дата обновления", "какой дом трогали последним", "единственное поле, общее с профилем"],
        ["Пустой слот", "показывается наравне с занятыми", "иначе список не отвечает на вопрос «а можно ещё?»"]
      ]
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede:
        "Выбран вариант Б (Макс, 05.08.2026), и тот же приём назначен экрану профиля. Доли экрана — " +
        "1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "row",
          status: "rejected",
          title: "А · Ряд карточек",
          tag: "как экран профиля",
          note:
            "Тот же приём, что в варианте А профиля: ряд одинаковых карточек по центру кадра. " +
            "Два экрана подряд выглядят одним движением.",
          facts: [
            ["карточка", "21.7% x 50%"],
            ["полей в карточке", "4"],
            ["метка забега", "у кромки шапки"],
            ["удаление", "под выбранной"]
          ],
          verdict:
            "Единообразие с профилем — сильный довод: игрок учится одному приёму, а не двум. Цена — ростер в карточку не влезает, останется числом.",
          size: [480, 270],
          draw: drawRow
        },
        {
          id: "list",
          status: "accepted",
          title: "Б · Список и панель дома",
          decision: "2026-08-05",
          tag: "приём Risk of Rain 2",
          note:
            "Слева узкие строки домов, справа — выбранный дом целиком: поля, ростер телами, " +
            "действия. Приём «подробности сфокусированной строки рядом» снят с рефа лобби RoR2.",
          facts: [
            ["строка", "26% ширины"],
            ["панель дома", "57% ширины"],
            ["ростер", "телами, 8 мест"],
            ["места", "пунктиром — свободные"]
          ],
          verdict:
            "Единственный вариант, где виден ростер, а не его число, и куда влезет книга гильдии. Цена — экран перестаёт быть похож на профиль и требует своего объяснения.",
          size: [480, 270],
          draw: drawListDetail
        },
        {
          id: "focus",
          status: "rejected",
          title: "В · Один дом раскрыт, прочие корешками",
          tag: "своё",
          note:
            "Выбранный дом занимает половину экрана, остальные стоят узкими корешками справа и " +
            "раскрываются по клику. Раскладка исходит из того, что домов у игрока обычно один-два.",
          facts: [
            ["раскрытый", "44% ширины"],
            ["корешок", "7.5% ширины"],
            ["ростер", "телами, 6 мест"],
            ["пустые", "тоже корешками"]
          ],
          verdict:
            "Честно отражает реальность: ряд из четырёх карточек при двух домах — это ряд из двух карточек и двух дырок. Цена — приём нигде больше в игре не встречается, его придётся объяснять.",
          size: [480, 270],
          draw: drawFocus
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> сколько домов на профиль (сейчас число живёт в " +
        "<code>GameConfig.MaxGuildsPerProfile</code>) · спрашиваем ли подтверждение на удаление дома " +
        "и чем оно отличается от удаления профиля · попадает ли книга гильдии на этот экран или " +
        "живёт только во дворе · показываем ли мемориал числом или лицами."
    }
  ]
};

export default section;
