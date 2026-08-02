/* Карта акта: формы областей.

   Модель, которую показывает раздел: карта делится не на этажи с разной шириной, а на ОБЛАСТИ —
   куски местности со своей внутренней топологией. Область = форма (как ходят рёбра внутри)
   × свойства (что про неё известно и что она делает). Зоны фракций ложатся третьим слоем и
   режут области наискось.

   Два ограничения Макса, из которых всё остальное следует:
   длина у всех маршрутов ОДНА (шаг = этаж, никто никого не обгоняет), а ширина свободна везде,
   кроме вех-застав. Поэтому форма может менять только связность и группировку, но не длину.

   Рёбра внутри областей строит порт `ladder` — тот же алгоритм монотонной лестницы, что и в
   игре (MapGenerator.ConnectColumns), только `rng` заменён на `jag`. Копия нужна затем, чтобы
   стенд показывал НАШУ карту, а не похожую на неё картинку. */

import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- геометрия схемы ---------- */

/** Ребро внутри области: из колонки `col`, ряда `from` в ряд `to` следующей колонки. */
type Edge = [col: number, from: number, to: number];

interface Region {
  /** Ширина каждой колонки области, слева направо. */
  cols: number[];
  edges: Edge[];
  /** Явные позиции рядов по Y в долях [-1..1] на колонку. Пусто — ряд стоит ровно по центру. */
  ys?: Array<number[] | null>;
  /** Узлы, нарисованные закрытыми: содержимое неизвестно до входа. */
  hidden?: boolean;
  /** Ряд узла-хозяина в последней колонке: он крупнее и светится. */
  owner?: number;
}

const NODE_R = 6.5;
const PAD_X = 40;
const PAD_Y = 40;

/** Монотонная лестница между соседними колонками — порт `MapGenerator.ConnectColumns`.
 *  Она и даёт планарность: указатели идут вдоль диагонали, поэтому рёбра не пересекаются. */
function ladder(ws: number, wt: number, salt: number, maxEdges = 4): Array<[number, number]> {
  const out: Array<[number, number]> = [];
  const fanned = new Array<number>(ws).fill(0);
  const merged = new Array<number>(wt).fill(0);
  let si = 0;
  let ti = 0;
  let step = 0;

  for (let guard = 0; guard < 64; guard++) {
    out.push([si, ti]);
    fanned[si] = (fanned[si] ?? 0) + 1;
    merged[ti] = (merged[ti] ?? 0) + 1;
    if (si === ws - 1 && ti === wt - 1) break;
    if (si === ws - 1) { ti++; continue; }
    if (ti === wt - 1) { si++; continue; }

    const s = si / (ws - 1);
    const t = ti / (wt - 1);
    const canFan = (fanned[si] ?? 0) < maxEdges;
    const canMerge = (merged[ti] ?? 0) < maxEdges;

    if (s < t - 1e-4 || !canFan) si++;
    else if (s > t + 1e-4 || !canMerge) ti++;
    else {
      const roll = Math.floor(jag(step++, salt) * 3);
      if (roll === 0) si++;
      else if (roll === 1) ti++;
      else { si++; ti++; }
    }
  }
  return out;
}

/** Ширина колонки: индексный доступ строг, а формы объявляются литералами — дефолт держит компилятор. */
function widthAt(cols: number[], col: number): number {
  return cols[col] ?? 1;
}

/** Область, сшитая лестницами: обычный случай, из него собираются поле, остров, логово. */
function woven(cols: number[], salt: number, maxEdges = 4): Edge[] {
  const edges: Edge[] = [];
  for (let c = 0; c < cols.length - 1; c++)
    for (const [from, to] of ladder(widthAt(cols, c), widthAt(cols, c + 1), salt + c, maxEdges))
      edges.push([c, from, to]);
  return edges;
}

/** Полосы, которые не сообщаются: ряд идёт прямо и никуда не сворачивает. */
function lanes(cols: number[]): Edge[] {
  const edges: Edge[] = [];
  for (let c = 0; c < cols.length - 1; c++)
    for (let r = 0; r < Math.min(widthAt(cols, c), widthAt(cols, c + 1)); r++) edges.push([c, r, r]);
  return edges;
}

/** Половины области, сшитые по отдельности: между ними рёбер нет вовсе. */
function split(cols: number[], salt: number): Edge[] {
  const edges: Edge[] = [];
  for (let c = 0; c < cols.length - 1; c++) {
    const ws = widthAt(cols, c);
    const wt = widthAt(cols, c + 1);
    const halfA = Math.ceil(ws / 2);
    const halfB = Math.ceil(wt / 2);
    for (const [from, to] of ladder(halfA, halfB, salt + c, 3)) edges.push([c, from, to]);
    for (const [from, to] of ladder(ws - halfA, wt - halfB, salt + 20 + c, 3))
      edges.push([c, from + halfA, to + halfB]);
  }
  return edges;
}

/** Снос вбок: каждый шаг обязан смещать ряд на единицу вниз, вернуться назад нельзя. */
function drift(cols: number[]): Edge[] {
  const edges: Edge[] = [];
  for (let c = 0; c < cols.length - 1; c++)
    for (let r = 0; r < widthAt(cols, c); r++) {
      const to = Math.min(r + 1, widthAt(cols, c + 1) - 1);
      edges.push([c, r, to]);
    }
  return edges;
}

/* ---------- рисование ---------- */

interface Layout {
  x: (col: number) => number;
  y: (col: number, row: number) => number;
}

function layoutOf(region: Region, w: number, h: number): Layout {
  const widest = Math.max(...region.cols);
  const stepX = region.cols.length > 1 ? (w - PAD_X * 2) / (region.cols.length - 1) : 0;
  const spanY = h - PAD_Y * 2;
  const stepY = widest > 1 ? Math.min(30, spanY / (widest - 1)) : 0;

  return {
    x: (col) => PAD_X + col * stepX,
    y: (col, row) => {
      const explicit = region.ys?.[col];
      const share = explicit?.[row];
      if (share !== undefined) return h / 2 + share * (spanY / 2);
      return h / 2 + (row - (widthAt(region.cols, col) - 1) / 2) * stepY;
    }
  };
}

