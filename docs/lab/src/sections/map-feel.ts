/* Карта: подача. Что уже нарисовано в игре и что запланировано, а главное — как на одном экране
   уживаются ДВА независимых слоя: чья это земля (зона фракции) и какой это рельеф (форма области).

   Разбор опирается на приём бумажной картографии: физическая карта показывает рельеф штриховкой и
   значками, политическая — принадлежность заливкой и границами. Оба слоя веками лежат на одном
   листе и не спорят, потому что заняли РАЗНЫЕ каналы восприятия. Тот же приём здесь.

   Канон подачи — docs/wiki/gdd/70-gamefeel/map-presentation.md. */

import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef } from "../types.js";

const GOBLIN = "132,214,92";
const BANDIT = "255,96,80";

/* ---------- общие куски ---------- */

/** Пятно зоны: круги в одном path — заливка объединяет их без внутренних швов. */
function blob(
  ctx: CanvasRenderingContext2D,
  pts: Array<[number, number]>,
  radius: number,
  fill: string,
  salt: number
): void {
  ctx.beginPath();
  pts.forEach(([x, y], i) => {
    const r = radius * (0.78 + jag(i, salt) * 0.5);
    ctx.moveTo(x + r, y);
    ctx.arc(x, y, r, 0, Math.PI * 2);
  });
  ctx.fillStyle = fill;
  ctx.fill();
}

interface Dot {
  x: number;
  y: number;
  /** Цвет зоны для ободка; пусто — ничейный узел. */
  zone?: string;
}

function nodes(ctx: CanvasRenderingContext2D, list: Dot[], ring: boolean): void {
  for (const d of list) {
    ctx.beginPath();
    ctx.arc(d.x, d.y, 7, 0, Math.PI * 2);
    ctx.fillStyle = COL.body;
    ctx.fill();
    ctx.lineWidth = ring && d.zone ? 2.4 : 1.5;
    ctx.strokeStyle = ring && d.zone ? `rgba(${d.zone},.95)` : "rgba(184,134,59,.7)";
    ctx.stroke();
  }
}

function roads(ctx: CanvasRenderingContext2D, list: Dot[], pairs: Array<[number, number]>): void {
  ctx.strokeStyle = "rgba(147,128,94,.5)";
  ctx.lineWidth = 1.4;
  for (const [a, b] of pairs) {
    const from = list[a];
    const to = list[b];
    if (!from || !to) continue;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  }
}

/** Сцена, общая для «плохо» и «хорошо»: те же узлы, те же дороги, разная подача слоёв. */
function scene(w: number, h: number): { list: Dot[]; pairs: Array<[number, number]> } {
  const left = w * 0.12;
  const step = (w * 0.76) / 4;
  const mid = h / 2 - 6;
  const list: Dot[] = [];
  for (let c = 0; c < 5; c++) {
    const rows = c === 2 ? 2 : 3;
    for (let r = 0; r < rows; r++) {
      list.push({
        x: left + c * step,
        y: mid + (r - (rows - 1) / 2) * 40,
        zone: c < 2 ? GOBLIN : c === 2 && r === 0 ? GOBLIN : BANDIT
      });
    }
  }
  const pairs: Array<[number, number]> = [];
  let base = 0;
  for (let c = 0; c + 1 < 5; c++) {
    const rows = c === 2 ? 2 : 3;
    const next = c + 1 === 2 ? 2 : 3;
    for (let r = 0; r < rows; r++)
      for (let n = 0; n < next; n++)
        if (Math.abs(r - n) <= 1) pairs.push([base + r, base + rows + n]);
    base += rows;
  }
  return { list, pairs };
}

/* ---------- стенд 1: как НЕ надо ---------- */

