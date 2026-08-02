/* Пол арены: плита, висящая в пустоте.

   Заказ Макса 2026-08-02: «хочется сохранить вид, что у нас этакая арена летает где-то над
   пустотой. Чтобы игра прямо говорила — хей, это просто боевая арена, вот поэтому мы показываем
   анимацию создания». Плюс биомы (лес, поляна, пещера) и удержание вида «сбоку» с текущего скрина.

   Почему это вообще решается процедурой, а не тайлсетом. Тайл выдаёт себя решёткой: на площади
   арены глаз находит период за секунду, и пол начинает читаться как обои. Шум периода не имеет,
   поэтому поверхность остаётся поверхностью на любом размере поля. Второе: биом при тайлсете —
   это НОВЫЙ набор картинок, а при процедуре — четыре цвета и три числа, то есть строка в SO.

   Что здесь показано и что нет. Стенды отвечают на два вопроса, которые я вынесла Максу: каким
   должен быть БОРТ (машинный против природного) и какой ТОЛЩИНЫ плита. Биомы идут четвёркой не
   ради красоты, а как доказательство, что смена биома — это данные: все четыре нарисованы ОДНИМ
   кодом, отличаются только конфигом.

   Пиксельность нарочная: поверхность считается блоками по 3 логических пикселя, а не гладко.
   Гладкий градиент в стенде соврал бы — в игре растр обязан совпадать с пиксельным артом юнитов. */

import { COL, jag, drawUnit } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- шум ----------
   Свой, а не общий: стенд обязан быть самодостаточным, а формулы движка живут в
   Assets/_Project/Art/Shaders/Lib/Procedural.hlsl и сюда не ездят. */

