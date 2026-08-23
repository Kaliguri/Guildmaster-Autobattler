/* Зоны влияния — то, что делаем СЕЙЧАС.

   Модель после уточнений Макса 2026-08-02:

   1. Местность не рисуем вовсе. Карта остаётся картой узлов и дорог; единственный территориальный
      слой — зона влияния.
   2. Зона — это не «фракция-государство», а УКАЗАТЕЛЬ СОСТАВА: какие враги ждут. Их много, и
      одного вида может быть несколько сразу — «Взрывные Гоблины» рядом с «Проворными Гоблинами».
   3. Узлы НЕ трогаем. Ни ободков, ни флажков: узел читается как принадлежащий зоне потому, что
      лежит на её территории. Подробности игрок берёт наведением на НАЗВАНИЕ, как на страну.
   4. Зоны ограничивают друг друга. Это РАЗДЕЛ карты, а не пятна с перекрытиями: у соседей общая
      граница, и она рваная.

   Как это считается. Плоскость делится по ближайшему узлу — это Вороной, а объединение ячеек узлов
   одной зоны и есть её территория. Координаты перед замером гнутся фрактальным шумом, поэтому
   граница рваная, а у соседей она ОДНА И ТА ЖЕ: обе стороны считают один и тот же критерий.
   Дальше радиуса влияния земля ничейная. Reference: redblobgames (Вороной + релаксация Ллойда),
   реф стиля — мод Spire Biomes для Slay the Spire. */

import { tick } from "../clock.js";
import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";
import { fbm, hash2, vnoise } from "../lib/noise.js";

/* ---------- палитра зон ---------- */

interface ZoneDef {
  /** Вид врага задаёт ЦВЕТ: гоблины зелёные, разбойники красные. */
  hue: [number, number, number];
  /** Полное имя на карте. */
  name: string;
  /** Оттеночный сдвиг: две зоны одного вида различаются светлотой, а не цветом. */
  shade: number;
}

const ZONES: ZoneDef[] = [
  { hue: [132, 214, 92], name: "Взрывные Гоблины", shade: 1 },
  { hue: [132, 214, 92], name: "Проворные Гоблины", shade: 0.68 },
  { hue: [255, 96, 80], name: "Жадные Разбойники", shade: 1 },
  { hue: [120, 168, 255], name: "Дозор Короны", shade: 1 }
];

function rgb(z: ZoneDef, alpha: number): string {
  const [r, g, b] = z.hue;
  const k = z.shade;
  return `rgba(${Math.round(r * k)},${Math.round(g * k)},${Math.round(b * k)},${alpha})`;
}

/* ---------- сцена ---------- */

interface Dot {
  x: number;
  y: number;
  zone: number;
}

