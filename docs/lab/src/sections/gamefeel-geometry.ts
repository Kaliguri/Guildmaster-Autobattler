/* Геометрия удара: чем меряется форма, откуда берутся её длина и толщина, и что показывает замер
   ЖИВЫХ ассетов — боевого вида UnitView_BoneStorybook и клипа Attack.

   Раздел существует потому, что числа формы живут в трёх местах сразу: доли роста в feel-конфиге,
   рост и якоря в префабе, путь клинка в клипе. Порознь каждое выглядит разумным, а вместе дают
   серп в три роста юнита — и увидеть это можно только сведя их в один масштаб. */

import { drawFeedState, fetchHitGeometry, type HitFormArchetype, type HitGeometryDump } from "../api.js";
import type { DrawFn, SectionDef } from "../types.js";

const feed = fetchHitGeometry();

/** Урон-образец: круглая сотня, о которой и был вопрос. */
const SAMPLE_DAMAGE = 100;

/* ---------- расчёт: ровно тот же порядок, что в HitFormFactory.Build ---------- */

/** Вес удара 0…1 → множитель размера. Кривая одна на все архетипы, потолок обязателен. */
function weightOf(d: HitGeometryDump, damage: number): number {
  const frac = damage / d.combat.baseHp;
  const t = Math.min(1, frac / d.form.heavyHitFrac);
  return d.form.sizeMin + (d.form.sizeMax - d.form.sizeMin) * t;
}

/** Дистанция между ЦЕНТРАМИ при контакте: ступень дальности плюс оба радиуса тел. */
function contactDistance(d: HitGeometryDump): number {
  // Ступень Melee — первая в массиве; дампа без неё не бывает, но пустой массив уронил бы сцену молча.
  const melee = d.combat.attackRangeBands[0] ?? 1;
  return melee + d.unit.bodyRadiusPerSize * 2;
}

/** Точка A — кончик клинка на кадре StrikeStart, в координатах бьющего. */
function pointA(d: HitGeometryDump): [number, number] {
  return d.swing.tipAtStrikeStart;
}

/** Точка B — Hit Point цели: она стоит на дистанции контакта справа, высота якоря её же. */
function pointB(d: HitGeometryDump): [number, number] {
  return [contactDistance(d), d.unit.hitPoint];
}

function reach(d: HitGeometryDump): number {
  const a = pointA(d), b = pointB(d);
  return Math.hypot(b[0] - a[0], b[1] - a[1]);
}

/** Длина архетипа по его собственным числам, без правила «не короче пути». */
function archLength(d: HitGeometryDump, a: HitFormArchetype, damage: number): number {
  return a.lengthH * d.form.unitHeight * weightOf(d, damage);
}

/** Толщина (не полу-) в мировых единицах: минимум и максимум коридора архетипа. */
function thickness(d: HitGeometryDump, a: HitFormArchetype, damage: number): [number, number] {
  const k = 2 * d.form.unitHeight * weightOf(d, damage);
  return [a.halfThicknessH[0] * k, a.halfThicknessH[1] * k];
}

const fmt = (v: number, digits = 2): string => v.toFixed(digits);

/* ---------- общая рисовалка сцены в мировом масштабе ---------- */

interface View {
  ctx: CanvasRenderingContext2D;
  /** Мировые → экранные. */
  px: (wx: number, wy: number) => [number, number];
  scale: number;
}

/** Кадр в МИРОВЫХ единицах: сцена задаётся тем, что должно поместиться, а не подбором пикселей. */
function view(ctx: CanvasRenderingContext2D, w: number, h: number,
              minX: number, maxX: number, minY: number, maxY: number): View {
  const pad = 26;
  const scale = Math.min((w - pad * 2) / (maxX - minX), (h - pad * 2) / (maxY - minY));
  const ox = pad + (w - pad * 2 - (maxX - minX) * scale) / 2;
  const oy = pad + (h - pad * 2 - (maxY - minY) * scale) / 2;
  // Y мира растёт вверх, Y канваса — вниз.
  const px = (wx: number, wy: number): [number, number] =>
    [ox + (wx - minX) * scale, oy + (maxY - wy) * scale];
  return { ctx, px, scale };
}

