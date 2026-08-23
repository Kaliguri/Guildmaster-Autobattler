/* Фон экранов меты: чем закрыт кадр под настройками, паузой и главным меню.

   Раздел отличается от остальных «Интерфейса» тем, что здесь ЦВЕТ и есть предмет: чертёж соседних
   разделов нарочно серый, чтобы разговор шёл про раскладку, а тут наоборот — раскладка решена, и
   остался вопрос «из чего сделана плоскость под ней».

   Числа рефа не на глаз: `Art_Dev/UI Refs/Guildrun Settings.jpg` промерен пипеткой по сетке
   05.08.2026, замеры приведены в первой таблице раздела. */

import type { SectionDef, DrawFn } from "../types.js";

/** Наши ступени патины (tokens.primitives.uss). Держим здесь литералами: стенд показывает,
 *  что получится ИЗ НИХ, и подмена значения молча соврала бы про результат. */
const PATINA = {
  p950: [1, 12, 14],
  p900: [6, 65, 71],
  p800: [18, 62, 66],
  p700: [24, 86, 92],
  p600: [34, 121, 129],
  p500: [49, 174, 185]
} as const;

/** Цвет рефа по замеру: светлый конец и тёмный конец. */
const REF = { light: [27, 75, 63], dark: [2, 10, 12] } as const;

type Rgb = readonly [number, number, number] | number[];

function mix(a: Rgb, b: Rgb, t: number): string {
  const r = Math.round(a[0] + (b[0] - a[0]) * t);
  const g = Math.round(a[1] + (b[1] - a[1]) * t);
  const bl = Math.round(a[2] + (b[2] - a[2]) * t);
  return `rgb(${r}, ${g}, ${bl})`;
}

function rgba(c: Rgb, a: number): string {
  return `rgba(${c[0]}, ${c[1]}, ${c[2]}, ${a})`;
}

/** Слой 1 — вертикальный градиент: свет собран в ВЕРХНЕЙ трети, а не по центру и не сверху донизу.
 *  У рефа пик яркости на y=25% (lum 47), верхняя кромка тусклее (39), низ уходит в 8. */
function verticalWash(ctx: CanvasRenderingContext2D, w: number, h: number, light: Rgb, dark: Rgb): void {
  const g = ctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0.00, mix(dark, light, 0.72));
  g.addColorStop(0.25, mix(dark, light, 1.00));
  g.addColorStop(0.55, mix(dark, light, 0.80));
  g.addColorStop(0.72, mix(dark, light, 0.62));
  g.addColorStop(1.00, mix(dark, light, 0.06));
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
}

/** Слой 2 — виньетка ПО ГОРИЗОНТАЛИ: у рефа центр строки светлее краёв (59 против 41-49). */
function sideVignette(ctx: CanvasRenderingContext2D, w: number, h: number, strength = 0.5): void {
  const g = ctx.createLinearGradient(0, 0, w, 0);
  g.addColorStop(0.0, `rgba(0, 0, 0, ${strength})`);
  g.addColorStop(0.42, "rgba(0, 0, 0, 0)");
  g.addColorStop(0.58, "rgba(0, 0, 0, 0)");
  g.addColorStop(1.0, `rgba(0, 0, 0, ${strength})`);
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
}

/** Слой 3 — тёмная волна снизу. Граница НЕ прямая: у рефа она на 0.775 высоты у краёв и опускается
 *  до 0.855 к центру, то есть свет вытянут вниз посередине кадра. */
function bottomWave(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  edge: number,
  dip: number,
  alpha: number,
  rim = false
): void {
  ctx.save();
  ctx.beginPath();
  ctx.moveTo(0, edge * h);
  ctx.bezierCurveTo(w * 0.28, (edge + dip) * h, w * 0.72, (edge + dip) * h, w, edge * h);
  ctx.lineTo(w, h);
  ctx.lineTo(0, h);
  ctx.closePath();
  const g = ctx.createLinearGradient(0, edge * h, 0, h);
  g.addColorStop(0, `rgba(0, 0, 0, 0)`);
  g.addColorStop(0.45, `rgba(0, 0, 0, ${alpha * 0.75})`);
  g.addColorStop(1, `rgba(0, 0, 0, ${alpha})`);
  ctx.fillStyle = g;
  ctx.fill();
  // Светлая кромка по гребню: она и делает волну ФОРМОЙ, а не просто затемнением снизу.
  if (rim) {
    ctx.beginPath();
    ctx.moveTo(0, edge * h);
    ctx.bezierCurveTo(w * 0.28, (edge + dip) * h, w * 0.72, (edge + dip) * h, w, edge * h);
    ctx.strokeStyle = rgba(PATINA.p500, 0.13);
    ctx.lineWidth = Math.max(1, h * 0.005);
    ctx.stroke();
  }
  ctx.restore();
}

