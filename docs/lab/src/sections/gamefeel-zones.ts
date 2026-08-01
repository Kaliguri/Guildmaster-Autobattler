/* Зоны: круг, линия и то, что стоит на арене.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Зоны. */

import { tick } from "../clock.js";
import { bubblesUp, ground, statusBody } from "../draw.js";
import type { DrawFn, SectionDef } from "../types.js";

/** Зона лежит в ПЛОСКОСТИ ЗЕМЛИ, поэтому круг рисуется эллипсом с той же сплюснутостью, что тень
 *  юнита и купол барьера. Один множитель на всё — иначе зона читается как сфера в воздухе. */
const GROUND_SQUASH = 0.42;

const COLOR = {
  frost: "138,206,255",
  steel: "196,206,214",
  spore: "132,214,92",
  fire: "255,146,48"
} as const;

/** Контур зоны. `fill` — доля подводки (0 только контур, 1 заполнен), `flash` — вспышка
 *  срабатывания. Заливка держится на минимуме намеренно: зоны пересекаются, а аддитив слипается —
 *  две дают пятно, три кашу. Поэтому форму несёт контур. */
function zoneCircle(
  ctx: CanvasRenderingContext2D,
  cx: number, cy: number, r: number,
  color: string, fill: number, flash: number, dashed: boolean
): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";

  if (fill > 0 || flash > 0) {
    // Заполнение подводки: сектор от двенадцати часов.
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.ellipse(cx, cy, r, r * GROUND_SQUASH, 0, -Math.PI / 2,
      -Math.PI / 2 + Math.PI * 2 * Math.max(fill, flash));
    ctx.closePath();
    ctx.fillStyle = `rgba(${color},${(0.07 + 0.16 * flash).toFixed(3)})`;
    ctx.fill();
  }

  ctx.setLineDash(dashed ? [5, 5] : []);
  ctx.strokeStyle = `rgba(${color},${(0.4 + 0.55 * Math.max(fill * 0.5, flash)).toFixed(3)})`;
  ctx.lineWidth = 1.6 + 1.6 * flash;
  ctx.beginPath();
  ctx.ellipse(cx, cy, r, r * GROUND_SQUASH, 0, 0, Math.PI * 2);
  ctx.stroke();
  ctx.setLineDash([]);

  if (flash > 0) {
    // Срабатывание: кромка вспыхивает белым и чуть расходится.
    const k = 1 + 0.06 * (1 - flash);
    ctx.strokeStyle = `rgba(255,250,235,${(0.75 * flash).toFixed(3)})`;
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.ellipse(cx, cy, r * k, r * GROUND_SQUASH * k, 0, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.restore();
}

/** Линия: подводка идёт ОТ ЮНИТА к концу, а не от края к центру — у прокола есть направление. */
function zoneLine(
  ctx: CanvasRenderingContext2D,
  x0: number, y0: number, len: number, halfWidth: number,
  color: string, fill: number, flash: number
): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const hw = halfWidth * GROUND_SQUASH;
  const filled = len * Math.max(fill, flash);

  ctx.fillStyle = `rgba(${color},${(0.07 + 0.16 * flash).toFixed(3)})`;
  ctx.fillRect(x0, y0 - hw, filled, hw * 2);
  ctx.strokeStyle = `rgba(${color},${(0.4 + 0.55 * Math.max(fill * 0.5, flash)).toFixed(3)})`;
  ctx.lineWidth = 1.6 + 1.6 * flash;
  ctx.beginPath();
  ctx.rect(x0, y0 - hw, len, hw * 2);
  ctx.stroke();

  if (flash > 0) {
    ctx.strokeStyle = `rgba(255,250,235,${(0.75 * flash).toFixed(3)})`;
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.moveTo(x0 + filled, y0 - hw * 1.3);
    ctx.lineTo(x0 + filled, y0 + hw * 1.3);
    ctx.stroke();
  }
  ctx.restore();
}

/** Кто это делает: пунктир от кастующего к зоне, если центр не на нём. Без него круг «вокруг
 *  цели» выглядит бесхозным. */
function zoneOrigin(
  ctx: CanvasRenderingContext2D,
  x0: number, y0: number, cx: number, cy: number, color: string, k: number
): void {
  if (k <= 0) return;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.strokeStyle = `rgba(${color},${(0.3 * k).toFixed(3)})`;
  ctx.setLineDash([3, 5]);
  ctx.lineWidth = 1.2;
  ctx.beginPath();
  ctx.moveTo(x0, y0);
  ctx.lineTo(cx, cy);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.restore();
}

