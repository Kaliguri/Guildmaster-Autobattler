/* Зоны влияния на карте — то, что делаем СЕЙЧАС.

   Решение Макса 2026-08-02: местность не рисуем вовсе. Карта остаётся картой узлов и дорог, а
   единственный территориальный слой на ней — зона влияния фракции. Рельеф продолжает жить как
   МЕХАНИКА (форма области решает, как ходят дороги), но собственной картинки не имеет: его видно
   по рисунку графа. Атласная земля и летающие острова отложены — раздел «Земля и страна».

   Здесь два вопроса: чем рисовать саму зону (шесть вариантов) и как узел говорит о своей
   принадлежности (три варианта). Плюс запланированные эффекты живьём. */

import { tick } from "../clock.js";
import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

const GOBLIN = "132,214,92";
const BANDIT = "255,96,80";
const CROWN = "120,168,255";

/* ---------- общая сцена: узлы и дороги ---------- */

interface Dot {
  x: number;
  y: number;
  zone: string;
}

function scene(w: number, h: number): { dots: Dot[]; pairs: Array<[number, number]> } {
  const cols = [2, 3, 3, 3, 2];
  const left = w * 0.14;
  const stepX = (w * 0.72) / (cols.length - 1);
  const dots: Dot[] = [];
  const starts: number[] = [];

  cols.forEach((rows, c) => {
    starts.push(dots.length);
    for (let r = 0; r < rows; r++)
      dots.push({
        x: left + c * stepX,
        y: h * 0.47 + (r - (rows - 1) / 2) * (h * 0.2),
        zone: c <= 1 ? GOBLIN : c === 2 && r === 0 ? GOBLIN : BANDIT
      });
  });

  const pairs: Array<[number, number]> = [];
  for (let c = 0; c + 1 < cols.length; c++) {
    const a0 = starts[c] ?? 0;
    const b0 = starts[c + 1] ?? 0;
    const ra = cols[c] ?? 1;
    const rb = cols[c + 1] ?? 1;
    for (let i = 0; i < ra; i++) {
      const t = ra === 1 ? 0 : i / (ra - 1);
      const centre = Math.round(t * (rb - 1));
      for (let d = -1; d <= 1; d++) {
        const j = centre + d;
        if (j >= 0 && j < rb) pairs.push([a0 + i, b0 + j]);
      }
    }
  }
  return { dots, pairs };
}

function roads(ctx: CanvasRenderingContext2D, dots: Dot[], pairs: Array<[number, number]>): void {
  ctx.strokeStyle = "rgba(147,128,94,.5)";
  ctx.lineWidth = 1.4;
  for (const [a, b] of pairs) {
    const from = dots[a];
    const to = dots[b];
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  }
}

type Belong = "ring" | "flag" | "halo" | "none";