/** Слой 4 — диагональная штриховка света. У рефа она есть, но амплитуда всего +-2 единицы яркости
 *  (4-5% от локальной) и только в верхней трети: это подпись, а не узор. */
function lightStripes(ctx: CanvasRenderingContext2D, w: number, h: number, gain: number): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const bands = [
    { y: 0.02, len: 0.62, a: 1.0 },
    { y: 0.11, len: 0.44, a: 0.7 },
    { y: 0.19, len: 0.80, a: 0.85 },
    { y: 0.31, len: 0.35, a: 0.5 },
    { y: 0.44, len: 0.55, a: 0.4 }
  ];
  for (const b of bands) {
    const x0 = w * (1 - b.len);
    const g = ctx.createLinearGradient(x0, 0, w, 0);
    g.addColorStop(0, rgba(PATINA.p500, 0));
    g.addColorStop(0.5, rgba(PATINA.p500, 0.05 * gain * b.a));
    g.addColorStop(1, rgba(PATINA.p500, 0));
    ctx.fillStyle = g;
    ctx.save();
    ctx.translate(0, b.y * h);
    ctx.rotate(-0.055);
    ctx.fillRect(x0, 0, w - x0, h * 0.055);
    ctx.restore();
  }
  ctx.restore();
}

/** Слой 5 — зерно. Детерминированное (свой генератор), иначе стенд дрожал бы между перерисовками
 *  и «стало хуже» читалось бы там, где просто выпал другой шум. */
function grain(ctx: CanvasRenderingContext2D, w: number, h: number, alpha: number): void {
  let seed = 20260805;
  const rnd = () => {
    seed = (seed * 1664525 + 1013904223) % 4294967296;
    return seed / 4294967296;
  };
  // Шаг 2, а не 3: на трёх пикселях зерно читается точками телевизионного шума, а не фактурой.
  // Порог 0.82 оставляет светлыми примерно каждый шестой пиксель — этого хватает, чтобы убрать
  // пластиковую гладкость градиента, и мало, чтобы фактуру было видно как отдельный слой.
  const step = 2;
  for (let y = 0; y < h; y += step) {
    for (let x = 0; x < w; x += step) {
      const v = rnd();
      if (v > 0.82) {
        ctx.fillStyle = `rgba(255, 255, 255, ${alpha * (v - 0.82) * 2})`;
        ctx.fillRect(x, y, step, step);
      } else if (v < 0.12) {
        ctx.fillStyle = `rgba(0, 0, 0, ${alpha * (0.12 - v) * 2})`;
        ctx.fillRect(x, y, step, step);
      }
    }
  }
}

/** Разметка поверх фона: показывает, что фон обязан держать под собой РЕАЛЬНЫЙ экран, а не быть
 *  красивым сам по себе. Строки настроек, лента табов, ряд кнопок — грубо, но в натуральных долях. */
function settingsOverlay(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  ctx.save();
  // лента табов
  ctx.fillStyle = "rgba(255, 255, 255, 0.10)";
  ctx.fillRect(w * 0.28, h * 0.06, w * 0.44, h * 0.055);
  ctx.fillStyle = rgba(PATINA.p600, 0.85);
  ctx.fillRect(w * 0.42, h * 0.06, w * 0.15, h * 0.055);
  // строки: подпись слева, виджет справа
  for (let i = 0; i < 6; i++) {
    const y = h * (0.19 + i * 0.059);
    ctx.fillStyle = "rgba(233, 226, 212, 0.62)";
    ctx.fillRect(w * 0.282, y, w * 0.115, h * 0.016);
    ctx.fillStyle = "rgba(0, 0, 0, 0.42)";
    ctx.fillRect(w * 0.5, y - h * 0.012, w * 0.218, h * 0.042);
    ctx.strokeStyle = "rgba(233, 226, 212, 0.22)";
    ctx.lineWidth = 1;
    ctx.strokeRect(w * 0.5, y - h * 0.012, w * 0.218, h * 0.042);
  }
  // ряд действий внизу
  ctx.fillStyle = "rgba(0, 0, 0, 0.45)";
  ctx.fillRect(w * 0.41, h * 0.86, w * 0.18, h * 0.072);
  ctx.strokeStyle = "rgba(233, 226, 212, 0.3)";
  ctx.strokeRect(w * 0.41, h * 0.86, w * 0.18, h * 0.072);
  ctx.restore();
}

