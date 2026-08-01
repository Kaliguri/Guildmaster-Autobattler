/* Удар: взмах, архетипы формы, слои хита, гибриды и дальний бой.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Удар.

   Состояние (порезы на теле, точки попаданий) НЕ накапливается, а вычисляется от времени. Причина
   не в чистоте: каркас не рисует стенд, пока тот за краем экрана, и накопленное состояние
   расходилось бы у соседних стендов в зависимости от того, докрутил ли до них игрок. */

import { frame, tick, TOTAL } from "../clock.js";
import { COL, drawUnit, ground, jag, miniLabel, RED, sparks } from "../draw.js";
import type { DrawFn, SectionDef } from "../types.js";

/* ---------- клип атаки ---------- */

const CONTACT = 16;
const WINDUP_START = 6;
const STRIKE_START = 13;
const STRIKE_END = 19;
const RECOVERY_END = 27;

const REST = -18;
const BACK = -145;
const FOLLOW = 55;
const ARM = 150;

const easeOut = (t: number): number => 1 - Math.pow(1 - t, 2);
const easeIn = (t: number): number => t * t;
const easeInOut = (t: number): number => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);

/** Угол руки в градусах для ДРОБНОГО кадра — дробного, чтобы трейл был гладким. */
function armAngle(f: number): number {
  if (f < WINDUP_START) return REST;
  if (f < STRIKE_START) return REST + (BACK - REST) * easeOut((f - WINDUP_START) / (STRIKE_START - WINDUP_START));
  if (f <= STRIKE_END) return BACK + (FOLLOW - BACK) * easeIn((f - STRIKE_START) / (STRIKE_END - STRIKE_START));
  if (f <= RECOVERY_END) return FOLLOW + (REST - FOLLOW) * easeInOut((f - STRIKE_END) / (RECOVERY_END - STRIKE_END));
  return REST;
}

function shoulderOf(x: number, groundY: number, h: number): { x: number; y: number } {
  return { x, y: groundY - h + (h / 16) * 5.6 };
}

function tipAt(f: number, sx: number, sy: number, armLen: number): { x: number; y: number } {
  const a = (armAngle(f) * Math.PI) / 180;
  return { x: sx + Math.cos(a) * armLen, y: sy + Math.sin(a) * armLen };
}

function drawArmAndBlade(ctx: CanvasRenderingContext2D, sx: number, sy: number, f: number, armLen: number): void {
  const a = (armAngle(f) * Math.PI) / 180;
  const hand = { x: sx + Math.cos(a) * armLen * 0.42, y: sy + Math.sin(a) * armLen * 0.42 };
  const tip = { x: sx + Math.cos(a) * armLen, y: sy + Math.sin(a) * armLen };

  ctx.lineCap = "round";
  ctx.strokeStyle = COL.bodyLit;
  ctx.lineWidth = 9;
  ctx.beginPath();
  ctx.moveTo(sx, sy);
  ctx.lineTo(hand.x, hand.y);
  ctx.stroke();

  ctx.strokeStyle = "#C9B591";
  ctx.lineWidth = 6;
  ctx.beginPath();
  ctx.moveTo(hand.x, hand.y);
  ctx.lineTo(tip.x, tip.y);
  ctx.stroke();
}

/** Номер удара — соль процедурной формы: два удара подряд не совпадают. */
function hitCounter(): number {
  return Math.floor(tick / TOTAL);
}

/** Каждый пятый цикл идёт лечение: видно, как гаснут старые порезы. */
function healPhase(): boolean {
  return hitCounter() % 5 === 4;
}

function label(ctx: CanvasRenderingContext2D, text: string, color: string): void {
  ctx.font = "500 15px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(text.toUpperCase(), 46, 44);
  ctx.fillStyle = color;
  ctx.fillRect(30, 33, 8, 8);
}

/* ---------- серп по двум точкам ----------
   A — начало взмаха (StrikeStart), B — точка хита. Дуга строится между ними с прогибом; куда меч
   ушёл дальше, эффект не интересует. Форма процедурна: прогиб, толщина и неровность берутся от
   номера удара, поэтому два удара подряд не дают одинаковый знак. РАЗМЕР генератор не трогает —
   он несёт вес удара. */

interface SickleOpt {
  bow: number;
  thick: number;
  progress: number;
  fade: number;
  /** Где на форме лежит точка хита. У серпа и веретена она ЦЕНТРАЛЬНАЯ: клинок не встаёт в теле, он
   *  проходит насквозь. У дробящего КОНЕЧНАЯ: булава там и остаётся, а дальше идёт звезда. */
  hitAtCenter?: boolean;
}

function sickle(
  ctx: CanvasRenderingContext2D,
  ax: number, ay: number, bx: number, by: number, opt: SickleOpt
): void {
  const prog = Math.max(0.02, Math.min(1, opt.progress));
  const tipX = opt.hitAtCenter ? ax + (bx - ax) * 2 : bx;
  const tipY = opt.hitAtCenter ? ay + (by - ay) * 2 : by;
  // «Прорастание» от A к концу формы.
  const ex = ax + (tipX - ax) * prog;
  const ey = ay + (tipY - ay) * prog;
  const mx = (ax + ex) / 2;
  const my = (ay + ey) / 2;
  const dx = ex - ax;
  const dy = ey - ay;
  const len = Math.max(1e-3, Math.hypot(dx, dy));
  const nx = -dy / len; // нормаль к хорде — в неё уходит прогиб
  const ny = dx / len;

  const band = (scale: number, alpha: number, color: string): void => {
    const cx = mx + nx * opt.bow * scale;
    const cy = my + ny * opt.bow * scale;
    const t = opt.thick * scale;
    ctx.beginPath();
    ctx.moveTo(ax, ay);
    ctx.quadraticCurveTo(cx + nx * t, cy + ny * t, ex, ey);
    ctx.quadraticCurveTo(cx - nx * t, cy - ny * t, ax, ay);
    ctx.closePath();
    ctx.fillStyle = `rgba(${color},${alpha.toFixed(3)})`;
    ctx.fill();
  };

  band(1.0, 0.4 * opt.fade, "77,242,255");
  band(0.82, 0.55 * opt.fade, "160,250,255");
  band(0.55, 0.92 * opt.fade, "255,255,255");
}

