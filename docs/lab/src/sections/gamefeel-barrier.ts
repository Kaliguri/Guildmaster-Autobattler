/* Барьер: форма купола, узор трещин, стопка оболочек по типам, финалы и блок.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Барьер. */

import { tick } from "../clock.js";
import { ground, jag, miniLabel, RED, statusBody } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/* ---------- форма купола: круглая, без углов ---------- */

type DomeKind = "ellipse" | "dome" | "cocoon";

/** Одна параметризация на все три формы: контур, узор трещин и осколки обязаны жить в одной
 *  геометрии, иначе трещины пойдут мимо поверхности.
 *  n — показатель суперэллипса (2 = эллипс, больше = прямее бока), taper — сужение к верху. */
const DOME: Record<DomeKind, { n: number; taper: number; label: string }> = {
  ellipse: { n: 2.0, taper: 0.0, label: "эллипс" },
  dome: { n: 2.15, taper: 0.26, label: "купол-овоид" },
  cocoon: { n: 2.7, taper: 0.1, label: "кокон" }
};

function domePoint(
  kind: DomeKind,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  a: number,
  r = 1
): { x: number; y: number } {
  const d = DOME[kind];
  const ca = Math.cos(a);
  const sa = Math.sin(a);
  // Суперэллипс: |x|^n + |y|^n = 1. При n=2 это обычный эллипс, углов нет ни при каком n.
  const k = Math.pow(Math.pow(Math.abs(ca), d.n) + Math.pow(Math.abs(sa), d.n), -1 / d.n);
  let x = ca * k;
  const y = sa * k;
  x *= 1 - d.taper * Math.max(0, -y); // сужение к верху: облегает голову
  return { x: cx + x * rx * r, y: cy + y * ry * r };
}

function domePath(
  ctx: CanvasRenderingContext2D,
  kind: DomeKind,
  cx: number,
  cy: number,
  rx: number,
  ry: number
): void {
  ctx.beginPath();
  const steps = 48;
  for (let i = 0; i <= steps; i++) {
    const p = domePoint(kind, cx, cy, rx, ry, (i / steps) * Math.PI * 2 - Math.PI / 2);
    if (i === 0) ctx.moveTo(p.x, p.y);
    else ctx.lineTo(p.x, p.y);
  }
  ctx.closePath();
}

/* ---------- узор трещин: заготовлен целиком, проявляется частями ---------- */

/** Трещины барьера — НЕ счётные события (в отличие от порезов на теле): у барьера смысл
 *  «насколько цел», то есть площадь, а не счёт. Поэтому узор генерируется целиком при рождении
 *  щита, а показ проявляет его долю. Заодно снимается лимит: двадцатый удар не вытесняет первую. */
const CRACK_SEGMENTS = 16;

interface CrackSeg {
  ang: number;
  pts: Array<{ r: number; a: number }>;
}

const patternCache = new Map<number, CrackSeg[]>();

function crackPattern(seed: number): CrackSeg[] {
  const cached = patternCache.get(seed);
  if (cached) return cached;

  const segs: CrackSeg[] = [];
  for (let i = 0; i < CRACK_SEGMENTS; i++) {
    const ang = (i / CRACK_SEGMENTS) * 360 + (jag(i, seed) - 0.5) * 18;
    const pts: Array<{ r: number; a: number }> = [];
    let r = 1;
    let a = (ang * Math.PI) / 180;
    pts.push({ r, a });
    const steps = 2 + Math.floor(jag(i, seed + 3) * 3);
    for (let s = 0; s < steps; s++) {
      a += (jag(i * 5 + s, seed + 7) - 0.5) * 0.9;
      r -= 0.16 + jag(i * 5 + s, seed + 11) * 0.2;
      if (r < 0.12) break;
      pts.push({ r, a });
    }
    segs.push({ ang, pts });
  }
  patternCache.set(seed, segs);
  return segs;
}

/** Порядок проявления идёт ОТ ТОЧЕК УДАРА, а не по индексу: узор остаётся связным, но помнит,
 *  откуда били. Без этого сегменты вылезали бы в произвольных местах. */
function crackOrder(segs: CrackSeg[], hitAngles: number[]): number[] {
  return segs
    .map((s, i) => {
      let best = 180;
      for (const hit of hitAngles) {
        const d = Math.abs(((s.ang - hit + 540) % 360) - 180);
        if (d < best) best = d;
      }
      return { i, score: best };
    })
    .sort((a, b) => a.score - b.score || a.i - b.i)
    .map((s) => s.i);
}

/** Геометрия узора монотонна (история), яркость — текущая целостность: сеть широкая и бледная
 *  читается как «его сильно били, но сейчас он крепкий». Убирать проявленное нельзя — трещины не
 *  заживают; поэтому добавленный щит гасит узор, а не стирает. */
