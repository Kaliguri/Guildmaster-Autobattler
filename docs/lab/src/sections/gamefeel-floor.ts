/* Пол арены: плита, висящая в пустоте.

   Заказ Макса 2026-08-02: «хочется сохранить вид, что у нас этакая арена летает где-то над
   пустотой. Чтобы игра прямо говорила — хей, это просто боевая арена, вот поэтому мы показываем
   анимацию создания». Плюс биомы (лес, поляна, пещера) и удержание вида «сбоку» с текущего скрина.

   ВСЕ РАЗМЕРЫ ЗДЕСЬ — НАСТОЯЩИЕ, вычитанные из проекта, а не подобранные на глаз. Первая версия
   стенда врала вдвое по масштабу юнита, и Макс это поймал сразу: сравнивать варианты на неверных
   пропорциях бессмысленно, потому что толщина борта и размер фактуры читаются только относительно
   фигуры. Источники чисел перечислены в GEOM ниже, каждое со ссылкой на файл.

   Трава — НАСТОЯЩИЕ спрайты из пака Cainos, который стоит на боевой сцене, расставленные
   процедурно. Так и задумано в игре: спрайт рисует человек (или пак), машина решает, где он лежит.
   Это средняя полоса правила — «собираем», а не «считаем» (code-standards §8). Предыдущая попытка
   считать траву формулой провалилась ровно потому, что пиксельная фактура собирается из авторских
   кластеров, а не из шума.

   Земля под травой считается: тон квантуется в три ступени и дизерится Байером — ограниченная
   палитра и упорядоченный растр, как в пиксель-арте, а не плавная растяжка. */

import { COL, jag, drawUnit } from "../draw.js";
import { paint } from "../stage.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- НАСТОЯЩАЯ ГЕОМЕТРИЯ ----------
   Правило: ни одного числа с потолка. Если число описывает игру — рядом стоит, откуда оно взято. */

const GEOM = {
  /** Пикселей на юнит. Assets/Cainos/.../TX Tileset Grass.png.meta: spritePixelsToUnits: 32.
   *  Клетка тайлмапа на сцене — ровно 1 единица (WorldScene, m_CellSize 1,1). */
  ppu: 32,
  /** Поле боя: ArenaLayoutAuthoring._boundsSize = (20, 12). */
  arenaW: 20,
  arenaH: 12,
  /** Зона камеры: ArenaLayoutAuthoring._cameraZoneSize = (26, 16). Больше поля — именно в этот
   *  запас и уходит борт с пустотой под ним. */
  camW: 26,
  camH: 16,
  /** Человек-эталон: ArenaLayoutAuthoring._refHumanHeight / _refHumanWidth. */
  humanH: 1.7,
  humanW: 0.6
} as const;

/** Арена в текстурных пикселях: 640 x 384. Стенд рисует ИМЕННО столько, поэтому растр на картинке
 *  равен растру в игре — один блок здесь есть один пиксель там. */
const ARENA_PX_W = GEOM.arenaW * GEOM.ppu;
const ARENA_PX_H = GEOM.arenaH * GEOM.ppu;
/** Рост человека в тех же пикселях: 54. Ради этого числа стенд и переписан. */
const HUMAN_PX_H = Math.round(GEOM.humanH * GEOM.ppu);

/* ---------- атлас растений ----------
   ВРЕМЕННО: копия Assets/Cainos/Pixel Art Top Down - Basic/Texture/TX Plant.png. Взята по прямой
   просьбе Макса («возьми временно спрайты травы с пака что юзаем на сцене»), чтобы стенд показывал
   реальную траву, а не мою имитацию. Копия, а не ссылка, потому что сервер Лаборатории раздаёт
   docs/lab и выше своего корня не поднимается. Когда у арены появится свой набор растений, файл
   уезжает вместе с этой строкой. */

const PLANTS = new Image();
PLANTS.src = "assets/tmp-cainos-plant.png";
// Атлас грузится асинхронно, а рисовалки синхронные: без этого трава появлялась бы только со
// второго захода на страницу. paint() перерисовывает все живые сцены разом.
PLANTS.onload = () => paint();

/** Мелкая трава: нижний блок атласа, сетка 4x4 по 32 px от (0, 384). */
const GRASS_CELLS: Array<[number, number]> = [];
for (let ry = 0; ry < 4; ry++) for (let rx = 0; rx < 4; rx++) GRASS_CELLS.push([rx * 32, 384 + ry * 32]);

