/* Карта: подача. Три вещи в одном разделе.

   1. Как на одном листе уживаются ДВА независимых слоя: чья это земля (зона фракции) и какой это
      рельеф (форма области). Приём взят у бумажной картографии: физическая карта несёт рельеф
      штриховкой и значками, политическая — принадлежность заливкой. Веками лежат на одном листе и
      не спорят, потому что заняли РАЗНЫЕ каналы восприятия.
   2. Значок каждого рельефа и вид области целиком: узлы, дороги, россыпь значков.
   3. Что УЖЕ нарисовано в игре — карточками, а не списком. Список врал самому правилу стенда:
      «раздел показывает принятое», а принятое лежало таблицей и посмотреть его было негде.

   Канон подачи — docs/wiki/gdd/70-gamefeel/map-presentation.md. */

import { tick } from "../clock.js";
import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

const GOBLIN = "132,214,92";
const BANDIT = "255,96,80";
const INK = "58,44,30";

/* ---------- общие куски ---------- */

/** Пятно зоны: круги в одном path — заливка объединяет их без внутренних швов. */
function blob(
  ctx: CanvasRenderingContext2D,
  pts: Array<[number, number]>,
  radius: number,
  fill: string,
  salt: number
): void {
  ctx.beginPath();
  pts.forEach(([x, y], i) => {
    const r = radius * (0.78 + jag(i, salt) * 0.5);
    ctx.moveTo(x + r, y);
    ctx.arc(x, y, r, 0, Math.PI * 2);
  });
  ctx.fillStyle = fill;
  ctx.fill();
}

interface Dot {
  x: number;
  y: number;
  zone?: string;
}