/** Звезда-трещина в точке B — вторая часть дробящего удара. */
function crackStar(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, radius: number, t: number, fade: number, salt: number
): void {
  const RAYS = 8;
  const inner = radius * (0.05 + 0.45 * t);
  for (let i = 0; i < RAYS; i++) {
    const a = (i / RAYS) * Math.PI * 2 + jag(i, salt) * 0.35 - 0.17;
    const lenK = 0.45 + jag(i, salt + 4) * 0.55;
    const wid = (0.045 + jag(i, salt + 8) * 0.05) * (1 - t * 0.4);
    ctx.beginPath();
    ctx.moveTo(x + Math.cos(a - wid) * inner, y + Math.sin(a - wid) * inner);
    ctx.lineTo(x + Math.cos(a) * radius * lenK, y + Math.sin(a) * radius * lenK);
    ctx.lineTo(x + Math.cos(a + wid) * inner, y + Math.sin(a + wid) * inner);
    ctx.closePath();
    ctx.fillStyle = `rgba(255,255,255,${(0.85 * fade).toFixed(3)})`;
    ctx.fill();
  }
}

/* ---------- три способа нарисовать взмах ---------- */

type SwingKind = "trail" | "swept" | "cross" | "final";

function swingScene(ctx: CanvasRenderingContext2D, w: number, h: number) {
  ground(ctx, w, h, 134);
  const groundY = h - 140;
  const bodyH = 250;
  ctx.fillStyle = "rgba(147,128,94,.65)";
  ctx.font = "500 15px ui-monospace, Consolas, monospace";
  ctx.fillText("H", 46, groundY - bodyH - 10);
  ctx.strokeStyle = "rgba(147,128,94,.35)";
  ctx.beginPath();
  ctx.moveTo(52, groundY - bodyH);
  ctx.lineTo(52, groundY);
  ctx.stroke();
  return { groundY, H: bodyH, attacker: w * 0.34, target: w * 0.63 };
}

function drawSwing(kind: SwingKind): DrawFn {
  return (ctx, w, h) => {
    const s = swingScene(ctx, w, h);
    const sh = shoulderOf(s.attacker, s.groundY, s.H);

    drawUnit(ctx, s.target, s.groundY, s.H * 0.95);
    drawUnit(ctx, s.attacker, s.groundY, s.H, true);

    if (kind === "trail") {
      // След: подкадры назад, гаснет и сужается. Живёт весь свинг.
      const STEPS = 26;
      const SPAN = 9;
      ctx.globalCompositeOperation = "lighter";
      for (let i = STEPS; i > 0; i--) {
        const f0 = frame - (i / STEPS) * SPAN;
        const f1 = frame - ((i - 1) / STEPS) * SPAN;
        if (f0 < WINDUP_START) continue;
        const p0 = tipAt(f0, sh.x, sh.y, ARM);
        const p1 = tipAt(f1, sh.x, sh.y, ARM);
        const k = 1 - i / STEPS;
        ctx.strokeStyle = `rgba(77,242,255,${(0.06 + k * 0.5).toFixed(3)})`;
        ctx.lineWidth = 2 + k * 7;
        ctx.lineCap = "round";
        ctx.beginPath();
        ctx.moveTo(p0.x, p0.y);
        ctx.lineTo(p1.x, p1.y);
        ctx.stroke();
      }
      ctx.globalCompositeOperation = "source-over";
      drawArmAndBlade(ctx, sh.x, sh.y, frame, ARM);
      label(ctx, "трейл: живёт весь свинг", COL.holo);
      return;
    }

    if (kind === "swept") {
      if (frame >= STRIKE_START && frame <= STRIKE_END + 2) {
        const cur = Math.min(frame, STRIKE_END);
        const a0 = (armAngle(STRIKE_START) * Math.PI) / 180;
        const a1 = (armAngle(cur) * Math.PI) / 180;
        const fade = frame > STRIKE_END ? 1 - (frame - STRIKE_END) / 2 : 1;

        ctx.globalCompositeOperation = "lighter";
        const grad = ctx.createRadialGradient(sh.x, sh.y, ARM * 0.45, sh.x, sh.y, ARM * 1.04);
        grad.addColorStop(0, "rgba(77,242,255,0)");
        grad.addColorStop(0.72, `rgba(77,242,255,${(0.3 * fade).toFixed(3)})`);
        grad.addColorStop(1, `rgba(255,255,255,${(0.55 * fade).toFixed(3)})`);
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.moveTo(sh.x, sh.y);
        ctx.arc(sh.x, sh.y, ARM * 1.04, a0, a1, false);
        ctx.closePath();
        ctx.fill();

        ctx.strokeStyle = `rgba(255,255,255,${(0.75 * fade).toFixed(3)})`;
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(sh.x, sh.y, ARM * 1.04, a0, a1, false);
        ctx.stroke();
        ctx.globalCompositeOperation = "source-over";
      }
      drawArmAndBlade(ctx, sh.x, sh.y, frame, ARM);
      label(ctx, "дуга: центр в плече, только strike", COL.holo);
      return;
    }

    // «cross» и «final» строят одну и ту же форму; у финала сверху ещё ядро попадания.
    const START = CONTACT;
    const END = CONTACT + 5;
    const cx = s.target - 18;
    const cy = s.groundY - s.H * 0.62;

    if (frame >= START && frame <= END) {
      const t = (frame - START) / (END - START);
      const grow = Math.min(1, t / 0.28);
      const fade = t < 0.35 ? 1 : 1 - (t - 0.35) / 0.65;
      const a = tipAt(STRIKE_START, sh.x, sh.y, ARM);
      const salt = hitCounter() * 5;

      sickle(ctx, a.x, a.y, cx, cy, {
        bow: s.H * (0.2 + jag(salt, 1) * 0.08),
        thick: s.H * (0.055 + jag(salt, 2) * 0.02),
        progress: grow, fade, hitAtCenter: true
      });

      if (kind === "cross") {
        // Опорные точки: видно, что форма строится по ним, а не по клипу.
        ctx.globalCompositeOperation = "source-over";
        ctx.font = "600 13px ui-monospace, Consolas, monospace";
        for (const pt of [[a.x, a.y, "A"], [cx, cy, "B"]] as Array<[number, number, string]>) {
          ctx.strokeStyle = "rgba(184,134,59,.75)";
          ctx.lineWidth = 1.2;
          ctx.beginPath();
          ctx.arc(pt[0], pt[1], 6, 0, Math.PI * 2);
          ctx.stroke();
          ctx.fillStyle = "rgba(198,154,75,.95)";
          ctx.fillText(pt[2], pt[0] - 3, pt[1] - 11);
        }
      }
    }

    if (kind === "final" && frame >= CONTACT && frame <= CONTACT + 2) {
      // Ядро попадания: живёт 3 кадра и ТОЛЬКО если удар вошёл. Отсюда читается промах.
      const k = 1 - (frame - CONTACT) / 3;
      const r = s.H * 0.34 * k;
      const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
      g.addColorStop(0, `rgba(255,255,255,${(0.95 * k).toFixed(3)})`);
      g.addColorStop(0.45, `rgba(255,204,51,${(0.5 * k).toFixed(3)})`);
      g.addColorStop(1, "rgba(255,204,51,0)");
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.fill();

      for (let i = 0; i < 7; i++) {
        const ang = ((-30 + i * 13) * Math.PI) / 180;
        const d = s.H * (0.14 + 0.16 * (frame - CONTACT + 1)) * (0.7 + (i % 3) * 0.2);
        ctx.fillStyle = `rgba(255,242,140,${(0.85 * k).toFixed(3)})`;
        ctx.beginPath();
        ctx.arc(cx + Math.cos(ang) * d, cy + Math.sin(ang) * d, 3.2 * k + 1, 0, Math.PI * 2);
        ctx.fill();
      }
    }

    ctx.globalCompositeOperation = "source-over";
    drawArmAndBlade(ctx, sh.x, sh.y, frame, ARM);
    label(ctx, kind === "final" ? "принято: серп A -> B + ядро" : "серп A -> B, 4 кадра",
      kind === "final" ? COL.honey : COL.holo);
  };
}