function drawPattern(
  ctx: CanvasRenderingContext2D,
  kind: DomeKind,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  seed: number,
  shown: number,
  hitAngles: number[],
  color: string,
  alpha: number
): void {
  if (shown <= 0) return;
  const segs = crackPattern(seed);
  const order = crackOrder(segs, hitAngles.length ? hitAngles : [-90]);
  const count = Math.min(segs.length, Math.max(1, Math.round(shown * segs.length)));

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  for (let k = 0; k < count; k++) {
    const seg = segs[order[k] as number];
    if (!seg) continue;
    const edge = k / Math.max(1, count - 1); // последние проявленные ещё тонкие
    ctx.strokeStyle = `rgba(${color},${(alpha * (1 - edge * 0.35)).toFixed(3)})`;
    ctx.lineWidth = 1.7 - edge * 0.5;
    ctx.beginPath();
    seg.pts.forEach((pt, i) => {
      const q = domePoint(kind, cx, cy, rx, ry, pt.a, pt.r);
      if (i === 0) ctx.moveTo(q.x, q.y);
      else ctx.lineTo(q.x, q.y);
    });
    ctx.stroke();
  }
  ctx.restore();
}

/* ---------- поверхность купола: в покое силуэт, под удар волна ---------- */

/** Состояние оболочки глазами показа. hitAng === null значит «урон пришёл всюду» (тик эффекта). */
interface Glow {
  glow: number;
  hitAng: number | null;
  dot: boolean;
}

/** Яркость участка поверхности. В покое почти ноль (силуэт). Под удар пятно в точке контакта
 *  расходится волной: чем слабее остаточная вспышка, тем шире фронт. */
function domeGlow(angDeg: number, st: Glow, base: number): number {
  if (st.glow <= 0) return base;
  if (st.hitAng === null) return base + st.glow * 0.42; // урон эффектом: ровно по всей
  const d = Math.abs(((angDeg - st.hitAng + 540) % 360) - 180);
  const spread = 52 + (1 - Math.min(1, st.glow)) * 115;
  const near = Math.max(0, 1 - d / spread);
  return base + st.glow * (0.18 + 0.82 * near * near);
}