function nodes(ctx: CanvasRenderingContext2D, list: Dot[], ring: boolean, r = 7): void {
  for (const d of list) {
    ctx.beginPath();
    ctx.arc(d.x, d.y, r, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = ring && d.zone ? 2.4 : 1.5;
    ctx.strokeStyle = ring && d.zone ? `rgba(${d.zone},.95)` : "rgba(184,134,59,.7)";
    ctx.stroke();
  }
}

function roads(ctx: CanvasRenderingContext2D, list: Dot[], pairs: Array<[number, number]>): void {
  ctx.strokeStyle = "rgba(147,128,94,.5)";
  ctx.lineWidth = 1.4;
  for (const [a, b] of pairs) {
    const from = list[a];
    const to = list[b];
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  }
}

/* ---------- значки рельефа ----------
   Картографический знак, а не иллюстрация: он обязан читаться на общем зуме карты, поэтому это
   несколько крупных штрихов. Ахроматика намеренная — цвет занят фракцией. */

type ReliefKind = "vale" | "isle" | "trail" | "comb" | "forks" | "muster" | "scree" | "lair";

/** Один знак в точке. `s` — размер в пикселях (высота знака). */
function glyph(ctx: CanvasRenderingContext2D, kind: ReliefKind, x: number, y: number, s: number): void {
  ctx.strokeStyle = `rgba(${INK},.62)`;
  ctx.fillStyle = `rgba(${INK},.55)`;
  ctx.lineWidth = Math.max(1, s * 0.14);
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.beginPath();

  switch (kind) {
    case "vale":
      // Кочки: три травяных пучка.
      for (let i = -1; i <= 1; i++) {
        ctx.moveTo(x + i * s * 0.42, y + s * 0.3);
        ctx.quadraticCurveTo(x + i * s * 0.42, y - s * 0.1, x + i * s * 0.42 + s * 0.16, y - s * 0.32);
      }
      ctx.stroke();
      break;

    case "isle":
      // Остров: горбик и волна под ним — как на морских картах.
      ctx.moveTo(x - s * 0.45, y + s * 0.05);
      ctx.quadraticCurveTo(x, y - s * 0.55, x + s * 0.45, y + s * 0.05);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(x - s * 0.5, y + s * 0.34);
      ctx.quadraticCurveTo(x - s * 0.25, y + s * 0.18, x, y + s * 0.34);
      ctx.quadraticCurveTo(x + s * 0.25, y + s * 0.5, x + s * 0.5, y + s * 0.34);
      ctx.stroke();
      break;

    case "trail":
      // Тропа: цепочка следов, уходящая вбок.
      for (let i = -1; i <= 1; i++) {
        const px = x + i * s * 0.36;
        const py = y + i * s * 0.12;
        ctx.moveTo(px, py);
        ctx.lineTo(px + s * 0.12, py);
      }
      ctx.stroke();
      break;

    case "comb":
      // Гребень: параллельные штрихи с общей осью.
      for (let i = -1; i <= 1; i++) {
        ctx.moveTo(x + i * s * 0.3, y - s * 0.34);
        ctx.lineTo(x + i * s * 0.3, y + s * 0.34);
      }
      ctx.stroke();
      break;

    case "forks":
      // Рукава: ствол, расходящийся надвое.
      ctx.moveTo(x - s * 0.45, y);
      ctx.lineTo(x, y);
      ctx.moveTo(x, y);
      ctx.lineTo(x + s * 0.45, y - s * 0.34);
      ctx.moveTo(x, y);
      ctx.lineTo(x + s * 0.45, y + s * 0.34);
      ctx.stroke();
      break;

    case "muster":
      // Перевал: две вершины и седловина между ними.
      ctx.moveTo(x - s * 0.5, y + s * 0.32);
      ctx.lineTo(x - s * 0.2, y - s * 0.3);
      ctx.lineTo(x, y + s * 0.06);
      ctx.lineTo(x + s * 0.22, y - s * 0.34);
      ctx.lineTo(x + s * 0.5, y + s * 0.32);
      ctx.stroke();
      break;

    case "scree":
      // Осыпь: камни, сползающие в одну сторону.
      for (let i = 0; i < 3; i++) {
        const px = x - s * 0.34 + i * s * 0.34;
        const py = y - s * 0.24 + i * s * 0.24;
        ctx.moveTo(px, py);
        ctx.lineTo(px + s * 0.2, py + s * 0.16);
      }
      ctx.stroke();
      break;

    case "lair":
      // Логово: тёмный зев под сводом.
      ctx.moveTo(x - s * 0.4, y + s * 0.34);
      ctx.quadraticCurveTo(x - s * 0.4, y - s * 0.36, x, y - s * 0.36);
      ctx.quadraticCurveTo(x + s * 0.4, y - s * 0.36, x + s * 0.4, y + s * 0.34);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(x - s * 0.22, y + s * 0.34);
      ctx.quadraticCurveTo(x, y - s * 0.16, x + s * 0.22, y + s * 0.34);
      ctx.fill();
      break;
  }
}

/** Россыпь знаков по площади области: плотность одинаковая, положение детерминированное. */
function reliefField(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  halfW: number,
  halfH: number,
  kind: ReliefKind,
  count: number,
  salt: number
): void {
  for (let i = 0; i < count; i++) {
    const x = cx + (jag(i, salt) - 0.5) * halfW * 2;
    const y = cy + (jag(i, salt + 40) - 0.5) * halfH * 2;
    glyph(ctx, kind, x, y, 13 + jag(i, salt + 80) * 4);
  }
}

/* ---------- карточка рельефа: значок + вид области ---------- */

interface ReliefCard {
  kind: ReliefKind;
  title: string;
  en: string;
  /** Ширины колонок области. */
  cols: number[];
  /** Как связывать: густо, полосами, половинами или со сносом вбок. */
  wiring: "woven" | "lanes" | "split" | "drift";
  note: string;
}

const RELIEFS: ReliefCard[] = [
  { kind: "vale", title: "Долина", en: "Vale", cols: [3, 4, 3], wiring: "woven", note: "Простор: выбор переигрывается на каждом шагу. Знак — травяные кочки." },
  { kind: "isle", title: "Остров", en: "Isle", cols: [1, 3, 3, 1], wiring: "woven", note: "Один вход, один выход. Знак — горбик над волной, как на морских картах." },
  { kind: "trail", title: "Тропа", en: "Trail", cols: [1, 1, 1, 1], wiring: "lanes", note: "Ширина один. Знак — цепочка следов, уходящая вбок." },
  { kind: "comb", title: "Гребень", en: "Comb", cols: [3, 3, 3], wiring: "lanes", note: "Полосы не сообщаются. Знак — параллельные штрихи." },
  { kind: "forks", title: "Рукава", en: "Forks", cols: [2, 4, 4], wiring: "split", note: "Две половины без связи. Знак — ствол, расходящийся надвое." },
  { kind: "muster", title: "Перевал", en: "Muster", cols: [4, 1, 4], wiring: "woven", note: "Всё сходится и снова расходится. Знак — две вершины и седловина." },
  { kind: "scree", title: "Осыпь", en: "Scree", cols: [4, 4, 4], wiring: "drift", note: "Сносит вбок, назад нельзя. Знак — камни, сползающие в одну сторону." },
  { kind: "lair", title: "Логово", en: "Lair", cols: [3, 3, 1], wiring: "woven", note: "Дороги сходятся к хозяину. Знак — тёмный зев под сводом." }
];

/** Мини-раскладка области: узлы по колонкам и дороги по выбранной проводке. */
function regionOf(card: ReliefCard, w: number, h: number): { list: Dot[]; pairs: Array<[number, number]> } {
  const list: Dot[] = [];
  const starts: number[] = [];
  const stepX = (w - 96) / Math.max(1, card.cols.length - 1);
  const cy = h * 0.46;

  card.cols.forEach((rows, c) => {
    starts.push(list.length);
    for (let r = 0; r < rows; r++)
      list.push({ x: 48 + c * stepX, y: cy + (r - (rows - 1) / 2) * 26 });
  });

  const pairs: Array<[number, number]> = [];
  for (let c = 0; c + 1 < card.cols.length; c++) {
    const a0 = starts[c] ?? 0;
    const b0 = starts[c + 1] ?? 0;
    const ra = card.cols[c] ?? 1;
    const rb = card.cols[c + 1] ?? 1;

    for (let i = 0; i < ra; i++) {
      if (card.wiring === "lanes") {
        if (i < rb) pairs.push([a0 + i, b0 + i]);
      } else if (card.wiring === "drift") {
        pairs.push([a0 + i, b0 + Math.min(i + 1, rb - 1)]);
      } else if (card.wiring === "split") {
        const half = Math.ceil(ra / 2);
        const halfB = Math.ceil(rb / 2);
        const target = i < half
          ? Math.min(Math.round((i / Math.max(1, half - 1)) * (halfB - 1)), halfB - 1)
          : halfB + Math.min(Math.round(((i - half) / Math.max(1, ra - half - 1)) * (rb - halfB - 1)), rb - halfB - 1);
        pairs.push([a0 + i, b0 + Math.max(0, target)]);
      } else {
        const t = ra === 1 ? 0 : i / (ra - 1);
        const centre = Math.round(t * (rb - 1));
        for (let d = -1; d <= 1; d++) {
          const j = centre + d;
          if (j >= 0 && j < rb) pairs.push([a0 + i, b0 + j]);
        }
      }
    }
  }
  return { list, pairs };
}

function reliefStand(card: ReliefCard): DrawFn {
  return (ctx, w, h) => {
    const { list, pairs } = regionOf(card, w, h);

    // Зона под областью — та же бледная заливка, что на карте: знак обязан читаться поверх неё.
    blob(ctx, [[w * 0.3, h * 0.42], [w * 0.6, h * 0.5], [w * 0.82, h * 0.42]], 48, `rgba(${GOBLIN},.12)`, 5);
    reliefField(ctx, w * 0.5, h * 0.46, w * 0.38, h * 0.26, card.kind, 5, 13);

    roads(ctx, list, pairs);
    nodes(ctx, list.map((d) => ({ ...d, zone: GOBLIN })), true, 6);

    // Крупный знак в углу — эталон, как он выглядит вне россыпи.
    glyph(ctx, card.kind, w - 34, 30, 26);
    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.85)";
    ctx.fillText(card.en, 16, h - 14);
  };
}

