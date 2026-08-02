/* Пол арены: плита в пустоте, язык плоского сторибука.

   Заказ Макса 2026-08-02: арена «летает где-то над пустотой», игра прямо говорит «это просто боевая
   арена», биомы, вид сбоку. Пиксель отменён решением `2026-08-01/14` — здесь его нет.

   ПОЧЕМУ ПРЕДЫДУЩАЯ ВЕРСИЯ ВЫГЛЯДЕЛА ПО-ДЕТСКИ. Макс сказал прямо: «непонятные фигуры, что это
   вообще». Разбор по референсам, на которые опирался Wildermyth (фоны Samurai Jack, Cartoon Saloon —
   «Тайна Келлс», старый концепт-арт Disney), даёт четыре конкретные причины, и ни одна из них не
   про «плоско»:

   1. ЖИРНЫЙ КОНТУР НА ВСЁМ. У фонов Samurai Jack контура нет вообще — границу держит контраст тона.
      Равномерная толстая обводка по каждому пятну — язык детской книжки. Здесь: земля БЕЗ контура,
      обводку получают только предметы, и толщина у неё разная.
   2. ФОРМЫ БЕЗ СМЫСЛА. Клякса не читается ничем. Участок земли должен быть УЗНАВАЕМЫМ объектом:
      тропа — вытянутой лентой вдоль движения, камень — угловатой плитой, выжженное — пятном с ядром.
   3. АБСОЛЮТНО РОВНАЯ ЗАЛИВКА. Плоский цвет без зерна читается как заливка в редакторе. У всех
      названных референсов поверх плоскости лежит живописная фактура. Зерно считается, стоит копейки
      и делает главную разницу между «дёшево» и «дорого».
   4. РАВНОМЕРНЫЙ СВЕТ. Виньетка по кругу — не свет, а затемнение. Драму даёт НАПРАВЛЕННЫЙ свет:
      одна сторона выбита, противоположная уходит в холодную тень, у форм появляется прижатая тень.

   ВЫБОР МАКСА (02.08.2026): контур тёмный цветной (не чёрный — душит цветной свет боя), плита
   строгий прямоугольник, гамма приглушённая природная, плотность скупая с пустым центром,
   борт ТОНКИЙ С ЦИФРОВЫМ ЭФФЕКТОМ.

   Про цифровой борт отдельно: канон arena-digital-swap говорит, что цифра — язык ПЕРЕХОДА, а не
   состояния, и по этой причине я её из борта убирала. Макс вернул решением. Значит либо канон
   получает исключение для края плиты, либо формулировку канона надо править — это его развилка,
   и до вердикта здесь стоит его вариант, а не моё прочтение. */

import { jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- настоящая геометрия ---------- */

const GEOM = {
  /** Поле боя: ArenaLayoutAuthoring._boundsSize = (20, 12) мировых единиц. */
  arenaW: 20,
  arenaH: 12,
  /** Человек-эталон: _refHumanHeight / _refHumanWidth. */
  humanH: 1.7,
  humanW: 0.6
} as const;

const U = 32;
const ARENA_W = GEOM.arenaW * U;
const ARENA_H = GEOM.arenaH * U;
const HUMAN_H = GEOM.humanH * U;
const HUMAN_W = GEOM.humanW * U;

/** Базовая толщина обводки ПРЕДМЕТОВ. У земли обводки нет вовсе. */
const LINE = Math.max(1.5, HUMAN_H * 0.04);

/* ---------- цвет ---------- */

type RGB = [number, number, number];

const rgb = (c: RGB) => `rgb(${c[0] | 0},${c[1] | 0},${c[2] | 0})`;

/** Обводка предмета: затемнённый тон самой заливки со сдвигом в холод. Не чёрный. */
const ink = (c: RGB, k = 0.4): RGB => [c[0] * k, c[1] * k, c[2] * k + 8];

const lighten = (c: RGB, k: number): RGB =>
  [c[0] + (255 - c[0]) * k, c[1] + (255 - c[1]) * k, c[2] + (255 - c[2]) * k];

/** Тень в этой гамме — не «тот же цвет темнее», а сдвиг в холод: так работает настоящий свет,
 *  и именно этим тень отличается от грязи. */
const shade = (c: RGB, k: number): RGB => [c[0] * (1 - k), c[1] * (1 - k * 0.92), c[2] * (1 - k * 0.72) + k * 22];

/* ---------- шум ---------- */

const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
const hash2 = (x: number, y: number, s: number) => jag(x * 374761 + y * 668265, s);

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
  ground: RGB;
  /** Тропа — вытянутая лента вытоптанного. */
  trail: RGB;
  /** Твёрдое: плиты, выходы породы. */
  hard: RGB;
  plant: RGB;
  plantKind: "grass" | "shard" | "bone";
  rim: RGB;
  voidTop: string;
  voidBottom: string;
}

const MEADOW: Biome = {
  id: "meadow",
  name: "Поляна",
  ground: [104, 122, 72],
  trail: [140, 120, 82],
  hard: [122, 120, 112],
  plant: [74, 96, 50],
  plantKind: "grass",
  rim: [54, 50, 44],
  voidTop: "#171320",
  voidBottom: "#0B0910"
};

const FOREST: Biome = {
  id: "forest",
  name: "Лес",
  ground: [70, 94, 62],
  trail: [106, 88, 60],
  hard: [92, 96, 90],
  plant: [44, 66, 42],
  plantKind: "grass",
  rim: [44, 46, 38],
  voidTop: "#101616",
  voidBottom: "#07090A"
};