const CYCLE = 108;
const CAST = 30; // окно подводки: 1 с
const HIT = 48; // кадр срабатывания
const TRAIL = 24; // сколько живёт след

interface Phase {
  fill: number;
  flash: number;
  trail: number;
  label: string;
}

/** Фаза зоны в цикле: подводка → срабатывание → след.
 *  `instant` — каста нет, и подводке негде жить: ровно это и надо увидеть. */
function zonePhase(c: number, instant: boolean): Phase {
  const idle: Phase = { fill: 0, flash: 0, trail: 0, label: "покой" };
  const age = c - HIT;

  if (instant) {
    if (c < HIT) return idle;
    if (age < 5) return { fill: 0, flash: 1 - age / 5, trail: 0, label: "СРАБОТАЛО без подводки" };
    if (age < 5 + TRAIL) return { fill: 0, flash: 0, trail: 1 - (age - 5) / TRAIL, label: "след" };
    return idle;
  }

  if (c < CAST) return idle;
  if (c < HIT) {
    const fill = (c - CAST) / (HIT - CAST);
    return { fill, flash: 0, trail: 0, label: `подводка · ${Math.round(100 * fill)}%` };
  }
  if (age < 5) return { fill: 1, flash: 1 - age / 5, trail: 0, label: "СРАБОТАЛО" };
  if (age < 5 + TRAIL) return { fill: 0, flash: 0, trail: 1 - (age - 5) / TRAIL, label: "след" };
  return idle;
}

function zoneLabel(ctx: CanvasRenderingContext2D, h: number, text: string, hot: boolean): void {
  ctx.font = "500 12px ui-monospace, Consolas, monospace";
  ctx.fillStyle = hot ? "rgba(255,250,235,1)" : "rgba(147,128,94,.85)";
  ctx.fillText(text, 18, h - 26);
}

type ZoneKind = "self" | "target" | "line" | "standing" | "overlap" | "instant";

function drawZone(kind: ZoneKind): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 84);
    const groundY = h - 84;
    const bodyH = 108;
    const u = bodyH / 16;
    const c = tick % CYCLE;

    if (kind === "self") {
      const p = zonePhase(c, false);
      const x = w / 2;
      zoneCircle(ctx, x, groundY + u * 0.6, u * 7.5, COLOR.steel, p.fill, p.flash, false);
      if (p.trail > 0) zoneCircle(ctx, x, groundY + u * 0.6, u * 7.5, COLOR.steel, 0, p.trail * 0.25, true);
      statusBody(ctx, x, groundY, bodyH);
      zoneLabel(ctx, h, `${p.label} · вокруг себя`, p.flash > 0);
      return;
    }

    if (kind === "target") {
      const p = zonePhase(c, false);
      const caster = w * 0.25;
      const tx = w * 0.68;
      zoneCircle(ctx, tx, groundY + u * 0.6, u * 8.5, COLOR.frost, p.fill, p.flash, false);
      if (p.trail > 0) zoneCircle(ctx, tx, groundY + u * 0.6, u * 8.5, COLOR.frost, 0, p.trail * 0.25, true);
      zoneOrigin(ctx, caster, groundY - bodyH * 0.4, tx, groundY + u * 0.6, COLOR.frost,
        Math.max(p.fill, p.flash));
      statusBody(ctx, caster, groundY, bodyH * 0.92);
      statusBody(ctx, tx, groundY, bodyH * 0.92, { frostbite: p.trail * 0.5 });
      zoneLabel(ctx, h, `${p.label} · вокруг цели`, p.flash > 0);
      return;
    }

    if (kind === "line") {
      const p = zonePhase(c, false);
      const x = w * 0.22;
      zoneLine(ctx, x, groundY + u * 0.6, u * 13, u * 2.6, COLOR.steel, p.fill, p.flash);
      statusBody(ctx, x, groundY, bodyH);
      statusBody(ctx, x + u * 8, groundY, bodyH * 0.92);
      zoneLabel(ctx, h, `${p.label} · линия`, p.flash > 0);
      return;
    }

    if (kind === "standing") {
      // Стоячая зона: подводки нет, контур держится всю жизнь, тик виден НА ТЕЛЕ.
      const life = c / CYCLE;
      const pulse = c % 20 < 5 ? 1 - (c % 20) / 5 : 0;
      const x = w / 2;
      zoneCircle(ctx, x, groundY + u * 0.6, u * 8, COLOR.spore, 0.14, 0, true);
      statusBody(ctx, x, groundY, bodyH, { poison: 0.5 + 0.4 * pulse });
      if (pulse > 0) bubblesUp(ctx, x, groundY, bodyH, COLOR.spore, 0.05, 4, 2);
      zoneLabel(ctx, h,
        pulse > 0 ? "тик по стоящему внутри" : `стоит · ${Math.round((1 - life) * 100)}% жизни`,
        pulse > 0);
      return;
    }

    if (kind === "overlap") {
      const p = zonePhase(c, false);
      zoneCircle(ctx, w * 0.38, groundY + u * 0.6, u * 7, COLOR.frost, p.fill, p.flash, false);
      zoneCircle(ctx, w * 0.58, groundY + u * 1.2, u * 6, COLOR.fire, p.fill * 0.7, p.flash * 0.7, false);
      zoneCircle(ctx, w * 0.5, groundY - u * 0.8, u * 8.5, COLOR.spore, 0.12, 0, true);
      statusBody(ctx, w * 0.47, groundY, bodyH);
      zoneLabel(ctx, h, "три зоны · контуры не слиплись", p.flash > 0);
      return;
    }

    const p = zonePhase(c, true);
    const x = w / 2;
    zoneCircle(ctx, x, groundY + u * 0.6, u * 7.5, COLOR.fire, p.fill, p.flash, false);
    if (p.trail > 0) zoneCircle(ctx, x, groundY + u * 0.6, u * 7.5, COLOR.fire, 0, p.trail * 0.25, true);
    statusBody(ctx, x, groundY, bodyH);
    zoneLabel(ctx, h, p.label, p.flash > 0);
  };
}