/* ---------- два слоя: как не надо и как надо ---------- */

function scene(w: number, h: number): { list: Dot[]; pairs: Array<[number, number]> } {
  const left = w * 0.12;
  const step = (w * 0.76) / 4;
  const mid = h / 2 - 6;
  const list: Dot[] = [];
  for (let c = 0; c < 5; c++) {
    const rows = c === 2 ? 2 : 3;
    for (let r = 0; r < rows; r++) {
      list.push({
        x: left + c * step,
        y: mid + (r - (rows - 1) / 2) * 40,
        zone: c < 2 ? GOBLIN : c === 2 && r === 0 ? GOBLIN : BANDIT
      });
    }
  }
  const pairs: Array<[number, number]> = [];
  let base = 0;
  for (let c = 0; c + 1 < 5; c++) {
    const rows = c === 2 ? 2 : 3;
    const next = c + 1 === 2 ? 2 : 3;
    for (let r = 0; r < rows; r++)
      for (let n = 0; n < next; n++)
        if (Math.abs(r - n) <= 1) pairs.push([base + r, base + rows + n]);
    base += rows;
  }
  return { list, pairs };
}

const drawClash: DrawFn = (ctx, w, h) => {
  const { list, pairs } = scene(w, h);
  blob(ctx, [[w * 0.2, h * 0.34], [w * 0.36, h * 0.42], [w * 0.5, h * 0.36]], 46, "rgba(184,134,59,.22)", 2);
  blob(ctx, [[w * 0.56, h * 0.62], [w * 0.72, h * 0.58], [w * 0.86, h * 0.66]], 44, "rgba(138,206,255,.20)", 6);
  blob(ctx, [[w * 0.18, h * 0.5], [w * 0.34, h * 0.56], [w * 0.46, h * 0.5]], 48, `rgba(${GOBLIN},.22)`, 3);
  blob(ctx, [[w * 0.62, h * 0.44], [w * 0.78, h * 0.5], [w * 0.9, h * 0.44]], 46, `rgba(${BANDIT},.22)`, 7);
  roads(ctx, list, pairs);
  nodes(ctx, list, false);
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(255,96,80,.9)";
  ctx.fillText("четыре пятна на одном листе: чей это край — уже не прочесть", 20, h - 16);
};

