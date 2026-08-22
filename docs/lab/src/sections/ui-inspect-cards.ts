/* Осмотр: панель по ЛКМ и расширенные карточки по ПКМ.

   Второй заход по экранам подготовки (первый — `ui-party-items`, вердикт 22.08.2026: отряд
   «Витриной», предметы «Строками»). Вердикт и определил задачу этого раздела: на «Витрине» подпись
   под телом вмещает имя и Реликвию, и больше ничего. Значит перки, Судьба, Обеты, уровень и травмы
   живут ТОЛЬКО здесь — панель перестала быть удобством и стала обязательной частью экрана.

   Три глубины показа (`gdd/50-modes-ux/preparation-screens` §1) спорят здесь двумя нижними:

   - панель осмотра (ЛКМ) отвечает «что этот боец умеет сейчас» — её меряют скоростью сравнения
     двух бойцов;
   - расширенная карточка (ПКМ) отвечает «кто он и из чего собран» — её меряют полнотой.

   Окно карточки — 80-90% кадра с видимой кромкой (слово Макса 22.08.2026): край подложки говорит
   «ты заглянул, а не ушёл с экрана».

   Травмы рисуются ЗДЕСЬ и только здесь: шесть мест ступенями 3/2/1
   (`gdd/30-run-meta/injuries-mettle`). На страницах подготовки их нет по тому же решению. */

import * as w from "./ui-wire.js";
import type { SectionDef } from "../types.js";

/** Окно поверх экрана: подложка гаснет, но её край остаётся виден. Доля кадра — предмет спора,
 *  поэтому размер приходит параметром, а не зашит. */
function overlay(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  frac: number
): w.Rect {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.82);
  const r: w.Rect = { x: (1 - frac) / 2, y: (1 - frac) / 2, w: frac, h: frac };
  w.box(ctx, r, width, height, {});
  return r;
}

/** Заголовок секции внутри карточки: подпись без рамки. Секций много, и рамка у каждой дала бы
 *  «рамку в рамке» — то, что визуальный язык прямо запрещает. */
function section(
  ctx: CanvasRenderingContext2D,
  s: string,
  at: { x: number; y: number },
  width: number,
  height: number
): void {
  w.text(ctx, s, at, width, height, { size: 8, color: w.WIRE.accent });
}

/** Шесть мест под травмы: три Ушиба, две Раны, одно Увечье. Группы разделены пробелом — ступень
 *  читается расстоянием, а не подписью: подписи в строку не влезают. */
function injuries(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number },
  width: number,
  height: number,
  taken = 2,
  size = 0.022
): void {
  let n = 0;
  let x = at.x;
  [3, 2, 1].forEach((count, g) => {
    for (let i = 0; i < count; i++) {
      w.box(ctx, { x, y: at.y, w: (size * height) / width, h: size }, width, height, {
        lit: n < taken,
        stroke: n < taken ? w.WIRE.danger : undefined
      });
      x += (size * height) / width + 0.006;
      n++;
    }
    if (g < 2) x += 0.012;
  });
  w.text(ctx, "3 / 2 / 1", { x: x + 0.008, y: at.y + size / 2 }, width, height, {
    size: 7,
    color: w.WIRE.dim
  });
}

/** Ряд слотов предмета: три открытых, четвёртый закрыт. Тот же язык, что на страницах подготовки. */
function itemRow(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number },
  width: number,
  height: number,
  size: number
): void {
  const sw = (size * height) / width;
  for (let i = 0; i < 4; i++) {
    const r: w.Rect = { x: at.x + i * (sw + 0.008), y: at.y, w: sw, h: size };
    if (i === 3) {
      w.box(ctx, r, width, height, { dashed: true, hollow: true });
      w.lock(ctx, r, width, height);
    } else {
      w.box(ctx, r, width, height, { lit: i < 2 });
    }
  }
}

/** Строки текста подряд: лор, описание, список статов. */
function lines(
  ctx: CanvasRenderingContext2D,
  items: string[],
  at: { x: number; y: number },
  width: number,
  height: number,
  step = 0.03,
  size = 7
): void {
  items.forEach((s, i) => {
    w.text(ctx, s, { x: at.x, y: at.y + i * step }, width, height, { size, color: w.WIRE.dim });
  });
}

/** Две кнопки осмотра. Вторая тусклая, но живая: правило ui-feedback §1 — мёртвая кнопка читается
 *  как сломанный экран, поэтому «нет Реликвии» гасит текст, а не отклик. */
function inspectButtons(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number; w: number },
  width: number,
  height: number,
  h = 0.05
): void {
  const half = at.w / 2 - 0.006;
  w.box(ctx, { x: at.x, y: at.y, w: half, h }, width, height, { label: "О СОСУДЕ", size: 8, lit: true });
  w.box(ctx, { x: at.x + half + 0.012, y: at.y, w: half, h }, width, height, {
    label: "о Реликвии",
    size: 8
  });
}

/* ══ Панель осмотра ════════════════════════════════════════════════════════ */

/** Общий фон для панелей: страница отряда «Витриной», поверх которой панель и живёт. */
function stageBehind(ctx: CanvasRenderingContext2D, width: number, height: number, upTo = 0.7): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.35);
  w.text(ctx, "ПОДГОТОВКА · ОТРЯД", { x: 0.05, y: 0.055 }, width, height, { size: 10 });
  for (let i = 0; i < 4; i++) {
    const x = 0.12 + i * 0.15;
    w.box(ctx, { x: x - 0.03, y: 0.16, w: 0.085, h: 0.24 }, width, height, {
      hollow: true,
      dashed: true,
      label: "тело",
      size: 8
    });
    w.disc(ctx, { x: x + 0.012, y: 0.44, r: 0.032 }, width, height, { lit: i === 1 });
    w.text(ctx, ["Ирма", "Кай", "Дан", "Сув"][i] ?? "имя", { x: x + 0.012, y: 0.5 }, width, height, {
      align: "center",
      size: 8
    });
  }
  if (upTo > 0.7) return;
}

/** III-А · Колонка у кромки: узкая панель во всю высоту, всё списком, скролл. */
function panelColumn(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  stageBehind(ctx, width, height);

  const r: w.Rect = { x: 0.72, y: 0.08, w: 0.23, h: 0.84 };
  const [bx, by, bw, bh] = w.px(r, width, height);
  ctx.fillStyle = w.WIRE.frame;
  ctx.fillRect(bx, by, bw, bh);
  w.box(ctx, r, width, height, { lit: true });

  w.disc(ctx, { x: 0.78, y: 0.16, r: 0.05 }, width, height, { label: "лицо" });
  w.text(ctx, "КАЙ", { x: 0.83, y: 0.13 }, width, height, { size: 11 });
  w.text(ctx, "Щит · Танк · ур. 3", { x: 0.74, y: 0.23 }, width, height, { size: 7, color: w.WIRE.accent });

  section(ctx, "СТАТЫ", { x: 0.74, y: 0.29 }, width, height);
  lines(ctx, ["HP 820", "броня 24 · маг. 12", "урон 41", "скорость 3.2"], { x: 0.74, y: 0.33 }, width, height);

  section(ctx, "ПЕРКИ", { x: 0.74, y: 0.44 }, width, height);
  lines(ctx, ["+ Стойкий: +8% брони", "− Тугодум: −10% каста"], { x: 0.74, y: 0.48 }, width, height);

  section(ctx, "СНАРЯЖЕНИЕ", { x: 0.74, y: 0.56 }, width, height);
  itemRow(ctx, { x: 0.74, y: 0.6 }, width, height, 0.06);

  section(ctx, "ПОВЕДЕНИЕ", { x: 0.74, y: 0.71 }, width, height);
  w.box(ctx, { x: 0.74, y: 0.74, w: 0.19, h: 0.045 }, width, height, { label: "держит строй", size: 7 });

  inspectButtons(ctx, { x: 0.74, y: 0.85, w: 0.19 }, width, height);
  w.callout(ctx, { x: 0.72, y: 0.66 }, { x: 0.66, y: 0.72 }, "длинный список — нужен скролл", width, height, "right");
}