function domeSurface(
  ctx: CanvasRenderingContext2D,
  kind: DomeKind,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  color: string,
  st: Glow,
  base: number,
  groundY?: number
): void {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  if (groundY !== undefined) {
    // Низ подрезан землёй: купол стоит, а не парит.
    ctx.beginPath();
    ctx.rect(cx - rx * 2.5, cy - ry * 2.5, rx * 5, groundY - (cy - ry * 2.5));
    ctx.clip();
  }

  domePath(ctx, kind, cx, cy, rx, ry); // объём: едва-едва, тело важнее
  ctx.fillStyle = `rgba(${color},${(base * 0.55 + st.glow * 0.09).toFixed(3)})`;
  ctx.fill();

  const steps = 64; // контур сегментами, каждый со своей яркостью
  ctx.lineWidth = 1.8;
  for (let i = 0; i < steps; i++) {
    const a0 = (i / steps) * Math.PI * 2 - Math.PI / 2;
    const a1 = ((i + 1) / steps) * Math.PI * 2 - Math.PI / 2;
    const mid = (((a0 + a1) / 2) * 180) / Math.PI;
    const al = domeGlow(mid, st, base);
    const p0 = domePoint(kind, cx, cy, rx, ry, a0);
    const p1 = domePoint(kind, cx, cy, rx, ry, a1);
    ctx.strokeStyle = `rgba(${color},${Math.min(1, al * 2.4).toFixed(3)})`;
    ctx.beginPath();
    ctx.moveTo(p0.x, p0.y);
    ctx.lineTo(p1.x, p1.y);
    ctx.stroke();
  }

  if (st.glow > 0 && st.hitAng !== null) {
    // Само место контакта.
    const p = domePoint(kind, cx, cy, rx, ry, (st.hitAng * Math.PI) / 180);
    const pr = rx * (0.3 + 0.4 * st.glow);
    const g = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, pr);
    g.addColorStop(0, `rgba(236,250,255,${(0.7 * st.glow).toFixed(3)})`);
    g.addColorStop(1, `rgba(${color},0)`);
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(p.x, p.y, pr, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

/** Пробитие: скорлупа расходится дугами ПО УЗОРУ — узор задаёт линии разлома, поэтому осколки
 *  выглядят как куски именно этой поверхности, а не как случайные обломки.
 *  Разлёт РАДИАЛЬНЫЙ: каждый кусок уходит прочь от юнита, наружу по своему углу (решение Макса
 *  31.07.2026). Барьер лопается целиком, а не сдувается в сторону последнего удара. */
function domeShards(
  ctx: CanvasRenderingContext2D,
  kind: DomeKind,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  t: number,
  color: string
): void {
  const fade = Math.pow(1 - t, 1.7); // быстрое угасание: скорлупы уже нет
  const pieces = 10;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  ctx.lineWidth = 1.8;
  for (let i = 0; i < pieces; i++) {
    const a0 = (i / pieces) * Math.PI * 2 - Math.PI / 2;
    const a1 = ((i + 1) / pieces) * Math.PI * 2 - Math.PI / 2;
    const mid = (a0 + a1) / 2;
    const d = rx * 0.85 * t * (0.7 + jag(i, 82) * 0.6);
    const ox = Math.cos(mid) * d;
    const oy = Math.sin(mid) * d * (ry / rx); // наружу по своему углу, с той же сплюснутостью
    ctx.strokeStyle = `rgba(${color},${(0.9 * fade).toFixed(3)})`;
    ctx.beginPath();
    for (let s = 0; s <= 5; s++) {
      const p = domePoint(kind, cx, cy, rx, ry, a0 + (a1 - a0) * (s / 5));
      if (s === 0) ctx.moveTo(p.x + ox, p.y + oy);
      else ctx.lineTo(p.x + ox, p.y + oy);
    }
    ctx.stroke();
  }
  ctx.restore();
}

/* ---------- стопка оболочек: тип, очередь, все ситуации ---------- */

type ShellType = "plain" | "phys" | "mag";

const SHELL_COLOR: Record<ShellType, string> = {
  plain: "176,182,190",
  phys: "255,150,60",
  mag: "196,124,255"
};

/** Вложенность = очередь трат: школьные снаружи, обычный ближе к телу — именно так они и
 *  тратятся в симуляции (pre-damage раньше вычета общего пула). */
const SHELL_DEPTH: Record<ShellType, number> = { plain: 0, mag: 1, phys: 2 };

interface CaseEvent {
  /** Кадр цикла, на котором событие происходит. */
  f: number;
  dmg?: number;
  type?: ShellType;
  /** Угол точки контакта в градусах, -90 — макушка. */
  ang?: number;
  /** Урон эффектом: приходит всюду, точки контакта нет. */
  dot?: boolean;
  /** Блок: у оболочки есть жест подставленного щита. */
  gesture?: boolean;
  /** Оболочка этого типа истекает по времени. */
  expire?: ShellType;
  /** Поверх стопки приходит новая оболочка. */
  add?: { t: ShellType; nom: number };
}

interface CaseDef {
  cycle: number;
  shells: Array<{ t: ShellType; nom: number; stacks?: number; born?: number }>;
  ev: CaseEvent[];
  /** Подпись состояния, когда стандартная соврала бы. */
  note?: string;
}

const CASES = {
  calm: { cycle: 60, shells: [{ t: "plain", nom: 200 }], ev: [] },
  weak: {
    cycle: 80,
    shells: [{ t: "plain", nom: 200 }],
    ev: [{ f: 24, dmg: 25, type: "phys", ang: -168 }]
  },
  strong: {
    cycle: 80,
    shells: [{ t: "plain", nom: 200 }],
    ev: [{ f: 24, dmg: 120, type: "phys", ang: -22 }]
  },
  dot: {
    cycle: 80,
    shells: [{ t: "plain", nom: 200 }],
    ev: [
      { f: 22, dmg: 18, dot: true },
      { f: 52, dmg: 18, dot: true }
    ]
  },
  two: {
    cycle: 90,
    shells: [
      { t: "plain", nom: 150 },
      { t: "mag", nom: 120 }
    ],
    ev: [{ f: 26, dmg: 60, type: "mag", ang: -150 }]
  },
  three: {
    cycle: 100,
    shells: [
      { t: "plain", nom: 150 },
      { t: "mag", nom: 120 },
      { t: "phys", nom: 100 }
    ],
    ev: [{ f: 30, dmg: 55, type: "phys", ang: -40 }]
  },
  wrong: {
    cycle: 90,
    shells: [{ t: "mag", nom: 200 }],
    ev: [{ f: 28, dmg: 70, type: "phys", ang: -135 }]
  },
  stack: {
    cycle: 70,
    shells: [{ t: "mag", nom: 480, stacks: 4 }],
    ev: [{ f: 26, dmg: 90, type: "mag", ang: -95 }]
  },
  layer: {
    cycle: 130,
    shells: [
      { t: "plain", nom: 150 },
      { t: "phys", nom: 60 }
    ],
    ev: [
      { f: 24, dmg: 40, type: "phys", ang: -60 },
      { f: 54, dmg: 40, type: "phys", ang: -70 },
      { f: 90, dmg: 50, type: "phys", ang: -110 }
    ]
  },
  through: {
    cycle: 100,
    shells: [{ t: "plain", nom: 20 }],
    ev: [{ f: 30, dmg: 100, type: "phys", ang: -50 }]
  },
  expire: {
    cycle: 110,
    shells: [{ t: "plain", nom: 200 }],
    ev: [
      { f: 22, dmg: 70, type: "phys", ang: -150 },
      { f: 60, expire: "plain" }
    ]
  },
  addover: {
    cycle: 120,
    shells: [{ t: "plain", nom: 100 }],
    ev: [
      { f: 20, dmg: 80, type: "phys", ang: -140 },
      { f: 56, add: { t: "phys", nom: 200 } }
    ]
  },

  // Блок — тот же барьер, только на 0.4 с и с жестом: BlockComponent накладывает короткий щит,
  // который гасит тот самый удар. Узор проявиться не успевает, и это и есть различитель.
  block: {
    cycle: 96,
    shells: [{ t: "plain", nom: 60, born: 22 }],
    ev: [
      { f: 24, dmg: 45, type: "phys", ang: -150, gesture: true },
      { f: 36, expire: "plain" }
    ]
  },
  // Блок направленный: удар в заднюю полусферу его не будит вовсе, поэтому оболочка не
  // поднимается и урон идёт в тело. Жест честен всегда — он есть ровно там, где блок сработал.
  backstab: {
    cycle: 96,
    shells: [],
    note: "блок не сработал · со спины",
    ev: [{ f: 26, dmg: 60, type: "phys", ang: 25 }]
  }
} satisfies Record<string, CaseDef>;

type CaseKey = keyof typeof CASES;

interface Shell extends Glow {
  t: ShellType;
  rest: number;
  eaten: number;
  shown: number;
  hits: number[];
  broken: number;
  seed: number;
  stacks: number;
  gone: boolean;
  unborn: boolean;
  fade?: number;
}

interface CaseState {
  shells: Shell[];
  bodyHit: number;
  bodyAng: number;
  gestureAge: number;
  gestureAng: number;
}

function runCase(def: CaseDef, c: number): CaseState {
  const shells: Shell[] = def.shells.map((s, i) => ({
    t: s.t,
    rest: s.nom,
    eaten: 0,
    shown: 0,
    hits: [],
    glow: 0,
    hitAng: null,
    dot: false,
    broken: -1,
    seed: 31 + i * 7,
    stacks: s.stacks ?? 1,
    gone: false,
    unborn: c < (s.born ?? 0)
  }));

  let bodyHit = -1;
  let bodyAng = -90;
  let gestureAge = -1;
  let gestureAng = -90;

  /** Школьная оболочка своего типа тратится первой, обычная — потом. Тот же порядок, что в симуляции. */
  function shellFor(type: ShellType | undefined): Shell | null {
    const usable = (s: Shell): boolean => !s.gone && !s.unborn && s.rest > 0;
    return shells.find((s) => usable(s) && s.t === type) ?? shells.find((s) => usable(s) && s.t === "plain") ?? null;
  }

  for (const e of def.ev) {
    if (c < e.f) break;
    const age = c - e.f;

    if (e.add) {
      shells.push({
        t: e.add.t,
        rest: e.add.nom,
        eaten: 0,
        shown: 0,
        hits: [],
        glow: age < 10 ? 1 - age / 10 : 0,
        hitAng: null,
        dot: false,
        broken: -1,
        seed: 97,
        stacks: 1,
        gone: false,
        unborn: false
      });
      continue;
    }

    if (e.expire) {
      for (const s of shells) {
        if (s.t === e.expire) {
          s.gone = true;
          s.fade = Math.min(1, age / 22);
        }
      }
      continue;
    }

    if (e.dot) {
      // Урон всюду: трещин не оставляет.
      const target = shellFor("plain");
      if (target && e.dmg) {
        target.rest = Math.max(0, target.rest - e.dmg);
        target.eaten += e.dmg;
        if (age < 18) {
          const g = Math.exp(-age / 5) * 0.35;
          if (g > target.glow) {
            target.glow = g;
            target.dot = true;
            target.hitAng = null;
          }
        }
      }
      continue;
    }

    if (e.gesture && age < 16) {
      gestureAge = age;
      gestureAng = e.ang ?? -90;
    }

    const shell = shellFor(e.type);
    if (!shell) {
      if (age < 20) {
        bodyHit = age;
        bodyAng = e.ang ?? -90;
      }
      continue;
    }

    const dmg = e.dmg ?? 0;
    const absorbed = Math.min(shell.rest, dmg);
    shell.rest -= absorbed;
    shell.eaten += absorbed;
    shell.hits.push(e.ang ?? -90);
    shell.shown = Math.max(shell.shown, shell.eaten / (shell.eaten + shell.rest));
    if (age < 22) {
      const g = Math.exp(-age / 5.5) * Math.min(1, dmg / 100);
      if (g > shell.glow) {
        shell.glow = g;
        shell.hitAng = e.ang ?? -90;
        shell.dot = false;
      }
    }
    if (shell.rest <= 0) shell.broken = Math.min(1, age / 20);
    if (absorbed < dmg && age < 20) {
      // Остаток ушёл в тело.
      bodyHit = age;
      bodyAng = e.ang ?? -90;
    }
  }

  return { shells, bodyHit, bodyAng, gestureAge, gestureAng };
}

/** Жест блока: пластина щита подставляется В ТОЧКУ удара — направление берётся оттуда же, откуда
 *  точка попадания. На скелетном юните это базовая поза плюс Aim части-щита, не отдельный клип. */
function blockGesture(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  rx: number,
  ry: number,
  angDeg: number,
  age: number
): void {
  const k = age < 4 ? age / 4 : 1 - (age - 4) / 12; // быстро вышел, мягко ушёл
  if (k <= 0) return;
  const a = (angDeg * Math.PI) / 180;
  ctx.save();
  ctx.translate(cx + Math.cos(a) * rx * 0.72, cy + Math.sin(a) * ry * 0.72);
  ctx.rotate(a);
  ctx.fillStyle = `rgba(255,214,120,${(0.5 * k).toFixed(3)})`;
  ctx.fillRect(-2, -ry * 0.3, 4.5, ry * 0.6); // сама пластина
  ctx.strokeStyle = `rgba(255,236,190,${(0.85 * k).toFixed(3)})`;
  ctx.lineWidth = 1.6;
  ctx.beginPath();
  ctx.moveTo(2.5, -ry * 0.3);
  ctx.lineTo(2.5, ry * 0.3);
  ctx.stroke();
  ctx.restore();
}

/** «Очень-очень незаметный» в покое: барьер не имеет права мутить тело под собой. */
const SHIELD_BASE = 0.055;
const SHIELD_COL = "138,206,255";

function shellName(t: ShellType): string {
  return t === "plain" ? "обычный" : t === "phys" ? "физ" : "маг";
}

function drawCase(key: CaseKey): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 76);
    const def: CaseDef = CASES[key];
    const c = tick % def.cycle;
    const st = runCase(def, c);

    const groundY = h - 76;
    const bodyH = 132;
    const x = w / 2;
    const u = bodyH / 16;
    const cx = x;
    const cy = groundY - bodyH * 0.52;
    const rx0 = u * 5.5;
    const ry0 = bodyH * 0.68;

    statusBody(ctx, x, groundY, bodyH);

    // Порез на теле есть только там, где урон дошёл до тела.
    if (st.bodyHit >= 0) {
      const k = 1 - st.bodyHit / 20;
      const a = (st.bodyAng * Math.PI) / 180;
      const px = cx + Math.cos(a) * u * 1.6;
      const py = cy + Math.sin(a) * u * 1.2;
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      ctx.strokeStyle = `rgba(${RED},${(0.75 * k + 0.25).toFixed(3)})`;
      ctx.lineWidth = 3;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(px - Math.cos(a) * u * 1.4, py - Math.sin(a) * u * 1.4);
      ctx.lineTo(px + Math.cos(a) * u * 1.4, py + Math.sin(a) * u * 1.4);
      ctx.stroke();
      ctx.restore();
    }

    for (const shell of st.shells) {
      if (shell.unborn) continue; // блок: оболочки ещё нет, щит не поднят
      const depth = SHELL_DEPTH[shell.t];
      const rx = rx0 * (1 + depth * 0.075);
      const ry = ry0 * (1 + depth * 0.075);
      const color = SHELL_COLOR[shell.t];
      const base = SHIELD_BASE * (0.8 + 0.25 * Math.min(3, shell.stacks)); // стаки типа: толще кайма

      if (shell.gone) {
        const f = 1 - (shell.fade ?? 0);
        if (f <= 0.02) continue;
        ctx.save();
        ctx.globalAlpha = f;
        domeSurface(
          ctx, "ellipse", cx, cy, rx * (1 + 0.1 * (1 - f)), ry * (1 + 0.1 * (1 - f)), color,
          { glow: 0, hitAng: null, dot: false }, base * f, groundY + u * 0.8
        );
        ctx.restore();
        continue;
      }

      if (shell.broken >= 0) {
        if (shell.broken < 1) domeShards(ctx, "ellipse", cx, cy, rx, ry, shell.broken, color);
        continue;
      }

      domeSurface(ctx, "ellipse", cx, cy, rx, ry, color, shell, base, groundY + u * 0.8);
      // Яркость узора = текущая целостность; геометрия — история. Широко и бледно = «били, но крепок».
      const integrity = shell.rest / Math.max(1e-4, shell.rest + shell.eaten);
      drawPattern(
        ctx, "ellipse", cx, cy, rx, ry, shell.seed, shell.shown, shell.hits, color,
        0.12 + 0.5 * (1 - integrity) + shell.glow * 0.4
      );
    }

    if (st.gestureAge >= 0) blockGesture(ctx, cx, cy, rx0 * 1.1, ry0 * 1.1, st.gestureAng, st.gestureAge);

    caseReadout(ctx, w, h, st, def);
  };
}