const drawLayers: DrawFn = (ctx, w, h) => {
  const { list, pairs } = scene(w, h);
  blob(ctx, [[w * 0.18, h * 0.5], [w * 0.34, h * 0.56], [w * 0.46, h * 0.5]], 52, `rgba(${GOBLIN},.13)`, 3);
  blob(ctx, [[w * 0.62, h * 0.44], [w * 0.78, h * 0.5], [w * 0.9, h * 0.44]], 50, `rgba(${BANDIT},.13)`, 7);
  reliefField(ctx, w * 0.28, h * 0.42, w * 0.14, h * 0.16, "vale", 4, 11);
  reliefField(ctx, w * 0.56, h * 0.62, w * 0.12, h * 0.14, "scree", 3, 19);
  reliefField(ctx, w * 0.84, h * 0.4, w * 0.12, h * 0.16, "lair", 3, 23);
  roads(ctx, list, pairs);
  nodes(ctx, list, true);
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(140,255,166,.9)";
  ctx.fillText("цвет = чья земля · знаки = какой рельеф · ободок = правда на узле", 20, h - 16);
};

/* ---------- подземелье: дыра в бездну ----------
   Правило Макса 2026-08-02: подземелье отделено от всего, фракции сверху на него НЕ влияют.
   Значит зона обязана обрываться на его краю, а сама дверь — не носить цвета ничьей земли. */

const drawVoid: DrawFn = (ctx, w, h) => {
  const cx = w * 0.56;
  const cy = h * 0.46;

  // Земля вокруг: зона фракции и её рельеф — они доходят до края дыры и обрываются.
  blob(ctx, [[w * 0.2, h * 0.4], [w * 0.34, h * 0.5], [w * 0.44, h * 0.42]], 46, `rgba(${GOBLIN},.13)`, 3);
  reliefField(ctx, w * 0.26, h * 0.44, w * 0.13, h * 0.16, "vale", 4, 11);

  // Сама дыра: чернота с мягкой кромкой, свет в неё уходит и не возвращается.
  const r = Math.min(w, h) * 0.24;
  const grad = ctx.createRadialGradient(cx, cy, r * 0.15, cx, cy, r);
  grad.addColorStop(0, "rgba(0,0,0,.95)");
  grad.addColorStop(0.55, "rgba(6,4,3,.9)");
  grad.addColorStop(1, "rgba(20,14,9,0)");
  ctx.fillStyle = grad;
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, Math.PI * 2);
  ctx.fill();

  // Кромка: рваная, дышит очень медленно — дыра живая, но не мигает.
  const breathe = 1 + Math.sin(tick * 0.02) * 0.015;
  ctx.strokeStyle = "rgba(184,134,59,.5)";
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  for (let i = 0; i <= 48; i++) {
    const a = (i / 48) * Math.PI * 2;
    const rr = r * 0.72 * breathe * (0.9 + jag(i % 48, 5) * 0.2);
    const px = cx + Math.cos(a) * rr;
    const py = cy + Math.sin(a) * rr * 0.94;
    if (i === 0) ctx.moveTo(px, py);
    else ctx.lineTo(px, py);
  }
  ctx.closePath();
  ctx.stroke();

  // Привратник у края — единственное, что на дыре читается.
  ctx.fillStyle = "rgba(255,204,51,.9)";
  ctx.beginPath();
  ctx.arc(cx - r * 0.86, cy + r * 0.2, 5, 0, Math.PI * 2);
  ctx.fill();

  // Дорога подходит и обрывается на кромке.
  ctx.strokeStyle = "rgba(147,128,94,.5)";
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  ctx.moveTo(w * 0.16, cy + 4);
  ctx.lineTo(cx - r * 0.9, cy + r * 0.2);
  ctx.stroke();
  nodes(ctx, [{ x: w * 0.16, y: cy + 4, zone: GOBLIN }], true);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("зона обрывается на кромке · дверь бесцветна · что внутри — не земля никого", 18, h - 14);
};

