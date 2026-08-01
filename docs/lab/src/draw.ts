/* Общие рисовалки: земля, силуэт юнита, тело со статусами, детерминированный шум, палитра.
   Один владелец на все разделы — иначе тело юнита разойдётся между стендами, и сравнивать
   эффекты станет нельзя. Специфика раздела живёт в его собственном файле. */

import { tick } from "./clock.js";

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

export const SHIELD = "138,206,255";

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