/** Обёртка: фон рисует переданный слой, поверх ложится разметка экрана. */
function withScreen(paint: DrawFn): DrawFn {
  return (ctx, w, h) => {
    paint(ctx, w, h);
    settingsOverlay(ctx, w, h);
  };
}

// --- Сам реф, воспроизведённый по замеру ---

const drawRef: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, REF.light, REF.dark);
  sideVignette(ctx, w, h, 0.34);
  bottomWave(ctx, w, h, 0.775, 0.08, 0.92);
  lightStripes(ctx, w, h, 1);
};

// --- Варианты в нашей патине ---

const drawA: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, PATINA.p700, PATINA.p950);
  sideVignette(ctx, w, h, 0.34);
  bottomWave(ctx, w, h, 0.775, 0.08, 0.92);
};

const drawB: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, PATINA.p700, PATINA.p950);
  sideVignette(ctx, w, h, 0.28);
  // Три силуэта разной глубины: у рефа волна одна, здесь она становится приёмом. Кромка каждой
  // подсвечена, иначе слои сливаются в один размыв — первая проба именно так и выглядела.
  bottomWave(ctx, w, h, 0.58, 0.12, 0.30, true);
  bottomWave(ctx, w, h, 0.72, -0.08, 0.44, true);
  bottomWave(ctx, w, h, 0.86, 0.08, 0.80, false);
};

const drawC: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, PATINA.p700, PATINA.p950);
  sideVignette(ctx, w, h, 0.34);
  bottomWave(ctx, w, h, 0.775, 0.08, 0.92);
  lightStripes(ctx, w, h, 3.2);
};

const drawD: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, PATINA.p700, PATINA.p950);
  sideVignette(ctx, w, h, 0.34);
  bottomWave(ctx, w, h, 0.775, 0.08, 0.92);
  grain(ctx, w, h, 0.05);
};

const drawE: DrawFn = (ctx, w, h) => {
  // Наш нынешний ответ: тёплый стол. Здесь он нарисован тем же способом, что и остальные, чтобы
  // сравнение шло по одному правилу, а не «шейдер против картинки».
  const wood = [54, 46, 38] as const;
  const woodDark = [22, 18, 14] as const;
  verticalWash(ctx, w, h, wood, woodDark);
  ctx.save();
  ctx.globalAlpha = 0.5;
  for (let i = 0; i < 26; i++) {
    const y = (i / 26) * h;
    ctx.strokeStyle = i % 2 === 0 ? "rgba(0,0,0,0.16)" : "rgba(255,240,210,0.05)";
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.bezierCurveTo(w * 0.3, y + h * 0.012, w * 0.7, y - h * 0.012, w, y);
    ctx.stroke();
  }
  ctx.restore();
  sideVignette(ctx, w, h, 0.42);
};

const drawF: DrawFn = (ctx, w, h) => {
  verticalWash(ctx, w, h, PATINA.p700, PATINA.p950);
  sideVignette(ctx, w, h, 0.3);
  bottomWave(ctx, w, h, 0.66, 0.09, 0.3);
  bottomWave(ctx, w, h, 0.80, 0.06, 0.85);
  lightStripes(ctx, w, h, 2.2);
  grain(ctx, w, h, 0.035);
};