/* ---------- что уже в игре ---------- */

/** Лист пергамента с рваным краем: шум по периметру, поправка на соотношение сторон. */
const drawSheet: DrawFn = (ctx, w, h) => {
  const pad = 26;
  ctx.save();
  ctx.beginPath();
  const steps = 96;
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const per = t * 2 * (w - pad * 2 + h - pad * 2);
    let x: number;
    let y: number;
    const sideW = w - pad * 2;
    const sideH = h - pad * 2;
    if (per < sideW) { x = pad + per; y = pad; }
    else if (per < sideW + sideH) { x = w - pad; y = pad + (per - sideW); }
    else if (per < sideW * 2 + sideH) { x = w - pad - (per - sideW - sideH); y = h - pad; }
    else { x = pad; y = h - pad - (per - sideW * 2 - sideH); }
    const n = (jag(i, 3) - 0.5) * 7;
    const nx = x + (y === pad || y === h - pad ? 0 : n);
    const ny = y + (x === pad || x === w - pad ? 0 : n);
    if (i === 0) ctx.moveTo(nx, ny);
    else ctx.lineTo(nx, ny);
  }
  ctx.closePath();
  ctx.fillStyle = "rgba(232,214,178,.92)";
  ctx.fill();
  ctx.clip();

  // Потёртости: два слоя пятен разного масштаба.
  for (let i = 0; i < 26; i++) {
    const x = pad + jag(i, 21) * (w - pad * 2);
    const y = pad + jag(i, 22) * (h - pad * 2);
    ctx.fillStyle = `rgba(176,150,104,${(0.05 + jag(i, 23) * 0.07).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(x, y, 10 + jag(i, 24) * 26, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("map.sheet", 16, h - 10);
};

/** Стол под листом: тайл-узор и тёплое пятно света, углы тонут. */
const drawTable: DrawFn = (ctx, w, h) => {
  ctx.fillStyle = "rgba(38,28,20,1)";
  ctx.fillRect(0, 0, w, h);
  ctx.strokeStyle = "rgba(96,74,52,.5)";
  ctx.lineWidth = 1;
  for (let x = 0; x < w; x += 18) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, h);
    ctx.stroke();
  }
  const g = ctx.createRadialGradient(w * 0.5, h * 0.45, 10, w * 0.5, h * 0.45, Math.max(w, h) * 0.55);
  g.addColorStop(0, "rgba(255,214,150,.22)");
  g.addColorStop(1, "rgba(0,0,0,.55)");
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);

  ctx.fillStyle = "rgba(232,214,178,.9)";
  ctx.fillRect(w * 0.2, h * 0.24, w * 0.6, h * 0.5);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("map.table", 16, h - 10);
};

/** Шторка перехода: закрытие по текстуре, углы первыми, дизеринг Байера. */
const drawTransition: DrawFn = (ctx, w, h) => {
  const cycle = (tick % 60) / 60;                       // 2 секунды при 30 Гц
  const closing = cycle < 0.42 ? cycle / 0.42 : cycle < 0.55 ? 1 : 1 - (cycle - 0.55) / 0.45;

  const { list, pairs } = scene(w, h);
  roads(ctx, list, pairs);
  nodes(ctx, list, true);

  const cx = w / 2;
  const cy = h / 2;
  const maxR = Math.hypot(w, h) / 2;
  for (let y = 0; y < h; y += 4) {
    for (let x = 0; x < w; x += 4) {
      const d = Math.hypot(x - cx, y - cy) / maxR;      // углы дальше центра — закрываются первыми
      const noise = jag(Math.floor(x / 4) + Math.floor(y / 4) * 97, 9) * 0.3;
      if (d + noise > 1.25 - closing * 1.3) {
        ctx.fillStyle = "rgba(24,17,11,.96)";
        ctx.fillRect(x, y, 4, 4);
      }
    }
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("шаг по карте: выбор засчитывается на закрытом кадре", 16, h - 10);
};

/** Моргание доступных узлов: одна огибающая на размер и яркость. */
const drawPulse: DrawFn = (ctx, w, h) => {
  const k = 0.5 + 0.5 * Math.sin(tick * 0.09);
  const y = h * 0.46;
  const xs = [w * 0.28, w * 0.5, w * 0.72];

  xs.forEach((x, i) => {
    const live = i < 2;                                  // третий недоступен: он не дышит
    const r = 13 + (live ? k * 1.9 : 0);
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = 2.4;
    ctx.strokeStyle = live
      ? `rgba(${GOBLIN},${(0.6 + k * 0.4).toFixed(2)})`
      : "rgba(90,74,52,.5)";
    ctx.stroke();
  });

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("map.pulse · одна огибающая: два поля разъезжались", 16, h - 10);
};

/** Бегущая волна по дорожке: цепочка точек, яркость бежит от узла к узлу. */
const drawFlow: DrawFn = (ctx, w, h) => {
  const y = h * 0.46;
  const x0 = w * 0.2;
  const x1 = w * 0.8;
  const count = 14;
  const phase = (tick * 0.02) % 1;

  for (let i = 0; i < count; i++) {
    const t = i / (count - 1);
    const x = x0 + (x1 - x0) * t;
    const d = (t - phase + 1) % 1;
    const glow = Math.max(0, 1 - d * 4);
    ctx.beginPath();
    ctx.arc(x, y, 2.4 + glow * 1.6, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(255,204,51,${(0.28 + glow * 0.6).toFixed(2)})`;
    ctx.fill();
  }
  nodes(ctx, [{ x: x0 - 16, y, zone: GOBLIN }, { x: x1 + 16, y, zone: GOBLIN }], true, 9);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("map.pathflow · доли такта, не секунды", 16, h - 10);
};