/** Что именно происходит в этом кадре: без подписи четырнадцать ситуаций не отличить друг от друга. */
function caseReadout(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  st: CaseState,
  def: CaseDef
): void {
  const alive = st.shells.filter((s) => !s.gone && !s.unborn && s.broken < 0);
  const busy = alive.find((s) => s.glow > 0.03);

  let line: string;
  if (st.gestureAge >= 0) line = "БЛОК · щит в точку удара";
  else if (st.shells.some((s) => s.broken >= 0 && s.broken < 1)) line = "ПРОБИТ";
  else if (st.shells.some((s) => s.gone && (s.fade ?? 0) < 1)) line = "РАЗВЕЯЛСЯ";
  else if (busy && def.note) line = def.note;
  else if (busy) line = busy.dot ? "тик эффекта · всюду слабее" : `поглощает · ${shellName(busy.t)}`;
  else if (st.bodyHit >= 0) line = "урон дошёл до ТЕЛА";
  else if (alive.length === 0) line = "барьера нет";
  else line = `${alive.length} ${alive.length === 1 ? "оболочка" : "оболочки"} · покой`;

  ctx.font = "500 12px ui-monospace, Consolas, monospace";
  ctx.fillStyle = line.startsWith("БЛОК")
    ? "rgba(255,214,120,1)"
    : line.startsWith("ПРОБИТ")
      ? "rgba(255,146,48,1)"
      : line.startsWith("РАЗВЕЯЛСЯ")
        ? "rgba(147,128,94,1)"
        : busy
          ? "rgba(232,220,196,1)"
          : "rgba(147,128,94,.8)";
  ctx.fillText(line, 18, h - 28);

  const shownPct = Math.round(100 * Math.max(0, ...st.shells.map((s) => s.shown)));
  ctx.fillStyle = "rgba(147,128,94,.75)";
  ctx.fillText(`узор ${shownPct}%`, w - 84, h - 28);
}

