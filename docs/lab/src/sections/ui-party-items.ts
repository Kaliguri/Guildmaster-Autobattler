/* Отряд и предметы: две страницы одного экрана подготовки.

   Задача пришла от Макса 22.08.2026 и описана им точно: на КАЖДОЙ странице стоит состав из
   четырёх «Сосудов» — экран не переключает предмет разговора, он переключает, ЧТО с этим составом
   делают. Отсюда общий каркас у всех раскладок: лента страниц сверху, четвёрка на месте, склад
   внизу; спорят они тем, где четвёрка стоит и сколько места отдано её содержимому.

   Числа состава — из ГДД (`gdd/50-modes-ux/preparation-screens`), не с потолка:
   - в бой выходят 4, нижняя лента — ВЕСЬ отряд восемью местами, четвёрка в ней же (6 открыто,
     2 за улучшением);
   - предметов на «Сосуде» 3 (`GameConfig.VesselItemSlots`), четвёртый слот закрыт;
   - перков ровно два, плюс и минус;
   - боевую роль несёт Реликвия, своих статов у «Сосуда» нет.

   Чего здесь НЕТ намеренно: травм. Решение Макса 22.08.2026 — на плитках подготовки они не
   показываются, их дом расширенная карточка. Шесть пипсов на каждой из восьми плиток превращают
   ленту отряда в медкарту, а решается там состав. Предметы отряда (Party) тоже отложены до своей
   вкладки.

   Панель осмотра рисуется здесь как часть кадра: она живёт везде, где есть юнит, и вопрос
   «постоянная она или выезжает» — одна из осей спора между раскладками.

   Чертёж серый и в долях кадра 1920x1080 — по правилам ui-wire. */

import * as w from "./ui-wire.js";
import type { SectionDef } from "../types.js";

const NAMES = ["Ирма", "Кай", "Дан", "Сув", "Лех"];
const RELICS = ["Клинок", "Щит", "Посох", "Ветер"];

/** Шапка страницы: имя экрана, лента страниц, счётчик отряда. Одна на все раскладки — иначе
 *  сравнение уедет в «а тут заголовок другой», вместо того чтобы спорить о раскладке. */
function header(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  active: number
): void {
  w.text(ctx, "ПОДГОТОВКА", { x: 0.05, y: 0.055 }, width, height, { size: 11 });
  ["ОТРЯД", "ПРЕДМЕТЫ", "РЕЛИКВИИ", "ПОВЕДЕНИЕ"].forEach((t, i) => {
    w.box(ctx, { x: 0.05 + i * 0.115, y: 0.09, w: 0.11, h: 0.045 }, width, height, {
      label: t,
      size: 8,
      lit: i === active
    });
  });
  w.text(ctx, "в отряде 5 / 6", { x: 0.95, y: 0.055 }, width, height, {
    size: 8,
    align: "right",
    color: w.WIRE.dim
  });
}

/** Плитка «Сосуда». Состав задал Макс: круглый портрет слева, имя сверху, остальная площадь — под
 *  содержимое человека. Круг против квадрата — не украшение: круглое это человек, квадратное вещь,
 *  и на любом расстоянии эти два рода объектов не путаются. */
