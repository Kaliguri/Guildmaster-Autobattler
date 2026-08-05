/* Лайн у эффектов: нужна ли форме удара тёмная кайма и какая именно.
   Развилка без вердикта. Канон, который она может тронуть, — docs/wiki/gdd/70-gamefeel/vfx-language.md
   §«Форму делает шейдер, а не спрайт» (разведение осей: юнит держит форму, эффект держит яркость).

   Повод: арт персонажей ушёл в плоский сторибук с ТОЛСТЫМ ЛАЙНАРТОМ, а эффект остался бесконтурным
   облаком света. Разведение осей от этого не сломалось, но сдвинулось: раньше противостояли пиксель
   и гладкий свет, теперь — обведённая фигура и необведённое свечение.

   Геометрия формы здесь СВОЯ, а не взятая из раздела «Удар», и это осознанно: там серп рисуется
   тремя вложенными полосами без общего пути, а кайме нужен ровно тот же контур, что у заливки, иначе
   сравнивались бы две разные формы вместо двух способов их обвести. Числа взяты оттуда же
   (полудлина 0.7 H, прогиб 0.2 H, полутолщина 0.055 H), чтобы форма осталась узнаваемой. */

import { frame } from "../clock.js";
import { COL, drawUnit, ground, jag, miniLabel } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- тайминг ----------
   Тот же, что у формы в разделе «Удар»: она начинается НА кадре контакта и живёт 4-5 кадров.
   Растягивать ради разглядывания нельзя — вопрос ровно в том, успевает ли кайма прочитаться. */

const CONTACT = 16;
const LIFE = 5;

/* ---------- цвета ----------
   Кайма тёмная, но не обязательно чёрная: тёмный оттенок СВОЕГО цвета сохраняет канал «чем ударили».
   Множитель один на все стихии — иначе кайма станет вторым владельцем цвета мимо палитры юнита. */

const RIM_K = 0.2;

/** Тёмная версия цвета формы: та же тройка, приглушённая. */
function darken(rgb: string): string {
  return rgb
    .split(",")
    .map((c) => Math.round(Number(c) * RIM_K))
    .join(",");
}

const BLACK = "0,0,0";

/** Стихии для свалки: разные цвета нужны, чтобы стало видно, теряет ли чёрная кайма различие. */
const ELEMENTS = ["77,242,255", "255,146,48", "132,214,92", "196,140,255"] as const;

/* ---------- форма ----------
   Линза из двух квадратичных кривых: A — начало взмаха, E — конец проросшей формы, прогиб уходит
   в нормаль к хорде. Один путь обслуживает и заливку, и кайму — в этом весь смысл стенда. */

interface Lens {
  ax: number; ay: number;
  ex: number; ey: number;
  bow: number;
  thick: number;
}

/** Строит контур линзы. `swell` раздувает толщину пропорционально (нажим кисти), `pad` — на
 *  постоянную величину во все стороны, включая продольную (ровная обводка по контуру).
 *
 *  Разница между ними и есть предмет спора: `pad` двигает границу везде одинаково и потому ТУПИТ
 *  острия, `swell` растёт вместе с толщиной формы и на остриях сходит в ноль. */
function lensPath(ctx: CanvasRenderingContext2D, L: Lens, swell = 0, pad = 0): void {
  const dx = L.ex - L.ax;
  const dy = L.ey - L.ay;
  const len = Math.max(1e-3, Math.hypot(dx, dy));
  const ux = dx / len;
  const uy = dy / len;
  const nx = -uy;
  const ny = ux;

  // Продольное удлинение живёт только у ровной обводки: контур отступает и вдоль оси тоже.
  const ax = L.ax - ux * pad;
  const ay = L.ay - uy * pad;
  const ex = L.ex + ux * pad;
  const ey = L.ey + uy * pad;

  const mx = (ax + ex) / 2;
  const my = (ay + ey) / 2;
  const cx = mx + nx * L.bow;
  const cy = my + ny * L.bow;
  const t = L.thick * (1 + swell) + pad;

  ctx.beginPath();
  ctx.moveTo(ax, ay);
  ctx.quadraticCurveTo(cx + nx * t, cy + ny * t, ex, ey);
  ctx.quadraticCurveTo(cx - nx * t, cy - ny * t, ax, ay);
  ctx.closePath();
}