/** Туман: выключен Максом, но живой — дымка густеет впереди и расходится за отрядом. */
const drawFog: DrawFn = (ctx, w, h) => {
  const { list, pairs } = scene(w, h);
  roads(ctx, list, pairs);
  nodes(ctx, list, true);

  for (let i = 0; i < 5; i++) {
    const t = (tick * 0.004 + i * 0.2) % 1;
    const x = w * 0.45 + i * w * 0.13;
    const a = 0.16 + Math.sin(t * Math.PI * 2) * 0.05;
    ctx.fillStyle = `rgba(226,214,190,${a.toFixed(3)})`;
    ctx.beginPath();
    ctx.ellipse(x, h * 0.46, w * 0.16, h * 0.34, 0, 0, Math.PI * 2);
    ctx.fill();
  }

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(255,96,80,.85)";
  ctx.fillText("map.fog · выключен: «не то, мб позже»", 16, h - 10);
};

/** Поездка фишки: выключена, шторка заменила её. */
const drawTravel: DrawFn = (ctx, w, h) => {
  const y = h * 0.46;
  const x0 = w * 0.22;
  const x1 = w * 0.78;
  const t = (tick % 90) / 90;
  const eased = t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;

  ctx.strokeStyle = "rgba(147,128,94,.4)";
  ctx.setLineDash([3, 5]);
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  ctx.moveTo(x0, y);
  ctx.lineTo(x1, y);
  ctx.stroke();
  ctx.setLineDash([]);

  nodes(ctx, [{ x: x0, y, zone: GOBLIN }, { x: x1, y, zone: GOBLIN }], true, 9);

  const px = x0 + (x1 - x0) * eased;
  ctx.beginPath();
  ctx.arc(px, y, 6, 0, Math.PI * 2);
  ctx.fillStyle = COL.honey;
  ctx.fill();

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(255,96,80,.85)";
  ctx.fillText("map.travel · выключена, заменена шторкой", 16, h - 10);
};