/* ---------- барьер целиком: постоянный, но тихий ---------- */

/** Жизнь барьера одним циклом: пассив, слабый удар, тик DoT, два сильных, пробитие, пауза.
 *  str — доля поглощённого урона; dot бьёт «всюду», поэтому своей точки не имеет. */
const SHIELD_EVENTS = [
  { f: 34, str: 0.28, ang: -168, dot: false, breaks: false },
  { f: 66, str: 0.14, ang: -90, dot: true, breaks: false },
  { f: 98, str: 0.62, ang: -22, dot: false, breaks: false },
  { f: 132, str: 0.55, ang: -142, dot: false, breaks: false },
  { f: 166, str: 1.0, ang: -64, dot: false, breaks: true }
];
const SHIELD_CYCLE = 232;

interface ShieldState extends Glow {
  cracks: number;
  broken: number;
  shown: number;
  hits: number[];
}

function shieldState(): ShieldState {
  const c = tick % SHIELD_CYCLE;
  let glow = 0;
  let hitAng: number | null = null;
  let cracks = 0;
  let dot = false;
  let broken = -1;
  const hits: number[] = [];
  const hitting = SHIELD_EVENTS.filter((e) => !e.dot).length;

  for (const e of SHIELD_EVENTS) {
    if (c < e.f) break;
    const age = c - e.f;
    if (!e.dot) {
      cracks++;
      hits.push(e.ang);
    }
    if (age < 22) {
      const k = Math.exp(-age / 5.5);
      if (e.str * k > glow) {
        glow = e.str * k;
        hitAng = e.dot ? null : e.ang;
        dot = e.dot;
      }
    }
    if (e.breaks) broken = Math.min(1, age / 22);
  }

  // Доля проявленного узора монотонна по ходу цикла: полный узор совпадает с пробитием.
  return { glow, hitAng, dot, cracks, broken, shown: hitting > 0 ? cracks / hitting : 0, hits };
}

