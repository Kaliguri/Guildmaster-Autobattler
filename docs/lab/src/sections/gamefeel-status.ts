/* Статусы: постоянный слой на теле — четыре канала и шкала стака.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Статусы. */

import { tick } from "../clock.js";
import { ST, ground, hexOf, jag, miniLabel, statusBody, unitPath } from "../draw.js";
import { isOn } from "../toggles.js";
import type { DrawFn, SectionDef } from "../types.js";

const INFO_LAYER = "status-info";

/* ---------- знаки каналов ---------- */

/** Горение: языки ВВЕРХ от контура. Вектор и есть сообщение — вверх читается как «горит» ещё до
 *  того, как разобран цвет.
 *  `colorOverride` нужен событиям жизни эффекта: снятие перекрашивает ФОРМУ, сохраняя её узнаваемой
 *  (видно, ЧТО сняли), а цвет говорит, ЧЕМ сняли. `scale`/`alphaMul` — толчок стака и таяние. */
export function burnFlames(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number, level: number,
  colorOverride?: string, scale = 1, alphaMul = 1
): void {
  if (level <= 0) return;
  const u = h / 16;
  const col = colorOverride ?? ST.burn;
  const n = Math.round(5 + level * 9);

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < n; i++) {
    const ph = (tick * 0.055 + jag(i, 21)) % 1; // жизнь языка, 0..1
    const sx = x + (jag(i, 22) - 0.5) * u * 5.4 * scale;
    const sy = groundY - u * (0.5 + jag(i, 23) * 12.5);
    const rise = u * (1.6 + 3.6 * level) * ph * scale;
    const w = u * (0.3 + 0.34 * level) * (1 - ph * 0.75) * scale;
    const a = (1 - ph) * (0.42 + 0.42 * level) * alphaMul;

    ctx.fillStyle = `rgba(${col},${a.toFixed(3)})`;
    ctx.beginPath();
    ctx.moveTo(sx - w, sy);
    ctx.quadraticCurveTo(sx - w * 0.4, sy - rise * 0.6, sx, sy - rise);
    ctx.quadraticCurveTo(sx + w * 0.4, sy - rise * 0.6, sx + w, sy);
    ctx.closePath();
    ctx.fill();

    if (level > 0.6) {
      // Ядро языка — только на верхних ступенях.
      ctx.fillStyle = `rgba(255,236,190,${(a * 0.5).toFixed(3)})`;
      ctx.beginPath();
      ctx.ellipse(sx, sy - rise * 0.35, w * 0.35, rise * 0.28, 0, 0, Math.PI * 2);
      ctx.fill();
    }
  }
  ctx.restore();
}

/** Стан: канал «Воля». Дрожь тела плюс три штриха по орбите над головой — единственный
 *  абстрактный знак, который постоянный слой себе позволяет. */
export function stunMarks(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number): void {
  const u = h / 16;
  const cy = groundY - h - u * 1.4;
  const r = u * 2.8;
  ctx.save();
  ctx.lineCap = "round";
  for (let i = 0; i < 3; i++) {
    const a = tick * 0.1 + (i * Math.PI * 2) / 3;
    const px = x + Math.cos(a) * r;
    const py = cy + Math.sin(a) * r * 0.32;
    const k = 0.45 + 0.55 * (0.5 + Math.sin(a) * 0.5); // дальние тусклее — намёк на объём
    ctx.strokeStyle = `rgba(${ST.stun},${(k * 0.95).toFixed(3)})`;
    ctx.lineWidth = 2.4;
    ctx.beginPath();
    ctx.moveTo(px - u * 0.42, py - u * 0.42);
    ctx.lineTo(px + u * 0.42, py + u * 0.42);
    ctx.stroke();
  }
  ctx.restore();
}