/** Акт целиком в миниатюре: пять колонок, четыре зоны по 6–9 узлов, как в игре. */
function scene(w: number, h: number): { dots: Dot[]; pairs: Array<[number, number]> } {
  const cols = [2, 4, 4, 4, 3, 2];
  const left = w * 0.11;
  const stepX = (w * 0.78) / (cols.length - 1);
  const dots: Dot[] = [];
  const starts: number[] = [];

  cols.forEach((rows, c) => {
    starts.push(dots.length);
    for (let r = 0; r < rows; r++) {
      // Зоны нарезаны наискось: граница намеренно не совпадает со столбцом этажа.
      let zone = 0;
      if (c >= 1 && r >= rows - 2) zone = 1;
      if (c >= 3) zone = 2;
      if (c >= 3 && r === 0) zone = 3;
      if (c >= 4 && r <= 1) zone = 3;
      dots.push({
        x: left + c * stepX + (jag(c * 9 + r, 5) - 0.5) * w * 0.02,
        y: h * 0.5 + (r - (rows - 1) / 2) * (h * 0.21) + (jag(c * 7 + r, 3) - 0.5) * h * 0.04,
        zone
      });
    }
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

/* ---------- шум ---------- */

/* ---------- раздел плоскости на зоны ---------- */

interface PartOpts {
  /** Сила рваности границы в пикселях. 0 — прямые рёбра Вороного. */
  warp: number;
  /** Радиус влияния узла: дальше него земля ничейная. */
  reach: number;
  /** Толщина яркой границы. */
  edge: number;
}

/** Кто владеет точкой: индекс зоны и расстояние до её ближайшего узла. */
function ownerAt(x: number, y: number, dots: Dot[], o: PartOpts): { zone: number; dist: number } {
  let px = x;
  let py = y;
  if (o.warp > 0) {
    px += fbm(x * 0.006, y * 0.006, 3) * o.warp + fbm(x * 0.03, y * 0.03, 11) * o.warp * 0.35;
    py += fbm(x * 0.006 + 5.2, y * 0.006 + 1.7, 7) * o.warp + fbm(x * 0.03, y * 0.03, 19) * o.warp * 0.35;
  }
  let best = 1e9;
  let zone = -1;
  for (const d of dots) {
    const dd = (d.x - px) * (d.x - px) + (d.y - py) * (d.y - py);
    if (dd < best) {
      best = dd;
      zone = d.zone;
    }
  }
  const dist = Math.sqrt(best);
  return { zone: dist > o.reach ? -1 : zone, dist };
}

const cache = new Map<string, HTMLCanvasElement>();

/** Заливка территорий и общая граница между соседями — одним проходом по пикселям. */
function renderPartition(w: number, h: number, dots: Dot[], o: PartOpts): HTMLCanvasElement {
  const key = `${Math.round(w)}x${Math.round(h)}|${o.warp}|${o.reach}|${o.edge}|${dots.length}|${dots[0]?.x.toFixed(1)}`;
  const hit = cache.get(key);
  if (hit) return hit;

  const cv = document.createElement("canvas");
  cv.width = Math.max(1, Math.round(w));
  cv.height = Math.max(1, Math.round(h));
  const c = cv.getContext("2d");
  if (!c) return cv;

  // Владелец каждого пикселя считается один раз и переиспользуется для границы.
  const own = new Int16Array(cv.width * cv.height);
  for (let y = 0; y < cv.height; y++)
    for (let x = 0; x < cv.width; x++) own[y * cv.width + x] = ownerAt(x, y, dots, o).zone;

  const img = c.createImageData(cv.width, cv.height);
  const px = img.data;

  for (let y = 0; y < cv.height; y++) {
    for (let x = 0; x < cv.width; x++) {
      const i = y * cv.width + x;
      const z = own[i] ?? -1;
      let r = 0;
      let g = 0;
      let b = 0;
      let a = 0;

      if (z >= 0) {
        const def = ZONES[z];
        if (def) {
          const k = def.shade;
          r = def.hue[0] * k;
          g = def.hue[1] * k;
          b = def.hue[2] * k;
          a = 46;                       // бледная заливка: узлы и дороги остаются главными
        }
      }

      // Граница: сосед принадлежит другому владельцу. Считается по ОДНОМУ критерию с обеих
      // сторон, поэтому у соседних зон она общая, а не две линии рядом.
      let border = false;
      for (let d = 1; d <= o.edge && !border; d++) {
        const right = x + d < cv.width ? own[i + d] ?? z : z;
        const down = y + d < cv.height ? own[i + d * cv.width] ?? z : z;
        const left = x - d >= 0 ? own[i - d] ?? z : z;
        const up = y - d >= 0 ? own[i - d * cv.width] ?? z : z;
        if (right !== z || down !== z || left !== z || up !== z) border = true;
      }

      if (border) {
        const def = ZONES[z >= 0 ? z : 0];
        if (z >= 0 && def) {
          const k = def.shade;
          r = def.hue[0] * k;
          g = def.hue[1] * k;
          b = def.hue[2] * k;
          a = 235;                      // яркая кромка — то, чем зона себя ограничивает
        }
      }

      px[i * 4] = r;
      px[i * 4 + 1] = g;
      px[i * 4 + 2] = b;
      px[i * 4 + 3] = a;
    }
  }

  c.putImageData(img, 0, 0);
  cache.set(key, cv);
  return cv;
}

/* ---------- узлы, дороги, печать ---------- */

function roads(ctx: CanvasRenderingContext2D, dots: Dot[], pairs: Array<[number, number]>): void {
  ctx.strokeStyle = "rgba(120,104,76,.7)";
  ctx.lineWidth = 1.4;
  ctx.setLineDash([3, 5]);
  for (const [a, b] of pairs) {
    const from = dots[a];
    const to = dots[b];
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  }
  ctx.setLineDash([]);
}

/** Узлы БЕЗ метки принадлежности: их вид не зависит от того, чья это земля. */
function nodes(ctx: CanvasRenderingContext2D, dots: Dot[]): void {
  for (const d of dots) {
    ctx.beginPath();
    ctx.arc(d.x, d.y, 8, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = "rgba(184,134,59,.8)";
    ctx.stroke();
  }
}

function zoneCentre(dots: Dot[], zone: number): { x: number; y: number } {
  const own = dots.filter((d) => d.zone === zone);
  if (own.length === 0) return { x: 0, y: 0 };
  return {
    x: own.reduce((s, d) => s + d.x, 0) / own.length,
    y: own.reduce((s, d) => s + d.y, 0) / own.length
  };
}

type Label = "seal" | "plate" | "plain" | "none";

/** Печать зоны: знак вида + имя. Реф — Spire Biomes: бледная эмблема под текстом, не поверх. */
function zoneLabel(
  ctx: CanvasRenderingContext2D,
  z: ZoneDef,
  cx: number,
  cy: number,
  kind: Label
): void {
  if (kind === "none") return;

  if (kind === "seal") {
    ctx.save();
    ctx.globalAlpha = 0.5;
    ctx.strokeStyle = rgb(z, 0.9);
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(cx, cy - 16, 26, 0, Math.PI * 2);
    ctx.stroke();
    // зубцы печати
    for (let i = 0; i < 16; i++) {
      const a = (i / 16) * Math.PI * 2;
      ctx.beginPath();
      ctx.moveTo(cx + Math.cos(a) * 26, cy - 16 + Math.sin(a) * 26);
      ctx.lineTo(cx + Math.cos(a) * 31, cy - 16 + Math.sin(a) * 31);
      ctx.stroke();
    }
    // знак вида: рожки для гоблинов, клинок для разбойников, корона для дозора
    ctx.lineWidth = 2.4;
    ctx.beginPath();
    if (z.hue[0] === 132) {
      ctx.moveTo(cx - 11, cy - 8);
      ctx.lineTo(cx - 6, cy - 22);
      ctx.lineTo(cx, cy - 10);
      ctx.lineTo(cx + 6, cy - 22);
      ctx.lineTo(cx + 11, cy - 8);
    } else if (z.hue[0] === 255) {
      ctx.moveTo(cx - 12, cy - 6);
      ctx.lineTo(cx + 10, cy - 26);
      ctx.moveTo(cx + 2, cy - 12);
      ctx.lineTo(cx + 12, cy - 4);
    } else {
      ctx.moveTo(cx - 12, cy - 8);
      ctx.lineTo(cx - 8, cy - 24);
      ctx.lineTo(cx, cy - 12);
      ctx.lineTo(cx + 8, cy - 24);
      ctx.lineTo(cx + 12, cy - 8);
      ctx.closePath();
    }
    ctx.stroke();
    ctx.restore();
  }

  ctx.font = "600 15px Georgia, 'Times New Roman', serif";
  const wide = ctx.measureText(z.name).width;

  if (kind === "plate") {
    ctx.fillStyle = "rgba(26,20,14,.72)";
    ctx.fillRect(cx - wide / 2 - 10, cy + 6, wide + 20, 24);
    ctx.strokeStyle = rgb(z, 0.75);
    ctx.lineWidth = 1.2;
    ctx.strokeRect(cx - wide / 2 - 10, cy + 6, wide + 20, 24);
  }

  // Тень под текстом: имя обязано читаться и на бледной заливке, и на границе.
  ctx.fillStyle = "rgba(20,15,10,.55)";
  ctx.fillText(z.name, cx - wide / 2 + 1, cy + 24);
  ctx.fillStyle = rgb(z, 0.95);
  ctx.fillText(z.name, cx - wide / 2, cy + 23);
}

/* ---------- стенды ---------- */

function partitionStand(o: PartOpts, label: Label, only?: number[]): DrawFn {
  return (ctx, w, h) => {
    const { dots, pairs } = scene(w, h);
    const used = only ? dots.filter((d) => only.includes(d.zone)) : dots;
    ctx.drawImage(renderPartition(w, h, used, o), 0, 0, w, h);
    roads(ctx, dots, pairs);
    nodes(ctx, dots);

    const shown = only ?? [0, 1, 2, 3];
    for (const zi of shown) {
      const def = ZONES[zi];
      if (!def) continue;
      const c = zoneCentre(dots, zi);
      zoneLabel(ctx, def, Math.max(90, Math.min(w - 90, c.x)), c.y, label);
    }
  };
}

/** Наведение на имя зоны — единственный способ узнать состав. Узел молчит. */
const drawHover: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const o: PartOpts = { warp: 46, reach: 96, edge: 2 };
  ctx.drawImage(renderPartition(w, h, dots, o), 0, 0, w, h);
  roads(ctx, dots, pairs);
  nodes(ctx, dots);

  const def = ZONES[0];
  const c = zoneCentre(dots, 0);
  if (def) zoneLabel(ctx, def, Math.max(90, Math.min(w - 90, c.x)), c.y, "seal");

  const bx = Math.max(20, Math.min(w - 250, c.x - 40));
  const by = c.y + 44;
  ctx.fillStyle = "rgba(24,18,12,.93)";
  ctx.fillRect(bx, by, 236, 96);
  ctx.strokeStyle = rgb(ZONES[0] as ZoneDef, 0.8);
  ctx.lineWidth = 1.4;
  ctx.strokeRect(bx, by, 236, 96);

  ctx.font = "600 13px Georgia, serif";
  ctx.fillStyle = rgb(ZONES[0] as ZoneDef, 0.95);
  ctx.fillText("Взрывные Гоблины", bx + 14, by + 24);
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(200,186,150,.92)";
  ctx.fillText("гоблины · 8 узлов", bx + 14, by + 44);
  ctx.fillText("враги взрываются при смерти", bx + 14, by + 62);
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.fillText("наведи на имя — узел молчит", bx + 14, by + 82);

  // курсор на имени
  ctx.fillStyle = "rgba(232,214,178,.9)";
  ctx.beginPath();
  ctx.moveTo(c.x + 30, c.y + 16);
  ctx.lineTo(c.x + 30, c.y + 30);
  ctx.lineTo(c.x + 40, c.y + 26);
  ctx.closePath();
  ctx.fill();
};

/** Две зоны одного вида рядом: цвет один, различает светлота и имя. */
const drawSameKind: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const o: PartOpts = { warp: 46, reach: 96, edge: 2 };
  const used = dots.filter((d) => d.zone === 0 || d.zone === 1);
  ctx.drawImage(renderPartition(w, h, used, o), 0, 0, w, h);
  roads(ctx, dots, pairs);
  nodes(ctx, dots);
  for (const zi of [0, 1]) {
    const def = ZONES[zi];
    if (!def) continue;
    const c = zoneCentre(dots, zi);
    zoneLabel(ctx, def, Math.max(90, Math.min(w - 90, c.x)), c.y, "plain");
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("один вид — один цвет; различают светлота и имя", 16, h - 12);
};

/** Раскрытие зоны при входе: кромка вспыхивает и заливка наливается. */
const drawReveal: DrawFn = (ctx, w, h) => {
  const { dots, pairs } = scene(w, h);
  const o: PartOpts = { warp: 46, reach: 96, edge: 2 };
  const cycle = (tick % 120) / 120;
  const grow = Math.min(1, cycle * 2.4);

  ctx.save();
  ctx.globalAlpha = 0.35 + grow * 0.65;
  ctx.drawImage(renderPartition(w, h, dots, o), 0, 0, w, h);
  ctx.restore();

  roads(ctx, dots, pairs);
  nodes(ctx, dots);
  const def = ZONES[2];
  if (def) {
    const c = zoneCentre(dots, 2);
    ctx.save();
    ctx.globalAlpha = grow;
    zoneLabel(ctx, def, Math.max(90, Math.min(w - 90, c.x)), c.y, "seal");
    ctx.restore();
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("шаг в чужую землю: кромка наливается, печать проявляется", 16, h - 12);
};

const WIDE: [number, number] = [620, 340];
const SIZE: [number, number] = [320, 270];

const EDGES: StandDef[] = [
  {
    id: "edge-straight",
    status: "rejected",
    title: "Прямые рёбра",
    tag: "warp 0",
    note: "Чистый Вороной без шума: границы прямые и выдают алгоритм с первого взгляда.",
    verdict: "Отклонено: карта сразу читается как схема раздела, а не как земля.",
    size: SIZE,
    draw: partitionStand({ warp: 0, reach: 96, edge: 2 }, "none")
  },
  {
    id: "edge-soft",
    status: "waiting",
    title: "Умеренная рваность",
    tag: "warp 28",
    note: "Шум гнёт координаты перед замером. Граница живая, но силуэт зоны ещё узнаваем.",
    size: SIZE,
    draw: partitionStand({ warp: 28, reach: 96, edge: 2 }, "none")
  },
  {
    id: "edge-wild",
    status: "waiting",
    title: "Сильная рваность",
    tag: "warp 68",
    note: "Красиво и дико, но у зоны появляются оторванные острова, а узел у границы может оказаться в чужой земле.",
    size: SIZE,
    draw: partitionStand({ warp: 68, reach: 96, edge: 2 }, "none")
  }
];

const LABELS: StandDef[] = [
  {
    id: "label-seal",
    status: "waiting",
    title: "Печать и имя",
    tag: "реф Spire Biomes",
    note: "Бледная эмблема вида под названием. Имя читается издалека, эмблема даёт «чья земля» боковым зрением.",
    verdict: "Ближе всего к рефу и к нашему листу-пергаменту.",
    size: WIDE,
    draw: partitionStand({ warp: 46, reach: 96, edge: 2 }, "seal")
  },
  {
    id: "label-plate",
    status: "waiting",
    title: "Имя на плашке",
    note: "Читается всегда и на любом фоне, но плашка — предмет интерфейса, а не карты: на пергаменте выглядит наклейкой.",
    size: WIDE,
    draw: partitionStand({ warp: 46, reach: 96, edge: 2 }, "plate")
  }
];

const CASES: StandDef[] = [
  {
    id: "hover",
    status: "waiting",
    title: "Наведение на имя",
    note: "Состав, число узлов и правило зоны показываются при наведении на НАЗВАНИЕ. Узлы молчат — они и так внутри территории.",
    size: WIDE,
    draw: drawHover
  },
  {
    id: "same-kind",
    status: "waiting",
    title: "Две зоны одного вида",
    note: "«Взрывные Гоблины» рядом с «Проворными». Цвет у вида общий, различают светлота и имя — иначе граница между ними исчезнет.",
    size: WIDE,
    draw: drawSameKind
  },
  {
    id: "reveal",
    status: "waiting",
    title: "Вход в зону",
    note: "Кромка наливается, печать проявляется. Событие, объясняющее, почему земля вдруг стала чужой.",
    size: WIDE,
    draw: drawReveal
  }
];

const section: SectionDef = {
  id: "map-zones",
  title: "Зоны влияния",
  eyebrow: "Карта акта",
  lede:
    "Зоны делят карту между собой, как страны на политической карте: у соседей общая рваная " +
    "граница, внутри — печать с именем. Узлы не трогаем вовсе.",
  blocks: [
    {
      kind: "note",
      html:
        "<b>Модель после уточнений Макса:</b> зона — не государство, а <b>указатель состава</b>: какие " +
        "враги ждут. Их много, и одного вида бывает несколько сразу — «Взрывные Гоблины» рядом с " +
        "«Проворными». Одной зоне принадлежит 6–12 узлов. <b>Узлы не трогаем</b>: ни ободков, ни " +
        "флажков — узел читается как чужой потому, что лежит на чужой земле, а подробности игрок " +
        "берёт наведением на имя. Местность не рисуем, атласная земля отложена."
    },
    {
      kind: "head",
      id: "edges",
      title: "Граница: чем рвём",
      lede:
        "Плоскость делится по ближайшему узлу — это Вороной, а объединение ячеек зоны и есть её " +
        "территория. Координаты гнутся шумом до замера, поэтому граница рваная и у соседей ОДНА."
    },
    { kind: "stands", items: EDGES },
    {
      kind: "note",
      html:
        "Почему граница получается общей: обе стороны считают <b>один и тот же критерий</b> — кто " +
        "ближе. Рисовать двум зонам по своему контуру нельзя, иначе на стыке появятся две линии и " +
        "щель между ними."
    },
    {
      kind: "head",
      id: "labels",
      title: "Имя зоны",
      lede: "Единственное место, где игрок узнаёт, чья это земля и что в ней водится."
    },
    { kind: "split", items: LABELS },
    {
      kind: "head",
      id: "cases",
      title: "Случаи, которые надо проверить",
      lede: "Наведение, две зоны одного вида, вход на чужую территорию."
    },
    { kind: "split", items: CASES },
    {
      kind: "table",
      head: ["Открытый вопрос", "Мой вариант"],
      rows: [
        ["Ничейная земля бывает?", "да: дальше радиуса влияния узлов земля ничья — так тракт между владениями остаётся свободным"],
        ["Сколько имён видно сразу", "все, но мелко; крупно — только та зона, где стоит отряд"],
        ["Эмблема — арт или процедура", "процедурный знак вида (рожки, клинок, корона); арт дороже и не масштабируется на десяток зон"],
        ["Что при перекрытии зон", "перекрытий нет по построению: это раздел, а не пятна"]
      ]
    }
  ]
};

export default section;