function shieldReadout(ctx: CanvasRenderingContext2D, h: number, st: ShieldState): void {
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText("состояние", 26, h - 34);

  const state =
    st.broken >= 0 ? "пробит"
    : st.glow > 0.4 ? "сильный удар"
    : st.dot && st.glow > 0 ? "тик эффекта"
    : st.glow > 0 ? "слабый удар"
    : "покой";
  ctx.fillStyle =
    st.broken >= 0 ? "rgba(255,146,48,1)"
    : st.glow > 0.4 ? "rgba(230,248,255,1)"
    : st.glow > 0 ? `rgba(${SHIELD_COL},1)`
    : "rgba(147,128,94,.75)";
  ctx.fillText(state, 150, h - 34);

  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(`трещин ${Math.min(st.cracks, SHIELD_EVENTS.length)}`, 300, h - 34);
}

function drawSurface(kind: DomeKind): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 92);
    const groundY = h - 92;
    const bodyH = 168;
    const x = w / 2;
    const u = bodyH / 16;
    const st = shieldState();

    statusBody(ctx, x, groundY, bodyH);

    const cx = x;
    const cy = groundY - bodyH * 0.52;
    const rx = u * 5.5;
    const ry = bodyH * 0.68;

    if (st.broken < 0) {
      domeSurface(ctx, kind, cx, cy, rx, ry, SHIELD_COL, st, SHIELD_BASE, groundY + u * 0.8);
      drawPattern(ctx, kind, cx, cy, rx, ry, 31, st.shown, st.hits, SHIELD_COL,
        0.14 + st.shown * 0.5 + st.glow * 0.4);
    } else if (st.broken < 1) {
      domeShards(ctx, kind, cx, cy, rx, ry, st.broken, SHIELD_COL);
    }

    shieldReadout(ctx, h, st);
    miniLabel(ctx, DOME[kind].label);
  };
}

const BREAK_CYCLE = 96;

