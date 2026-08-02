/* Пол арены в языке «плоского сторибука».

   Заказ Макса 2026-08-02: арена «летает где-то над пустотой», игра прямо говорит «это просто боевая
   арена», плюс биомы и вид сбоку с текущего скрина.

   ПОЧЕМУ ЗДЕСЬ НЕТ НИ ОДНОГО ПИКСЕЛЯ. Первые версии стенда рисовали растр, дизеринг и тайлы 32 px —
   и это была моя ошибка на сутки позже пивота: решением `2026-08-01/14` пиксель-арт как язык
   ПЕРСОНАЖЕЙ отменён, взят плоский сторибук (solid-цвета без нарисованных теней, толстый лайнарт,
   объём и свет отдаёт движок; референсы Wildermyth / Wildfrost / Cult of the Lamb). Макс поймал это
   вопросом «зачем нам вообще пиксель арт, мы же от него уходим».

   И это меняет ЦЕНУ процедуры, а не только её вид. В пикселе фактуру делает ручная работа —
   осмысленные кластеры на площади 32x32, — и машина там соревнуется с художником, проигрывая. В
   сторибуке фактуры нет вообще: стиль состоит из плоской формы, её контура и света от движка, а это
   три математические вещи. Соревноваться не с кем: человек решает ЧТО (силуэт), машина — ЧЕМ.

   ВЫБОР МАКСА (02.08.2026), под него всё и настроено:
   контур — тёмный цветной (не чёрный: он душит цветной свет от эффектов, а у нас бой светится);
   форма плиты — строгий прямоугольник (совпадает с боевым полем один в один, площадь не теряется);
   гамма — приглушённая природная (бой читается как бой, но не спорит с латунным интерфейсом);
   плотность — скупая, центр пуст (в центре дерутся; композицию держит контраст пустого и занятого).

   Пропорции настоящие и от стиля не зависят — источники в GEOM. */

import { jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- настоящая геометрия ----------
   Ни одного числа с потолка: рядом с каждым — откуда оно взято. */

const GEOM = {
  /** Поле боя: ArenaLayoutAuthoring._boundsSize = (20, 12) мировых единиц. */
  arenaW: 20,
  arenaH: 12,
  /** Человек-эталон: _refHumanHeight / _refHumanWidth. */
  humanH: 1.7,
  humanW: 0.6
} as const;

/** Логический масштаб холста: 32 логических пикселя на мировую единицу. На игру не влияет —
 *  рендер у нас экранный, не pixel-perfect; это просто удобный масштаб стенда. */
const U = 32;
const ARENA_W = GEOM.arenaW * U;
const ARENA_H = GEOM.arenaH * U;
const HUMAN_H = GEOM.humanH * U;
const HUMAN_W = GEOM.humanW * U;

/** Толщина лайнарта, одна на всё: разнобой в толщине контура — первое, что выдаёт несобранный
 *  сторибук. Привязана к росту человека, а не к пикселям холста. */
const LINE = Math.max(2, Math.round(HUMAN_H * 0.055));

/* ---------- цвет ---------- */

type RGB = [number, number, number];

function rgb(c: RGB): string {
  return `rgb(${c[0] | 0},${c[1] | 0},${c[2] | 0})`;
}

/** Контур — не чёрный, а затемнённый тон САМОЙ заливки (выбор Макса). Лёгкий сдвиг в синеву не
 *  даёт тёмным местам уйти в грязно-коричневый. */
function ink(c: RGB, k = 0.42): RGB {
  return [c[0] * k, c[1] * k, c[2] * k + 6];
}

function lighten(c: RGB, k: number): RGB {
  return [c[0] + (255 - c[0]) * k, c[1] + (255 - c[1]) * k, c[2] + (255 - c[2]) * k];
}

/* ---------- шум ---------- */

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function hash2(x: number, y: number, salt: number): number {
  return jag(x * 374761 + y * 668265, salt);
}

function vnoise(x: number, y: number, salt: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const fx = x - xi;
  const fy = y - yi;
  const u = fx * fx * (3 - 2 * fx);
  const v = fy * fy * (3 - 2 * fy);
  return lerp(
    lerp(hash2(xi, yi, salt), hash2(xi + 1, yi, salt), u),
    lerp(hash2(xi, yi + 1, salt), hash2(xi + 1, yi + 1, salt), u),
    v
  );
}

/* ---------- биом ---------- */

interface Biome {
  id: string;
  name: string;
  /** Основа плиты — то, чем залито всё поле. */
  ground: RGB;
  /** Пятна второго и третьего материала: вытоптанное и твёрдое. */
  patchA: RGB;
  patchB: RGB;
  /** Цвет растительности и тип её силуэта. */
  plant: RGB;
  plantKind: "grass" | "shard" | "bone";
  rim: RGB;
  voidTop: string;
  voidBottom: string;
}

const MEADOW: Biome = {
  id: "meadow",
  name: "Поляна",
  ground: [110, 127, 74],
  patchA: [138, 118, 80],
  patchB: [126, 122, 112],
  plant: [88, 108, 58],
  plantKind: "grass",
  rim: [58, 54, 46],
  voidTop: "#171320",
  voidBottom: "#0C0A11"
};

const FOREST: Biome = {
  id: "forest",
  name: "Лес",
  ground: [78, 100, 66],
  patchA: [104, 88, 62],
  patchB: [96, 98, 92],
  plant: [52, 74, 46],
  plantKind: "grass",
  rim: [48, 48, 40],
  voidTop: "#101616",
  voidBottom: "#07090A"
};

const CAVE: Biome = {
  id: "cave",
  name: "Пещера",
  ground: [96, 92, 104],
  patchA: [78, 74, 88],
  patchB: [116, 112, 124],
  plant: [122, 152, 176],
  plantKind: "shard",
  rim: [52, 50, 60],
  voidTop: "#0D0B14",
  voidBottom: "#06050A"
};

const ASH: Biome = {
  id: "ash",
  name: "Пепелище",
  ground: [112, 98, 88],
  patchA: [92, 78, 72],
  patchB: [130, 118, 108],
  plant: [176, 162, 142],
  plantKind: "bone",
  rim: [56, 46, 42],
  voidTop: "#150F0D",
  voidBottom: "#090606"
};

/* ---------- формы ----------
   Всё рисуется кривыми и заливается плоско. Ни градиента внутри формы, ни нарисованной тени:
   объём в этом языке даёт движок, а не художник. */

/** Замкнутая органическая клякса: радиус гуляет шумом по углу. Эллипс читался бы как «пятно на
 *  скатерти», а гуляющий радиус даёт участок земли. */
function blobPath(ctx: CanvasRenderingContext2D, cx: number, cy: number, rx: number, ry: number, wobble: number, seed: number): void {
  const steps = 44;
  ctx.beginPath();
  for (let i = 0; i <= steps; i++) {
    const a = (i / steps) * Math.PI * 2;
    const n = vnoise(Math.cos(a) * 2 + 4, Math.sin(a) * 2 + 4, seed) - 0.5;
    const k = 1 + n * wobble;
    const x = cx + Math.cos(a) * rx * k;
    const y = cy + Math.sin(a) * ry * k;
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.closePath();
}

/** Заливка с лайнартом — единственный способ, которым здесь появляется форма. */
function fillInk(ctx: CanvasRenderingContext2D, color: RGB, line = LINE): void {
  ctx.fillStyle = rgb(color);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color));
  ctx.lineWidth = line;
  ctx.lineJoin = "round";
  ctx.lineCap = "round";
  ctx.stroke();
}

/** Пучок травы силуэтом: несколько дуг, обведённых тёмным и залитых светлым поверх. В пикселе
 *  такое пришлось бы рисовать руками, здесь — три кривые, и двести пучков выходят единообразными
 *  с честными вариациями. */
function grassTuft(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  const blades = 3 + Math.floor(jag(seed, 3) * 2);
  ctx.lineCap = "round";
  for (let pass = 0; pass < 2; pass++) {
    ctx.strokeStyle = pass === 0 ? rgb(ink(color, 0.5)) : rgb(color);
    for (let i = 0; i < blades; i++) {
      const dir = (jag(seed + i * 7, 11) - 0.5) * 1.7;
      const hgt = s * (0.7 + jag(seed + i * 13, 17) * 0.6);
      ctx.lineWidth = pass === 0 ? Math.max(2.2, s * 0.3) : Math.max(1, s * 0.13);
      ctx.beginPath();
      ctx.moveTo(x + dir * s * 0.2, y);
      ctx.quadraticCurveTo(x + dir * s * 0.5, y - hgt * 0.6, x + dir * s * 1.05, y - hgt);
      ctx.stroke();
    }
  }
}