/* ---------- три архетипа: одна семья параметров ---------- */

type Archetype = "slash" | "pierce" | "blunt";

const ARCHETYPE_LABEL: Record<Archetype, string> = {
  slash: "РЕЖУЩИЙ · СЕРП",
  pierce: "КОЛЮЩИЙ · ВЕРЕТЕНО",
  blunt: "ДРОБЯЩИЙ · ЗВЕЗДА"
};

function drawArchetype(kind: Archetype): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 84);
    const groundY = h - 90;
    const bodyH = 210;
    const cx = w * 0.54;

    drawUnit(ctx, cx, groundY, bodyH);

    const archLabel = (): void => {
      ctx.font = "500 14px ui-monospace, Consolas, monospace";
      ctx.fillStyle = "rgba(147,128,94,.9)";
      ctx.fillText(ARCHETYPE_LABEL[kind], 34, 38);
      ctx.fillStyle = COL.honey;
      ctx.fillRect(20, 28, 7, 7);
    };

    const px = cx - 14;
    const py = groundY - bodyH * 0.62;
    const START = CONTACT;
    const END = CONTACT + 5;
    if (frame < START || frame > END) { archLabel(); return; }

    const t = (frame - START) / (END - START);
    const grow = Math.min(1, t / 0.28);
    const fade = t < 0.3 ? 1 : 1 - (t - 0.3) / 0.7;

    // A — начало взмаха, B — точка хита. Полудлина A→B ≈ 0.7 H, полная форма 1.4 H.
    const ax = px - bodyH * 0.5;
    const ay = py - bodyH * 0.49;
    const salt = hitCounter() * 7 + (kind === "slash" ? 1 : kind === "pierce" ? 2 : 3);

    ctx.save();
    ctx.globalCompositeOperation = "lighter";

    if (kind === "slash") {
      // Правило: закруглённая дуга, прогиб и толщина гуляют в коридоре.
      sickle(ctx, ax, ay, px, py, {
        bow: bodyH * (0.2 + jag(salt, 1) * 0.08),
        thick: bodyH * (0.055 + jag(salt, 2) * 0.02),
        progress: grow, fade, hitAtCenter: true
      });
    }

    if (kind === "pierce") {
      // Правило: почти прямая, узкая, ядро собрано у точки входа.
      sickle(ctx, ax, ay, px, py, {
        bow: bodyH * (0.015 + jag(salt, 1) * 0.02),
        thick: bodyH * (0.028 + jag(salt, 2) * 0.008),
        progress: grow, fade, hitAtCenter: true
      });
      if (frame >= CONTACT) {
        const k = Math.max(0, 1 - (frame - CONTACT) / 3);
        const g = ctx.createRadialGradient(px, py, 0, px, py, bodyH * 0.16 * k);
        g.addColorStop(0, `rgba(255,255,255,${(0.95 * k).toFixed(3)})`);
        g.addColorStop(1, "rgba(77,242,255,0)");
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(px, py, bodyH * 0.16 * k, 0, Math.PI * 2);
        ctx.fill();
      }
    }

    if (kind === "blunt") {
      // Правило двухчастное: лёгкое движение серпом плюс звезда-трещина в точке B.
      sickle(ctx, ax + bodyH * 0.22, ay + bodyH * 0.22, px, py, {
        bow: bodyH * (0.09 + jag(salt, 1) * 0.05),
        thick: bodyH * (0.048 + jag(salt, 2) * 0.018),
        progress: grow, fade: fade * 0.55
      });
      if (frame >= CONTACT) {
        const st = Math.min(1, (frame - CONTACT) / 4);
        crackStar(ctx, px, py, bodyH * 0.7 * Math.min(1, 0.35 + st), st, fade, salt);
        const g = ctx.createRadialGradient(px, py, 0, px, py, bodyH * 0.34 * (1 - st * 0.5));
        g.addColorStop(0, `rgba(160,250,255,${(0.5 * fade).toFixed(3)})`);
        g.addColorStop(1, "rgba(77,242,255,0)");
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(px, py, bodyH * 0.34, 0, Math.PI * 2);
        ctx.fill();
      }
    }

    ctx.globalCompositeOperation = "source-over";
    ctx.font = "600 13px ui-monospace, Consolas, monospace";
    for (const pt of [[ax, ay, "A"], [px, py, "B"]] as Array<[number, number, string]>) {
      ctx.strokeStyle = "rgba(184,134,59,.8)";
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.arc(pt[0], pt[1], 6, 0, Math.PI * 2);
      ctx.stroke();
      ctx.fillStyle = "rgba(198,154,75,.95)";
      ctx.fillText(pt[2], pt[0] - 3, pt[1] - 11);
    }
    ctx.restore();
    archLabel();
  };
}

/* ---------- порезы: состояние тела, а не событие ----------
   Каждый удар пишет порез с запасом, равным снятому HP; хил гасит яркость пропорционально запасу,
   а не по таймеру. Вычисляются от времени: см. шапку файла. */