function hash2(x: number, y: number, salt: number): number {
  return jag(x * 374761 + y * 668265, salt);
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function vnoise(x: number, y: number, salt: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const xf = x - xi;
  const yf = y - yi;
  const u = xf * xf * (3 - 2 * xf);
  const v = yf * yf * (3 - 2 * yf);
  return lerp(
    lerp(hash2(xi, yi, salt), hash2(xi + 1, yi, salt), u),
    lerp(hash2(xi, yi + 1, salt), hash2(xi + 1, yi + 1, salt), u),
    v
  );
}

function fbm(x: number, y: number, salt: number): number {
  let sum = 0;
  let amp = 0.5;
  let freq = 1;
  for (let o = 0; o < 3; o++) {
    sum += vnoise(x * freq, y * freq, salt + o * 31) * amp;
    amp *= 0.5;
    freq *= 2.07;
  }
  return sum;
}

/* ---------- биом ----------
   ВСЁ, чем один биом отличается от другого. Ни одной картинки: только цвета, три числа шума
   и тип россыпи. Это и есть будущий BiomeData — SO на биом, один шейдер на все. */

type RGB = [number, number, number];

interface Biome {
  id: string;
  name: string;
  /** Основа поверхности и цвет крупных пятен: между ними гуляет fbm. */
  base: RGB;
  patch: RGB;
  /** Мелкая крапка поверх — то, что даёт «фактуру», а не «заливку». */
  speck: RGB;
  speckAmount: number;
  /** Масштаб пятен: мельче число — крупнее пятна. Порядок 0.02 — пятна размером с юнита;
   *  0.1 и выше дают рябь, которая читается как шум телевизора, а не как земля. */
  scale: number;
  /** Три слоя борта сверху вниз. */
  bort: [RGB, RGB, RGB];
  /** Фон вокруг плиты. Не чёрный: чистый чёрный съедает силуэт и убивает bloom. */
  voidTop: string;
  voidBottom: string;
  /** Россыпь: чем усеяна поверхность и насколько густо. */
  detail: "grass" | "rock" | "crystal" | "bone";
  detailCount: number;
  detailColor: string;
}

const MEADOW: Biome = {
  id: "meadow",
  name: "Поляна",
  base: [122, 138, 58],
  patch: [72, 90, 34],
  speck: [150, 168, 78],
  speckAmount: 0.5,
  scale: 0.022,
  bort: [[92, 104, 44], [104, 82, 52], [72, 58, 40]],
  voidTop: "#141019",
  voidBottom: "#0B0910",
  detail: "grass",
  detailCount: 26,
  detailColor: "rgba(168,186,96,.85)"
};

const FOREST: Biome = {
  id: "forest",
  name: "Лес",
  base: [58, 82, 52],
  patch: [30, 46, 30],
  speck: [86, 112, 64],
  speckAmount: 0.62,
  scale: 0.030,
  bort: [[46, 66, 42], [78, 60, 40], [54, 44, 32]],
  voidTop: "#0F1414",
  voidBottom: "#070A0A",
  detail: "grass",
  detailCount: 40,
  detailColor: "rgba(104,140,74,.9)"
};

const CAVE: Biome = {
  id: "cave",
  name: "Пещера",
  base: [72, 68, 82],
  patch: [42, 38, 52],
  speck: [96, 92, 110],
  speckAmount: 0.45,
  scale: 0.026,
  bort: [[60, 56, 70], [48, 44, 56], [32, 30, 40]],
  voidTop: "#0C0A12",
  voidBottom: "#06050A",
  detail: "crystal",
  detailCount: 14,
  detailColor: "rgba(138,206,255,.75)"
};

const ASH: Biome = {
  id: "ash",
  name: "Пепелище",
  base: [86, 74, 68],
  patch: [50, 40, 38],
  speck: [116, 100, 92],
  speckAmount: 0.55,
  scale: 0.024,
  bort: [[70, 60, 56], [56, 44, 40], [38, 30, 28]],
  voidTop: "#130E0C",
  voidBottom: "#090606",
  detail: "bone",
  detailCount: 18,
  detailColor: "rgba(198,180,158,.8)"
};

/* ---------- геометрия сцены ---------- */

interface SlabOpts {
  biome: Biome;
  /** Машинный борт — ровная плита с клеткой перехода. Природный — слоистая порода со сколом. */
  bortStyle: "machined" | "natural";
  bortH: number;
  seed: number;
  /** Показывать юнита для масштаба: без него толщину плиты не с чем сравнить. */
  unit?: boolean;
  /** Тайл вместо шума — только для стенда «как сейчас». */
  tiled?: boolean;
  /** Обрезать плиту низом кадра: тоже только для «как сейчас». */
  clipped?: boolean;
}

const CELL = 3; // квант растра: поверхность считается блоками, а не попиксельно

/** Матрица Байера 4x4 — та же, что в движке (Lib/Procedural.hlsl). Здесь своя копия сознательно:
 *  стенд не имеет права зависеть от сборки Unity. */
const BAYER = [
  0 / 16, 8 / 16, 2 / 16, 10 / 16,
  12 / 16, 4 / 16, 14 / 16, 6 / 16,
  3 / 16, 11 / 16, 1 / 16, 9 / 16,
  15 / 16, 7 / 16, 13 / 16, 5 / 16
];

/** Кластеры фактуры: единица детали — СВЯЗНАЯ форма из нескольких блоков, а не одинокий блок.
 *  По три варианта на материал: одного мало (виден повтор), десяти много (пропадает узнаваемость).
 *  Координаты в блоках CELL от левого верхнего угла кластера. */
const CLUSTERS: Record<Biome["detail"], Array<Array<[number, number]>>> = {
  // Трава: «птичка», уголок и короткий пучок — то, как её рисуют вручную на низком разрешении.
  grass: [
    [[0, 1], [1, 0], [2, 1]],
    [[0, 0], [0, 1], [1, 1]],
    [[1, 0], [0, 1], [1, 1], [2, 1]]
  ],
  // Камень: компактные блоки со сбитым углом — форма читается как обломок, а не как квадрат.
  rock: [
    [[0, 0], [1, 0], [0, 1], [1, 1]],
    [[0, 0], [1, 0], [1, 1]],
    [[0, 1], [1, 0], [1, 1], [2, 1]]
  ],
  // Кристалл: вертикаль с утолщением у основания.
  crystal: [
    [[1, 0], [1, 1], [0, 2], [1, 2]],
    [[0, 0], [0, 1], [1, 1]],
    [[1, 0], [0, 1], [1, 1], [2, 2]]
  ],
  // Кость и обломки: горизонтальные штрихи с засечкой.
  bone: [
    [[0, 0], [1, 0], [2, 0], [2, 1]],
    [[0, 1], [1, 0], [2, 0]],
    [[0, 0], [1, 0], [1, 1]]
  ]
};

function mix(a: RGB, b: RGB, t: number): RGB {
  return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
}

/** Поверхность печётся один раз на конфиг и кэшируется: стенд статичный, а три октавы шума
 *  на каждый блок в реальном времени не нужны никому. */
const surfaceCache = new Map<string, HTMLCanvasElement>();

function paintSurface(b: Biome, w: number, h: number, seed: number, tiled: boolean): HTMLCanvasElement {
  const key = `${b.id}|${w}x${h}|${seed}|${tiled ? "t" : "n"}`;
  const hit = surfaceCache.get(key);
  if (hit) return hit;

  const cv = document.createElement("canvas");
  cv.width = Math.ceil(w);
  cv.height = Math.ceil(h);
  const c = cv.getContext("2d")!;

  // ---- 1. ТОН: три ступени, а не градиент. -------------------------------------------------
  // Пиксель-арт живёт ограниченной палитрой: плавная растяжка между двумя цветами читается как
  // мыло под фильтром, а не как земля. Поэтому тон квантуется в три уровня, а на границах между
  // ними ставится упорядоченный дизеринг — тот самый Байер, что уже лежит в Lib/Procedural.hlsl.
  const tone: RGB[] = [b.patch, mix(b.patch, b.base, 0.55), b.base];

  for (let y = 0; y < h; y += CELL) {
    for (let x = 0; x < w; x += CELL) {
      let t: number;
      if (tiled) {
        const tx = x % 32;
        const ty = y % 32;
        t = vnoise(tx * 0.22, ty * 0.22, 1);
      } else {
        t = fbm(x * b.scale, y * b.scale, seed);
      }

      // Дизеринг: порог по положению блока сдвигает ступень на полшага — переход рвётся в растр,
      // вместо того чтобы размазаться.
      const bay = BAYER[((y / CELL) & 3) * 4 + ((x / CELL) & 3)]!;
      const lvl = Math.min(2, Math.max(0, Math.floor(t * 3 + (bay - 0.5) * 0.9)));
      const col = tone[lvl]!;

      c.fillStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`;
      c.fillRect(x, y, CELL, CELL);
    }
  }

  if (tiled) {
    surfaceCache.set(key, cv);
    return cv;
  }

  // ---- 2. ФАКТУРА: кластерами, а не пикселями. ----------------------------------------------
  // Главный урок ресёрча (Slynyrd, Pixelblog-2): одиночный пиксель, не примыкающий к соседям, —
  // «сирота», и глаз читает его как грязь. Фактуру делают НЕСКОЛЬКО простых связных кластеров,
  // разбросанных с РАЗНОЙ плотностью; пустые участки так же обязательны, как занятые, потому что
  // равномерная густота выглядит неестественно и суетливо.
  const step = CELL * 4;
  for (let y = 0; y < h; y += step) {
    for (let x = 0; x < w; x += step) {
      // Низкочастотный шум решает, ГДЕ густо. Возведение в степень — не косметика: при линейной
      // плотности кластеры ложатся почти всюду, и получается тот самый «однородный ковёр», который
      // ресёрч называет главной ошибкой фактуры. Степень выдавливает середину, оставляя явные
      // проплешины и явные заросли, — то есть негативное пространство, без которого фактура
      // читается как заполнение, а не как земля.
      const dens = fbm(x * b.scale * 0.5, y * b.scale * 0.5, seed + 77);
      if (hash2(x, y, seed + 13) > Math.pow(dens, 2.4) * b.speckAmount * 1.6) continue;

      const shape = CLUSTERS[b.detail][Math.floor(hash2(x, y, seed + 29) * 3) % 3]!;
      // Два оттенка кластера вместо одного: ровный цвет по всей россыпи делает её наклейкой.
      const col = hash2(x, y, seed + 53) > 0.62 ? mix(b.speck, b.base, 0.45) : b.speck;
      c.fillStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`;
      for (const [dx, dy] of shape) {
        c.fillRect(x + dx * CELL, y + dy * CELL, CELL, CELL);
      }
    }
  }

  surfaceCache.set(key, cv);
  return cv;
}

/** Россыпь: спрайты в игре рисует человек, машина только расставляет их по сиду.
 *  Здесь вместо спрайтов схематичные знаки — предмет разбора расстановка, а не рисунок. */
function scatter(ctx: CanvasRenderingContext2D, b: Biome, x0: number, y0: number, w: number, h: number, seed: number): void {
  ctx.fillStyle = b.detailColor;
  ctx.strokeStyle = b.detailColor;
  ctx.lineWidth = 1;

  for (let i = 0; i < b.detailCount; i++) {
    const x = x0 + jag(i * 2 + 1, seed) * w;
    const y = y0 + jag(i * 2 + 2, seed + 11) * h;
    const s = 2 + jag(i, seed + 5) * 3;

    if (b.detail === "grass") {
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x - s * 0.5, y - s);
      ctx.moveTo(x, y);
      ctx.lineTo(x + s * 0.4, y - s * 1.2);
      ctx.stroke();
    } else if (b.detail === "rock") {
      ctx.fillRect(x, y, s, s * 0.7);
    } else if (b.detail === "crystal") {
      ctx.beginPath();
      ctx.moveTo(x, y - s * 1.6);
      ctx.lineTo(x + s * 0.6, y);
      ctx.lineTo(x - s * 0.6, y);
      ctx.closePath();
      ctx.fill();
    } else {
      ctx.fillRect(x, y, s * 1.4, 1.5);
      ctx.fillRect(x + s * 0.6, y - s * 0.5, 1.5, s);
    }
  }
}