/** Кусты: средний ряд атласа, шесть клеток 64 px от (0, 176). Крупная деталь поверх мелкой. */
const BUSH_CELLS: Array<[number, number]> = [];
for (let rx = 0; rx < 6; rx++) BUSH_CELLS.push([rx * 64, 176]);

/* ---------- шум ---------- */

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

/* ---------- биом ---------- */

type RGB = [number, number, number];

interface Biome {
  id: string;
  name: string;
  base: RGB;
  patch: RGB;
  /** Цвет кластеров — для биомов БЕЗ готовых спрайтов. */
  speck: RGB;
  /** Масштаб пятен земли в пикселях арены. 0.006 даёт пятна размером примерно с юнита. */
  scale: number;
  bort: [RGB, RGB, RGB];
  voidTop: string;
  voidBottom: string;
  /** Растительность спрайтами из атласа: сколько мелкой травы и сколько кустов. Ноль — биом
   *  обходится кластерами (у пещеры и пепелища подходящих спрайтов в паке нет). */
  grass: number;
  bushes: number;
  cluster: "rock" | "crystal" | "bone" | null;
  clusterAmount: number;
}

const MEADOW: Biome = {
  id: "meadow",
  name: "Поляна",
  base: [116, 132, 60],
  patch: [82, 100, 42],
  speck: [150, 168, 78],
  scale: 0.006,
  bort: [[92, 104, 44], [104, 82, 52], [72, 58, 40]],
  voidTop: "#141019",
  voidBottom: "#0B0910",
  grass: 420,
  bushes: 7,
  cluster: null,
  clusterAmount: 0
};

const FOREST: Biome = {
  id: "forest",
  name: "Лес",
  base: [62, 86, 54],
  patch: [38, 56, 36],
  speck: [86, 112, 64],
  scale: 0.008,
  bort: [[46, 66, 42], [78, 60, 40], [54, 44, 32]],
  voidTop: "#0F1414",
  voidBottom: "#070A0A",
  grass: 700,
  bushes: 18,
  cluster: null,
  clusterAmount: 0
};

const CAVE: Biome = {
  id: "cave",
  name: "Пещера",
  base: [74, 70, 84],
  patch: [46, 42, 56],
  speck: [104, 100, 120],
  scale: 0.007,
  bort: [[60, 56, 70], [48, 44, 56], [32, 30, 40]],
  voidTop: "#0C0A12",
  voidBottom: "#06050A",
  grass: 0,
  bushes: 0,
  cluster: "crystal",
  clusterAmount: 0.5
};

const ASH: Biome = {
  id: "ash",
  name: "Пепелище",
  base: [88, 76, 70],
  patch: [56, 46, 44],
  speck: [120, 104, 96],
  scale: 0.0065,
  bort: [[70, 60, 56], [56, 44, 40], [38, 30, 28]],
  voidTop: "#130E0C",
  voidBottom: "#090606",
  grass: 0,
  bushes: 0,
  cluster: "bone",
  clusterAmount: 0.45
};

/* ---------- растр ---------- */

/** Один блок земли — ОДИН пиксель арены. Раньше здесь стояло 3, и Макс справедливо сказал, что
 *  пиксели слишком крупные: при трёх блок был размером с восьмую часть человека. */
const CELL = 1;

const BAYER = [
  0 / 16, 8 / 16, 2 / 16, 10 / 16,
  12 / 16, 4 / 16, 14 / 16, 6 / 16,
  3 / 16, 11 / 16, 1 / 16, 9 / 16,
  15 / 16, 7 / 16, 13 / 16, 5 / 16
];

/** Кластеры для биомов без спрайтов. Связные формы, а не одиночные точки: пиксель без соседей
 *  читается как грязь (Slynyrd, Pixelblog-2). Координаты в пикселях от угла кластера. */
const CLUSTERS: Record<"rock" | "crystal" | "bone", Array<Array<[number, number]>>> = {
  rock: [
    [[0, 0], [1, 0], [2, 0], [0, 1], [1, 1], [2, 1], [1, 2]],
    [[0, 0], [1, 0], [1, 1], [2, 1]],
    [[0, 1], [1, 0], [2, 0], [2, 1], [1, 1]]
  ],
  crystal: [
    [[1, 0], [1, 1], [0, 2], [1, 2], [2, 2]],
    [[0, 0], [0, 1], [1, 1], [1, 2]],
    [[2, 0], [1, 1], [2, 1], [1, 2], [2, 2]]
  ],
  bone: [
    [[0, 0], [1, 0], [2, 0], [3, 0], [3, 1], [0, 1]],
    [[0, 1], [1, 0], [2, 0], [3, 1]],
    [[0, 0], [1, 0], [1, 1], [2, 1]]
  ]
};