/** Метка: канал «Снаружи». Один знак, медленное вращение — орбита читается как «извне». */
export function markSign(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number): void {
  const u = h / 16;
  const cy = groundY - h - u * 3.6;
  const squeeze = Math.cos(tick * 0.055); // псевдо-вращение сжатием по X
  ctx.save();
  ctx.translate(x, cy);
  ctx.scale(Math.max(0.18, Math.abs(squeeze)), 1);
  ctx.fillStyle = `rgba(${ST.mark},.9)`;
  ctx.beginPath();
  ctx.moveTo(0, -u * 1.15);
  ctx.lineTo(u * 0.8, 0);
  ctx.lineTo(0, u * 1.15);
  ctx.lineTo(-u * 0.8, 0);
  ctx.closePath();
  ctx.fill();
  ctx.restore();
}

/** Замедление: канал «Земля». Клякса под ногами, которую юнит тянет — работает и когда он стоит на
 *  месте, чего эхо-силуэты не умеют (и потому остаются за рывками). */
export function slowPuddle(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number): void {
  const u = h / 16;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < 3; i++) {
    const k = 1 - i * 0.28;
    const br = 0.5 + 0.5 * Math.sin(tick * 0.06 + i * 1.3);
    ctx.fillStyle = `rgba(${ST.slow},${(0.1 + 0.09 * br * k).toFixed(3)})`;
    ctx.beginPath();
    ctx.ellipse(x - u * (0.5 + i * 0.9), groundY + u * 0.7,
      u * (3.4 - i * 0.5), u * (1.05 - i * 0.12), 0, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.strokeStyle = `rgba(${ST.slow},.45)`;
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  ctx.ellipse(x, groundY + u * 0.7, u * 3.4, u * 1.05, 0, 0, Math.PI * 2);
  ctx.stroke();
  ctx.restore();
}

/** Информационный слой: точные числа по тумблеру. Постоянный слой их не несёт намеренно —
 *  «сколько именно» нужно в паузе разбора, а не каждую секунду боя. */
export function infoPanel(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number,
  rows: Array<[string, string, string]>
): void {
  if (!isOn(INFO_LAYER)) return;
  const u = h / 16;
  const w = 176;
  const rh = 20;
  const boxH = rows.length * rh + 10;
  const bx = x + u * 4;
  const by = groundY - h - u * 2;

  ctx.fillStyle = "rgba(10,9,8,.86)";
  ctx.fillRect(bx, by, w, boxH);
  ctx.strokeStyle = "rgba(58,44,30,1)";
  ctx.lineWidth = 1;
  ctx.strokeRect(bx, by, w, boxH);

  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  rows.forEach((row, i) => {
    ctx.fillStyle = row[2];
    ctx.fillRect(bx + 8, by + 10 + i * rh - 5, 6, 6);
    ctx.fillStyle = "rgba(232,220,196,.92)";
    ctx.fillText(row[0], bx + 22, by + 16 + i * rh);
    ctx.fillStyle = "rgba(198,154,75,.95)";
    ctx.fillText(row[1], bx + w - 8 - ctx.measureText(row[1]).width, by + 16 + i * rh);
  });
}

/* ---------- четыре канала на одном теле ---------- */

/** Выноска канала: подпись слева, линия к той высоте, где канал живёт. */
function channelTag(
  ctx: CanvasRenderingContext2D,
  name: string, y: number, x0: number, x1: number, color: string
): void {
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = color;
  ctx.fillRect(28, y - 4, 6, 6);
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(name, 42, y + 4);
  ctx.strokeStyle = "rgba(58,44,30,.9)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(x0, y);
  ctx.lineTo(x1, y);
  ctx.stroke();
}

const drawChannels: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 78);
  const groundY = h - 78;
  const bodyH = 200;
  const x = w * 0.58;
  const u = bodyH / 16;

  slowPuddle(ctx, x, groundY, bodyH);
  const bx = statusBody(ctx, x, groundY, bodyH, { stun: true, burn: 2 / 3 });
  burnFlames(ctx, bx, groundY, bodyH, 2 / 3);
  stunMarks(ctx, bx, groundY, bodyH);
  markSign(ctx, bx, groundY, bodyH);

  channelTag(ctx, "воля", groundY - bodyH - u * 1.4, 150, x - u * 3.4, hexOf(ST.stun));
  channelTag(ctx, "снаружи", groundY - bodyH - u * 3.6, 150, x - u * 1.6, hexOf(ST.mark));
  channelTag(ctx, "тело", groundY - bodyH * 0.55, 150, x - u * 3.2, hexOf(ST.burn));
  channelTag(ctx, "земля", groundY + u * 0.7, 150, x - u * 3.8, hexOf(ST.slow));

  infoPanel(ctx, x, groundY, bodyH, [
    ["стан", "0.8 с", hexOf(ST.stun)],
    ["поджог x3", "4.2 с", hexOf(ST.burn)],
    ["метка", "6.0 с", hexOf(ST.mark)],
    ["замедление", "-30%", hexOf(ST.slow)]
  ]);

  miniLabel(ctx, "четыре канала одновременно");
};