/** III-Б · Широкая карта: панель в две внутренние колонки, скролла нет. */
function panelWide(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  stageBehind(ctx, width, height);

  const r: w.Rect = { x: 0.61, y: 0.1, w: 0.34, h: 0.72 };
  const [bx, by, bw, bh] = w.px(r, width, height);
  ctx.fillStyle = w.WIRE.frame;
  ctx.fillRect(bx, by, bw, bh);
  w.box(ctx, r, width, height, { lit: true });

  w.disc(ctx, { x: 0.67, y: 0.19, r: 0.055 }, width, height, { label: "лицо" });
  w.text(ctx, "КАЙ", { x: 0.72, y: 0.155 }, width, height, { size: 12 });
  w.text(ctx, "Щит · Танк · уровень 3", { x: 0.72, y: 0.2 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });

  section(ctx, "СТАТЫ", { x: 0.63, y: 0.31 }, width, height);
  lines(ctx, ["HP 820", "броня 24", "маг. броня 12", "урон 41", "скорость 3.2"], { x: 0.63, y: 0.35 }, width, height);

  section(ctx, "ЧЕЛОВЕК", { x: 0.79, y: 0.31 }, width, height);
  lines(
    ctx,
    ["+ Стойкий", "− Тугодум", "Судьба: 4/10", "Обет: не добивать"],
    { x: 0.79, y: 0.35 },
    width,
    height
  );

  section(ctx, "СНАРЯЖЕНИЕ", { x: 0.63, y: 0.55 }, width, height);
  itemRow(ctx, { x: 0.63, y: 0.59 }, width, height, 0.07);
  w.box(ctx, { x: 0.63, y: 0.67, w: 0.3, h: 0.045 }, width, height, { label: "поведение: держит строй", size: 7 });

  inspectButtons(ctx, { x: 0.63, y: 0.735, w: 0.3 }, width, height, 0.055);
  w.measure(ctx, r, "34% ширины", width, height, "x", "before");
}

/** III-В · Полка снизу: панель лежит вдоль низа и не закрывает тела на витрине. */
function panelShelf(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  stageBehind(ctx, width, height);

  const r: w.Rect = { x: 0.05, y: 0.58, w: 0.9, h: 0.34 };
  const [bx, by, bw, bh] = w.px(r, width, height);
  ctx.fillStyle = w.WIRE.frame;
  ctx.fillRect(bx, by, bw, bh);
  w.box(ctx, r, width, height, { lit: true });

  w.disc(ctx, { x: 0.11, y: 0.68, r: 0.055 }, width, height, { label: "лицо" });
  w.text(ctx, "КАЙ", { x: 0.16, y: 0.645 }, width, height, { size: 12 });
  w.text(ctx, "Щит · Танк · ур. 3", { x: 0.16, y: 0.69 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.text(ctx, "держит строй", { x: 0.16, y: 0.73 }, width, height, { size: 7, color: w.WIRE.dim });

  section(ctx, "СТАТЫ", { x: 0.34, y: 0.64 }, width, height);
  lines(ctx, ["HP 820", "броня 24 · маг. 12", "урон 41 · скор. 3.2"], { x: 0.34, y: 0.68 }, width, height);

  section(ctx, "ЧЕЛОВЕК", { x: 0.52, y: 0.64 }, width, height);
  lines(ctx, ["+ Стойкий", "− Тугодум", "Судьба: 4/10"], { x: 0.52, y: 0.68 }, width, height);

  section(ctx, "СНАРЯЖЕНИЕ", { x: 0.67, y: 0.64 }, width, height);
  itemRow(ctx, { x: 0.67, y: 0.68 }, width, height, 0.06);

  inspectButtons(ctx, { x: 0.67, y: 0.82, w: 0.26 }, width, height);
  w.callout(ctx, { x: 0.5, y: 0.58 }, { x: 0.5, y: 0.53 }, "тела остаются открытыми", width, height, "center");
}

/* ══ Расширенная карточка «Сосуда» ═════════════════════════════════════════ */

/** IV-А · Разворот: вид в полный рост слева, секции справа двумя колонками. */
function cardSpread(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.025;

  w.text(ctx, "КАЙ, СЫН КАМЕНОТЁСА", { x: r.x + pad, y: r.y + 0.05 }, width, height, { size: 13 });
  w.text(ctx, "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", { x: r.x + pad, y: r.y + 0.09 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.03, w: 0.03, h: 0.05 }, width, height, {
    label: "Esc",
    size: 7
  });

  const art: w.Rect = { x: r.x + pad, y: r.y + 0.13, w: 0.22, h: 0.62 };
  w.box(ctx, art, width, height, { hollow: true, dashed: true, label: "вид целиком", size: 9 });
  w.measure(ctx, art, "22%", width, height);

  const c1 = r.x + pad + 0.25;
  const c2 = r.x + pad + 0.48;

  section(ctx, "ДОСЬЕ", { x: c1, y: r.y + 0.15 }, width, height);
  lines(
    ctx,
    ["Пришёл из каменоломни,", "когда гильдия взяла отца.", "Летопись: 12 побед"],
    { x: c1, y: r.y + 0.19 },
    width,
    height
  );

  section(ctx, "ПЕРКИ", { x: c1, y: r.y + 0.32 }, width, height);
  lines(ctx, ["+ Стойкий: +8% брони", "− Тугодум: −10% скор. каста"], { x: c1, y: r.y + 0.36 }, width, height);

  section(ctx, "СУДЬБА И ОБЕТЫ", { x: c1, y: r.y + 0.45 }, width, height);
  lines(ctx, ["Главный герой · 4 / 10", "Обет: не добивать раненых"], { x: c1, y: r.y + 0.49 }, width, height);

  section(ctx, "ТРАВМЫ И ЗАКАЛКА", { x: c1, y: r.y + 0.58 }, width, height);
  injuries(ctx, { x: c1, y: r.y + 0.61 }, width, height);
  w.text(ctx, "Закалка: Стойкость", { x: c1, y: r.y + 0.67 }, width, height, { size: 7, color: w.WIRE.dim });

  section(ctx, "СНАРЯЖЕНИЕ", { x: c2, y: r.y + 0.15 }, width, height);
  itemRow(ctx, { x: c2, y: r.y + 0.18 }, width, height, 0.07);
  w.box(ctx, { x: c2, y: r.y + 0.27, w: 0.2, h: 0.05 }, width, height, { label: "Реликвия «Щит» →", size: 7 });

  section(ctx, "СТАТЫ ИТОГОМ", { x: c2, y: r.y + 0.38 }, width, height);
  lines(
    ctx,
    ["HP 820", "броня 24 · маг. броня 12", "урон 41", "скорость 3.2", "реген маны 5.0"],
    { x: c2, y: r.y + 0.42 },
    width,
    height
  );

  section(ctx, "УЛУЧШЕНИЯ", { x: c2, y: r.y + 0.61 }, width, height);
  lines(ctx, ["+ 12% HP", "+ размер, − скорость"], { x: c2, y: r.y + 0.65 }, width, height);
}

/** IV-Б · Три колонны: вид, человек, боец. Границы колонок — границы смыслов. */
function cardColumns(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.025;

  w.text(ctx, "КАЙ, СЫН КАМЕНОТЁСА", { x: r.x + pad, y: r.y + 0.05 }, width, height, { size: 13 });
  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.03, w: 0.03, h: 0.05 }, width, height, {
    label: "Esc",
    size: 7
  });

  const art: w.Rect = { x: r.x + pad, y: r.y + 0.11, w: 0.19, h: 0.64 };
  w.box(ctx, art, width, height, { hollow: true, dashed: true, label: "вид целиком", size: 9 });

  const cw = 0.245;
  const human: w.Rect = { x: r.x + pad + 0.21, y: r.y + 0.11, w: cw, h: 0.64 };
  const fighter: w.Rect = { x: r.x + pad + 0.21 + cw + 0.02, y: r.y + 0.11, w: cw, h: 0.64 };
  w.box(ctx, human, width, height, { hollow: true });
  w.box(ctx, fighter, width, height, { hollow: true });

  w.text(ctx, "ЧЕЛОВЕК", { x: human.x + 0.012, y: human.y + 0.04 }, width, height, {
    size: 9,
    color: w.WIRE.accent
  });
  lines(
    ctx,
    [
      "Пришёл из каменоломни,",
      "когда взяли его отца.",
      "",
      "+ Стойкий: +8% брони",
      "− Тугодум: −10% скор. каста",
      "",
      "Судьба: Главный герой 4/10",
      "Обет: не добивать раненых",
      "",
      "Летопись: 12 побед, 2 смерти"
    ],
    { x: human.x + 0.012, y: human.y + 0.09 },
    width,
    height,
    0.045
  );

  w.text(ctx, "БОЕЦ", { x: fighter.x + 0.012, y: fighter.y + 0.04 }, width, height, {
    size: 9,
    color: w.WIRE.accent
  });
  section(ctx, "снаряжение", { x: fighter.x + 0.012, y: fighter.y + 0.09 }, width, height);
  itemRow(ctx, { x: fighter.x + 0.012, y: fighter.y + 0.12 }, width, height, 0.06);
  section(ctx, "травмы", { x: fighter.x + 0.012, y: fighter.y + 0.23 }, width, height);
  injuries(ctx, { x: fighter.x + 0.012, y: fighter.y + 0.26 }, width, height);
  lines(
    ctx,
    ["HP 820", "броня 24 · маг. броня 12", "урон 41 · скорость 3.2", "Закалка: Стойкость"],
    { x: fighter.x + 0.012, y: fighter.y + 0.36 },
    width,
    height,
    0.045
  );

  w.callout(
    ctx,
    { x: human.x + cw / 2, y: human.y + 0.64 },
    { x: human.x + cw / 2, y: r.y + 0.8 },
    "слева кто он, справа чем дерётся",
    width,
    height,
    "center"
  );
}