function line(v: View, ax: number, ay: number, bx: number, by: number, colour: string, width = 1.5, dash?: number[]): void {
  const { ctx } = v;
  ctx.save();
  ctx.strokeStyle = colour;
  ctx.lineWidth = width;
  if (dash) ctx.setLineDash(dash);
  const a = v.px(ax, ay), b = v.px(bx, by);
  ctx.beginPath();
  ctx.moveTo(a[0], a[1]);
  ctx.lineTo(b[0], b[1]);
  ctx.stroke();
  ctx.restore();
}

function dot(v: View, x: number, y: number, colour: string, r = 4): void {
  const p = v.px(x, y);
  v.ctx.fillStyle = colour;
  v.ctx.beginPath();
  v.ctx.arc(p[0], p[1], r, 0, Math.PI * 2);
  v.ctx.fill();
}

function label(v: View, x: number, y: number, text: string, colour: string, dx = 8, dy = -8): void {
  const p = v.px(x, y);
  v.ctx.font = "500 11px ui-monospace, Consolas, monospace";
  v.ctx.fillStyle = colour;
  v.ctx.fillText(text, p[0] + dx, p[1] + dy);
}

/** Силуэт юнита по замеренным габаритам: не рисунок, а рамка того, что рисует движок. */
function silhouette(v: View, d: HitGeometryDump, offsetX: number, colour: string): void {
  const { ctx } = v;
  const a = v.px(d.unit.bodyMinX + offsetX, d.unit.bodyMaxY);
  const b = v.px(d.unit.bodyMaxX + offsetX, d.unit.bodyMinY);
  ctx.save();
  ctx.fillStyle = colour;
  ctx.fillRect(a[0], a[1], b[0] - a[0], b[1] - a[1]);
  ctx.restore();
}

const INK = "rgba(232,222,199,.9)";
const DIM = "rgba(147,128,94,.75)";
const HOT = "rgba(255,138,76,.95)";
const COOL = "rgba(120,196,255,.95)";
const GRID = "rgba(147,128,94,.28)";

/** Земля и вертикаль корня: без них мировые координаты не читаются. */
function axes(v: View, d: HitGeometryDump, offsetX = 0): void {
  line(v, -1.4 + offsetX, 0, 3.2 + offsetX, 0, GRID, 1, [4, 4]);
  line(v, offsetX, d.unit.bodyMinY - 0.15, offsetX, d.unit.bodyMaxY + 0.15, GRID, 1, [4, 4]);
}

/* ---------- стенды ---------- */

const drawAnchors: DrawFn = (ctx, w, h) => {
  const d = feed.data;
  if (!d || drawFeedState(ctx, w, h, feed, "геометрия удара")) return;

  const v = view(ctx, w, h, -1.0, 1.6, d.unit.bodyMinY - 0.2, d.unit.bodyMaxY + 0.2);
  silhouette(v, d, 0, "rgba(232,222,199,.10)");
  axes(v, d);

  dot(v, 0, d.unit.headPoint, COOL);   label(v, 0, d.unit.headPoint, `Head ${fmt(d.unit.headPoint)}`, COOL);
  dot(v, 0, d.unit.hitPoint, HOT);     label(v, 0, d.unit.hitPoint, `Hit ${fmt(d.unit.hitPoint)}`, HOT);
  dot(v, 0, d.unit.feetPoint, COOL);   label(v, 0, d.unit.feetPoint, `Feet ${fmt(d.unit.feetPoint)}`, COOL);
  dot(v, d.unit.shotPoint[0], d.unit.shotPoint[1], DIM);
  label(v, d.unit.shotPoint[0], d.unit.shotPoint[1], "Shot", DIM);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = DIM;
  ctx.fillText(`тело ${fmt(d.unit.bodyMinY)} … ${fmt(d.unit.bodyMaxY)}`, 22, h - 22);
};