const CAVE: Biome = {
  id: "cave",
  name: "Пещера",
  ground: [88, 84, 98],
  trail: [72, 68, 82],
  hard: [116, 112, 126],
  plant: [126, 158, 182],
  plantKind: "shard",
  rim: [48, 46, 58],
  voidTop: "#0D0B14",
  voidBottom: "#06050A"
};

const ASH: Biome = {
  id: "ash",
  name: "Пепелище",
  ground: [104, 92, 84],
  trail: [84, 70, 66],
  hard: [128, 116, 106],
  plant: [180, 166, 146],
  plantKind: "bone",
  rim: [52, 44, 40],
  voidTop: "#150F0D",
  voidBottom: "#090606"
};

/* ---------- фактура ----------
   Зерно поверх плоских заливок — то, что отличает живописную плоскость от заливки в редакторе.
   Считается один раз в offscreen и накладывается на всё сразу. */

let grainTile: HTMLCanvasElement | null = null;

function grain(): HTMLCanvasElement {
  if (grainTile) return grainTile;
  const size = 128;
  const cv = document.createElement("canvas");
  cv.width = size;
  cv.height = size;
  const c = cv.getContext("2d")!;
  const img = c.createImageData(size, size);
  for (let i = 0; i < size * size; i++) {
    const x = i % size;
    const y = (i / size) | 0;
    // Два масштаба: мелкая крупа плюс широкие разводы — как у бумаги, а не как у телевизионного шума.
    const fine = hash2(x, y, 5);
    const wide = vnoise(x * 0.06, y * 0.06, 9);
    const v = (fine * 0.55 + wide * 0.45 - 0.5) * 255;
    img.data[i * 4] = 128 + v;
    img.data[i * 4 + 1] = 128 + v;
    img.data[i * 4 + 2] = 128 + v;
    img.data[i * 4 + 3] = 255;
  }
  c.putImageData(img, 0, 0);
  grainTile = cv;
  return cv;
}

/* ---------- формы ---------- */

/** Тропа: изогнутая лента поперёк поля. Читается как «здесь ходят» — в отличие от кляксы,
 *  которая не читается ничем. */
function trailPath(ctx: CanvasRenderingContext2D, x0: number, y0: number, w: number, h: number, seed: number): void {
  // Диапазон нарочно ШИРОКИЙ. Первая версия давала тропе сползать только сверху-слева вниз-направо,
  // и на трёх сидах подряд это читалось как одна и та же арена: узкий диапазон вариации хуже
  // отсутствия вариации, потому что обещает разнообразие и не даёт его.
  const vertical = jag(seed, 19) > 0.62;
  const width = h * (0.09 + jag(seed, 11) * 0.09);
  const bow = (jag(seed, 23) - 0.5) * 0.55; // прогиб в обе стороны, а не всегда в одну

  if (vertical) {
    // Тропа поперёк, сверху вниз: тот же приём, повёрнутый на девяносто градусов.
    const xA = x0 + w * (0.18 + jag(seed, 3) * 0.28);
    const xB = x0 + w * (0.52 + jag(seed, 7) * 0.32);
    ctx.beginPath();
    ctx.moveTo(xA, y0 - 10);
    ctx.bezierCurveTo(xA + w * bow, y0 + h * 0.35, xB - w * bow, y0 + h * 0.65, xB, y0 + h + 10);
    ctx.lineTo(xB + width, y0 + h + 10);
    ctx.bezierCurveTo(xB + width - w * bow, y0 + h * 0.65, xA + width + w * bow, y0 + h * 0.35, xA + width, y0 - 10);
    ctx.closePath();
    return;
  }

  const yA = y0 + h * (0.14 + jag(seed, 3) * 0.34);
  const yB = y0 + h * (0.40 + jag(seed, 7) * 0.44);
  ctx.beginPath();
  ctx.moveTo(x0 - 10, yA);
  ctx.bezierCurveTo(x0 + w * 0.35, yA + h * bow, x0 + w * 0.55, yB - h * bow, x0 + w + 10, yB);
  ctx.lineTo(x0 + w + 10, yB + width);
  ctx.bezierCurveTo(x0 + w * 0.55, yB + width - h * bow, x0 + w * 0.35, yA + width + h * bow, x0 - 10, yA + width);
  ctx.closePath();
}