/* ---------- сборка ---------- */

const LIVE: StandDef[] = [
  { id: "sheet", status: "accepted", title: "Лист пергамента", tag: "в игре", note: "Обтягивает граф с полями; рваный край считается в шейдере по периметру с поправкой на соотношение сторон — иначе рваность вытягивается вдоль длинной стороны.", size: [320, 260], draw: drawSheet },
  { id: "table", status: "accepted", title: "Стол под листом", tag: "в игре", note: "Карта лежит в тёплом пятне света, углы тонут. Край стола в кадре — баг: держим запас по краям.", size: [320, 260], draw: drawTable },
  { id: "transition", status: "accepted", title: "Шторка перехода", tag: "в игре", note: "Закрытие по текстуре, углы первыми, дизеринг Байера. Выбор засчитывается на закрытом кадре — подмены не видно.", size: [320, 260], draw: drawTransition },
  { id: "pulse", status: "accepted", title: "Моргание узлов", tag: "в игре", note: "Доступные узлы дышат размером и яркостью по ОДНОЙ огибающей: пока полей было два, они сходились раз в два такта и выглядели рассинхроном.", size: [320, 260], draw: drawPulse },
  { id: "flow", status: "accepted", title: "Волна по дорожкам", tag: "в игре", note: "Точки по длине дуги, яркость бежит от узла к узлу. Считается в долях такта общего метронома, а не в секундах.", size: [320, 260], draw: drawFlow },
  { id: "fog", status: "rejected", title: "Туман карты", tag: "выключен", note: "Дымка густеет впереди и расходится за отрядом; растворение дизерингом. Узлы оставались видимыми — туман был атмосферой, не механикой.", verdict: "Выключен Максом: «не то, мб позже». Не удалён, живёт за тумблером map.fog.", size: [320, 260], draw: drawFog },
  { id: "travel", status: "rejected", title: "Поездка фишки", tag: "выключена", note: "Фишка-шлем ехала по дорожке около полутора секунд.", verdict: "Заменена шторкой: шаг стал быстрее и чище, а подмена узла спряталась в закрытый кадр. Тумблер map.travel.", size: [320, 260], draw: drawTravel }
];

const RELIEF_STANDS: StandDef[] = RELIEFS.map((card) => ({
  id: `relief-${card.kind}`,
  status: "waiting",
  title: card.title,
  tag: card.en,
  note: card.note,
  size: [320, 260],
  draw: reliefStand(card)
}));