export type RimKind =
  /** Как сейчас: аддитивное свечение без границы. */
  | "none"
  /** Чёрная обводка постоянной ширины. */
  | "black"
  /** Тёмный оттенок своего цвета, ширина постоянная. */
  | "tinted"
  /** Тёмный оттенок своего цвета, ширина пропорциональна толщине формы — нажим кисти. */
  | "pressure";

/** Кайма рисуется ПОД свечением и в обычном режиме наложения: аддитив темнеть не умеет в принципе,
 *  и это ровно та правка шейдера, которой требует приём (Blend One One → premultiplied alpha). */
function drawRim(ctx: CanvasRenderingContext2D, L: Lens, kind: RimKind, rgb: string, w: number, alpha: number): void {
  if (kind === "none") return;
  ctx.save();
  ctx.globalCompositeOperation = "source-over";
  if (kind === "pressure") {
    lensPath(ctx, L, w / Math.max(1e-3, L.thick), 0);
  } else {
    lensPath(ctx, L, 0, w);
  }
  ctx.fillStyle = `rgba(${kind === "black" ? BLACK : darken(rgb)},${alpha.toFixed(3)})`;
  ctx.fill();
  ctx.restore();
}

/** Свечение: три вложенные полосы, ядро белым пересветом. Аддитив — как в движке. */
function drawGlow(ctx: CanvasRenderingContext2D, L: Lens, rgb: string, fade: number): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const bands: Array<[number, number, string]> = [
    [0, 0.4, rgb],
    [-0.18, 0.55, "160,250,255"],
    [-0.45, 0.92, "255,255,255"]
  ];
  for (const [swell, a, color] of bands) {
    lensPath(ctx, L, swell, 0);
    ctx.fillStyle = `rgba(${color},${(a * fade).toFixed(3)})`;
    ctx.fill();
  }
  ctx.restore();
}

/** Плоская заливка вместо свечения: две ступени сплошного цвета, никакого аддитива.
 *  Это язык самого арта — solid-цвета без нарисованных теней, — применённый к эффекту. */
function drawFlat(ctx: CanvasRenderingContext2D, L: Lens, rgb: string, fade: number): void {
  ctx.save();
  ctx.globalCompositeOperation = "source-over";
  lensPath(ctx, L, 0, 0);
  ctx.fillStyle = `rgba(${rgb},${(0.95 * fade).toFixed(3)})`;
  ctx.fill();
  lensPath(ctx, L, -0.45, 0);
  ctx.fillStyle = `rgba(255,255,255,${(0.95 * fade).toFixed(3)})`;
  ctx.fill();
  ctx.restore();
}

/* ---------- сцена ----------
   Плита пола нужна не для красоты. ПЕРВЫЙ прогон стенда шёл на голом фоне, и все четыре варианта
   выглядели одинаково — потому что тёмная кайма на тёмном фоне невидима физически. Это и есть
   главное свойство приёма: он живёт ровно там, где под ним светлое. Плита даёт форме площадь, на
   которой кайме есть что перекрывать, а тёмный верх сцены оставлен нарочно — на нём та же кайма
   пропадает, и это видно в том же кадре. */

function arena(ctx: CanvasRenderingContext2D, w: number, h: number, bottom: number): number {
  const y = h - bottom;
  const g = ctx.createLinearGradient(0, y - h * 0.42, 0, h);
  g.addColorStop(0, "rgba(122,101,68,0)");
  g.addColorStop(1, "rgba(122,101,68,.34)");
  ctx.fillStyle = g;
  ctx.fillRect(0, y - h * 0.42, w, h - (y - h * 0.42));
  return ground(ctx, w, h, bottom);
}

/** Толщина каймы в долях роста. Заметно больше, чем кажется нужным на глаз: в движке её ещё
 *  подъест bloom, растекающийся с ядра на контур. */
const RIM_W = 0.038;

/* ---------- одиночный удар ---------- */

/** Жизнь формы от кадра контакта: прорастание, затем угасание. Вне окна формы нет вовсе. */
function formPhase(offset = 0): { alive: boolean; grow: number; fade: number } {
  const f = frame - CONTACT - offset;
  if (f < 0 || f > LIFE) return { alive: false, grow: 0, fade: 0 };
  const t = f / LIFE;
  return { alive: true, grow: Math.min(1, t / 0.28), fade: t < 0.3 ? 1 : 1 - (t - 0.3) / 0.7 };
}