/** IV-В · Лист состояний: сетка секций-карточек, как лист персонажа в НРИ. */
function cardSheet(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.025;

  const head: w.Rect = { x: r.x + pad, y: r.y + 0.03, w: r.w - pad * 2, h: 0.14 };
  w.box(ctx, head, width, height, { hollow: true });
  w.disc(ctx, { x: head.x + 0.04, y: head.y + 0.07, r: 0.05 }, width, height, { label: "лицо" });
  w.text(ctx, "КАЙ, СЫН КАМЕНОТЁСА", { x: head.x + 0.08, y: head.y + 0.05 }, width, height, { size: 12 });
  w.text(ctx, "Реликвия «Щит» · Танк · уровень 3 · Закалка: Стойкость", { x: head.x + 0.08, y: head.y + 0.095 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: head.x + head.w - 0.03, y: head.y + 0.005, w: 0.03, h: 0.05 }, width, height, {
    label: "Esc",
    size: 7
  });

  // Шесть клеток: три сверху, три снизу. Вид — отдельной клеткой, а не фоном.
  const gap = 0.018;
  const cw = (head.w - gap * 2) / 3;
  const chh = 0.29;
  const cells: Array<[string, string[]]> = [
    ["ВИД", []],
    ["ДОСЬЕ", ["Пришёл из каменоломни,", "когда гильдия взяла отца.", "Летопись: 12 побед, 2 смерти"]],
    ["ПЕРКИ", ["+ Стойкий: +8% брони", "− Тугодум: −10% каста"]],
    ["ТРАВМЫ", []],
    ["СНАРЯЖЕНИЕ", []],
    ["СТАТЫ", ["HP 820", "броня 24 · маг. 12", "урон 41 · скорость 3.2"]]
  ];
  cells.forEach(([title, body], i) => {
    const cell: w.Rect = {
      x: head.x + (i % 3) * (cw + gap),
      y: r.y + 0.2 + Math.floor(i / 3) * (chh + gap),
      w: cw,
      h: chh
    };
    w.box(ctx, cell, width, height, { hollow: true });
    w.text(ctx, title, { x: cell.x + 0.012, y: cell.y + 0.035 }, width, height, {
      size: 8,
      color: w.WIRE.accent
    });
    if (title === "ВИД") {
      w.box(
        ctx,
        { x: cell.x + 0.012, y: cell.y + 0.055, w: cell.w - 0.024, h: chh - 0.075 },
        width,
        height,
        { hollow: true, dashed: true, label: "вид целиком", size: 8 }
      );
      return;
    }
    if (title === "ТРАВМЫ") {
      injuries(ctx, { x: cell.x + 0.012, y: cell.y + 0.07 }, width, height);
      w.text(ctx, "Ушиб колена · Рана плеча", { x: cell.x + 0.012, y: cell.y + 0.15 }, width, height, {
        size: 7,
        color: w.WIRE.dim
      });
      return;
    }
    if (title === "СНАРЯЖЕНИЕ") {
      itemRow(ctx, { x: cell.x + 0.012, y: cell.y + 0.06 }, width, height, 0.07);
      w.box(ctx, { x: cell.x + 0.012, y: cell.y + 0.17, w: cell.w - 0.024, h: 0.05 }, width, height, {
        label: "Реликвия «Щит» →",
        size: 7
      });
      return;
    }
    lines(ctx, body, { x: cell.x + 0.012, y: cell.y + 0.075 }, width, height, 0.04);
  });
}

/* ══ Расширенная карточка Реликвии ═════════════════════════════════════════ */

/** V-А · Разворот кита: знак и лор слева, способности справа. */
function relicSpread(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.025;

  w.text(ctx, "THE BULWARK", { x: r.x + pad, y: r.y + 0.05 }, width, height, { size: 13 });
  w.text(ctx, "Стандартная · Обычная · класс Танк · уровень 2", { x: r.x + pad, y: r.y + 0.09 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.03, w: 0.03, h: 0.05 }, width, height, {
    label: "Esc",
    size: 7
  });

  const sign: w.Rect = { x: r.x + pad, y: r.y + 0.13, w: 0.22, h: 0.34 };
  w.box(ctx, sign, width, height, { hollow: true, dashed: true, label: "знак Реликвии", size: 9 });
  section(ctx, "ЛОР", { x: r.x + pad, y: r.y + 0.51 }, width, height);
  lines(
    ctx,
    ["Щит, за которым погиб", "последний защитник Врат.", "Носитель: Кай."],
    { x: r.x + pad, y: r.y + 0.55 },
    width,
    height
  );

  const c = r.x + pad + 0.25;
  section(ctx, "АВТОАТАКА", { x: c, y: r.y + 0.15 }, width, height);
  lines(ctx, ["удар щитом · 41 урона · 1.2 с"], { x: c, y: r.y + 0.19 }, width, height);

  section(ctx, "СПОСОБНОСТИ", { x: c, y: r.y + 0.26 }, width, height);
  for (let i = 0; i < 3; i++) {
    const row: w.Rect = { x: c, y: r.y + 0.3 + i * 0.11, w: 0.48, h: 0.095 };
    w.box(ctx, row, width, height, { hollow: true });
    w.box(ctx, { x: row.x + 0.008, y: row.y + 0.015, w: 0.035, h: 0.065 }, width, height, {});
    w.text(ctx, ["Стена", "Вызов", "Клятва"][i] ?? "—", { x: row.x + 0.055, y: row.y + 0.03 }, width, height, {
      size: 9
    });
    w.text(
      ctx,
      ["40 маны · при HP < 50%", "25 маны · раз в 8 с", "60 маны · при смерти союзника"][i] ?? "",
      { x: row.x + 0.055, y: row.y + 0.062 },
      width,
      height,
      { size: 7, color: w.WIRE.dim }
    );
  }

  section(ctx, "ПАССИВКИ", { x: c, y: r.y + 0.66 }, width, height);
  lines(ctx, ["+15% брони соседям в строю"], { x: c, y: r.y + 0.7 }, width, height);
}