/** Плита породы: угловатый многоугольник. Именно углы отличают камень от лужи. */
function slabRockPath(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number, seed: number): void {
  const n = 6 + Math.floor(jag(seed, 3) * 3);
  ctx.beginPath();
  for (let i = 0; i < n; i++) {
    const a = (i / n) * Math.PI * 2 + jag(seed + i, 13) * 0.35;
    const rr = r * (0.62 + jag(seed + i * 5, 17) * 0.55) * (i % 2 === 0 ? 1 : 0.82);
    const x = cx + Math.cos(a) * rr * 1.35;
    const y = cy + Math.sin(a) * rr * 0.72;
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.closePath();
}

/** Пучок травы: тонкие дуги. Обводка тоньше, чем у предметов — трава не предмет, а деталь. */
function grassTuft(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  // Каждый пятый пучок — ЛИСТ, а не метёлка. Один силуэт, повторённый полсотни раз, читается
  // штампом; второй тип формы снимает это за десять строк.
  if (jag(seed, 51) > 0.8) {
    const lw = s * 0.85;
    const lh = s * 1.15;
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.quadraticCurveTo(x - lw, y - lh * 0.45, x - lw * 0.2, y - lh);
    ctx.quadraticCurveTo(x + lw * 0.55, y - lh * 0.5, x, y);
    ctx.closePath();
    ctx.fillStyle = rgb(color);
    ctx.fill();
    ctx.strokeStyle = rgb(shade(color, 0.4));
    ctx.lineWidth = Math.max(1, s * 0.11);
    ctx.lineJoin = "round";
    ctx.stroke();
    return;
  }

  const blades = 3 + Math.floor(jag(seed, 3) * 2);
  ctx.lineCap = "round";
  for (let pass = 0; pass < 2; pass++) {
    ctx.strokeStyle = pass === 0 ? rgb(shade(color, 0.45)) : rgb(color);
    for (let i = 0; i < blades; i++) {
      const dir = (jag(seed + i * 7, 11) - 0.5) * 1.5;
      const hgt = s * (0.75 + jag(seed + i * 13, 17) * 0.55);
      ctx.lineWidth = pass === 0 ? Math.max(1.8, s * 0.24) : Math.max(0.8, s * 0.1);
      ctx.beginPath();
      ctx.moveTo(x + dir * s * 0.18, y);
      ctx.quadraticCurveTo(x + dir * s * 0.45, y - hgt * 0.62, x + dir * s * 0.95, y - hgt);
      ctx.stroke();
    }
  }
}

function shardShape(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  const hh = s * (1.2 + jag(seed, 5) * 0.9);
  const ww = s * (0.42 + jag(seed, 9) * 0.28);
  ctx.beginPath();
  ctx.moveTo(x, y - hh);
  ctx.lineTo(x + ww, y - hh * 0.3);
  ctx.lineTo(x + ww * 0.42, y);
  ctx.lineTo(x - ww * 0.5, y);
  ctx.lineTo(x - ww * 0.82, y - hh * 0.38);
  ctx.closePath();
  ctx.fillStyle = rgb(color);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE * 0.7;
  ctx.stroke();
  // Грань, поймавшая свет: одна светлая плоскость — и осколок перестаёт быть наклейкой.
  ctx.beginPath();
  ctx.moveTo(x, y - hh);
  ctx.lineTo(x + ww * 0.42, y);
  ctx.lineTo(x - ww * 0.05, y);
  ctx.closePath();
  ctx.fillStyle = rgb(lighten(color, 0.3));
  ctx.fill();
}

function boneShape(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB): void {
  const ww = s * (1.1 + jag(seed, 7) * 0.7);
  ctx.beginPath();
  ctx.moveTo(x - ww * 0.5, y);
  ctx.lineTo(x + ww * 0.5, y - s * 0.13);
  ctx.lineTo(x + ww * 0.5, y + s * 0.16);
  ctx.lineTo(x - ww * 0.5, y + s * 0.28);
  ctx.closePath();
  ctx.fillStyle = rgb(color);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE * 0.6;
  ctx.stroke();
}

/* ---------- сцена ---------- */

interface SlabOpts {
  biome: Biome;
  rimU: number;
  seed: number;
  unit?: boolean;
  density?: number;
  crop?: boolean;
  /** Цифровая разметка на борту — выбор Макса. */
  digital?: boolean;
  /** Прошлый заход: жирный контур на всём, ровная заливка, без света. Для сравнения. */
  naive?: boolean;
}

/** Борт плиты — «край конструкта».
 *
 *  Собран из семи слоёв, и это осознанно дороже, чем полоса с сеткой: край — единственное место,
 *  где игра говорит, что арена СДЕЛАНА, и весь бой он остаётся на экране. Слои снизу вверх по
 *  смыслу: тело с падением к низу, ячейки данных по мировой сетке, скан-строки, угловые якоря,
 *  бегущая полоса просчёта, светящееся ребро и проекционные лучи в пустоту.
 *
 *  Почему ячейки берут шаг ровно в мировую единицу: край тогда не декор, а ЛИНЕЙКА — по нему
 *  читается масштаб поля, и это единственная разметка, которую видно, не включая дев-режим.
 *
 *  В движке это один шейдер на quad: все семь слоёв — функции от x вдоль борта и от y поперёк,
 *  ни одного ветвления по объектам. Здесь они разложены руками, чтобы было видно, из чего он
 *  состоит и что можно выключать по отдельности. */
function drawRim(ctx: CanvasRenderingContext2D, b: Biome, x0: number, y: number, w: number, hh: number, digital: boolean, seed = 1): void {
  // Верхняя кромка: тонкая светлая полоса на переходе грани в борт.
  ctx.fillStyle = rgb(lighten(b.rim, 0.42));
  ctx.fillRect(x0, y - LINE * 0.5, w, LINE * 0.5);

  const g = ctx.createLinearGradient(0, y, 0, y + hh);
  g.addColorStop(0, rgb(b.rim));
  g.addColorStop(1, rgb(shade(b.rim, 0.5)));
  ctx.fillStyle = g;
  ctx.fillRect(x0, y, w, hh);

  if (!digital) return;

  ctx.save();
  ctx.beginPath();
  ctx.rect(x0, y, w, hh);
  ctx.clip();

  // 1. ЭНЕРГИЯ ВДОЛЬ КРАЯ — НЕПРЕРЫВНАЯ, а не по клеткам.
  //    Здесь я дважды ошиблась одинаково: сперва заменила тайлмап шумом, потом разлиновала борт
  //    ячейками по мировой единице. И то и другое — «мыслить клетками». Клетка навязывает ритм,
  //    которого в предмете нет: борт это ОДНА пластина, у неё нет тридцати секций. Поэтому свечение
  //    здесь — плавное поле вдоль длины, с двумя-тремя сгущениями по сиду, и ни одной вертикальной
  //    границы.
  const glow = ctx.createLinearGradient(x0, 0, x0 + w, 0);
  const knots = 3;
  for (let i = 0; i <= knots + 1; i++) {
    const t = i / (knots + 1);
    const a = 0.05 + jag(i, seed + 3) * 0.13;
    glow.addColorStop(Math.min(1, t), `rgba(77,242,255,${a.toFixed(3)})`);
  }
  ctx.fillStyle = glow;
  ctx.fillRect(x0, y, w, hh);

  // Свет копится к нижнему ребру: пластина светится краем, а не всей плоскостью.
  const toEdge = ctx.createLinearGradient(0, y, 0, y + hh);
  toEdge.addColorStop(0, "rgba(77,242,255,0)");
  toEdge.addColorStop(1, "rgba(77,242,255,.16)");
  ctx.fillStyle = toEdge;
  ctx.fillRect(x0, y, w, hh);

  // 2. СКАН-СТРОКИ поперёк борта: горизонтальные линии через два пикселя. Дают «материал экрана»
  //    вместо крашеного металла. Они идут ВДОЛЬ пластины и её не режут.
  ctx.strokeStyle = "rgba(180,240,255,.055)";
  for (let sy = y + 1.5; sy < y + hh; sy += 3) {
    ctx.beginPath();
    ctx.moveTo(x0, sy);
    ctx.lineTo(x0 + w, sy);
    ctx.stroke();
  }

  // 3. ВОЛНА ПРОСЧЁТА: мягкое пятно света, ползущее вдоль края. Именно пятно с растушёванными
  //    краями, а не подсвеченная секция — у пластины нет секций. В игре оно поедет вдоль борта и
  //    свяжет край с анимацией создания; здесь застыло.
  const headX = x0 + jag(seed, 21) * w;
  const wave = ctx.createLinearGradient(headX - U * 4, 0, headX + U * 1.2, 0);
  wave.addColorStop(0, "rgba(140,255,246,0)");
  wave.addColorStop(0.72, "rgba(140,255,246,.13)");
  wave.addColorStop(1, "rgba(140,255,246,0)");
  ctx.fillStyle = wave;
  ctx.fillRect(x0, y, w, hh);

  // 5. УГЛОВЫЕ ЯКОРЯ: короткие уголки на концах борта. Приём интерфейсов прицеливания — говорит
  //    «это размеченная область», и стоит четыре линии.
  ctx.strokeStyle = "rgba(77,242,255,.55)";
  ctx.lineWidth = 1.6;
  const arm = U * 0.7;
  for (const [ax, dir] of [[x0, 1], [x0 + w, -1]] as Array<[number, number]>) {
    ctx.beginPath();
    ctx.moveTo(ax, y + hh);
    ctx.lineTo(ax + arm * dir, y + hh);
    ctx.moveTo(ax, y + hh);
    ctx.lineTo(ax, y + hh - arm * 0.8);
    ctx.stroke();
  }

  ctx.restore();

  // 6. РЕБРО: светящаяся линия низа плюс ореол под ней. Плита не обрывается, а заканчивается.
  const halo = ctx.createLinearGradient(0, y + hh, 0, y + hh + U * 0.6);
  halo.addColorStop(0, "rgba(77,242,255,.20)");
  halo.addColorStop(1, "rgba(77,242,255,0)");
  ctx.fillStyle = halo;
  ctx.fillRect(x0, y + hh, w, U * 0.6);

  ctx.strokeStyle = "rgba(150,250,255,.62)";
  ctx.lineWidth = 1.6;
  ctx.beginPath();
  ctx.moveTo(x0, y + hh);
  ctx.lineTo(x0 + w, y + hh);
  ctx.stroke();

  // 7. ПРОЕКЦИОННЫЕ ЛУЧИ вниз: редкие вертикали, гаснущие в пустоту. Читаются как «плита откуда-то
  //    спроецирована», то есть договаривают мысль «арену собрали», уже не трогая саму плиту.
  //    Позиции берутся сидом свободно, а НЕ по шагу сетки: иначе лучи снова расчертят край на клетки.
  for (let i = 0; i < 7; i++) {
    if (jag(i, seed + 31) < 0.35) continue;
    const px = x0 + jag(i * 5 + 1, seed + 37) * w;
    const len = U * (1.2 + jag(i, seed + 33) * 2.2);
    const ray = ctx.createLinearGradient(0, y + hh, 0, y + hh + len);
    ray.addColorStop(0, "rgba(77,242,255,.13)");
    ray.addColorStop(1, "rgba(77,242,255,0)");
    ctx.strokeStyle = ray;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(px, y + hh);
    ctx.lineTo(px, y + hh + len);
    ctx.stroke();
  }
}

function unitFigure(ctx: CanvasRenderingContext2D, x: number, groundY: number, lit: boolean): void {
  const hh = HUMAN_H;
  const ww = HUMAN_W;
  const body: RGB = lit ? [152, 118, 96] : [112, 90, 78];

  ctx.fillStyle = "rgba(24,20,18,.32)";
  ctx.beginPath();
  ctx.ellipse(x + ww * 0.2, groundY, ww * 0.9, ww * 0.3, 0, 0, Math.PI * 2);
  ctx.fill();

  ctx.beginPath();
  ctx.moveTo(x - ww * 0.5, groundY);
  ctx.lineTo(x - ww * 0.58, groundY - hh * 0.6);
  ctx.quadraticCurveTo(x, groundY - hh * 0.76, x + ww * 0.58, groundY - hh * 0.6);
  ctx.lineTo(x + ww * 0.5, groundY);
  ctx.closePath();
  ctx.fillStyle = rgb(body);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(body, 0.45));
  ctx.lineWidth = LINE * 0.8;
  ctx.lineJoin = "round";
  ctx.stroke();

  ctx.beginPath();
  ctx.ellipse(x, groundY - hh * 0.8, ww * 0.5, hh * 0.13, 0, 0, Math.PI * 2);
  ctx.fillStyle = rgb(lighten(body, 0.14));
  ctx.fill();
  ctx.strokeStyle = rgb(ink(body, 0.45));
  ctx.stroke();
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

    ctx.fillStyle = rgb(b.ground);
    ctx.fillRect(x0, y0, pw, ph);

    if (o.naive) {
      // Прошлый заход: кляксы с равномерной жирной обводкой, ровная заливка, свет по кругу.
      const blobs: Array<[RGB, number, number, number]> = [
        [b.trail, 0.28, 0.32, 0.19],
        [b.hard, 0.62, 0.24, 0.11],
        [b.trail, 0.76, 0.68, 0.15]
      ];
      for (const [col, fx, fy, fr] of blobs) {
        ctx.beginPath();
        ctx.ellipse(x0 + pw * fx, y0 + ph * fy, pw * fr, ph * fr * 0.72, 0, 0, Math.PI * 2);
        ctx.fillStyle = rgb(col);
        ctx.fill();
        ctx.strokeStyle = rgb(ink(col));
        ctx.lineWidth = 4;
        ctx.stroke();
      }
      ctx.restore();
      drawRim(ctx, b, x0, y0 + ph, pw, rimH, false, o.seed);
      return;
    }

    // 0. ТОНАЛЬНЫЕ МАССЫ — крупные плоскости чуть разного тона на всю плиту.
    //    Приём фонов Samurai Jack: глубину даёт не количество деталей, а НЕСКОЛЬКО БОЛЬШИХ
    //    плоскостей разной светлоты. Без них земля остаётся одной заливкой, по которой рассыпана
    //    мелочь, и картинка читается пустой независимо от числа объектов.
    for (let m = 0; m < 3; m++) {
      const t = jag(m, o.seed + 71);
      const cx = x0 + pw * (0.2 + jag(m * 3, o.seed + 73) * 0.65);
      const cy = y0 + ph * (0.2 + jag(m * 3 + 1, o.seed + 75) * 0.6);
      const r = pw * (0.3 + jag(m, o.seed + 77) * 0.32);
      const mass = ctx.createRadialGradient(cx, cy, r * 0.15, cx, cy, r);
      const tint = t > 0.5 ? lighten(b.ground, 0.1) : shade(b.ground, 0.13);
      mass.addColorStop(0, `rgba(${tint.map((v) => v | 0).join(",")},.85)`);
      mass.addColorStop(1, `rgba(${tint.map((v) => v | 0).join(",")},0)`);
      ctx.fillStyle = mass;
      ctx.fillRect(x0, y0, pw, ph);
    }

    // 1. ТРОПА — узнаваемая форма вместо кляксы: лента поперёк поля, вдоль движения.
    trailPath(ctx, x0, y0, pw, ph, o.seed);
    ctx.fillStyle = rgb(b.trail);
    ctx.fill();
    // Прижатая тень у нижней кромки формы: земля перестаёт быть аппликацией и получает толщину.
    ctx.save();
    ctx.clip();
    ctx.fillStyle = `rgba(${shade(b.trail, 0.4).map((v) => v | 0).join(",")},.55)`;
    ctx.fillRect(x0, y0, pw, ph);
    ctx.restore();
    trailPath(ctx, x0, y0 - 3, pw, ph, o.seed);
    ctx.fillStyle = rgb(b.trail);
    ctx.fill();

    // 2. ПЛИТЫ ПОРОДЫ — углы, а не овалы, и группами: одиночный камень читается мусором.
    const clusters = [
      [0.16, 0.74, 0.9],
      [0.72, 0.2, 0.75],
      [0.86, 0.58, 0.6]
    ];
    for (let ci = 0; ci < clusters.length; ci++) {
      const [fx, fy, sc] = clusters[ci] as [number, number, number];
      const n = 2 + Math.floor(jag(ci, o.seed + 3) * 2);
      for (let i = 0; i < n; i++) {
        const cx = x0 + pw * fx + (jag(ci * 7 + i, o.seed + 5) - 0.5) * pw * 0.1;
        const cy = y0 + ph * fy + (jag(ci * 7 + i, o.seed + 9) - 0.5) * ph * 0.09;
        const r = HUMAN_H * 0.5 * sc * (0.7 + jag(i, o.seed + 11) * 0.6);
        // Тень под плитой — смещена по направлению света, а не размазана вокруг.
        slabRockPath(ctx, cx + r * 0.16, cy + r * 0.2, r, o.seed + ci * 31 + i);
        ctx.fillStyle = "rgba(22,18,26,.28)";
        ctx.fill();
        slabRockPath(ctx, cx, cy, r, o.seed + ci * 31 + i);
        ctx.fillStyle = rgb(b.hard);
        ctx.fill();
        ctx.strokeStyle = rgb(ink(b.hard, 0.5));
        ctx.lineWidth = LINE * 0.9;
        ctx.lineJoin = "round";
        ctx.stroke();
        // Освещённая верхняя грань: свет идёт слева-сверху, и это видно на каждом предмете.
        ctx.save();
        slabRockPath(ctx, cx, cy, r, o.seed + ci * 31 + i);
        ctx.clip();
        ctx.fillStyle = `rgba(${lighten(b.hard, 0.26).map((v) => v | 0).join(",")},.9)`;
        ctx.fillRect(cx - r * 2, cy - r * 2, r * 4, r * 1.9);
        ctx.restore();
      }
    }

    // 2б. КРУПНЫЙ АКЦЕНТ — один валун на арену, у края. Композиции нужен ЯКОРЬ: когда всё
    //     одного размера, глазу не за что зацепиться и поле читается шумом, сколько объектов в
    //     него ни клади. Ставится у края, чтобы не спорить с боем в центре.
    {
      const ax = x0 + pw * (jag(o.seed, 81) > 0.5 ? 0.11 : 0.89);
      const ay = y0 + ph * (0.24 + jag(o.seed, 83) * 0.55);
      const ar = HUMAN_H * 0.85;
      slabRockPath(ctx, ax + ar * 0.2, ay + ar * 0.24, ar, o.seed + 91);
      ctx.fillStyle = "rgba(20,16,24,.34)";
      ctx.fill();
      slabRockPath(ctx, ax, ay, ar, o.seed + 91);
      ctx.fillStyle = rgb(b.hard);
      ctx.fill();
      ctx.strokeStyle = rgb(ink(b.hard, 0.5));
      ctx.lineWidth = LINE;
      ctx.stroke();
      ctx.save();
      slabRockPath(ctx, ax, ay, ar, o.seed + 91);
      ctx.clip();
      ctx.fillStyle = `rgba(${lighten(b.hard, 0.3).map((v) => v | 0).join(",")},.92)`;
      ctx.fillRect(ax - ar * 2, ay - ar * 2, ar * 4, ar * 2);
      // Скол: одна линия внутри формы. Пустая заливка читается наклейкой, а трещина — камнем.
      ctx.strokeStyle = rgb(ink(b.hard, 0.62));
      ctx.lineWidth = LINE * 0.7;
      ctx.beginPath();
      ctx.moveTo(ax - ar * 0.7, ay - ar * 0.1);
      ctx.lineTo(ax - ar * 0.05, ay + ar * 0.18);
      ctx.lineTo(ax + ar * 0.6, ay - ar * 0.05);
      ctx.stroke();
      ctx.restore();
    }

    // 2в. ЦВЕТОВОЙ АКЦЕНТ — одно пятно другого оттенка на всю арену (мох, лишайник, выцветшая
    //     трава). Пять процентов площади, но именно оно снимает ощущение монохрома: приглушённая
    //     гамма без единого чужого тона выглядит выцветшей, а не сдержанной.
    {
      const mx = x0 + pw * (0.3 + jag(o.seed, 95) * 0.45);
      const my = y0 + ph * (0.3 + jag(o.seed, 97) * 0.42);
      const mr = HUMAN_H * (1.1 + jag(o.seed, 99) * 0.7);
      const accent: RGB = b.plantKind === "grass"
        ? [118, 140, 78]
        : b.plantKind === "shard" ? [96, 122, 150] : [138, 106, 74];
      const g2 = ctx.createRadialGradient(mx, my, mr * 0.1, mx, my, mr);
      g2.addColorStop(0, `rgba(${accent.join(",")},.5)`);
      g2.addColorStop(1, `rgba(${accent.join(",")},0)`);
      ctx.fillStyle = g2;
      ctx.fillRect(x0, y0, pw, ph);
    }

    // 3. РАСТИТЕЛЬНОСТЬ — скупо и по краям (выбор Макса): в центре дерутся.
    const count = Math.round(52 * dens);
    for (let i = 0; i < count; i++) {
      const fx = jag(i * 3 + 1, o.seed + 31);
      const fy = jag(i * 3 + 2, o.seed + 33);
      const dx = (fx - 0.5) * 2;
      const dy = (fy - 0.5) * 2;
      const edge = Math.min(1, Math.sqrt(dx * dx + dy * dy) / 0.95);
      if (jag(i * 3 + 5, o.seed + 35) > Math.pow(edge, 2.2) * 1.2 * dens) continue;
      const px = x0 + fx * pw;
      const py = y0 + fy * ph;
      const s = HUMAN_H * (0.15 + jag(i, o.seed + 41) * 0.13);
      if (b.plantKind === "grass") grassTuft(ctx, px, py, s, o.seed + i * 17, b.plant);
      else if (b.plantKind === "shard") shardShape(ctx, px, py, s, o.seed + i * 17, b.plant);
      else boneShape(ctx, px, py, s, o.seed + i * 17, b.plant);
    }

    // 4. ЗЕРНО поверх всего: живописная фактура вместо ровной заливки. Главная разница между
    //    «дёшево» и «дорого» в плоской графике, и стоит она одного прохода.
    ctx.save();
    ctx.globalCompositeOperation = "overlay";
    ctx.globalAlpha = 0.16;
    const gt = grain();
    for (let gy = y0; gy < y0 + ph; gy += 128) {
      for (let gx = x0; gx < x0 + pw; gx += 128) ctx.drawImage(gt, gx, gy);
    }
    ctx.restore();

    // 5. НАПРАВЛЕННЫЙ СВЕТ вместо круглой виньетки: слева-сверху выбито, справа-снизу уходит
    //    в холод. Это и есть «объём и свет отдаёт движок» из канона.
    const lg = ctx.createLinearGradient(x0, y0, x0 + pw * 0.9, y0 + ph);
    lg.addColorStop(0, "rgba(255,238,198,.16)");
    lg.addColorStop(0.45, "rgba(255,238,198,0)");
    lg.addColorStop(1, "rgba(24,26,54,.34)");
    ctx.fillStyle = lg;
    ctx.fillRect(x0, y0, pw, ph);

    // 6. ПРИТЕНЕНИЕ ПО ПЕРИМЕТРУ — узкая тёмная кайма ВДОЛЬ КРАЁВ ПЛИТЫ, а не круглая виньетка.
    //    Плита обретает толщину: земля у обрыва темнее, потому что свет туда не заворачивает.
    //    Круглое затемнение этого не даёт — оно говорит про кадр, а не про предмет.
    const edgeW = HUMAN_H * 0.9;
    const sides: Array<[number, number, number, number, number, number]> = [
      [x0, y0, x0, y0 + edgeW, pw, edgeW],
      [x0, y0 + ph, x0, y0 + ph - edgeW, pw, edgeW],
      [x0, y0, x0 + edgeW, y0, edgeW, ph],
      [x0 + pw, y0, x0 + pw - edgeW, y0, edgeW, ph]
    ];
    for (let i = 0; i < sides.length; i++) {
      const [gx0, gy0, gx1, gy1] = sides[i] as [number, number, number, number, number, number];
      const eg = ctx.createLinearGradient(gx0, gy0, gx1, gy1);
      eg.addColorStop(0, "rgba(16,14,24,.34)");
      eg.addColorStop(1, "rgba(16,14,24,0)");
      ctx.fillStyle = eg;
      if (i < 2) ctx.fillRect(x0, i === 0 ? y0 : y0 + ph - edgeW, pw, edgeW);
      else ctx.fillRect(i === 2 ? x0 : x0 + pw - edgeW, y0, edgeW, ph);
    }

    ctx.restore();

    if (o.unit) {
      unitFigure(ctx, x0 + pw * 0.32, y0 + ph * 0.72, true);
      unitFigure(ctx, x0 + pw * 0.58, y0 + ph * 0.44, false);
      unitFigure(ctx, x0 + pw * 0.68, y0 + ph * 0.64, false);
    }

    if (o.crop) return;

    drawRim(ctx, b, x0, y0 + ph, pw, rimH, !!o.digital, o.seed);

    ctx.strokeStyle = rgb(shade(b.rim, 0.55));
    ctx.lineWidth = LINE * 0.8;
    ctx.lineJoin = "miter";
    ctx.strokeRect(x0, y0, pw, ph + rimH);

    const dustTop = y0 + ph + rimH;
    for (let i = 0; i < 26; i++) {
      const px = x0 + jag(i * 3 + 1, o.seed + 61) * pw;
      const py = dustTop + jag(i * 3 + 2, o.seed + 63) * Math.max(h - dustTop, 1);
      const a = 0.3 * (1 - (py - dustTop) / Math.max(h - dustTop, 1));
      ctx.fillStyle = `rgba(198,178,132,${a.toFixed(3)})`;
      ctx.beginPath();
      ctx.arc(px, py, 1.3, 0, Math.PI * 2);
      ctx.fill();
    }
  };
}

