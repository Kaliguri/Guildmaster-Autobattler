/* Общие рисовалки: земля, силуэт юнита, тело со статусами, детерминированный шум, палитра.
   Один владелец на все разделы — иначе тело юнита разойдётся между стендами, и сравнивать
   эффекты станет нельзя. Специфика раздела живёт в его собственном файле. */

import { tick } from "./clock.js";
import type { DrawFn } from "./types.js";

/* ---------- палитра ---------- */

export const COL = {
  white: "#FFFFFF",
  holo: "#4DF2FF",
  honey: "#FFCC33",
  mint: "#8CFFA6",
  brass: "#B8863B",
  muted: "#93805E",
  edge: "#3A2C1E",
  body: "#5A4A34",
  bodyLit: "#7A6544"
} as const;

/** Вскрытое — красный у всех юнитов: цвет раны не зависит от того, кто её нанёс. */
export const RED = "255,72,72";

/** Оттенки статусов: цвет говорит, ЧТО происходит. Палитра бьющего сюда не приходит — на теле
 *  цели важно состояние, а не автор (решение 2026-07-31/72). */
export const ST = {
  burn: "255,146,48",
  poison: "132,214,92",
  frost: "138,214,255",
  stun: "255,212,72",
  mark: "255,96,80",
  slow: "104,164,216"
} as const;

/* ---------- детерминированный шум ----------
   Math.random в стенде запрещён: два прогона обязаны выглядеть одинаково, иначе сравнение
   вариантов превращается в спор о том, что кому показалось. */

export function jag(i: number, salt = 0): number {
  const x = Math.sin((i + 1) * 12.9898 + salt * 78.233) * 43758.5453;
  return x - Math.floor(x);
}

/* ---------- сцена ----------
   Канвас, трансформ и очистку держит каркас: рисовалка получает готовый ctx и логический размер.
   Здесь остаётся то, что рисуется поверх. */

/** Обёртка для сцены, которая НЕ зависит от времени: рисуется один раз в свой канвас, дальше
 *  копируется целиком.
 *
 *  Каркас перерисовывает всё видимое каждый кадр — так надо анимациям удара и статусов. Но
 *  процедурный пол статичен, а стоит десятки тысяч операций на сцену: страница с двумя десятками
 *  таких стендов вешала браузер, и правка стенда переставала быть секундным делом.
 *
 *  Кэш свой у каждой обёрнутой рисовалки и живёт, пока жив её замыкание, — то есть ровно столько,
 *  сколько существует стенд. Ключ включает масштаб: смена окна или DPI обязана перерисовать, иначе
 *  снимок вышел бы мыльным. */
export function still(draw: DrawFn): DrawFn {
  let cache: HTMLCanvasElement | null = null;
  let cacheKey = "";
  return (ctx, w, h) => {
    const k = ctx.getTransform().a || 1;
    const key = `${w}x${h}@${k.toFixed(3)}`;
    if (!cache || cacheKey !== key) {
      const off = document.createElement("canvas");
      off.width = Math.max(1, Math.round(w * k));
      off.height = Math.max(1, Math.round(h * k));
      const octx = off.getContext("2d");
      // Отказ браузера дать второй контекст — внешний отказ, и деградировать он обязан в
      // «рисуем как раньше», а не в пустой стенд.
      if (!octx) {
        draw(ctx, w, h);
        return;
      }
      octx.setTransform(k, 0, 0, k, 0, 0);
      draw(octx, w, h);
      cache = off;
      cacheKey = key;
    }
    ctx.drawImage(cache, 0, 0, w, h);
  };
}

/** Линия земли. Отступ снизу у сцен разный, поэтому передаётся, а не угадывается. */
export function ground(ctx: CanvasRenderingContext2D, w: number, h: number, bottom = 56): number {
  const y = h - bottom;
  ctx.strokeStyle = "rgba(58,44,30,.7)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(30, y);
  ctx.lineTo(w - 30, y);
  ctx.stroke();
  return y;
}

export function miniLabel(ctx: CanvasRenderingContext2D, name: string): void {
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(name, 26, 30);
  ctx.fillStyle = COL.holo;
  ctx.fillRect(14, 21, 6, 6);
}

/* ---------- силуэт юнита ----------
   Путь нужен и для рисовки, и для обрезки эффекта «внутри тела»: материальное состояние
   не имеет права вылезать за силуэт. */

export function unitPath(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number): void {
  const u = h / 16;
  ctx.beginPath();
  ctx.rect(x - u * 2, groundY - h, u * 4, u * 4.5);                 // голова
  ctx.rect(x - u * 2.6, groundY - h + u * 4.5, u * 5.2, u * 6.5);   // корпус
  ctx.rect(x - u * 2.2, groundY - u * 5, u * 1.8, u * 5);           // нога
  ctx.rect(x + u * 0.5, groundY - u * 5, u * 1.8, u * 5);           // нога
}