/** Кристалл: угловатый силуэт под ту же кисть. */
function shardShape(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  const hh = s * (1.1 + jag(seed, 5) * 0.8);
  const ww = s * (0.5 + jag(seed, 9) * 0.3);
  ctx.beginPath();
  ctx.moveTo(x, y - hh);
  ctx.lineTo(x + ww, y - hh * 0.25);
  ctx.lineTo(x + ww * 0.4, y);
  ctx.lineTo(x - ww * 0.5, y);
  ctx.lineTo(x - ww * 0.8, y - hh * 0.35);
  ctx.closePath();
  fillInk(ctx, color, Math.max(1.4, LINE * 0.7));
}

/** Кость: скруглённая перемычка. */
function boneShape(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  const ww = s * (1.2 + jag(seed, 7) * 0.7);
  ctx.beginPath();
  ctx.moveTo(x - ww * 0.5, y);
  ctx.lineTo(x + ww * 0.5, y - s * 0.14);
  ctx.lineTo(x + ww * 0.5, y + s * 0.2);
  ctx.lineTo(x - ww * 0.5, y + s * 0.32);
  ctx.closePath();
  fillInk(ctx, color, Math.max(1.2, LINE * 0.6));
}

/* ---------- сцена ---------- */

interface SlabOpts {
  biome: Biome;
  /** Толщина борта в МИРОВЫХ единицах — сравнимо с ростом человека (1.7). */
  rimU: number;
  seed: number;
  unit?: boolean;
  /** Насыщенность растительностью: 1 — принятая скупая норма, выше — для сравнения. */
  density?: number;
  crop?: boolean;
  /** Показать, как то же место выглядело в отменённом пиксельном языке. */
  pixelLegacy?: boolean;
}

/** Борт: плоская полоса со светлой кромкой сверху. Никакой неоновой клетки — канон
 *  arena-digital-swap: цифра у нас язык ПЕРЕХОДА, а не состояния. */
function drawRim(ctx: CanvasRenderingContext2D, b: Biome, x0: number, y: number, w: number, hh: number): void {
  ctx.fillStyle = rgb(lighten(b.rim, 0.34));
  ctx.fillRect(x0, y - LINE * 0.6, w, LINE * 0.6);
  ctx.fillStyle = rgb(b.rim);
  ctx.fillRect(x0, y, w, hh);
  ctx.fillStyle = rgb(ink(b.rim, 0.62));
  ctx.fillRect(x0, y + hh * 0.55, w, hh * 0.45);
}

/** Юнит-заглушка в языке сторибука: плоский силуэт с тем же лайнартом. Не предложение по стилю
 *  персонажей — только мерка роста, без неё толщину борта сравнивать не с чем. */
function unitFigure(ctx: CanvasRenderingContext2D, x: number, groundY: number, lit: boolean): void {
  const hh = HUMAN_H;
  const ww = HUMAN_W;
  const body: RGB = lit ? [150, 116, 96] : [116, 92, 80];

  // Тень — плоский эллипс, а не размытое пятно: в этом языке тень такая же форма, как всё остальное.
  ctx.fillStyle = "rgba(28,24,20,.34)";
  ctx.beginPath();
  ctx.ellipse(x, groundY, ww * 0.95, ww * 0.34, 0, 0, Math.PI * 2);
  ctx.fill();

  ctx.beginPath();
  ctx.moveTo(x - ww * 0.55, groundY);
  ctx.lineTo(x - ww * 0.62, groundY - hh * 0.62);
  ctx.quadraticCurveTo(x, groundY - hh * 0.78, x + ww * 0.62, groundY - hh * 0.62);
  ctx.lineTo(x + ww * 0.55, groundY);
  ctx.closePath();
  fillInk(ctx, body, LINE * 0.8);

  ctx.beginPath();
  ctx.ellipse(x, groundY - hh * 0.82, ww * 0.52, hh * 0.14, 0, 0, Math.PI * 2);
  fillInk(ctx, lighten(body, 0.12), LINE * 0.8);
}