function lensAt(px: number, py: number, H: number, grow: number, salt: number): Lens {
  const ax = px - H * 0.5;
  const ay = py - H * 0.49;
  // Точка хита ЦЕНТРАЛЬНАЯ: клинок проходит навылет, форма продолжается за B на столько же.
  const tipX = ax + (px - ax) * 2;
  const tipY = ay + (py - ay) * 2;
  return {
    ax, ay,
    ex: ax + (tipX - ax) * grow,
    ey: ay + (tipY - ay) * grow,
    bow: H * (0.2 + jag(salt, 1) * 0.08),
    thick: H * (0.055 + jag(salt, 2) * 0.02)
  };
}

const RIM_LABEL: Record<RimKind, string> = {
  none: "без каймы",
  black: "чёрная кайма",
  tinted: "тёмно-цветная",
  pressure: "тёмно-цветная с нажимом"
};

function drawSingle(kind: RimKind): DrawFn {
  return (ctx, w, h) => {
    const groundY = arena(ctx, w, h, 74);
    const H = 200;
    const cx = w * 0.54;

    drawUnit(ctx, cx, groundY, H, true);

    const ph = formPhase();
    if (ph.alive) {
      const L = lensAt(cx - 14, groundY - H * 0.62, H, ph.grow, 3);
      drawRim(ctx, L, kind, ELEMENTS[0], H * RIM_W, 0.9 * ph.fade);
      drawGlow(ctx, L, ELEMENTS[0], ph.fade);
    }

    miniLabel(ctx, RIM_LABEL[kind]);
  };
}

/* ---------- свалка ----------
   Приём проверяется НЕ на одиночном ударе. Свет насыщается к белому и потому терпит наложение,
   темнота копится без предела — восемь тёмных контуров в одном месте складываются в грязь раньше,
   чем восемь свечений. Плюс цвета здесь разные: видно, теряет ли чёрная кайма «чем ударили». */

const CROWD = 8;

function drawCrowd(kind: RimKind): DrawFn {
  return (ctx, w, h) => {
    const groundY = arena(ctx, w, h, 62);
    const H = 132;

    // Пять тел вразнобой: формы должны ложиться поверх арта, иначе кайма не с чем спорить.
    for (let i = 0; i < 5; i++) {
      const x = w * (0.16 + i * 0.17);
      drawUnit(ctx, x, groundY - jag(i, 21) * 10, H * (0.92 + jag(i, 22) * 0.16), true);
    }

    for (let i = 0; i < CROWD; i++) {
      // Разброс стартов узкий НАМЕРЕННО: свалка обязана показывать худший случай, а не средний.
      // При широком разбросе одновременно живут три формы из восьми, и вопрос «копится ли темнота
      // в грязь» остаётся непроверенным — а именно ради него стенд и заведён.
      const offset = Math.round(-4 + jag(i, 31) * 5);
      const ph = formPhase(offset);
      if (!ph.alive) continue;

      const px = w * (0.2 + jag(i, 32) * 0.62);
      const py = groundY - H * (0.35 + jag(i, 33) * 0.55);
      const rgb = ELEMENTS[i % ELEMENTS.length] ?? ELEMENTS[0];
      const L = lensAt(px, py, H * (0.85 + jag(i, 34) * 0.4), ph.grow, i * 5 + 1);

      drawRim(ctx, L, kind, rgb, H * RIM_W, 0.9 * ph.fade);
      drawGlow(ctx, L, rgb, ph.fade);
    }

    miniLabel(ctx, `${RIM_LABEL[kind]} · восемь ударов`);
  };
}

/* ---------- побочный вопрос: не кайма, а сама подача ----------
   Проверять это надо раньше выбора каймы. Первая версия стенда сравнивала «мягкий край» с «жёстким»
   и не показала ничего: в шейдере край и так резкий (полтексела), а мягкость создаёт не размытие, а
   ГРАДИЕНТ ПОПЕРЁК — три вложенные полосы разной силы. Настоящая развилка поэтому другая: свет
   градиентом против плоской заливки, то есть языка самого арта. */