function drawRegion(ctx: CanvasRenderingContext2D, w: number, h: number, region: Region): void {
  const at = layoutOf(region, w, h);
  const lastCol = region.cols.length - 1;

  // Дороги — под узлами: узел всегда читается поверх линии, а не наоборот.
  ctx.strokeStyle = "rgba(147,128,94,.55)";
  ctx.lineWidth = 1.4;
  for (const [col, from, to] of region.edges) {
    ctx.beginPath();
    ctx.moveTo(at.x(col) + NODE_R + 2, at.y(col, from));
    ctx.lineTo(at.x(col + 1) - NODE_R - 2, at.y(col + 1, to));
    ctx.stroke();
  }

  for (let col = 0; col <= lastCol; col++) {
    for (let row = 0; row < widthAt(region.cols, col); row++) {
      const x = at.x(col);
      const y = at.y(col, row);
      const isPort = col === 0 || col === lastCol;
      const isOwner = col === lastCol && region.owner === row;
      const r = isOwner ? NODE_R * 1.6 : NODE_R;

      ctx.beginPath();
      ctx.arc(x, y, r, 0, Math.PI * 2);
      ctx.fillStyle = isOwner ? "rgba(255,204,51,.28)" : region.hidden ? "rgba(58,44,30,.85)" : COL.body;
      ctx.fill();
      ctx.lineWidth = isOwner ? 2 : 1.6;
      ctx.strokeStyle = isOwner ? COL.honey : isPort ? "rgba(255,204,51,.8)" : "rgba(184,134,59,.75)";
      ctx.stroke();

      if (region.hidden && !isPort) {
        ctx.font = "600 10px ui-monospace, Consolas, monospace";
        ctx.fillStyle = "rgba(147,128,94,.9)";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText("?", x, y + 0.5);
        ctx.textAlign = "left";
        ctx.textBaseline = "alphabetic";
      }
    }
  }
}

/** Подписи входа и выхода: сколько дорог область принимает и сколько отдаёт. */
function ports(ctx: CanvasRenderingContext2D, w: number, h: number, inN: string, outN: string): void {
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.textAlign = "left";
  ctx.fillText(`вход ${inN}`, 12, h - 14);
  ctx.textAlign = "right";
  ctx.fillText(`выход ${outN}`, w - 12, h - 14);
  ctx.textAlign = "left";
}

function shape(region: Region, inN: string, outN: string): DrawFn {
  return (ctx, w, h) => {
    drawRegion(ctx, w, h, region);
    ports(ctx, w, h, inN, outN);
  };
}

/* ---------- акт в сборе ---------- */

/** Область на карте акта: полоса рядов на своих этажах. Схема, а не раскладка движка. */
interface Placed {
  label: string;
  col: number;      // левый этаж
  span: number;     // сколько этажей занимает
  lane: number;     // полоса по вертикали: -1 верх, 0 центр, 1 низ
  height: number;   // высота полосы в долях
}

const ACT: Placed[] = [
  { label: "веер", col: 0, span: 1, lane: 0, height: 0.9 },
  { label: "поле", col: 1, span: 2, lane: -0.55, height: 0.8 },
  { label: "тропа", col: 1, span: 2, lane: 0.62, height: 0.34 },
  { label: "остров", col: 3, span: 2, lane: -0.5, height: 0.7 },
  { label: "гребёнка", col: 3, span: 2, lane: 0.55, height: 0.6 },
  { label: "застава", col: 5, span: 1, lane: 0, height: 0.3 },
  { label: "поле", col: 6, span: 2, lane: -0.45, height: 0.85 },
  { label: "подземелье", col: 6, span: 2, lane: 0.62, height: 0.45 }
];

/** Зона фракции: пятно, выращенное вокруг точек. Круги в ОДНОМ path — заливка объединяет их без
 *  внутренних швов, и это самая дешёвая замена метаболу на канвасе. */
function blob(
  ctx: CanvasRenderingContext2D,
  pts: Array<[number, number]>,
  radius: number,
  color: string,
  salt: number
): void {
  ctx.save();
  ctx.beginPath();
  pts.forEach(([x, y], i) => {
    const r = radius * (0.78 + jag(i, salt) * 0.5);
    ctx.moveTo(x + r, y);
    ctx.arc(x, y, r, 0, Math.PI * 2);
  });
  ctx.fillStyle = color;
  ctx.fill();
  ctx.restore();
}

const drawAct: DrawFn = (ctx, w, h) => {
  const left = 26;
  const stepX = (w - left * 2) / 8;
  const midY = h / 2 - 6;
  const spanY = h * 0.72;

  // Зоны — ПОД областями: пятно это местность, узлы стоят на ней.
  blob(ctx, [
    [left + stepX * 1.2, midY - spanY * 0.32],
    [left + stepX * 2.1, midY - spanY * 0.12],
    [left + stepX * 2.8, midY + spanY * 0.1],
    [left + stepX * 3.6, midY + spanY * 0.3],
    [left + stepX * 4.2, midY + spanY * 0.42]
  ], 44, "rgba(132,214,92,.10)", 3);

  blob(ctx, [
    [left + stepX * 4.6, midY - spanY * 0.42],
    [left + stepX * 5.4, midY - spanY * 0.2],
    [left + stepX * 6.2, midY + spanY * 0.02],
    [left + stepX * 7.0, midY + spanY * 0.26]
  ], 46, "rgba(255,96,80,.10)", 7);

  for (const area of ACT) {
    const x0 = left + area.col * stepX;
    const x1 = left + (area.col + area.span) * stepX;
    const cy = midY + area.lane * spanY * 0.5;
    const hh = spanY * area.height * 0.5;

    ctx.strokeStyle = "rgba(184,134,59,.35)";
    ctx.setLineDash([4, 4]);
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.roundRect(x0 - 14, cy - hh, x1 - x0 + 28, hh * 2, 10);
    ctx.stroke();
    ctx.setLineDash([]);

    ctx.font = "500 10px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.95)";
    ctx.textAlign = "center";
    ctx.fillText(area.label, (x0 + x1) / 2, cy - hh - 5);
    ctx.textAlign = "left";

    // Узлы внутри области — намёк на содержимое, не точная раскладка.
    const rows = area.label === "тропа" || area.label === "застава" ? 2 : 3;
    for (let c = 0; c <= area.span; c++) {
      for (let r = 0; r < rows; r++) {
        const x = x0 + (c * (x1 - x0)) / Math.max(1, area.span);
        const y = cy + (r - (rows - 1) / 2) * Math.min(22, (hh * 2) / rows);
        ctx.beginPath();
        ctx.arc(x, y, 3.4, 0, Math.PI * 2);
        ctx.fillStyle = area.label === "подземелье" ? "rgba(58,44,30,.9)" : COL.body;
        ctx.fill();
        ctx.strokeStyle = "rgba(184,134,59,.6)";
        ctx.lineWidth = 1;
        ctx.stroke();
      }
    }
  }

  ctx.font = "500 10px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(132,214,92,.85)";
  ctx.fillText("зона: гоблины", left, h - 12);
  ctx.fillStyle = "rgba(255,96,80,.85)";
  ctx.fillText("зона: разбойники", left + 120, h - 12);
};