function slab(o: SlabOpts): DrawFn {
  return (ctx, w, h) => {
    const b = o.biome;
    const dens = o.density ?? 1;

    const vg = ctx.createLinearGradient(0, 0, 0, h);
    vg.addColorStop(0, b.voidTop);
    vg.addColorStop(1, b.voidBottom);
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    const rimH = o.rimU * U;
    const pw = o.crop ? w : ARENA_W;
    const ph = o.crop ? h : ARENA_H;
    const x0 = o.crop ? 0 : Math.round((w - pw) / 2);
    const y0 = o.crop ? 0 : Math.round((h - ph - rimH) / 2);

    ctx.save();
    ctx.beginPath();
    ctx.rect(x0, y0, pw, ph);
    ctx.clip();

    if (o.pixelLegacy) {
      // Отменённый язык: растр с дизерингом. Оставлен ровно ради сравнения.
      for (let y = 0; y < ph; y += 3) {
        for (let x = 0; x < pw; x += 3) {
          const t = vnoise((x % 32) * 0.16, (y % 32) * 0.16, 1);
          const c = lerp(78, 132, t);
          ctx.fillStyle = `rgb(${c | 0},${(c * 1.1) | 0},${(c * 0.55) | 0})`;
          ctx.fillRect(x0 + x, y0 + y, 3, 3);
        }
      }
      ctx.restore();
      drawRim(ctx, b, x0, y0 + ph, pw, rimH);
      return;
    }

    // 1. Основа плиты — одна плоская заливка. Ни фактуры, ни тайла.
    ctx.fillStyle = rgb(b.ground);
    ctx.fillRect(x0, y0, pw, ph);

    // 2. Участки другого материала: вытоптанное и твёрдое. Крупные формы, а не рябь — место
    //    делает КОМПОЗИЦИЯ пятен, а не заполнение площади деталями.
    const patches: Array<[RGB, number, number, number, number, number]> = [
      [b.patchA, 0.26, 0.30, 0.20, 0.13, 1],
      [b.patchA, 0.78, 0.68, 0.16, 0.11, 2],
      [b.patchB, 0.60, 0.22, 0.11, 0.08, 3],
      [b.patchB, 0.16, 0.76, 0.09, 0.07, 4]
    ];
    for (const [col, fx, fy, frx, fry, s] of patches) {
      blobPath(ctx, x0 + pw * fx, y0 + ph * fy, pw * frx, ph * fry, 0.36, o.seed * 10 + s);
      fillInk(ctx, col);
    }

    // 3. Растительность силуэтами. Скупо и по краям: центр — там, где дерутся, и любая деталь в нём
    //    отнимает читаемость у юнитов и телеграфов (выбор Макса).
    const count = Math.round(46 * dens);
    for (let i = 0; i < count; i++) {
      const fx = jag(i * 3 + 1, o.seed + 31);
      const fy = jag(i * 3 + 2, o.seed + 33);
      const dx = (fx - 0.5) * 2;
      const dy = (fy - 0.5) * 2;
      const edge = Math.min(1, Math.sqrt(dx * dx + dy * dy) / 0.95);
      if (jag(i * 3 + 5, o.seed + 35) > Math.pow(edge, 2.2) * 1.15 * dens) continue;

      const px = x0 + fx * pw;
      const py = y0 + fy * ph;
      const s = HUMAN_H * (0.16 + jag(i, o.seed + 41) * 0.14);
      if (b.plantKind === "grass") grassTuft(ctx, px, py, s, o.seed + i * 17, b.plant);
      else if (b.plantKind === "shard") shardShape(ctx, px, py, s, o.seed + i * 17, b.plant);
      else boneShape(ctx, px, py, s, o.seed + i * 17, b.plant);
    }

    // 4. Свет — от движка, а не нарисованный: мягкая виньетка поверх плоских форм. Ровно то, что
    //    канон называет «объём и свет отдаёт движок».
    const vig = ctx.createRadialGradient(x0 + pw / 2, y0 + ph * 0.42, ph * 0.32, x0 + pw / 2, y0 + ph / 2, ph * 1.05);
    vig.addColorStop(0, "rgba(255,242,214,.07)");
    vig.addColorStop(1, "rgba(18,14,22,.42)");
    ctx.fillStyle = vig;
    ctx.fillRect(x0, y0, pw, ph);

    ctx.restore();

    if (o.unit) {
      unitFigure(ctx, x0 + pw * 0.34, y0 + ph * 0.74, true);
      unitFigure(ctx, x0 + pw * 0.60, y0 + ph * 0.46, false);
      unitFigure(ctx, x0 + pw * 0.70, y0 + ph * 0.66, false);
    }

    if (o.crop) return;

    drawRim(ctx, b, x0, y0 + ph, pw, rimH);

    // Контур всей плиты: в сторибуке край — такая же форма, как остальное.
    ctx.strokeStyle = rgb(ink(b.rim, 0.5));
    ctx.lineWidth = LINE;
    ctx.lineJoin = "miter";
    ctx.strokeRect(x0, y0, pw, ph + rimH);

    // Пыль под плитой: самый дешёвый сигнал «висит», сильнее самого борта.
    const dustTop = y0 + ph + rimH;
    for (let i = 0; i < 26; i++) {
      const px = x0 + jag(i * 3 + 1, o.seed + 61) * pw;
      const py = dustTop + jag(i * 3 + 2, o.seed + 63) * Math.max(h - dustTop, 1);
      const a = 0.3 * (1 - (py - dustTop) / Math.max(h - dustTop, 1));
      ctx.fillStyle = `rgba(198,178,132,${a.toFixed(3)})`;
      ctx.beginPath();
      ctx.arc(px, py, 1.4, 0, Math.PI * 2);
      ctx.fill();
    }
  };
}