function drawFill(flat: boolean): DrawFn {
  return (ctx, w, h) => {
    const groundY = arena(ctx, w, h, 74);
    const H = 200;
    const cx = w * 0.54;

    drawUnit(ctx, cx, groundY, H, true);

    const ph = formPhase();
    if (ph.alive) {
      const L = lensAt(cx - 14, groundY - H * 0.62, H, ph.grow, 3);
      if (flat) {
        drawRim(ctx, L, "pressure", ELEMENTS[0], H * RIM_W, 0.92 * ph.fade);
        drawFlat(ctx, L, ELEMENTS[0], ph.fade);
      } else {
        drawGlow(ctx, L, ELEMENTS[0], ph.fade);
      }
    }

    miniLabel(ctx, flat ? "плоская заливка плюс кайма" : "свет градиентом — как сейчас");
  };
}

/* ---------- дуга за клинком ----------
   Второй кандидат на обводку и единственный, о ком спросили отдельно. Дуга устроена иначе, чем
   форма: это не знак, а СЛЕД — кольцевой сектор от плеча, у которого хвост гаснет по УГЛУ, показывая,
   где клинок был раньше. Отсюда развилка внутри развилки: контур либо держит полную силу и тогда
   обводит место, где свечения уже нет, либо гаснет вместе с хвостом и перестаёт быть лайном.

   Клип взмаха повторяет числами раздел «Удар» — иначе сравнивались бы две разные дуги. */

const WINDUP_START = 6;
const STRIKE_START = 13;
const STRIKE_END = 19;
const RECOVERY_END = 27;
const REST = -18;
const BACK = -145;
const FOLLOW = 55;

/** Гашение хвоста по углу: то же число, что в `CombatFeelConfig.SwingArcTailBias`. */
const TAIL_BIAS = 1.6;

const easeOut = (t: number): number => 1 - Math.pow(1 - t, 2);
const easeIn = (t: number): number => t * t;
const easeInOut = (t: number): number => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);

function armAngle(f: number): number {
  if (f < WINDUP_START) return REST;
  if (f < STRIKE_START) return REST + (BACK - REST) * easeOut((f - WINDUP_START) / (STRIKE_START - WINDUP_START));
  if (f <= STRIKE_END) return BACK + (FOLLOW - BACK) * easeIn((f - STRIKE_START) / (STRIKE_END - STRIKE_START));
  if (f <= RECOVERY_END) return FOLLOW + (REST - FOLLOW) * easeInOut((f - STRIKE_END) / (RECOVERY_END - STRIKE_END));
  return REST;
}

const rad = (deg: number): number => (deg * Math.PI) / 180;

function sectorPath(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, a0: number, a1: number, rIn: number, rOut: number
): void {
  ctx.beginPath();
  ctx.arc(x, y, rOut, a0, a1, false);
  ctx.arc(x, y, Math.max(1, rIn), a1, a0, true);
  ctx.closePath();
}

/** Гашение хвоста по УГЛУ одной заливкой.
 *
 *  Первая версия рисовала сектор сорока сегментами и получила швы: при аддитивном наложении места
 *  стыков складывались в светлые радиальные полосы, а без нахлёста между ними оставались тёмные щели.
 *  Конический градиент решает это по построению — путь один, значит стыков нет вовсе. */
function tailGradient(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, a0: number, a1: number,
  rgb: string, peak: number, fade: number
): CanvasGradient {
  const g = ctx.createConicGradient(a0, x, y);
  const frac = Math.min(0.999, Math.abs(a1 - a0) / (Math.PI * 2));
  const STOPS = 10;
  for (let i = 0; i <= STOPS; i++) {
    const t = i / STOPS;
    const k = Math.pow(t, TAIL_BIAS);
    g.addColorStop(t * frac, `rgba(${rgb},${(peak * k * fade).toFixed(3)})`);
  }
  // За сектором цвета быть не должно: путь его и так обрезает, но лишний стоп страхует от
  // подтекания на torцах при округлении долей.
  g.addColorStop(Math.min(1, frac + 0.0005), `rgba(${rgb},0)`);
  return g;
}