const drawClash: DrawFn = (ctx, w, h) => {
  const { list, pairs } = scene(w, h);

  // Рельеф пятном — и он немедленно спорит с пятном зоны за одно и то же место.
  blob(ctx, [[w * 0.2, h * 0.34], [w * 0.36, h * 0.42], [w * 0.5, h * 0.36]], 46, "rgba(184,134,59,.22)", 2);
  blob(ctx, [[w * 0.56, h * 0.62], [w * 0.72, h * 0.58], [w * 0.86, h * 0.66]], 44, "rgba(138,206,255,.20)", 6);

  blob(ctx, [[w * 0.18, h * 0.5], [w * 0.34, h * 0.56], [w * 0.46, h * 0.5]], 48, `rgba(${GOBLIN},.22)`, 3);
  blob(ctx, [[w * 0.62, h * 0.44], [w * 0.78, h * 0.5], [w * 0.9, h * 0.44]], 46, `rgba(${BANDIT},.22)`, 7);

  roads(ctx, list, pairs);
  nodes(ctx, list, false);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(255,96,80,.9)";
  ctx.fillText("четыре пятна на одном листе: чей это край — уже не прочесть", 20, h - 16);
};

/* ---------- стенд 2: как надо ---------- */

/** Значки рельефа: ахроматические, мелкие, россыпью по площади — как на бумажной карте. */
function reliefMarks(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  kind: "scree" | "comb" | "vale" | "lair",
  salt: number
): void {
  ctx.strokeStyle = "rgba(58,44,30,.55)";
  ctx.fillStyle = "rgba(58,44,30,.5)";
  ctx.lineWidth = 1.2;

  for (let i = 0; i < 7; i++) {
    const x = cx + (jag(i, salt) - 0.5) * 90;
    const y = cy + (jag(i, salt + 40) - 0.5) * 58;
    if (kind === "scree") {
      // Осыпь — россыпь камешков, сползающих вбок.
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.lineTo(x + 5, y + 4);
      ctx.stroke();
    } else if (kind === "comb") {
      // Гребень — короткие параллельные штрихи.
      ctx.beginPath();
      ctx.moveTo(x, y - 4);
      ctx.lineTo(x, y + 4);
      ctx.stroke();
    } else if (kind === "vale") {
      // Долина — точки-кочки.
      ctx.beginPath();
      ctx.arc(x, y, 1.6, 0, Math.PI * 2);
      ctx.fill();
    } else {
      // Логово — угловатые «зубцы».
      ctx.beginPath();
      ctx.moveTo(x - 4, y + 3);
      ctx.lineTo(x, y - 4);
      ctx.lineTo(x + 4, y + 3);
      ctx.stroke();
    }
  }
}

const drawLayers: DrawFn = (ctx, w, h) => {
  const { list, pairs } = scene(w, h);

  // 1. Зона — ЦВЕТОМ, снизу и бледно.
  blob(ctx, [[w * 0.18, h * 0.5], [w * 0.34, h * 0.56], [w * 0.46, h * 0.5]], 52, `rgba(${GOBLIN},.13)`, 3);
  blob(ctx, [[w * 0.62, h * 0.44], [w * 0.78, h * 0.5], [w * 0.9, h * 0.44]], 50, `rgba(${BANDIT},.13)`, 7);

  // 2. Рельеф — ахроматическими значками, поверх цвета и не мешая ему.
  reliefMarks(ctx, w * 0.26, h * 0.42, "vale", 11);
  reliefMarks(ctx, w * 0.55, h * 0.62, "scree", 19);
  reliefMarks(ctx, w * 0.82, h * 0.4, "lair", 23);

  roads(ctx, list, pairs);
  // 3. Принадлежность — ободком на узле: она не зависит от того, видно ли пятно.
  nodes(ctx, list, true);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(140,255,166,.9)";
  ctx.fillText("цвет = чья земля · значки = какой рельеф · ободок = правда на узле", 20, h - 16);
};

/* ---------- стенд 3: словарь значков ---------- */

const drawGlyphs: DrawFn = (ctx, w, h) => {
  const kinds: Array<["scree" | "comb" | "vale" | "lair", string]> = [
    ["vale", "Долина — кочки"],
    ["comb", "Гребень — параллельные штрихи"],
    ["scree", "Осыпь — камни сползают вбок"],
    ["lair", "Логово — зубцы"]
  ];
  const step = h / (kinds.length + 0.6);
  kinds.forEach(([kind, label], i) => {
    const y = step * (i + 0.8);
    reliefMarks(ctx, w * 0.26, y, kind, 31 + i * 7);
    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.textBaseline = "middle";
    ctx.fillText(label, w * 0.52, y);
    ctx.textBaseline = "alphabetic";
  });
};