/** Борт машинный: ровная кромка, клетка застывшего перехода, свечение по нижнему ребру.
 *  Говорит «плита сконструирована» — то же, что говорит анимация создания на входе в бой. */
function bortMachined(ctx: CanvasRenderingContext2D, x0: number, y: number, w: number, hh: number): void {
  // Серо-нейтральный, а не синий: насыщенная синева читается как вода или лёд, то есть снова
  // как материал мира — ровно то, от чего машинный борт должен уводить.
  const g = ctx.createLinearGradient(0, y, 0, y + hh);
  g.addColorStop(0, "rgba(50,52,60,1)");
  g.addColorStop(1, "rgba(20,21,27,1)");
  ctx.fillStyle = g;
  ctx.fillRect(x0, y, w, hh);

  // Разметка плиты — ЛАТУНЬ, а не бирюза. Канон arena-digital-swap: цифра у нас язык ПЕРЕХОДА,
  // а не состояния; неоновая клетка, висящая на борту весь бой, держит переход включённым
  // навсегда и потому спорит со стилем. Ровная геометрия говорит «конструкт» и без неона,
  // а латунь — цвет нашего интерфейса, и плита через неё попадает в общую гамму.
  ctx.strokeStyle = "rgba(184,134,59,.22)";
  ctx.lineWidth = 1;
  const step = 12;
  for (let x = x0; x <= x0 + w; x += step) {
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, y + hh);
    ctx.stroke();
  }
  for (let yy = y + step * 0.5; yy < y + hh; yy += step) {
    ctx.beginPath();
    ctx.moveTo(x0, yy);
    ctx.lineTo(x0 + w, yy);
    ctx.stroke();
  }

  // Нижнее ребро светится: плита не обрывается, а заканчивается намеренно.
  ctx.strokeStyle = "rgba(184,134,59,.55)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(x0, y + hh);
  ctx.lineTo(x0 + w, y + hh);
  ctx.stroke();
}

