/* Сцены: кто из стендов сейчас на экране и кого надо рисовать.

   До роутера все стенды тикали каждый кадр, хотя видно два-три. Здесь два уровня отсечения:
   роутер держит в списке только открытый раздел, а IntersectionObserver внутри раздела гасит
   канвасы за краем экрана. При девятнадцати сценах барьера разница — порядок величины. */

import type { StandDef } from "./types.js";

interface Live {
  canvas: HTMLCanvasElement;
  stand: StandDef;
  w: number;
  h: number;
  visible: boolean;
  failed: boolean;
  /** Ширина в CSS-пикселях, на которую канвас настроен сейчас. */
  cssW: number;
}

const live: Live[] = [];
const records = new WeakMap<Element, Live>();

const io =
  "IntersectionObserver" in window
    ? new IntersectionObserver(
        (entries) => {
          for (const entry of entries) {
            const rec = records.get(entry.target);
            if (rec) rec.visible = entry.isIntersecting;
          }
        },
        // Запас в экран: стенд успевает нарисоваться до того, как игрок до него доскроллит,
        // иначе первый кадр после появления выглядит как подвисание.
        { rootMargin: "200px 0px" }
      )
    : null;

export function watch(canvas: HTMLCanvasElement, stand: StandDef, w: number, h: number): void {
  // Видимым СЧИТАЕТСЯ, пока наблюдатель не сказал обратного. Отказ обязан деградировать в
  // «рисуем лишнее», а не в «не рисуем ничего»: пустой стенд читается как поломка сайта, а
  // лишняя отрисовка — только как расход кадра. Браузер без IntersectionObserver попадает
  // ровно сюда и просто рисует всё.
  const rec: Live = { canvas, stand, w, h, visible: true, failed: false, cssW: 0 };
  records.set(canvas, rec);
  live.push(rec);
  io?.observe(canvas);
}

/** Сцена вне потока страницы: лайтбокс. Наблюдателем не проверяется — она заведомо на экране,
 *  раз её открыли, и гасить её нечему. */
export function register(canvas: HTMLCanvasElement, stand: StandDef, w: number, h: number): void {
  const rec: Live = { canvas, stand, w, h, visible: true, failed: false, cssW: 0 };
  records.set(canvas, rec);
  live.push(rec);
}

export function unregister(canvas: HTMLCanvasElement): void {
  const at = live.findIndex((rec) => rec.canvas === canvas);
  if (at < 0) return;
  io?.unobserve(canvas);
  live.splice(at, 1);
  records.delete(canvas);
}

/** Смена маршрута: старые сцены больше не существуют. */
export function reset(): void {
  for (const rec of live) io?.unobserve(rec.canvas);
  live.length = 0;
}

/** Пересчитать масштаб при смене размера окна. */
export function invalidateSizes(): void {
  for (const rec of live) rec.cssW = 0;
}

export function paint(): void {
  for (const rec of live) {
    if (!rec.visible || !rec.stand.draw) continue;
    const ctx = prepare(rec);
    if (!ctx) continue;
    try {
      rec.stand.draw(ctx, rec.w, rec.h);
    } catch (err) {
      // Падение одной рисовалки не имеет права гасить сайт: раньше именно так тридцать сцен
      // молча пропадали из-за одной строки в тридцать первой. Ругаемся один раз, не каждый кадр.
      if (!rec.failed) {
        rec.failed = true;
        console.error(`Стенд «${rec.stand.id}» упал при отрисовке:`, err);
      }
    }
  }
}

/** Канвас держит логический размер W×H, а на экране растягивается по ширине контейнера.
 *  Пересчёт кэшируется: без него каждый кадр дёргал бы вёрстку. */
function prepare(rec: Live): CanvasRenderingContext2D | null {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const cssW = rec.canvas.clientWidth || rec.w;
  const scale = cssW / rec.w;
  if (rec.cssW !== cssW) {
    rec.canvas.style.height = `${rec.h * scale}px`;
    rec.canvas.width = Math.round(cssW * dpr);
    rec.canvas.height = Math.round(rec.h * scale * dpr);
    rec.cssW = cssW;
  }
  const ctx = rec.canvas.getContext("2d");
  if (!ctx) return null;
  const k = rec.canvas.width / rec.w;
  ctx.setTransform(k, 0, 0, k, 0, 0);
  ctx.clearRect(0, 0, rec.w, rec.h);
  return ctx;
}