/** V-Б · Две страницы книги: слева «Сосуд», справа его Реликвия, переход — перелистывание. */
function relicBook(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.88);
  const pad = 0.022;
  const half = (r.w - pad * 3) / 2;

  // Корешок между страницами: он и объясняет, почему это одно окно, а не два.
  const spine = r.x + pad + half + pad / 2;
  const [sx] = w.px({ x: spine, y: 0, w: 0, h: 0 }, width, height);
  ctx.strokeStyle = w.WIRE.line;
  ctx.beginPath();
  ctx.moveTo(sx, r.y * height + 0.03 * height);
  ctx.lineTo(sx, (r.y + r.h) * height - 0.03 * height);
  ctx.stroke();

  // Левая страница: человек, кратко.
  const lx = r.x + pad;
  w.text(ctx, "КАЙ", { x: lx, y: r.y + 0.055 }, width, height, { size: 12 });
  w.text(ctx, "Сосуд · уровень 3", { x: lx, y: r.y + 0.095 }, width, height, { size: 7, color: w.WIRE.accent });
  w.box(ctx, { x: lx, y: r.y + 0.12, w: half * 0.5, h: 0.42 }, width, height, {
    hollow: true,
    dashed: true,
    label: "вид целиком",
    size: 8
  });
  section(ctx, "ПЕРКИ", { x: lx + half * 0.55, y: r.y + 0.16 }, width, height);
  lines(ctx, ["+ Стойкий", "− Тугодум"], { x: lx + half * 0.55, y: r.y + 0.2 }, width, height);
  section(ctx, "ТРАВМЫ", { x: lx + half * 0.55, y: r.y + 0.29 }, width, height);
  injuries(ctx, { x: lx + half * 0.52, y: r.y + 0.32 }, width, height, 2, 0.015);
  section(ctx, "СУДЬБА", { x: lx + half * 0.55, y: r.y + 0.4 }, width, height);
  lines(ctx, ["Главный герой 4/10"], { x: lx + half * 0.55, y: r.y + 0.44 }, width, height);
  section(ctx, "СНАРЯЖЕНИЕ", { x: lx, y: r.y + 0.6 }, width, height);
  itemRow(ctx, { x: lx, y: r.y + 0.63 }, width, height, 0.07);

  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.025, w: 0.03, h: 0.045 }, width, height, {
    label: "Esc",
    size: 7
  });

  // Правая страница: Реликвия.
  const rx = spine + pad / 2;
  w.text(ctx, "THE BULWARK", { x: rx, y: r.y + 0.055 }, width, height, { size: 12 });
  w.text(ctx, "Стандартная · Танк · уровень 2", { x: rx, y: r.y + 0.095 }, width, height, {
    size: 7,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: rx, y: r.y + 0.12, w: half * 0.34, h: 0.22 }, width, height, {
    hollow: true,
    dashed: true,
    label: "знак",
    size: 8
  });
  lines(
    ctx,
    ["удар щитом · 41 · 1.2 с"],
    { x: rx + half * 0.38, y: r.y + 0.16 },
    width,
    height
  );
  section(ctx, "СПОСОБНОСТИ", { x: rx, y: r.y + 0.38 }, width, height);
  for (let i = 0; i < 3; i++) {
    const row: w.Rect = { x: rx, y: r.y + 0.42 + i * 0.09, w: half, h: 0.075 };
    w.box(ctx, row, width, height, { hollow: true });
    w.text(ctx, ["Стена", "Вызов", "Клятва"][i] ?? "—", { x: row.x + 0.01, y: row.y + 0.025 }, width, height, {
      size: 8
    });
    w.text(ctx, ["40 маны · HP < 50%", "25 маны · раз в 8 с", "60 маны · смерть союзника"][i] ?? "", {
      x: row.x + 0.01,
      y: row.y + 0.052
    }, width, height, { size: 7, color: w.WIRE.dim });
  }

  w.callout(
    ctx,
    { x: spine, y: r.y + r.h - 0.06 },
    { x: spine + 0.06, y: r.y + r.h - 0.02 },
    "один разворот: человек и его кит рядом",
    width,
    height
  );
}

/** V-В · Список способностей: кит крупными строками во всю ширину, лор внизу. */
function relicList(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.025;

  w.box(ctx, { x: r.x + pad, y: r.y + 0.03, w: 0.09, h: 0.16 }, width, height, {
    hollow: true,
    dashed: true,
    label: "знак",
    size: 8
  });
  w.text(ctx, "THE BULWARK", { x: r.x + pad + 0.11, y: r.y + 0.07 }, width, height, { size: 13 });
  w.text(ctx, "Стандартная · Обычная · Танк · уровень 2 · носит Кай", { x: r.x + pad + 0.11, y: r.y + 0.115 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.03, w: 0.03, h: 0.05 }, width, height, {
    label: "Esc",
    size: 7
  });

  const rowW = r.w - pad * 2;
  for (let i = 0; i < 4; i++) {
    const row: w.Rect = { x: r.x + pad, y: r.y + 0.22 + i * 0.115, w: rowW, h: 0.1 };
    w.box(ctx, row, width, height, { hollow: true });
    w.box(ctx, { x: row.x + 0.01, y: row.y + 0.018, w: 0.04, h: 0.065 }, width, height, {});
    w.text(ctx, ["Удар щитом (авто)", "Стена", "Вызов", "Клятва"][i] ?? "—", { x: row.x + 0.06, y: row.y + 0.03 }, width, height, {
      size: 10
    });
    w.text(
      ctx,
      [
        "41 урона · раз в 1.2 с",
        "40 маны · при HP < 50% · барьер 180 на 6 с",
        "25 маны · раз в 8 с · тянет врага к себе",
        "60 маны · при смерти союзника · +30% брони всем"
      ][i] ?? "",
      { x: row.x + 0.06, y: row.y + 0.065 },
      width,
      height,
      { size: 7, color: w.WIRE.dim }
    );
  }

  section(ctx, "ЛОР", { x: r.x + pad, y: r.y + 0.72 }, width, height);
  lines(ctx, ["Щит, за которым погиб последний защитник Врат."], { x: r.x + pad, y: r.y + 0.76 }, width, height);
  w.measure(ctx, { x: r.x + pad, y: r.y + 0.565, w: rowW, h: 0.1 }, "строка 10% высоты", width, height);
}

/* ══ Карточка-разворот: форма, заданная Максом 22.08.2026 ══════════════════

   «Не надо все в одну умещать. Сверху должны быть табы: I. Основное (боевое, перки + внешний вид
   (в данный момент, если есть релик - то внешний вид в релике), II. Дополнительно (Лор (кто,
   откуда, как выглядит + статистика)». И сама карточка — книга с двумя страницами, разворот.

   Отсюда каркас: окно, шапка с именем, ряд табов, корешок посередине и две страницы. Спорят
   варианты только тем, что на какой странице лежит. У Реликвии — тот же каркас: игрок учит форму
   один раз. */

interface Book {
  r: w.Rect;
  left: w.Rect;
  right: w.Rect;
}

/** Разворот с табами. Корешок рисуется линией, а не зазором: зазор читался бы как два окна,
 *  а это одно — и переход между страницами обязан выглядеть перелистыванием. */