const drawBreak: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 74);
  const groundY = h - 74;
  const bodyH = 150;
  const x = w / 2;
  const u = bodyH / 16;
  const c = tick % BREAK_CYCLE;
  const cx = x;
  const cy = groundY - bodyH * 0.52;
  const rx = u * 5.5;
  const ry = bodyH * 0.68;

  statusBody(ctx, x, groundY, bodyH);

  if (c < 30) {
    // Целый, с трещинами от прошлых ударов.
    const st: Glow = { glow: c > 24 ? (c - 24) / 6 : 0, hitAng: -64, dot: false };
    domeSurface(ctx, "ellipse", cx, cy, rx, ry, SHIELD_COL, st, SHIELD_BASE, groundY + u * 0.8);
    drawPattern(ctx, "ellipse", cx, cy, rx, ry, 31, 0.95, [-64, -150, -20], SHIELD_COL, 0.6 + st.glow * 0.4);
  } else if (c < 56) {
    domeShards(ctx, "ellipse", cx, cy, rx, ry, (c - 30) / 26, SHIELD_COL);
  }

  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = c >= 30 && c < 56 ? "rgba(255,146,48,1)" : "rgba(147,128,94,.9)";
  ctx.fillText(
    c < 30 ? "трещин 4 · держится" : c < 56 ? "ПРОБИТ · осколки прочь от юнита" : "барьера нет",
    26, h - 34
  );
};

const FADE_CYCLE = 96;

const drawFade: DrawFn = (ctx, w, h) => {
  ground(ctx, w, h, 74);
  const groundY = h - 74;
  const bodyH = 150;
  const x = w / 2;
  const u = bodyH / 16;
  const c = tick % FADE_CYCLE;
  const cx = x;
  const cy = groundY - bodyH * 0.52;
  const rx = u * 5.5;
  const ry = bodyH * 0.68;

  statusBody(ctx, x, groundY, bodyH);

  if (c < 60) {
    const t = c < 30 ? 0 : (c - 30) / 30; // развеивание: ровно, без направления, вверх
    ctx.save();
    ctx.globalAlpha = 1 - t;
    ctx.translate(0, -ry * 0.35 * t);
    domeSurface(ctx, "ellipse", cx, cy, rx, ry * (1 + 0.12 * t), SHIELD_COL,
      { glow: 0, hitAng: null, dot: false }, SHIELD_BASE * (1 - t * 0.4), groundY + u * 0.8);
    drawPattern(ctx, "ellipse", cx, cy, rx, ry, 31, 0.35, [-150], SHIELD_COL, 0.3 * (1 - t));
    ctx.restore();
  }

  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(
    c < 30 ? "трещин 2 · время идёт" : c < 60 ? "РАЗВЕЯЛСЯ · ровно, вверх, без осколков" : "барьера нет",
    26, h - 34
  );
};

/* ---------- сборка раздела ---------- */

const CASE_CARDS: Array<[CaseKey, string, string]> = [
  ["calm", "Покой", "Барьер есть, но его почти не видно. Тело под ним читается полностью — иначе канал «Тело» потерян."],
  ["weak", "Слабый удар", "Подсветка от поглощённого урона, пятно в точке контакта. Одна трещина."],
  ["strong", "Сильный удар", "То же событие, больше урона — ярче и трещина крупнее. Сила читается яркостью, а не формой."],
  ["dot", "Тик эффекта", "Урон не в место, а всюду: вспышка по всей поверхности, но слабее. Трещин не оставляет."],
  ["two", "Два типа", "Маг снаружи, обычный внутри. Магический удар ест маговую — вспыхивает ровно она."],
  ["three", "Три типа", "Предел стопки. Физический удар проходит маговую насквозь, не задев её: типы не конкурируют."],
  ["wrong", "Не тот тип", "Магощит против физического удара: не реагирует вовсе. Молчание оболочки — тоже информация."],
  ["stack", "Четыре одного типа", "Одна оболочка, кайма толще по ступеням. Четыре каймы были бы шумом без нового смысла."],
  ["layer", "Внешняя пробита", "Лопнула, осколки разлетелись прочь от юнита и погасли — и открылась следующая. Дальше бьют её."],
  ["through", "Насквозь", "Щита 20, удар 100: барьер лопнул, остаток ушёл в тело. Форма НЕ обрывается, порез есть."],
  ["expire", "Развеялся", "Истёк по времени: ровно, вверх, без осколков. Трещины уходят вместе с ним."],
  ["addover", "Новый поверх битого", "Треснувший остаётся треснувшим, свежий приходит целым слоем снаружи. Трещины не «заживают»."],
  ["block", "Блок щитом", "Тот же барьер, но живёт 0.4 с и приходит с жестом: щит подставляется в точку удара. Узор проявиться не успевает — мигнул и всё."],
  ["backstab", "Блок со спины", "Блок направленный: удар в заднюю полусферу его не будит вовсе. Оболочка не поднимается, урон идёт в тело — и жест честен всегда."]
];

const caseStands: StandDef[] = CASE_CARDS.map(([key, title, note]) => ({
  id: `case-${key}`,
  status: "note",
  tag: "ситуация",
  title,
  note,
  draw: drawCase(key)
}));