/** Борт природный: три слоя породы и рваный скол. Говорит «это кусок настоящего мира». */
function bortNatural(ctx: CanvasRenderingContext2D, b: Biome, x0: number, y: number, w: number, hh: number, seed: number): void {
  const bands: Array<[RGB, number]> = [
    [b.bort[0], 0.22],
    [b.bort[1], 0.44],
    [b.bort[2], 0.34]
  ];
  let cy = y;
  for (const [col, share] of bands) {
    const bh = hh * share;
    ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
    ctx.fillRect(x0, cy, w, bh + 1);
    cy += bh;
  }

  // Скол: низ рвётся зубцами, иначе плита читается аккуратным кубиком.
  ctx.fillStyle = `rgb(${b.bort[2][0]},${b.bort[2][1]},${b.bort[2][2]})`;
  ctx.beginPath();
  ctx.moveTo(x0, y + hh - 2);
  for (let x = x0; x <= x0 + w; x += 6) {
    const d = jag(x, seed + 21) * hh * 0.42;
    ctx.lineTo(x, y + hh - 2 + d);
    ctx.lineTo(x + 3, y + hh - 2 + d * 0.35);
  }
  ctx.lineTo(x0 + w, y + hh - 2);
  ctx.closePath();
  ctx.fill();
}

function slab(o: SlabOpts): DrawFn {
  return (ctx, w, h) => {
    const b = o.biome;

    // Пустота вокруг. Не чёрная: чистый чёрный съел бы силуэт и погасил bloom.
    const vg = ctx.createLinearGradient(0, 0, 0, h);
    vg.addColorStop(0, b.voidTop);
    vg.addColorStop(1, b.voidBottom);
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    // Пустота обязана быть ВИДНА со всех сторон, иначе плита не висит, а лежит на краю кадра —
    // ровно та болезнь, которой болеет стенд «как сейчас». Поэтому поля в долях, а не в пикселях.
    const padX = o.clipped ? 10 : Math.round(w * 0.15);
    const x0 = padX;
    const pw = w - padX * 2;
    const topY = o.clipped ? 0 : Math.round(h * 0.15);
    const bortH = o.bortH;
    const floorY = o.clipped ? h - bortH : h - bortH - Math.round(h * 0.18);
    const ph = floorY - topY;

    // 1. Верхняя грань.
    const surf = paintSurface(b, pw, ph, o.seed, !!o.tiled);
    ctx.drawImage(surf, x0, topY);

    // 2. Россыпь по сиду.
    if (!o.tiled) scatter(ctx, b, x0 + 4, topY + 8, pw - 8, ph - 12, o.seed);

    // 3. Виньетка: края уходят в тень, композиция собирается к центру.
    const vig = ctx.createRadialGradient(x0 + pw / 2, topY + ph / 2, ph * 0.25, x0 + pw / 2, topY + ph / 2, ph * 0.95);
    vig.addColorStop(0, "rgba(0,0,0,0)");
    vig.addColorStop(1, "rgba(0,0,0,.42)");
    ctx.fillStyle = vig;
    ctx.fillRect(x0, topY, pw, ph);

    // 4. Юнит для масштаба — вместе со своей тенью, иначе толщину плиты сравнивать не с чем.
    if (o.unit) {
      drawUnit(ctx, x0 + pw * 0.36, floorY - 16, 46, true);
      drawUnit(ctx, x0 + pw * 0.64, floorY - 40, 40, false);
    }

    // 5. Кромка: без неё плита читается ковриком, а не предметом.
    ctx.fillStyle = o.bortStyle === "machined" ? "rgba(150,178,200,.9)" : "rgba(196,182,140,.55)";
    ctx.fillRect(x0, floorY - 2, pw, 2);

    // 6. Борт.
    if (o.bortStyle === "machined") bortMachined(ctx, x0, floorY, pw, bortH);
    else bortNatural(ctx, b, x0, floorY, pw, bortH, o.seed);

    // 7. Верхний край плиты тоже обрывается в пустоту.
    if (!o.clipped) {
      ctx.fillStyle = "rgba(0,0,0,.55)";
      ctx.fillRect(x0, topY, pw, 3);

      // 8. Пыль, уходящая вниз: без неё пустота читается как пустое место, а не как объём под
      //    плитой. Дешёвый и самый сильный сигнал «висит», поэтому он здесь, а не в списке идей.
      const dustTop = floorY + bortH;
      for (let i = 0; i < 26; i++) {
        const dx = x0 + jag(i * 3 + 1, o.seed + 41) * pw;
        const dy = dustTop + jag(i * 3 + 2, o.seed + 43) * (h - dustTop);
        const a = 0.30 * (1 - (dy - dustTop) / Math.max(h - dustTop, 1));
        ctx.fillStyle = `rgba(198,178,132,${a.toFixed(3)})`;
        ctx.fillRect(dx, dy, 1.5, 1.5);
      }
    }
  };
}