export type ArcOutline =
  /** Как сейчас: голый свет. */
  | "none"
  /** Контур полной силы по всему сектору. */
  | "solid"
  /** Контур гаснет вместе с хвостом. */
  | "faded";

function drawSwingArc(outline: ArcOutline): DrawFn {
  return (ctx, w, h) => {
    const groundY = arena(ctx, w, h, 56);
    const H = 168;
    const cx = w * 0.44;

    drawUnit(ctx, cx, groundY, H, true);

    const sx = cx;
    const sy = groundY - H + (H / 16) * 5.6;   // плечо
    const arm = H * 0.52;
    const rOut = arm * 1.04;
    const rIn = rOut * 0.4;                     // у плеча свечения нет: там рука, а не след
    const lw = H * RIM_W;

    const live = frame >= STRIKE_START && frame <= STRIKE_END + 2;
    if (live) {
      const cur = Math.min(frame, STRIKE_END);
      const a0 = rad(armAngle(STRIKE_START));
      const a1 = rad(armAngle(cur));
      const fade = frame > STRIKE_END ? 1 - (frame - STRIKE_END) / 2 : 1;

      // Контур — под свечением и в обычном наложении, как у формы удара.
      if (outline === "solid") {
        // Один путь целиком: заливка без швов, сила постоянна вдоль всего сектора.
        ctx.save();
        ctx.globalCompositeOperation = "source-over";
        const da = lw / rOut;
        sectorPath(ctx, sx, sy, a0 - da, a1 + da, rIn - lw, rOut + lw);
        ctx.fillStyle = `rgba(${BLACK},${(0.9 * fade).toFixed(3)})`;
        ctx.fill();
        ctx.restore();
      }

      if (outline === "faded") {
        ctx.save();
        ctx.globalCompositeOperation = "source-over";
        const da = lw / rOut;
        sectorPath(ctx, sx, sy, a0 - da, a1 + da, rIn - lw, rOut + lw);
        ctx.fillStyle = tailGradient(ctx, sx, sy, a0 - da, a1 + da, BLACK, 0.9, fade);
        ctx.fill();
        ctx.restore();
      }

      // Свечение: кольцо с угловым гашением плюс яркая кромка у внешнего края.
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      sectorPath(ctx, sx, sy, a0, a1, rIn, rOut);
      ctx.fillStyle = tailGradient(ctx, sx, sy, a0, a1, ELEMENTS[0], 0.26, fade);
      ctx.fill();
      sectorPath(ctx, sx, sy, a0, a1, rOut * 0.9, rOut);
      ctx.fillStyle = tailGradient(ctx, sx, sy, a0, a1, "220,250,255", 0.5, fade);
      ctx.fill();
      ctx.restore();
    }

    // Рука и клинок — поверх дуги: след принадлежит движению, а не наоборот.
    const a = rad(armAngle(frame));
    const hand = { x: sx + Math.cos(a) * arm * 0.42, y: sy + Math.sin(a) * arm * 0.42 };
    const tip = { x: sx + Math.cos(a) * arm, y: sy + Math.sin(a) * arm };
    ctx.lineCap = "round";
    ctx.strokeStyle = COL.bodyLit;
    ctx.lineWidth = 9;
    ctx.beginPath();
    ctx.moveTo(sx, sy);
    ctx.lineTo(hand.x, hand.y);
    ctx.stroke();
    ctx.strokeStyle = "#C9B591";
    ctx.lineWidth = 6;
    ctx.beginPath();
    ctx.moveTo(hand.x, hand.y);
    ctx.lineTo(tip.x, tip.y);
    ctx.stroke();

    miniLabel(ctx, outline === "none" ? "дуга без обводки — как сейчас"
      : outline === "solid" ? "обводка полной силы"
      : "обводка гаснет с хвостом");
  };
}

/* ---------- техническая иллюстрация: почему аддитивом нельзя ----------
   Не развилка, а факт про шейдер: Blend One One умеет только ПРИБАВЛЯТЬ свет. Тёмный цвет,
   прибавленный к кадру, кадр не темнит — он его чуть подсвечивает. */