const section: SectionDef = {
  id: "zones",
  title: "Зоны",
  eyebrow: "Лаборатория · джус · зоны",
  lede:
    "Акцент на <b>подводке</b>: контур растёт к моменту срабатывания. Игрок в автобое не уклоняется, " +
    "но растущий круг делает бой читаемым в момент, а не только в разборе — и лаг показа делает эту " +
    "подводку честной, потому что симуляция уже посчитала, а лента её держит.",

  blocks: [
    {
      kind: "head",
      id: "shapes",
      title: "Круг, линия и то, что стоит на арене",
      lede:
        "Контур несёт форму, заливка живёт на минимуме. Зоны пересекаются, а аддитивные заливки " +
        "складываются: две дают пятно, три — кашу. Зона лежит в плоскости земли — с той же " +
        "сплюснутостью, что тень юнита и купол барьера."
    },
    {
      kind: "stands",
      items: [
        {
          id: "self", status: "accepted", tag: "форма · вокруг себя", title: "Круг вокруг себя",
          note: "«Стальной вихрь» Копейщика, радиус 2.5. Подводка заполняется, срабатывание бьёт по контуру и внутрь, след гаснет.",
          draw: drawZone("self")
        },
        {
          id: "target", status: "accepted", tag: "форма · вокруг цели", title: "Круг вокруг цели",
          note: "Ледяная зона Криоманта, радиус 3.0. Центр не на кастующем — пунктир от него к зоне объясняет, кто это делает.",
          draw: drawZone("target")
        },
        {
          id: "line", status: "waiting", title: "Линия",
          note: "Форма есть в данных, носителя ждёт. Прокол сквозь ряд: подводка идёт от юнита к концу, а не от края к центру.",
          draw: drawZone("line")
        },
        {
          id: "standing", status: "waiting", title: "Стоячая зона",
          note: "Облако спор. Подводки нет — её никто не заряжает; контур держится всё время жизни, а тик виден <b>на телах</b>, не на земле.",
          draw: drawZone("standing")
        },
        {
          id: "overlap", status: "note", tag: "проверка", title: "Три зоны разом",
          note: "Проверка на кашу: контуры переживают пересечение, заливки бы слиплись. Ровно поэтому заливка почти нулевая.",
          draw: drawZone("overlap")
        },
        {
          id: "instant", status: "note", tag: "проблема", title: "Каст без cast-time",
          note: "Подводке негде жить: круг вспыхивает в кадре урона. Отсюда требование — зональному касту нужно окно подготовки.",
          draw: drawZone("instant")
        }
      ]
    },
    {
      kind: "note",
      html:
        "Два стенда здесь — не варианты, а <b>заявки</b>. «Линия» ждёт носителя: форма в данных есть, " +
        "способности с ней нет. «Каст без cast-time» — не украшение, а найденная дыра: она заведена " +
        "как <code>BAL-022</code>, потому что решается числом в данных, а не рисованием."
    }
  ]
};

export default section;