/* ---------- шкала стака ---------- */

/** Пороги абсолютные и живут на самом статусе: масштаб стаков у Изморози и Углей свой, единой
 *  формулы на всех нет. Четвёртая ступень открыта вверх — 999 не граница, а пример. */
const BURN_STEPS = [1, 10, 30, 100] as const;

function burnStep(stacks: number): number {
  if (stacks >= BURN_STEPS[3]) return 4;
  if (stacks >= BURN_STEPS[2]) return 3;
  if (stacks >= BURN_STEPS[1]) return 2;
  return 1;
}

/** Стенды вариантов ходят по трём представителям ступеней; четвёртая живёт своим стендом, иначе
 *  подпись «ступень 3 из 3» на сотне стаков врала бы. */
const BURN_SCALE = [4, 17, 52] as const;
const BURN_HOLD = 42; // 1.4 с на значение — успеть разглядеть

function burnStacks(): { stacks: number; det: number } {
  const i = Math.floor(tick / BURN_HOLD) % BURN_SCALE.length;
  const local = tick % BURN_HOLD;
  // Возврат шкалы к началу читается как детонация: стаки потратили.
  const det = i === 0 && local < 7 ? local / 7 : -1;
  return { stacks: BURN_SCALE[i] ?? BURN_SCALE[0], det };
}

type BurnMode = "steps" | "cont" | "binary";

/** Кроссфейд между ступенями: стаки DoT ходят вокруг порога, и на жёстком переключателе тело
 *  мигало бы между состояниями. */
const STEP_FADE = 5; // 0.15 с при 30 Гц

function burnLevel(mode: BurnMode, stacks: number): number {
  if (stacks <= 0) return 0;
  if (mode === "binary") return 1;
  if (mode === "cont") return Math.min(1, stacks / BURN_SCALE[2]);

  const local = tick % BURN_HOLD;
  const cur = burnStep(stacks) / 3;
  if (local >= STEP_FADE) return Math.min(1, cur);

  const i = Math.floor(tick / BURN_HOLD) % BURN_SCALE.length;
  const prevStacks = BURN_SCALE[(i - 1 + BURN_SCALE.length) % BURN_SCALE.length] ?? BURN_SCALE[0];
  const prev = burnStep(prevStacks) / 3;
  return Math.min(1, prev + (cur - prev) * (local / STEP_FADE));
}

function burnLevelLabel(mode: BurnMode, stacks: number): string {
  if (stacks <= 0) return "нет";
  if (mode === "binary") return "горит";
  if (mode === "cont") return `${(Math.min(1, stacks / BURN_SCALE[2]) * 100).toFixed(0)}%`;
  return `ступень ${burnStep(stacks)} из 3`;
}