/* ---------- стенды ---------- */

const FULL: [number, number] = [760, 560];

const NAIVE_STAND: StandDef = {
  id: "naive",
  status: "rejected",
  title: "Кляксы с обводкой",
  tag: "прошлый заход",
  note: "Овалы, равномерная жирная обводка, ровная заливка, свет по кругу.",
  facts: [
    ["обводка", "одна толщина на всём"],
    ["формы", "не читаются"],
    ["фактура", "нет"],
    ["свет", "виньетка"]
  ],
  verdict:
    "«Непонятные фигуры, что это вообще» — и претензия точная. Пятно без узнаваемой формы не " +
    "сообщает ничего, а равномерная толстая обводка по каждому пятну — язык детской книжки.",
  size: FULL,
  draw: slab({ biome: MEADOW, rimU: 1, seed: 4, naive: true })
};

const MAIN_STAND: StandDef = {
  id: "storybook",
  status: "waiting",
  title: "Тропа, порода, зерно, свет",
  tag: "четыре правки",
  note: "Земля без обводки, формы узнаваемые, зерно поверх заливок, свет направленный. Борт тонкий с цифрой.",
  facts: [
    ["земля", "без обводки"],
    ["тропа", "лента вдоль движения"],
    ["порода", "углы, группами"],
    ["свет", "слева-сверху"]
  ],
  verdict:
    "Разница не в «плоско или нет», а в четырёх приёмах, которых не было: узнаваемая форма, " +
    "обводка только у предметов и разной толщины, живописное зерно, направленный свет с прижатыми тенями.",
  size: FULL,
  draw: slab({ biome: MEADOW, rimU: 1, seed: 4, unit: true, digital: true })
};

