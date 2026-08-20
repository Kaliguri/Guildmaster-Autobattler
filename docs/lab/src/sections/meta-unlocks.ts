/* Мета: что копится между забегами и как это видно в хабе.

   Раздел отвечает на два разных вопроса и потому состоит из двух половин.

   1. СОДЕРЖАНИЕ — все открытия по восьми осям. Их полтора десятка в демо и втрое больше за ним,
      и до этого раздела они лежали россыпью по трём канон-докам: Капитаны в guildmaster,
      дом в guild-development, карта в act-map-regions. Ни одного места, где видно ВЕСЬ объём.
   2. ПОДАЧА — созвездия: как эта россыпь ложится на один экран, чтобы игрок видел, что кого
      открывает. Заявка Макса 2026-08-03: «мета виделась красиво и на своем экране».

   Почему созвездие, а не дерево умений. Узел дерева PoE стоит очко из КОНЕЧНОГО запаса, поэтому
   дерево обещает «или-или» и учит копить. Наша мета не отнимает: взяв одно, игрок не теряет
   другое. Язык PoE соврал бы про характер системы ещё до первого клика — отсюда модель
   достижений (корень, ветки, видимая зависимость), а очки не заводятся вовсе.

   Рисовалки берут узлы из одного реестра ниже: стенды — его срезы, а не отдельные картинки.
   Иначе созвездие и таблица разошлись бы на первой же правке. */

import { COL, jag } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- каналы оплаты ----------
   Канал определяет ФОРМУ рамки, а не только цвет: цветовую разницу съедает и дальтонизм, и
   тёмный фон, а форма читается силуэтом. Приём взят у достижений Minecraft (task/goal/challenge). */

const CH = {
  vault: { color: "255,204,51", label: "валюта гильдии", form: "square" },
  deed: { color: "77,242,255", label: "веха", form: "circle" },
  copper: { color: "140,255,166", label: "мелочь", form: "spike" }
} as const;

type Channel = keyof typeof CH;

/** Состояние узла. Скрытый существует ради «мега-уников» (Совершенство, Стиллер заклинаний,
 *  2026-08-03): их нельзя показывать даже силуэтом, иначе сюрприз потрачен до встречи. */
type State = "open" | "available" | "locked" | "hidden";

interface Node {
  x: number;
  y: number;
  label: string;
  ch: Channel;
  state: State;
  /** Индексы узлов, которые этот отпирает. Линия рисуется от родителя к ребёнку. */
  to?: number[];
}

/* ---------- рисовалка узла ---------- */

function starfield(ctx: CanvasRenderingContext2D, w: number, h: number, salt: number): void {
  for (let i = 0; i < 90; i++) {
    const x = jag(i, salt) * w;
    const y = jag(i, salt + 5) * h;
    const a = 0.05 + jag(i, salt + 9) * 0.12;
    ctx.fillStyle = `rgba(200,190,170,${a})`;
    ctx.fillRect(x, y, 1, 1);
  }
}

function nodeShape(ctx: CanvasRenderingContext2D, n: Node, r: number): void {
  const form = CH[n.ch].form;
  ctx.beginPath();
  if (form === "square") {
    ctx.rect(n.x - r, n.y - r, r * 2, r * 2);
  } else if (form === "circle") {
    ctx.arc(n.x, n.y, r, 0, Math.PI * 2);
  } else {
    for (let i = 0; i < 12; i++) {
      const rad = i % 2 === 0 ? r * 1.25 : r * 0.55;
      const a = (Math.PI * 2 * i) / 12 - Math.PI / 2;
      const px = n.x + Math.cos(a) * rad;
      const py = n.y + Math.sin(a) * rad;
      if (i === 0) ctx.moveTo(px, py);
      else ctx.lineTo(px, py);
    }
    ctx.closePath();
  }
}