/** Схематичный юнит блоками: намёк на пиксель-арт, без претензии на арт. */
export function drawUnit(
  ctx: CanvasRenderingContext2D,
  x: number,
  groundY: number,
  h: number,
  lit = false
): void {
  const u = h / 16;
  ctx.fillStyle = lit ? COL.bodyLit : COL.body;
  ctx.fillRect(x - u * 2, groundY - h, u * 4, u * 4.5);
  ctx.fillRect(x - u * 2.6, groundY - h + u * 4.5, u * 5.2, u * 6.5);
  ctx.fillRect(x - u * 2.2, groundY - u * 5, u * 1.8, u * 5);
  ctx.fillRect(x + u * 0.5, groundY - u * 5, u * 1.8, u * 5);
  ctx.fillStyle = "rgba(0,0,0,.35)";
  ctx.beginPath();
  ctx.ellipse(x, groundY + u * 0.6, u * 3.4, u * 1.1, 0, 0, Math.PI * 2);
  ctx.fill();
}

/** Материальное состояние тела: тинт, налёт, дрожь. */
export interface BodyState {
  stun?: boolean;
  /** Доля 0..1 — насыщенность тинта, а не число стаков. */
  burn?: number;
  poison?: number;
  /** Изморозь растёт снизу вверх: доля высоты тела. */
  frostbite?: number;
}

/** Тело со статусами. Подаётся ОДНИМ куском, как BodyVisualState в движке: тинт, налёт и дрожь
 *  приходят вместе, а не тремя независимыми писателями.
 *  Возвращает фактический X — стан тело трясёт, и всё, что цепляется к телу, трясётся вместе. */
export function statusBody(
  ctx: CanvasRenderingContext2D,
  x: number,
  groundY: number,
  h: number,
  o: BodyState = {}
): number {
  const u = h / 16;
  const bx = x + (o.stun ? (jag(tick, 41) - 0.5) * u * 0.5 : 0);

  drawUnit(ctx, bx, groundY, h);

  ctx.save();
  unitPath(ctx, bx, groundY, h);
  ctx.clip();

  if (o.burn) {
    ctx.fillStyle = `rgba(${ST.burn},${(0.08 + 0.24 * o.burn).toFixed(3)})`;
    ctx.fillRect(bx - u * 3, groundY - h, u * 6, h + u);
  }
  if (o.poison) {
    ctx.fillStyle = `rgba(${ST.poison},${(0.1 + 0.18 * o.poison).toFixed(3)})`;
    ctx.fillRect(bx - u * 3, groundY - h, u * 6, h + u);
  }
  if (o.frostbite) {
    const top = groundY - h * o.frostbite;
    ctx.fillStyle = `rgba(${ST.frost},.30)`;
    ctx.fillRect(bx - u * 3, top, u * 6, groundY - top);
    ctx.fillStyle = "rgba(214,244,255,.7)";
    ctx.fillRect(bx - u * 3, top, u * 6, 2);
  }
  ctx.restore();
  return bx;
}

/* ---------- потоки вокруг тела ----------
   Три движения, которыми пользуются и статусы, и зоны: штрихи (вверх — воля, вниз — гнёт),
   искры внутрь (лечение собирает) и пузыри (споры, яд, всё летучее). Живут здесь, а не в
   разделе, потому что их зовут двое: иначе одинаковый по смыслу поток разъедется в деталях. */

export function strokesFlow(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number,
  color: string, up: boolean, speed: number, count: number, len: number, alpha: number
): void {
  const u = h / 16;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  for (let i = 0; i < count; i++) {
    const ph = (tick * speed + jag(i, 91)) % 1;
    const px = x + (jag(i, 92) - 0.5) * u * 6;
    const span = h * 1.05;
    const py = up ? groundY - span * ph : groundY - span + span * ph;
    ctx.strokeStyle = `rgba(${color},${(Math.sin(ph * Math.PI) * alpha).toFixed(3)})`;
    ctx.lineWidth = 1.8;
    ctx.beginPath();
    ctx.moveTo(px, py);
    ctx.lineTo(px, py + (up ? len * u : -len * u));
    ctx.stroke();
  }
  ctx.restore();
}

/** Лечение СОБИРАЕТ: искры приходят извне к телу. `reaches` = false — гаснут на подлёте и
 *  осыпаются, это антихил «не доходит». */
