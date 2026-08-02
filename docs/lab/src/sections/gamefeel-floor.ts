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

/** Промежуточный тон между двумя: нужен для ступеней плоской лепки. */
const mix = (a: RGB, b: RGB, t: number): RGB => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];

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
  /** Цвет ПРЕДМЕТОВ биома: бревно, столб. Одна форма — разный смысл: упавший ствол, сломанная
   *  колонна, обугленная балка. */
  prop: RGB;
  /** Свечение трещины. null — трещина просто тёмная. */
  crackGlow: RGB | null;
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
  prop: [116, 92, 62],
  crackGlow: null,
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
  prop: [96, 76, 52],
  crackGlow: null,
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
  prop: [104, 100, 116],
  crackGlow: [110, 190, 220],
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
  prop: [74, 62, 58],
  crackGlow: [214, 116, 62],
  rim: [52, 44, 40],
  voidTop: "#150F0D",
  voidBottom: "#090606"
};

/* ---------- время суток и небо ----------
   Решения Макса 02.08.2026: облака ПЛЫВУТ НАД ареной (значит их тени бегут по полю), а под плитой
   небо уходит в чёрную непроглядную бездну. Время суток красит небо сильно, свет умеренно, землю
   слабо — иначе поедет читаемость боя, а у нас цвет несёт информацию о стихии удара.

   Из ресёрча, и оба пункта контринтуитивны:
   - ЗАКАТ МЯГЧЕ ДНЯ ПО КОНТРАСТУ. Солнце низко, свет рассеивается сильнее, тени СВЕТЛЕЮТ. Драму
     даёт цвет и длина тени, а не жёсткость. Сделать закат контрастным — типичная ошибка.
   - СУМЕРКИ ЭТО 70/30, а не «всё синее»: большая холодная масса плюс узкая тёплая полоса остатка.
     Убери тёплый остаток — получишь просто затемнённый день. */

interface TimeOfDay {
  id: string;
  name: string;
  /** Небо сразу под кромкой плиты — оттуда начинается падение в бездну. */
  skyNear: RGB;
  /** Дно: почти чёрное, но с оттенком времени суток. Чистый чёрный съел бы силуэт и погасил bloom. */
  abyss: RGB;
  /** Цвет прямого света и цвет тени. Тень КОМПЛЕМЕНТАРНА свету — этим она отличается от грязи. */
  light: RGB;
  shadow: RGB;
  /** Сила прямого света: день жёсткий, сумерки почти без него. */
  lightPower: number;
  /** Длина теней в долях роста человека: полдень короткие, низкое солнце длинные. */
  shadowLen: number;
  /** Слабая покраска земли. Держим низкой сознательно (см. шапку). */
  groundTint: RGB;
  groundTintAmount: number;
  /** Общая яркость сцены. */
  exposure: number;
}

const MORNING: TimeOfDay = {
  id: "morning", name: "Утро",
  skyNear: [172, 188, 206], abyss: [16, 22, 34],
  light: [255, 232, 206], shadow: [86, 104, 146],
  lightPower: 0.6, shadowLen: 1.5,
  groundTint: [214, 220, 226], groundTintAmount: 0.10,
  exposure: 0.98
};

const DAY: TimeOfDay = {
  id: "day", name: "День",
  skyNear: [138, 172, 200], abyss: [10, 14, 24],
  light: [255, 248, 226], shadow: [70, 88, 132],
  lightPower: 1, shadowLen: 0.75,
  groundTint: [255, 252, 240], groundTintAmount: 0.05,
  exposure: 1
};

const SUNSET: TimeOfDay = {
  id: "sunset", name: "Закат",
  skyNear: [226, 148, 96], abyss: [22, 14, 22],
  light: [255, 186, 118], shadow: [96, 78, 132],
  // Мягче дня: тени светлее, свет рассеяннее. Длинные тени — вот что делает закат закатом.
  lightPower: 0.72, shadowLen: 2.4,
  groundTint: [255, 196, 140], groundTintAmount: 0.16,
  exposure: 0.94
};

const DUSK: TimeOfDay = {
  id: "dusk", name: "Сумерки",
  skyNear: [86, 92, 140], abyss: [6, 7, 14],
  light: [178, 168, 200], shadow: [40, 48, 88],
  lightPower: 0.22, shadowLen: 3.2,
  groundTint: [140, 150, 196], groundTintAmount: 0.2,
  exposure: 0.74
};

const TIMES = [MORNING, DAY, SUNSET, DUSK];

interface Weather {
  id: string;
  name: string;
  /** Сколько облаков плывёт над ареной. */
  clouds: number;
  /** Насколько они плотные. */
  cloudAlpha: number;
  /** Осталось нулём: облака ушли в фон, а то, что ЗА предметом, тени на него не бросает. */
  shadowAmount: number;
  /** Гроза: тучи темнее, добавляется вспышка. */
  storm: boolean;
}

const CLEAR: Weather = { id: "clear", name: "Малооблачно", clouds: 3, cloudAlpha: 0.5, shadowAmount: 0, storm: false };
const NORMAL: Weather = { id: "normal", name: "Обычно", clouds: 7, cloudAlpha: 0.66, shadowAmount: 0, storm: false };
const CLOUDY: Weather = { id: "cloudy", name: "Многооблачно", clouds: 14, cloudAlpha: 0.82, shadowAmount: 0, storm: false };
const STORM: Weather = { id: "storm", name: "Гроза", clouds: 18, cloudAlpha: 0.92, shadowAmount: 0, storm: true };

const WEATHERS = [CLEAR, NORMAL, CLOUDY, STORM];

/** Облачная масса по рефу Dead Weight: клубы сверху, ПЛОСКИЙ НИЗ.
 *
 *  Плоское дно — главное правило рисования облака и то, чего не было в первой версии: там были
 *  просто сросшиеся круги, и они читались комками ваты. Дно облака плоское, потому что на этой
 *  высоте водяной пар начинает конденсироваться — и глаз знает это, даже не зная почему.
 *
 *  Масса вытянута вдоль горизонта и собрана из долей разного размера: крупная в середине, мельче
 *  к краям. Один силуэт на всё поле выглядел бы штампом. */
/** Доли массы: позиция и радиус каждой шапки. Считаются один раз и переиспользуются всеми
 *  проходами света — иначе тон ляжет не на ту форму. */
function cloudLobes(cx: number, cy: number, r: number, seed: number): Array<[number, number, number]> {
  const n = 3 + Math.floor(jag(seed, 3) * 3);
  const halfW = r * 1.5;
  const out: Array<[number, number, number]> = [];
  for (let i = 0; i < n; i++) {
    const t = (i + 0.5) / n;
    const bell = Math.sin(t * Math.PI);
    const lr = r * (0.34 + jag(seed + i * 7, 11) * 0.42) * (0.45 + bell * 0.85);
    out.push([cx - halfW + t * halfW * 2, cy - lr * 0.25, lr]);
  }
  return out;
}