/** Один узел со своим состоянием. Подпись — справа, кроме случая, когда её просят снизу. */
function drawNode(
  ctx: CanvasRenderingContext2D,
  n: Node,
  opts: { r?: number; label?: boolean; below?: boolean } = {}
): void {
  const r = opts.r ?? 7;
  const c = CH[n.ch].color;

  if (n.state === "hidden") {
    // Скрытое не показывается: на его месте остаётся пустое небо и еле заметная пыль.
    ctx.fillStyle = "rgba(147,128,94,.16)";
    ctx.beginPath();
    ctx.arc(n.x, n.y, 1.6, 0, Math.PI * 2);
    ctx.fill();
    return;
  }

  if (n.state === "open") {
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    const g = ctx.createRadialGradient(n.x, n.y, 0, n.x, n.y, r * 3.4);
    g.addColorStop(0, `rgba(${c},.34)`);
    g.addColorStop(1, `rgba(${c},0)`);
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(n.x, n.y, r * 3.4, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  nodeShape(ctx, n, r);
  if (n.state === "open") {
    ctx.fillStyle = `rgba(${c},.9)`;
    ctx.fill();
    ctx.lineWidth = 1.4;
    ctx.strokeStyle = `rgba(${c},1)`;
    ctx.stroke();
  } else if (n.state === "available") {
    ctx.fillStyle = "rgba(12,11,9,.9)";
    ctx.fill();
    ctx.lineWidth = 2;
    ctx.strokeStyle = `rgba(${c},.95)`;
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(n.x, n.y, r * 0.34, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(${c},.95)`;
    ctx.fill();
  } else {
    ctx.fillStyle = "rgba(12,11,9,.85)";
    ctx.fill();
    ctx.setLineDash([3, 3]);
    ctx.lineWidth = 1.2;
    ctx.strokeStyle = "rgba(147,128,94,.75)";
    ctx.stroke();
    ctx.setLineDash([]);
  }

  if (opts.label === false) return;
  ctx.font = "500 10px ui-monospace, Consolas, monospace";
  ctx.fillStyle =
    n.state === "open" ? `rgba(${c},.95)` : n.state === "available" ? "rgba(220,208,186,.9)" : "rgba(147,128,94,.85)";
  if (opts.below) {
    ctx.textAlign = "center";
    ctx.fillText(n.label, n.x, n.y + r + 14);
    ctx.textAlign = "left";
  } else {
    ctx.fillText(n.label, n.x + r + 6, n.y + 3.5);
  }
}

/** Связи «этот отпирает тот»: сплошная от открытого, пунктир — к запертому. */
function drawLinks(ctx: CanvasRenderingContext2D, nodes: Node[]): void {
  nodes.forEach((n) => {
    (n.to ?? []).forEach((i) => {
      const m = nodes[i];
      if (!m || m.state === "hidden") return;
      const live = n.state === "open";
      ctx.strokeStyle = live ? `rgba(${CH[n.ch].color},.42)` : "rgba(147,128,94,.28)";
      ctx.lineWidth = live ? 1.6 : 1;
      ctx.setLineDash(live ? [] : [4, 4]);
      ctx.beginPath();
      ctx.moveTo(n.x, n.y);
      ctx.lineTo(m.x, m.y);
      ctx.stroke();
      ctx.setLineDash([]);
    });
  });
}

function constellation(nodes: Node[], salt: number, title?: string): DrawFn {
  return (ctx, w, h) => {
    starfield(ctx, w, h, salt);
    drawLinks(ctx, nodes);
    nodes.forEach((n) => drawNode(ctx, n));
    if (title) {
      ctx.font = "500 12px ui-monospace, Consolas, monospace";
      ctx.fillStyle = "rgba(147,128,94,.9)";
      ctx.fillText(title, 18, 24);
    }
  };
}

/* ---------- созвездие «Поход»: всё, что берётся вехами ---------- */

const TRAIL: Node[] = [
  // 0 — корень. Пять веток расходятся сразу: Капитаны идут ДВУМЯ независимыми ветками, потому что
  // I и II открыты с самого начала (Макс, 2026-08-03). Подчинить второго первому значило бы
  // нарисовать зависимость, которой нет.
  { x: 48, y: 190, label: "первый забег", ch: "deed", state: "open", to: [1, 4, 7, 12, 15] },

  { x: 160, y: 44, label: "Капитан I", ch: "deed", state: "open", to: [2] },
  { x: 292, y: 30, label: "Экипаж I.2", ch: "deed", state: "open", to: [3] },
  { x: 424, y: 24, label: "Экипаж I.3", ch: "deed", state: "available" },

  { x: 160, y: 104, label: "Капитан II", ch: "deed", state: "open", to: [5] },
  { x: 292, y: 96, label: "Экипаж II.2", ch: "deed", state: "available", to: [6] },
  { x: 424, y: 90, label: "Экипаж II.3", ch: "deed", state: "locked" },

  { x: 160, y: 186, label: "Возвышение 1", ch: "deed", state: "open", to: [8, 9] },
  { x: 292, y: 162, label: "живой баланс", ch: "deed", state: "open" },
  { x: 424, y: 196, label: "Возвышение 2", ch: "deed", state: "available", to: [10, 11] },
  { x: 556, y: 150, label: "Капитан III", ch: "deed", state: "locked" },
  { x: 556, y: 236, label: "Великий путь", ch: "deed", state: "locked" },

  { x: 160, y: 266, label: "Подземелья", ch: "deed", state: "open", to: [13] },
  { x: 292, y: 282, label: "особые места", ch: "deed", state: "available", to: [14] },
  { x: 424, y: 300, label: "виды дорог", ch: "deed", state: "locked" },

  { x: 160, y: 348, label: "+3 Реликвии", ch: "deed", state: "open", to: [16] },
  { x: 292, y: 356, label: "ещё +3", ch: "deed", state: "available", to: [17, 18] },
  { x: 424, y: 352, label: "уники", ch: "deed", state: "locked" },
  { x: 536, y: 378, label: "", ch: "deed", state: "hidden" }
];

/* ---------- созвездие «Гильдия»: всё, что берётся золотом дома ---------- */

const HOUSE: Node[] = [
  { x: 60, y: 150, label: "хаб", ch: "vault", state: "open", to: [1, 4, 6] },
  { x: 160, y: 70, label: "ростер 10", ch: "vault", state: "open", to: [2] },
  { x: 266, y: 56, label: "ростер 12", ch: "vault", state: "available", to: [3] },
  { x: 376, y: 62, label: "ростер 16", ch: "vault", state: "locked" },
  { x: 168, y: 156, label: "реролл пула", ch: "vault", state: "open", to: [5] },
  { x: 288, y: 148, label: "бан пула", ch: "vault", state: "available" },
  { x: 158, y: 244, label: "кандидаты", ch: "vault", state: "available", to: [7, 8] },
  { x: 278, y: 224, label: "тренировка", ch: "vault", state: "locked" },
  { x: 284, y: 284, label: "старт забега", ch: "vault", state: "locked" }
];

/* ---------- созвездие «Игрок»: мелочь, косметика, ничего кроме ---------- */

const PLAYER: Node[] = [
  { x: 58, y: 150, label: "мелочь", ch: "copper", state: "open", to: [1, 3, 5] },
  { x: 156, y: 76, label: "кубик", ch: "copper", state: "open", to: [2] },
  { x: 268, y: 56, label: "ритуалы спора", ch: "copper", state: "available" },
  { x: 162, y: 158, label: "роба", ch: "copper", state: "open", to: [4] },
  { x: 280, y: 150, label: "курсор", ch: "copper", state: "available" },
  { x: 156, y: 240, label: "жесты", ch: "copper", state: "locked" }
];

/* ---------- экран хаба целиком ---------- */

/** Вписать созвездие в прямоугольник: координаты масштабируются, РАЗМЕР узла остаётся экранным.
 *  Через ctx.scale() так нельзя — вместе с координатами съёжились бы и узлы, и подписи, и полосы
 *  превратились бы в неразличимую пыль (первая версия этого стенда именно так и выглядела). */
function bandNodes(nodes: Node[], box: [number, number, number, number]): Node[] {
  const vis = nodes.filter((n) => n.state !== "hidden");
  const xs = vis.map((n) => n.x);
  const ys = vis.map((n) => n.y);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);
  const [bx, by, bw, bh] = box;
  const k = Math.min(bw / Math.max(maxX - minX, 1), bh / Math.max(maxY - minY, 1));
  return nodes.map((n) => ({
    ...n,
    x: bx + (n.x - minX) * k,
    y: by + (n.y - minY) * k
  }));
}

const drawSky: DrawFn = (ctx, w, h) => {
  starfield(ctx, w, h, 3);

  const left = 22;
  const inner = w - left - 26;
  const bands: Array<[string, string, Node[], [number, number, number, number]]> = [
    ["ПОХОД · вехи", CH.deed.color, TRAIL, [left, 56, inner, 190]],
    ["ГИЛЬДИЯ «Тихий Дом» · золото", CH.vault.color, HOUSE, [left, 316, inner * 0.72, 130]],
    ["ИГРОК · мелочь", CH.copper.color, PLAYER, [left, 496, inner * 0.58, 84]]
  ];

  bands.forEach(([title, color, nodes, box], bi) => {
    ctx.font = "500 11px ui-monospace, Consolas, monospace";
    ctx.fillStyle = `rgba(${color},.85)`;
    ctx.fillText(title, left, box[1] - 22);

    const placed = bandNodes(nodes, box);
    drawLinks(ctx, placed);
    placed.forEach((n) => drawNode(ctx, n, { r: 5.5, label: false }));

    // Разделитель — ровно посередине зазора между низом полосы и заголовком следующей.
    // Иначе он прилипает к заголовку и читается как подчёркивание, а не как граница.
    const next = bands[bi + 1];
    if (next) {
      const y = (box[1] + box[3] + (next[3][1] - 22)) / 2;
      ctx.strokeStyle = "rgba(58,44,30,.9)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(left, y);
      ctx.lineTo(w - 26, y);
      ctx.stroke();
    }
  });

  ctx.font = "400 10px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.75)";
  ctx.fillText("сверху то, что переживает всё · снизу то, что принадлежит только тебе", left, h - 16);
};

/* ---------- четыре состояния и три рамки ---------- */

const drawStates: DrawFn = (ctx, w, h) => {
  starfield(ctx, w, h, 11);
  const y = h / 2 - 16;
  const xs = [w * 0.16, w * 0.38, w * 0.62, w * 0.86];
  const list: Node[] = [
    { x: xs[0] ?? 0, y, label: "открыт", ch: "deed", state: "open" },
    { x: xs[1] ?? 0, y, label: "доступен", ch: "deed", state: "available" },
    { x: xs[2] ?? 0, y, label: "закрыт", ch: "deed", state: "locked" },
    { x: xs[3] ?? 0, y, label: "скрыт", ch: "deed", state: "hidden" }
  ];
  list.forEach((n) => drawNode(ctx, n, { r: 13, below: true }));

  ctx.font = "400 10px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.8)";
  ctx.textAlign = "center";
  const cap = ["взято", "есть чем платить", "условие видно", "места не видно"];
  cap.forEach((t, i) => ctx.fillText(t, xs[i] ?? 0, y + 46));
  ctx.textAlign = "left";
};

const drawChannels: DrawFn = (ctx, w, h) => {
  starfield(ctx, w, h, 21);
  const y = h / 2 - 12;
  const xs = [w * 0.2, w * 0.5, w * 0.8];
  const list: Node[] = [
    { x: xs[0] ?? 0, y, label: "золото", ch: "vault", state: "open" },
    { x: xs[1] ?? 0, y, label: "веха", ch: "deed", state: "open" },
    { x: xs[2] ?? 0, y, label: "мелочь", ch: "copper", state: "open" }
  ];
  list.forEach((n) => drawNode(ctx, n, { r: 14, below: true }));

  ctx.font = "400 10px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.8)";
  ctx.textAlign = "center";
  ["опции дома", "весь контент", "косметика игрока"].forEach((t, i) =>
    ctx.fillText(t, xs[i] ?? 0, y + 48)
  );
  ctx.textAlign = "left";
};

/* ---------- отклонённое: дерево очков ---------- */

const drawPointTree: DrawFn = (ctx, w, h) => {
  const cx = w / 2;
  const rows = [1, 2, 4, 6];
  const pts: Array<[number, number]> = [];
  rows.forEach((n, r) => {
    const y = 52 + r * ((h - 110) / (rows.length - 1));
    for (let i = 0; i < n; i++) {
      const x = cx + (i - (n - 1) / 2) * (w / (n + 2.2));
      pts.push([x, y]);
    }
  });

  ctx.strokeStyle = "rgba(147,128,94,.3)";
  ctx.lineWidth = 1;
  let from = 0;
  rows.forEach((n, r) => {
    if (r === rows.length - 1) return;
    const next = from + n;
    for (let i = 0; i < n; i++)
      for (let j = 0; j < (rows[r + 1] ?? 0); j++) {
        const a = pts[from + i];
        const b = pts[next + j];
        if (!a || !b || Math.abs(a[0] - b[0]) > w / 3) continue;
        ctx.beginPath();
        ctx.moveTo(a[0], a[1]);
        ctx.lineTo(b[0], b[1]);
        ctx.stroke();
      }
    from = next;
  });

  pts.forEach(([x, y], i) => {
    const taken = i < 4;
    ctx.beginPath();
    ctx.arc(x, y, 6.5, 0, Math.PI * 2);
    ctx.fillStyle = taken ? "rgba(255,204,51,.75)" : "rgba(12,11,9,.9)";
    ctx.fill();
    ctx.lineWidth = 1.3;
    ctx.strokeStyle = taken ? COL.honey : "rgba(147,128,94,.6)";
    ctx.stroke();
  });

  ctx.font = "500 12px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(255,96,80,.9)";
  ctx.fillText("осталось очков: 2", 20, h - 18);
};

/* ---------- расписание демо лентой ---------- */

const drawSchedule: DrawFn = (ctx, w, h) => {
  starfield(ctx, w, h, 31);
  // Поля широкие с обеих сторон: подписи центрируются по метке, поэтому крайние вылезают за кадр
  // ровно на половину своей ширины — «Великий путь» у правого края это и показал.
  const left = 72;
  const right = w - 78;
  const y = h * 0.5;

  ctx.strokeStyle = "rgba(147,128,94,.45)";
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  ctx.moveTo(left, y);
  ctx.lineTo(right, y);
  ctx.stroke();

  const marks: Array<[string, string[], number]> = [
    ["1-я победа", ["имя боя"], 0],
    ["забег 1", ["Экипаж", "книга"], 1],
    ["забег 2", ["место", "+пул"], 2],
    ["победа В0", ["В1", "Экипаж", "подземелья"], 3],
    ["забег на В1", ["живой баланс", "+фичи"], 4],
    ["победа В1", ["В2", "Капитан III", "ориентир"], 5],
    ["победа В2", ["В3", "Великий путь", "место"], 6]
  ];

  marks.forEach(([title, items, i]) => {
    const x = left + ((right - left) * i) / (marks.length - 1);
    const up = i % 2 === 0;

    ctx.beginPath();
    ctx.arc(x, y, 5, 0, Math.PI * 2);
    ctx.fillStyle = `rgba(${CH.deed.color},.85)`;
    ctx.fill();

    ctx.strokeStyle = "rgba(77,242,255,.3)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, up ? y - 26 : y + 26);
    ctx.stroke();

    ctx.textAlign = "center";
    ctx.font = "500 10px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(220,208,186,.9)";
    ctx.fillText(title, x, up ? y - 34 : y + 40);

    ctx.font = "400 9px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    items.forEach((t, k) => {
      const ty = up ? y - 48 - k * 12 : y + 54 + k * 12;
      ctx.fillText(t, x, ty);
    });
    ctx.textAlign = "left";
  });
};

/* ---------- стенды ---------- */

const skyStand: StandDef = {
  id: "sky",
  status: "waiting",
  title: "Экран меты в хабе целиком",
  tag: "как это видно игроку",
  note:
    "Три созвездия на одном небе, разделённые полосами. Сверху — <b>Поход</b> (вехи, весь контент), " +
    "посередине — <b>Гильдия</b> с именем активного дома, снизу — <b>Игрок</b> (мелочь). Порядок не " +
    "произволен: сверху то, что переживает всё, снизу — то, что принадлежит только тебе.",
  facts: [
    ["полос", "три, по каналу оплаты"],
    ["дом", "подписан именем: покупки принадлежат ему, не профилю"],
    ["очков", "нет вовсе"]
  ],
  verdict:
    "Домовая полоса подписана именем не для красоты: заведёшь вторую гильдию — её покупки обнулятся, а вехи останутся. Без подписи экран соврёт в этот момент.",
  size: [700, 640],
  draw: drawSky
};

const section: SectionDef = {
  id: "meta-unlocks",
  title: "Мета: открытия и созвездия",
  lede:
    "Что копится между забегами: восемь осей, три канала оплаты и расписание демо. Плюс подача — " +
    "как полтора десятка открытий ложатся на один экран в хабе.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "model",
      title: "Модель",
      lede:
        "Мета у нас растёт в ШИРИНУ: открывает опции, но не поднимает мощь (2026-07-15/31). " +
        "Поэтому очков и трат, исключающих друг друга, здесь нет — узел либо открыт, либо ещё нет."
    },
    {
      kind: "table",
      head: ["Ось", "Что открывается", "Канал"],
      rows: [
        ["Капитаны", "персоны: Полководец · Торговец · Мистик · Маг", "веха"],
        ["Экипажи", "по три на Капитана: другой угол темы, не апгрейд", "веха"],
        ["Пул Приказов", "ширина пула фич Капитана; глубину даёт забег", "веха"],
        ["Карта", "Подземелья · Великий путь · особые места · виды дорог", "веха"],
        ["Ширина пула Реликвий", "часть Реликвий закрыта и открывается игрой", "веха"],
        ["Информация на экранах", "ориентир сложности, состав врага, счётчики", "веха"],
        ["Профиль", "возвышения · прегены · Судьбы · компендиум", "веха"],
        ["Дом", "слоты 8→64 · рероллы и баны пула · кандидаты · старт забега", "золото гильдии"],
        ["Игрок", "кубик, ритуалы спора, роба, курсор, жесты", "мелочь"],
        ["Живой баланс", "модификаторы забега; спрятан до Возвышения 1", "веха"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Каналов три, и они не смешиваются.</b> Золото гильдии покупает <i>опции дома</i>, веха берёт " +
        "<i>весь контент</i>, мелочь — <i>косметику игрока</i>. Граница, которую легко потерять: " +
        "ритуал разрешения спора — косметика (мелочь), а мини-игра-событие забега — контент (веха). " +
        "Слово «мини-игра» одно, каналы разные (2026-08-03)."
    },
    {
      kind: "head",
      id: "screen",
      title: "Как это видно в хабе",
      lede: "Заявка Макса 2026-08-03: мета должна быть видна красиво и на своём экране, с зависимостями «что кого открывает»."
    },
    { kind: "split", items: [skyStand] },
    {
      kind: "legend",
      items: [
        { color: "#FFCC33", text: "золото гильдии — квадрат" },
        { color: "#4DF2FF", text: "веха — круг" },
        { color: "#8CFFA6", text: "мелочь — шип" }
      ]
    },
    {
      kind: "stands",
      items: [
        {
          id: "states",
          status: "waiting",
          title: "Четыре состояния узла",
          note:
            "Закрытый показывает <b>силуэт и условие</b>, а не «???»: «Капитан Морн, Ситцевый — победи " +
            "Гоблинской Бандой». В демо это же правило работает витриной полной версии бесплатно.",
          facts: [["скрытый", "мега-уники: места на небе не видно"]],
          verdict: "Скрытое состояние заведено под уники вроде Совершенства: силуэт тратит сюрприз до встречи.",
          size: [420, 250],
          draw: drawStates
        },
        {
          id: "channels",
          status: "waiting",
          title: "Канал читается формой",
          note:
            "Цвет съедают и тёмный фон, и дальтонизм, поэтому канал несёт <b>форма рамки</b>. Приём " +
            "достижений Minecraft: task — квадрат, goal — круг, challenge — шип.",
          facts: [["квадрат", "золото"], ["круг", "веха"], ["шип", "мелочь"]],
          verdict: "Одного взгляда хватает, чтобы понять, чем за узел платят.",
          size: [420, 250],
          draw: drawChannels
        },
        {
          id: "point-tree",
          status: "rejected",
          title: "Дерево очков (PoE)",
          note:
            "Узел стоит очко из конечного запаса, поэтому дерево обещает «или-или» и учит копить, " +
            "боясь потратить не туда.",
          facts: [["у нас", "мета не отнимает"], ["следствие", "счётчика очков нет вовсе"]],
          verdict: "Соврало бы про характер системы до первого клика: взяв одно, игрок у нас не теряет другое.",
          size: [420, 250],
          draw: drawPointTree
        }
      ]
    },
    {
      kind: "head",
      id: "constellations",
      title: "Созвездия по каналам",
      lede: "Те же узлы крупно. Сплошная линия идёт от открытого узла, пунктир ведёт к запертому."
    },
    {
      kind: "split",
      items: [
        {
          id: "trail",
          status: "waiting",
          title: "Поход — всё, что берётся вехами",
          note:
            "Пять веток от корня: Капитаны с экипажами, карта, пул Реликвий, возвышения с живым " +
            "балансом. Состояние узлов показано на середине демо.",
          facts: [
            ["Капитаны", "I и II открыты сразу, III — за победу на В1"],
            ["Экипажи", "№2 за забег этим Капитаном, №3 за победу"],
            ["живой баланс", "не раньше Возвышения 1"]
          ],
          verdict: "Ветки чередуются намеренно: две вехи подряд не открывают одну ось, иначе мета читается как «ещё один Капитан каждый раз».",
          size: [720, 420],
          draw: constellation(TRAIL, 7)
        }
      ]
    },
    {
      kind: "stands",
      items: [
        {
          id: "house",
          status: "waiting",
          title: "Гильдия — золото дома",
          note:
            "Идёт <b>параллельно расписанию вех</b>: валюта капает за забег, темп задают цены. " +
            "Привязать её к вехам нельзя — два расписания начнут спорить за один момент награды.",
          facts: [["в демо", "ростер 8→12, рероллы, кандидаты"]],
          verdict: "Единственная полка, которая принадлежит дому, а не профилю.",
          size: [440, 320],
          draw: constellation(HOUSE, 13)
        },
        {
          id: "player",
          status: "waiting",
          title: "Игрок — мелочь",
          note:
            "Косметика, которую видят остальные: кубик и ритуалы спора, роба аватара, курсор, жесты. " +
            "Силы не покупает никогда — ни прямо, ни через удобство.",
          facts: [["ритуалы", "вариации арбитра — тоже косметика"]],
          verdict: "Полка живёт в профиле игрока и переезжает между домами вместе с ним.",
          size: [440, 320],
          draw: constellation(PLAYER, 17)
        }
      ]
    },
    {
      kind: "head",
      id: "all",
      title: "Все открытия, как я их вижу",
      lede:
        "Полный объём: демо берёт из этого списка полтора десятка, остальное стоит на небе закрытым " +
        "и работает витриной. Числа и условия — предложение, вердиктов нет."
    },
    {
      kind: "table",
      head: ["Ось", "Узлы", "Чем открывается"],
      rows: [
        ["Капитаны", "I Полководец · II Торговец (оба сразу) · III Мистик · IV Маг", "III — победа на В1; IV — за демо"],
        ["Экипажи", "по три на Капитана: №1 сразу, №2, №3", "№2 — забег этим Капитаном, №3 — победа им"],
        ["Пул Приказов", "стартовые 4 фичи → до 6–8 в пуле", "по фиче за победу этим Капитаном"],
        ["Карта: вставки", "Подземелья · Великий путь", "победа на В0 · победа на В2"],
        ["Карта: особые места", "Хранилище · Алтарь · Лабиринт · Штольня · Сокрытое логово", "по одному за завершённый забег, в демо два"],
        ["Карта: дороги", "тракт и тропа · привратник · заслон · вслепую", "за демо, витрина"],
        ["Пул Реликвий", "стартовые 12 из 21 → +3 → +3 → +3", "за завершённые забеги"],
        ["Пул Реликвий: уники", "Совершенство · Стиллер заклинаний · прочие мега-уники", "скрыты полностью, условия позже"],
        ["Информация", "ориентир сложности · состав врага в тултипе · счётчик побед над боем", "победа на В1 и дальше"],
        ["Профиль: возвышения", "В1 · В2 · В3 (в демо до четырёх)", "победа на предыдущем"],
        ["Профиль: прегены", "2–3 именных «Сосуда»", "вехи по контенту"],
        ["Профиль: Судьбы", "2–3 квеста уровня «Сосуда»", "вехи по контенту"],
        ["Профиль: знание", "компендиум · «Из мастерской» · имена боёв · системный язык", "имя боя — после 1 победы в демо"],
        ["Дом", "ростер 10 · 12 · реролл пула · бан пула · кандидаты · тренировка · старт забега", "золото гильдии, вне расписания"],
        ["Игрок", "скины кубика · ритуалы спора · роба · курсор · подпись ника · жесты", "мелочь, пари и Зароки"],
        ["Живой баланс", "The Lovers · погода акта · перекос состава врага · баны", "после Возвышения 1"]
      ]
    },
    {
      kind: "head",
      id: "schedule",
      title: "Расписание демо",
      lede:
        "Один акт, 3–4 возвышения, порядка шести завершённых забегов, пятнадцать открытий. " +
        "Висит на вехах, а не на номерах: номер мы не контролируем, игрок может проиграть трижды подряд."
    },
    {
      kind: "split",
      items: [
        {
          id: "timeline",
          status: "waiting",
          title: "Что падает и когда",
          note:
            "Бюджет — <b>2–3 открытия на завершённый забег</b>, к концу демо 1–2. Меньше — мета молчит, " +
            "больше — игрок перестаёт замечать отдельное открытие.",
          facts: [
            ["первое", "внутри первого забега, после 1-й победы"],
            ["поражение", "тоже открывает: минимум один узел"],
            ["итого", "15 открытий за ~6 забегов"]
          ],
          verdict:
            "Проигранный забег обязан открывать: иначе застрявший новичок получает молчащую систему ровно тогда, когда ему нужнее всего причина остаться.",
          size: [720, 320],
          draw: drawSchedule
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто и ждёт вердикта:</b> сколько Реликвий в стартовом пуле из 21 · какое достижение " +
        "открывает Экипаж №3 (предложено «победа этим Капитаном») · цены дома и сколько золота падает " +
        "за забег · какие два особых места заходят в демо · где физически живёт экран — стол мастера " +
        "или отдельная дверь хаба · насколько дефолтный ориентир сложности слабее Приказов Мистика, " +
        "чтобы мета не обесценила фичу Капитана."
    }
  ]
};

export default section;