const section: SectionDef = {
  id: "ui-backdrop",
  title: "Фон экранов меты",
  eyebrow: "Интерфейс",
  transport: false,
  lede:
    "Под настройками, паузой и главным меню лежит не игра, а собственная плоскость. Раздел отвечает " +
    "на один вопрос: из чего она сделана. Раскладка уже решена, поэтому здесь единственный раз в " +
    "«Интерфейсе» показан цвет, а не чертёж.",
  blocks: [
    {
      kind: "head",
      id: "ref",
      title: "Что на самом деле у рефа",
      lede:
        "Промерено пипеткой по сетке, а не описано на глаз: <code>Guildrun Settings.jpg</code>, " +
        "05.08.2026. Прежняя запись в брифе настроек («средняя яркость 9.7, фон почти чёрный») " +
        "ОШИБАЛАСЬ — на ней стояло решение гасить кадр скримом, и решение было снято."
    },
    {
      kind: "table",
      head: ["Что мерили", "Значение", "Что это значит"],
      rows: [
        ["Средняя яркость кадра", "41.8 из 255", "Плоскость средней темноты, а не чернота"],
        ["Тон", "H=165°, S=0.47", "Зелёный (малахит). Наша патина холоднее: H=184-187°"],
        ["Светлее всего", "#1B4B3F на y=25%", "Свет собран в ВЕРХНЕЙ трети, не по центру"],
        ["Темнее всего", "#020A0C на y=96%", "Нижняя четверть уходит почти в чёрное"],
        ["Пик по горизонтали", "59 в центре против 41-49 по краям", "Есть боковая виньетка"],
        ["Граница тени", "0.775 высоты у краёв, 0.855 в центре", "Волна ПРОГНУТА вниз посередине"],
        ["Рябь полос, верхняя треть", "±2 единицы (4-5%)", "Штриховка есть, но она подпись, а не узор"],
        ["Рябь полос, нижняя половина", "±0.8 единицы", "Ниже середины полос нет вовсе"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Главный вывод замера:</b> фон рефа — не текстура и не картинка. Это ТРИ градиента " +
        "(вертикальный размыв, боковая виньетка, тёмная волна снизу) плюс еле различимая диагональная " +
        "штриховка. Ни зерна, ни рисунка материала, ни фотофактуры в нём нет — то, что читается как " +
        "«красивый эффект», собрано из плавных заливок."
    },
    {
      kind: "split",
      items: [
        {
          id: "backdrop-ref-rebuild",
          status: "note",
          title: "Реф, собранный из замеров",
          tag: "эталон",
          size: [480, 270],
          note:
            "Не скриншот, а ВОСПРОИЗВЕДЕНИЕ по числам таблицы: если бы фон рефа был чем-то большим, " +
            "чем три градиента, эта картинка на него не походила бы.",
          facts: [
            ["слои", "размыв + виньетка + волна + штрих"],
            ["цвет", "#1B4B3F → #020A0C"],
            ["волна", "0.775 края, 0.855 центр"]
          ],
          draw: drawRef
        },
        {
          id: "ours-today",
          status: "note",
          title: "Что под экраном сегодня",
          tag: "как есть",
          size: [480, 270],
          note:
            "Стол — тёплое дерево из <code>MapStyle</code>, общее с картой акта. Держит регистр " +
            "«гроссбух», в котором живут инвентарь, лавка и награда.",
          facts: [
            ["источник", "SH_Map_Table"],
            ["тон", "тёплый, H≈30°"],
            ["регистр", "гроссбух"]
          ],
          verdict:
            "Спорит с регистром меты. Токены темы разводят два языка: мир и забег — дерево и латунь, " +
            "мета — тёмное стекло. Настройки лежат по вторую сторону этой границы.",
          draw: withScreen(drawE)
        }
      ]
    },
    {
      kind: "head",
      id: "variants",
      title: "Четыре варианта в нашей патине",
      lede:
        "Все нарисованы одним набором слоёв и отличаются тем, ЧТО добавлено сверх минимума. " +
        "Разметка экрана положена поверх нарочно: фон обязан держать под собой строки, а не " +
        "нравиться сам по себе."
    },
    {
      kind: "stands",
      items: [
        {
          id: "backdrop-var-a",
          status: "waiting",
          title: "A · Три градиента",
          tag: "минимум",
          size: [360, 203],
          note: "Прямой перенос рефа на нашу патину: размыв, боковая виньетка, тёмная волна. И всё.",
          facts: [
            ["слоёв", "3"],
            ["цена", "один шейдер, 4 числа"],
            ["движение", "нет"]
          ],
          verdict: "Дешевле некуда и ближе всего к рефу. Риск — на большом экране прочитается пустовато.",
          draw: withScreen(drawA)
        },
        {
          id: "backdrop-var-b",
          status: "waiting",
          title: "B · Дюны",
          tag: "геометрия",
          size: [360, 203],
          note:
            "Волна становится приёмом: три силуэта разной глубины, средний прогнут в другую сторону. " +
            "Низ кадра получает форму, а не просто затемнение.",
          facts: [
            ["слоёв", "5"],
            ["цена", "тот же шейдер, 3 кривые"],
            ["движение", "напрашивается медленный ход"]
          ],
          verdict: "Даёт кадру горизонт. Опасность — читается как пейзаж и начинает спорить с содержимым.",
          draw: withScreen(drawB)
        },
        {
          id: "backdrop-var-c",
          status: "waiting",
          title: "C · Лучи",
          tag: "штриховка",
          size: [360, 203],
          note:
            "Диагональная штриховка рефа, усиленная втрое: у него она на пределе различимости, здесь " +
            "её видно. Ниже середины кадра полос по-прежнему нет.",
          facts: [
            ["слоёв", "4"],
            ["усиление против рефа", "×3.2"],
            ["движение", "можно вести медленно"]
          ],
          verdict: "Единственный вариант с ощущением света. Легко переборщить — на ×5 это уже обои.",
          draw: withScreen(drawC)
        },
        {
          id: "backdrop-var-d",
          status: "waiting",
          title: "D · Зерно",
          tag: "фактура",
          size: [360, 203],
          note:
            "Градиенты плюс чернильное зерно. Фактура наша, а не Guildrun: у него зерна нет вовсе, " +
            "зато оно роднит фон с материалом стола и переходом-чернилами.",
          facts: [
            ["слоёв", "4"],
            ["зерно", "5% альфы, шаг 3px"],
            ["движение", "не двигать — поплывёт"]
          ],
          verdict: "Убирает пластиковую гладкость градиента. Плата — на тёмном низе зерно заметнее, чем на свету.",
          draw: withScreen(drawD)
        },
        {
          id: "var-f",
          status: "waiting",
          title: "F · Всё сразу",
          tag: "предел",
          size: [360, 203],
          note:
            "Дюны, лучи и зерно вместе — не предложение, а граница: так видно, где набор слоёв " +
            "перестаёт быть фоном и начинает быть картиной.",
          facts: [
            ["слоёв", "7"],
            ["назначение", "показать перебор"],
            ["движение", "—"]
          ],
          verdict: "Тут фон уже соперничает с интерфейсом. Полезен как верхняя граница, а не как кандидат.",
          draw: withScreen(drawF)
        },
        {
          id: "var-a-plain",
          status: "note",
          title: "A без разметки",
          tag: "чистый кадр",
          size: [360, 203],
          note: "Тот же вариант A, но без строк поверх — чтобы судить саму плоскость.",
          facts: [["слоёв", "3"]],
          draw: drawA
        }
      ]
    },
    {
      kind: "head",
      id: "notes",
      title: "Что решать помимо картинки"
    },
    {
      kind: "table",
      head: ["Вопрос", "Как есть", "Чем платим за смену"],
      rows: [
        [
          "Тон патины холоднее рефа",
          "H=184-187° против 165°",
          "Либо принимаем свою бирюзу, либо заводим тёплую ступень — а она поедет по всем кнопкам"
        ],
        [
          "Один фон на всю мету или разные",
          "стол общий с картой",
          "Разные фоны = граница между меню и настройками читается как смена сцены"
        ],
        [
          "Двигать или нет",
          "стол статичен",
          "Медленный ход лучей оживляет, но за ним придётся следить в каждом кадре паузы"
        ],
        [
          "Кто рисует",
          "MenuBackdropView, своя камера и квад",
          "Слои дописываются в тот же шейдер: новых сущностей не нужно"
        ]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Рекомендация:</b> вариант <b>A</b> как основа и <b>C</b> поверх него — три градиента " +
        "закрывают кадр, штриховка даёт свет, и оба слоя живут в одном шейдере рядом с тем, что уже " +
        "рисует стол. <b>B</b> красив, но даёт горизонт, а под ним ещё лежат шесть строк настроек; " +
        "<b>D</b> стоит вернуть, когда фон будет утверждён — зерно правится одним числом в любой момент."
    }
  ]
};

export default section;