const RIM_STANDS: StandDef[] = [
  {
    id: "thin-digital",
    status: "accepted",
    title: "Пластина с цифровым краем",
    tag: "выбор Макса",
    facts: [["борт", "1 ед"], ["свечение", "непрерывное, без секций"], ["ребро", "светится с ореолом"]],
    verdict: "Пластина в пустоте, и цифра проступает на её краю — плита читается сделанной, а не найденной.",
    size: FULL,
    draw: slab({ biome: MEADOW, rimU: 1, seed: 4, unit: true, digital: true })
  },
  {
    id: "thick",
    status: "rejected",
    title: "Толстая плита",
    facts: [["борт", "1.5 ед"], ["к росту", "почти человек"]],
    verdict: "Остров: тянет обратно в «кусок мира», хотя борт и плоский.",
    size: FULL,
    draw: slab({ biome: MEADOW, rimU: 1.5, seed: 4, unit: true, digital: true })
  }
];

/** Три сида одной поляны. Один сид ничего не доказывает: по нему не отличить удачную композицию
 *  от везения, и не видно, держится ли правило «центр пуст, акцент у края» на любом заходе. */
/** Три сида, которые крутятся ВЕЗДЕ: и на поляне целиком, и в каждом биоме. Один сид не отличает
 *  удачную композицию от везения — а по трём сразу видно, держатся ли правила расстановки. */