/** Счётчик модели и то, во что его перевело тело, — рядом, чтобы разрыв был виден сразу. */
function stackReadout(
  ctx: CanvasRenderingContext2D, h: number,
  stacks: string, shown: string, hot: boolean
): void {
  ctx.font = "500 14px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("стаков в симе", 26, h - 40);
  ctx.fillStyle = hot ? "rgba(255,146,48,1)" : "rgba(232,220,196,.95)";
  ctx.fillText(stacks, 158, h - 40);
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("видно на теле", 210, h - 40);
  ctx.fillStyle = hot ? "rgba(255,220,150,1)" : "rgba(198,154,75,.95)";
  ctx.fillText(shown, 342, h - 40);
}

function drawBurnStand(mode: BurnMode): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 74);
    const groundY = h - 74;
    const bodyH = 156;
    const x = w / 2;
    const u = bodyH / 16;
    const st = burnStacks();
    const level = burnLevel(mode, st.stacks);

    const bx = statusBody(ctx, x, groundY, bodyH, { burn: level });
    burnFlames(ctx, bx, groundY, bodyH, level);

    if (st.det >= 0) {
      // Детонация: событие, а не статус.
      const k = 1 - st.det;
      const r = u * 9 * (0.4 + st.det);
      const cy = groundY - bodyH * 0.55;
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      const g = ctx.createRadialGradient(bx, cy, 0, bx, cy, r);
      g.addColorStop(0, `rgba(255,240,205,${(0.85 * k).toFixed(3)})`);
      g.addColorStop(0.5, `rgba(${ST.burn},${(0.45 * k).toFixed(3)})`);
      g.addColorStop(1, `rgba(${ST.burn},0)`);
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(bx, cy, r, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
    }

    stackReadout(ctx, h, String(st.stacks), burnLevelLabel(mode, st.stacks), st.stacks >= 5);
    infoPanel(ctx, x, groundY, bodyH, [[`поджог x${st.stacks}`, "4.0 с", hexOf(ST.burn)]]);
  };
}

/* ---------- четвёртая ступень: пасхалка на сломе ---------- */

/** Число частиц НЕ растёт — работает тот же потолок, что у искр: 999 стаков не имеют права стать
 *  999 частицами. Меняется характер: языки сходятся в столб, тело уходит в силуэт внутри пересвета. */
const PILLAR_EMBERS = 28;