const CUT_LIMIT = 12;

interface Cut {
  ox: number;
  oy: number;
  angle: number;
  len: number;
  bright: number;
}

function cutsNow(): Cut[] {
  const now = hitCounter();
  const out: Cut[] = [];
  for (let n = Math.max(0, now - CUT_LIMIT + 1); n <= now; n++) {
    const dmg = 0.1 + jag(n, 5) * 0.3;
    // Каждый пятый цикл — лечение: гасит с головы очереди, самые старые.
    const healed = Math.floor((now - n) / 5) * 0.35;
    const bright = 1 - healed;
    if (bright <= 0.02) continue;
    out.push({
      ox: (jag(n, 1) - 0.5) * 0.55, // доли ширины корпуса
      oy: (jag(n, 2) - 0.5) * 0.7,
      angle: -20 + (jag(n, 3) - 0.5) * 30,
      len: 0.1 + dmg * 0.5,
      bright
    });
  }
  return out;
}

function drawCuts(ctx: CanvasRenderingContext2D, cx: number, groundY: number, h: number): number {
  const u = h / 16;
  const box = { x: cx - u * 2.6, y: groundY - h + u * 4.5, w: u * 5.2, h: u * 6.5 };
  const list = cutsNow();

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  for (const c of list) {
    const x = box.x + box.w * (0.5 + c.ox);
    const y = box.y + box.h * (0.5 + c.oy);
    const half = h * c.len * 0.5;
    const a = (c.angle * Math.PI) / 180;
    const dx = Math.cos(a) * half;
    const dy = Math.sin(a) * half;

    ctx.strokeStyle = `rgba(${RED},${(0.5 * c.bright).toFixed(3)})`;
    ctx.lineWidth = 4;
    ctx.beginPath();
    ctx.moveTo(x - dx, y - dy);
    ctx.lineTo(x + dx, y + dy);
    ctx.stroke();

    ctx.strokeStyle = `rgba(255,190,190,${(0.9 * c.bright).toFixed(3)})`;
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    ctx.moveTo(x - dx, y - dy);
    ctx.lineTo(x + dx, y + dy);
    ctx.stroke();
  }
  ctx.restore();
  return list.length;
}

/* ---------- слои хита ---------- */

/** Дуга за клинком: сектор от плеча на strike-фазе. Стадия 1, до хита. */
function bladeArc(ctx: CanvasRenderingContext2D, sx: number, sy: number, radius: number, fade: number): void {
  const cur = Math.min(frame, STRIKE_END);
  const a0 = (armAngle(STRIKE_START) * Math.PI) / 180;
  const a1 = (armAngle(cur) * Math.PI) / 180;
  const g = ctx.createRadialGradient(sx, sy, radius * 0.5, sx, sy, radius);
  g.addColorStop(0, "rgba(77,242,255,0)");
  g.addColorStop(0.8, `rgba(77,242,255,${(0.13 * fade).toFixed(3)})`);
  g.addColorStop(1, `rgba(150,235,250,${(0.24 * fade).toFixed(3)})`);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.moveTo(sx, sy);
  ctx.arc(sx, sy, radius, a0, a1, false);
  ctx.closePath();
  ctx.fill();
}

const drawHitFull: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 64);
  const groundY = h - 70;
  const bodyH = 230;
  const attacker = w * 0.32;
  const target = w * 0.63;
  const sh = shoulderOf(attacker, groundY, bodyH);

  drawUnit(ctx, target, groundY, bodyH * 0.95);
  drawUnit(ctx, attacker, groundY, bodyH, true);
  drawCuts(ctx, target, groundY, bodyH * 0.95);

  const bx = target - 16;
  const by = groundY - bodyH * 0.6;
  const dmg = 0.22;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";

  // Стадия 1 — дуга на взмахе.
  if (frame >= STRIKE_START && frame <= STRIKE_END + 2) {
    const fade = frame > STRIKE_END ? 1 - (frame - STRIKE_END) / 2 : 1;
    bladeArc(ctx, sh.x, sh.y, ARM * 1.04, fade);
  }

  // Стадия 2 — серп, ядро, искры.
  if (frame >= CONTACT && frame <= CONTACT + 5) {
    const t = (frame - CONTACT) / 5;
    const grow = Math.min(1, t / 0.28);
    const fade = t < 0.28 ? 1 : 1 - (t - 0.28) / 0.72;
    const a = tipAt(STRIKE_START, sh.x, sh.y, ARM);
    const salt = hitCounter() * 5;
    sickle(ctx, a.x, a.y, bx, by, {
      bow: bodyH * (0.2 + jag(salt, 1) * 0.08),
      thick: bodyH * (0.055 + jag(salt, 2) * 0.02),
      progress: grow, fade, hitAtCenter: true
    });
    const k = Math.max(0, 1 - t * 2);
    if (k > 0) {
      const g = ctx.createRadialGradient(bx, by, 0, bx, by, bodyH * 0.3 * k);
      g.addColorStop(0, `rgba(255,255,255,${(0.95 * k).toFixed(3)})`);
      g.addColorStop(0.5, `rgba(158,219,255,${(0.5 * k).toFixed(3)})`);
      g.addColorStop(1, "rgba(158,219,255,0)");
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(bx, by, bodyH * 0.3 * k, 0, Math.PI * 2);
      ctx.fill();
    }
  }
  if (frame >= CONTACT) sparks(ctx, bx, by, -18, dmg, frame - CONTACT, bodyH, hitCounter() * 9);

  ctx.globalCompositeOperation = "source-over";
  ctx.restore();
  drawArmAndBlade(ctx, sh.x, sh.y, frame, ARM);
  miniLabel(ctx, frame < CONTACT ? "СТАДИЯ 1 · ВЗМАХ, ДУГА ЗА КЛИНКОМ" : "СТАДИЯ 2 · СЕРП, ЯДРО, ИСКРЫ, ПОРЕЗ");
};