const SEEDS = [11, 27, 43];

const SEED_STANDS: StandDef[] = SEEDS.map((s, i) => ({
  id: `seed-${s}`,
  status: "waiting" as const,
  title: `Сид ${s}`,
  tag: i === 0 ? "тот же биом, другой сид" : undefined,
  facts: [["биом", "поляна"], ["сид", String(s)]],
  draw: slab({ biome: MEADOW, rimU: 1, seed: s, unit: true, digital: true })
}));

/** Четыре биома НА ТЕХ ЖЕ трёх сидах: ряд — биом, столбец — сид. Так сразу видно и то, что биом
 *  это конфиг (ряды отличаются только данными), и то, что генерация даёт разброс (столбцы
 *  отличаются композицией). По одному фрагменту на биом ни того, ни другого не проверить. */
const BIOME_STANDS: StandDef[] = [MEADOW, FOREST, CAVE, ASH].flatMap((b) =>
  SEEDS.map((s, i) => ({
    id: `${b.id}-${s}`,
    status: "waiting" as const,
    title: i === 0 ? b.name : `${b.name} · ${s}`,
    tag: i === 0 ? "тот же код, другой конфиг" : undefined,
    facts: [["сид", String(s)], ["силуэт", b.plantKind]],
    draw: slab({ biome: b, rimU: 1, seed: s, crop: true })
  }))
);