function vessel(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  opts: { i: number; lit?: boolean; empty?: boolean; slots?: boolean; brief?: boolean }
): void {
  w.box(ctx, r, width, height, { lit: opts.lit, dashed: opts.empty });
  if (opts.empty) {
    w.text(ctx, "пустой слот", { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
      align: "center",
      size: 8,
      color: w.WIRE.dim
    });
    return;
  }

  const pad = 0.012;
  const rad = Math.min(r.h * 0.26, 0.055);
  const cx = r.x + pad + (rad * height) / width;
  w.disc(ctx, { x: cx, y: r.y + pad + rad, r: rad }, width, height, { label: "лицо" });

  const tx = cx + (rad * height) / width + 0.012;
  w.text(ctx, NAMES[opts.i] ?? "имя", { x: tx, y: r.y + pad + rad * 0.5 }, width, height, { size: 10 });
  w.text(ctx, `Реликвия: ${RELICS[opts.i] ?? "—"}`, { x: tx, y: r.y + pad + rad * 1.1 }, width, height, {
    size: 7,
    color: w.WIRE.accent
  });
  w.text(ctx, "Танк · уровень 3", { x: tx, y: r.y + pad + rad * 1.6 }, width, height, {
    size: 7,
    color: w.WIRE.dim
  });

  if (opts.brief) {
    if (opts.slots) itemRow(ctx, { x: r.x + pad, y: r.y + pad * 2 + rad * 2 }, width, height, 0.055, opts.i);
    return;
  }

  // Основная часть плитки: то, чем этот человек отличается от любого другого с тем же китом.
  const bodyY = r.y + pad * 2 + rad * 2;
  const bw = r.w - pad * 2;
  w.box(ctx, { x: r.x + pad, y: bodyY, w: bw * 0.48, h: 0.036 }, width, height, {
    label: "+ перк",
    size: 7
  });
  w.box(ctx, { x: r.x + pad + bw * 0.52, y: bodyY, w: bw * 0.48, h: 0.036 }, width, height, {
    label: "− перк",
    size: 7
  });
  w.box(ctx, { x: r.x + pad, y: bodyY + 0.046, w: bw, h: 0.034 }, width, height, {
    label: "Судьба: Главный герой · 4/10",
    size: 7,
    hollow: true,
    dashed: true
  });

  if (opts.slots) itemRow(ctx, { x: r.x + pad, y: bodyY + 0.09 }, width, height, 0.05, opts.i);
}

/** Ряд слотов предмета: три открытых и четвёртый закрытый — по прямому слову Макса. Квадратные. */
function itemRow(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number },
  width: number,
  height: number,
  size: number,
  seed = 0,
  vertical = false
): void {
  const sw = (size * height) / width;
  for (let i = 0; i < 4; i++) {
    const r: w.Rect = {
      x: at.x + (vertical ? 0 : i * (sw + 0.008)),
      y: at.y + (vertical ? i * (size + 0.014) : 0),
      w: sw,
      h: size
    };
    if (i === 3) {
      w.box(ctx, r, width, height, { dashed: true, hollow: true });
      w.lock(ctx, r, width, height);
      continue;
    }
    const filled = i <= (seed + 1) % 3;
    w.box(ctx, r, width, height, { lit: filled, dashed: !filled });
    if (!filled) {
      w.text(ctx, "+", { x: r.x + r.w / 2, y: r.y + r.h / 2 }, width, height, {
        align: "center",
        size: 10,
        color: w.WIRE.dim
      });
    }
  }
}

/** Нижняя лента: ВЕСЬ отряд восемью местами, четвёрка боя в ней же — решение Макса 22.08.2026.
 *  Поэтому первые четыре помечены «в бою», два места пусты, два закрыты до улучшения. */
