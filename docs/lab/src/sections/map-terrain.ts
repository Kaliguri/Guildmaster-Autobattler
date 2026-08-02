/* Карта как атлас: рельеф — это ЗЕМЛЯ, фракция — страна на ней.

   Прототип по заказу Макса 2026-08-02. Знаки-пиктограммы провалились ровно потому, что значок не
   создаёт места, а подписывает его: восемь иконок поверх бледных пятен — легенда, а не карта. Земля
   же не нуждается в подписи «тут остров», если нарисована островом.

   Как считается берег. Метаболы дают круглые кляксы, поэтому здесь поле расстояний до узлов и дорог
   области, а координаты перед замером искажаются фрактальным шумом (domain warp). Один этот приём
   ломает берег на ВСЕХ масштабах сразу — крупные бухты и мелкая зубчатость появляются вместе, без
   отдельного сглаживания и отдельного дробления. Полноценный Вороной с релаксацией Ллойда
   (redblobgames) даёт то же самое плюс общую границу у соседних земель; для одной области он избыточен,
   но именно он поедет в движок, когда земель станет много.

   Приёмы отрисовки взяты у бумажной картографии: ореол параллельных линий у берега, тень по
   юго-восточной кромке, отмывка от источника в левом верхнем углу, штриховка Лемана (1799) вдоль
   склона. Последняя изобреталась ровно для нашей задачи — показать рельеф, когда цвет занят другим.

   Тяжёлые вычисления считаются ОДИН раз в offscreen-canvas и кэшируются: стенд статичный, а поле с
   тремя октавами шума на каждый пиксель в реальном времени не нужно никому. */

import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

const GOBLIN: [number, number, number] = [132, 214, 92];
const BANDIT: [number, number, number] = [255, 96, 80];

/* ---------- шум ---------- */