/** Искры при трёх уровнях урона: видно, что количество идёт пропорцией, а не «побольше». */
const drawSparkScale: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 56);
  const ys = h * 0.52;
  const levels: Array<[number, number, string]> = [
    [0.05, w * 0.2, "5%"],
    [0.15, w * 0.5, "15%"],
    [0.35, w * 0.8, "35%"]
  ];

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  levels.forEach(([f, x], i) => {
    if (frame >= CONTACT) sparks(ctx, x, ys, -18, f, frame - CONTACT, 190, hitCounter() * 9 + i * 31);
  });
  ctx.restore();

  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  for (const [, x, name] of levels) {
    ctx.fillStyle = "rgba(147,128,94,.85)";
    ctx.fillText(name, x - 12, h - 26);
    ctx.fillStyle = "rgba(184,134,59,.5)";
    ctx.beginPath();
    ctx.arc(x, ys, 3, 0, Math.PI * 2);
    ctx.fill();
  }
  miniLabel(ctx, "ИСКРЫ ПРОПОРЦИОНАЛЬНО УРОНУ");
};

const drawCutStand: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 46);
  const groundY = h - 46;
  const bodyH = 190;
  const cx = w / 2;
  drawUnit(ctx, cx, groundY, bodyH);
  const count = drawCuts(ctx, cx, groundY, bodyH);
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.fillText(`ПОРЕЗОВ: ${count} / ${CUT_LIMIT}`, 22, h - 22);
  miniLabel(ctx, healPhase() ? "ХИЛ ГАСИТ СТАРЫЕ" : "УДАРЫ ПИШУТ ПОРЕЗЫ");
};

/* ---------- гибриды: четыре способа смешать два цвета ---------- */

const FIRE = "255,140,64";
const ICE = "158,219,255";

type HybridMode = "along" | "core" | "across" | "layers";

const HYBRID_LABEL: Record<HybridMode, string> = {
  along: "ВДОЛЬ ФОРМЫ",
  core: "ЯДРО И КАЙМА",
  across: "ПОПЕРЁК ТОЛЩИНЫ",
  layers: "СЛОЯМИ"
};

function drawHybrid(mode: HybridMode): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 54);
    const groundY = h - 54;
    const bodyH = 170;
    const cx = w / 2 + 20;
    drawUnit(ctx, cx, groundY, bodyH);

    const START = CONTACT;
    const END = CONTACT + 5;
    if (frame < START || frame > END) { miniLabel(ctx, HYBRID_LABEL[mode]); return; }

    const t = (frame - START) / (END - START);
    const grow = Math.min(1, t / 0.28);
    const fade = t < 0.3 ? 1 : 1 - (t - 0.3) / 0.7;

    const ax = cx - bodyH * 0.5;
    const ay = groundY - bodyH * 1.07;
    const bx = cx;
    const by = groundY - bodyH * 0.58;
    const bow = bodyH * 0.22;
    const thick = bodyH * 0.06;

    // Конец формы: точка хита центральная, поэтому дуга уходит за цель.
    const tipX = ax + (bx - ax) * 2;
    const tipY = ay + (by - ay) * 2;
    const ex = ax + (tipX - ax) * grow;
    const ey = ay + (tipY - ay) * grow;
    const mx = (ax + ex) / 2;
    const my = (ay + ey) / 2;
    const len = Math.max(1e-3, Math.hypot(ex - ax, ey - ay));
    const nx = -(ey - ay) / len;
    const ny = (ex - ax) / len;

    const bandPath = (scale: number): void => {
      const c1x = mx + nx * bow * scale;
      const c1y = my + ny * bow * scale;
      const th = thick * scale;
      ctx.beginPath();
      ctx.moveTo(ax, ay);
      ctx.quadraticCurveTo(c1x + nx * th, c1y + ny * th, ex, ey);
      ctx.quadraticCurveTo(c1x - nx * th, c1y - ny * th, ax, ay);
      ctx.closePath();
    };

    ctx.save();
    ctx.globalCompositeOperation = "lighter";

    if (mode === "along") {
      // Градиент по хорде: от начала взмаха к концу проводки.
      const g = ctx.createLinearGradient(ax, ay, ex, ey);
      g.addColorStop(0, `rgba(${FIRE},${(0.85 * fade).toFixed(3)})`);
      g.addColorStop(1, `rgba(${ICE},${(0.85 * fade).toFixed(3)})`);
      bandPath(1.0); ctx.fillStyle = g; ctx.fill();
      bandPath(0.5); ctx.fillStyle = `rgba(255,255,255,${(0.9 * fade).toFixed(3)})`; ctx.fill();
    }

    if (mode === "core") {
      bandPath(1.0); ctx.fillStyle = `rgba(${ICE},${(0.55 * fade).toFixed(3)})`; ctx.fill();
      bandPath(0.7); ctx.fillStyle = `rgba(${FIRE},${(0.8 * fade).toFixed(3)})`; ctx.fill();
      bandPath(0.38); ctx.fillStyle = `rgba(255,255,255,${(0.92 * fade).toFixed(3)})`; ctx.fill();
    }

    if (mode === "across") {
      // Градиент по нормали: одна кромка горячая, другая холодная.
      const g = ctx.createLinearGradient(
        mx + nx * (bow + thick), my + ny * (bow + thick),
        mx - nx * thick, my - ny * thick
      );
      g.addColorStop(0, `rgba(${FIRE},${(0.85 * fade).toFixed(3)})`);
      g.addColorStop(1, `rgba(${ICE},${(0.85 * fade).toFixed(3)})`);
      bandPath(1.0); ctx.fillStyle = g; ctx.fill();
      bandPath(0.42); ctx.fillStyle = `rgba(255,255,255,${(0.88 * fade).toFixed(3)})`; ctx.fill();
    }

    if (mode === "layers") {
      bandPath(1.0); ctx.fillStyle = `rgba(${ICE},${(0.5 * fade).toFixed(3)})`; ctx.fill();
      bandPath(0.78); ctx.fillStyle = `rgba(${FIRE},${(0.62 * fade).toFixed(3)})`; ctx.fill();
      bandPath(0.52); ctx.fillStyle = `rgba(${ICE},${(0.7 * fade).toFixed(3)})`; ctx.fill();
      bandPath(0.3); ctx.fillStyle = `rgba(255,255,255,${(0.92 * fade).toFixed(3)})`; ctx.fill();
    }

    ctx.globalCompositeOperation = "source-over";
    ctx.restore();
    miniLabel(ctx, HYBRID_LABEL[mode]);
  };
}

/* ---------- дальний бой: линия-всполох ---------- */