function bench(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number; w: number; h: number },
  width: number,
  height: number
): void {
  w.text(ctx, "ОТРЯД ГИЛЬДИИ", { x: at.x, y: at.y - 0.026 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.text(ctx, "8 мест · 6 открыто", { x: at.x + at.w, y: at.y - 0.026 }, width, height, {
    size: 7,
    align: "right",
    color: w.WIRE.dim
  });

  const gap = 0.008;
  const cw = (at.w - gap * 7) / 8;
  for (let i = 0; i < 8; i++) {
    const r: w.Rect = { x: at.x + i * (cw + gap), y: at.y, w: cw, h: at.h };
    if (i >= 6) {
      w.box(ctx, r, width, height, { dashed: true, hollow: true });
      w.lock(ctx, r, width, height);
      continue;
    }
    if (i >= 5) {
      w.box(ctx, r, width, height, { dashed: true, label: "пусто", size: 7 });
      continue;
    }
    w.box(ctx, r, width, height, { lit: i < 4 });
    const rad = Math.min(at.h * 0.28, 0.04);
    w.disc(ctx, { x: r.x + r.w / 2, y: r.y + 0.018 + rad, r: rad }, width, height, {});
    w.text(ctx, NAMES[i] ?? "имя", { x: r.x + r.w / 2, y: r.y + at.h * 0.72 }, width, height, {
      align: "center",
      size: 8
    });
    w.text(ctx, i < 4 ? "в бою" : "в запасе", { x: r.x + r.w / 2, y: r.y + at.h * 0.9 }, width, height, {
      align: "center",
      size: 7,
      color: i < 4 ? w.WIRE.accent : w.WIRE.dim
    });
  }
}

/** Склад предметов: сетка плиток с поиском. Отдельно от ленты отряда — вещи и люди не смешиваются. */
function stash(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number; w: number; h: number },
  width: number,
  height: number,
  cols: number,
  rows: number
): void {
  w.text(ctx, "ПРЕДМЕТЫ В ЗАПАСЕ", { x: at.x, y: at.y - 0.026 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  w.box(ctx, { x: at.x + at.w - 0.15, y: at.y - 0.042, w: 0.15, h: 0.032 }, width, height, {
    label: "поиск · сортировка",
    size: 7,
    hollow: true
  });

  const gap = 0.007;
  const cw = (at.w - gap * (cols - 1)) / cols;
  const ch = (at.h - gap * (rows - 1)) / rows;
  for (let i = 0; i < cols * rows; i++) {
    w.box(
      ctx,
      {
        x: at.x + (i % cols) * (cw + gap),
        y: at.y + Math.floor(i / cols) * (ch + gap),
        w: cw,
        h: ch
      },
      width,
      height,
      { lit: i === 2 }
    );
  }
}

/** Панель осмотра: то, что открывается по ЛКМ и живёт везде, где есть юнит. Две кнопки внизу,
 *  вторая гаснет без Реликвии. Рисуется во всех раскладках, чтобы спорить о её МЕСТЕ, а не о
 *  её существовании. */
function inspect(
  ctx: CanvasRenderingContext2D,
  r: w.Rect,
  width: number,
  height: number,
  opts: { floating?: boolean } = {}
): void {
  if (opts.floating) {
    // Заливка кадром, а не пропуск: выехавшая панель закрывает то, что под ней, и чертёж обязан
    // показать именно это — иначе спор «что она загораживает» решается на глаз.
    const [bx, by, bw2, bh2] = w.px(r, width, height);
    ctx.fillStyle = w.WIRE.frame;
    ctx.fillRect(bx, by, bw2, bh2);
  }
  w.box(ctx, r, width, height, { lit: opts.floating });
  const pad = 0.014;
  const rad = Math.min(r.h * 0.1, 0.045);
  w.disc(ctx, { x: r.x + pad + (rad * height) / width, y: r.y + pad + rad, r: rad }, width, height, {
    label: "лицо"
  });
  const tx = r.x + pad * 2 + (rad * 2 * height) / width;
  w.text(ctx, "КАЙ", { x: tx, y: r.y + pad + rad * 0.6 }, width, height, { size: 10 });
  w.text(ctx, "Щит · Танк", { x: tx, y: r.y + pad + rad * 1.3 }, width, height, {
    size: 7,
    color: w.WIRE.accent
  });

  const listY = r.y + pad * 2 + rad * 2;
  ["HP 820", "броня 24", "урон 41", "скорость 3.2"].forEach((s, i) => {
    w.text(ctx, s, { x: r.x + pad, y: listY + i * 0.032 }, width, height, { size: 7, color: w.WIRE.dim });
  });

  w.text(ctx, "снаряжение", { x: r.x + pad, y: listY + 0.16 }, width, height, {
    size: 7,
    color: w.WIRE.accent
  });
  itemRow(ctx, { x: r.x + pad, y: listY + 0.185 }, width, height, 0.045, 1);

  // Две кнопки: вторая тусклая, но живая — правило ui-feedback §1.
  const btnY = r.y + r.h - pad - 0.05;
  w.box(ctx, { x: r.x + pad, y: btnY, w: r.w / 2 - pad * 1.5, h: 0.05 }, width, height, {
    label: "О СОСУДЕ",
    size: 8,
    lit: true
  });
  w.box(ctx, { x: r.x + r.w / 2 + pad * 0.5, y: btnY, w: r.w / 2 - pad * 1.5, h: 0.05 }, width, height, {
    label: "о Реликвии",
    size: 8
  });
}

function toBattle(ctx: CanvasRenderingContext2D, r: w.Rect, width: number, height: number): void {
  w.box(ctx, r, width, height, { label: "В БОЙ", size: 9, lit: true });
}

/* ══ Страница «Отряд» ══════════════════════════════════════════════════════ */

/** I-А · Ложа: четвёрка 2x2 занимает левые две трети, панель осмотра стоит справа постоянно. */
function partyBox(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 0);

  const cw = 0.3;
  const chh = 0.235;
  for (let i = 0; i < 4; i++) {
    vessel(
      ctx,
      { x: 0.05 + (i % 2) * (cw + 0.02), y: 0.17 + Math.floor(i / 2) * (chh + 0.02), w: cw, h: chh },
      width,
      height,
      { i, lit: i === 1 }
    );
  }
  w.measure(ctx, { x: 0.05, y: 0.425, w: cw, h: chh }, "плитка 30%", width, height);

  inspect(ctx, { x: 0.71, y: 0.17, w: 0.24, h: 0.49 }, width, height);
  bench(ctx, { x: 0.05, y: 0.72, w: 0.76, h: 0.17 }, width, height);
  toBattle(ctx, { x: 0.83, y: 0.79, w: 0.12, h: 0.07 }, width, height);
}

/** I-Б · Разворот: четвёрка широкими плитками во всю ширину, панель осмотра выезжает по клику. */
function partySpread(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 0);

  const cw = 0.44;
  const chh = 0.23;
  for (let i = 0; i < 4; i++) {
    vessel(
      ctx,
      { x: 0.05 + (i % 2) * (cw + 0.02), y: 0.17 + Math.floor(i / 2) * (chh + 0.02), w: cw, h: chh },
      width,
      height,
      { i, lit: i === 1 }
    );
  }
  w.measure(ctx, { x: 0.05, y: 0.42, w: cw, h: chh }, "плитка 44%", width, height);

  bench(ctx, { x: 0.05, y: 0.71, w: 0.78, h: 0.18 }, width, height);
  toBattle(ctx, { x: 0.85, y: 0.78, w: 0.1, h: 0.07 }, width, height);

  // Панель осмотра приходит поверх правого края и гасит под собой часть плиток.
  inspect(ctx, { x: 0.66, y: 0.13, w: 0.29, h: 0.54 }, width, height, { floating: true });
  w.callout(ctx, { x: 0.66, y: 0.62 }, { x: 0.6, y: 0.66 }, "выезжает по ЛКМ и гасит две плитки", width, height, "right");
}

/** I-В · Кокпит: четвёрка компактными плитками слева, вся правая половина — постоянный осмотр. */
function partyCockpit(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 0);

  const cw = 0.21;
  const chh = 0.16;
  for (let i = 0; i < 4; i++) {
    vessel(
      ctx,
      { x: 0.05 + (i % 2) * (cw + 0.015), y: 0.17 + Math.floor(i / 2) * (chh + 0.015), w: cw, h: chh },
      width,
      height,
      { i, lit: i === 1, brief: true }
    );
  }
  w.callout(ctx, { x: 0.2, y: 0.28 }, { x: 0.1, y: 0.62 }, "плитка без содержимого", width, height);

  inspect(ctx, { x: 0.5, y: 0.17, w: 0.45, h: 0.55 }, width, height);

  bench(ctx, { x: 0.05, y: 0.79, w: 0.76, h: 0.15 }, width, height);
  toBattle(ctx, { x: 0.83, y: 0.84, w: 0.12, h: 0.065 }, width, height);
}