const section: SectionDef = {
  id: "map-feel",
  title: "Подача",
  eyebrow: "Карта акта",
  lede:
    "Что уже нарисовано в игре, чем показываем рельеф и как на одном листе уживаются два слоя: " +
    "чья это земля и какая это местность.",
  blocks: [
    {
      kind: "head",
      id: "live",
      title: "Что уже в игре",
      lede: "Живьём, а не списком: у каждого эффекта свой тумблер, выключается всё, что включается."
    },
    { kind: "stands", items: LIVE },
    {
      kind: "note",
      html:
        "Восьмой эффект — локальная постобработка карты (<code>post.map</code>): свой Volume, арену " +
        "он не накрывает. Показывать его отдельной карточкой нечего — он виден на всех остальных."
    },
    {
      kind: "head",
      id: "channels",
      title: "Один канал — один владелец",
      lede:
        "Задача ровно та, которую бумажная картография решила давно: физическая карта несёт рельеф " +
        "штриховкой и значками, политическая — принадлежность заливкой. Веками на одном листе и не " +
        "спорят, потому что заняли разные каналы восприятия."
    },
    {
      kind: "table",
      head: ["Канал", "Владелец", "Почему он"],
      rows: [
        ["Оттенок (цвет)", "фракция зоны", "единственное, что читается боковым зрением на всей площади"],
        ["Знаки и штриховка (ахроматика)", "рельеф области", "другая размерность — с цветом не конкурирует"],
        ["Форма графа и рисунок дорог", "рельеф области", "осыпь видна диагоналями, гребень — параллелями"],
        ["Яркость и насыщенность", "состояние узла", "доступен, пройден, недоступен — только светлотой"],
        ["Иконка внутри узла", "тип узла", "бой, лавка, «?»"],
        ["Ободок узла", "принадлежность зоне", "правда, не зависящая от того, видно ли пятно"],
        ["Толщина и фактура линии", "вид дороги", "тракт, тропа, привратник, заслон"]
      ]
    },
    {
      kind: "split",
      items: [
        {
          id: "clash",
          status: "rejected",
          title: "Оба слоя пятнами",
          tag: "как не надо",
          note: "Самый естественный первый ход — дать рельефу тоже своё пятно. Через минуту на листе четыре перекрывающихся заливки, и ни одна не читается.",
          verdict: "Отклонено на бумаге, до кода: два цветных слоя в одном канале не расходятся никакими настройками прозрачности.",
          size: [620, 330],
          draw: drawClash
        },
        {
          id: "layers",
          status: "waiting",
          title: "Разными каналами",
          tag: "предложение",
          note: "Зона — бледный цвет снизу. Рельеф — ахроматические знаки поверх. Принадлежность — ободок на узле, который виден при любом зуме и не зависит от пятна.",
          facts: [["цвет", "фракция"], ["знаки", "рельеф"], ["ободок", "правда на узле"]],
          verdict: "Оба слоя читаются одновременно, потому что физически не конкурируют.",
          size: [620, 330],
          draw: drawLayers
        }
      ]
    },
    {
      kind: "head",
      id: "glyphs",
      title: "Знак рельефа и вид области",
      lede:
        "На каждый рельеф — свой картографический знак и то, как область выглядит целиком: узлы, " +
        "дороги своей топологии и россыпь знаков поверх зоны. Тумблер общий — map.relief."
    },
    { kind: "stands", items: RELIEF_STANDS },
    {
      kind: "note",
      html:
        "Знак — <b>картографический</b>, а не иллюстрация: несколько крупных штрихов, читаемых на " +
        "общем зуме. Рисуется процедурно тем же шейдером, что и пятно зоны, спрайты не заводим. " +
        "Плотность россыпи одна на все рельефы — иначе густота начнёт спорить с плотностью узлов."
    },
    {
      kind: "head",
      id: "void",
      title: "Подземелье — дыра в бездну",
      lede: "Правило Макса: подземелье отделено от всего, фракции сверху на него не влияют."
    },
    {
      kind: "split",
      items: [
        {
          id: "dungeon-void",
          status: "waiting",
          title: "Зона обрывается на кромке",
          note: "Земля вокруг чья-то: у неё цвет фракции и свои знаки рельефа. Дыра — ничья: пятно доходит до кромки и обрывается, дверь не носит цвета, внутри своя жизнь. Кромка дышит очень медленно — дыра живая, но не мигает.",
          facts: [["цвет зоны", "не заходит внутрь"], ["знаки рельефа", "обрываются на кромке"], ["привратник", "единственная метка на краю"]],
          verdict: "Так подземелье читается как посторонний объект на карте, а не как ещё одна область чьей-то земли.",
          size: [620, 330],
          draw: drawVoid
        }
      ]
    },
    {
      kind: "head",
      id: "planned",
      title: "Что запланировано",
      lede: "Владелец списка — gdd/70-gamefeel/map-presentation.md; здесь указатель."
    },
    {
      kind: "table",
      head: ["Эффект", "Зачем"],
      rows: [
        ["Пятно зоны: SDF-метабол + domain warp", "органический островной край; печётся в RT один раз на акт"],
        ["Знаки рельефа", "второй слой, не спорящий с цветом; тумблер map.relief"],
        ["Ободок зоны на узле", "принадлежность, которой можно верить"],
        ["Картуш с именем зоны", "«Молниеносные Гоблины» читается сразу"],
        ["Карточка области при наведении", "имя, строка образа, строка правила"],
        ["Grand Line", "тёплая сквозная линия и три отметки наград"],
        ["Дыра подземелья и привратник", "риск виден, начинка нет"],
        ["Виды дорог линией", "тракт, тропа, привратник, заслон, вслепую, режущая"],
        ["Раскрытие зоны при входе", "чернила расползаются от узла"],
        ["Шов между областями", "разделение без второго пятна"],
        ["Грейдинг и ambient-партиклы под зону", "воздух местности"],
        ["Звук карты", "шелест бумаги, перо"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> сколько цветов фракций карта выдержит одновременно — думаю, три, дальше " +
        "оттенки на бледной заливке перестают различаться."
    }
  ]
};

export default section;