function book(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  title: string,
  sub: string,
  activeTab: number
): Book {
  const r = overlay(ctx, width, height, 0.86);
  const pad = 0.022;

  w.text(ctx, title, { x: r.x + pad, y: r.y + 0.045 }, width, height, { size: 12 });
  w.text(ctx, sub, { x: r.x + pad, y: r.y + 0.082 }, width, height, { size: 7, color: w.WIRE.accent });
  w.box(ctx, { x: r.x + r.w - pad - 0.03, y: r.y + 0.022, w: 0.03, h: 0.045 }, width, height, {
    label: "Esc",
    size: 7
  });

  ["I · ОСНОВНОЕ", "II · ДОПОЛНИТЕЛЬНО"].forEach((t, i) => {
    w.box(ctx, { x: r.x + pad + i * 0.2, y: r.y + 0.105, w: 0.19, h: 0.045 }, width, height, {
      label: t,
      size: 8,
      lit: i === activeTab
    });
  });

  const top = r.y + 0.175;
  const bottom = r.y + r.h - pad;
  const half = (r.w - pad * 3) / 2;
  const spine = r.x + pad + half + pad / 2;
  const [sx] = w.px({ x: spine, y: 0, w: 0, h: 0 }, width, height);
  ctx.strokeStyle = w.WIRE.line;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(sx, top * height);
  ctx.lineTo(sx, bottom * height);
  ctx.stroke();

  return {
    r,
    left: { x: r.x + pad, y: top, w: half, h: bottom - top },
    right: { x: spine + pad / 2, y: top, w: half, h: bottom - top }
  };
}

/** Фигура «Сосуда» в облачении текущей Реликвии: по слову Макса вид показывается таким, какой он
 *  СЕЙЧАС, — реликвия одевает человека, и карточка обязана это показывать, а не абстрактное тело. */
function figure(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  label = "вид целиком · в облачении «Щита»"
): void {
  w.box(ctx, r, width, height, { hollow: true, dashed: true });
  w.text(ctx, label, { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.dim
  });
}

/** VI-А · Портрет и лист: левая страница целиком под облик, правая — всё боевое. */
function bookFigure(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "КАЙ, СЫН КАМЕНОТЁСА", "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", 0);

  figure(ctx, { x: b.left.x, y: b.left.y, w: b.left.w, h: b.left.h - 0.07 }, width, height);
  w.text(ctx, "Щит · Танк · держит строй", { x: b.left.x + b.left.w / 2, y: b.left.y + b.left.h - 0.035 }, width, height, {
    align: "center",
    size: 8,
    color: w.WIRE.accent
  });

  const c = b.right.x;
  section(ctx, "СТАТЫ", { x: c, y: b.right.y + 0.03 }, width, height);
  lines(
    ctx,
    ["HP 820", "броня 24 · маг. броня 12", "урон 41 · скорость 3.2", "реген маны 5.0"],
    { x: c, y: b.right.y + 0.065 },
    width,
    height
  );

  section(ctx, "ПЕРКИ", { x: c, y: b.right.y + 0.2 }, width, height);
  lines(ctx, ["+ Стойкий: +8% брони", "− Тугодум: −10% каста"], { x: c, y: b.right.y + 0.235 }, width, height);

  section(ctx, "СНАРЯЖЕНИЕ", { x: c, y: b.right.y + 0.31 }, width, height);
  itemRow(ctx, { x: c, y: b.right.y + 0.345 }, width, height, 0.07);

  section(ctx, "ТРАВМЫ", { x: c, y: b.right.y + 0.45 }, width, height);
  injuries(ctx, { x: c, y: b.right.y + 0.48 }, width, height);
  w.text(ctx, "Закалка: Стойкость", { x: c, y: b.right.y + 0.54 }, width, height, {
    size: 7,
    color: w.WIRE.dim
  });
}

/** VI-Б · Человек и боец: слева облик и то, что делает его собой; справа — чем он дерётся. */
function bookHalves(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "КАЙ, СЫН КАМЕНОТЁСА", "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", 0);

  figure(ctx, { x: b.left.x, y: b.left.y, w: b.left.w, h: b.left.h * 0.58 }, width, height);
  section(ctx, "ПЕРКИ", { x: b.left.x, y: b.left.y + b.left.h * 0.63 }, width, height);
  lines(
    ctx,
    ["+ Стойкий: +8% брони", "− Тугодум: −10% скорости каста"],
    { x: b.left.x, y: b.left.y + b.left.h * 0.63 + 0.035 },
    width,
    height
  );
  section(ctx, "СУДЬБА И ОБЕТЫ", { x: b.left.x, y: b.left.y + b.left.h * 0.63 + 0.11 }, width, height);
  lines(
    ctx,
    ["Главный герой · 4 / 10", "Обет: не добивать раненых"],
    { x: b.left.x, y: b.left.y + b.left.h * 0.63 + 0.145 },
    width,
    height
  );

  const c = b.right.x;
  section(ctx, "СТАТЫ", { x: c, y: b.right.y + 0.03 }, width, height);
  lines(
    ctx,
    ["HP 820", "броня 24 · маг. броня 12", "урон 41", "скорость 3.2", "реген маны 5.0"],
    { x: c, y: b.right.y + 0.065 },
    width,
    height
  );
  section(ctx, "СНАРЯЖЕНИЕ", { x: c, y: b.right.y + 0.24 }, width, height);
  itemRow(ctx, { x: c, y: b.right.y + 0.275 }, width, height, 0.08);
  section(ctx, "ТРАВМЫ И ЗАКАЛКА", { x: c, y: b.right.y + 0.39 }, width, height);
  injuries(ctx, { x: c, y: b.right.y + 0.42 }, width, height);
  lines(
    ctx,
    ["Ушиб колена · Рана плеча", "Закалка: Стойкость"],
    { x: c, y: b.right.y + 0.49 },
    width,
    height
  );
}

/** VI-В · Клетки на развороте: облик колонкой, остальное — четыре секции в рамках. */
function bookCells(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "КАЙ, СЫН КАМЕНОТЁСА", "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", 0);

  figure(ctx, { x: b.left.x, y: b.left.y, w: b.left.w * 0.42, h: b.left.h }, width, height, "вид в облачении");

  const cells: Array<[string, w.Rect]> = [
    ["СТАТЫ", { x: b.left.x + b.left.w * 0.46, y: b.left.y, w: b.left.w * 0.54, h: b.left.h / 2 - 0.012 }],
    [
      "ПЕРКИ",
      { x: b.left.x + b.left.w * 0.46, y: b.left.y + b.left.h / 2 + 0.012, w: b.left.w * 0.54, h: b.left.h / 2 - 0.012 }
    ],
    ["СНАРЯЖЕНИЕ", { x: b.right.x, y: b.right.y, w: b.right.w, h: b.right.h / 2 - 0.012 }],
    ["ТРАВМЫ", { x: b.right.x, y: b.right.y + b.right.h / 2 + 0.012, w: b.right.w, h: b.right.h / 2 - 0.012 }]
  ];

  cells.forEach(([title, cell]) => {
    w.box(ctx, cell, width, height, { hollow: true });
    w.text(ctx, title, { x: cell.x + 0.012, y: cell.y + 0.032 }, width, height, {
      size: 8,
      color: w.WIRE.accent
    });
    if (title === "СТАТЫ") {
      lines(ctx, ["HP 820", "броня 24 · маг. 12", "урон 41 · скор. 3.2"], { x: cell.x + 0.012, y: cell.y + 0.07 }, width, height);
    } else if (title === "ПЕРКИ") {
      lines(ctx, ["+ Стойкий: +8% брони", "− Тугодум: −10% каста"], { x: cell.x + 0.012, y: cell.y + 0.07 }, width, height);
    } else if (title === "СНАРЯЖЕНИЕ") {
      itemRow(ctx, { x: cell.x + 0.012, y: cell.y + 0.055 }, width, height, 0.08);
      w.box(ctx, { x: cell.x + 0.012, y: cell.y + 0.16, w: cell.w - 0.024, h: 0.045 }, width, height, {
        label: "Реликвия «Щит» →",
        size: 7
      });
    } else {
      injuries(ctx, { x: cell.x + 0.012, y: cell.y + 0.06 }, width, height);
      lines(ctx, ["Ушиб колена · Рана плеча", "Закалка: Стойкость"], { x: cell.x + 0.012, y: cell.y + 0.13 }, width, height);
    }
  });
}