/** I-Г · Витрина: четвёрка стоит на живой арене, лента отряда — полка снизу. */
export function partyStage(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.35);
  header(ctx, width, height, 0);

  // Тела стоят в кадре, подпись — под каждым, без панели.
  for (let i = 0; i < 4; i++) {
    const x = 0.14 + i * 0.16;
    w.box(ctx, { x: x - 0.03, y: 0.2, w: 0.09, h: 0.26 }, width, height, {
      hollow: true,
      dashed: true,
      label: "тело",
      size: 8
    });
    w.disc(ctx, { x: x + 0.015, y: 0.5, r: 0.035 }, width, height, { lit: i === 1 });
    w.text(ctx, NAMES[i] ?? "имя", { x: x + 0.015, y: 0.57 }, width, height, {
      align: "center",
      size: 8
    });
    w.text(ctx, RELICS[i] ?? "—", { x: x + 0.015, y: 0.605 }, width, height, {
      align: "center",
      size: 7,
      color: w.WIRE.accent
    });
  }
  w.callout(ctx, { x: 0.45, y: 0.42 }, { x: 0.42, y: 0.66 }, "живая расстановка за интерфейсом", width, height, "center");

  inspect(ctx, { x: 0.72, y: 0.16, w: 0.23, h: 0.5 }, width, height, { floating: true });
  bench(ctx, { x: 0.05, y: 0.73, w: 0.76, h: 0.16 }, width, height);
  toBattle(ctx, { x: 0.83, y: 0.79, w: 0.12, h: 0.07 }, width, height);
}