function mix(a: RGB, b: RGB, t: number): RGB {
  return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
}

const surfaceCache = new Map<string, HTMLCanvasElement>();

/** Земля: три тональные ступени с упорядоченным дизерингом на границах. */
function paintGround(b: Biome, w: number, h: number, seed: number, tiled: boolean): HTMLCanvasElement {
  const key = `${b.id}|${w}x${h}|${seed}|${tiled ? "t" : "n"}`;
  const hit = surfaceCache.get(key);
  if (hit) return hit;

  const cv = document.createElement("canvas");
  cv.width = Math.ceil(w);
  cv.height = Math.ceil(h);
  const c = cv.getContext("2d")!;

  const tone: RGB[] = [b.patch, mix(b.patch, b.base, 0.55), b.base];

  for (let y = 0; y < h; y += CELL) {
    for (let x = 0; x < w; x += CELL) {
      let t: number;
      if (tiled) {
        // Тайл 32 px — ровно клетка тайлмапа со сцены. Период виден глазом, в этом и смысл стенда.
        const tx = x % 32;
        const ty = y % 32;
        t = vnoise(tx * 0.16, ty * 0.16, 1);
      } else {
        t = fbm(x * b.scale, y * b.scale, seed);
      }
      const bay = BAYER[((y / CELL) & 3) * 4 + ((x / CELL) & 3)]!;
      const lvl = Math.min(2, Math.max(0, Math.floor(t * 3 + (bay - 0.5) * 0.9)));
      const col = tone[lvl]!;
      c.fillStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`;
      c.fillRect(x, y, CELL, CELL);
    }
  }

  if (!tiled && b.cluster) {
    const shapes = CLUSTERS[b.cluster];
    for (let y = 0; y < h; y += 8) {
      for (let x = 0; x < w; x += 8) {
        const dens = fbm(x * b.scale * 0.5, y * b.scale * 0.5, seed + 77);
        // Степень выдавливает середину: остаются явные проплешины и явные заросли. Ровная
        // плотность читается как заполнение, а не как земля.
        if (hash2(x, y, seed + 13) > Math.pow(dens, 2.4) * b.clusterAmount * 1.6) continue;
        const shape = shapes[Math.floor(hash2(x, y, seed + 29) * 3) % 3]!;
        const col = hash2(x, y, seed + 53) > 0.62 ? mix(b.speck, b.base, 0.45) : b.speck;
        c.fillStyle = `rgb(${col[0] | 0},${col[1] | 0},${col[2] | 0})`;
        for (const [dx, dy] of shape) c.fillRect(x + dx, y + dy, 1, 1);
      }
    }
  }

  surfaceCache.set(key, cv);
  return cv;
}

/** Растительность спрайтами: машина решает ТОЛЬКО где и какой, сам рисунок авторский. */
function plantSprites(
  ctx: CanvasRenderingContext2D,
  b: Biome,
  x0: number,
  y0: number,
  w: number,
  h: number,
  seed: number
): void {
  if (!PLANTS.complete || PLANTS.naturalWidth === 0) return;
  ctx.imageSmoothingEnabled = false;

  // Кусты идут первыми: мелкая трава ложится поверх и связывает их с землёй.
  for (let i = 0; i < b.bushes; i++) {
    const dens = 0.35 + jag(i * 7 + 3, seed + 91) * 0.65;
    if (jag(i * 7 + 4, seed + 92) > dens) continue;
    const cell = BUSH_CELLS[Math.floor(jag(i, seed + 61) * BUSH_CELLS.length) % BUSH_CELLS.length]!;
    const x = x0 + jag(i * 5 + 1, seed + 63) * (w - 64);
    const y = y0 + jag(i * 5 + 2, seed + 65) * (h - 64);
    ctx.drawImage(PLANTS, cell[0], cell[1], 64, 64, Math.round(x), Math.round(y), 64, 64);
  }

  for (let i = 0; i < b.grass; i++) {
    const gx = jag(i * 3 + 1, seed + 31);
    const gy = jag(i * 3 + 2, seed + 33);
    // Плотность неравномерна: тот же приём, что и у кластеров — фактура живёт контрастом
    // занятого и пустого, а не ровным ковром.
    const dens = fbm(gx * w * 0.004, gy * h * 0.004, seed + 79);
    if (jag(i * 3 + 5, seed + 35) > Math.pow(dens, 1.6) * 2.2) continue;
    const cell = GRASS_CELLS[Math.floor(jag(i, seed + 37) * GRASS_CELLS.length) % GRASS_CELLS.length]!;
    ctx.drawImage(PLANTS, cell[0], cell[1], 32, 32, Math.round(x0 + gx * (w - 32)), Math.round(y0 + gy * (h - 32)), 32, 32);
  }
}

/* ---------- сцена ---------- */

interface SlabOpts {
  biome: Biome;
  bortStyle: "machined" | "natural";
  /** Толщина борта в МИРОВЫХ единицах: так её можно сравнить с ростом человека (1.7). */
  bortU: number;
  seed: number;
  unit?: boolean;
  tiled?: boolean;
  clipped?: boolean;
  /** Показать фрагмент поверхности во всю карточку вместо всей арены: для сравнения фактуры,
   *  когда композиция не предмет разговора. */
  crop?: boolean;
}

function bortMachined(ctx: CanvasRenderingContext2D, x0: number, y: number, w: number, hh: number): void {
  const g = ctx.createLinearGradient(0, y, 0, y + hh);
  g.addColorStop(0, "rgba(50,52,60,1)");
  g.addColorStop(1, "rgba(20,21,27,1)");
  ctx.fillStyle = g;
  ctx.fillRect(x0, y, w, hh);

  // Разметка — ЛАТУНЬ, а не бирюза. Канон arena-digital-swap: цифра у нас язык ПЕРЕХОДА, а не
  // состояния; неоновая клетка на борту держала бы переход включённым весь бой.
  ctx.strokeStyle = "rgba(184,134,59,.22)";
  ctx.lineWidth = 1;
  const step = GEOM.ppu; // шаг разметки — ровно одна мировая единица
  for (let x = x0; x <= x0 + w; x += step) {
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, y + hh);
    ctx.stroke();
  }

  ctx.strokeStyle = "rgba(184,134,59,.55)";
  ctx.beginPath();
  ctx.moveTo(x0, y + hh);
  ctx.lineTo(x0 + w, y + hh);
  ctx.stroke();
}

function bortNatural(ctx: CanvasRenderingContext2D, b: Biome, x0: number, y: number, w: number, hh: number, seed: number): void {
  const bands: Array<[RGB, number]> = [[b.bort[0], 0.22], [b.bort[1], 0.44], [b.bort[2], 0.34]];
  let cy = y;
  for (const [col, share] of bands) {
    const bh = hh * share;
    ctx.fillStyle = `rgb(${col[0]},${col[1]},${col[2]})`;
    ctx.fillRect(x0, cy, w, bh + 1);
    cy += bh;
  }
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
    ctx.imageSmoothingEnabled = false;

    const vg = ctx.createLinearGradient(0, 0, 0, h);
    vg.addColorStop(0, b.voidTop);
    vg.addColorStop(1, b.voidBottom);
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    // Фрагмент: карточка показывает кусок поверхности один к одному, без композиции.
    if (o.crop) {
      const g = paintGround(b, w, h, o.seed, false);
      ctx.drawImage(g, 0, 0);
      plantSprites(ctx, b, 0, 0, w, h, o.seed);
      return;
    }

    // Вся арена: 640 x 384 пикселя, ровно как в игре.
    const pw = ARENA_PX_W;
    const ph = ARENA_PX_H;
    const bortH = Math.round(o.bortU * GEOM.ppu);
    const x0 = Math.round((w - pw) / 2);
    const topY = o.clipped ? 0 : Math.round((h - ph - bortH) / 2);
    const floorY = topY + ph;

    const g = paintGround(b, pw, ph, o.seed, !!o.tiled);
    ctx.drawImage(g, x0, topY);
    if (!o.tiled) plantSprites(ctx, b, x0, topY, pw, ph, o.seed);

    const vig = ctx.createRadialGradient(x0 + pw / 2, topY + ph / 2, ph * 0.3, x0 + pw / 2, topY + ph / 2, ph * 0.95);
    vig.addColorStop(0, "rgba(0,0,0,0)");
    vig.addColorStop(1, "rgba(0,0,0,.38)");
    ctx.fillStyle = vig;
    ctx.fillRect(x0, topY, pw, ph);

    // Юниты РОСТОМ 54 пикселя — 1.7 мировой единицы, как задано эталоном в авторинге арены.
    if (o.unit) {
      drawUnit(ctx, x0 + pw * 0.34, floorY - Math.round(ph * 0.22), HUMAN_PX_H, true);
      drawUnit(ctx, x0 + pw * 0.62, floorY - Math.round(ph * 0.52), HUMAN_PX_H, false);
      drawUnit(ctx, x0 + pw * 0.72, floorY - Math.round(ph * 0.30), HUMAN_PX_H, false);
    }

    ctx.fillStyle = o.bortStyle === "machined" ? "rgba(150,178,200,.9)" : "rgba(196,182,140,.55)";
    ctx.fillRect(x0, floorY - 2, pw, 2);

    if (o.bortStyle === "machined") bortMachined(ctx, x0, floorY, pw, bortH);
    else bortNatural(ctx, b, x0, floorY, pw, bortH, o.seed);

    if (!o.clipped) {
      ctx.fillStyle = "rgba(0,0,0,.55)";
      ctx.fillRect(x0, topY, pw, 3);

      // Пыль под плитой: самый дешёвый сигнал «висит», сильнее самого борта.
      const dustTop = floorY + bortH;
      for (let i = 0; i < 30; i++) {
        const dx = x0 + jag(i * 3 + 1, o.seed + 41) * pw;
        const dy = dustTop + jag(i * 3 + 2, o.seed + 43) * Math.max(h - dustTop, 1);
        const a = 0.32 * (1 - (dy - dustTop) / Math.max(h - dustTop, 1));
        ctx.fillStyle = `rgba(198,178,132,${a.toFixed(3)})`;
        ctx.fillRect(dx, dy, 1.5, 1.5);
      }
    }
  };
}

/* ---------- стенды ---------- */

/** Логический размер карточки со всей ареной: 640 пикселей поля плюс поля на пустоту. */
const FULL: [number, number] = [760, 520];

const BORT_STANDS: StandDef[] = [
  {
    id: "machined",
    status: "waiting",
    title: "Борт машинный",
    tag: "рекомендация Никси",
    note: "Верхняя грань природная, плита под ней — <b>конструкт</b>: ровная кромка, латунная разметка по мировой сетке, светящееся нижнее ребро.",
    facts: [
      ["говорит", "местность подделана"],
      ["разметка", "латунь, шаг 1 единица"],
      ["борт", "0.75 ед · 24 px"]
    ],
    verdict: "Картинка проговаривает мысль сама. Анимация создания перестаёт быть эффектом на входе и становится объяснением того, что видно весь бой.",
    size: FULL,
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortU: 0.75, seed: 4, unit: true })
  },
  {
    id: "natural",
    status: "waiting",
    title: "Борт природный",
    note: "Три слоя породы и рваный скол — плита как вырванный кусок земли.",
    facts: [
      ["говорит", "это кусок настоящего мира"],
      ["слои", "дёрн · земля · камень"],
      ["борт", "0.75 ед · 24 px"]
    ],
    verdict: "Красивее в отрыве, но работает ПРОТИВ заявленной мысли: вырванный кусок земли утверждает, что арена — место, а не сконструированный полигон.",
    size: FULL,
    draw: slab({ biome: MEADOW, bortStyle: "natural", bortU: 0.75, seed: 4, unit: true })
  }
];

const THICK_STANDS: StandDef[] = [
  {
    id: "thin",
    status: "waiting",
    title: "Тонкая пластина",
    facts: [["борт", "0.4 ед · 13 px"], ["к росту", "четверть человека"]],
    verdict: "Читается как пластина, положенная в пустоту. Держит мысль «полигон, а не место».",
    size: FULL,
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortU: 0.4, seed: 4, unit: true })
  },
  {
    id: "thick",
    status: "waiting",
    title: "Толстая плита",
    facts: [["борт", "1.5 ед · 48 px"], ["к росту", "почти человек"]],
    verdict: "Читается как остров — тянет обратно в «кусок мира», даже с машинным бортом.",
    size: FULL,
    draw: slab({ biome: MEADOW, bortStyle: "machined", bortU: 1.5, seed: 4, unit: true })
  }
];

const BIOME_STANDS: StandDef[] = [MEADOW, FOREST, CAVE, ASH].map((b) => ({
  id: b.id,
  status: "waiting" as const,
  title: b.name,
  tag: "фрагмент 1:1",
  note: "Кусок поверхности в масштабе игры.",
  facts: [
    ["трава", b.grass ? `${b.grass} спрайтов` : "кластерами"],
    ["кусты", b.bushes ? String(b.bushes) : "нет"]
  ],
  draw: slab({ biome: b, bortStyle: "machined", bortU: 0.75, seed: 9, crop: true })
}));

const NOW_STAND: StandDef = {
  id: "now",
  status: "note",
  title: "Как сейчас",
  tag: "точка отсчёта",
  note: "Тайл 32 px повторяется, борт упирается в низ кадра, пустоты под плитой не видно.",
  facts: [
    ["поверхность", "тайл 32 px"],
    ["период", "виден глазом"],
    ["пустота", "нет"]
  ],
  verdict: "Плита не висит, а лежит на краю экрана — поэтому «арена где-то в пустоте» не читается, хотя борт уже нарисован.",
  size: FULL,
  draw: slab({ biome: MEADOW, bortStyle: "natural", bortU: 0.75, seed: 4, unit: true, tiled: true, clipped: true })
};

const section: SectionDef = {
  id: "floor",
  title: "Пол арены",
  lede: "Плита, висящая в пустоте: поверхность биома сверху, борт сбоку, ничего под ней. Все размеры — настоящие, из ArenaLayoutAuthoring и импорта тайлсета.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "scale",
      title: "Настоящие пропорции",
      lede: "Первая версия стенда врала вдвое по росту юнита. Числа ниже вычитаны из проекта."
    },
    {
      kind: "table",
      head: ["Величина", "Значение", "Откуда"],
      rows: [
        ["Пикселей на единицу", "32", "TX Tileset Grass.png.meta, клетка тайлмапа 1×1"],
        ["Поле боя", "20 × 12 ед = 640 × 384 px", "ArenaLayoutAuthoring._boundsSize"],
        ["Зона камеры", "26 × 16 ед", "ArenaLayoutAuthoring._cameraZoneSize"],
        ["Человек", "1.7 × 0.6 ед = 54 × 19 px", "_refHumanHeight / _refHumanWidth"],
        ["Ростов в ширину арены", "около 12", "20 ÷ 1.7"]
      ]
    },
    {
      kind: "note",
      html:
        "Зона камеры шире поля на <b>6 единиц по горизонтали и 4 по вертикали</b> — в этот запас " +
        "и уходит борт с пустотой под ним. То есть ручка для «плита висит» в данных уже есть, " +
        "менять её не придётся."
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
      lede: "Теперь в мировых единицах, то есть сравнимо с ростом человека."
    },
    { kind: "split", items: THICK_STANDS },
    {
      kind: "head",
      id: "biomes",
      title: "Биом — это конфиг",
      lede: "Четыре фрагмента одним кодом. Трава — настоящие спрайты пака, расставленные процедурно."
    },
    { kind: "stands", items: BIOME_STANDS },
    {
      kind: "note",
      html:
        "Растения взяты <b>временно</b> из <code>Assets/Cainos/Pixel Art Top Down - Basic</code> — " +
        "того пака, что стоит на боевой сцене. Так и задумано в игре: <b>спрайт рисует человек, " +
        "машина решает, где он лежит</b>. Это средняя полоса правила — «собираем», а не «считаем». " +
        "Считается только земля под ними."
    },
    {
      kind: "head",
      id: "how",
      title: "Из чего собирается плита",
      lede: "Пять слоёв сверху вниз по экрану."
    },
    {
      kind: "table",
      head: ["Слой", "Чем делается", "Полоса правила"],
      rows: [
        ["Земля", "три тональные ступени с дизерингом Байера", "считаем"],
        ["Растительность", "авторские спрайты, расставленные по сиду", "собираем"],
        ["Кромка", "линия на переходе верха в борт", "считаем"],
        ["Борт", "латунная разметка по мировой сетке либо слои породы", "считаем"],
        ["Пыль под плитой", "точки, гаснущие вниз", "считаем"]
      ]
    },
    {
      kind: "note",
      html:
        "Боковые и верхний борта не нужны: при взгляде сверху их не видно. Наклонять камеру не " +
        "будем — позиция в симуляции равна позиции на экране, и всё плоское (зоны, круги под " +
        "ногами, будущие тени) держится именно на этом."
    }
  ]
};

export default section;