const drawSwingPath: DrawFn = (ctx, w, h) => {
  const d = feed.data;
  if (!d || drawFeedState(ctx, w, h, feed, "геометрия удара")) return;

  const dist = contactDistance(d);
  const v = view(ctx, w, h, -1.1, dist + 1.2, -1.3, 1.7);
  silhouette(v, d, 0, "rgba(232,222,199,.08)");
  silhouette(v, d, dist, "rgba(255,138,76,.08)");
  axes(v, d);
  axes(v, d, dist);

  // Путь кончика клинка — реальные сэмплы клипа между StrikeStart и StrikeEnd.
  ctx.save();
  ctx.strokeStyle = "rgba(232,222,199,.55)";
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  d.swing.arc.forEach((p, i) => {
    const s = v.px(p[0], p[1]);
    if (i === 0) ctx.moveTo(s[0], s[1]); else ctx.lineTo(s[0], s[1]);
  });
  ctx.stroke();
  ctx.restore();

  const a = pointA(d), b = pointB(d);
  dot(v, a[0], a[1], COOL, 5);        label(v, a[0], a[1], "A · начало замаха", COOL, -6, -12);
  dot(v, b[0], b[1], HOT, 5);         label(v, b[0], b[1], "B · Hit Point цели", HOT);
  dot(v, d.swing.tipAtHit[0], d.swing.tipAtHit[1], DIM, 4);
  label(v, d.swing.tipAtHit[0], d.swing.tipAtHit[1], "клинок на кадре Hit", DIM, -10, 16);
  line(v, a[0], a[1], b[0], b[1], "rgba(255,138,76,.5)", 1.2, [5, 4]);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = DIM;
  ctx.fillText(`центры на ${fmt(dist)} · |AB| = ${fmt(reach(d))} · клинок ${fmt(d.swing.bladeLength)}`, 22, h - 22);
};