function hash2(x: number, y: number, salt: number): number {
  return jag(x * 374761 + y * 668265, salt);
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

/** Значение-шум с плавной интерполяцией: основа fbm и доменного искажения. */
function vnoise(x: number, y: number, salt: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const xf = x - xi;
  const yf = y - yi;
  const u = xf * xf * (3 - 2 * xf);
  const v = yf * yf * (3 - 2 * yf);
  const a = hash2(xi, yi, salt);
  const b = hash2(xi + 1, yi, salt);
  const c = hash2(xi, yi + 1, salt);
  const d = hash2(xi + 1, yi + 1, salt);
  return lerp(lerp(a, b, u), lerp(c, d, u), v);
}

function fbm(x: number, y: number, salt: number): number {
  let sum = 0;
  let amp = 0.5;
  let freq = 1;
  for (let o = 0; o < 3; o++) {
    sum += (vnoise(x * freq, y * freq, salt + o * 17) - 0.5) * amp;
    amp *= 0.5;
    freq *= 2.1;
  }
  return sum;
}

/* ---------- геометрия области ---------- */

interface Node {
  x: number;
  y: number;
  /** 0 — гоблины, 1 — разбойники: политический слой. */
  side: 0 | 1;
}

type Seg = [number, number, number, number];

/** Одна и та же область во всех стендах: сравнивать надо подачу, а не разные графы. */
function region(w: number, h: number): { nodes: Node[]; segs: Seg[] } {
  const cols = [2, 4, 3, 4, 2];
  const left = w * 0.16;
  const stepX = (w * 0.68) / (cols.length - 1);
  const midY = h * 0.47;
  const nodes: Node[] = [];
  const starts: number[] = [];

  cols.forEach((rows, c) => {
    starts.push(nodes.length);
    for (let r = 0; r < rows; r++)
      nodes.push({
        x: left + c * stepX,
        y: midY + (r - (rows - 1) / 2) * (h * 0.17),
        side: c < 2 || (c === 2 && r === 0) ? 0 : 1
      });
  });

  const segs: Seg[] = [];
  for (let c = 0; c + 1 < cols.length; c++) {
    const a0 = starts[c] ?? 0;
    const b0 = starts[c + 1] ?? 0;
    const ra = cols[c] ?? 1;
    const rb = cols[c + 1] ?? 1;
    for (let i = 0; i < ra; i++) {
      const t = ra === 1 ? 0 : i / (ra - 1);
      const centre = Math.round(t * (rb - 1));
      for (let d = -1; d <= 1; d++) {
        const j = centre + d;
        if (j < 0 || j >= rb) continue;
        const from = nodes[a0 + i];
        const to = nodes[b0 + j];
        if (from && to) segs.push([from.x, from.y, to.x, to.y]);
      }
    }
  }
  return { nodes, segs };
}

function distToSeg(px: number, py: number, s: Seg): number {
  const [x0, y0, x1, y1] = s;
  const dx = x1 - x0;
  const dy = y1 - y0;
  const len = dx * dx + dy * dy;
  let t = len > 0 ? ((px - x0) * dx + (py - y0) * dy) / len : 0;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return Math.hypot(px - (x0 + dx * t), py - (y0 + dy * t));
}

/* ---------- поле земли ---------- */

interface FieldOpts {
  /** Сила доменного искажения в пикселях. 0 — гладкий контур. */
  warp: number;
  /** Радиус, на котором проходит берег. */
  reach: number;
}

/** Одно искажение и для берега, И для границы стран: иначе рваный берег соседствует с
 *  линейкой-границей, и два слоя выглядят из разных игр. */
function warpXY(x: number, y: number, warp: number): [number, number] {
  if (warp <= 0) return [x, y];
  return [
    x + fbm(x * 0.013, y * 0.013, 3) * warp + fbm(x * 0.055, y * 0.055, 11) * warp * 0.35,
    y + fbm(x * 0.013 + 5.2, y * 0.013 + 1.7, 7) * warp + fbm(x * 0.055, y * 0.055, 19) * warp * 0.35
  ];
}

/** Расстояние до земли в точке: отрицательное внутри, положительное в воде. */
function landField(x: number, y: number, segs: Seg[], o: FieldOpts): number {
  const [px, py] = warpXY(x, y, o.warp);
  let best = 1e9;
  for (const s of segs) {
    const d = distToSeg(px, py, s);
    if (d < best) best = d;
  }
  return best - o.reach;
}

/** Ближайшая сторона: политическая принадлежность точки суши. */
function sideAt(x: number, y: number, nodes: Node[]): 0 | 1 {
  let best = 1e9;
  let side: 0 | 1 = 0;
  for (const n of nodes) {
    const d = (n.x - x) * (n.x - x) + (n.y - y) * (n.y - y);
    if (d < best) {
      best = d;
      side = n.side;
    }
  }
  return side;
}

/* ---------- отрисовка земли в offscreen ---------- */

interface LandOpts extends FieldOpts {
  /** Ореол параллельных линий в воде вдоль берега. */
  halo: boolean;
  /** Тень по юго-восточной кромке — земля приподнимается над листом. */
  shadow: boolean;
  /** Отмывка от источника в левом верхнем углу. */
  shade: boolean;
  /** Политическая заливка стран поверх суши. */
  political: boolean;
}

const cache = new Map<string, HTMLCanvasElement>();

function renderLand(w: number, h: number, o: LandOpts, segs: Seg[], nodes: Node[]): HTMLCanvasElement {
  const key = `${w}x${h}|${o.warp}|${o.reach}|${o.halo}|${o.shadow}|${o.shade}|${o.political}`;
  const hit = cache.get(key);
  if (hit) return hit;

  const cv = document.createElement("canvas");
  cv.width = Math.round(w);
  cv.height = Math.round(h);
  const c = cv.getContext("2d");
  if (!c) return cv;

  const img = c.createImageData(cv.width, cv.height);
  const px = img.data;

  for (let y = 0; y < cv.height; y++) {
    for (let x = 0; x < cv.width; x++) {
      const i = (y * cv.width + x) * 4;
      const d = landField(x, y, segs, o);

      let r = 0;
      let g = 0;
      let b = 0;
      let a = 0;

      if (d <= 0) {
        // Суша: тёплая бумага, слегка светлее к середине массива.
        const depth = Math.min(1, -d / 26);
        r = 214 - depth * 10;
        g = 196 - depth * 12;
        b = 156 - depth * 14;
        a = 255;

        if (o.shade) {
          // Отмывка по СГЛАЖЕННОМУ склону и слабо: у медиальной оси поле ломается резко, и на
          // полной силе земля превращается в мятую фольгу — проверено рендером до правки.
          const gx = (landField(x + 5, y, segs, o) - landField(x - 5, y, segs, o)) / 10;
          const gy = (landField(x, y + 5, segs, o) - landField(x, y - 5, segs, o)) / 10;
          const lit = Math.max(-1, Math.min(1, (-gx - gy) * 1.4));
          r += lit * 9 + depth * 4;
          g += lit * 8 + depth * 3;
          b += lit * 6 + depth;
        }

        if (o.political) {
          // Страна — бледной заливкой ПОВЕРХ суши, не вместо неё; координаты те же искажённые,
          // поэтому граница гнётся вместе с берегом.
          const [wx, wy] = warpXY(x, y, o.warp);
          const tint = sideAt(wx, wy, nodes) === 0 ? GOBLIN : BANDIT;
          r = r * 0.82 + tint[0] * 0.18;
          g = g * 0.82 + tint[1] * 0.18;
          b = b * 0.82 + tint[2] * 0.18;
        }

        if (o.shadow && d > -3) {
          // Кромка темнее — берег читается линией без отдельной обводки.
          r *= 0.72;
          g *= 0.72;
          b *= 0.7;
        }
      } else {
        if (o.shadow) {
          // Тень по юго-восточной стороне: смотрим, есть ли земля выше-левее.
          const up = landField(x - 4, y - 4, segs, o);
          if (up <= 0 && d < 7) {
            r = 92;
            g = 74;
            b = 52;
            a = Math.round(120 * (1 - d / 7));
          }
        }
        if (o.halo && a === 0) {
          // Ореол: две ЧЁТКИЕ линии по контуру. Размазанная полоса в воде не читается вовсе —
          // первая версия давала градиент, и его просто не было видно.
          for (const [edge, strength] of [[3, 0.75], [9, 0.45]] as Array<[number, number]>) {
            if (Math.abs(d - edge) < 1.1) {
              const k = strength * (1 - Math.abs(d - edge) / 1.1);
              r = 168;
              g = 146;
              b = 104;
              a = Math.round(255 * k);
            }
          }
        }
      }

      px[i] = Math.max(0, Math.min(255, r));
      px[i + 1] = Math.max(0, Math.min(255, g));
      px[i + 2] = Math.max(0, Math.min(255, b));
      px[i + 3] = a;
    }
  }

  c.putImageData(img, 0, 0);
  cache.set(key, cv);
  return cv;
}

/* ---------- поверх земли: дороги, узлы, подписи ---------- */

function drawRoadsAndNodes(
  ctx: CanvasRenderingContext2D,
  nodes: Node[],
  segs: Seg[],
  ringed: boolean
): void {
  ctx.strokeStyle = "rgba(74,58,40,.55)";
  ctx.lineWidth = 1.3;
  ctx.setLineDash([2, 4]);
  for (const s of segs) {
    ctx.beginPath();
    ctx.moveTo(s[0], s[1]);
    ctx.lineTo(s[2], s[3]);
    ctx.stroke();
  }
  ctx.setLineDash([]);

  for (const n of nodes) {
    ctx.beginPath();
    ctx.arc(n.x, n.y, 7, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = 2;
    const tint = n.side === 0 ? GOBLIN : BANDIT;
    ctx.strokeStyle = ringed ? `rgba(${tint[0]},${tint[1]},${tint[2]},.95)` : "rgba(58,44,30,.75)";
    ctx.stroke();
  }
}

/** Подпись страны: капитель в разрядку, как на политических картах. */
function countryLabel(ctx: CanvasRenderingContext2D, text: string, cx: number, cy: number, tint: [number, number, number]): void {
  ctx.font = "600 12px ui-monospace, Consolas, monospace";
  ctx.fillStyle = `rgba(${tint[0]},${tint[1]},${tint[2]},.95)`;
  const spaced = text.toUpperCase().split("").join(" ");
  const wide = ctx.measureText(spaced).width;
  ctx.fillText(spaced, cx - wide / 2, cy);
}

/** Подпись земли: курсив, как названия физических объектов в атласе. */
function landLabel(ctx: CanvasRenderingContext2D, text: string, cx: number, cy: number): void {
  ctx.font = "italic 500 13px Georgia, 'Times New Roman', serif";
  ctx.fillStyle = "rgba(74,58,40,.85)";
  const wide = ctx.measureText(text).width;
  ctx.fillText(text, cx - wide / 2, cy);
}

function terrain(o: LandOpts, caption: string, labels: boolean): DrawFn {
  return (ctx, w, h) => {
    const { nodes, segs } = region(w, h);
    ctx.drawImage(renderLand(w, h, o, segs, nodes), 0, 0, w, h);
    drawRoadsAndNodes(ctx, nodes, segs, o.political);

    if (labels) {
      landLabel(ctx, "Долина Трёх Костров", w * 0.5, h * 0.2);
      countryLabel(ctx, "гоблины", w * 0.26, h * 0.83, GOBLIN);
      countryLabel(ctx, "разбойники", w * 0.75, h * 0.83, BANDIT);
    }

    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.fillText(caption, 16, h - 12);
  };
}

/* ---------- штриховка Лемана ---------- */

const drawHachures: DrawFn = (ctx, w, h) => {
  const { nodes, segs } = region(w, h);
  const o: LandOpts = { warp: 26, reach: 34, halo: false, shadow: true, shade: false, political: false };
  ctx.drawImage(renderLand(w, h, o, segs, nodes), 0, 0, w, h);

  // Штрих идёт ВДОЛЬ склона и тем гуще, чем склон круче: рельеф без единого пятна цвета.
  ctx.strokeStyle = "rgba(58,44,30,.5)";
  ctx.lineWidth = 1;
  for (let y = 8; y < h; y += 7) {
    for (let x = 8; x < w; x += 7) {
      const d = landField(x, y, segs, o);
      if (d > -2 || d < -30) continue;              // штрихуем только прибережный склон
      const gx = landField(x + 2, y, segs, o) - landField(x - 2, y, segs, o);
      const gy = landField(x, y + 2, segs, o) - landField(x, y - 2, segs, o);
      const len = Math.hypot(gx, gy) || 1;
      const steep = Math.min(1, len * 1.6);
      const nx = gx / len;
      const ny = gy / len;
      const l = 3 + steep * 5;
      ctx.globalAlpha = 0.25 + steep * 0.5;
      ctx.beginPath();
      ctx.moveTo(x - nx * l * 0.5, y - ny * l * 0.5);
      ctx.lineTo(x + nx * l * 0.5, y + ny * l * 0.5);
      ctx.stroke();
    }
  }
  ctx.globalAlpha = 1;

  drawRoadsAndNodes(ctx, nodes, segs, false);
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("штриховка Лемана: рельеф без цвета, штрих вдоль склона", 16, h - 12);
};

/* ---------- стенды ---------- */

const SIZE: [number, number] = [620, 330];

const STANDS: StandDef[] = [
  {
    id: "smooth",
    status: "rejected",
    title: "Гладкий контур",
    tag: "база сравнения",
    note: "Поле расстояний без искажения: земля обнимает узлы и дороги, но берег правильный и оттого мёртвый. В природе гладких берегов не бывает ни на одном масштабе.",
    verdict: "Держим как точку отсчёта: видно, что всю жизнь берегу даёт именно шум, а не форма.",
    size: SIZE,
    draw: terrain({ warp: 0, reach: 34, halo: false, shadow: false, shade: false, political: false }, "warp 0", false)
  },
  {
    id: "fractal",
    status: "waiting",
    title: "Фрактальный берег",
    tag: "domain warp",
    note: "Координаты искажаются шумом перед замером расстояния — крупные бухты и мелкая зубчатость появляются одновременно, из одной формулы. Ни сглаживания, ни отдельного дробления контура не нужно.",
    facts: [["октав", "3"], ["искажение", "26 px"], ["берег", "R = 34 px"]],
    size: SIZE,
    draw: terrain({ warp: 26, reach: 34, halo: false, shadow: false, shade: false, political: false }, "warp 26", false)
  },
  {
    id: "full",
    status: "waiting",
    title: "Берег, ореол, тень, отмывка",
    tag: "картографический набор",
    note: "Ореол — линии, повторяющие контур снаружи; тень по юго-восточной кромке приподнимает землю над листом; отмывка светит с верхнего левого угла. Три приёма, каждый в одну строку, и силуэт превращается в карту.",
    facts: [["ореол", "две линии"], ["тень", "юго-восток"], ["свет", "северо-запад"]],
    verdict: "Это тот минимум, ниже которого «процедурная земля» выглядит заливкой.",
    size: SIZE,
    draw: terrain({ warp: 26, reach: 34, halo: true, shadow: true, shade: true, political: false }, "halo + shadow + shade", false)
  },
  {
    id: "political",
    status: "waiting",
    title: "Страны на земле",
    tag: "оба слоя",
    note: "Физический слой остался прежним, поверх него — бледная политическая заливка по ближайшему узлу, ободки на узлах и подписи: земля курсивом, страна капителью в разрядку. Разная типографика делает половину работы по разделению слоёв.",
    facts: [["земля", "форма и светлота"], ["страна", "оттенок и подпись"], ["узел", "ободок"]],
    verdict: "Ровно атласное разделение: физическая карта и политическая на одном листе.",
    size: SIZE,
    draw: terrain({ warp: 26, reach: 34, halo: true, shadow: true, shade: true, political: true }, "physical + political", true)
  },
  {
    id: "hachures",
    status: "waiting",
    title: "Штриховка склонов",
    tag: "Леман, 1799",
    note: "Штрих идёт вдоль склона и густеет там, где круче. Приём изобретён ровно для нашей задачи: показать рельеф, когда цвет занят чем-то другим. Кандидат на язык осыпи и гребня.",
    size: SIZE,
    draw: drawHachures
  }
];

const section: SectionDef = {
  id: "map-terrain",
  title: "Земля и страна",
  eyebrow: "Карта акта",
  lede:
    "Прототип атласной модели: рельеф показан самой землёй, фракция — страной на ней. " +
    "Берег считается полем расстояний с доменным искажением, поверх — приёмы бумажной картографии.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "why",
      title: "Почему не значки",
      lede: "Значок не создаёт места, он его подписывает."
    },
    {
      kind: "text",
      html:
        "Восемь пиктограмм поверх бледных пятен — это легенда, а не карта: они говорят, <i>что здесь " +
        "числится</i>, но не дают почувствовать место. Земля же не нуждается в подписи «тут остров», " +
        "если нарисована островом. Поэтому рельеф уезжает в <b>форму суши</b>, а знаки остаются только " +
        "как фактура поверхности — лес, болото, камни."
    },
    {
      kind: "table",
      head: ["Слой", "Чем показан"],
      rows: [
        ["Рельеф области", "силуэт земли, берег, отмывка, штриховка склонов"],
        ["Фракция", "бледная заливка страны поверх суши, ободок узла, подпись"],
        ["Вода и пустота между землями", "фон пергамента, дороги идут перешейками"]
      ]
    },
    { kind: "stands", items: STANDS },
    {
      kind: "head",
      id: "how",
      title: "Как это считается",
      lede: "Метабол дал бы круглую кляксу; материк требует другого."
    },
    {
      kind: "table",
      head: ["Шаг", "Что делает"],
      rows: [
        ["Поле расстояний", "до узлов и дорог области: земля обнимает и то, и другое"],
        ["Доменное искажение", "координаты гнутся фрактальным шумом до замера — берег ломается на всех масштабах"],
        ["Порог", "d < 0 суша, d > 0 вода; ореол и тень читаются из того же поля"],
        ["Градиент поля", "готовая нормаль склона: отмывка и штриховка Лемана бесплатно"],
        ["Ближайший узел", "политическая принадлежность точки — это и есть Вороной, посчитанный на лету"],
        ["Печать в RT", "один раз на генерацию акта; в рантайме это просто картинка"]
      ]
    },
    {
      kind: "note",
      html:
        "В движке шаг «ближайший узел» станет настоящим <b>Вороным с релаксацией Ллойда</b> " +
        "(<a href=\"http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/\">redblobgames</a>): " +
        "он даёт соседним землям <b>общую границу</b> вместо наползающих друг на друга пятен. Для одной " +
        "области это избыточно, поэтому здесь честный ближайший узел."
    },
    {
      kind: "note",
      html:
        "<b>Что проверить глазами:</b> не перетягивает ли земля внимание с узлов. Правило, которое " +
        "я бы держала: земля живёт в светлоте и фактуре, узлы — в контрасте и цвете. Если карту " +
        "хочется рассматривать вместо того, чтобы читать, — душим землю, а не узлы."
    }
  ]
};

export default section;