/** VII-А · Дополнительно, лор и статистика: слева кто он и откуда, справа числа забегов. */
function bookLore(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "КАЙ, СЫН КАМЕНОТЁСА", "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", 1);

  section(ctx, "КТО И ОТКУДА", { x: b.left.x, y: b.left.y + 0.03 }, width, height);
  lines(
    ctx,
    [
      "Пришёл из каменоломни у Серых",
      "Врат, когда гильдия взяла его",
      "отца в свой первый поход.",
      "",
      "Немногословен, спит у входа,",
      "первым встаёт на дежурство."
    ],
    { x: b.left.x, y: b.left.y + 0.065 },
    width,
    height,
    0.035
  );

  section(ctx, "КАК ВЫГЛЯДИТ", { x: b.left.x, y: b.left.y + 0.31 }, width, height);
  lines(
    ctx,
    ["Широкие плечи, шрам через бровь,", "каменная пыль под ногтями."],
    { x: b.left.x, y: b.left.y + 0.345 },
    width,
    height,
    0.035
  );

  section(ctx, "СТАТИСТИКА", { x: b.right.x, y: b.right.y + 0.03 }, width, height);
  const rows = [
    ["боёв", "48"],
    ["побед", "41"],
    ["смертей в бою", "2"],
    ["урона нанесено", "184 200"],
    ["урона принято", "301 500"],
    ["походов", "6"]
  ];
  rows.forEach(([k, v], i) => {
    const y = b.right.y + 0.075 + i * 0.045;
    w.text(ctx, k ?? "", { x: b.right.x, y }, width, height, { size: 7, color: w.WIRE.dim });
    w.text(ctx, v ?? "", { x: b.right.x + b.right.w, y }, width, height, {
      size: 8,
      align: "right",
      color: w.WIRE.text
    });
  });

  section(ctx, "ЛЕТОПИСЬ", { x: b.right.x, y: b.right.y + 0.36 }, width, height);
  lines(
    ctx,
    ["Выстоял один против троих у", "Тихого брода, поход четвёртый."],
    { x: b.right.x, y: b.right.y + 0.395 },
    width,
    height,
    0.035
  );
}

/** VII-Б · Дополнительно, летопись лентой: слева облик и краткое досье, справа записи подвигов. */
function bookChronicle(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "КАЙ, СЫН КАМЕНОТЁСА", "Сосуд гильдии · Реликвия «Щит» · Танк · уровень 3", 1);

  figure(ctx, { x: b.left.x, y: b.left.y, w: b.left.w * 0.5, h: b.left.h * 0.6 }, width, height, "облик");
  section(ctx, "КТО И ОТКУДА", { x: b.left.x + b.left.w * 0.55, y: b.left.y + 0.03 }, width, height);
  lines(
    ctx,
    ["Из каменоломни у Серых", "Врат. Немногословен.", "Шрам через бровь."],
    { x: b.left.x + b.left.w * 0.55, y: b.left.y + 0.065 },
    width,
    height,
    0.035
  );
  section(ctx, "ЦИФРАМИ", { x: b.left.x, y: b.left.y + b.left.h * 0.66 }, width, height);
  lines(
    ctx,
    ["48 боёв · 41 победа · 2 смерти", "184 200 урона · 6 походов"],
    { x: b.left.x, y: b.left.y + b.left.h * 0.66 + 0.035 },
    width,
    height,
    0.035
  );

  section(ctx, "ЛЕТОПИСЬ ПОДВИГОВ", { x: b.right.x, y: b.right.y + 0.03 }, width, height);
  for (let i = 0; i < 4; i++) {
    const row: w.Rect = { x: b.right.x, y: b.right.y + 0.065 + i * 0.105, w: b.right.w, h: 0.09 };
    w.box(ctx, row, width, height, { hollow: true });
    w.text(ctx, ["Поход 4", "Поход 5", "Поход 5", "Поход 6"][i] ?? "", { x: row.x + 0.012, y: row.y + 0.028 }, width, height, {
      size: 7,
      color: w.WIRE.accent
    });
    w.text(
      ctx,
      [
        "Выстоял один против троих",
        "Пережил бой на 3% HP",
        "Ни одной смерти за акт",
        "Убил Ветерана в одиночку"
      ][i] ?? "",
      { x: row.x + 0.012, y: row.y + 0.062 },
      width,
      height,
      { size: 7, color: w.WIRE.dim }
    );
  }
}

/** VIII-А · Реликвия, «Основное»: слева знак и облачение, справа кит строками. */
function relicBookMain(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "THE BULWARK", "Стандартная · Обычная · класс Танк · уровень 2 · носит Кай", 0);

  figure(ctx, { x: b.left.x, y: b.left.y, w: b.left.w * 0.55, h: b.left.h * 0.62 }, width, height, "знак и облачение");
  section(ctx, "АВТОАТАКА", { x: b.left.x + b.left.w * 0.6, y: b.left.y + 0.03 }, width, height);
  lines(ctx, ["удар щитом", "41 урона · 1.2 с"], { x: b.left.x + b.left.w * 0.6, y: b.left.y + 0.065 }, width, height, 0.035);
  section(ctx, "УРОВЕНЬ И КОПИИ", { x: b.left.x, y: b.left.y + b.left.h * 0.66 }, width, height);
  lines(
    ctx,
    ["уровень 2 · копий собрано 2 / 3"],
    { x: b.left.x, y: b.left.y + b.left.h * 0.66 + 0.035 },
    width,
    height,
    0.035
  );
  section(ctx, "ПАССИВКИ", { x: b.left.x, y: b.left.y + b.left.h * 0.78 }, width, height);
  lines(
    ctx,
    ["+15% брони соседям в строю", "Уровень 2: барьер держится дольше"],
    { x: b.left.x, y: b.left.y + b.left.h * 0.78 + 0.035 },
    width,
    height,
    0.035
  );

  section(ctx, "СПОСОБНОСТИ", { x: b.right.x, y: b.right.y + 0.03 }, width, height);
  for (let i = 0; i < 3; i++) {
    const row: w.Rect = { x: b.right.x, y: b.right.y + 0.065 + i * 0.135, w: b.right.w, h: 0.115 };
    w.box(ctx, row, width, height, { hollow: true });
    w.box(ctx, { x: row.x + 0.01, y: row.y + 0.02, w: 0.04, h: 0.075 }, width, height, {});
    w.text(ctx, ["Стена", "Вызов", "Клятва"][i] ?? "", { x: row.x + 0.06, y: row.y + 0.035 }, width, height, { size: 9 });
    w.text(
      ctx,
      ["40 маны · при HP < 50%", "25 маны · раз в 8 с", "60 маны · при смерти союзника"][i] ?? "",
      { x: row.x + 0.06, y: row.y + 0.066 },
      width,
      height,
      { size: 7, color: w.WIRE.dim }
    );
    w.text(
      ctx,
      ["барьер 180 на 6 с", "тянет врага к себе", "+30% брони всему отряду"][i] ?? "",
      { x: row.x + 0.06, y: row.y + 0.092 },
      width,
      height,
      { size: 7, color: w.WIRE.dim }
    );
  }
}

/** VIII-Б · Реликвия, «Дополнительно»: лор кита и его история в гильдии. */
function relicBookLore(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  const b = book(ctx, width, height, "THE BULWARK", "Стандартная · Обычная · класс Танк · уровень 2 · носит Кай", 1);

  section(ctx, "ЧЕЙ БЫЛ", { x: b.left.x, y: b.left.y + 0.03 }, width, height);
  lines(
    ctx,
    [
      "Щит, за которым погиб последний",
      "защитник Врат. Его несли шестеро,",
      "и ни один не дожил до утра.",
      "",
      "Реликвия не помнит имён — только",
      "то, что за ней всегда кто-то стоял."
    ],
    { x: b.left.x, y: b.left.y + 0.065 },
    width,
    height,
    0.035
  );
  section(ctx, "КАК МЕНЯЕТ НОСИТЕЛЯ", { x: b.left.x, y: b.left.y + 0.31 }, width, height);
  lines(
    ctx,
    ["Плечи каменеют, шаг тяжелеет,", "спина не поворачивается к бою."],
    { x: b.left.x, y: b.left.y + 0.345 },
    width,
    height,
    0.035
  );

  section(ctx, "СТАТИСТИКА", { x: b.right.x, y: b.right.y + 0.03 }, width, height);
  const rows = [
    ["боёв с ней", "31"],
    ["побед", "27"],
    ["носителей было", "3"],
    ["урона принято", "241 800"],
    ["походов с ней", "5"]
  ];
  rows.forEach(([k, v], i) => {
    const y = b.right.y + 0.075 + i * 0.045;
    w.text(ctx, k ?? "", { x: b.right.x, y }, width, height, { size: 7, color: w.WIRE.dim });
    w.text(ctx, v ?? "", { x: b.right.x + b.right.w, y }, width, height, {
      size: 8,
      align: "right",
      color: w.WIRE.text
    });
  });
  section(ctx, "НОСИТЕЛИ", { x: b.right.x, y: b.right.y + 0.32 }, width, height);
  lines(ctx, ["Кай (сейчас) · Ирма · Дан"], { x: b.right.x, y: b.right.y + 0.355 }, width, height);
}