/** Силуэт массы: доли сверху, ПЛОСКИЙ НИЗ. Плоское дно — там, где восходящий воздух доходит до
 *  уровня конденсации; глаз знает это правило, даже не зная физики. */
/** Рваная дуга: та же окружность, но радиус гуляет шумом по углу. Идеальная дуга — третья примета
 *  нарисованной ваты: у настоящего облака край клубится на всех масштабах сразу. */
function raggedArc(
  ctx: CanvasRenderingContext2D,
  lx: number,
  ly: number,
  lr: number,
  from: number,
  to: number,
  seed: number,
  amp = 0.13
): void {
  // Шагов немного и шум ДЕШЁВЫЙ: первая версия считала vnoise на каждую точку каждой ступени
  // каждой доли, и страница со стендами просто вставала. Рваность — деталь второго плана,
  // платить за неё интерполированным шумом незачем.
  const steps = Math.max(5, Math.round((to - from) * 3.5));
  for (let i = 0; i <= steps; i++) {
    const a = from + ((to - from) * i) / steps;
    const n = (jag(i * 3, seed) - 0.5) * 2;
    const fine = (jag(i * 7 + 1, seed + 3) - 0.5);
    const rr = lr * (1 + n * amp + fine * amp * 0.5);
    const x = lx + Math.cos(a) * rr;
    const y = ly + Math.sin(a) * rr;
    if (i === 0) ctx.lineTo(x, y);
    else ctx.lineTo(x, y);
  }
}

/** Силуэт массы: доли сверху, ПЛОСКИЙ НИЗ. Плоское дно — там, где восходящий воздух доходит до
 *  уровня конденсации; глаз знает это правило, даже не зная физики. */
function cloudMass(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number, seed: number): void {
  const lobes = cloudLobes(cx, cy, r, seed);
  const halfW = r * 1.5;
  ctx.beginPath();
  ctx.moveTo(cx - halfW, cy);
  for (let i = 0; i < lobes.length; i++) {
    const [lx, ly, lr] = lobes[i]!;
    raggedArc(ctx, lx, ly, lr, Math.PI * 0.98, Math.PI * 2.02, seed + i * 13);
  }
  ctx.lineTo(cx + halfW, cy);
  ctx.closePath();
}

/** ОБЪЁМ МАССЫ — то, ради чего затевался разбор теории.
 *
 *  Раньше объём изображала одна светлая полоса поверх тела, и это давало вату: полоса не знает,
 *  где у формы выпуклости. Теперь каждая ДОЛЯ лепится отдельно, как шар — «подходи к кучевому
 *  облаку так же, как лепил бы сферу». Три правила из теории, и все три контринтуитивны:
 *
 *  1. ТЕНЬ ОБЛАКА СВЕТЛАЯ. Типичная ошибка — валить её в тёмное; на деле тени облаков заметно
 *     светлее теней на земле, разница со светом идёт больше по ЦВЕТУ, чем по яркости.
 *  2. ТЕНЬ ТЕПЛЕЕ НЕБА и уходит в фиолетовый: она смешана из синего света неба и тёплого
 *     отражения от земли снизу.
 *  3. У ДНА ЕСТЬ ОТРАЖЁННЫЙ СВЕТ. Низ облака не чёрный — он ловит охру и фиолет от того, что
 *     под ним. Чёрное дно — вторая примета нарисованной ваты. */