/* ---------- стенд 4: состояние узла ---------- */

const drawStates: DrawFn = (ctx, w, h) => {
  const labels = ["доступен", "пройден", "недоступен", "на Grand Line"];
  const step = w / (labels.length + 0.4);
  const y = h / 2 - 10;

  labels.forEach((label, i) => {
    const x = step * (i + 0.7);
    const alpha = i === 0 ? 1 : i === 1 ? 0.55 : i === 2 ? 0.3 : 1;

    if (i === 3) {
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      ctx.strokeStyle = "rgba(255,204,51,.5)";
      ctx.lineWidth = 5;
      ctx.beginPath();
      ctx.moveTo(x - 34, y);
      ctx.lineTo(x + 34, y);
      ctx.stroke();
      ctx.restore();
    }

    ctx.beginPath();
    ctx.arc(x, y, 13, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(90,74,52,${alpha})`;
    ctx.fill();
    ctx.lineWidth = 2.4;
    ctx.strokeStyle = `rgba(${GOBLIN},${alpha})`;
    ctx.stroke();

    if (i === 1) {
      ctx.strokeStyle = `rgba(184,134,59,.8)`;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(x - 5, y);
      ctx.lineTo(x - 1, y + 4);
      ctx.lineTo(x + 6, y - 5);
      ctx.stroke();
    }

    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.textAlign = "center";
    ctx.fillText(label, x, y + 42);
    ctx.textAlign = "left";
  });

  ctx.fillStyle = "rgba(147,128,94,.75)";
  ctx.fillText("состояние живёт в ЯРКОСТИ — оттенок занят фракцией и трогать его нельзя", 20, h - 16);
};

const section: SectionDef = {
  id: "map-feel",
  title: "Подача карты",
  eyebrow: "Карта акта",
  lede:
    "Два независимых слоя на одном листе: чья это земля и какой это рельеф. Плюс реестр того, " +
    "что уже нарисовано в игре и что запланировано.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "channels",
      title: "Один канал — один владелец",
      lede:
        "Задача ровно та, которую бумажная картография решила давно: физическая карта показывает " +
        "рельеф штриховкой и значками, политическая — принадлежность заливкой. Веками лежат на " +
        "одном листе и не спорят, потому что заняли разные каналы восприятия."
    },
    {
      kind: "table",
      head: ["Канал", "Владелец", "Почему он"],
      rows: [
        ["Оттенок (цвет)", "фракция зоны", "цвет — единственное, что читается боковым зрением на всей площади"],
        ["Значки и штриховка (ахроматика)", "рельеф области", "не спорит с цветом: другая размерность"],
        ["Форма графа и рисунок дорог", "рельеф области", "осыпь видна диагоналями, гребень — параллелями"],
        ["Яркость и насыщенность", "состояние узла", "доступен, пройден, недоступен — только светлотой"],
        ["Иконка внутри узла", "тип узла", "бой, лавка, «?» — как сейчас"],
        ["Ободок узла", "принадлежность зоне", "правда, не зависящая от того, видно ли пятно"],
        ["Толщина и фактура линии", "вид дороги", "тракт, тропа, привратник, заслон"]
      ]
    },
    {
      kind: "split",
      items: [
        {
          id: "clash",
          status: "rejected",
          title: "Оба слоя пятнами",
          tag: "как не надо",
          note: "Самый естественный первый ход — дать рельефу тоже своё пятно. Через минуту на листе четыре перекрывающихся заливки, и ни одна не читается.",
          verdict: "Отклонено на бумаге, до кода: два цветных слоя в одном канале не расходятся никакими настройками прозрачности.",
          size: [620, 330],
          draw: drawClash
        },
        {
          id: "layers",
          status: "waiting",
          title: "Разными каналами",
          tag: "предложение",
          note: "Зона — бледный цвет снизу. Рельеф — ахроматические значки поверх. Принадлежность — ободок на узле, который виден при любом зуме и не зависит от пятна.",
          facts: [["цвет", "фракция"], ["значки", "рельеф"], ["ободок", "правда на узле"]],
          verdict: "Оба слоя читаются одновременно, потому что физически не конкурируют.",
          size: [620, 330],
          draw: drawLayers
        }
      ]
    },
    {
      kind: "head",
      id: "glyphs",
      title: "Словарь значков рельефа",
      lede: "Процедурная россыпь по площади области, а не спрайты: рисуется тем же шейдером, что пятно зоны."
    },
    {
      kind: "stands",
      items: [
        {
          id: "relief-glyphs",
          status: "waiting",
          title: "Четыре рельефа",
          note: "Значок обязан читаться на общем зуме карты, поэтому он крупный штрих, а не иллюстрация. Долина, гребень, осыпь, логово.",
          size: [320, 260],
          draw: drawGlyphs
        },
        {
          id: "node-states",
          status: "waiting",
          title: "Состояние — яркостью",
          note: "Оттенок ободка занят фракцией навсегда, поэтому доступность говорит только светлотой. Узел на Grand Line подсвечен самой линией, а не своим цветом.",
          size: [320, 260],
          draw: drawStates
        }
      ]
    },
    {
      kind: "head",
      id: "registry",
      title: "Что уже в игре",
      lede: "Тумблеры живут в реестре VisualToggles; выключается всё, что включается."
    },
    {
      kind: "table",
      head: ["Эффект", "Тумблер", "Заметка"],
      rows: [
        ["Лист пергамента с рваным краем", "map.sheet", "рваность в шейдере по периметру, с поправкой на соотношение сторон"],
        ["Стол под листом, тёплое пятно света", "map.table", "углы тонут; край стола в кадре = баг"],
        ["Шторка перехода на шаг", "—", "выбор засчитывается на закрытом кадре, подмены не видно"],
        ["Моргание доступных узлов", "map.pulse", "одна огибающая: два поля разъезжались"],
        ["Бегущая волна по дорожкам", "map.pathflow", "доли такта, не секунды"],
        ["Поездка фишки-шлема", "map.travel", "выключена, заменена шторкой"],
        ["Туман карты", "map.fog", "выключен: «не то, мб позже»"],
        ["Локальная постобработка карты", "post.map", "свой Volume, арену не накрывает"]
      ]
    },
    {
      kind: "head",
      id: "planned",
      title: "Что запланировано",
      lede: "Владелец списка — gdd/70-gamefeel/map-presentation.md; здесь указатель."
    },
    {
      kind: "table",
      head: ["Эффект", "Зачем"],
      rows: [
        ["Пятно зоны: SDF-метабол по узлам + domain warp", "органический островной край вместо полигонов; печётся в RT один раз"],
        ["Значки рельефа", "второй слой, не спорящий с цветом"],
        ["Ободок зоны на узле", "принадлежность, которой можно верить"],
        ["Картуш с именем зоны", "«Молниеносные Гоблины» читается сразу"],
        ["Карточка области при наведении", "имя, строка образа, строка правила"],
        ["Grand Line", "тёплая сквозная линия и три отметки наград на ней"],
        ["Дверь подземелья, привратник", "риск виден, начинка нет"],
        ["Виды дорог линией", "тракт, тропа, привратник, заслон, вслепую, режущая"],
        ["Раскрытие зоны при входе", "чернила расползаются от узла"],
        ["Шов между областями", "разделение без второго пятна"],
        ["Грейдинг и ambient-партиклы под зону", "воздух местности"],
        ["Звук карты", "шелест бумаги, перо"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открытые вопросы к обсуждению:</b> сколько цветов фракций карта выдержит одновременно " +
        "(думаю, три — дальше оттенки перестают различаться на бледной заливке) · нужен ли значкам " +
        "рельефа отдельный тумблер · как показывать зону, накрывшую подземелье, у которого " +
        "содержимое скрыто."
    }
  ]
};

export default section;