/** Форма как её сейчас ставит HitFormVfx: центр в точке хита, длина в обе стороны. */
function drawForm(v: View, cx: number, cy: number, angle: number, length: number, thick: number, colour: string): void {
  const { ctx } = v;
  const c = v.px(cx, cy);
  ctx.save();
  ctx.translate(c[0], c[1]);
  ctx.rotate(-angle);
  ctx.fillStyle = colour;
  ctx.beginPath();
  ctx.ellipse(0, 0, (length * v.scale) / 2, Math.max(1.5, (thick * v.scale) / 2), 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

const drawFormNow: DrawFn = (ctx, w, h) => {
  const d = feed.data;
  if (!d || drawFeedState(ctx, w, h, feed, "геометрия удара")) return;

  const a = pointA(d), b = pointB(d);
  const dist = contactDistance(d);
  const len = reach(d) * 2;                     // правило «не короче пути», форма навылет
  const angle = Math.atan2(b[1] - a[1], b[0] - a[0]);
  const thick = thickness(d, d.form.archetypes.slash, SAMPLE_DAMAGE)[1];

  const half = len / 2;
  const v = view(ctx, w, h, b[0] - half - 0.4, b[0] + half + 0.4, -1.6, 1.9);
  silhouette(v, d, 0, "rgba(232,222,199,.08)");
  silhouette(v, d, dist, "rgba(255,138,76,.08)");

  drawForm(v, b[0], b[1], angle, len, thick, "rgba(255,138,76,.35)");
  dot(v, a[0], a[1], COOL, 5);  label(v, a[0], a[1], "A", COOL, -4, -10);
  dot(v, b[0], b[1], HOT, 5);   label(v, b[0], b[1], "B", HOT);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = DIM;
  ctx.fillText(`длина ${fmt(len)} = 2 × |AB| · за целью ${fmt(half)}`, 22, h - 22);
};

const drawFormCentred: DrawFn = (ctx, w, h) => {
  const d = feed.data;
  if (!d || drawFeedState(ctx, w, h, feed, "геометрия удара")) return;

  const a = pointA(d), b = pointB(d);
  const dist = contactDistance(d);
  const overshoot = 0.25;                        // сколько формы выходит за цель, в долях |AB|
  const len = reach(d) * (1 + overshoot);
  const angle = Math.atan2(b[1] - a[1], b[0] - a[0]);
  const thick = thickness(d, d.form.archetypes.slash, SAMPLE_DAMAGE)[1];

  // Центр — середина пути, сдвинутая на выход за цель: форма покрывает A→B и чуть дальше.
  const cx = (a[0] + b[0]) / 2 + Math.cos(angle) * (reach(d) * overshoot) / 2;
  const cy = (a[1] + b[1]) / 2 + Math.sin(angle) * (reach(d) * overshoot) / 2;

  const v = view(ctx, w, h, a[0] - 0.5, b[0] + len * 0.5, -1.6, 1.9);
  silhouette(v, d, 0, "rgba(232,222,199,.08)");
  silhouette(v, d, dist, "rgba(255,138,76,.08)");

  drawForm(v, cx, cy, angle, len, thick, "rgba(120,196,255,.32)");
  dot(v, a[0], a[1], COOL, 5);  label(v, a[0], a[1], "A", COOL, -4, -10);
  dot(v, b[0], b[1], HOT, 5);   label(v, b[0], b[1], "B", HOT);

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = DIM;
  ctx.fillText(`длина ${fmt(len)} = |AB| + 25% · за целью ${fmt(reach(d) * overshoot)}`, 22, h - 22);
};

/* ---------- таблица чисел ---------- */

function renderNumbers(host: HTMLElement): void {
  void feed.settled.then(() => {
    const d = feed.data;
    if (!d) {
      host.textContent = "Данных нет — подними ./scripts/lab-serve.ps1";
      return;
    }

    const w = weightOf(d, SAMPLE_DAMAGE);
    const frac = SAMPLE_DAMAGE / d.combat.baseHp;
    const names: Array<[keyof HitGeometryDump["form"]["archetypes"], string]> = [
      ["slash", "Slash · режущий"],
      ["pierce", "Pierce · колющий"],
      ["blunt", "Blunt · дробящий"],
      ["bolt", "Bolt · всполох выстрела"]
    ];

    const rows = names.map(([key, title]) => {
      const arch = d.form.archetypes[key];
      const len = archLength(d, arch, SAMPLE_DAMAGE);
      const [tMin, tMax] = thickness(d, arch, SAMPLE_DAMAGE);
      const minReach = key === "slash" ? Math.max(len, reach(d) * 2) : len;
      return `<tr><td>${title}</td><td>${fmt(arch.lengthH)}</td><td>${fmt(len)}</td>` +
             `<td>${key === "slash" ? `<b>${fmt(minReach)}</b>` : "—"}</td>` +
             `<td>${fmt(tMin)} … ${fmt(tMax)}</td>` +
             `<td>${fmt(len / d.form.unitHeight, 2)}</td></tr>`;
    }).join("");

    host.innerHTML =
      `<p class="lede">Урон ${SAMPLE_DAMAGE} по цели с ${d.combat.baseHp} HP: доля ` +
      `${(frac * 100).toFixed(2)}% при пороге тяжёлого ${(d.form.heavyHitFrac * 100).toFixed(0)}% → ` +
      `множитель размера <b>${fmt(w, 3)}</b> из коридора ${fmt(d.form.sizeMin)}…${fmt(d.form.sizeMax)}. ` +
      `Мера H = ${fmt(d.form.unitHeight)}.</p>` +
      `<table><thead><tr><th>Архетип</th><th>в долях H</th><th>длина, ед.</th>` +
      `<th>с правилом пути</th><th>толщина, ед.</th><th>в ростах юнита</th></tr></thead>` +
      `<tbody>${rows}</tbody></table>` +
      `<p class="note">Снято ${d.source.snapped} с ${d.source.prefab} и клипа ${d.source.clip}.</p>`;
  });
}

const section: SectionDef = {
  id: "geometry",
  title: "Геометрия удара",
  lede:
    "Чем меряется форма удара, откуда берутся её длина и толщина и почему коса выходит больше, чем " +
    "просят её собственные числа. Всё на этой странице — замер живых ассетов, а не реконструкция: " +
    "якоря и габариты сняты с боевого вида, путь клинка — сэмплированием настоящего клипа атаки.",
  transport: false,
  blocks: [
    {
      kind: "head", id: "chain", title: "Цепочка расчёта",
      lede: "Тот же порядок, что в HitFormFactory.Build — шаг за шагом."
    },
    {
      kind: "text",
      html:
        "<b>1. Архетип.</b> Тип урона автоатаки решает форму: Slash, Pierce, Blunt; у дальнего — Bolt " +
        "независимо от типа. Стойка может перекрасить удар в стихию, но не меняет способ доставки.<br>" +
        "<b>2. Вес удара.</b> <code>frac = урон / MaxHP цели</code>, затем " +
        "<code>weight = lerp(sizeMin, sizeMax, frac / heavyHitFrac)</code> с потолком.<br>" +
        "<b>3. Длина.</b> <code>lengthH × H × weight</code>, где H — рост юнита-человека в мировых " +
        "единицах. У режущего сверх того действует правило пути: форма не короче отрезка от начала " +
        "замаха до точки хита, а поскольку она центрирована в точке хита и уходит в обе стороны — " +
        "не короче ДВУХ таких отрезков.<br>" +
        "<b>4. Толщина и прогиб</b> — те же доли H, помноженные на тот же weight; внутри коридора " +
        "архетипа выбор делает сид удара, поэтому соседние удары не выглядят штампом.<br>" +
        "<b>5. Жизнь</b> — общая для всех архетипов, плюс окно hitstop, на котором форма стоит замороженной."
    },
    { kind: "live", id: "numbers", render: renderNumbers },

    {
      kind: "head", id: "anchors", title: "Юнит и его якоря",
      lede:
        "Форма строится не по телу, а по четырём точкам-якорям. Hit Point принимает точку B, искры и " +
        "боевые цифры; Feet Point — пыль под ногами; Head Point держит надголовный интерфейс."
    },
    {
      kind: "stands",
      items: [
        {
          id: "unit-anchors", status: "note", title: "Якоря против нарисованного тела", size: [480, 330],
          note: "Серым — габариты того, что рисует движок (замер рендереров). Точками — якоря.",
          draw: drawAnchors
        }
      ]
    },

    {
      kind: "head", id: "path", title: "Путь клинка",
      lede:
        "Кончик оружия за окно взмаха, снятый сэмплированием клипа Attack между маркерами StrikeStart " +
        "и StrikeEnd. Цель стоит на дистанции контакта: ступень ближнего боя плюс два радиуса тел."
    },
    {
      kind: "split",
      items: [
        {
          id: "swing-path", status: "note", title: "Дуга взмаха и точки A → B", size: [740, 420],
          note:
            "A снимается на кадре StrikeStart — клинок в этот момент отведён назад и вверх. B — Hit " +
            "Point цели. Пунктир между ними и есть «путь», которым меряется коса.",
          draw: drawSwingPath
        }
      ]
    },

    {
      kind: "head", id: "form", title: "Отчего коса выходит больше своих чисел",
      lede:
        "Правило «не короче пути клинка» удваивается геометрией: форма навылет центрирована в точке " +
        "хита, поэтому до A дотягивается только её половина."
    },
    {
      kind: "split",
      items: [
        {
          id: "form-now", status: "note", tag: "как сейчас", title: "Центр в точке хита", size: [740, 420],
          note: "Половина формы уходит ЗА цель — там, где клинок не был и не будет.",
          draw: drawFormNow
        },
        {
          id: "form-centred", status: "waiting", tag: "предложение", title: "Центр на пути", size: [740, 420],
          note:
            "Форма покрывает отрезок A→B и выходит за цель на четверть пути. Длина падает почти вдвое " +
            "при том же покрытии замаха.",
          verdict:
            "Требует правки HitFormVfx.Apply: сейчас ветка «навылет» ставит центр ровно в точку хита. " +
            "Вердикта нет — решать Максу.",
          draw: drawFormCentred
        }
      ]
    }
  ]
};

export default section;