function drawBlend(premultiplied: boolean): DrawFn {
  return (ctx, w, h) => {
    const groundY = arena(ctx, w, h, 74);
    const H = 200;
    const cx = w * 0.54;

    drawUnit(ctx, cx, groundY, H, true);

    const ph = formPhase();
    if (ph.alive) {
      const L = lensAt(cx - 14, groundY - H * 0.62, H, ph.grow, 3);
      if (premultiplied) {
        drawRim(ctx, L, "tinted", ELEMENTS[0], H * RIM_W, 0.9 * ph.fade);
      } else {
        // Та же кайма, поданная аддитивом: тёмный цвет прибавляется и почти ничего не меняет.
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        lensPath(ctx, L, 0, H * RIM_W);
        ctx.fillStyle = `rgba(${darken(ELEMENTS[0])},${(0.9 * ph.fade).toFixed(3)})`;
        ctx.fill();
        ctx.restore();
      }
      drawGlow(ctx, L, ELEMENTS[0], ph.fade);
    }

    miniLabel(ctx, premultiplied ? "premultiplied — кайма перекрывает" : "Blend One One — каймы нет");
  };
}

/* ---------- раздел ---------- */

const RIM_FACTS: Record<RimKind, Array<[string, string]>> = {
  none: [["граница", "нет"], ["цвет", "цел"], ["правка шейдера", "не нужна"]],
  black: [["граница", "постоянная"], ["цвет", "теряется на периферии"], ["острия", "тупятся"]],
  tinted: [["граница", "постоянная"], ["цвет", "цел"], ["острия", "тупятся"]],
  pressure: [["граница", "по толщине формы"], ["цвет", "цел"], ["острия", "сохранены"]]
};

const RIM_NOTE: Record<RimKind, string> = {
  none: "Как в движке сегодня. Аддитивное свечение, край растворяется в градиент.",
  black: "Классика манги и комикса. Работает мгновенно, но контур становится вторым владельцем цвета: глаз цепляет его раньше заливки.",
  tinted: "Тот же приём, но кайма — тёмный оттенок цвета формы. Канал «чем ударили» остаётся живым.",
  pressure: "Ширина каймы идёт по толщине формы: толще в середине, сходит в ноль на остриях. Это разница между «обвели фигуру» и «нарисовали от руки»."
};

/** Вердикт 05.08.2026: принята чёрная обводка постоянной ширины. */
const RIM_STATUS: Record<RimKind, StandDef["status"]> = {
  none: "note",
  black: "accepted",
  tinted: "rejected",
  pressure: "rejected"
};

const RIM_VERDICT: Partial<Record<RimKind, string>> = {
  black:
    "Выбрана как есть. В свалке обводка не складывается в грязь, а разделяет формы, которые без неё " +
    "сливались в белое пятно, — прогноз «темнота копится» не подтвердился.",
  tinted: "Проиграла чёрной: разница видна только в свалке, а цена та же.",
  pressure:
    "Нажим на чёрной обводке не рисовался и остаётся неопробованным: если ровная ширина затупит " +
    "острия сильнее, чем хочется, это одна ручка."
};

function rimStand(kind: RimKind, draw: DrawFn, size: [number, number], prefix = ""): StandDef {
  return {
    id: prefix + kind,
    status: RIM_STATUS[kind],
    tag: kind === "none" ? "как сейчас" : undefined,
    title: RIM_LABEL[kind],
    size,
    decision: kind === "black" ? "2026-08-05/3" : undefined,
    note: RIM_NOTE[kind],
    facts: RIM_FACTS[kind],
    verdict: RIM_VERDICT[kind],
    draw
  };
}