/* ---------- стенды ---------- */

const BORT_STANDS: StandDef[] = [
  {
    id: "machined",
    status: "waiting",
    title: "Борт машинный",
    tag: "рекомендация Никси",
    note:
      "Верхняя грань природная, а плита под ней — <b>конструкт</b>: ровная кромка, клетка застывшего " +
      "перехода, светящееся нижнее ребро.",
    facts: [
      ["говорит", "местность подделана"],
      ["клетка", "та же, что в ArenaSwapShape"],
      ["ребро", "светится: плита заканчивается намеренно"]
    ],
    verdict:
      "Картинка проговаривает мысль сама, без текста. Анимация создания перестаёт быть эффектом на " +
      "входе и становится объяснением того, что видно весь бой.",
    size: [480, 330],
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortH: 26, seed: 4, unit: true })
  },
  {
    id: "natural",
    status: "waiting",
    title: "Борт природный",
    note: "Три слоя породы и рваный скол — плита как вырванный кусок земли.",
    facts: [
      ["говорит", "это кусок настоящего мира"],
      ["слои", "дёрн · земля · камень"],
      ["низ", "рваный скол"]
    ],
    verdict:
      "Красивее в отрыве, но работает ПРОТИВ заявленной мысли: вырванный кусок земли утверждает, " +
      "что арена — место, а не сконструированный полигон.",
    size: [480, 330],
    draw: slab({ biome: MEADOW, bortStyle: "natural", bortH: 26, seed: 4, unit: true })
  }
];