/* ══ Страница «Предметы» ═══════════════════════════════════════════════════ */

/** II-А · Строки: «Сосуд» строкой, четыре квадратных слота в ряд, склад под ними. */
export function itemsRows(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 1);

  for (let i = 0; i < 4; i++) {
    const r: w.Rect = { x: 0.05, y: 0.17 + i * 0.1, w: 0.62, h: 0.09 };
    w.box(ctx, r, width, height, { lit: i === 1 });
    const rad = 0.032;
    w.disc(ctx, { x: r.x + 0.022, y: r.y + r.h / 2, r: rad }, width, height, { label: "лицо" });
    w.text(ctx, NAMES[i] ?? "имя", { x: r.x + 0.05, y: r.y + 0.03 }, width, height, { size: 9 });
    w.text(ctx, RELICS[i] ?? "—", { x: r.x + 0.05, y: r.y + 0.062 }, width, height, {
      size: 7,
      color: w.WIRE.accent
    });
    itemRow(ctx, { x: r.x + 0.17, y: r.y + 0.014 }, width, height, 0.062, i);
  }

  inspect(ctx, { x: 0.7, y: 0.17, w: 0.25, h: 0.45 }, width, height);
  stash(ctx, { x: 0.05, y: 0.65, w: 0.62, h: 0.24 }, width, height, 9, 2);
  toBattle(ctx, { x: 0.83, y: 0.82, w: 0.12, h: 0.07 }, width, height);
}

/** II-Б · Пары: та же сетка 2x2, что на странице отряда, слоты внутри плитки. */
function itemsPairs(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 1);

  const cw = 0.3;
  const chh = 0.26;
  for (let i = 0; i < 4; i++) {
    vessel(
      ctx,
      { x: 0.05 + (i % 2) * (cw + 0.02), y: 0.17 + Math.floor(i / 2) * (chh + 0.02), w: cw, h: chh },
      width,
      height,
      { i, lit: i === 1, slots: true, brief: true }
    );
  }
  w.callout(ctx, { x: 0.36, y: 0.6 }, { x: 0.4, y: 0.68 }, "та же сетка, что в отряде", width, height);

  inspect(ctx, { x: 0.71, y: 0.17, w: 0.24, h: 0.45 }, width, height);
  stash(ctx, { x: 0.05, y: 0.75, w: 0.9, h: 0.15 }, width, height, 14, 1);
}