function nodes(ctx: CanvasRenderingContext2D, dots: Dot[], belong: Belong, r = 9): void {
  for (const d of dots) {
    if (belong === "halo") {
      const g = ctx.createRadialGradient(d.x, d.y, r, d.x, d.y, r * 2.6);
      g.addColorStop(0, `rgba(${d.zone},.5)`);
      g.addColorStop(1, `rgba(${d.zone},0)`);
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(d.x, d.y, r * 2.6, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.beginPath();
    ctx.arc(d.x, d.y, r, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = belong === "ring" ? 2.6 : 1.5;
    ctx.strokeStyle = belong === "ring" ? `rgba(${d.zone},.95)` : "rgba(184,134,59,.7)";
    ctx.stroke();

    if (belong === "flag") {
      ctx.beginPath();
      ctx.arc(d.x + r * 0.9, d.y - r * 0.9, 3.4, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(${d.zone},.95)`;
      ctx.fill();
    }
  }
}

/* ---------- поле зоны ----------
   Метабол по узлам зоны: круги в одном path, координаты гнутся шумом. Форма считается один раз и
   переиспользуется всеми вариантами — сравнивать надо ЗАЛИВКУ, а не разные пятна. */

function zonePath(ctx: CanvasRenderingContext2D, dots: Dot[], zone: string, radius: number): void {
  ctx.beginPath();
  const own = dots.filter((d) => d.zone === zone);
  own.forEach((d, i) => {
    const r = radius * (0.85 + jag(i, 7) * 0.35);
    ctx.moveTo(d.x + r, d.y);
    ctx.arc(d.x, d.y, r, 0, Math.PI * 2);
  });
}

/** Кромка ОБЪЕДИНЕНИЯ: заливка минус её же сжатая копия. Обводить каждый круг нельзя — внутренние
 *  дуги остаются видны, и территория читается как гроздь пузырей, а не как земля (проверено
 *  рендером: первая версия делала именно это). */
function unionOutline(
  ctx: CanvasRenderingContext2D,
  w: number, h: number,
  dots: Dot[], zone: string, radius: number, alpha: number, width = 2.5
): void {
  const cv = document.createElement("canvas");
  cv.width = Math.max(1, Math.round(w));
  cv.height = Math.max(1, Math.round(h));
  const c = cv.getContext("2d");
  if (!c) return;

  zonePath(c, dots, zone, radius);
  c.fillStyle = `rgba(${zone},${alpha})`;
  c.fill();
  c.globalCompositeOperation = "destination-out";
  zonePath(c, dots, zone, radius - width);
  c.fill();

  ctx.drawImage(cv, 0, 0);
}

const R = 46;

type Fill = "soft" | "outline" | "hatch" | "dither" | "stipple" | "full";

function paintZone(ctx: CanvasRenderingContext2D, w: number, h: number, dots: Dot[], zone: string, fill: Fill): void {
  ctx.save();

  if (fill === "outline") {
    unionOutline(ctx, w, h, dots, zone, R, 0.85);
    ctx.restore();
    return;
  }

  zonePath(ctx, dots, zone, R);
  ctx.clip();

  if (fill === "soft" || fill === "full") {
    ctx.fillStyle = `rgba(${zone},.14)`;
    ctx.fillRect(0, 0, w, h);
  } else if (fill === "hatch") {
    ctx.strokeStyle = `rgba(${zone},.4)`;
    ctx.lineWidth = 1.2;
    for (let i = -h; i < w; i += 9) {
      ctx.beginPath();
      ctx.moveTo(i, 0);
      ctx.lineTo(i + h, h);
      ctx.stroke();
    }
  } else if (fill === "dither") {
    // Байер 4×4 — тот же язык, которым карта уже растворяет шторку и туман.
    const bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    ctx.fillStyle = `rgba(${zone},.30)`;   // ядовитая плотность проверена рендером и отвергнута
    for (let y = 0; y < h; y += 3) {
      for (let x = 0; x < w; x += 3) {
        const v = bayer[(y / 3) % 4 * 4 + ((x / 3) % 4)] ?? 0;
        if (v < 6) ctx.fillRect(x, y, 3, 3);
      }
    }
  } else if (fill === "stipple") {
    ctx.fillStyle = `rgba(${zone},.6)`;
    for (let i = 0; i < 900; i++) {
      const x = jag(i, 21) * w;
      const y = jag(i, 22) * h;
      let near = 1e9;
      for (const d of dots) if (d.zone === zone) near = Math.min(near, Math.hypot(d.x - x, d.y - y));
      const k = Math.min(1, Math.max(0, (near - R * 0.35) / (R * 0.7)));   // гуще к краю
      if (jag(i, 23) < k * 0.9) {
        ctx.beginPath();
        ctx.arc(x, y, 1.1, 0, Math.PI * 2);
        ctx.fill();
      }
    }
  }

  ctx.restore();

  if (fill === "full") {
    unionOutline(ctx, w, h, dots, zone, R, 0.6);
  }
}

/** Картуш: имя зоны на плашке. Заливка сама по себе не говорит, ЧТО это за земля. */
function cartouche(
  ctx: CanvasRenderingContext2D, text: string, cx: number, cy: number, zone: string, limit = 0
): void {
  ctx.font = "600 12px ui-monospace, Consolas, monospace";
  const spaced = text.toUpperCase().split("").join(" ");
  const wide = ctx.measureText(spaced).width;
  if (limit > 0) cx = Math.max(wide / 2 + 14, Math.min(limit - wide / 2 - 14, cx));
  ctx.fillStyle = "rgba(26,20,14,.72)";
  ctx.fillRect(cx - wide / 2 - 9, cy - 11, wide + 18, 21);
  ctx.strokeStyle = `rgba(${zone},.75)`;
  ctx.lineWidth = 1.2;
  ctx.strokeRect(cx - wide / 2 - 9, cy - 11, wide + 18, 21);
  ctx.fillStyle = `rgba(${zone},.95)`;
  ctx.fillText(spaced, cx - wide / 2, cy + 4);
}

function zoneStand(fill: Fill, belong: Belong, labels: boolean): DrawFn {
  return (ctx, w, h) => {
    const { dots, pairs } = scene(w, h);
    paintZone(ctx, w, h, dots, GOBLIN, fill);
    paintZone(ctx, w, h, dots, BANDIT, fill);
    roads(ctx, dots, pairs);
    nodes(ctx, dots, belong);
    if (labels) {
      cartouche(ctx, "молниеносные гоблины", w * 0.29, h * 0.13, GOBLIN, w);
      cartouche(ctx, "жадные разбойники", w * 0.72, h * 0.87, BANDIT, w);
    }
  };
}

/* ---------- запланированные эффекты ---------- */

/** Раскрытие зоны при входе: чернила расползаются от узла, за 0.6 с. */
const drawReveal: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const cycle = (tick % 90) / 90;
  const grow = Math.min(1, cycle * 2.2);

  const src = dots[6] ?? dots[0];
  if (src) {
    ctx.save();
    ctx.beginPath();
    ctx.arc(src.x, src.y, 26 + grow * (w * 0.42), 0, Math.PI * 2);
    ctx.clip();
    paintZone(ctx, w, h, dots, BANDIT, "soft");
    unionOutline(ctx, w, h, dots, BANDIT, R, 0.5 * grow);
    ctx.restore();
  }
  paintZone(ctx, w, h, dots, GOBLIN, "soft");
  roads(ctx, dots, pairs);
  nodes(ctx, dots, "ring");

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("вошёл в землю — чернила расходятся от узла", 16, h - 12);
};

/** Подсветка достижимого на один-два шага вперёд — вместо подсветки ряда. */
const drawReach: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const from = 2;                                    // «где стоит отряд»
  const one = new Set<number>();
  const two = new Set<number>();
  for (const [a, b] of pairs) if (a === from) one.add(b);
  for (const [a, b] of pairs) if (one.has(a)) two.add(b);

  paintZone(ctx, w, h, dots, GOBLIN, "soft");
  paintZone(ctx, w, h, dots, BANDIT, "soft");

  ctx.strokeStyle = "rgba(147,128,94,.25)";
  ctx.lineWidth = 1.4;
  for (const [a, b] of pairs) {
    const f = dots[a];
    const t = dots[b];
    if (!f || !t) continue;
    const lit = a === from || (one.has(a) && two.has(b));
    ctx.strokeStyle = lit ? "rgba(255,204,51,.75)" : "rgba(147,128,94,.22)";
    ctx.lineWidth = lit ? 2 : 1.2;
    ctx.beginPath();
    ctx.moveTo(f.x, f.y);
    ctx.lineTo(t.x, t.y);
    ctx.stroke();
  }

  dots.forEach((d, i) => {
    const step = i === from ? 0 : one.has(i) ? 1 : two.has(i) ? 2 : 3;
    const alpha = step === 0 ? 1 : step === 1 ? 0.95 : step === 2 ? 0.6 : 0.25;
    ctx.beginPath();
    ctx.arc(d.x, d.y, i === from ? 11 : 9, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(90,74,52,${alpha})`;
    ctx.fill();
    ctx.lineWidth = 2.4;
    ctx.strokeStyle = `rgba(${d.zone},${alpha})`;
    ctx.stroke();
    if (i === from) {
      ctx.fillStyle = COL.honey;
      ctx.beginPath();
      ctx.arc(d.x, d.y, 4, 0, Math.PI * 2);
      ctx.fill();
    }
  });

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("шаг — ярко, два — приглушённо, дальше — фон", 16, h - 12);
};

/** Счётчик шагов до босса: ответ на вопрос, ради которого хотели подсветку вех. */
const drawSteps: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  paintZone(ctx, w, h, dots, GOBLIN, "soft");
  paintZone(ctx, w, h, dots, BANDIT, "soft");
  roads(ctx, dots, pairs);
  nodes(ctx, dots, "ring");

  // плашка у края листа
  ctx.fillStyle = "rgba(26,20,14,.8)";
  ctx.fillRect(w - 168, 18, 150, 40);
  ctx.strokeStyle = "rgba(184,134,59,.6)";
  ctx.lineWidth = 1.2;
  ctx.strokeRect(w - 168, 18, 150, 40);
  ctx.font = "600 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(232,214,178,.95)";
  ctx.fillText("до босса: 9", w - 154, 44);

  // и он же в тултипе узла
  const d = dots[7];
  if (d) {
    ctx.fillStyle = "rgba(26,20,14,.86)";
    ctx.fillRect(d.x + 14, d.y - 34, 172, 52);
    ctx.strokeStyle = `rgba(${d.zone},.7)`;
    ctx.strokeRect(d.x + 14, d.y - 34, 172, 52);
    ctx.font = "600 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(232,214,178,.95)";
    ctx.fillText("Жадные разбойники", d.x + 24, d.y - 16);
    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.95)";
    ctx.fillText("бой · 7 шагов до босса", d.x + 24, d.y + 2);
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("число, а не подсветка ряда", 16, h - 12);
};

/** Ambient-частицы под зону: воздух местности без единого пятна нового цвета. */
const drawAmbient: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  paintZone(ctx, w, h, dots, GOBLIN, "soft");
  paintZone(ctx, w, h, dots, BANDIT, "soft");
  roads(ctx, dots, pairs);
  nodes(ctx, dots, "ring");

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < 46; i++) {
    const own = i % 2 === 0 ? GOBLIN : BANDIT;
    const host = dots.filter((d) => d.zone === own);
    const anchor = host[i % host.length];
    if (!anchor) continue;
    const ph = (tick * 0.006 + jag(i, 31)) % 1;
    const x = anchor.x + (jag(i, 32) - 0.5) * 90;
    const y = anchor.y + (jag(i, 33) - 0.5) * 80 - ph * 26;
    const a = Math.sin(ph * Math.PI) * 0.5;
    ctx.fillStyle = `rgba(${own},${a.toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(x, y, 1.6, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("споры у гоблинов, угли у разбойников — цвет зоны, не новый", 16, h - 12);
};

/** Грейдинг: карта целиком уходит в тон той земли, где стоит отряд. */
const drawGrading: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const k = 0.5 + 0.5 * Math.sin(tick * 0.03);

  paintZone(ctx, w, h, dots, GOBLIN, "soft");
  paintZone(ctx, w, h, dots, BANDIT, "soft");
  roads(ctx, dots, pairs);
  nodes(ctx, dots, "ring");

  ctx.save();
  ctx.globalCompositeOperation = "overlay";
  ctx.fillStyle = `rgba(${BANDIT},${(0.05 + k * 0.10).toFixed(3)})`;
  ctx.fillRect(0, 0, w, h);
  ctx.restore();

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("весь лист чуть уходит в тон земли под ногами", 16, h - 12);
};

/* ---------- стенды ---------- */

const SIZE: [number, number] = [320, 260];
const WIDE: [number, number] = [620, 330];

const FILLS: StandDef[] = [
  {
    id: "fill-soft",
    status: "waiting",
    title: "Мягкая заливка",
    note: "Метабол по узлам зоны, 14% непрозрачности. Самый спокойный вариант: пятно есть, но глаз за него не цепляется.",
    size: SIZE,
    draw: zoneStand("soft", "ring", false)
  },
  {
    id: "fill-outline",
    status: "waiting",
    title: "Только контур",
    note: "Заливки нет вовсе, есть рваная кромка. Карта остаётся чистой, но принадлежность читается лишь вблизи границы.",
    size: SIZE,
    draw: zoneStand("outline", "ring", false)
  },
  {
    id: "fill-hatch",
    status: "waiting",
    title: "Штриховка",
    note: "Диагональные линии цветом зоны — приём старых карт. Хорошо переносит наложение: две штриховки под разным углом не сливаются в кашу, в отличие от двух заливок.",
    size: SIZE,
    draw: zoneStand("hatch", "ring", false)
  },
  {
    id: "fill-dither",
    status: "waiting",
    title: "Дизеринг",
    note: "Байер 4×4 — тот же язык, которым карта уже растворяет шторку перехода и туман. Держит стиль лучше всех остальных.",
    size: SIZE,
    draw: zoneStand("dither", "ring", false)
  },
  {
    id: "fill-stipple",
    status: "waiting",
    title: "Точечная отсыпка",
    note: "Точки, густеющие к краю владения. Классика бумажных карт: центр земли чист, а граница видна плотностью.",
    size: SIZE,
    draw: zoneStand("stipple", "ring", false)
  },
  {
    id: "fill-full",
    status: "waiting",
    title: "Заливка, кромка и картуш",
    tag: "полный набор",
    note: "Мягкая заливка + рваная кромка + имя зоны на плашке. Имя обязательно: заливка сама по себе говорит «чья-то земля», но не говорит чья.",
    verdict: "Мой фаворит для игры; штриховка и дизеринг — сильные альтернативы, если пятна начнут спорить.",
    size: WIDE,
    draw: zoneStand("full", "ring", true)
  }
];

const BELONG: StandDef[] = [
  {
    id: "belong-ring",
    status: "waiting",
    title: "Ободок узла",
    note: "Цвет зоны идёт по кромке узла. Виден при любом зуме, не мешает иконке типа внутри.",
    size: SIZE,
    draw: zoneStand("soft", "ring", false)
  },
  {
    id: "belong-flag",
    status: "waiting",
    title: "Точка-флажок",
    note: "Маленькая метка сбоку. Меньше шума, но на общем зуме теряется первой.",
    size: SIZE,
    draw: zoneStand("soft", "flag", false)
  },
  {
    id: "belong-halo",
    status: "waiting",
    title: "Свечение под узлом",
    note: "Мягкое гало цвета зоны. Красиво, но спорит с морганием доступных узлов — два свечения на одном объекте.",
    size: SIZE,
    draw: zoneStand("soft", "halo", false)
  }
];

const PLANNED: StandDef[] = [
  {
    id: "reveal",
    status: "waiting",
    title: "Раскрытие зоны",
    note: "Вошёл в чужую землю — заливка расползается чернилами от узла. Событие, которое объясняет, почему цвет вдруг появился.",
    size: WIDE,
    draw: drawReveal
  },
  {
    id: "reach",
    status: "waiting",
    title: "Достижимое на два шага",
    note: "Вместо подсветки ряда: шаг — ярко, два — приглушённо, дальше — фон. Отвечает на вопрос, который игрок реально задаёт: «куда я могу пойти».",
    size: WIDE,
    draw: drawReach
  },
  {
    id: "steps",
    status: "waiting",
    title: "Счётчик до босса",
    note: "Число на плашке и в тултипе узла. Это и был настоящий вопрос за просьбой «подсвечивать вехи».",
    size: WIDE,
    draw: drawSteps
  },
  {
    id: "ambient",
    status: "waiting",
    title: "Частицы зоны",
    note: "Споры, угли, пыль — цветом самой зоны. Новых оттенков не заводим: иначе воздух начнёт спорить с принадлежностью.",
    size: WIDE,
    draw: drawAmbient
  },
  {
    id: "grading",
    status: "waiting",
    title: "Грейдинг под землю",
    note: "Весь лист чуть уходит в тон той земли, где стоит отряд. Самый дешёвый способ сказать «ты в чужих краях» без единого нового объекта.",
    size: WIDE,
    draw: drawGrading
  }
];

const section: SectionDef = {
  id: "map-zones",
  title: "Зоны влияния",
  eyebrow: "Карта акта",
  lede:
    "Местность не рисуем: карта остаётся картой узлов и дорог. Единственный территориальный слой — " +
    "зона влияния фракции. Здесь варианты того, чем её рисовать, и запланированные эффекты живьём.",
  blocks: [
    {
      kind: "note",
      html:
        "<b>Решение Макса 2026-08-02:</b> местность не рисуем — «может это и не надо». Рельеф остаётся " +
        "механикой (форма области решает, как ходят дороги), но собственной картинки не имеет: его " +
        "видно по рисунку графа. Атласная земля и летающие острова — <b>отложены</b>, лежат в разделе " +
        "«Земля и страна» с пометкой."
    },
    {
      kind: "head",
      id: "fills",
      title: "Чем рисовать зону",
      lede: "Одна и та же сцена, шесть способов показать «это чья-то земля»."
    },
    { kind: "stands", items: FILLS.slice(0, 5) },
    { kind: "split", items: [FILLS[5] as StandDef] },
    {
      kind: "note",
      html:
        "<b>Про наложение:</b> зоны лежат поперёк областей и будут перекрываться. Две заливки в " +
        "перекрытии дают третий, ложный цвет; две штриховки под разным углом и два дизеринга — нет. " +
        "Это главный довод против самой красивой мягкой заливки, и проверять его надо на живой карте " +
        "с тремя фракциями."
    },
    {
      kind: "head",
      id: "belong",
      title: "Как узел говорит о принадлежности",
      lede: "Пятно — атмосфера, правду носит узел. Три способа её носить."
    },
    { kind: "stands", items: BELONG },
    {
      kind: "head",
      id: "planned",
      title: "Запланированное — живьём",
      lede: "То, что до сих пор было строкой в таблице."
    },
    { kind: "split", items: PLANNED },
    {
      kind: "table",
      head: ["Не нарисовано здесь", "Где смотреть"],
      rows: [
        ["Grand Line и три ступени наград", "раздел «Формы областей»"],
        ["Дыра подземелья и привратник", "раздел «Подача»"],
        ["Виды дорог линией", "раздел «Формы областей»"],
        ["Лист, стол, шторка, моргание, волна", "раздел «Подача» — они уже в игре"],
        ["Звук карты", "нечего рисовать: шелест бумаги и перо"]
      ]
    }
  ]
};

export default section;