const THICK_STANDS: StandDef[] = [
  {
    id: "thin",
    status: "waiting",
    title: "Тонкая пластина",
    facts: [["борт", "14 px"]],
    verdict: "Читается как пластина, положенная в пустоту. Держит мысль «полигон, а не место».",
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortH: 14, seed: 4, unit: true })
  },
  {
    id: "thick",
    status: "waiting",
    title: "Толстая плита",
    facts: [["борт", "44 px"]],
    verdict: "Читается как остров — то есть тянет обратно в «кусок мира», даже с машинным бортом.",
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortH: 44, seed: 4, unit: true })
  }
];

const BIOME_STANDS: StandDef[] = [MEADOW, FOREST, CAVE, ASH].map((b) => ({
  id: b.id,
  status: "waiting" as const,
  title: b.name,
  tag: "тот же код, другой конфиг",
  facts: [
    ["цветов", "4"],
    ["чисел шума", "3"],
    ["россыпь", b.detail]
  ],
  draw: slab({ biome: b, bortStyle: "machined", bortH: 22, seed: 9 })
}));

const NOW_STAND: StandDef = {
  id: "now",
  status: "note",
  title: "Как сейчас",
  tag: "точка отсчёта",
  note: "Тайл травы, борт упирается в низ кадра, пустоты под плитой не видно.",
  facts: [
    ["поверхность", "тайл 32 px"],
    ["период", "виден глазом"],
    ["пустота", "нет"]
  ],
  verdict:
    "Плита не висит, а лежит на краю экрана — поэтому «арена где-то в пустоте» не читается, " +
    "хотя борт уже нарисован.",
  size: [480, 330],
  draw: slab({ biome: MEADOW, bortStyle: "natural", bortH: 26, seed: 4, unit: true, tiled: true, clipped: true })
};