const section: SectionDef = {
  id: "barrier",
  title: "Барьер",
  eyebrow: "Лаборатория · джус · барьер",
  lede:
    "Поглощающий барьер (<code>EffectTag.Shield</code>) висит постоянно, но в покое почти невидим: " +
    "пока по нему не бьют, он не имеет права мутить тело под собой. Подсветка растёт от поглощённого " +
    "урона и сильнее всего в точке контакта; урон эффектом вспыхивает по всей поверхности, но слабее " +
    "— бьют не в место, а всюду. Остаток читается трещинами.",

  blocks: [
    {
      kind: "head",
      id: "surface",
      title: "Поверхность: три круглые формы",
      lede:
        "Форма круглая, без углов — гранёные и стеклянно-фасетные варианты отклонены. Из трёх круглых " +
        "принят эллипс, с заметным зазором до тела: барьер окружает юнита, а не облегает."
    },
    {
      kind: "text",
      html:
        "Это не блок щитом-предметом (тот решён 31.07: вспышка золотом, серп обрывается о щит, пореза " +
        "нет). Здесь — поглощающий барьер, <code>EffectTag.Shield</code>. В покое видно только силуэт — " +
        "тонкую линию контура; подсветка приходит с ударом, пятном в точке контакта, и расходится " +
        "волной по поверхности."
    },
    {
      kind: "stands",
      items: [
        {
          id: "ellipse",
          status: "accepted",
          title: "Эллипс",
          size: [480, 380],
          decision: "2026-07-31/86",
          note: "Вытянутый по высоте сфероид, симметричный. Самая нейтральная из круглых форм — и самая понятная как «силовое поле».",
          facts: [["полуоси", "0.34 H · 0.68 H"], ["центр", "0.52 H от земли"], ["низ", "подрезан землёй"]],
          verdict: "Зазор до тела заметный — барьер читается как объём вокруг юнита, а не как обводка по силуэту.",
          draw: drawSurface("ellipse")
        },
        {
          id: "dome-ovoid",
          status: "rejected",
          title: "Купол-овоид",
          size: [480, 380],
          note: "Тот же сфероид, но сужается к верху. Обнимает силуэт плотнее — и именно поэтому проиграл: барьеру полагается быть просторнее тела.",
          facts: [["сужение", "к макушке на 26%"], ["минус", "облегает, а не окружает"]],
          draw: drawSurface("dome")
        },
        {
          id: "cocoon",
          status: "rejected",
          title: "Кокон",
          size: [480, 380],
          note: "Суперэллипс: бока почти прямые, углов нет ни одного. Плотный и аккуратный, но читается как футляр, а не как силовое поле.",
          facts: [["показатель", "2.7 вместо 2"], ["минус", "прямые бока"]],
          draw: drawSurface("cocoon")
        }
      ]
    },

    {
      kind: "head",
      id: "stack-cases",
      title: "Стопка: типы, очередь, все ситуации",
      lede:
        "Оболочка — на тип, не на эффект: четыре налёта Антимага дают одну фиолетовую, потому что " +
        "игроку нужно «магией не пробить», а не «какой из четырёх сейчас тает». Значит оболочек " +
        "максимум три, и вложенность у них постоянная — школьные снаружи, обычный ближе к телу, " +
        "потому что именно так они и тратятся в симуляции."
    },
    {
      kind: "text",
      html:
        "Трещины есть только у оболочек, которые <b>реально ели урон</b>: пока внешняя цела, до " +
        "внутренней урон не доходил."
    },
    {
      kind: "legend",
      items: [
        { color: "#B0B6BE", text: "обычный · держит всё" },
        { color: "#FF963C", text: "физический · только физику" },
        { color: "#C47CFF", text: "магический · только магию" }
      ]
    },
    { kind: "stands", items: caseStands },
    {
      kind: "note",
      html:
        "<b>Барьер не излишен при наличии блока — блок на нём и стоит.</b> <code>BlockComponent</code> " +
        "накладывает короткий щит-эффект, который гасит тот самый удар; барьер это механизм, блок — " +
        "один из способов его получить. Вард Антимага, водяной щит Монаха и щит от нехватки HP живут " +
        "вообще без жеста, а DoT будит барьер и не будит блок. Различитель в показе — <b>длительность " +
        "и жест</b>: мигнувшая оболочка читается как «отбил», висящая — как «под защитой».<br><br>" +
        "<b>Блок направленный, барьер — нет.</b> Блок проверяет фронтальный сектор 180° (тем же " +
        "правилом, что парирование), поэтому со спины не срабатывает. Барьер направления не знает и " +
        "гасит любой урон, включая тик яда и удар в спину, — просто без жеста."
    },

    {
      kind: "head",
      id: "endings",
      title: "Два финала",
      lede:
        "«Сломали» и «кончился» обязаны читаться по-разному — иначе игрок не поймёт, его защита не " +
        "выдержала или просто истекла."
    },
    {
      kind: "split",
      items: [
        {
          id: "break",
          status: "accepted",
          tag: "финал 1 · разрушение",
          title: "Пробит",
          size: [480, 330],
          decision: "2026-07-31/90",
          note: "Барьер <b>сломали</b>: трещины сходятся, поверхность расходится кусками <b>наружу от юнита</b> и быстро гаснет по прозрачности. Лопается целиком, а не сдувается в сторону.",
          draw: drawBreak
        },
        {
          id: "fade",
          status: "accepted",
          tag: "финал 2 · развеивание",
          title: "Развеялся",
          size: [480, 330],
          note: "Барьер <b>истёк</b>: гаснет ровно, без осколков и без направления, тает вверх.",
          draw: drawFade
        }
      ]
    }
  ]
};

export default section;