const section_: SectionDef = {
  id: "ui-inspect-cards",
  title: "Осмотр и карточки",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Что открывается по ЛКМ и по ПКМ. После вердикта 22.08.2026 страница отряда показывает состав " +
    "телами, и подпись под телом вмещает имя да Реликвию — значит перки, Судьба, Обеты, уровень и " +
    "травмы живут ТОЛЬКО здесь. Панель осмотра из удобства стала обязательной частью экрана.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "why",
      title: "Две глубины, две мерки",
      lede: "Панель и карточка отвечают на разные вопросы, поэтому и меряются разным."
    },
    {
      kind: "table",
      head: ["Слой", "Вопрос", "Жест", "Чем меряется"],
      rows: [
        ["Панель осмотра", "что этот боец умеет сейчас", "ЛКМ", "скоростью сравнения двух бойцов"],
        ["Расширенная карточка", "кто он и из чего собран", "ПКМ", "полнотой: всё в одном окне"],
        ["Карточка Реликвии", "что умеет кит", "ПКМ или кнопка", "читаемостью способностей"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Травмы рисуются здесь и только здесь</b> — шесть мест ступенями 3/2/1. На страницах " +
        "подготовки их нет по решению 22.08.2026, поэтому карточка обязана показывать их так, " +
        "чтобы «сколько мне осталось смертей» читалось без счёта в уме."
    },
    {
      kind: "head",
      id: "panel",
      title: "Панель осмотра — три места",
      lede: "Фон у всех трёх один: страница отряда «Витриной», ради которой панель и переспорили."
    },
    {
      kind: "stands",
      items: [
        {
          id: "panel-column",
          status: "accepted",
          title: "III-А · Колонка у кромки",
          tag: "как в Guildrun",
          note: "Узкая панель во всю высоту у правой кромки, содержимое списком сверху вниз.",
          facts: [
            ["ширина", "23%"],
            ["высота", "84%"],
            ["содержимое", "списком, со скроллом"],
            ["тела", "закрывает крайнего"]
          ],
          verdict:
            "Самая узкая: состав почти не страдает, а панель вмещает всё, если дать скролл. Цена — скролл и есть плата: перки и снаряжение уходят ниже сгиба, и «сравнить двоих» превращается в прокрутку у каждого.",
          size: [480, 270],
          draw: panelColumn
        },
        {
          id: "panel-wide",
          status: "rejected",
          title: "III-Б · Широкая карта",
          tag: "две колонки внутри",
          note: "Панель шире и делится на «статы» и «человек»: всё помещается без прокрутки.",
          facts: [
            ["ширина", "34%"],
            ["высота", "72%"],
            ["содержимое", "две колонки, без скролла"],
            ["тела", "закрывает двоих"]
          ],
          verdict:
            "Единственная, где перки, Судьба и снаряжение видны одновременно со статами — сравнение бойцов идёт в один взгляд. Цена — треть кадра, и на витрине она накрывает половину состава.",
          size: [480, 270],
          draw: panelWide
        },
        {
          id: "panel-shelf",
          status: "rejected",
          title: "III-В · Полка снизу",
          tag: "тела не трогает",
          note: "Панель лежит вдоль низа кадра тремя секциями в ряд; тела остаются открытыми.",
          facts: [
            ["высота", "34%"],
            ["ширина", "90%"],
            ["содержимое", "три секции в ряд"],
            ["тела", "не закрывает"]
          ],
          verdict:
            "Единственная, что не спорит с витриной: состав остаётся на виду целиком, и выбор «кто идёт» не прерывается. Цена — низ кадра занят постоянно, а лента отряда живёт ровно там же: одному из двух придётся уехать.",
          size: [480, 270],
          draw: panelShelf
        }
      ]
    },
    {
      kind: "head",
      id: "book",
      title: "Карточка-разворот с табами",
      lede:
        "Форма задана Максом 22.08.2026 и не спорится: карточка — книга с двумя страницами, сверху " +
        "два таба, и всё в одну страницу не умещается."
    },
    {
      kind: "table",
      head: ["Таб", "Что несёт"],
      rows: [
        ["I · Основное", "боевое, перки и внешний вид — в облачении текущей Реликвии"],
        ["II · Дополнительно", "лор: кто, откуда, как выглядит — и статистика"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Вид показывается в облачении.</b> «Внешний вид (в данный момент, если есть релик — то " +
        "внешний вид в релике)»: карточка рисует человека таким, какой он сейчас, а не абстрактное " +
        "тело. Значит арт зависит от связки «Сосуд + Реликвия», и это требование к пайплайну арта, " +
        "а не к раскладке."
    },
    {
      kind: "stands",
      items: [
        {
          id: "book-figure",
          status: "rejected",
          title: "VI-А · Портрет и лист",
          tag: "таб I · Основное",
          note: "Левая страница целиком под облик, правая — всё боевое подряд: статы, перки, снаряжение, травмы.",
          facts: [
            ["страница", "43% каждая"],
            ["облик", "во всю левую"],
            ["секций справа", "4"],
            ["таб", "Основное"]
          ],
          verdict:
            "Облик получает столько места, сколько он и заслуживает на карточке человека, — и сразу видно, во что его одела Реликвия. Цена — правая страница несёт четыре секции подряд и к низу становится списком.",
          size: [480, 270],
          draw: bookFigure
        },
        {
          id: "book-halves",
          status: "rejected",
          title: "VI-Б · Человек и боец",
          tag: "таб I · Основное",
          note: "Слева облик и то, что делает его собой — перки, Судьба, Обеты. Справа — чем он дерётся.",
          facts: [
            ["облик", "58% левой страницы"],
            ["слева", "перки, Судьба, Обеты"],
            ["справа", "статы, снаряжение, травмы"],
            ["таб", "Основное"]
          ],
          verdict:
            "Разворот работает как разворот: левая страница про человека, правая про бойца — и глаз знает, на какой стороне искать. Цена — облик ужимается до 58% страницы, а Судьба с Обетами спорят за низ с перками.",
          size: [480, 270],
          draw: bookHalves
        },
        {
          id: "book-cells",
          status: "accepted",
          title: "VI-В · Клетки на развороте",
          tag: "таб I · Основное",
          note: "Облик узкой колонкой, остальное — четыре секции в рамках, по две на страницу.",
          facts: [
            ["облик", "42% левой страницы"],
            ["клеток", "4, по две на страницу"],
            ["границы", "у каждой секции своя"],
            ["таб", "Основное"]
          ],
          verdict:
            "Каждая секция в рамке: травмы находятся мгновенно, а пятую секцию можно добавить не переверстывая разворот. Цена — рамок становится много, и разворот начинает читаться сеткой, а не книгой.",
          size: [480, 270],
          draw: bookCells
        },
        {
          id: "book-lore",
          status: "accepted",
          title: "VII-А · Лор и статистика",
          tag: "таб II · Дополнительно",
          note: "Слева кто и откуда плюс как выглядит, справа — числа забегов и строка летописи.",
          facts: [
            ["слева", "лор и внешность"],
            ["справа", "шесть строк статистики"],
            ["летопись", "внизу правой"],
            ["таб", "Дополнительно"]
          ],
          verdict:
            "Чистое деление: слева слова, справа числа — и ни одно не мешает другому. Цена — облика на этом табе нет вовсе, хотя «как выглядит» описано словами: текст конкурирует с картинкой, которой рядом нет.",
          size: [480, 270],
          draw: bookLore
        },
        {
          id: "book-chronicle",
          status: "rejected",
          title: "VII-Б · Летопись лентой",
          tag: "таб II · Дополнительно",
          note: "Слева облик и краткое досье с цифрами, справа — подвиги записями по походам.",
          facts: [
            ["облик", "есть и на этом табе"],
            ["слева", "досье + цифры строкой"],
            ["справа", "4 записи летописи"],
            ["таб", "Дополнительно"]
          ],
          verdict:
            "Летопись подана как то, чем она и является — списком событий, а не абзацем; и облик остаётся на виду при переходе между табами. Цена — статистика сжата до двух строк, и сравнить двух «Сосудов» числами уже не выйдет.",
          size: [480, 270],
          draw: bookChronicle
        },
        {
          id: "relic-book-main",
          status: "accepted",
          title: "VIII-А · Реликвия, «Основное»",
          tag: "тот же каркас",
          note: "Слева знак и облачение плюс автоатака и пассивки, справа — три способности строками с ценой и условием.",
          facts: [
            ["каркас", "тот же разворот"],
            ["слева", "знак, автоатака, пассивки"],
            ["справа", "3 способности"],
            ["строка", "11.5% высоты"]
          ],
          verdict:
            "Цена и условие каста читаются целиком, а знак и облачение объясняют, во что Реликвия одевает носителя. Цена — пассивки задвинуты в низ левой страницы, где их легко не заметить.",
          size: [480, 270],
          draw: relicBookMain
        },
        {
          id: "relic-book-lore",
          status: "accepted",
          title: "VIII-Б · Реликвия, «Дополнительно»",
          tag: "тот же каркас",
          note: "Слева чей был щит и как он меняет носителя, справа — статистика Реликвии и список её носителей.",
          facts: [
            ["слева", "лор и влияние на носителя"],
            ["справа", "5 строк статистики"],
            ["носители", "списком"],
            ["таб", "Дополнительно"]
          ],
          verdict:
            "«Как меняет носителя» — единственное место, где связь Реликвии с обликом человека объяснена словами, а не только показана. Цена — копии и уровень попали в статистику, хотя это механика и им место на табе «Основное».",
          size: [480, 270],
          draw: relicBookLore
        }
      ]
    },
    {
      kind: "head",
      id: "vessel-card",
      title: "Первая попытка — отклонена целиком",
      lede:
        "Шесть карточек ниже нарисованы до того, как форма была задана: одно окно без табов, всё " +
        "содержимое разом. Вердикт Макса 22.08.2026 — «ничего из этого»; оставлены как след того, " +
        "почему разворот с табами оказался нужен."
    },
    {
      kind: "stands",
      items: [
        {
          id: "card-spread",
          status: "rejected",
          title: "IV-А · Разворот",
          tag: "вид слева, секции справа",
          note: "Вид в полный рост занимает левую пятую, справа две колонки секций.",
          facts: [
            ["окно", "86% кадра"],
            ["вид", "22% ширины"],
            ["секции", "две колонки"],
            ["порядок", "человек → боец"]
          ],
          verdict:
            "Вид работает как портрет в паспорте: смотришь на человека и читаешь про него же. Цена — восемь секций в двух колонках выстраиваются в стену текста, и глазу не за что зацепиться.",
          size: [480, 270],
          draw: cardSpread
        },
        {
          id: "card-columns",
          status: "rejected",
          title: "IV-Б · Три колонны",
          tag: "кто он / чем дерётся",
          note: "Вид, «человек» и «боец» — три вертикали с явной границей между смыслами.",
          facts: [
            ["окно", "86% кадра"],
            ["вид", "19% ширины"],
            ["колонки", "24.5% каждая"],
            ["граница", "лор и механика врозь"]
          ],
          verdict:
            "Разводит две природы фактов: слева кто он, справа чем дерётся — и ни один игрок не ищет травмы среди лора. Цена — колонки одинаковой ширины, хотя левая почти всегда полупустая.",
          size: [480, 270],
          draw: cardColumns
        },
        {
          id: "card-sheet",
          status: "rejected",
          title: "IV-В · Лист состояний",
          tag: "как лист НРИ",
          note: "Шапка во всю ширину, под ней шесть клеток-секций: вид, досье, перки, травмы, снаряжение, статы.",
          facts: [
            ["окно", "86% кадра"],
            ["шапка", "14% высоты"],
            ["клеток", "6, по три в ряд"],
            ["вид", "клетка, а не фон"]
          ],
          verdict:
            "Каждая секция в своей рамке — глаз находит травмы за полсекунды, и добавить седьмую клетку можно не переверстывая. Цена — вид «Сосуда» ужимается до клетки, то есть человек на карточке человека перестаёт быть главным.",
          size: [480, 270],
          draw: cardSheet
        }
      ]
    },
    {
      kind: "head",
      id: "relic-card",
      title: "Расширенная карточка Реликвии — три устройства",
      lede:
        "Общий родитель с карточкой «Сосуда»: то же окно, тот же жест, та же кромка. Содержимое своё — " +
        "кит, уровни, пассивки."
    },
    {
      kind: "stands",
      items: [
        {
          id: "relic-spread",
          status: "rejected",
          title: "V-А · Разворот кита",
          tag: "зеркало IV-А",
          note: "Знак и лор слева, автоатака и три способности строками справа.",
          facts: [
            ["окно", "86% кадра"],
            ["знак", "22% ширины"],
            ["способность", "строкой 9.5%"],
            ["родство", "та же сетка, что IV-А"]
          ],
          verdict:
            "Держит пару с карточкой «Сосуда»: одинаковая сетка, и переход между ними не сбивает глаз. Цена — знак Реликвии занимает место, которое киту нужнее: способностям остаётся половина ширины.",
          size: [480, 270],
          draw: relicSpread
        },
        {
          id: "relic-book",
          status: "rejected",
          title: "V-Б · Две страницы книги",
          tag: "заявка Макса",
          note: "Один разворот: слева «Сосуд» кратко, справа его Реликвия. Переход — перелистывание, а не второе окно.",
          facts: [
            ["окно", "88% кадра"],
            ["страница", "43% каждая"],
            ["корешок", "линия по центру"],
            ["переход", "листанием"]
          ],
          verdict:
            "Отвечает на вопрос, который иначе требует двух окон: «подходит ли этот кит этому человеку» — обе половины видны разом. Цена — каждой стороне достаётся половина места, поэтому обе показывают сокращённо, и полная карточка всё равно нужна отдельно.",
          size: [480, 270],
          draw: relicBook
        },
        {
          id: "relic-list",
          status: "rejected",
          title: "V-В · Список способностей",
          tag: "кит во всю ширину",
          note: "Шапка со знаком, под ней четыре строки во всю ширину: автоатака и три способности с ценой и условием.",
          facts: [
            ["окно", "86% кадра"],
            ["строка", "10% высоты"],
            ["ширина строки", "полная"],
            ["лор", "внизу, одной строкой"]
          ],
          verdict:
            "Условие каста и цена читаются целиком, без переносов — а именно они решают, возьмёшь ли ты этот кит. Цена — лор и знак задвинуты, и Реликвия перестаёт быть вещью с историей, становясь таблицей.",
          size: [480, 270],
          draw: relicList
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто и решается вместе с выбором:</b> ставит ли панель осмотра паузу в бою · остаётся " +
        "ли тултип там, где есть панель · чем ПКМ и перетаскивание заменяются на геймпаде · что " +
        "показывает панель для ВРАГА — тот же состав или урезанный."
    }
  ]
};

export default section_;