/** II-В · Верстак: один «Сосуд» крупно со слотами, трое корешками, деталь предмета справа. */
function itemsBench(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 1);

  // Корешки: трое остальных сжаты в узкие полосы слева.
  for (let i = 0; i < 4; i++) {
    const r: w.Rect = { x: 0.05, y: 0.17 + i * 0.075, w: 0.13, h: 0.065 };
    w.box(ctx, r, width, height, { lit: i === 1 });
    w.disc(ctx, { x: r.x + 0.018, y: r.y + r.h / 2, r: 0.024 }, width, height, {});
    w.text(ctx, NAMES[i] ?? "имя", { x: r.x + 0.042, y: r.y + r.h / 2 }, width, height, { size: 8 });
  }

  // Выбранный — крупно, слоты во всю ширину рабочей зоны.
  const stage: w.Rect = { x: 0.2, y: 0.17, w: 0.45, h: 0.42 };
  w.box(ctx, stage, width, height, {});
  w.disc(ctx, { x: 0.26, y: 0.28, r: 0.07 }, width, height, { label: "лицо", lit: true });
  w.text(ctx, "КАЙ", { x: 0.32, y: 0.24 }, width, height, { size: 12 });
  w.text(ctx, "Щит · Танк · уровень 3", { x: 0.32, y: 0.285 }, width, height, {
    size: 8,
    color: w.WIRE.accent
  });
  itemRow(ctx, { x: 0.22, y: 0.42 }, width, height, 0.13, 1);
  w.measure(ctx, { x: 0.22, y: 0.42, w: 0.073, h: 0.13 }, "слот 13%", width, height);

  const det: w.Rect = { x: 0.68, y: 0.17, w: 0.27, h: 0.42 };
  w.box(ctx, det, width, height, { hollow: true });
  w.text(ctx, "РУНА ПЛАМЕНИ", { x: 0.7, y: 0.215 }, width, height, { size: 10 });
  ["зачаровывающий", "урон → огонь", "вешает Поджог", "цена 40 золота"].forEach((s, i) => {
    w.text(ctx, s, { x: 0.7, y: 0.26 + i * 0.032 }, width, height, { size: 7, color: w.WIRE.dim });
  });
  w.box(ctx, { x: 0.7, y: 0.5, w: 0.23, h: 0.05 }, width, height, { label: "надеть на Кая", size: 8, lit: true });

  stash(ctx, { x: 0.05, y: 0.68, w: 0.9, h: 0.21 }, width, height, 14, 2);
}

/** II-Г · Колонны: четыре колонки, слоты столбиком под портретом. */
function itemsColumns(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.scrim(ctx, width, height, 0.75);
  header(ctx, width, height, 1);

  for (let i = 0; i < 4; i++) {
    const r: w.Rect = { x: 0.05 + i * 0.16, w: 0.145, y: 0.17, h: 0.55 };
    w.box(ctx, r, width, height, { lit: i === 1 });
    w.disc(ctx, { x: r.x + r.w / 2, y: r.y + 0.075, r: 0.045 }, width, height, { label: "лицо" });
    w.text(ctx, NAMES[i] ?? "имя", { x: r.x + r.w / 2, y: r.y + 0.155 }, width, height, {
      align: "center",
      size: 9
    });
    w.text(ctx, RELICS[i] ?? "—", { x: r.x + r.w / 2, y: r.y + 0.19 }, width, height, {
      align: "center",
      size: 7,
      color: w.WIRE.accent
    });
    itemRow(ctx, { x: r.x + (r.w - 0.039) / 2, y: r.y + 0.225 }, width, height, 0.069, i, true);
  }
  w.measure(ctx, { x: 0.05, y: 0.17, w: 0.145, h: 0.55 }, "колонка 14.5%", width, height);

  inspect(ctx, { x: 0.71, y: 0.17, w: 0.24, h: 0.45 }, width, height);
  stash(ctx, { x: 0.05, y: 0.78, w: 0.9, h: 0.12 }, width, height, 14, 1);
}