/** Правда живёт на узле, пятно — атмосфера: узел носит метку своей зоны ободком. */
const drawMark: DrawFn = (ctx, w, h) => {
  const cx = w / 2;
  const cy = h / 2 - 6;

  blob(ctx, [[cx - 40, cy - 10], [cx + 10, cy + 12], [cx + 46, cy - 6]], 46, "rgba(132,214,92,.12)", 5);
  blob(ctx, [[cx + 74, cy - 30], [cx + 96, cy + 20]], 44, "rgba(255,96,80,.12)", 9);

  const nodes: Array<[number, number, string]> = [
    [cx - 52, cy - 16, "132,214,92"],
    [cx - 6, cy + 18, "132,214,92"],
    [cx + 40, cy - 2, "132,214,92"],
    [cx + 84, cy - 22, "255,96,80"],
    [cx + 92, cy + 22, "255,96,80"]
  ];

  ctx.strokeStyle = "rgba(147,128,94,.5)";
  ctx.lineWidth = 1.4;
  for (let i = 0; i + 1 < nodes.length; i++) {
    const from = nodes[i];
    const to = nodes[i + 1];
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from[0] + 12, from[1]);
    ctx.lineTo(to[0] - 12, to[1]);
    ctx.stroke();
  }

  for (const [x, y, tint] of nodes) {
    ctx.beginPath();
    ctx.arc(x, y, 10, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.strokeStyle = `rgba(${tint},.95)`;   // ободок — принадлежность зоне
    ctx.lineWidth = 2.2;
    ctx.stroke();
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("ободок = чья земля · пятно может врать, узел нет", 20, h - 14);
};

/* ---------- дороги ----------
   Ребро карты сейчас пустое: линия между узлами без свойств и без цены. А выбор игрок делает именно
   на дороге. Стенд показывает, как ранг и свойство дороги читаются САМОЙ ЛИНИЕЙ: значок на ребре
   превратил бы карту в приборную панель, а линия работает боковым зрением. */

interface RoadKind {
  label: string;
  /** Рисует дорогу от (x0,y) до (x1,y); подпись каркас ставит сам. */
  paint: (ctx: CanvasRenderingContext2D, x0: number, x1: number, y: number) => void;
}

function node(ctx: CanvasRenderingContext2D, x: number, y: number, dim = false): void {
  ctx.beginPath();
  ctx.arc(x, y, 6, 0, Math.PI * 2);
  ctx.fillStyle = dim ? "rgba(58,44,30,.85)" : COL.body;
  ctx.fill();
  ctx.lineWidth = 1.5;
  ctx.strokeStyle = "rgba(184,134,59,.75)";
  ctx.stroke();
}

const ROADS: RoadKind[] = [
  {
    label: "тракт — безопасно и бедно",
    paint: (ctx, x0, x1, y) => {
      ctx.strokeStyle = "rgba(184,134,59,.75)";
      ctx.lineWidth = 4.5;
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
    }
  },
  {
    label: "тропа — опасно и щедро",
    paint: (ctx, x0, x1, y) => {
      ctx.strokeStyle = "rgba(147,128,94,.75)";
      ctx.lineWidth = 1.2;
      ctx.setLineDash([3, 4]);
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
      ctx.setLineDash([]);
    }
  },
  {
    label: "привратник — плата объявлена",
    paint: (ctx, x0, x1, y) => {
      ctx.strokeStyle = "rgba(184,134,59,.7)";
      ctx.lineWidth = 2.2;
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
      const mid = (x0 + x1) / 2;
      ctx.fillStyle = "rgba(255,204,51,.9)";
      ctx.beginPath();
      ctx.arc(mid, y, 5, 0, Math.PI * 2);
      ctx.fill();
    }
  },
  {
    label: "заслон — пройти можно с боем",
    paint: (ctx, x0, x1, y) => {
      ctx.strokeStyle = "rgba(184,134,59,.7)";
      ctx.lineWidth = 2.2;
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
      const mid = (x0 + x1) / 2;
      ctx.strokeStyle = "rgba(255,96,80,.95)";
      ctx.lineWidth = 2.4;
      ctx.beginPath();
      ctx.moveTo(mid - 5, y - 6);
      ctx.lineTo(mid + 5, y + 6);
      ctx.moveTo(mid + 5, y - 6);
      ctx.lineTo(mid - 5, y + 6);
      ctx.stroke();
    }
  },
  {
    label: "вслепую — куда ведёт, неизвестно",
    paint: (ctx, x0, x1, y) => {
      const grad = ctx.createLinearGradient(x0, y, x1, y);
      grad.addColorStop(0, "rgba(184,134,59,.75)");
      grad.addColorStop(1, "rgba(184,134,59,0)");
      ctx.strokeStyle = grad;
      ctx.lineWidth = 2.2;
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
    }
  },
  {
    label: "режущая — закроет часть карты",
    paint: (ctx, x0, x1, y) => {
      ctx.strokeStyle = "rgba(184,134,59,.75)";
      ctx.lineWidth = 2.2;
      ctx.beginPath();
      ctx.moveTo(x0, y);
      ctx.lineTo(x1, y);
      ctx.stroke();
      // Пунктиром — то, что после этого шага станет недоступным.
      ctx.strokeStyle = "rgba(255,96,80,.4)";
      ctx.lineWidth = 1.2;
      ctx.setLineDash([2, 3]);
      ctx.beginPath();
      ctx.moveTo(x1, y);
      ctx.lineTo(x1 + 26, y - 16);
      ctx.moveTo(x1, y);
      ctx.lineTo(x1 + 26, y + 16);
      ctx.stroke();
      ctx.setLineDash([]);
    }
  }
];

const drawRoads: DrawFn = (ctx, w, h) => {
  const x0 = 46;
  const x1 = w - 150;
  const top = 34;
  const step = (h - top - 24) / ROADS.length;

  ROADS.forEach((road, i) => {
    const y = top + step * (i + 0.5);
    road.paint(ctx, x0, x1, y);
    node(ctx, x0, y);
    node(ctx, x1, y, road.label.startsWith("вслепую"));

    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.textBaseline = "middle";
    ctx.fillText(road.label, x1 + 34, y);
    ctx.textBaseline = "alphabetic";
  });
};

/* ---------- великий путь ----------
   Одна сквозная дорога через весь акт, прочерченная заранее. Идёшь по ней — капают награды тремя
   ступенями; свернул — счётчик встаёт. Это первая идея, где содержанием выбора становится САМА
   линия, а не то, что лежит в узлах. */

const drawGrandLine: DrawFn = (ctx, w, h) => {
  const cols = 9;
  const left = 40;
  const stepX = (w - left * 2) / (cols - 1);
  const midY = h / 2 - 4;
  const spanY = h * 0.34;

  // Ряд узлов на каждом этаже: три сверху вниз, кроме талии-костра.
  const rowsOf = (c: number): number => (c === 4 ? 1 : 3);
  const yOf = (c: number, r: number): number => midY + (r - (rowsOf(c) - 1) / 2) * spanY;

  // Сам путь: ряд на каждом этаже, детерминированно.
  const path: number[] = [];
  for (let c = 0; c < cols; c++) path.push(c === 4 ? 0 : Math.floor(jag(c, 17) * 3));

  ctx.strokeStyle = "rgba(147,128,94,.35)";
  ctx.lineWidth = 1;
  for (let c = 0; c + 1 < cols; c++)
    for (let a = 0; a < rowsOf(c); a++)
      for (let b = 0; b < rowsOf(c + 1); b++) {
        if (Math.abs(a - b) > 1 && rowsOf(c) === rowsOf(c + 1)) continue;
        ctx.beginPath();
        ctx.moveTo(left + c * stepX + 7, yOf(c, a));
        ctx.lineTo(left + (c + 1) * stepX - 7, yOf(c + 1, b));
        ctx.stroke();
      }

  // Великий путь поверх обычных дорог — толще, теплее, с лёгким свечением.
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.strokeStyle = "rgba(255,204,51,.55)";
  ctx.lineWidth = 4.5;
  ctx.lineJoin = "round";
  ctx.beginPath();
  for (let c = 0; c < cols; c++) {
    const x = left + c * stepX;
    const y = yOf(c, path[c] ?? 0);
    if (c === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();
  ctx.restore();

  for (let c = 0; c < cols; c++) {
    for (let r = 0; r < rowsOf(c); r++) {
      const on = (path[c] ?? 0) === r;
      ctx.beginPath();
      ctx.arc(left + c * stepX, yOf(c, r), on ? 7 : 5.5, 0, Math.PI * 2);
      ctx.fillStyle = on ? "rgba(255,204,51,.3)" : COL.body;
      ctx.fill();
      ctx.lineWidth = on ? 2 : 1.4;
      ctx.strokeStyle = on ? COL.honey : "rgba(184,134,59,.6)";
      ctx.stroke();
    }
  }

  // Три ступени награды: где именно они капают.
  const marks: Array<[number, string]> = [[3, "1 · реликвия"], [5, "2 · предмет"], [8, "3 · реликвия элиты"]];
  ctx.font = "500 10px ui-monospace, Consolas, monospace";
  for (const [c, label] of marks) {
    const x = left + c * stepX;
    const y = yOf(c, path[c] ?? 0);
    ctx.strokeStyle = "rgba(255,204,51,.5)";
    ctx.lineWidth = 1;
    ctx.setLineDash([2, 3]);
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, h - 32);
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.fillStyle = "rgba(255,204,51,.9)";
    ctx.textAlign = "center";
    ctx.fillText(label, x, h - 18);
    ctx.textAlign = "left";
  }

  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("костёр в талии не считается", left, 22);
};

/* ---------- стенды ---------- */

const FORMS: StandDef[] = [
  {
    id: "field",
    status: "accepted",
    title: "Поле",
    tag: "база",
    note: "Много входов, много выходов, густая связность. Место, где выбор переигрывается на каждом шагу.",
    facts: [["порты", "много → много"], ["этажей", "2–3"], ["связность", "до 4 исходов"]],
    verdict: "Опора карты: без поля остальные формы читаются как коридоры.",
    size: [320, 250],
    draw: shape({ cols: [3, 4, 4, 3], edges: woven([3, 4, 4, 3], 11) }, "много", "много")
  },
  {
    id: "isle",
    status: "accepted",
    title: "Остров",
    tag: "база",
    note: "Один вход и один выход, внутри — своя жизнь. Зашёл — идёшь насквозь, вся остальная карта на эти этажи недоступна.",
    facts: [["порты", "1 → 1"], ["этажей", "2–4"], ["цена", "коммит на всю длину"]],
    verdict: "Главный носитель свойств: сокрытость, гниль, владение вешаются именно сюда.",
    size: [320, 250],
    draw: shape({ cols: [1, 3, 3, 1], edges: woven([1, 3, 3, 1], 23) }, "1", "1")
  },
  {
    id: "trail",
    status: "accepted",
    title: "Тропа",
    tag: "база",
    note: "Ширина <b>один</b>, длина от одного узла до четырёх. Внутри не происходит ничего: зашёл — прошёл. Её ценность в том, что она скучная и предсказуемая, а выбор остаётся снаружи — рядом всегда лежит другая область.",
    facts: [["порты", "1 → 1"], ["ширина", "1"], ["этажей", "1–4"], ["мост", "тропа длиной 1"]],
    verdict: "Уточнено Максом 2026-08-02: именно единица. Двойка была моей перестраховкой — выбор обязан жить между областями, а не внутри тропы.",
    size: [320, 250],
    draw: shape({ cols: [1, 1, 1, 1], edges: lanes([1, 1, 1, 1]) }, "1", "1")
  },
  {
    id: "lanes",
    status: "accepted",
    title: "Гребёнка",
    note: "Несколько троп бок о бок, которые <b>не сообщаются</b>. Выбор делается один раз на входе и не переигрывается: свернуть на соседнюю дорогу нельзя.",
    facts: [["порты", "n → n"], ["этажей", "2–3"], ["суть", "коммит без права передумать"]],
    verdict: "Отличается от поля именно запретом перестроиться — там выбор каждый шаг, здесь один.",
    size: [320, 250],
    draw: shape({ cols: [4, 4, 4, 4], edges: lanes([4, 4, 4, 4]) }, "n", "n")
  },
  {
    id: "forks",
    status: "accepted",
    title: "Рукава",
    note: "Карта распадается на две половины, между ними рёбер нет вовсе; внутри каждой — своё ветвление. Не «выбери дорогу», а «выбери половину акта».",
    facts: [["порты", "1 → 2"], ["этажей", "3–4"], ["внутри", "мини-поле в каждой половине"]],
    verdict: "Самая сильная развилка из всех; на трёх этажах уже почти делит акт надвое.",
    size: [320, 250],
    draw: shape({ cols: [2, 4, 4, 4], edges: split([2, 4, 4, 4], 41) }, "1–2", "2")
  },
  {
    id: "muster",
    status: "accepted",
    title: "Сход",
    tag: "объединил перекрёсток и заставу",
    note: "Все дороги сходятся в один-два узла и снова расходятся. Топологически это «сброс маршрута»: до схода выбор был локальным, после — открыта вся ширина. <b>Застава</b> — тот же сход, которому якорь назначил гарантированный тип (привал, сундуки).",
    facts: [["порты", "всё → 1–2 → всё"], ["этажей", "1"], ["сейчас в игре", "этажи 7 и 13"]],
    verdict: "Отдельной формы «застава» не завожу: разница в содержимом, а не в топологии.",
    size: [320, 250],
    draw: shape({ cols: [4, 1, 4], edges: woven([4, 1, 4], 53) }, "много", "много")
  },
  {
    id: "shoals",
    status: "rejected",
    title: "Архипелаг",
    note: "Кучки по два-три узла с пустотой между ними. Задумывался как форма, но связность у него ровно та же, что у поля или гребёнки — отличается только расстановка.",
    facts: [["порты", "много → много"], ["ось", "раскладка, а не топология"]],
    verdict: "Отклонён как форма 2026-08-02: это не форма, а опция расстановки («гроздья») у поля. Дубль по той же причине, по какой ушёл мост.",
    size: [320, 250],
    draw: shape(
      {
        cols: [4, 4, 4],
        edges: lanes([4, 4, 4]).concat([[0, 1, 2], [1, 2, 1]] as Edge[]),
        ys: [[-0.85, -0.6, 0.55, 0.8], [-0.8, -0.55, 0.6, 0.85], [-0.85, -0.6, 0.55, 0.8]]
      },
      "много", "много"
    )
  },
  {
    id: "race",
    status: "accepted",
    title: "Лестница",
    tag: "имя рабочее",
    note: "Каждый шаг обязан смещать вбок в одну сторону — вернуться к центру нельзя. Куда бы ты ни вошёл, тебя сносит к одному краю карты.<br>Имя спорное: «монотонной лестницей» в коде уже зовётся алгоритм связывания колонок. Кандидаты: <b>Осыпь</b>, <b>Каскад</b>, <b>Спуск</b>.",
    facts: [["порты", "n → край"], ["этажей", "2–3"], ["образ", "река, осыпь, склон"]],
    verdict: "Максу интересна. Польза — предсказуемый снос: «хочешь оказаться внизу карты, иди сюда».",
    size: [320, 250],
    draw: shape({ cols: [4, 4, 4, 4], edges: drift([4, 4, 4, 4]) }, "n", "край")
  },
  {
    id: "lair",
    status: "accepted",
    title: "Логово",
    note: "Область, все дороги которой сходятся к узлу-хозяину на выходе. Мини-акт: идёшь по землям фракции и в конце встречаешь её вожака.",
    facts: [["порты", "1–2 → 1"], ["этажей", "2–3"], ["выход", "элита-владелец"]],
    verdict: "Отличается от схода тем, что принадлежит фракции: сход нейтрален, логово — чьё-то.",
    size: [320, 250],
    draw: shape({ cols: [2, 3, 3, 1], edges: woven([2, 3, 3, 1], 67), owner: 0 }, "1–2", "1")
  },
  {
    id: "bridge",
    status: "rejected",
    title: "Мост",
    note: "Узкая связка между двумя большими областями: один-два узла шириной, один этаж.",
    facts: [["порты", "1 → 1"], ["этажей", "1–2"]],
    verdict: "Отклонено Максом 2026-08-02: это тропа длиной в этаж. Отдельной сущности не заслуживает — связку делает короткая тропа.",
    size: [320, 250],
    draw: shape({ cols: [1, 1, 1], edges: lanes([1, 1, 1]) }, "1", "1")
  }
];

const DELVES: StandDef[] = [
  {
    id: "delve",
    status: "accepted",
    title: "Структура подземелья",
    tag: "вход · веха · веха · выход",
    note: "Длина статичная (Макс, 2026-08-02). Подземелье — <b>разновидность ивента</b>: первая веха обычно текстовый выбор, который настраивает вторую, вторая — испытание, к которому этот выбор готовил. У входа стоит <b>странный старик</b>: пропускает за плату, и берётся не только плохое — иногда за цену дают шанс.",
    facts: [["длина", "фиксирована, вариаций нет"], ["веха 1", "ивент, чаще всего"], ["веха 2", "сложное испытание"], ["вход", "плата привратнику"]],
    verdict: "Ивент, который настраивает бой, — то, чего у нас нет нигде: сейчас текстовое событие и бой не знают друг о друге.",
    size: [620, 330],
    draw: (ctx, w, h) => {
      const y = h / 2 - 8;
      const xs = [w * 0.16, w * 0.38, w * 0.62, w * 0.84];
      ctx.strokeStyle = "rgba(147,128,94,.55)";
      ctx.lineWidth = 1.6;
      for (let i = 0; i + 1 < xs.length; i++) {
        const a = xs[i] ?? 0;
        const b = xs[i + 1] ?? 0;
        ctx.beginPath();
        ctx.moveTo(a + 14, y);
        ctx.lineTo(b - 14, y);
        ctx.stroke();
      }
      const labels = ["вход · плата", "веха 1 · ивент", "веха 2 · испытание", "выход · награда"];
      xs.forEach((x, i) => {
        const big = i === 2;
        ctx.beginPath();
        ctx.arc(x, y, big ? 16 : 12, 0, Math.PI * 2);
        ctx.fillStyle = big ? "rgba(255,204,51,.24)" : "rgba(58,44,30,.85)";
        ctx.fill();
        ctx.lineWidth = big ? 2.2 : 1.8;
        ctx.strokeStyle = big ? COL.honey : "rgba(255,204,51,.8)";
        ctx.stroke();
        if (i === 1 || i === 2) {
          ctx.font = "600 12px ui-monospace, Consolas, monospace";
          ctx.fillStyle = "rgba(147,128,94,.95)";
          ctx.textAlign = "center";
          ctx.textBaseline = "middle";
          ctx.fillText(i === 1 ? "?" : "!", x, y + 0.5);
          ctx.textBaseline = "alphabetic";
        }
        ctx.font = "500 11px ui-monospace, Consolas, monospace";
        ctx.fillStyle = "rgba(147,128,94,.9)";
        ctx.textAlign = "center";
        ctx.fillText(labels[i] ?? "", x, y + 44);
        ctx.textAlign = "left";
      });
    }
  }
];

const ZONES: StandDef[] = [
  {
    id: "act",
    status: "waiting",
    title: "Акт в сборе",
    note: "Области лежат полосами, зоны фракций растут поверх и режут их <b>наискось</b> — граница зоны намеренно не совпадает с границей области. Именно из-за этого карта читается как местность, а не как оглавление.",
    facts: [["области", "8 на акт"], ["зоны", "3–5, по 8–16 узлов"], ["длина маршрутов", "одинаковая у всех"]],
    verdict: "Ключевая картинка модели: форма отвечает за то, как ходишь, зона — за то, чья это земля.",
    size: [620, 330],
    draw: drawAct
  },
  {
    id: "mark",
    status: "waiting",
    title: "Метка на узле",
    note: "Пятно зоны — атмосфера: оно размывается, перекрывается, уходит под туман. Принадлежность носит сам узел ободком, и её видно при любом зуме.",
    facts: [["пятно", "атмосфера"], ["ободок", "правда"], ["прецедент", "туман карты, D9"]],
    verdict: "То же правило, что мы приняли для тумана: атмосфере нельзя доверять механику в одиночку.",
    size: [620, 330],
    draw: drawMark
  }
];

const section: SectionDef = {
  id: "map-shapes",
  title: "Формы областей",
  eyebrow: "Карта акта",
  lede:
    "Карта делится на области — куски местности со своей внутренней топологией. " +
    "Область = форма × свойства; зоны фракций ложатся третьим слоем и режут области наискось.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "model",
      title: "Модель",
      lede: "Два ограничения, из которых следует всё остальное."
    },
    {
      kind: "text",
      html:
        "<b>Длина у всех маршрутов одна</b> — шаг равен этажу, никто никого не обгоняет и не срезает. " +
        "<b>Ширина свободна везде, кроме вех</b> (сход-застава, сундуки, босс). Отсюда: форма области " +
        "может менять только связность и группировку узлов, но не длину пути. Подземелье занимает " +
        "ровно столько же этажей, сколько параллельная ему обычная дорога."
    },
    {
      kind: "note",
      html:
        "Приняты поле, остров и тропа (ширина <b>1</b>, длина 1–4). Отклонены мост и архипелаг — " +
        "оба оказались не формами: мост это короткая тропа, архипелаг — способ расставить узлы. " +
        "Гребёнка, рукава и сход одобрены как направление, лестница и логово интересны. " +
        "Остальное — <code>waiting</code>."
    },
    { kind: "stands", items: FORMS },
    {
      kind: "head",
      id: "rarity",
      title: "Редкость: рельеф обычный и рельеф-чудо",
      lede:
        "Уточнение Макса: редкая форма и ЕСТЬ чудо света — с позиции карты это особенный рельеф. " +
        "Отдельной категории «чудеса» рядом с формами не нужно."
    },
    {
      kind: "table",
      head: ["Частота", "Рельеф", "Сколько на акт"],
      rows: [
        ["Обычный", "поле, остров, тропа", "костяк карты, 6–8 областей"],
        ["Редкий", "гребёнка, рукава, сход", "1–2, не подряд"],
        ["Чудо", "лестница, логово", "1 гарантированное + шанс на второе"],
        ["Вставка", "подземелья", "с середины первого акта, не больше двух"]
      ]
    },
    {
      kind: "note",
      html:
        "Отсюда следует имя: если форма — это рельеф, она и зовётся рельефом. " +
        "<b>Осыпь</b> вместо лестницы, <b>Гребень</b>, <b>Перевал</b> вместо схода, " +
        "<b>Рукава</b>, <b>Долина</b> вместо поля. Тогда карточка при наведении пишется сама: " +
        "«Осыпь — камни ползут вниз, наверх не вернуться»."
    },
    {
      kind: "note",
      html:
        "<b>Гарантия важнее веса.</b> Чистая вероятность даёт «редко» = «никогда»: за десять забегов " +
        "игрок не увидит того, на что ушла неделя работы. Поэтому на акт кладётся одно " +
        "гарантированное редкое место, а дальше уже вес."
    },
    {
      kind: "head",
      id: "delves",
      title: "Подземелья и их варианты",
      lede: "Не форма и не свойство — авторская вставка. Формы генерируются правилами, эти собраны руками."
    },
    { kind: "split", items: DELVES },
    {
      kind: "table",
      head: ["Сценарий Макса", "Веха 1 — ивент", "Веха 2 — испытание"],
      rows: [
        [
          "Могучий враг",
          "костёр или выбранный бафф; какая будет элита — говорят здесь",
          "усиленная элита"
        ],
        [
          "Вражда",
          "выбрать сторону между двумя фракциями (у каждой своя награда) или «Нападайте вдвоём!!!»",
          "сторона — сложный бой с подкреплением; обе — очень сложный ×1.5 без подкрепления, но с баффами для своих; предупреждаем заранее"
        ],
        [
          "Выживание",
          "выбор сложности и награды задаёт число фаз",
          "2 / 3 / 4 волны. HP не восстанавливаются, позиции сохраняются, между волнами только быстрая переэкипировка реликвии; часто в окружении"
        ],
        [
          "Спасение",
          "взять пару временных Сосудов (их можно экипировать) или предмет с реликвией как доп награду",
          "первую волну надо успеть добить по таймеру, вторая — сложная"
        ]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Чего эти сценарии требуют от движка, и чего сейчас нет:</b> волны с сохранением позиций " +
        "и HP · переэкипировка между волнами · таймер как условие · временные Сосуды на один бой · " +
        "подкрепление по условию · баффы врагам от союзной фракции · спавн с нескольких сторон арены."
    },
    {
      kind: "table",
      head: ["Мои сценарии под ту же структуру", "Веха 1 — ивент", "Веха 2 — испытание"],
      rows: [
        ["Улей", "поджечь гнездо или красться", "бой; поджёг — подкрепление приходит раньше"],
        ["Тюремный блок", "кого вынести: Сосуда, припасы или реликвию", "стража, усиленная ровно на взятое"],
        ["Хранилище", "объявляешь вперёд, сколько сундуков берёшь", "охрана по объявленной жадности"],
        ["Алтарь", "сделка: травма, реликвия или здоровье", "бой, где заплаченное и работает"],
        ["Лабиринт", "три двери вслепую, за каждой своя награда", "бой по выбранной двери"],
        ["Штольня", "насколько глубоко копаем", "бой с дебафом по глубине и добычей по ней же"],
        ["Сокрытое логово", "старик за плату говорит, чьё логово", "босс фракции"]
      ]
    },
    {
      kind: "head",
      id: "props",
      title: "Свойства",
      lede: "Навешиваются на любую форму и на вставку: одна ось умножает другую."
    },
    {
      kind: "table",
      head: ["Свойство", "Что делает"],
      rows: [
        ["Сокрытая", "содержимое неизвестно до входа, награда объявлена заранее"],
        ["Туманная", "фракция известна, типы узлов внутри — нет"],
        ["Гнилая", "дебаф копится по мере прохождения области, снимается на привале"],
        ["Спорная", "внутри две фракции вперемешку — линия фронта"],
        ["Ничейная", "без фракции, дикий пул; редко и намеренно"],
        ["Владение", "эффект зоны сильнее к ядру и слабее к краю"],
        ["Тихая", "боёв нет вовсе: караван, ярмарка, две лавки подряд"]
      ]
    },
    {
      kind: "head",
      id: "wonders",
      title: "Особые места — банк идей",
      lede:
        "Это не рельеф: у рельефа правило топологическое, а здесь — своё игровое правило поверх " +
        "любой формы. Вердиктов нет; колонка «чем платишь» важнее колонки «правило»."
    },
    {
      kind: "table",
      head: ["Место", "Правило", "Чем платишь"],
      rows: [
        ["Разлом", "две зоны наложились: в бою враги обеих фракций", "состав волны непредсказуем"],
        ["Караван-сарай", "две лавки и привал, боёв нет", "этажи без наград за бой"],
        ["Поле битвы", "узлы уже кем-то пройдены: трофеи на земле", "падальщики приходят на шум"],
        ["Обелиск", "бафф на весь остаток акта", "сложность всех боёв растёт"],
        ["Мёртвая земля", "гниль копится с каждым узлом", "снимается только на привале"],
        ["Клетки", "можно освободить и нанять Сосуда", "он приходит с травмой"],
        ["Дорога торговцев", "тропа, где вместо боёв лавки", "золото уходит быстрее, чем копится"],
        ["Затопленный ход", "часть узлов скрыта до подхода на шаг", "маршрут строится вслепую"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Наведение на область даёт карточку</b> (заказ Макса): имя, строка образа, строка правила. " +
        "Значит форма — не только топология, а контент: имя, лок-ключи, лор, вес редкости живут в SO. " +
        "Длину держать жёстко: тултип на карте читают две секунды, а не абзац."
    },
    {
      kind: "head",
      id: "roads",
      title: "Дороги — слой, которого у нас нет",
      lede:
        "Ребро карты сейчас пустое: линия без свойств и без цены. А выбор игрок делает именно на " +
        "дороге, не в узле. Ранг и свойство обязаны читаться самой линией — значок на ребре " +
        "превращает карту в приборную панель."
    },
    {
      kind: "split",
      items: [
        {
          id: "road-kinds",
          status: "waiting",
          title: "Шесть видов дорог",
          note: "Тракт против тропы — самое дешёвое, что можно сделать с выбором пути: одна линия говорит «безопасно и бедно» или «опасно и щедро» без единого слова.",
          facts: [["читается", "толщиной, пунктиром, затуханием"], ["привратник", "плата объявлена заранее"], ["режущая", "показывает, что закроется"]],
          verdict: "Дороги — единственный слой карты, который сейчас не несёт вообще ничего.",
          size: [620, 330],
          draw: drawRoads
        }
      ]
    },
    {
      kind: "table",
      head: ["Ещё дороги", "Что меняется в выборе", "Цена"],
      rows: [
        ["Тайная тропа", "видна только с трофейной картой или следопытом в ростере", "средне"],
        ["Пограничная", "идёт по границе зон: бой смешанный, выбираешь, в чью землю выйти", "средне"],
        ["Под наблюдением", "пошёл — следующая встреча знает о тебе", "средне"],
        ["Обоз", "только по ней провозится груз или временный Сосуд", "средне"],
        ["Встреча в пути", "короткое событие на самом ребре", "дёшево"],
        ["Клятва на распутье", "обет привязан к дороге, а не к области", "средне"],
        ["Тесная (кооп)", "пройти можно только разделившись — или только вместе", "дорого"]
      ]
    },
    {
      kind: "head",
      id: "grand",
      title: "Великий путь",
      lede:
        "Случайная сквозная дорога от начала акта до конца, прочерченная заранее. Идёшь по ней — " +
        "награды капают тремя ступенями. Идея Макса 2026-08-02."
    },
    {
      kind: "split",
      items: [
        {
          id: "grand-line",
          status: "waiting",
          title: "Три ступени по пути",
          note: "Первая ступень — за четыре узла на линии, вторая — за половину, третья — <b>только за путь целиком</b>. Костёр в талии не считается. Награды намеренно негромкие: линия должна влиять на стратегию, а не диктовать её.",
          facts: [["1", "реликвия как за обычного врага"], ["2", "предмет, один из трёх"], ["3", "реликвия как за элиту"], ["гейт", "открывается в мете"]],
          verdict: "Первая идея, где содержанием выбора становится сама линия, а не то, что лежит в узлах.",
          size: [620, 330],
          draw: drawGrandLine
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Линия идёт как угодно — и это её цена.</b> Следуя ей, отряд теряет право идти туда, куда " +
        "ему нужно: заложничество и есть наказание, дополнительной платы не требуется (Макс, " +
        "2026-08-02; моё «вести её через опасное» отклонено). Ступени 1 и 2 считаются суммарно, " +
        "третья — весь путь целиком. Открыто: одна линия на партию в коопе."
    },
    {
      kind: "head",
      id: "gating",
      title: "Что открывается когда",
      lede: "Карта получилась богатой, поэтому слои включаются постепенно (Макс, 2026-08-02)."
    },
    {
      kind: "table",
      head: ["Слой", "Когда доступен"],
      rows: [
        ["Фракции зон", "сразу, с нулевого возвышения"],
        ["Фракции + модификаторы", "с первого возвышения"],
        ["Подземелья", "открываются в мете; опциональные допы, длину маршрута не меняют: вход виден, выход виден, начинка нет"],
        ["Великий путь", "открывается в мете"]
      ]
    },
    {
      kind: "head",
      id: "layer",
      title: "Слой карты — банк идей",
      lede:
        "Ресёрч соседей дал мало: у Monster Train, Wildfrost, Griftlands и Roguebook новизна живёт " +
        "в бою и колоде, карта у всех остаётся выбором маршрута. Поэтому идеи растут из наших систем."
    },
    {
      kind: "table",
      head: ["Идея", "Что даёт", "Цена"],
      rows: [
        ["Арена видна с карты", "узел показывает поле боя — маршрут становится выбором арены под состав", "дёшево"],
        ["Засада", "враг ставится первым, награда выше", "дёшево"],
        ["Клятва маршрута", "объявил ограничение на входе — двойная награда на выходе", "средне, Обеты есть"],
        ["Марш", "у фракции свой отряд, идущий по карте параллельно тебе", "дорого"],
        ["Захват", "соседняя фракция расширяет зону на непройденные узлы впереди", "средне"],
        ["Слух", "выбил элиту — соседние узлы настороже, лавка даёт скидку", "средне"],
        ["Дозор", "Сосуд уходит разведать область и выбывает на два узла", "средне"],
        ["Карта-трофей", "фрагменты карты с врагов раскрывают скрытое", "дёшево"],
        ["Слухи на привале", "четвёртое действие: узнать фракцию сокрытого места", "дёшево"],
        ["Разделиться (кооп)", "игроки ведут по половине состава разными дорогами и сходятся позже", "дорого, уникально"],
        ["Займ гильдии", "награда сейчас, плата на выходе из акта", "средне"],
        ["Печать гильдмастера", "помечаешь узел: добыча твоя, сложность выше", "средне"],
        ["Сбойный узел", "узел, которого Система не индексировала", "дёшево в коде, дорого в лоре"],
        ["Встреча в пути", "короткое событие на РЕБРЕ, а не на узле", "дёшево"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Оговорка ко всей «живой карте»:</b> карта видна целиком и служит планированию, поэтому " +
        "меняться она вправе <b>только впереди отряда</b> и <b>только с телеграфом за шаг</b>. " +
        "Карта, которая перекраивается за спиной, отнимает ровно то, ради чего её показывают."
    },
    {
      kind: "head",
      id: "zones",
      title: "Зоны поверх областей",
      lede: "Третий слой: чья это земля и что здесь работает иначе."
    },
    { kind: "split", items: ZONES },
    {
      kind: "note",
      html:
        "Открыто: сколько форм берём в первую очередь · шаблон или процедура у подземелья · " +
        "бывает ли область без фракции · остаётся ли стремнина вообще."
    },
    {
      kind: "head",
      id: "juice",
      title: "Джус карты — что уже есть и что запланировано",
      lede:
        "Заказ Макса 2026-08-02: не потерять визуальный слой карты, пока обсуждаем топологию. " +
        "Владелец списка — <code>gdd/70-gamefeel/map-presentation.md</code>; здесь указатель."
    },
    {
      kind: "table",
      head: ["Эффект", "Состояние", "Тумблер / заметка"],
      rows: [
        ["Лист пергамента с рваным краем", "в игре", "map.sheet"],
        ["Стол под листом, тёплое пятно света", "в игре", "map.table"],
        ["Шторка перехода на шаг по карте", "в игре", "выбор засчитывается на закрытом кадре"],
        ["Моргание доступных узлов", "в игре", "map.pulse, одна огибающая"],
        ["Бегущая волна по дорожкам", "в игре", "map.pathflow"],
        ["Поездка фишки-шлема", "в игре, выключена", "map.travel, заменена шторкой"],
        ["Туман карты", "в игре, выключен", "map.fog, «не то, мб позже»"],
        ["Пятно зоны: SDF-метабол + domain warp", "план", "печётся в RT один раз на генерацию акта"],
        ["Ободок зоны на узле", "план", "правда о принадлежности, в отличие от пятна"],
        ["Картуш с именем зоны", "план", "«Молниеносные Гоблины» + иконка эффекта"],
        ["Раскрытие зоны при входе", "план", "чернила расползаются от узла"],
        ["Дверь подземелья и карточка награды", "план", "закрытая створка, содержимое скрыто"],
        ["Обвал за спиной на входе в область", "план", "коммит должен ощущаться"],
        ["Шов между областями", "план", "гряда штриховкой в пробеле, не второе пятно"],
        ["Подсветка достижимого на 1–2 шага", "план", "вместо подсветки ряда"],
        ["Счётчик шагов до босса", "план", "ответ на вопрос, ради которого хотели вехи"],
        ["Грейдинг карты по зоне", "план", "локальный Volume, стык с visual-direction"],
        ["Ambient-партиклы под зону", "план", "споры, пепел, светлячки"],
        ["Чернила дорисовываются за фишкой", "план", "тех-вектор вместо «листа на столе»"],
        ["Роза ветров и сетка на Shapes", "план", "процедурно, не арт"],
        ["Параллакс-дымка", "план", "слой над листом"],
        ["Звук карты", "план", "шелест бумаги, перо; стык с audio"]
      ]
    }
  ]
};

export default section;