export function healInward(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number,
  color: string, reaches: boolean
): void {
  const u = h / 16;
  const cy = groundY - h * 0.52;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < 9; i++) {
    const ph = (tick * 0.018 + jag(i, 151)) % 1;
    const a = jag(i, 152) * Math.PI * 2;
    const stop = reaches ? 0.16 : 0.52; // докуда добирается искра
    const r = 1.25 - (1.25 - stop) * ph;
    const px = x + Math.cos(a) * u * 5.6 * r;
    const py = cy + Math.sin(a) * h * 0.62 * r + (reaches ? 0 : u * 3 * Math.max(0, ph - 0.75) * 4);
    const k = reaches ? Math.sin(ph * Math.PI) : Math.max(0, 1 - Math.pow(ph / 0.8, 2));
    ctx.fillStyle = `rgba(${color},${(k * 0.85).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(px, py, 1.9 * (0.7 + 0.5 * k), 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

export function bubblesUp(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number,
  color: string, speed: number, count: number, r: number
): void {
  const u = h / 16;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < count; i++) {
    const ph = (tick * speed + jag(i, 101)) % 1;
    const px = x + (jag(i, 102) - 0.5) * u * 5 + Math.sin(ph * 6 + i) * u * 0.4;
    const py = groundY - u * 2 - h * 0.95 * ph;
    ctx.fillStyle = `rgba(${color},${(Math.sin(ph * Math.PI) * 0.85).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(px, py, r * (0.6 + jag(i, 103) * 0.8), 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

/* ---------- искры удара ----------
   Быстрые жёлтые (0.15 с) плюс медленные красные с тяжестью (0.4 с). Потолок числа обязателен:
   кит с огромным уроном не имеет права выдать стену частиц вместо читаемого удара. */

export function sparks(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, dirDeg: number,
  damageFrac: number, ageFrames: number, h: number, salt: number
): void {
  const count = Math.min(48, Math.round(damageFrac * 120) + 6);
  const fast = Math.round(count * 0.6);
  const dir = (dirDeg * Math.PI) / 180;

  for (let i = 0; i < count; i++) {
    const isFast = i < fast;
    const life = isFast ? 4.5 : 12; // 0.15 с и 0.4 с при 30 Гц
    if (ageFrames > life) continue;
    const k = 1 - ageFrames / life;
    const a = dir + (jag(i, salt) - 0.5) * (isFast ? 1.1 : 2.0);
    const speed = h * (isFast ? 0.055 : 0.025) * (0.6 + jag(i, salt + 3) * 0.8);
    const d = speed * ageFrames;
    const gravity = isFast ? 0 : h * 0.0022 * ageFrames * ageFrames;
    ctx.fillStyle = isFast
      ? `rgba(255,242,140,${(0.9 * k).toFixed(3)})`
      : `rgba(${RED},${(0.85 * k).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(x + Math.cos(a) * d, y + Math.sin(a) * d + gravity, (isFast ? 2.6 : 2.0) * k + 0.8, 0, Math.PI * 2);
    ctx.fill();
  }
}

/* ---------- тик и вспышка ----------
   Две вещи, которые обязаны НЕ путаться: тик эффекта — мягкая цветная волна вверх по силуэту,
   удар — резкая белая вспышка втрое короче. Живут рядом, чтобы разница держалась в одном месте. */

export const DOT_WAVE = 10;

export function dotWave(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number, color: string, age: number
): void {
  if (age < 0 || age > DOT_WAVE) return;
  const t = age / DOT_WAVE;
  const u = h / 16;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  unitPath(ctx, x, groundY, h);
  ctx.clip();
  const front = groundY - h * 1.15 * t; // фронт волны идёт снизу вверх
  const g = ctx.createLinearGradient(0, front + u * 3, 0, front - u * 3);
  g.addColorStop(0, `rgba(${color},0)`);
  g.addColorStop(0.5, `rgba(${color},${(0.55 * (1 - t * 0.5)).toFixed(3)})`);
  g.addColorStop(1, `rgba(${color},0)`);
  ctx.fillStyle = g;
  ctx.fillRect(x - u * 4, front - u * 3, u * 8, u * 6);
  ctx.restore();
}

export function hitFlash(
  ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number, age: number
): void {
  if (age < 0 || age > 3) return;
  const k = 1 - age / 3;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  unitPath(ctx, x, groundY, h);
  ctx.clip();
  ctx.fillStyle = `rgba(255,255,255,${(0.8 * k).toFixed(3)})`;
  ctx.fillRect(x - h / 4, groundY - h, h / 2, h);
  ctx.restore();
}

/* ---------- гранёный контур ----------
   Нужен ЗАМОРОЗКЕ: лёд гранёный. Барьер, наоборот, стал гладким эллипсом и живёт своей
   геометрией — эти две формы принципиально разные, и путать их нельзя. */

export function facetPoint(
  cx: number, cy: number, rx: number, ry: number, i: number, n: number
): { x: number; y: number; a: number } {
  const a = (i / n) * Math.PI * 2 - Math.PI / 2;
  return { x: cx + Math.cos(a) * rx, y: cy + Math.sin(a) * ry, a };
}

export function facetOutline(
  ctx: CanvasRenderingContext2D,
  cx: number, cy: number, rx: number, ry: number,
  color: string, alpha: number
): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.strokeStyle = `rgba(${color},${alpha})`;
  ctx.lineWidth = 1.6;
  ctx.beginPath();
  for (let i = 0; i <= 9; i++) {
    const p = facetPoint(cx, cy, rx, ry, i, 9);
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  }
  ctx.closePath();
  ctx.stroke();
  ctx.restore();
}