const section: SectionDef = {
  id: "floor",
  title: "Пол арены",
  lede:
    "Плита, висящая в пустоте: поверхность биома сверху, борт сбоку, ничего под ней. " +
    "Два вопроса под вердикт — каким должен быть борт и какой толщины плита.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "why",
      title: "Почему процедура, а не тайлсет",
      lede: "Два довода, и оба практические."
    },
    {
      kind: "table",
      head: ["Довод", "Что это значит"],
      rows: [
        ["Тайл выдаёт период", "на площади арены глаз находит решётку за секунду, и пол читается как обои"],
        ["Биом становится данными", "не новый набор картинок, а четыре цвета и три числа — строка в SO"],
        ["Сид даёт варианты", "та же арена с другим сидом — другая поляна, без единого нового ассета"],
        ["Палитра доезжает сама", "цвет берётся токеном, а не пипеткой по картинке"]
      ]
    },
    { kind: "stands", items: [NOW_STAND] },
    {
      kind: "head",
      id: "bort",
      title: "Вопрос 1: каким должен быть борт",
      lede: "Тут решается, что игра говорит про арену — и это не вопрос вкуса."
    },
    { kind: "split", items: BORT_STANDS },
    {
      kind: "note",
      html:
        "<b>Моё мнение:</b> машинный. Заказ звучал как «игра прямо говорит — это просто боевая " +
        "арена», а природный скол утверждает обратное: вырванный кусок земли с корнями — это кусок " +
        "мира. Контраст «природная поверхность на искусственной плите» проговаривает мысль сам."
    },
    {
      kind: "head",
      id: "thick",
      title: "Вопрос 2: толщина плиты",
      lede: "Тонкая — пластина, толстая — остров. Разница только в одном числе."
    },
    { kind: "stands", items: THICK_STANDS },
    {
      kind: "head",
      id: "biomes",
      title: "Биом — это конфиг",
      lede: "Четыре биома ниже нарисованы ОДНИМ кодом. Отличаются только данными."
    },
    { kind: "stands", items: BIOME_STANDS },
    {
      kind: "head",
      id: "how",
      title: "Из чего собирается плита",
      lede: "Четыре слоя сверху вниз по экрану."
    },
    {
      kind: "table",
      head: ["Слой", "Чем делается", "Полоса правила"],
      rows: [
        ["Верхняя грань", "fbm-пятна плюс крапка, растр блоками", "считаем"],
        ["Россыпь", "авторские спрайты, расставленные по сиду с плотностью биома", "собираем"],
        ["Кромка", "линия на переходе верха в борт", "считаем"],
        ["Борт", "клетка и светящееся ребро либо слои породы со сколом", "считаем"],
        ["Пустота", "градиент фона, не чёрный", "считаем"]
      ]
    },
    {
      kind: "note",
      html:
        "Боковые и верхний борта не нужны: при взгляде сверху их не видно — на скрине Макса кладка " +
        "тоже только внизу. Наклонять камеру не будем: позиция в симуляции равна позиции на экране, " +
        "и всё плоское (зоны, круги под ногами, будущие тени) держится именно на этом."
    },
    {
      kind: "note",
      html:
        "<b>Готча, которая решает всё.</b> Борт занимает место <b>под</b> полем, а не внутри него, " +
        "иначе съест нижний ряд юнитов. Значит <code>CameraZone</code> расширяется вниз на высоту " +
        "борта плюс запас на пустоту. Шов уже есть: <code>ArenaLayoutData</code> держит " +
        "<code>CameraZone</code> отдельно от <code>Bounds</code>. Сейчас борт упирается в низ кадра — " +
        "и это единственная причина, по которой плита не висит."
    }
  ]
};

export default section;