function paintCloudVolume(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  r: number,
  seed: number,
  lit: RGB,
  shadow: RGB,
  bounce: RGB,
  alpha: number
): void {
  const lobes = cloudLobes(cx, cy, r, seed);

  ctx.save();
  cloudMass(ctx, cx, cy, r, seed);
  ctx.clip();

  // 1. Заливаем всю массу ТЕНЕВЫМ тоном — он основа, свет ляжет сверху.
  ctx.fillStyle = `rgba(${shadow.map((v) => v | 0).join(",")},${alpha.toFixed(3)})`;
  ctx.fillRect(cx - r * 2, cy - r * 2.4, r * 4, r * 3.2);

  // 2. Отражённый свет у самого дна: тёплая полоса снизу.
  const bg = ctx.createLinearGradient(0, cy - r * 0.35, 0, cy);
  bg.addColorStop(0, `rgba(${bounce.map((v) => v | 0).join(",")},0)`);
  bg.addColorStop(1, `rgba(${bounce.map((v) => v | 0).join(",")},${(alpha * 0.55).toFixed(3)})`);
  ctx.fillStyle = bg;
  ctx.fillRect(cx - r * 2, cy - r * 0.35, r * 4, r * 0.4);

  // 3. Каждая доля лепится ЧЕТЫРЬМЯ ПЛОСКИМИ СТУПЕНЯМИ, а не градиентом. Градиент — язык
  //    фотореализма; в сторибуке объём держится на ограниченном числе тонов с ясными границами
  //    между ними, и именно счётное число ступеней отличает «нарисовано» от «отрендерено».
  //    Ступени смещаются к источнику (слева-сверху) и уменьшаются — классическая лепка формы.
  const steps: Array<[number, number, RGB]> = [
    [0.94, 0.10, mix(shadow, lit, 0.45)],   // полутон
    [0.72, 0.28, lit],                      // свет
    [0.40, 0.46, lighten(lit, 0.22)]        // блик
  ];

  for (let i = 0; i < lobes.length; i++) {
    const [lx, ly, lr] = lobes[i]!;
    for (const [scale, shift, tone] of steps) {
      ctx.fillStyle = `rgba(${tone.map((v) => v | 0).join(",")},${alpha.toFixed(3)})`;
      // Ступени рисуются обычной дугой: рваность нужна СИЛУЭТУ, а внутренние границы тона от неё
      // только шумят и стоят втрое дороже. Первая версия рвала и их — страница со стендами вставала.
      ctx.beginPath();
      ctx.arc(lx - lr * shift * 0.7, ly - lr * shift * 0.8, lr * scale, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  ctx.restore();

  // 4. Кайма на освещённой стороне: тонкая яркая дуга по верхне-левым шапкам. Она читается как
  //    просвечивающий край и стоит одну линию.
  ctx.save();
  cloudMass(ctx, cx, cy, r, seed);
  ctx.clip();
  ctx.strokeStyle = `rgba(${lighten(lit, 0.4).map((v) => v | 0).join(",")},${(alpha * 0.75).toFixed(3)})`;
  ctx.lineWidth = Math.max(1, r * 0.07);
  for (const [lx, ly, lr] of lobes) {
    ctx.beginPath();
    ctx.arc(lx, ly, lr * 0.94, Math.PI * 1.05, Math.PI * 1.72);
    ctx.stroke();
  }
  ctx.restore();
}

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
/** Тропа строится ПО ТОЧКАМ ПРОХОЖДЕНИЯ, а не выбором из заранее нарисованных изгибов (решение
 *  Макса 02.08.2026). Разница принципиальная: набор вариантов конечен и через три арены узнаётся,
 *  а точки дают непрерывный спектр — иногда почти прямая, иногда крутая дуга, и никто не считал
 *  это отдельным случаем. Ширина ленты тоже гуляет вдоль длины, поэтому тропа не выглядит трубой.
 *
 *  Возвращает опорные точки; сама лента рисуется в strokeTrail. */
function trailPoints(x0: number, y0: number, w: number, h: number, seed: number): Array<[number, number]> {
  const vertical = jag(seed, 19) > 0.55;
  const n = 3 + Math.floor(jag(seed, 21) * 2); // три-четыре точки
  const pts: Array<[number, number]> = [];

  for (let i = 0; i < n; i++) {
    const t = i / (n - 1);
    // Вдоль оси идём равномерно, поперёк — гуляем свободно. Так тропа всегда пересекает поле
    // насквозь и при этом нигде не повторяет свой прошлый путь.
    const along = -0.08 + t * 1.16;
    const across = 0.16 + jag(seed + i * 13, 31) * 0.68;
    pts.push(vertical ? [x0 + w * across, y0 + h * along] : [x0 + w * along, y0 + h * across]);
  }
  return pts;
}

/** Лента по точкам: сглаженная кривая переменной ширины. */
function strokeTrail(ctx: CanvasRenderingContext2D, pts: Array<[number, number]>, width: number, seed: number): void {
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i]!;
    const b = pts[i + 1]!;
    const mid: [number, number] = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
    const prev = pts[i - 1] ?? a;
    const c1: [number, number] = [(prev[0] + a[0] * 3) / 4, (prev[1] + a[1] * 3) / 4];
    ctx.lineWidth = width * (0.75 + jag(seed + i * 7, 41) * 0.6);
    ctx.beginPath();
    ctx.moveTo(c1[0], c1[1]);
    ctx.quadraticCurveTo(a[0], a[1], mid[0], mid[1]);
    ctx.lineTo(b[0], b[1]);
    ctx.stroke();
  }
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
function grassTuft(ctx: CanvasRenderingContext2D, x: number, y: number, s: number, seed: number, color: RGB, leaves = true): void {
  // Каждый пятый пучок — ЛИСТ, а не метёлка. Один силуэт, повторённый полсотни раз, читается
  // штампом; второй тип формы снимает это за десять строк.
  if (leaves && jag(seed, 51) > 0.8) {
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

/* ---------- переиспользуемые архетипы ----------
   Каждый — ОДНА форма, меняющая смысл вместе с цветом биома: упавший ствол становится сломанной
   колонной, столб — сталагмитом, трещина — светящимся разломом. Так и делают переиспользуемые
   пропсы: общая геометрия плюс параметр материала, а не отдельный набор картинок на биом. */

/** БРЕВНО — единственная длинная горизонталь на арене. Композиции она нужна: всё остальное
 *  либо мелкое, либо круглое, и картинке не за что зацепиться по ширине. */
function logProp(ctx: CanvasRenderingContext2D, x: number, y: number, len: number, seed: number, color: RGB): void {
  const th = len * 0.17;
  const ang = (jag(seed, 3) - 0.5) * 0.7;
  ctx.save();
  ctx.translate(x, y);
  ctx.rotate(ang);

  ctx.fillStyle = "rgba(20,16,24,.3)";
  ctx.beginPath();
  ctx.ellipse(th * 0.3, th * 0.55, len * 0.5, th * 0.6, 0, 0, Math.PI * 2);
  ctx.fill();

  ctx.beginPath();
  ctx.moveTo(-len * 0.5, -th * 0.5);
  ctx.lineTo(len * 0.5, -th * 0.5);
  ctx.quadraticCurveTo(len * 0.5 + th * 0.4, 0, len * 0.5, th * 0.5);
  ctx.lineTo(-len * 0.5, th * 0.5);
  ctx.quadraticCurveTo(-len * 0.5 - th * 0.4, 0, -len * 0.5, -th * 0.5);
  ctx.closePath();
  ctx.fillStyle = rgb(color);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE;
  ctx.lineJoin = "round";
  ctx.stroke();

  // Освещённая верхняя половина: свет слева-сверху, как у всего остального.
  ctx.save();
  ctx.beginPath();
  ctx.rect(-len * 0.5, -th * 0.5, len, th * 0.42);
  ctx.clip();
  ctx.fillStyle = `rgba(${lighten(color, 0.24).map((v) => v | 0).join(",")},.9)`;
  ctx.fillRect(-len, -th, len * 2, th);
  ctx.restore();

  // Торец: без него бревно читается доской.
  ctx.beginPath();
  ctx.ellipse(len * 0.5, 0, th * 0.28, th * 0.5, 0, 0, Math.PI * 2);
  ctx.fillStyle = rgb(shade(color, 0.22));
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE * 0.7;
  ctx.stroke();

  ctx.restore();
}

/** ТРЕЩИНА — ломаная по поверхности, с ветвями. Единственный архетип, который может СВЕТИТЬСЯ
 *  изнутри земли: в пещере холодным, на пепелище тлеющим. */
function crackProp(ctx: CanvasRenderingContext2D, x: number, y: number, len: number, seed: number, dark: RGB, glow: RGB | null): void {
  const seg = 5;
  const ang = jag(seed, 5) * Math.PI * 2;
  const pts: Array<[number, number]> = [];
  let cx = x;
  let cy = y;
  for (let i = 0; i <= seg; i++) {
    pts.push([cx, cy]);
    const a = ang + (jag(seed + i * 7, 11) - 0.5) * 1.5;
    cx += Math.cos(a) * (len / seg);
    cy += Math.sin(a) * (len / seg) * 0.55;
  }

  const drawLine = (width: number, style: string) => {
    ctx.strokeStyle = style;
    ctx.lineWidth = width;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.beginPath();
    ctx.moveTo(pts[0]![0], pts[0]![1]);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i]![0], pts[i]![1]);
    ctx.stroke();
    // Две ветви от случайных узлов — без них трещина читается царапиной.
    for (let i = 1; i < pts.length - 1; i++) {
      if (jag(seed + i * 13, 17) < 0.55) continue;
      const p = pts[i]!;
      const a = ang + (jag(seed + i, 19) - 0.5) * 2.6;
      ctx.beginPath();
      ctx.moveTo(p[0], p[1]);
      ctx.lineTo(p[0] + Math.cos(a) * len * 0.22, p[1] + Math.sin(a) * len * 0.14);
      ctx.stroke();
    }
  };

  if (glow) {
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    drawLine(LINE * 3.2, `rgba(${glow.join(",")},.16)`);
    ctx.restore();
  }
  drawLine(LINE * 1.5, rgb(ink(dark, 0.34)));
  if (glow) drawLine(LINE * 0.55, `rgba(${lighten(glow, 0.3).join(",")},.9)`);
}

/** СТОЛБ — единственный архетип с высотой: пень, сталагмит, обломок колонны. Даёт вертикальный
 *  акцент и длинную тень, то есть единственный намёк на третье измерение. */
function pillarProp(ctx: CanvasRenderingContext2D, x: number, y: number, h: number, seed: number, color: RGB): void {
  const w = h * (0.36 + jag(seed, 3) * 0.16);
  const lean = (jag(seed, 7) - 0.5) * 0.18;

  // Длинная тень по направлению света: слева-сверху, значит тень уходит вправо-вниз.
  ctx.fillStyle = "rgba(20,16,24,.3)";
  ctx.beginPath();
  ctx.moveTo(x - w * 0.4, y);
  ctx.lineTo(x + w * 0.4, y);
  ctx.lineTo(x + w * 0.4 + h * 0.7, y + h * 0.34);
  ctx.lineTo(x + w * 0.1 + h * 0.7, y + h * 0.38);
  ctx.closePath();
  ctx.fill();

  ctx.beginPath();
  ctx.moveTo(x - w * 0.5, y);
  ctx.lineTo(x - w * 0.34 + lean * h, y - h);
  ctx.lineTo(x + w * 0.34 + lean * h, y - h * 0.94);
  ctx.lineTo(x + w * 0.5, y);
  ctx.closePath();
  ctx.fillStyle = rgb(color);
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE;
  ctx.lineJoin = "round";
  ctx.stroke();

  // Освещённая грань: левая половина светлее.
  ctx.save();
  ctx.beginPath();
  ctx.moveTo(x - w * 0.5, y);
  ctx.lineTo(x - w * 0.34 + lean * h, y - h);
  ctx.lineTo(x + lean * h, y - h * 0.97);
  ctx.lineTo(x, y);
  ctx.closePath();
  ctx.clip();
  ctx.fillStyle = `rgba(${lighten(color, 0.26).map((v) => v | 0).join(",")},.92)`;
  ctx.fillRect(x - w, y - h, w * 2, h);
  ctx.restore();

  // Скол на вершине: ровный срез читается столбиком из конструктора.
  ctx.beginPath();
  ctx.ellipse(x + lean * h, y - h * 0.97, w * 0.34, w * 0.14, 0, 0, Math.PI * 2);
  ctx.fillStyle = rgb(lighten(color, 0.34));
  ctx.fill();
  ctx.strokeStyle = rgb(ink(color, 0.45));
  ctx.lineWidth = LINE * 0.6;
  ctx.stroke();
}

/** Замкнутая органическая клякса: радиус гуляет шумом по углу. Для ЛУЖИ это уместно (у воды нет
 *  своей формы, она принимает форму углубления) — в отличие от участков земли, где клякса как раз
 *  и читалась «непонятной фигурой». */
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

/** ЛУЖА — проба к разговору «сможем ли повторить водный шейдер».
 *
 *  Здесь нарочно НЕ фотовода. Из полного водного шейдера (рефракция, каустика, волны Герстнера,
 *  planar reflections через вторую камеру) нашему языку нужны четыре вещи, и все дешёвые:
 *  плоское пятно цвета, светлая кайма, пара бегущих бликов и ОТРАЖЕНИЕ СИЛУЭТОМ.
 *
 *  Про отражение честно: при взгляде сверху в воде отражается небо, а не тот, кто рядом. Отражение
 *  юнита — условность, и работает она именно как условность: силуэт, опрокинутый от точки касания,
 *  сплющенный, размытый по краю и сдвигаемый рябью. Физику здесь изображать нечем и незачем.
 *
 *  В движке: маска лужи в стенсиль, силуэты юнитов рисуются вторым проходом с flip по Y, тинтом и
 *  UV-сдвигом от шума по времени — то есть без второй камеры и без RenderTexture. */
function puddleProp(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  r: number,
  seed: number,
  water: RGB,
  reflect: Array<[number, number, number]> = []
): void {
  blobPath(ctx, cx, cy, r, r * 0.52, 0.3, seed);

  ctx.save();
  ctx.clip();

  // 1. Тело: плоский цвет с лёгким уходом в глубину к центру.
  const body = ctx.createRadialGradient(cx, cy, r * 0.1, cx, cy, r);
  body.addColorStop(0, rgb(shade(water, 0.22)));
  body.addColorStop(1, rgb(water));
  ctx.fillStyle = body;
  ctx.fillRect(cx - r * 1.4, cy - r, r * 2.8, r * 2);

  // 2. Отражения силуэтов: опрокинуты вниз, сплющены, полупрозрачны. Обрезаются маской лужи —
  //    поэтому отражение честно «влезает» только в воду.
  for (const [ux, uh, tint] of reflect) {
    ctx.save();
    ctx.globalAlpha = 0.34;
    ctx.translate(ux, cy);
    ctx.scale(1, -0.55); // опрокинуть и сплющить: вода не зеркало, а лужа
    ctx.fillStyle = `rgb(${(70 + tint * 40) | 0},${(60 + tint * 30) | 0},${(58 + tint * 26) | 0})`;
    ctx.beginPath();
    ctx.moveTo(-HUMAN_W * 0.5, 0);
    ctx.lineTo(-HUMAN_W * 0.6, uh * 0.62);
    ctx.quadraticCurveTo(0, uh * 0.78, HUMAN_W * 0.6, uh * 0.62);
    ctx.lineTo(HUMAN_W * 0.5, 0);
    ctx.closePath();
    ctx.fill();
    ctx.beginPath();
    ctx.ellipse(0, uh * 0.82, HUMAN_W * 0.5, uh * 0.13, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  // 3. Рябь: горизонтальные светлые полосы, разрывающие отражение. Именно они делают воду водой —
  //    без них отражение читается второй фигурой, а не отражением.
  ctx.globalAlpha = 0.5;
  for (let i = 0; i < 7; i++) {
    const ry = cy - r * 0.5 + (i / 7) * r;
    const rw = r * (0.4 + jag(i, seed + 3) * 0.8);
    const rx = cx + (jag(i, seed + 5) - 0.5) * r * 0.7;
    ctx.strokeStyle = `rgba(${lighten(water, 0.55).map((v) => v | 0).join(",")},${(0.16 + jag(i, seed + 7) * 0.2).toFixed(2)})`;
    ctx.lineWidth = Math.max(1, r * 0.035);
    ctx.beginPath();
    ctx.moveTo(rx - rw * 0.5, ry);
    ctx.lineTo(rx + rw * 0.5, ry);
    ctx.stroke();
  }
  ctx.globalAlpha = 1;
  ctx.restore();

  // 4. Кайма: мокрый край темнее воды и земли. Без неё лужа выглядит наклейкой.
  blobPath(ctx, cx, cy, r, r * 0.52, 0.3, seed);
  ctx.strokeStyle = rgb(ink(water, 0.5));
  ctx.lineWidth = LINE * 0.9;
  ctx.lineJoin = "round";
  ctx.stroke();

  // 5. Блик по дальней кромке: единственное место, где вода ловит свет источника.
  ctx.strokeStyle = `rgba(${lighten(water, 0.7).map((v) => v | 0).join(",")},.5)`;
  ctx.lineWidth = LINE * 0.6;
  ctx.beginPath();
  ctx.ellipse(cx, cy, r * 0.86, r * 0.44, 0, Math.PI * 1.15, Math.PI * 1.75);
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
  /** Время суток и облачность. Не заданы — день при обычной облачности. */
  tod?: TimeOfDay;
  weather?: Weather;
  /** Витрина приёмов: какие визуальные слои включены. Не задано — включены все. Пустой объект —
   *  голая база, по которой видно, что даёт каждая правка по отдельности. */
  layers?: Partial<Record<"masses" | "boulder" | "colorAccent" | "edgeShade" | "leaves" | "grain" | "props", boolean>>;
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
    // Витрина: если layers задан, включено только перечисленное. Иначе включено всё.
    const on = (k: "masses" | "boulder" | "colorAccent" | "edgeShade" | "leaves" | "grain" | "props") =>
      o.layers ? o.layers[k] === true : true;
    const tod = o.tod ?? DAY;
    const wx = o.weather ?? NORMAL;

    // ПОД ПЛИТОЙ небо уходит в непроглядную бездну (решение Макса): у кромки — цвет неба текущего
    // времени суток, ниже — падение в почти чёрное. Именно падение, а не ровная темнота: ровная
    // читалась бы фоном, а градиент — глубиной.
    const vg = ctx.createLinearGradient(0, 0, 0, h);
    vg.addColorStop(0, rgb(shade(tod.skyNear, 0.55)));
    vg.addColorStop(0.42, rgb(tod.skyNear));
    vg.addColorStop(1, rgb(tod.abyss));
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    // ОБЛАЧНОЕ ПОЛЕ — задний фон (реф Dead Weight, docs/art-refs/dead-weight).
    // Главная поправка после рефа: облака это ПОВЕРХНОСТЬ, а не отдельные штучки в пустоте.
    // Поле собрано рядами, уходящими к горизонту: чем выше в кадре, тем ряд мельче, плотнее и
    // бледнее — атмосферная перспектива делает глубину без единого приёма сверх этого.
    // Три плоских тона на массу (тень снизу, тело, подсвеченный верх) — ровно язык сторибука,
    // и в рефе сделано так же.
    {
      const rows = 6;
      for (let row = 0; row < rows; row++) {
        // 0 — у горизонта, 1 — ближний край кадра.
        const depth = row / (rows - 1);
        const rowY = h * (0.06 + Math.pow(depth, 1.6) * 0.92);
        const scale = 0.28 + depth * 0.95;
        // Дальние ряды бледнее: их съедает воздух.
        const haze = 0.45 + depth * 0.55;
        // Дальние ряды ГУЩЕ по числу: у горизонта поле смыкается в сплошную полосу, ближе к нам
        // расходится на отдельные массы. В рефе именно так и читается облачное море.
        const perRow = Math.max(3, Math.round(wx.clouds * (1.9 - depth * 1.0) / 2));

        for (let i = 0; i < perRow; i++) {
          const salt = row * 31 + i * 7;
          // Позиция РАВНОМЕРНАЯ по индексу плюс джиттер, а не чистый случай. Чистый случай
          // кучкует облака и оставляет дыры — та же ошибка, что была у россыпи на земле.
          const even = (i + 0.5) / perRow;
          const jitter = (jag(salt, o.seed + 101) - 0.5) * (1.4 / perRow);
          const cxp = (even + jitter) * w * 1.25 - w * 0.12;
          const cyp = rowY + (jag(salt, o.seed + 103) - 0.5) * h * 0.05;
          // Массы перекрываются: радиус берётся с запасом относительно шага ряда, поэтому
          // соседи налезают друг на друга и поле читается сплошным, а не пунктиром.
          const cr = w * (0.055 + jag(salt, o.seed + 105) * 0.075) * scale * (1 + 0.5 / perRow);

          // ЦВЕТА МАССЫ по теории: свет тёплый, тень СВЕТЛАЯ и уходит в фиолет (смесь синего
          // неба сверху и тёплого отражения снизу), дно ловит отражённый свет.
          const litness = wx.storm ? 0.45 : 0.92;
          const lit: RGB = [
            lerp(tod.shadow[0], lighten(tod.light, 0.3)[0], litness),
            lerp(tod.shadow[1], lighten(tod.light, 0.3)[1], litness),
            lerp(tod.shadow[2], lighten(tod.light, 0.3)[2], litness)
          ];
          // Тень НЕ тёмная: держим её близко к свету по яркости и разводим по цвету.
          const shadowTone: RGB = [
            lerp(lit[0], tod.shadow[0], wx.storm ? 0.62 : 0.42) * 1.02,
            lerp(lit[1], tod.shadow[1], wx.storm ? 0.62 : 0.42),
            lerp(lit[2], tod.shadow[2], wx.storm ? 0.55 : 0.34) * 1.06
          ];
          // Отражённый свет снизу: тёплая охра от земли под облаками.
          const bounce: RGB = [
            lerp(shadowTone[0], tod.light[0], 0.5),
            lerp(shadowTone[1], tod.light[1], 0.34),
            lerp(shadowTone[2], tod.light[2], 0.12)
          ];

          paintCloudVolume(ctx, cxp, cyp, cr, o.seed + salt, lit, shadowTone, bounce, wx.cloudAlpha * haze);
        }
      }
    }

    // Геометрия плиты: поле 640×384 по центру кадра, борт под ним.
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
    if (on("masses")) for (let m = 0; m < 3; m++) {
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

    // 1. ТРОПА по точкам прохождения. На части сидов её НЕТ ВОВСЕ — так решил Макс, и это делает
    //    тропу событием, а не обязательным украшением: арена без неё читается как нехоженое место.
    const hasTrail = jag(o.seed, 17) > 0.25;
    if (hasTrail) {
      const pts = trailPoints(x0, y0, pw, ph, o.seed);
      const tw = HUMAN_W * (2.2 + jag(o.seed, 11) * 1.6);

      // Тень тропы: та же лента, сдвинутая вниз и затемнённая. Земля получает толщину.
      ctx.strokeStyle = `rgba(${shade(b.trail, 0.42).map((v) => v | 0).join(",")},.5)`;
      ctx.save();
      ctx.translate(0, 3);
      strokeTrail(ctx, pts, tw, o.seed);
      ctx.restore();

      ctx.strokeStyle = rgb(b.trail);
      strokeTrail(ctx, pts, tw, o.seed);

      // Развилка: от середины уходит вторая, более узкая ветвь. Намекает, что местом пользовались
      // не по одному маршруту.
      if (jag(o.seed, 29) > 0.55) {
        const from = pts[Math.max(1, Math.floor(pts.length / 2))]!;
        const dir = jag(o.seed, 33) > 0.5 ? 1 : -1;
        const branch: Array<[number, number]> = [
          from,
          [from[0] + pw * 0.16 * dir, from[1] + ph * (jag(o.seed, 37) - 0.5) * 0.5],
          [from[0] + pw * 0.42 * dir, y0 + ph * (0.1 + jag(o.seed, 39) * 0.8)]
        ];
        ctx.strokeStyle = rgb(b.trail);
        strokeTrail(ctx, branch, tw * 0.62, o.seed + 5);
      }
    }

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
    if (on("boulder")) {
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
    if (on("colorAccent")) {
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

    // 2г. ПРЕДМЕТЫ БИОМА — бревно, трещина, столб. Три архетипа, выбранные Максом: одна форма,
    //     меняющая смысл вместе с цветом. Бревно даёт единственную длинную горизонталь, столб —
    //     единственную вертикаль с тенью, трещина — единственное, что светится изнутри земли.
    //     Ставятся не в центр: там дерутся (та же причина, по которой скупа растительность).
    if (on("props")) {
      const edgeSpot = (salt: number): [number, number] => {
        const side = jag(salt, 61);
        const fx = side < 0.5 ? 0.10 + jag(salt, 63) * 0.24 : 0.66 + jag(salt, 65) * 0.24;
        const fy = 0.14 + jag(salt, 67) * 0.72;
        return [x0 + pw * fx, y0 + ph * fy];
      };

      if (jag(o.seed, 71) > 0.35) {
        const [lx, ly] = edgeSpot(o.seed + 1);
        logProp(ctx, lx, ly, HUMAN_H * (2.2 + jag(o.seed, 73) * 1.4), o.seed + 3, b.prop);
      }
      if (jag(o.seed, 77) > 0.4) {
        const [cx2, cy2] = edgeSpot(o.seed + 2);
        crackProp(ctx, cx2, cy2, HUMAN_H * (1.8 + jag(o.seed, 79) * 1.6), o.seed + 7, b.hard, b.crackGlow);
      }
      if (jag(o.seed, 83) > 0.45) {
        const [px2, py2] = edgeSpot(o.seed + 3);
        pillarProp(ctx, px2, py2, HUMAN_H * (0.8 + jag(o.seed, 85) * 0.7), o.seed + 11, b.prop);
      }
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
      if (b.plantKind === "grass") grassTuft(ctx, px, py, s, o.seed + i * 17, b.plant, on("leaves"));
      else if (b.plantKind === "shard") shardShape(ctx, px, py, s, o.seed + i * 17, b.plant);
      else boneShape(ctx, px, py, s, o.seed + i * 17, b.plant);
    }

    // 4. ЗЕРНО поверх всего: живописная фактура вместо ровной заливки. Главная разница между
    //    «дёшево» и «дорого» в плоской графике, и стоит она одного прохода.
    if (on("grain")) {
    ctx.save();
    ctx.globalCompositeOperation = "overlay";
    ctx.globalAlpha = 0.16;
    const gt = grain();
    for (let gy = y0; gy < y0 + ph; gy += 128) {
      for (let gx = x0; gx < x0 + pw; gx += 128) ctx.drawImage(gt, gx, gy);
    }
    ctx.restore();
    }

    // 5. НАПРАВЛЕННЫЙ СВЕТ вместо круглой виньетки: слева-сверху выбито, справа-снизу уходит
    //    в холод. Это и есть «объём и свет отдаёт движок» из канона.
    // Свет и тень берут цвет у времени суток. Тень КОМПЛЕМЕНТАРНА свету — этим она отличается от
    // простого затемнения. Сила прямого света падает к сумеркам, и картинка становится плоской
    // не потому, что тёмная, а потому что рассеянная.
    const lg = ctx.createLinearGradient(x0, y0, x0 + pw * 0.9, y0 + ph);
    lg.addColorStop(0, `rgba(${tod.light.map((v) => v | 0).join(",")},${(0.17 * tod.lightPower).toFixed(3)})`);
    lg.addColorStop(0.45, `rgba(${tod.light.map((v) => v | 0).join(",")},0)`);
    lg.addColorStop(1, `rgba(${tod.shadow.map((v) => v | 0).join(",")},${(0.2 + 0.26 * (1 - tod.lightPower)).toFixed(3)})`);
    ctx.fillStyle = lg;
    ctx.fillRect(x0, y0, pw, ph);

    // Слабая покраска земли — «немного влияния цвета на арену», как просил Макс. Держим низкой:
    // цвет у нас несёт информацию о стихии удара, и перекрашенная арена съела бы этот канал.
    ctx.fillStyle = `rgba(${tod.groundTint.map((v) => v | 0).join(",")},${tod.groundTintAmount.toFixed(3)})`;
    ctx.fillRect(x0, y0, pw, ph);

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

    // ГРОЗА: вспышка подсвечивает арену, но ЕДВА — боевые вспышки несут информацию, и спорить
    // с ними погода не имеет права.
    if (wx.storm) {
      ctx.fillStyle = "rgba(198,214,255,.1)";
      ctx.fillRect(0, 0, w, h);
      const bx = x0 + pw * (0.2 + jag(o.seed, 111) * 0.6);
      ctx.strokeStyle = "rgba(226,238,255,.85)";
      ctx.lineWidth = 2;
      ctx.lineJoin = "miter";
      ctx.beginPath();
      ctx.moveTo(bx, 0);
      let ly = 0;
      let lx = bx;
      while (ly < y0 * 0.8) {
        ly += y0 * 0.3;
        lx += (jag(ly, o.seed + 113) - 0.5) * pw * 0.12;
        ctx.lineTo(lx, ly);
      }
      ctx.stroke();
    }

    // Экспозиция времени суток — последний штрих, как пост-обработка в движке.
    if (tod.exposure < 1) {
      ctx.fillStyle = `rgba(${tod.abyss.map((v) => v | 0).join(",")},${(1 - tod.exposure).toFixed(3)})`;
      ctx.fillRect(0, 0, w, h);
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
/** ВИТРИНА ПРИЁМОВ: голая база и та же картинка с ОДНОЙ включённой правкой. Так видно вклад
 *  каждого приёма по отдельности — на общей сборке они смешиваются, и спорить о них невозможно. */
const LAYER_SHOWCASE: Array<{ key: "masses" | "boulder" | "colorAccent" | "edgeShade" | "leaves" | "grain" | "props"; title: string; note: string }> = [
  { key: "masses", title: "+ Тональные массы", note: "Несколько крупных плоскостей разной светлоты. Глубину даёт число ПЛАНОВ, а не число деталей — приём фонов Samurai Jack." },
  { key: "boulder", title: "+ Крупный акцент", note: "Один валун у края. Когда всё одного размера, глазу не за что зацепиться и поле читается шумом." },
  { key: "colorAccent", title: "+ Цветовое пятно", note: "Один чужой тон, около пяти процентов площади. Гамма без него выглядит выцветшей, а не сдержанной." },
  { key: "edgeShade", title: "+ Притенение краёв", note: "Кайма вдоль кромок плиты, а не круглая виньетка: у обрыва свет не заворачивает, и плита получает толщину." },
  { key: "leaves", title: "+ Второй силуэт", note: "Каждый пятый пучок — лист. Одна форма, повторённая полсотни раз, читается штампом." },
  { key: "grain", title: "+ Зерно", note: "Шум двух масштабов поверх заливок. Идеально ровный цвет читается как заливка в редакторе." },
  { key: "props", title: "+ Предметы биома", note: "Бревно, трещина, столб. Горизонталь, свечение из земли и единственная вертикаль с тенью." }
];

const SHOWCASE_STANDS: StandDef[] = [
  {
    id: "base",
    status: "note" as const,
    title: "Оригинал",
    tag: "база без правок",
    note: "Земля, тропа, порода, трава. Всё, что ниже, добавляет ровно один приём поверх этого.",
    draw: slab({ biome: MEADOW, rimU: 1, seed: 27, crop: true, layers: {} })
  },
  ...LAYER_SHOWCASE.map((l) => ({
    id: `layer-${l.key}`,
    status: "waiting" as const,
    title: l.title,
    note: l.note,
    draw: slab({ biome: MEADOW, rimU: 1, seed: 27, crop: true, layers: { [l.key]: true } })
  }))
];

/** Шкала времени суток при обычной облачности: видно всю логику света на одном ряду. */
const TIME_STANDS: StandDef[] = TIMES.map((t) => ({
  id: `tod-${t.id}`,
  status: "waiting" as const,
  title: t.name,
  facts: [
    ["свет", `${Math.round(t.lightPower * 100)}%`],
    ["длина теней", `${t.shadowLen}×роста`],
    ["покраска земли", `${Math.round(t.groundTintAmount * 100)}%`]
  ],
  size: [430, 330] as [number, number],
  draw: slab({ biome: MEADOW, rimU: 1, seed: 27, unit: true, digital: true, tod: t, weather: NORMAL })
}));

/** Шкала облачности днём: меняется только погода. */
const WEATHER_STANDS: StandDef[] = WEATHERS.map((wv) => ({
  id: `wx-${wv.id}`,
  status: "waiting" as const,
  title: wv.name,
  facts: [["облаков", String(wv.clouds)], ["плотность", `${Math.round(wv.cloudAlpha * 100)}%`]],
  size: [430, 330] as [number, number],
  draw: slab({ biome: MEADOW, rimU: 1, seed: 27, unit: true, digital: true, tod: DAY, weather: wv })
}));

/** Комбинации, ради которых всё и затевалось. */
const COMBO_STANDS: StandDef[] = [
  { t: SUNSET, wv: CLOUDY, note: "Закат при плотных облаках: горящие кромки и длинные тени." },
  { t: DUSK, wv: STORM, note: "Гроза в сумерках: тёмное небо, вспышка, почти нет прямого света." },
  { t: MORNING, wv: CLEAR, note: "Утро в ясную погоду: чисто, холодновато, тени ещё длинные." }
].map(({ t, wv, note }) => ({
  id: `combo-${t.id}-${wv.id}`,
  status: "waiting" as const,
  title: `${t.name} · ${wv.name}`,
  note,
  size: [430, 330] as [number, number],
  draw: slab({ biome: MEADOW, rimU: 1, seed: 27, unit: true, digital: true, tod: t, weather: wv })
}));

/** Проба воды: лужа с отражениями и те же фигуры рядом. Стенд собран отдельно от slab, потому что
 *  отражению нужно знать, ГДЕ стоят фигуры, — а в общей сборке они рисуются после земли. */
function waterProbe(withReflection: boolean): DrawFn {
  return (ctx, w, h) => {
    const b = MEADOW;
    ctx.fillStyle = rgb(b.ground);
    ctx.fillRect(0, 0, w, h);

    for (let m = 0; m < 2; m++) {
      const tint = m === 0 ? lighten(b.ground, 0.09) : shade(b.ground, 0.11);
      const g = ctx.createRadialGradient(w * (0.3 + m * 0.4), h * 0.4, w * 0.05, w * (0.3 + m * 0.4), h * 0.4, w * 0.5);
      g.addColorStop(0, `rgba(${tint.map((v) => v | 0).join(",")},.8)`);
      g.addColorStop(1, `rgba(${tint.map((v) => v | 0).join(",")},0)`);
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, w, h);
    }

    const water: RGB = [72, 104, 116];
    const cx = w * 0.5;
    const cy = h * 0.62;
    const r = w * 0.34;

    // Фигуры стоят у ближней кромки — отражение уходит от их ног в воду.
    const figs: Array<[number, number]> = [[cx - r * 0.45, HUMAN_H * 0.95], [cx + r * 0.42, HUMAN_H * 1.15]];

    puddleProp(ctx, cx, cy, r, 3, water, withReflection ? figs.map(([x, hh], i) => [x, hh, i === 0 ? 1 : 0] as [number, number, number]) : []);

    for (let i = 0; i < figs.length; i++) {
      const [fx, fh] = figs[i]!;
      ctx.save();
      ctx.translate(0, 0);
      const body: RGB = i === 0 ? [150, 116, 96] : [116, 92, 80];
      ctx.beginPath();
      ctx.moveTo(fx - HUMAN_W * 0.5, cy);
      ctx.lineTo(fx - HUMAN_W * 0.6, cy - fh * 0.62);
      ctx.quadraticCurveTo(fx, cy - fh * 0.78, fx + HUMAN_W * 0.6, cy - fh * 0.62);
      ctx.lineTo(fx + HUMAN_W * 0.5, cy);
      ctx.closePath();
      ctx.fillStyle = rgb(body);
      ctx.fill();
      ctx.strokeStyle = rgb(ink(body, 0.45));
      ctx.lineWidth = LINE * 0.8;
      ctx.lineJoin = "round";
      ctx.stroke();
      ctx.beginPath();
      ctx.ellipse(fx, cy - fh * 0.8, HUMAN_W * 0.5, fh * 0.13, 0, 0, Math.PI * 2);
      ctx.fillStyle = rgb(lighten(body, 0.14));
      ctx.fill();
      ctx.strokeStyle = rgb(ink(body, 0.45));
      ctx.stroke();
      ctx.restore();
    }

    const lg = ctx.createLinearGradient(0, 0, w * 0.9, h);
    lg.addColorStop(0, "rgba(255,238,198,.14)");
    lg.addColorStop(0.45, "rgba(255,238,198,0)");
    lg.addColorStop(1, "rgba(24,26,54,.3)");
    ctx.fillStyle = lg;
    ctx.fillRect(0, 0, w, h);
  };
}

const WATER_STANDS: StandDef[] = [
  {
    id: "water-flat",
    status: "note",
    title: "Лужа без отражения",
    tag: "четыре дешёвых слоя",
    note: "Плоское пятно, кайма, рябь, блик по дальней кромке.",
    facts: [["слоёв", "4"], ["рефракции", "нет"], ["второй камеры", "нет"]],
    verdict: "Уже читается водой. Всё, что здесь есть, — цвет, обводка и несколько линий.",
    size: [480, 340],
    draw: waterProbe(false)
  },
  {
    id: "water-reflect",
    status: "waiting",
    title: "Лужа с отражением",
    tag: "то, ради чего затевалось",
    note: "Силуэт опрокинут от точки касания, сплющен, разорван рябью.",
    facts: [["приём", "flip Y + сплющивание"], ["обрезка", "маской лужи"], ["рябь", "рвёт силуэт"]],
    verdict:
      "Условность, а не физика: сверху в воде отражается небо. Но именно как условность это читается — " +
      "и рябь тут обязательна, без неё отражение выглядит второй фигурой, а не отражением.",
    size: [480, 340],
    draw: waterProbe(true)
  }
];

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
      id: "tod",
      title: "Время суток",
      lede: "Меняется только время. Облачность у всех четырёх — «Обычно»."
    },
    { kind: "stands", items: TIME_STANDS },
    {
      kind: "table",
      head: ["Что гуляет от времени суток", "Насколько", "Почему так"],
      rows: [
        ["Небо и бездна под плитой", "<b>очень сильно</b>", "это фон, ему можно всё"],
        ["Длина теней", "<b>сильно</b>", "полдень 0.75 роста, сумерки 3.2 — это и читается как время"],
        ["Цвет света и тени", "умеренно", "тень комплементарна свету, иначе она читается грязью"],
        ["Покраска земли", "5-20%", "выше — и цвет стихии удара перестанет различаться"],
        ["Цвет боевых эффектов", "<b>ноль</b>", "цвет у нас несёт информацию (канон vfx-color)"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Два контринтуитивных правила из ресёрча, и оба здесь применены.</b> Закат <b>мягче</b> " +
        "дня по контрасту: солнце низко, свет рассеивается, тени светлеют — драму даёт цвет и длина " +
        "тени, а не жёсткость. Сумерки — это <b>70/30</b>, большая холодная масса плюс узкая тёплая " +
        "полоса остатка; без тёплого остатка получается не вечер, а просто затемнённый день."
    },
    {
      kind: "head",
      id: "weather",
      title: "Облачность",
      lede: "Меняется только погода, время — день. Облака идут ЗАДНИМ ФОНОМ и плиту не перекрывают."
    },
    { kind: "stands", items: WEATHER_STANDS },
    {
      kind: "note",
      html:
        "Облака — <b>задний фон</b>: плита висит НА фоне неба, а не под пологом. Первая версия " +
        "рисовала их поверх сцены, и поле укрывалось туманом — вместе с этим ушли и тени облаков " +
        "на арене: то, что находится ЗА предметом, тени на него не бросает. Молния подсвечивает " +
        "арену <b>едва</b>: боевые вспышки несут информацию, и спорить с ними погода права не имеет."
    },
    {
      kind: "head",
      id: "combo",
      title: "Комбинации",
      lede: "Ради чего всё и затевалось."
    },
    { kind: "stands", items: COMBO_STANDS },
    {
      kind: "head",
      id: "water",
      title: "Вода: сможем ли повторить",
      lede: "Проба к разговору про водный шейдер. Из полного набора нам нужны четыре слоя из десятка."
    },
    { kind: "split", items: WATER_STANDS },
    {
      kind: "table",
      head: ["Что делает «полный» водный ассет", "Нужно ли нам", "Почему"],
      rows: [
        ["Planar reflections через вторую камеру и RenderTexture", "нет", "силуэт с flip по Y дешевле и в плоском языке честнее"],
        ["Рефракция / GrabPass", "нет", "под водой ничего нет — преломлять нечего"],
        ["Каустика", "нет", "фотореализм, чужой нашему языку"],
        ["Волны Герстнера", "нет", "это трёхмерная поверхность, у нас плоскость"],
        ["Искажение по шуму со сдвигом во времени", "<b>да</b>", "рябь, которая рвёт отражение — обязательна"],
        ["Пена и кромка по краю", "<b>да</b>", "мокрый край; без него лужа читается наклейкой"],
        ["Бегущие блики", "<b>да</b>", "единственное место, где вода ловит свет"],
        ["Интерактивные всплески от юнитов", "потом", "требует контакта с симуляцией"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Развилка, которую стоит держать в голове сразу.</b> Если вода получит игровой смысл " +
        "(замедляет, проводит молнию, тушит горение), она перестаёт быть декором и уезжает в " +
        "симуляцию — а значит расстановка луж становится <b>сим-рандомом</b> и обязана совпадать " +
        "у всех клиентов коопа. Пока вода чисто визуальная, ей достаточно показ-рандома. Дешевле " +
        "решить это до реализации, чем переносить генерацию потом."
    },
    {
      kind: "head",
      id: "showcase",
      title: "Каждый приём по отдельности",
      lede: "Первый стенд — база. Дальше та же картинка с ОДНОЙ включённой правкой."
    },
    { kind: "stands", items: SHOWCASE_STANDS },
    {
      kind: "note",
      html:
        "На общей сборке приёмы смешиваются, и спорить о них невозможно — поэтому здесь они " +
        "разложены. Самый большой вклад даёт <b>не тот приём, который дороже всех</b>: тональные " +
        "массы это три градиента, а меняют они больше, чем валун, зерно и притенение вместе."
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