const section: SectionDef = {
  id: "floor",
  title: "Пол арены",
  lede:
    "Плита в пустоте в языке плоского сторибука. Разбор, чем живописная плоскость отличается от " +
    "аппликации, — по референсам, на которые опирался Wildermyth.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "why",
      title: "Почему предыдущая версия выглядела по-детски",
      lede: "Четыре причины, и ни одна из них не про «плоско»."
    },
    {
      kind: "table",
      head: ["Причина", "Как было", "Как надо"],
      rows: [
        ["Обводка", "жирная и одинаковая на всём", "у земли нет вовсе; у предметов — разной толщины"],
        ["Форма", "клякса, не читается ничем", "тропа лентой, порода углами и группами"],
        ["Заливка", "идеально ровный цвет", "живописное зерно поверх плоскости"],
        ["Свет", "виньетка по кругу", "направленный, с прижатыми тенями у форм"]
      ]
    },
    {
      kind: "note",
      html:
        "Опора — не вкус, а родословная стиля: <b>Wildermyth</b> строился на фонах " +
        "<b>Samurai Jack</b>, работах <b>Cartoon Saloon</b> («Тайна Келлс», «Песнь моря») и старом " +
        "концепт-арте Disney. У фонов Samurai Jack <b>обводки нет вообще</b> — границу держит " +
        "контраст тона. Толстый контур по каждому пятну и есть та деталь, которая читается как " +
        "детская книжка."
    },
    { kind: "split", items: [NAIVE_STAND, MAIN_STAND] },
    {
      kind: "head",
      id: "rim",
      title: "Борт: тонкая пластина с цифрой",
      lede: "Выбор Макса. Толстая оставлена рядом как отклонённая."
    },
    { kind: "split", items: RIM_STANDS },
    {
      kind: "note",
      html:
        "Цифра на борту — <b>решение Макса</b>. Канон <code>arena-digital-swap</code> говорит, что " +
        "цифра у нас язык ПЕРЕХОДА, а не состояния, и по этой причине я её из борта убирала. Значит " +
        "либо край плиты получает исключение, либо формулировку канона надо править: развилка " +
        "открыта, до вердикта стоит вариант Макса."
    },
    {
      kind: "head",
      id: "seeds",
      title: "Три сида одной поляны",
      lede: "По одному сиду не отличить удачную композицию от везения."
    },
    { kind: "stands", items: SEED_STANDS },
    {
      kind: "note",
      html:
        "<b>Что проверять глазами:</b> держится ли на каждом сиде правило «центр пуст, акцент у " +
        "края». Если на каком-то заходе валун уехал в середину или тропа перерезала боевой " +
        "коридор — виновата не картинка, а правила расстановки."
    },
    {
      kind: "head",
      id: "biomes",
      title: "Биом — это конфиг",
      lede: "Ряд — биом, столбец — сид. Ряды отличаются только данными, столбцы — только сидом."
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
        ["Ростов в ширину арены", "около 12", "20 ÷ 1.7"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Фигурки — мерка, а не предложение по стилю персонажей.</b> Толщину борта не с чем " +
        "сравнивать в пустоте. Язык персонажей живёт в решениях <code>2026-08-01/14</code>–<code>/16</code>."
    }
  ]
};

export default section;