/* ---------- стенды ---------- */

const FULL: [number, number] = [760, 560];

const LEGACY_STAND: StandDef = {
  id: "pixel-legacy",
  status: "rejected",
  title: "Пиксельная земля",
  tag: "отменено каноном",
  note: "Растр с дизерингом — то, что я рисовала до того, как свериться со стилем.",
  facts: [
    ["язык", "пиксель-арт"],
    ["отменён", "решение 2026-08-01/14"],
    ["конкурент", "художник, и он сильнее"]
  ],
  verdict:
    "Проигрывает дважды. По канону — пиксель как язык персонажей отменён ещё вчера. По существу — " +
    "в пикселе фактуру делает ручная работа, и машина там соревнуется с художником, а не помогает ему.",
  size: FULL,
  draw: slab({ biome: MEADOW, rimU: 0.75, seed: 4, pixelLegacy: true })
};

const MAIN_STAND: StandDef = {
  id: "storybook",
  status: "waiting",
  title: "Сторибук: плита в пустоте",
  tag: "по четырём выборам Макса",
  note: "Плоские заливки, тёмный цветной контур, объём от света. Ни одного растрового пикселя.",
  facts: [
    ["контур", "тёмный цветной"],
    ["форма", "строгий прямоугольник"],
    ["гамма", "приглушённая природная"],
    ["центр", "пуст"]
  ],
  verdict:
    "Здесь у процедуры нет конкурента: стиль состоит из формы, контура и света — трёх математических " +
    "вещей. Человек решает, ЧТО за силуэт; машина — где он лежит и как освещён.",
  size: FULL,
  draw: slab({ biome: MEADOW, rimU: 0.75, seed: 4, unit: true })
};

const RIM_STANDS: StandDef[] = [
  {
    id: "thin",
    status: "waiting",
    title: "Тонкая пластина",
    facts: [["борт", "0.4 ед"], ["к росту", "четверть человека"]],
    verdict: "Пластина, положенная в пустоту. Держит мысль «полигон, а не место».",
    size: FULL,
    draw: slab({ biome: MEADOW, rimU: 0.4, seed: 4, unit: true })
  },
  {
    id: "thick",
    status: "waiting",
    title: "Толстая плита",
    facts: [["борт", "1.5 ед"], ["к росту", "почти человек"]],
    verdict: "Остров: тянет обратно в «кусок мира», хотя борт и плоский.",
    size: FULL,
    draw: slab({ biome: MEADOW, rimU: 1.5, seed: 4, unit: true })
  }
];

const DENSITY_STANDS: StandDef[] = [
  {
    id: "sparse",
    status: "accepted",
    title: "Скупо",
    tag: "выбор Макса",
    facts: [["центр", "пуст"], ["жизнь", "по краям"]],
    verdict: "Юниты и телеграфы читаются идеально: в центре с ними ничто не спорит.",
    draw: slab({ biome: MEADOW, rimU: 0.75, seed: 7, crop: true })
  },
  {
    id: "rich",
    status: "rejected",
    title: "Богато",
    facts: [["центр", "занят"], ["риск", "читаемость боя"]],
    verdict: "Лучший скриншот и худший бой: в автобаттлере игрок считывает всё глазами и не может замедлиться.",
    draw: slab({ biome: MEADOW, rimU: 0.75, seed: 7, crop: true, density: 3.4 })
  }
];