const drawRanged: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 54);
  const groundY = h - 60;
  const bodyH = 200;
  const shooter = w * 0.18;
  const target = w * 0.81;

  drawUnit(ctx, target, groundY, bodyH * 0.95);
  drawUnit(ctx, shooter, groundY, bodyH, true);

  const ax = shooter + bodyH * 0.16;
  const ay = groundY - bodyH * 0.62;
  const bx = target - 16;
  const by = groundY - bodyH * 0.6;

  // Полёт: снаряд идёт от A к B и приходит ровно на кадре контакта.
  const FLY_A = CONTACT - 8;
  if (frame >= FLY_A && frame < CONTACT) {
    const k = (frame - FLY_A) / (CONTACT - FLY_A);
    const px = ax + (bx - ax) * k;
    const py = ay + (by - ay) * k;
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    const g = ctx.createRadialGradient(px, py, 0, px, py, bodyH * 0.07);
    g.addColorStop(0, "rgba(255,255,255,.95)");
    g.addColorStop(1, "rgba(77,242,255,0)");
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(px, py, bodyH * 0.07, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  // Линия-всполох: ПОСЛЕ попадания, по вектору полёта, прогиб ноль.
  if (frame >= CONTACT && frame <= CONTACT + 5) {
    const t = (frame - CONTACT) / 5;
    const fade = t < 0.25 ? 1 : 1 - (t - 0.25) / 0.75;
    const salt = hitCounter() * 3;

    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    sickle(ctx, ax, ay, bx, by, {
      bow: bodyH * (0.004 + jag(salt, 1) * 0.012),
      thick: bodyH * (0.024 + jag(salt, 2) * 0.008),
      progress: 1, fade, hitAtCenter: true
    });
    const k = Math.max(0, 1 - t * 1.6);
    const g = ctx.createRadialGradient(bx, by, 0, bx, by, bodyH * 0.26 * k);
    g.addColorStop(0, `rgba(255,255,255,${(0.95 * k).toFixed(3)})`);
    g.addColorStop(1, "rgba(77,242,255,0)");
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(bx, by, bodyH * 0.26 * k, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();

    ctx.font = "600 13px ui-monospace, Consolas, monospace";
    for (const pt of [[ax, ay, "A"], [bx, by, "B"]] as Array<[number, number, string]>) {
      ctx.strokeStyle = "rgba(184,134,59,.75)";
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.arc(pt[0], pt[1], 6, 0, Math.PI * 2);
      ctx.stroke();
      ctx.fillStyle = "rgba(198,154,75,.95)";
      ctx.fillText(pt[2], pt[0] - 3, pt[1] - 11);
    }
  }

  miniLabel(ctx, frame < CONTACT ? "ПОЛЁТ СНАРЯДА" : "ЛИНИЯ-ВСПОЛОХ ПОСЛЕ ХИТА");
};

/* ---------- область попадания: удары бьют в площадь, а не в точку ---------- */

const ATTACKERS = [
  { name: "убийца", bias: { x: -0.34, y: -0.22 }, from: "спина" },
  { name: "защитник", bias: { x: 0.0, y: 0.0 }, from: "лоб" },
  { name: "копейщик", bias: { x: 0.18, y: 0.38 }, from: "снизу" },
  { name: "стрелок", bias: { x: 0.3, y: -0.1 }, from: "издали" }
] as const;

const drawHitZone: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 90);
  const groundY = h - 96;
  const bodyH = 250;
  const cx = w / 2;
  const u = bodyH / 16;

  drawUnit(ctx, cx, groundY, bodyH);

  const box = { x: cx - u * 2.6, y: groundY - bodyH + u * 4.5, w: u * 5.2, h: u * 6.5 };
  ctx.strokeStyle = "rgba(77,242,255,.55)";
  ctx.setLineDash([5, 5]);
  ctx.lineWidth = 1.5;
  ctx.strokeRect(box.x, box.y, box.w, box.h);
  ctx.setLineDash([]);

  // Девять последних попаданий; бьющий меняется каждые три удара.
  const now = hitCounter();
  const shown = 9;
  for (let i = 0; i < shown; i++) {
    const n = now - (shown - 1 - i);
    if (n < 0) continue;
    const who = ATTACKERS[Math.floor(n / 3) % ATTACKERS.length] ?? ATTACKERS[0];
    const px = box.x + box.w * (0.5 + who.bias.x + (jag(n, 71) - 0.5) * 0.55);
    const py = box.y + box.h * (0.5 + who.bias.y + (jag(n, 72) - 0.5) * 0.55);
    const age = shown - 1 - i;

    if (age === 0) {
      ctx.globalCompositeOperation = "lighter";
      const g = ctx.createRadialGradient(px, py, 0, px, py, 26);
      g.addColorStop(0, "rgba(255,204,51,.95)");
      g.addColorStop(1, "rgba(255,204,51,0)");
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(px, py, 26, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalCompositeOperation = "source-over";
      ctx.fillStyle = "#FFFFFF";
      ctx.beginPath();
      ctx.arc(px, py, 3.5, 0, Math.PI * 2);
      ctx.fill();
    } else {
      ctx.fillStyle = `rgba(147,128,94,${Math.max(0.12, 0.7 - age * 0.09).toFixed(2)})`;
      ctx.beginPath();
      ctx.arc(px, py, 4, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  const who = ATTACKERS[Math.floor(now / 3) % ATTACKERS.length] ?? ATTACKERS[0];
  ctx.font = "500 15px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(`БЬЁТ: ${who.name.toUpperCase()}  ·  ${who.from.toUpperCase()}`, 30, 42);
  ctx.fillStyle = "rgba(184,134,59,.9)";
  ctx.fillText("ОБЛАСТЬ, А НЕ ТОЧКА", 30, h - 30);
};

/* ---------- таймлайн клипа ---------- */

const drawTimeline: DrawFn = (ctx, w, h) => {
  const x0 = 60;
  const x1 = w - 60;
  const y = h * 0.5;
  const barH = 34;
  const fx = (f: number): number => x0 + (x1 - x0) * (f / (TOTAL - 1));

  ctx.fillStyle = "rgba(147,128,94,.28)";
  ctx.fillRect(x0, y, fx(STRIKE_START) - x0, barH);
  ctx.fillStyle = "rgba(255,204,51,.34)";
  ctx.fillRect(fx(STRIKE_START), y, fx(STRIKE_END) - fx(STRIKE_START), barH);
  ctx.fillStyle = "rgba(58,44,30,.85)";
  ctx.fillRect(fx(STRIKE_END), y, x1 - fx(STRIKE_END), barH);

  ctx.strokeStyle = "rgba(58,44,30,1)";
  ctx.lineWidth = 1;
  ctx.strokeRect(x0, y, x1 - x0, barH);

  ctx.fillStyle = "rgba(147,128,94,.35)";
  for (let f = 0; f < TOTAL; f++) ctx.fillRect(fx(f), y + barH, 1, f % 5 === 0 ? 8 : 4);
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  for (let f = 0; f < TOTAL; f += 5) {
    ctx.fillStyle = "rgba(147,128,94,.7)";
    ctx.fillText(String(f), fx(f) - 4, y + barH + 26);
  }

  const marker = (f: number, name: string, color: string, up: boolean): void => {
    const x = fx(f);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(x, y - (up ? 46 : 0));
    ctx.lineTo(x, y + barH);
    ctx.stroke();
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(x, y - (up ? 46 : 0));
    ctx.lineTo(x - 6, y - (up ? 56 : 10));
    ctx.lineTo(x + 6, y - (up ? 56 : 10));
    ctx.closePath();
    ctx.fill();
    ctx.font = "600 14px ui-monospace, Consolas, monospace";
    ctx.fillText(name, x - ctx.measureText(name).width / 2, y - (up ? 66 : 20));
  };

  marker(STRIKE_START, "StrikeStart", COL.honey, false);
  marker(CONTACT, "Hit", COL.white, true);
  marker(STRIKE_END, "StrikeEnd", COL.honey, false);

  // Головка воспроизведения.
  ctx.strokeStyle = "rgba(184,134,59,.9)";
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(fx(frame), y - 8);
  ctx.lineTo(fx(frame), y + barH + 12);
  ctx.stroke();

  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.8)";
  ctx.fillText("КЛИП АТАКИ · 30 КАДРОВ · 1.00 С", x0, 40);
};

/* ---------- раздел ---------- */

const WIDE: [number, number] = [740, 560];

const section: SectionDef = {
  id: "hits",
  title: "Удар",
  eyebrow: "Лаборатория · джус · удар",
  lede:
    "Один и тот же удар, одна и та же анимация руки. Отличается только то, что рисует эффект — и от " +
    "этого зависит, читается взмах как «движение», как «траектория» или как «удар». Контакт на " +
    "кадре 16; крути транспортом покадрово.",

  blocks: [
    {
      kind: "head", id: "swing", title: "Удар мили: три способа нарисовать взмах",
      lede:
        "Все три показаны без хит-эффекта: искры, вспышка и отброс убраны намеренно, иначе разницу " +
        "между вариантами не разглядеть. В бою поверх любого из них ляжет ядро попадания — но только " +
        "если удар попал."
    },
    {
      kind: "stands",
      items: [
        {
          id: "trail", status: "rejected", title: "Трейл клинка", size: WIDE,
          note: "Непрерывный след за кончиком оружия. Живёт весь взмах — от начала замаха до конца возврата, тает по хвосту.",
          facts: [["владелец", "оружие, каждый кадр"], ["живёт", "кадры 6–26"], ["ширина", "0.03 H"], ["инструмент", "Trail / полилиния"]],
          verdict: "Читается как <b>движение</b>. Показывает, где клинок был, но не выделяет момент удара — трейл на замахе и на возврате выглядит одинаково.",
          draw: drawSwing("trail")
        },
        {
          id: "swept", status: "accepted", title: "Дуга за клинком", size: WIDE,
          decision: "2026-07-31/52",
          note: "Сектор с центром в плече, заметающий угол, пройденный за strike-фазу. Принадлежит <b>оружию</b> и рисуется всегда, когда клинок махнул, — в том числе на промахе.",
          facts: [["владелец", "ось вращения руки"], ["живёт", "кадры 13–20, strike"], ["радиус", "длина руки, ≈0.9 H"], ["инструмент", "Shapes Disc"]],
          verdict: "Читается как <b>траектория</b>: «клинок прошёл здесь». Это правда и при промахе — в отличие от серпа, который заявляет «удар состоялся».",
          draw: drawSwing("swept")
        },
        {
          id: "cross", status: "accepted", title: "Мазок поперёк", size: WIDE,
          decision: "2026-07-31/60",
          note: "Широкий серп поперёк направления удара. От оружия отвязан, ставится в мире и остаётся на месте, пока гаснет.",
          facts: [["владелец", "событие взмаха"], ["живёт", "кадры 14–19"], ["длина", "1.2 H, толщина 0.09 H"], ["инструмент", "Shapes / шейдер"]],
          verdict: "Читается как <b>удар</b>. Крупнее всех, не спорит с силуэтом, видна издалека — и это подпись эталона.",
          draw: drawSwing("cross")
        }
      ]
    },

    {
      kind: "head", id: "final", title: "Принято: два эффекта в РАЗНОЕ время",
      lede:
        "Взяты оба слоя — и дуга, и серп, — но конфликт двух центров решён не иерархией, а " +
        "<b>временем</b>: дуга живёт на strike-фазе взмаха, серп начинается на кадре Hit. " +
        "Одновременно их не бывает, поэтому спорить им не о чем. Отклонён только трейл — более " +
        "слабое повторение дуги."
    },
    {
      kind: "split",
      items: [
        {
          id: "layers-of-one-hit", status: "accepted", title: "Слои одного удара", size: WIDE,
          decision: "2026-07-31/41",
          note: "<b>Форма принадлежит КОНТАКТУ</b>: серп, ядро, искры и порез появляются ПОСЛЕ хита и только если удар вошёл. Дуга принадлежит оружию и есть всегда.",
          verdict: "Поэтому промах читается сам собой: дуга в пустоту и надпись evade, формы нет вовсе. Заявлять «удар состоялся» там, где его не было, эффект не имеет права.",
          draw: drawSwing("final")
        },
        {
          id: "timeline", status: "note", tag: "разметка клипа", title: "Тридцать кадров, три маркера",
          size: [1080, 230],
          note: "StrikeStart, Hit, StrikeEnd. Эффект вешается на маркер, а не на «примерно тогда»: клип может ускоряться, маркеры едут вместе с ним.",
          draw: drawTimeline
        }
      ]
    },

    {
      kind: "head", id: "archetypes", title: "Три архетипа — одна параметрическая семья",
      lede:
        "Это не три эффекта, а один шейдер с разными числами: количество лучей, кривизна, длина, " +
        "толщина, разброс. Новый тип урона — новая комбинация параметров, а не новый ассет."
    },
    {
      kind: "stands",
      items: [
        {
          id: "slash", status: "accepted", tag: "режущий", title: "Серп", size: [560, 440],
          facts: [["лучей", "1 широкий"], ["кривизна", "высокая"], ["размер", "1.2 H / 0.09 H"], ["рандом", "±10° угла"]],
          draw: drawArchetype("slash")
        },
        {
          id: "pierce", status: "accepted", tag: "колющий", title: "Веретено", size: [560, 440],
          facts: [["лучей", "1 узкий"], ["кривизна", "ноль"], ["размер", "0.9 H / 0.03 H"], ["рандом", "±6° угла"]],
          draw: drawArchetype("pierce")
        },
        {
          id: "blunt", status: "accepted", tag: "дробящий", title: "Звезда-трещина", size: [560, 440],
          facts: [["лучей", "7–9 неровных"], ["кривизна", "радиально"], ["размер", "0.7 H"], ["рандом", "и есть характер"]],
          draw: drawArchetype("blunt")
        }
      ]
    },
    {
      kind: "note",
      html:
        "Магия мазка не получает ни в одном виде: у магического и светового урона нет клинка, которым " +
        "машут, и «след лезвия» для них враньё. Им нужна своя форма — кольцо, всплеск, луч."
    },

    {
      kind: "head", id: "hit-layers", title: "Что происходит в момент попадания",
      lede:
        "Четыре слоя на одно событие, и каждый отвечает за своё. Количество искр и вес порезов идут " +
        "пропорционально урону, с потолком."
    },
    {
      kind: "split",
      items: [
        {
          id: "hit-full", status: "accepted", title: "Полная сборка", size: [740, 470],
          note: "Обе стадии на одном стенде: дуга на взмахе, затем серп, ядро, искры и порез.",
          draw: drawHitFull
        },
        {
          id: "hit-zone", status: "accepted", title: "Область, а не точка", size: [700, 470],
          note: "Удары бьют в площадь корпуса со смещением от того, кто бьёт: убийца заходит со спины, копейщик снизу. Отсюда и требование к барьеру — анимироваться в точку контакта.",
          draw: drawHitZone
        }
      ]
    },
    {
      kind: "stands",
      items: [
        {
          id: "sparks", status: "accepted", tag: "искры · 5% / 15% / 35% HP", title: "Два потока", size: [520, 300],
          note: "Быстрые уходят прочь по вектору за 0.15 с — это удар; медленные падают вниз за 0.4 с — это вскрытое. Разница читается даже когда цвета близки.",
          draw: drawSparkScale
        },
        {
          id: "cuts", status: "accepted", tag: "порезы · накопление и заживление", title: "Порезы как состояние", size: [520, 300],
          note: "Каждый удар пишет порез с запасом, равным снятому HP. Хил гасит яркость пропорционально: порез на 1000 при лечении на 400 тускнеет на 40%. Лимит на теле — 12.",
          draw: drawCutStand
        }
      ]
    },

    {
      kind: "head", id: "hybrids", title: "Гибриды: четыре способа смешать два цвета",
      lede:
        "Юнит с несколькими стихиями получает цвет на элемент, а удар вроде «огонь плюс режущий» несёт " +
        "два. Здесь один и тот же серп покрашен четырьмя способами — огонь и лёд взяты как самая " +
        "контрастная пара."
    },
    {
      kind: "stands",
      items: [
        {
          id: "along", status: "waiting", tag: "отложен, не отвергнут", title: "Вдоль формы", size: [480, 330],
          note: "От начала взмаха к концу проводки. Читается как процесс: удар начался одним, пришёл другим.",
          facts: [["плюс", "использует ось, которая уже есть"], ["минус", "на 4 кадрах может не прочитаться"]],
          draw: drawHybrid("along")
        },
        {
          id: "core", status: "accepted", title: "Ядро и кайма", size: [480, 330],
          decision: "2026-07-31/43",
          note: "Главный элемент светит в ядре, второй — по краю. Оба цвета видны одновременно в одном месте.",
          facts: [["плюс", "читается мгновенно, годится для 4 кадров"], ["минус", "кайма тонкая"]],
          verdict: "На форме длиной в четыре кадра одновременность надёжнее градиента вдоль: глазу не нужно успеть проследить ось.",
          draw: drawHybrid("core")
        },
        {
          id: "across", status: "waiting", tag: "отложен, не отвергнут", title: "Поперёк толщины", size: [480, 330],
          note: "Верхняя кромка одним цветом, нижняя другим. Похоже на раскалённую сталь с остывающим краем.",
          facts: [["плюс", "оба цвета равноправны"], ["минус", "толщина мала, цвета трутся"]],
          draw: drawHybrid("across")
        },
        {
          id: "layers", status: "waiting", tag: "отложен, не отвергнут", title: "Слоями", size: [480, 330],
          note: "Внешний слой первого цвета, средний второго, ядро всегда белое. Форма остаётся одна, но расслоена.",
          facts: [["плюс", "работает и на трёх стихиях"], ["минус", "ближе к «полосатому», чем к смеси"]],
          draw: drawHybrid("layers")
        }
      ]
    },
    {
      kind: "note",
      html:
        "Три остальных способа <b>не отвергнуты, а отложены</b> — дословно «про другие не забываем, " +
        "но пока ток его берем». Ядро при любом из них остаётся белым пересветом.<br><br>" +
        "Сам способ смешения стоит <b>закрепить за сочетанием</b> (огонь+режущий всегда смешивается " +
        "одинаково), а процедурно гонять только силу и направление градиента: если язык меняется от " +
        "удара к удару, игрок его не выучит — а язык тут и есть смысл."
    },

    {
      kind: "head", id: "ranged", title: "Дальний бой: та же система, только прямая",
      lede:
        "Выстрелу серп не нужен — у него нет взмаха, и рисовать ему дугу значило бы придумывать " +
        "движение, которого не было. Ему достаточно линии-всполоха: та же процедурная форма, что у " +
        "колющего, только A и B лежат вдоль вектора снаряда."
    },
    {
      kind: "split",
      items: [
        {
          id: "ranged-line", status: "accepted", title: "Линия-всполох после хита", size: [740, 420],
          decision: "2026-07-31/64",
          note: "Появляется <b>после попадания</b> и отвечает на один вопрос — откуда прилетело. До этого летит снаряд, и он приходит ровно на кадре контакта.",
          verdict: "Та же семья параметров, что у ближнего боя: меняются только прогиб (почти ноль) и то, откуда взята точка A.",
          draw: drawRanged
        }
      ]
    }
  ]
};

export default section;