function burnPillar(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number): void {
  const u = h / 16;
  const top = groundY - h * 2.1;
  const sway = Math.sin(tick * 0.085) * u * 0.7;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";

  const g = ctx.createLinearGradient(0, groundY, 0, top);
  g.addColorStop(0, "rgba(255,238,196,.50)");
  g.addColorStop(0.32, `rgba(${ST.burn},.40)`);
  g.addColorStop(1, `rgba(${ST.burn},0)`);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(x - u * 3.6, groundY + u);
  ctx.quadraticCurveTo(x - u * 2.8 + sway, groundY - h * 0.9, x + sway * 1.5, top);
  ctx.quadraticCurveTo(x + u * 2.8 + sway, groundY - h * 0.9, x + u * 3.6, groundY + u);
  ctx.closePath();
  ctx.fill();

  const core = ctx.createLinearGradient(0, groundY, 0, groundY - h * 1.35);
  core.addColorStop(0, "rgba(255,252,240,.55)");
  core.addColorStop(1, "rgba(255,236,190,0)");
  ctx.fillStyle = core;
  ctx.beginPath();
  ctx.moveTo(x - u * 1.5, groundY + u);
  ctx.quadraticCurveTo(x - u * 1.1 + sway, groundY - h * 0.7, x + sway, groundY - h * 1.35);
  ctx.quadraticCurveTo(x + u * 1.1 + sway, groundY - h * 0.7, x + u * 1.5, groundY + u);
  ctx.closePath();
  ctx.fill();

  for (let i = 0; i < PILLAR_EMBERS; i++) {
    const ph = (tick * 0.035 + jag(i, 31)) % 1;
    const px = x + (jag(i, 32) - 0.5) * u * 5 * (0.3 + ph) + sway * ph * 2;
    const py = groundY - u + -h * 2.2 * ph;
    const r = (1.9 - ph * 1.2) * (0.6 + jag(i, 33) * 0.8);
    ctx.fillStyle = `rgba(255,${Math.round(210 - ph * 70)},140,${((1 - ph) * 0.85).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(px, py, Math.max(0.5, r), 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

const drawAbsurd: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 74);
  const groundY = h - 74;
  const bodyH = 156;
  const x = w / 2;
  const u = bodyH / 16;

  const bx = statusBody(ctx, x, groundY, bodyH, { burn: 1 });
  burnPillar(ctx, bx, groundY, bodyH);

  // Тело внутри пересвета: силуэт остаётся читаемым, но перестаёт быть носителем цвета.
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  unitPath(ctx, bx, groundY, bodyH);
  ctx.clip();
  const g = ctx.createRadialGradient(bx, groundY - bodyH * 0.5, u, bx, groundY - bodyH * 0.5, u * 7);
  g.addColorStop(0, "rgba(255,246,214,.55)");
  g.addColorStop(1, "rgba(255,180,90,.12)");
  ctx.fillStyle = g;
  ctx.fillRect(bx - u * 4, groundY - bodyH - u, u * 8, bodyH + u * 2);
  ctx.restore();

  stackReadout(ctx, h, "999", "ступень 4 · пасхалка", true);
  infoPanel(ctx, x, groundY, bodyH, [["поджог x999", "беск.", hexOf(ST.burn)]]);
};

/* ---------- раздел ---------- */

const section: SectionDef = {
  id: "status",
  title: "Статусы",
  eyebrow: "Лаборатория · джус · постоянный слой",
  lede:
    "У статуса другая экономика внимания, чем у удара. Удар живёт четыре кадра и имеет право звенеть; " +
    "статус висит секундами на восьми телах разом, поэтому обязан быть тихим и однозначным " +
    "одновременно. Отсюда четыре канала — четыре разных места вокруг тела, которые физически не " +
    "накладываются, — и не больше одного статуса в канале.",

  blocks: [
    {
      kind: "head",
      id: "channels",
      title: "Четыре канала на одном теле",
      lede:
        "Худший законный случай: юнит застанен, горит вторым стаком, помечен и замедлен. Четыре " +
        "статуса одновременно — и вопрос ровно один: получилась ёлка или читается?"
    },
    {
      kind: "text",
      html:
        "Цвет здесь красится <b>статусом</b>, а не палитрой бьющего: на теле цели важно состояние, а не " +
        "автор. Ось «кто сделал» уже отыграна вспышкой и искрами в момент наложения."
    },
    {
      kind: "toggle",
      id: INFO_LAYER,
      label: "Z · подробности",
      note:
        "тумблер, а не удержание: нажал — осталось. На паузе слой сам не всплывает — бой ещё и " +
        "разглядывают."
    },
    {
      kind: "split",
      items: [
        {
          id: "four-channels",
          status: "accepted",
          tag: "худший законный случай",
          title: "Стан, горение, метка, замедление",
          size: [740, 470],
          decision: "2026-07-31/74",
          note:
            "Каналы не спорят, потому что заняли разные зоны вокруг силуэта и разные векторы движения: " +
            "дрожь на месте, языки вверх, орбита над головой, растекание под ногами.",
          verdict:
            "Земля тут ключевая — она работает и когда юнит стоит, а эхо-силуэты не работали бы (и " +
            "потому остаются за рывками).",
          draw: drawChannels
        }
      ]
    },
    {
      kind: "legend",
      items: [
        { color: hexOf(ST.stun), text: "воля — стан, сон, заморозка" },
        { color: hexOf(ST.burn), text: "тело — горение, яд, изморозь, кровь" },
        { color: hexOf(ST.mark), text: "снаружи — щит, метка, усиление" },
        { color: hexOf(ST.slow), text: "земля — замедление, корни" }
      ]
    },

    {
      kind: "head",
      id: "stack-steps",
      title: "Сколько ступеней у стака",
      lede:
        "Постоянный слой отвечает на «что происходит и насколько плохо». Точное число стаков и " +
        "длительность — работа информационного слоя. Пороги у Поджога абсолютные: 1–9 мало · 10–29 " +
        "средне · 30–99 много · 100 и выше пасхалка. Стенды ходят по представителю каждой ступени — " +
        "4, 17, 52 стака, — переход идёт кроссфейдом 0.15 с, а возврат шкалы к началу читается как " +
        "детонация."
    },
    {
      kind: "stands",
      items: [
        {
          id: "steps", status: "accepted", title: "Три ступени", size: [480, 330],
          decision: "2026-07-31/78",
          note: "Мало / средне / много. Любое число стаков сворачивается в три состояния тела, пороги живут на самом статусе.",
          facts: [["различает", "1-9 · 10-29 · 30-99"], ["переход", "кроссфейд 0.15 с"], ["точность", "по Z"]],
          verdict: "Глаз надёжно берёт три ступени яркости. «Пора детонировать» — верхняя, и она заметно отличается от остальных.",
          draw: drawBurnStand("steps")
        },
        {
          id: "continuous", status: "rejected", title: "Непрерывно", size: [480, 330],
          note: "Интенсивность прямо пропорциональна стакам — как налёт изморози, растущий снизу вверх.",
          facts: [["различает", "формально всё"], ["стройность", "одна формула на все стаки"]],
          verdict: "Стройнее в коде и честнее к модели, но 17 против 20 в бою не читается — то есть шкала существует ради себя. Плюс упирается в потолок: после сотни расти уже некуда, а стаки идут дальше.",
          draw: drawBurnStand("cont")
        },
        {
          id: "binary", status: "rejected", title: "Без градации", size: [480, 330],
          note: "Горит или не горит. Постоянный слой предельно тихий, вся точность уехала в информационный.",
          facts: [["различает", "есть / нет"], ["шум", "минимальный"]],
          verdict: "Самый спокойный экран, но «горит на четыре стака» и «горит на пятьдесят» выглядят одинаково — вся разница уехала в Z, а он тумблер и по умолчанию выключен.",
          draw: drawBurnStand("binary")
        }
      ]
    },

    {
      kind: "head",
      id: "fourth-step",
      title: "Четвёртая ступень: пасхалка на сломе",
      lede:
        "Стак иногда уезжает туда, где балансу нечего сказать — сотня и выше у эффекта без потолка. " +
        "Это не повод крутить яркость дальше: три ступени уже заняли шкалу читаемости. Вместо ступени " +
        "тело меняет состояние — перестаёт «гореть» и становится источником."
    },
    {
      kind: "split",
      items: [
        {
          id: "absurd", status: "accepted", tag: "ступень 4 · пасхалка", title: "Юнит становится факелом",
          size: [480, 330], decision: "2026-07-31/80",
          note: "Число частиц НЕ растёт — работает тот же потолок, что у искр. Меняется характер: языки от контура сходятся в один столб выше роста, тело уходит в силуэт внутри пересвета.",
          facts: [["порог", "100 и выше, открыт вверх"], ["где живёт", "у эффектов без балансного потолка"], ["частицы", "потолок тот же, растёт характер"]],
          verdict: "В язык читаемости четвёртая ступень НЕ входит: важную информацию на неё вешать нельзя — её почти никто никогда не увидит.",
          draw: drawAbsurd
        }
      ]
    },
    {
      kind: "note",
      html:
        "Верхней границы у ступени нет намеренно: 999 — не граница, а пример. Иначе на тысяче стаков " +
        "показ провалился бы в никуда — ровно в том случае, для которого ступень и заводится. Где стак " +
        "ограничен балансом, сотня недостижима и пасхалка просто не срабатывает: она живёт в тех " +
        "местах, где <b>игра ломается по задумке</b>. Свой тумблер ей обязателен, как любому эффекту джуса."
    }
  ]
};

export default section;