const BIOME_STANDS: StandDef[] = [MEADOW, FOREST, CAVE, ASH].map((b) => ({
  id: b.id,
  status: "waiting" as const,
  title: b.name,
  tag: "тот же код, другой конфиг",
  facts: [["цветов", "6"], ["силуэт", b.plantKind]],
  draw: slab({ biome: b, rimU: 0.75, seed: 9, crop: true })
}));

const section: SectionDef = {
  id: "floor",
  title: "Пол арены",
  lede:
    "Плита, висящая в пустоте, в языке плоского сторибука: заливки, лайнарт, свет от движка. " +
    "Пропорции настоящие — из ArenaLayoutAuthoring.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "why",
      title: "Почему процедура здесь сильна, а в пикселе была слаба",
      lede: "Это не «то же самое другими красками» — меняется то, кто кому конкурент."
    },
    {
      kind: "table",
      head: ["", "Пиксель-арт", "Сторибук"],
      rows: [
        ["Из чего состоит стиль", "фактура: кластеры, палитра, растр", "форма, контур, свет"],
        ["Кто делает это лучше", "художник вручную", "формула"],
        ["Роль машины", "имитировать руку и проигрывать", "считать форму и свет"],
        ["Роль человека", "рисовать каждый тайл", "решать, ЧТО за силуэт"],
        ["Разрешение", "привязано к растру", "любое"]
      ]
    },
    { kind: "split", items: [LEGACY_STAND, MAIN_STAND] },
    {
      kind: "note",
      html:
        "Пиксельный стенд оставлен слева как <b>отклонённый</b>, а не удалён: он объясняет, почему " +
        "процедурная земля выглядела странно первые три захода. Язык персонажей сменился решением " +
        "<code>2026-08-01/14</code> — solid-цвета, толстый лайнарт, объём и свет отдаёт движок."
    },
    {
      kind: "head",
      id: "rim",
      title: "Толщина плиты",
      lede: "Форма — строгий прямоугольник (выбор Макса). Остаётся один вопрос: насколько толстый борт."
    },
    { kind: "split", items: RIM_STANDS },
    {
      kind: "head",
      id: "density",
      title: "Плотность: почему центр пуст",
      lede: "Решено скупо. Справа — то, от чего отказались, чтобы решение было видно."
    },
    { kind: "stands", items: DENSITY_STANDS },
    {
      kind: "head",
      id: "biomes",
      title: "Биом — это конфиг",
      lede: "Четыре фрагмента одним кодом. Отличаются шестью цветами и типом силуэта."
    },
    { kind: "stands", items: BIOME_STANDS },
    {
      kind: "head",
      id: "geom",
      title: "Настоящие пропорции",
      lede: "От стиля не зависят: числа вычитаны из проекта."
    },
    {
      kind: "table",
      head: ["Величина", "Значение", "Откуда"],
      rows: [
        ["Поле боя", "20 × 12 ед", "ArenaLayoutAuthoring._boundsSize"],
        ["Зона камеры", "26 × 16 ед", "ArenaLayoutAuthoring._cameraZoneSize"],
        ["Человек", "1.7 × 0.6 ед", "_refHumanHeight / _refHumanWidth"],
        ["Ростов в ширину арены", "около 12", "20 ÷ 1.7"],
        ["Толщина лайнарта", "одна на всё", "разнобой выдаёт несобранный сторибук"]
      ]
    },
    {
      kind: "note",
      html:
        "Зона камеры шире поля на <b>6 единиц по горизонтали и 4 по вертикали</b> — в этот запас " +
        "и уходит борт с пустотой под ним. Ручка для «плита висит» в данных уже есть."
    },
    {
      kind: "note",
      html:
        "<b>Фигурки — мерка, а не предложение по стилю персонажей.</b> Толщину борта не с чем " +
        "сравнивать в пустоте, поэтому силуэт нарисован тем же лайнартом. Настоящий язык персонажей " +
        "живёт в решениях <code>2026-08-01/14</code>–<code>/16</code>."
    }
  ]
};

export default section;