const section: SectionDef = {
  id: "lineart",
  title: "Лайн у эффектов",
  lede:
    "Арт персонажей ушёл в плоский сторибук с толстым лайнартом, а эффект остался бесконтурным " +
    "облаком света. Здесь один и тот же серп обведён четырьмя способами — на одиночном ударе и в " +
    "свалке из восьми. Принята чёрная обводка постоянной ширины (05.08.2026).",
  blocks: [
    {
      kind: "text",
      cls: "lede",
      html:
        "Разведение осей («юнит держит форму, эффект держит яркость») от пивота арта не сломалось, но " +
        "сдвинулось: раньше противостояли пиксельная фигура и гладкий свет, теперь — <b>обведённая " +
        "фигура и необведённое свечение</b>. Вопрос стенда узкий: нужна ли форме удара граница, и если " +
        "да, то какая."
    },
    {
      kind: "note",
      html:
        "Читать стенд честно можно только там, где кайма лежит <b>поверх тела</b>: на пустом фоне " +
        "тёмный контур почти невидим — это не дефект рисовалки, а свойство приёма. Всё остальное " +
        "тёмное на сцене — тоже правда: именно с ним контур и будет конкурировать."
    },

    {
      kind: "head", id: "rims", title: "Четыре способа обвести форму",
      lede:
        "Тайминг настоящий: форма начинается на кадре контакта и живёт пять кадров. Растягивать его " +
        "ради разглядывания нельзя — вопрос ровно в том, успевает ли кайма прочитаться."
    },
    {
      kind: "stands",
      items: [
        rimStand("none", drawSingle("none"), [430, 320]),
        rimStand("black", drawSingle("black"), [430, 320]),
        rimStand("tinted", drawSingle("tinted"), [430, 320]),
        rimStand("pressure", drawSingle("pressure"), [430, 320])
      ]
    },
    {
      kind: "note",
      html:
        "Разница между «постоянной шириной» и «нажимом» видна на <b>остриях</b>: постоянная обводка " +
        "отступает от контура во все стороны, включая продольную, и потому притупляет оба конца — " +
        "росчерк становится похож на предмет. Нажим растёт вместе с толщиной формы и на остриях " +
        "сходит в ноль."
    },

    {
      kind: "head", id: "crowd", title: "Те же четыре в свалке из восьми ударов",
      lede:
        "Приём проверяется здесь, а не на одиночном ударе. Свет насыщается к белому и потому терпит " +
        "наложение; темнота копится без предела. Цвета у ударов разные — видно, теряет ли чёрная " +
        "кайма ответ на вопрос «чем ударили»."
    },
    {
      kind: "stands",
      items: [
        rimStand("none", drawCrowd("none"), [430, 300], "crowd-"),
        rimStand("black", drawCrowd("black"), [430, 300], "crowd-"),
        rimStand("tinted", drawCrowd("tinted"), [430, 300], "crowd-"),
        rimStand("pressure", drawCrowd("pressure"), [430, 300], "crowd-")
      ]
    },

    {
      kind: "head", id: "edge", title: "Побочный вопрос: свет или заливка",
      lede:
        "Первая версия этой пары сравнивала мягкий край с жёстким и не показала ничего: в шейдере " +
        "край и так резкий — полтексела, — а «мягкость» создаёт не размытие, а градиент поперёк из " +
        "трёх вложенных полос. Настоящая развилка поэтому другая: эффект остаётся светом или " +
        "переходит на язык самого арта — плоскую заливку."
    },
    {
      kind: "split",
      items: [
        {
          id: "edge-soft", status: "note", tag: "как сейчас", title: "Свет градиентом", size: [560, 360],
          note: "Три аддитивные полосы, ядро белым пересветом. Так рисует движок сегодня, и это осознанный выбор: свет живёт в измерении яркости и с формой арта не спорит.",
          draw: drawFill(false)
        },
        {
          id: "edge-hard", status: "waiting", title: "Плоская заливка плюс кайма", size: [560, 360],
          note: "Solid-цвет двумя ступенями, без аддитива вовсе, плюс кайма с нажимом. Это ровно язык персонажей, применённый к эффекту.",
          facts: [["читается", "как рисунок, не как свет"], ["bloom", "перестаёт работать"], ["риск", "спорит с артом за форму"]],
          verdict: "Крайняя точка шкалы: дальше этого приём заходить некуда. Полезен как граница разговора — если это перебор, значит правда где-то между ним и нынешним светом.",
          draw: drawFill(true)
        }
      ]
    },

    {
      kind: "head", id: "swing", title: "Дуга за клинком: тот же приём на следе, а не на знаке",
      lede:
        "Форма удара обводку получила (05.08.2026), дуга — нет, и вопрос открыт. Дуга устроена иначе: " +
        "это след, у которого хвост гаснет по углу и показывает, где клинок был раньше. Поэтому " +
        "вариантов здесь три, а не два — контур либо держит силу вдоль всего сектора, либо гаснет " +
        "вместе с ним."
    },
    {
      kind: "stands",
      items: [
        {
          id: "arc-none", status: "note", tag: "как сейчас", title: "Без обводки", size: [430, 340],
          note: "Голый свет: кольцо от плеча с угловым гашением хвоста. Так рисует движок сегодня.",
          draw: drawSwingArc("none")
        },
        {
          id: "arc-solid", status: "waiting", title: "Обводка полной силы", size: [430, 340],
          note: "Контур одинаков вдоль всего сектора. Знак читается, но контур держится и там, где свечение уже погасло.",
          facts: [["хвост", "обведён при нулевом свете"], ["читается", "как лента, а не след"]],
          draw: drawSwingArc("solid")
        },
        {
          id: "arc-faded", status: "waiting", title: "Обводка гаснет с хвостом", size: [430, 340],
          note: "Контур гаснет тем же законом, что свечение: он есть там, где есть свет, и исчезает вместе с ним.",
          facts: [["хвост", "без контура, и это честно"], ["передняя кромка", "получает чёткий край"], ["риск", "идёт на каждый взмах"]],
          verdict: "Вышло лучше моего прогноза: я ждала бесформенного тёмного ореола, а контур собрался у передней кромки и дал ей край, не тронув хвост.",
          draw: drawSwingArc("faded")
        }
      ]
    },
    {
      kind: "note",
      html:
        "Стенд подтвердил половину моего прогноза и опроверг вторую. <b>Полная сила — плохо:</b> " +
        "контур держится там, где свечения уже нет, и хвост читается чёрной тряпкой, а не следом. " +
        "<b>Гаснущий контур вышел лучше ожидаемого:</b> я ждала бесформенного тёмного ореола, а он " +
        "собрался у передней кромки — там, где свет и так ярче всего, — и дал ей край.<br><br>" +
        "Что остаётся против даже у него: дуга идёт на <b>каждый</b> взмах, включая промахи и " +
        "холостые, — это самый частый эффект боя, и постоянная тёмная линия у клинка утомляет " +
        "быстрее, чем знак, мелькающий на попадании.<br><br>" +
        "Если берём — работа та же, что у формы: premultiplied в <code>SH_Vfx_SwingArc</code> плюс " +
        "контур кольца по внешнему и внутреннему радиусу, гашение хвоста уже там есть."
    },

    {
      kind: "head", id: "blend", title: "Чем оплачивается кайма",
      lede:
        "Не развилка, а факт про шейдер. Оба наших VFX-шейдера стоят на Blend One One — чистом " +
        "аддитиве, который умеет только прибавлять свет. Тёмный цвет, прибавленный к кадру, кадр не " +
        "темнит."
    },
    {
      kind: "split",
      items: [
        {
          id: "blend-add", status: "note", title: "Аддитив: каймы не будет", size: [560, 360],
          note: "Кайма нарисована — и её нет. Вычитания в этом режиме не существует, поэтому любой тёмный цвет либо подсвечивает, либо не делает ничего.",
          draw: drawBlend(false)
        },
        {
          id: "blend-pma", status: "note", title: "Premultiplied: кайма перекрывает", size: [560, 360],
          note: "<code>Blend One OneMinusSrcAlpha</code>. Пиксель с нулевой альфой ведёт себя как чистый аддитив — свечение и bloom работают как работали; пиксель с альфой перекрывает то, что под ним.",
          verdict: "Правка блендинга нужна ровно один раз и одинаковая для обоих шейдеров. Сама кайма после этого стоит один smoothstep: расстояние до центральной линии и полутолщина в шейдере уже посчитаны.",
          draw: drawBlend(true)
        }
      ]
    },
    {
      kind: "note",
      html:
        "Две готчи, которые вылезут при переносе в движок. Первая: quad натянут точно на форму, " +
        "поэтому кайма наружу <b>обрежется краем меша</b> — параметру длины нужен запас. Вторая: bloom " +
        "с порогом 1.0 будет <b>растекаться на тонкий контур и подъедать его</b>, так что в игре лайн " +
        "придётся делать заметно толще, чем кажется правильным здесь.<br><br>" +
        "Рванность отдельной механики не требует: неровность краёв в шейдере формы уже есть и " +
        "детерминирована по сиду — кайма строится по той же маске и унаследует её даром."
    }
  ]
};

export default section;