const section: SectionDef = {
  id: "ui-party-items",
  title: "Отряд и предметы",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Две страницы одного экрана подготовки. Состав из четырёх «Сосудов» стоит на КАЖДОЙ из них — " +
    "страницы переключают не предмет разговора, а то, что с составом делают. Спорят раскладки тем, " +
    "где стоит четвёрка, сколько места отдано её содержимому и где живёт панель осмотра.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "rules",
      title: "Что задано и не спорится",
      lede: "Числа — из ГДД, не из раскладки. Раскладка решает только, как их разложить."
    },
    {
      kind: "table",
      head: ["Что", "Сколько", "Откуда"],
      rows: [
        ["Основные слота, два ряда по два", "4", "в бою четверо"],
        ["Места в ленте отряда", "8, открыто 6", "весь отряд, четвёрка в них же"],
        ["Слоты предмета у «Сосуда»", "3 открытых + 1 закрытый", "GameConfig.VesselItemSlots"],
        ["Форма", "человек — круг, вещь — квадрат", "не путаются на любом расстоянии"],
        ["Травмы на плитках", "не показываются", "их дом — расширенная карточка"],
        ["Предметы отряда", "своя вкладка, отложена", "решение 22.08.2026"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Панель осмотра нарисована во всех раскладках намеренно.</b> Она открывается по ЛКМ и " +
        "живёт везде, где есть юнит, — значит вопрос не «быть ли ей», а <b>где она стоит и что " +
        "гасит под собой</b>. Постоянная панель ест ширину у состава; выезжающая ширину сохраняет, " +
        "но закрывает часть плиток именно в тот момент, когда игрок сравнивает бойцов."
    },
    {
      kind: "head",
      id: "party",
      title: "Страница «Отряд» — четыре раскладки",
      lede: "Вопрос страницы: кто выйдет в бой. Принята I-Г (Макс, 22.08.2026). Доли — от кадра 1920x1080."
    },
    {
      kind: "stands",
      items: [
        {
          id: "party-box",
          status: "rejected",
          title: "I-А · Ложа",
          tag: "две трети составу",
          note:
            "Четвёрка 2x2 занимает левые две трети, панель осмотра стоит справа постоянно, лента " +
            "отряда — во всю ширину под ними.",
          facts: [
            ["плитка", "30% x 23.5%"],
            ["панель осмотра", "24%, постоянная"],
            ["лента отряда", "8 мест, 76%"],
            ["содержимое плитки", "перки + Судьба"]
          ],
          verdict:
            "Состав и запас видны одновременно, панель никогда ничего не закрывает. Цена — плитка теряет треть ширины ради панели, которая большую часть времени показывает того же, кого игрок и так видит.",
          size: [480, 270],
          draw: partyBox
        },
        {
          id: "party-spread",
          status: "rejected",
          title: "I-Б · Разворот",
          tag: "плитка максимальная",
          note:
            "Четвёрка широкими плитками во всю ширину кадра, панель осмотра выезжает поверх " +
            "правого края только по клику.",
          facts: [
            ["плитка", "44% x 23%"],
            ["панель осмотра", "29%, выезжает"],
            ["лента отряда", "8 мест, 78%"],
            ["содержимое плитки", "влезает всё"]
          ],
          verdict:
            "Самая крупная плитка из четырёх: в неё помещаются перки, Судьба и снаряжение без сокращений. Цена — выехавшая панель накрывает двух «Сосудов» из четырёх, то есть закрывает ровно то, с чем сравнивают.",
          size: [480, 270],
          draw: partySpread
        },
        {
          id: "party-cockpit",
          status: "rejected",
          title: "I-В · Кокпит",
          tag: "половина кадра осмотру",
          note:
            "Четвёрка сжата в компактные плитки без содержимого, вся правая половина отдана " +
            "постоянной панели осмотра.",
          facts: [
            ["плитка", "21% x 16%, кратко"],
            ["панель осмотра", "45%, постоянная"],
            ["лента отряда", "8 мест, 40%"],
            ["содержимое плитки", "имя и Реликвия"]
          ],
          verdict:
            "Единственная раскладка, где осмотр показывает по-настоящему много: 45% ширины хватает на статы, снаряжение и поведение сразу. Цена — состав превращается в список имён, и «кто у меня собран» приходится читать по одному.",
          size: [480, 270],
          draw: partyCockpit
        },
        {
          id: "party-stage",
          status: "accepted",
          title: "I-Г · Витрина",
          tag: "состав телами",
          note:
            "Четвёрка стоит телами на живой арене, подпись под каждым, лента отряда — полка снизу, " +
            "панель осмотра поверх мира.",
          facts: [
            ["тело", "9% x 26%"],
            ["панель осмотра", "23%, поверх мира"],
            ["лента отряда", "8 мест, 76%"],
            ["вуаль", "35% вместо 75%"]
          ],
          verdict:
            "Состав читается тем же способом, что и в бою: телами, а не карточками — и решение «кто идёт» принимается в тех же образах, в которых будет проверяться. Цена — под тела уходит середина кадра, а перкам и Судьбе на подписи места нет вовсе: они уезжают в панель и карточку целиком.",
          size: [480, 270],
          draw: partyStage
        }
      ]
    },
    {
      kind: "head",
      id: "items",
      title: "Страница «Предметы» — четыре раскладки",
      lede:
        "Вопрос страницы: что на ком надето. Четвёрка та же и на том же месте — меняется только то, " +
        "как к ней прицеплены четыре квадратных слота."
    },
    {
      kind: "stands",
      items: [
        {
          id: "items-rows",
          status: "accepted",
          title: "II-А · Строки",
          tag: "слоты в ряд",
          note:
            "«Сосуд» строкой: портрет, имя, четыре слота в ряд. Четыре строки друг под другом, " +
            "склад сеткой под ними, панель осмотра справа.",
          facts: [
            ["строка", "62% x 9%"],
            ["слот", "6.2% высоты"],
            ["склад", "9 x 2 плитки"],
            ["панель осмотра", "25%, постоянная"]
          ],
          verdict:
            "Слоты выстроены в одну вертикаль — «у кого пусто» читается одним взглядом сверху вниз, и это главный вопрос страницы. Цена — строка не даёт места ничему, кроме слотов: имя и Реликвия сжаты до двух строк текста.",
          size: [480, 270],
          draw: itemsRows
        },
        {
          id: "items-pairs",
          status: "rejected",
          title: "II-Б · Пары",
          tag: "сетка как в отряде",
          note:
            "Ровно та же сетка 2x2, что на странице отряда, слоты внутри плитки. Склад — узкой " +
            "лентой во всю ширину внизу.",
          facts: [
            ["плитка", "30% x 26%"],
            ["слот", "5% высоты"],
            ["склад", "14 плиток в строку"],
            ["переключение", "плитки не двигаются"]
          ],
          verdict:
            "Единственная, где при переключении страниц плитки остаются на своих местах: экран меняет содержимое, а не раскладку, и глаз не ищет заново. Цена — слоты уходят вглубь плитки, и сравнение «у кого пусто» становится обходом четырёх углов вместо одной вертикали.",
          size: [480, 270],
          draw: itemsPairs
        },
        {
          id: "items-bench",
          status: "rejected",
          title: "II-В · Верстак",
          tag: "один в фокусе",
          note:
            "Трое сжаты в корешки слева, выбранный — крупно по центру с большими слотами, справа " +
            "деталь предмета с кнопкой «надеть».",
          facts: [
            ["корешок", "13% x 6.5%"],
            ["слот", "13% высоты"],
            ["деталь предмета", "27%"],
            ["склад", "14 x 2 плитки"]
          ],
          verdict:
            "Самые крупные слоты и единственное место, где описание предмета читается без тултипа — надевание становится осмысленным, а не перетаскиванием иконок. Цена — состав виден только именами, и «переложить от Кая к Ирме» требует двух переключений фокуса.",
          size: [480, 270],
          draw: itemsBench
        },
        {
          id: "items-columns",
          status: "rejected",
          title: "II-Г · Колонны",
          tag: "слоты столбиком",
          note:
            "Четыре колонки: портрет сверху, под ним четыре слота столбиком. Склад — полосой " +
            "внизу, панель осмотра справа.",
          facts: [
            ["колонка", "14.5% x 55%"],
            ["слот", "8% высоты"],
            ["склад", "14 плиток в строку"],
            ["панель осмотра", "24%, постоянная"]
          ],
          verdict:
            "Человек и его вещи стоят одной вертикалью — связь «чьё это» не собирается в голове, а видна. Цена — четыре колонки съедают высоту кадра, складу остаётся одна строка, и весь запас приходится листать.",
          size: [480, 270],
          draw: itemsColumns
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Второй заход:</b> панель осмотра крупным планом (её состав и место двух кнопок) и " +
        "расширенные карточки «Сосуда» и Реликвии — окно на 80–90% кадра по ПКМ. У карточек общий " +
        "родитель, но разное содержимое; заявка Макса — оформить пару как «две страницы книги». " +
        "Рисуются после того, как выбрана сетка страниц: карточка наследует её ритм."
    }
  ]
};

export default section;
