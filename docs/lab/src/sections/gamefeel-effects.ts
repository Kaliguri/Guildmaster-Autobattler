/* Превью общих эффектов, кровь по DPS, жизнь эффекта.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Жизнь эффекта, §Кровотечение.

   Всё здесь красится СТАТУСОМ, а не палитрой бьющего: это язык боя, а не язык персонажа.
   Уникальные личные эффекты в сетку не входят намеренно — они берут цвет юнита. */

import { tick } from "../clock.js";
import {
  bubblesUp, DOT_WAVE, dotWave, facetOutline, ground, healInward, hitFlash, jag, RED,
  sparks, ST, statusBody, strokesFlow, unitPath
} from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";
import { burnFlames, markSign, slowPuddle, stunMarks } from "./gamefeel-status.js";

const EFF = {
  heal: "126,222,160",
  weaken: "158,146,178",
  empower: "214,150,255",
  knock: "255,232,160"
} as const;

/** Общая сцена превью: один размер на все, чтобы эффекты сравнивались, а не мерились рамками. */
const PW = 260;
const PH = 220;
const PGY = 164;
const PUH = 100;

const DOT_PERIOD = 30; // тик DoT раз в секунду

function effHead(ctx: CanvasRenderingContext2D, name: string): void {
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.fillText(name, 16, 22);
}

/* ---------- кровь: ступень считается по DPS ---------- */

/** Ступень крови — по суммарному DPS в ДОЛЯХ maxHP цели за секунду: абсолютные числа устарели бы с
 *  ростом статов за забег и по-разному читались бы на гоблине и на боссе. Число порезов — второй
 *  факт: сколько ран открыто. Одно не заменяет другое. */
const BLOOD_LEVELS = [
  { cuts: 2, drip: 0.02, size: 1.7, streaks: 0, pool: false },
  { cuts: 4, drip: 0.045, size: 2.0, streaks: 0, pool: false },
  { cuts: 6, drip: 0.075, size: 2.4, streaks: 3, pool: false },
  { cuts: 8, drip: 0.11, size: 2.9, streaks: 5, pool: true }
] as const;

interface CutBox { x: number; y: number; w: number; h: number }

function cutBox(x: number, groundY: number, h: number): CutBox {
  const u = h / 16;
  return { x: x - u * 2.6, y: groundY - h + u * 4.5, w: u * 5.2, h: u * 6.5 };
}

function bloodLevel(ctx: CanvasRenderingContext2D, x: number, groundY: number, h: number, level: number): void {
  const u = h / 16;
  const cfg = BLOOD_LEVELS[level - 1] ?? BLOOD_LEVELS[0];
  const box = cutBox(x, groundY, h);

  if (cfg.pool) {
    // Четвёртая ступень: лужа под ногами.
    const puddle = 0.75 + 0.25 * Math.sin(tick * 0.05);
    ctx.save();
    ctx.fillStyle = "rgba(120,18,18,.55)";
    ctx.beginPath();
    ctx.ellipse(x, groundY + u * 0.7, u * (2.4 + 0.5 * puddle), u * 0.85, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  for (let i = 0; i < cfg.cuts; i++) {
    const cx = box.x + box.w * (0.2 + jag(i, 161) * 0.6);
    const cy = box.y + box.h * (0.15 + jag(i, 162) * 0.7);
    const ang = ((-30 + jag(i, 163) * 60) * Math.PI) / 180;
    const half = h * 0.075;

    ctx.strokeStyle = `rgba(${RED},.5)`;
    ctx.lineWidth = 3.2;
    ctx.beginPath();
    ctx.moveTo(cx - Math.cos(ang) * half, cy - Math.sin(ang) * half);
    ctx.lineTo(cx + Math.cos(ang) * half, cy + Math.sin(ang) * half);
    ctx.stroke();
    ctx.strokeStyle = "rgba(255,190,190,.85)";
    ctx.lineWidth = 1.2;
    ctx.beginPath();
    ctx.moveTo(cx - Math.cos(ang) * half, cy - Math.sin(ang) * half);
    ctx.lineTo(cx + Math.cos(ang) * half, cy + Math.sin(ang) * half);
    ctx.stroke();

    if (i < cfg.streaks) {
      // Струйка вниз по телу.
      const len = u * (2.5 + jag(i, 164) * 2.5);
      const g = ctx.createLinearGradient(cx, cy, cx, cy + len);
      g.addColorStop(0, `rgba(${RED},.7)`);
      g.addColorStop(1, `rgba(${RED},0)`);
      ctx.strokeStyle = g;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.lineTo(cx + u * 0.2, cy + len);
      ctx.stroke();
    }

    const ph = (tick * cfg.drip + jag(i, 165)) % 1; // капля: темп растёт со ступенью
    if (ph < 0.92) {
      ctx.fillStyle = `rgba(${RED},${((1 - ph) * 0.85).toFixed(3)})`;
      ctx.beginPath();
      ctx.ellipse(cx, cy + (groundY - cy) * ph, cfg.size * 0.7, cfg.size, 0, 0, Math.PI * 2);
      ctx.fill();
    }
  }
  ctx.restore();

  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.fillText(`ран ${cfg.cuts}`, 16, PH - 24);
}

/* ---------- четыре ПОДХОДА к показу стадий крови ----------
   Один и тот же счётчик 1→4 по кругу у всех четырёх: сравниваются языки показа, а не тайминги. */

const BL_HOLD = 40;

function bloodStage(): number {
  return 1 + (Math.floor(tick / BL_HOLD) % 4);
}

/** Порезы у всех подходов одни и те же — меняется только то, ЧЕМ читается стадия. */
function bloodCuts(
  ctx: CanvasRenderingContext2D,
  x: number, groundY: number, h: number,
  count: number, bright: number, width: number
): CutBox {
  const box = cutBox(x, groundY, h);
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.lineCap = "round";
  for (let i = 0; i < count; i++) {
    const cx = box.x + box.w * (0.2 + jag(i, 161) * 0.6);
    const cy = box.y + box.h * (0.15 + jag(i, 162) * 0.7);
    const ang = ((-30 + jag(i, 163) * 60) * Math.PI) / 180;
    const half = h * 0.075;
    const tone = Math.round(150 + 60 * bright);

    ctx.strokeStyle = `rgba(${RED},${(0.5 * bright).toFixed(3)})`;
    ctx.lineWidth = 3.2 * width;
    ctx.beginPath();
    ctx.moveTo(cx - Math.cos(ang) * half, cy - Math.sin(ang) * half);
    ctx.lineTo(cx + Math.cos(ang) * half, cy + Math.sin(ang) * half);
    ctx.stroke();

    ctx.strokeStyle = `rgba(255,${tone},${tone},${(0.9 * bright).toFixed(3)})`;
    ctx.lineWidth = 1.2 * width;
    ctx.beginPath();
    ctx.moveTo(cx - Math.cos(ang) * half, cy - Math.sin(ang) * half);
    ctx.lineTo(cx + Math.cos(ang) * half, cy + Math.sin(ang) * half);
    ctx.stroke();
  }
  ctx.restore();
  return box;
}

function stageLabel(ctx: CanvasRenderingContext2D, stage: number, note: string): void {
  ctx.font = "500 12px ui-monospace, Consolas, monospace";
  ctx.fillStyle = stage === 4 ? "rgba(255,120,120,1)" : "rgba(147,128,94,.9)";
  ctx.fillText(`стадия ${stage} · ${note}`, 16, PH - 24);
}

type BloodMode = "drip" | "soak" | "glow" | "pulse";

const BLOOD_NOTES: Record<BloodMode, [string, string, string, string]> = {
  drip: ["редкие капли", "капает", "подтёки", "лужа"],
  soak: ["низ намок", "до пояса", "почти весь", "залит целиком"],
  glow: ["тусклые", "заметные", "горят", "раскалены"],
  pulse: ["редкий толчок", "ровный ритм", "частый", "колотится"]
};

function drawBloodApproach(mode: BloodMode): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 56);
    const x = w / 2;
    const groundY = PGY;
    const bodyH = PUH;
    const u = bodyH / 16;
    const st = bloodStage();
    const k = (st - 1) / 3; // 0 на первой стадии, 1 на четвёртой
    const notes = BLOOD_NOTES[mode];

    if (mode === "drip") {
      if (st === 4) {
        ctx.save();
        ctx.fillStyle = "rgba(120,18,18,.5)";
        ctx.beginPath();
        ctx.ellipse(x, groundY + u * 0.7, u * (2.2 + 0.4 * Math.sin(tick * 0.06)), u * 0.8, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }
      statusBody(ctx, x, groundY, bodyH);
      const box = bloodCuts(ctx, x, groundY, bodyH, 3 + st, 1, 1);

      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      for (let i = 0; i < 3 + st; i++) {
        const cx = box.x + box.w * (0.2 + jag(i, 161) * 0.6);
        const cy = box.y + box.h * (0.15 + jag(i, 162) * 0.7);
        if (st >= 3 && i < st) {
          // Подтёки с третьей стадии.
          const len = u * (2 + jag(i, 164) * 2.5);
          const g = ctx.createLinearGradient(cx, cy, cx, cy + len);
          g.addColorStop(0, `rgba(${RED},.7)`);
          g.addColorStop(1, `rgba(${RED},0)`);
          ctx.strokeStyle = g;
          ctx.lineWidth = 2;
          ctx.beginPath();
          ctx.moveTo(cx, cy);
          ctx.lineTo(cx + u * 0.2, cy + len);
          ctx.stroke();
        }
        const ph = (tick * (0.02 + 0.03 * k) + jag(i, 165)) % 1;
        if (ph < 0.92) {
          ctx.fillStyle = `rgba(${RED},${((1 - ph) * 0.85).toFixed(3)})`;
          ctx.beginPath();
          ctx.ellipse(cx, cy + (groundY - cy) * ph, 1.4 + k * 1.2, 1.8 + k * 1.4, 0, 0, Math.PI * 2);
          ctx.fill();
        }
      }
      ctx.restore();
      stageLabel(ctx, st, notes[st - 1] ?? "");
      return;
    }

    if (mode === "soak") {
      statusBody(ctx, x, groundY, bodyH);
      // Подтон намеренно СЛАБЫЙ: «не съедает силуэт, просто чуть красит». Плотная заливка забивала
      // бы силуэт, а он несёт класс и оружие — то есть более важный факт.
      ctx.save();
      unitPath(ctx, x, groundY, bodyH);
      ctx.clip();
      const top = groundY - bodyH * (0.15 + 0.72 * k);
      const g = ctx.createLinearGradient(0, groundY, 0, top);
      g.addColorStop(0, `rgba(150,26,26,${(0.24 + 0.26 * k).toFixed(3)})`);
      g.addColorStop(1, "rgba(150,26,26,0)");
      ctx.fillStyle = g;
      ctx.fillRect(x - u * 4, top, u * 8, groundY - top + u);
      ctx.restore();
      bloodCuts(ctx, x, groundY, bodyH, 4, 0.9, 1);
      stageLabel(ctx, st, notes[st - 1] ?? "");
      return;
    }

    if (mode === "glow") {
      statusBody(ctx, x, groundY, bodyH);
      const breath = 0.85 + 0.15 * Math.sin(tick * 0.12);
      bloodCuts(ctx, x, groundY, bodyH, 5, (0.35 + 0.65 * k) * breath, 0.8 + 0.9 * k);
      if (st >= 3) {
        // Ореол вокруг ран на верхних стадиях.
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        unitPath(ctx, x, groundY, bodyH);
        ctx.clip();
        const g = ctx.createRadialGradient(x, groundY - bodyH * 0.55, u, x, groundY - bodyH * 0.55, u * 5);
        g.addColorStop(0, `rgba(255,60,60,${(0.22 * k).toFixed(3)})`);
        g.addColorStop(1, "rgba(255,60,60,0)");
        ctx.fillStyle = g;
        ctx.fillRect(x - u * 4, groundY - bodyH, u * 8, bodyH);
        ctx.restore();
      }
      stageLabel(ctx, st, notes[st - 1] ?? "");
      return;
    }

    const speed = 0.06 + 0.1 * k; // пульс: чаще и глубже со стадией
    const pulse = Math.pow(Math.max(0, Math.sin(tick * speed)), 6);
    statusBody(ctx, x, groundY, bodyH);
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    unitPath(ctx, x, groundY, bodyH);
    ctx.clip();
    ctx.fillStyle = `rgba(200,30,30,${((0.12 + 0.5 * k) * pulse).toFixed(3)})`;
    ctx.fillRect(x - u * 4, groundY - bodyH, u * 8, bodyH);
    ctx.restore();
    bloodCuts(ctx, x, groundY, bodyH, 4, 0.8 + 0.2 * pulse, 1);
    stageLabel(ctx, st, notes[st - 1] ?? "");
  };
}

/* ---------- жизнь эффекта ----------
   Носитель формы во всех шести случаях один — горение: у него есть узнаваемый знак, и на нём видно,
   что именно происходит с ФОРМОЙ. Событие всегда на кадре 30, чтобы стенды сравнивались. */

const LIFE_CYCLE = 78;
const LIFE_AT = 30;
const CLEANSE_COL = "150,222,255"; // голубой: с тебя сняли ПЛОХОЕ
const DISPEL_COL = "96,84,118"; // тёмный: с тебя сорвали ХОРОШЕЕ

type LifeMode = "birth" | "stack" | "refresh" | "cleanse" | "dispel" | "expire";

function drawLifeCase(mode: LifeMode): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 56);
    const x = w / 2;
    const groundY = PGY;
    const bodyH = PUH;
    const c = tick % LIFE_CYCLE;
    const age = c - LIFE_AT;
    const lvl = 2 / 3;

    let color: string | undefined;
    let scale = 1;
    let alpha = 1;
    let show = true;
    let note = "";

    switch (mode) {
      case "birth":
        show = c >= LIFE_AT;
        if (age >= 0 && age < 8) {
          scale = 1.7 - 0.7 * (age / 8);
          alpha = age / 8;
          note = "НАЛОЖИЛСЯ";
        } else note = show ? "держится" : "эффекта нет";
        break;

      case "stack": // толчок: форма дёрнулась наружу
        if (age >= 0 && age < 7) {
          scale = 1 + 0.4 * Math.sin((Math.PI * age) / 7);
          note = "СТАК +1";
        } else note = "глубина держится";
        break;

      case "refresh": // волна по контуру, без роста
        note = age >= 0 && age < DOT_WAVE ? "ДЛИТЕЛЬНОСТЬ ОБНОВЛЕНА" : "тот же стак, срок сброшен";
        break;

      case "cleanse": // перекраска, ПОТОМ таяние
        if (age >= 0 && age < 3) { color = CLEANSE_COL; note = "КЛИНС · форма голубеет"; }
        else if (age >= 3 && age < 9) { color = CLEANSE_COL; alpha = 1 - (age - 3) / 6; note = "тает"; }
        else if (age >= 9) { show = false; note = "снято"; }
        else note = "горит";
        break;

      case "dispel": // темнеет и лопается, без таяния
        if (age >= 0 && age < 3) { color = DISPEL_COL; note = "ДИСПЕЛ · форма темнеет"; }
        else if (age >= 3 && age < 7) {
          color = DISPEL_COL;
          scale = 1 - 0.5 * ((age - 3) / 4);
          alpha = 1 - (age - 3) / 4;
          note = "срывается";
        } else if (age >= 7) { show = false; note = "сорвано"; }
        else note = "бафф держится";
        break;

      default: // истёк: ровное угасание в СВОЁМ цвете
        if (age >= 0 && age < 12) { alpha = 1 - age / 12; note = "истёк · гаснет"; }
        else if (age >= 12) { show = false; note = "нет эффекта"; }
        else note = "горит";
        break;
    }

    const bx = statusBody(ctx, x, groundY, bodyH, { burn: show && !color ? lvl * alpha : 0 });
    if (show) burnFlames(ctx, bx, groundY, bodyH, lvl, color, scale, alpha);
    if (mode === "refresh" && age >= 0 && age < DOT_WAVE) dotWave(ctx, bx, groundY, bodyH, ST.burn, age);

    ctx.font = "500 12px ui-monospace, Consolas, monospace";
    const hot = note === note.toUpperCase() && note.length > 3;
    ctx.fillStyle = hot
      ? mode === "cleanse" ? `rgba(${CLEANSE_COL},1)`
        : mode === "dispel" ? "rgba(178,160,205,1)"
          : "rgba(255,220,150,1)"
      : "rgba(147,128,94,.85)";
    ctx.fillText(note, 16, PH - 24);
  };
}

/* ---------- превью общих эффектов ---------- */

type EffectKey =
  | "stun" | "sleep" | "frozen" | "knockup"
  | "burn" | "ember" | "frostbite" | "stealth"
  | "mark" | "empower" | "hot" | "antiheal" | "weaken"
  | "slow" | "root"
  | "blood1" | "blood2" | "blood3" | "blood4"
  | "wave-poison" | "wave-burn" | "wave-hit";

function drawEffect(key: EffectKey): DrawFn {
  return (ctx, w, h) => {
    ground(ctx, w, h, 56);
    const x = w / 2;
    const groundY = PGY;
    const bodyH = PUH;
    const u = bodyH / 16;

    switch (key) {
      case "stun": {
        const bx = statusBody(ctx, x, groundY, bodyH, { stun: true });
        stunMarks(ctx, bx, groundY, bodyH);
        effHead(ctx, "стан");
        return;
      }
      case "sleep": {
        const pulse = 0.5 + 0.5 * Math.sin(tick * 0.055);
        ctx.save();
        ctx.globalAlpha = 0.78 + 0.22 * pulse;
        statusBody(ctx, x, groundY, bodyH * 0.88);
        ctx.restore();
        bubblesUp(ctx, x, groundY - bodyH * 0.55, bodyH * 0.5, "220,214,255", 0.01, 5, 1.7);
        effHead(ctx, "сон");
        return;
      }
      case "frozen": {
        // Анимации нет намеренно: соседние превью шевелятся, и стоп-кадр читается сам.
        statusBody(ctx, x, groundY, bodyH, { frostbite: 1 });
        facetOutline(ctx, x, groundY - bodyH * 0.5, u * 4.2, bodyH * 0.58, ST.frost, 0.55);
        effHead(ctx, "заморозка · анимация стоп");
        return;
      }
      case "knockup": {
        const lift = u * (2.6 + Math.sin(tick * 0.09) * 0.5);
        ctx.save();
        ctx.translate(0, -lift);
        statusBody(ctx, x, groundY, bodyH);
        ctx.restore();
        ctx.fillStyle = "rgba(0,0,0,.30)"; // тень поджалась: юнит выше
        ctx.beginPath();
        ctx.ellipse(x, groundY + u * 0.6, u * 2.1, u * 0.65, 0, 0, Math.PI * 2);
        ctx.fill();
        for (let i = 0; i < 7; i++) {
          // Пыль осталась внизу.
          const ph = (tick * 0.045 + jag(i, 111)) % 1;
          const px = x + (jag(i, 112) - 0.5) * u * 6 * (0.4 + ph);
          ctx.fillStyle = `rgba(${EFF.knock},${((1 - ph) * 0.4).toFixed(3)})`;
          ctx.beginPath();
          ctx.arc(px, groundY - u * 0.3, 1.7 * (1 - ph * 0.6), 0, Math.PI * 2);
          ctx.fill();
        }
        effHead(ctx, "подкидывание");
        return;
      }
      case "burn": {
        const bx = statusBody(ctx, x, groundY, bodyH, { burn: 2 / 3 });
        burnFlames(ctx, bx, groundY, bodyH, 2 / 3);
        effHead(ctx, "горение · ступень 2");
        return;
      }
      case "ember": {
        const bx = statusBody(ctx, x, groundY, bodyH, { burn: 0.2 });
        ctx.save();
        unitPath(ctx, bx, groundY, bodyH);
        ctx.clip();
        ctx.globalCompositeOperation = "lighter";
        for (let i = 0; i < 14; i++) {
          // Запас ВНУТРИ тела: его тратит детонация.
          const t = tick * 0.06 + i * 1.7;
          const a = 0.35 + 0.55 * (0.5 + 0.5 * Math.sin(t));
          const px = bx + (jag(i, 121) - 0.5) * u * 4.6;
          const py = groundY - u * (1.5 + jag(i, 122) * 12);
          ctx.fillStyle = `rgba(255,${Math.round(180 + 50 * a)},120,${a.toFixed(3)})`;
          ctx.beginPath();
          ctx.arc(px, py, 1.5, 0, Math.PI * 2);
          ctx.fill();
        }
        ctx.restore();
        effHead(ctx, "угли");
        return;
      }
      case "frostbite": {
        const fill = 0.35 + 0.3 * (0.5 + 0.5 * Math.sin(tick * 0.03));
        statusBody(ctx, x, groundY, bodyH, { frostbite: fill });
        effHead(ctx, "изморозь · стаки растут");
        return;
      }
      case "stealth": {
        ctx.save();
        ctx.globalAlpha = 0.3;
        statusBody(ctx, x, groundY, bodyH);
        ctx.restore();
        ctx.save(); // контур: союзник должен видеть, где он
        ctx.strokeStyle = "rgba(190,200,220,.55)";
        ctx.lineWidth = 1.2;
        unitPath(ctx, x, groundY, bodyH);
        ctx.stroke();
        ctx.restore();
        effHead(ctx, "скрытность");
        return;
      }
      case "mark": {
        statusBody(ctx, x, groundY, bodyH);
        markSign(ctx, x, groundY, bodyH);
        effHead(ctx, "метка");
        return;
      }
      case "empower": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        strokesFlow(ctx, bx, groundY, bodyH, EFF.empower, true, 0.02, 9, 1.6, 0.75);
        ctx.save(); // свечение части-носителя приёма
        ctx.globalCompositeOperation = "lighter";
        unitPath(ctx, bx, groundY, bodyH);
        ctx.clip();
        const g = ctx.createLinearGradient(0, groundY - bodyH, 0, groundY - bodyH * 0.4);
        g.addColorStop(0, `rgba(${EFF.empower},.45)`);
        g.addColorStop(1, `rgba(${EFF.empower},0)`);
        ctx.fillStyle = g;
        ctx.fillRect(bx - u * 3, groundY - bodyH, u * 6, bodyH);
        ctx.restore();
        effHead(ctx, "усиление");
        return;
      }
      case "hot": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        // Вектор ВНУТРЬ: яд выделяет, лечение собирает. Один канал, два противоположных смысла —
        // иначе «его травят» и «его лечат» сливаются в одно зелёное пятно.
        healInward(ctx, bx, groundY, bodyH, EFF.heal, true);
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        unitPath(ctx, bx, groundY, bodyH);
        ctx.clip();
        ctx.fillStyle = `rgba(${EFF.heal},.16)`; // тело чуть отзывается на приход
        ctx.fillRect(bx - u * 3, groundY - bodyH, u * 6, bodyH);
        ctx.restore();
        effHead(ctx, "лечение во времени · внутрь");
        return;
      }
      case "antiheal": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        // Тот же вектор внутрь, но искры НЕ доходят: гаснут на подлёте и осыпаются.
        healInward(ctx, bx, groundY, bodyH, EFF.heal, false);
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        ctx.strokeStyle = `rgba(${EFF.weaken},.42)`;
        ctx.setLineDash([4, 6]);
        ctx.lineWidth = 1.3;
        ctx.beginPath();
        ctx.ellipse(bx, groundY - bodyH * 0.52, u * 4.4, bodyH * 0.56, 0, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.restore();
        effHead(ctx, "антихил · не доходит");
        return;
      }
      case "weaken": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        strokesFlow(ctx, bx, groundY, bodyH, EFF.weaken, false, 0.014, 8, 1.6, 0.55);
        ctx.save(); // тело гаснет сверху: зеркало усиления
        ctx.globalCompositeOperation = "multiply";
        unitPath(ctx, bx, groundY, bodyH);
        ctx.clip();
        ctx.fillStyle = "rgba(120,116,132,.55)";
        ctx.fillRect(bx - u * 3, groundY - bodyH, u * 6, bodyH);
        ctx.restore();
        effHead(ctx, "ослабление");
        return;
      }
      case "slow": {
        slowPuddle(ctx, x, groundY, bodyH);
        statusBody(ctx, x, groundY, bodyH);
        effHead(ctx, "замедление");
        return;
      }
      case "root": {
        statusBody(ctx, x, groundY, bodyH);
        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        const grip = 0.75 + 0.25 * Math.sin(tick * 0.05); // дуги смыкаются, а не растекаются
        for (let i = 0; i < 5; i++) {
          const side = i % 2 === 0 ? -1 : 1;
          const r = u * (1.4 + i * 0.55);
          ctx.strokeStyle = `rgba(${ST.slow},${(0.65 - i * 0.09).toFixed(3)})`;
          ctx.lineWidth = 2;
          ctx.beginPath();
          ctx.ellipse(x + side * u * 0.5, groundY - u * 0.6 * grip, r, r * 0.85,
            side * 0.4, Math.PI * 0.95, Math.PI * 2.05);
          ctx.stroke();
        }
        ctx.restore();
        effHead(ctx, "корни");
        return;
      }
      case "wave-poison": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        const wave = tick % DOT_PERIOD;
        dotWave(ctx, bx, groundY, bodyH, ST.poison, wave < DOT_WAVE ? wave : -1);
        effHead(ctx, wave < DOT_WAVE ? "тик яда" : "между тиками · чисто");
        return;
      }
      case "wave-burn": {
        const bx = statusBody(ctx, x, groundY, bodyH, { burn: 2 / 3 });
        burnFlames(ctx, bx, groundY, bodyH, 2 / 3);
        const wave = tick % DOT_PERIOD;
        dotWave(ctx, bx, groundY, bodyH, ST.burn, wave < DOT_WAVE ? wave : -1);
        effHead(ctx, "языки держатся, волна на тике");
        return;
      }
      case "wave-hit": {
        const bx = statusBody(ctx, x, groundY, bodyH);
        const hit = tick % DOT_PERIOD;
        hitFlash(ctx, bx, groundY, bodyH, hit < 4 ? hit : -1);
        if (hit < 6) sparks(ctx, bx, groundY - bodyH * 0.55, -20, 0.12, hit, bodyH, 7);
        effHead(ctx, hit < 4 ? "ВСПЫШКА · 3 кадра" : "удар · для сравнения");
        return;
      }
      default: {
        // Кровь: ступень зашита в ключ, чтобы четыре уровня стояли рядом и сравнивались.
        const bx = statusBody(ctx, x, groundY, bodyH);
        const lvl = Number(key.slice(-1));
        bloodLevel(ctx, bx, groundY, bodyH, lvl);
        const wave = tick % DOT_PERIOD;
        dotWave(ctx, bx, groundY, bodyH, RED, wave < DOT_WAVE ? wave : -1);
        effHead(ctx, `кровь · ступень ${lvl}`);
      }
    }
  };
}

/* ---------- сборка раздела ---------- */

const SIZE: [number, number] = [PW, PH];

function preview(id: EffectKey, title: string, note: string, status: StandDef["status"] = "note"): StandDef {
  return { id, status, title, note, size: SIZE, draw: drawEffect(id) };
}

const section: SectionDef = {
  id: "effects",
  title: "Эффекты",
  eyebrow: "Лаборатория · джус · словарь эффектов",
  lede:
    "Как выглядит каждый общий эффект — по каналам. Всё здесь красится <b>статусом</b>, а не палитрой " +
    "бьющего: это язык боя, а не язык персонажа. Уникальные личные эффекты в сетку не входят " +
    "намеренно — они берут цвет юнита и живут по своим правилам.",

  blocks: [
    {
      kind: "head", id: "will", title: "Канал воля · «кто сейчас не играет»",
      lede: "Отнятая дееспособность. Единственный канал, которому разрешён абстрактный знак над головой."
    },
    {
      kind: "stands",
      items: [
        preview("stun", "Стан", "Поза обвисла, тело дрожит, три штриха по орбите. Читается как «выключен и мучается»."),
        preview("sleep", "Сон", "Тело осело, медленный пульс, редкие точки вверх. «Выключен, но цел» — и снимается чужим уроном."),
        preview("frozen", "Заморозка", "Абсолютный стоп-кадр плюс гранёный контур. Отнятие движения, а не эффект поверх."),
        preview("knockup", "Подкидывание", "Тело над землёй, тень поджалась, пыль осталась внизу. Единственный статус, который меняет высоту.")
      ]
    },

    {
      kind: "head", id: "body", title: "Канал тело · «кто тает»",
      lede: "Материальное состояние: живёт строго внутри силуэта и не имеет права из него вылезать."
    },
    {
      kind: "stands",
      items: [
        preview("burn", "Горение", "Языки от контура вверх, тёплый подтон внутри силуэта. Три ступени по стакам."),
        preview("ember", "Угли", "Искры-точки ВНУТРИ силуэта, мерцают. Запас, который тратит детонация, — поэтому внутри, а не снаружи."),
        preview("frostbite", "Изморозь", "Налёт снизу вверх, высота = стаки. Дошёл до макушки — приехала Заморозка."),
        preview("stealth", "Скрытность", "Прозрачность и десатурация, контур остаётся. Союзник должен видеть, где он.")
      ]
    },
    {
      kind: "text",
      html:
        "<b>Яда и крови в этой сетке нет намеренно.</b> У яда нет состояния тела — только тик и дебафф, " +
        "поэтому постоянного знака ему не полагается; у крови ступени считаются по DPS, а не по стакам. " +
        "Оба разобраны отдельными блоками ниже."
    },

    {
      kind: "head", id: "outside", title: "Канал снаружи · «кто усилен, помечен, защищён»",
      lede: "Орбита и приходящее извне. Здесь же живёт барьер — у него свой раздел."
    },
    {
      kind: "stands",
      items: [
        preview("mark", "Метка", "Один знак над головой, медленное вращение. Орбита читается как «извне», а не «изнутри»."),
        preview("empower", "Усиление", "Восходящие штрихи и свечение части-носителя приёма — тот же язык, что у телеграфа каста."),
        preview("hot", "Лечение (HoT)", "Искры приходят <b>извне к телу</b>: яд выделяет, лечение собирает. Один канал, два противоположных смысла."),
        preview("antiheal", "Антихил", "Тот же вектор внутрь, но искры гаснут на подлёте и осыпаются, не доставая до тела."),
        preview("weaken", "Ослабление", "Зеркало усиления: штрихи идут вниз и тускло. Пара «усилен / ослаблен» обязана читаться вектором.")
      ]
    },

    {
      kind: "head", id: "earth", title: "Канал земля · «кто не дойдёт»",
      lede: "Под ногами. Работает и когда юнит стоит на месте — то, чего эхо-силуэты не умеют."
    },
    {
      kind: "stands",
      items: [
        preview("slow", "Замедление", "Вязкая клякса, тянется за юнитом. Работает и когда он стоит на месте."),
        preview("root", "Корни", "То же место, форма схватывающая: дуги смыкаются к ногам, а не растекаются.")
      ]
    },

    {
      kind: "head", id: "blood", title: "Кровь · четыре уровня по DPS",
      lede:
        "У кровотечения нет ступеней в механике — есть N независимых порций с разными сроками. Поэтому " +
        "ступень считается не по стакам, а по суммарному DPS в долях maxHP цели за секунду: тогда шкала " +
        "одинаково работает на гоблине и на боссе и не устаревает с ростом чисел за забег."
    },
    {
      kind: "stands",
      items: [
        preview("blood1", "1 · Сочится", "до 1% maxHP/с. Редкая капля из ближайшего пореза, тело чистое."),
        preview("blood2", "2 · Капает", "1–3%. Капли идут постоянно, из нескольких порезов сразу."),
        preview("blood3", "3 · Течёт", "3–8%. По телу идут струйки, капли чаще и крупнее."),
        preview("blood4", "4 · Хлещет", "выше 8% — смерть за десяток секунд от одной крови. Лужа под ногами и след при движении.")
      ]
    },

    {
      kind: "head", id: "blood-approaches", title: "Кровь · четыре подхода к показу стадий",
      lede:
        "Один и тот же счётчик стадий (1→4 по кругу), четыре разных языка показа. Вопрос не «какие " +
        "числа», а чем именно стадия читается: количеством, цветом тела, светом ран или ритмом."
    },
    {
      kind: "stands",
      items: [
        {
          id: "bl-drip", status: "rejected", title: "A · Капли и струйки", size: SIZE,
          note: "Растёт темп капания, потом добавляются подтёки, на четвёртой — лужа под ногами.",
          verdict: "Мелкая графика: в свалке из восьми тел разница между «капает» и «течёт» теряется.",
          draw: drawBloodApproach("drip")
        },
        {
          id: "bl-soak", status: "accepted", title: "B · Пропитывание", size: SIZE,
          decision: "2026-08-01/9",
          note: "Тело краснеет снизу вверх — он залит своей кровью. Подтон намеренно слабый: <b>не съедает силуэт, просто чуть красит</b>.",
          verdict: "Работает площадью, а не мелкой графикой, поэтому читается и в свалке из восьми тел. Силуэт при этом остаётся носителем класса и оружия.",
          draw: drawBloodApproach("soak")
        },
        {
          id: "bl-glow", status: "rejected", title: "C · Свечение ран", size: SIZE,
          note: "Число ран не меняется — разгораются сами порезы, от тусклых до раскалённых.",
          verdict: "Светящаяся рана читается как магия, а кровь у нас предельно материальна.",
          draw: drawBloodApproach("glow")
        },
        {
          id: "bl-pulse", status: "rejected", title: "D · Пульс", size: SIZE,
          note: "Тело толчками отдаёт красным в такт сердцу: чем выше стадия, тем чаще и глубже.",
          verdict: "Ритм спорит с тиками DoT и с ударами: третий пульсирующий источник на том же теле.",
          draw: drawBloodApproach("pulse")
        }
      ]
    },

    {
      kind: "head", id: "life", title: "Жизнь эффекта · рождение, повтор, конец",
      lede:
        "Шесть разных событий, которые до этого выглядели одинаково. Носитель формы везде один — " +
        "горение, — и это принципиально: снятие сохраняет форму и меняет только цвет. Тогда видно и " +
        "«что» сняли (по форме), и «чем» (по цвету), ровно как у удара форма говорит КАК доставили, " +
        "а цвет — ЧЕМ ударили."
    },
    {
      kind: "stands",
      items: [
        { id: "life-birth", status: "accepted", title: "Наложился", size: SIZE, decision: "2026-08-01/12",
          note: "Всплеск формы наружу и осадка: эффекта не было, стал.", draw: drawLifeCase("birth") },
        { id: "life-stack", status: "accepted", title: "Стак +1", size: SIZE, decision: "2026-08-01/13",
          note: "Толчок: форма дёргается наружу и возвращается. Стало <b>больше</b>.", draw: drawLifeCase("stack") },
        { id: "life-refresh", status: "accepted", title: "Обновили срок", size: SIZE, decision: "2026-08-01/13",
          note: "Волна по телу, форма не растёт. Стало <b>дольше</b>, но не сильнее.", draw: drawLifeCase("refresh") },
        { id: "life-cleanse", status: "accepted", title: "Клинс", size: SIZE, decision: "2026-08-01/10",
          note: "Форма голубеет, потом тает. Голубой — цвет <b>действия</b>, а не кастера: клинс Пастыря и Друида читаются одинаково.",
          draw: drawLifeCase("cleanse") },
        { id: "life-dispel", status: "accepted", title: "Диспел баффа", size: SIZE, decision: "2026-08-01/11",
          note: "Темнеет и <b>срывается</b>, без таяния. С тебя сняли хорошее — противоположный факт, поэтому и цвет другой.",
          draw: drawLifeCase("dispel") },
        { id: "life-expire", status: "accepted", title: "Истёк сам", size: SIZE,
          note: "Ровное угасание <b>в своём цвете</b>. Любая перекраска сделала бы истечение событием, а оно не событие.",
          draw: drawLifeCase("expire") }
      ]
    },
    {
      kind: "text",
      html:
        "«Не влез в потолок» не показываем: глухих потолков у нас почти нет — угли и гниль до 999, кровь " +
        "без потолка, а двадцатый стак Изморози это не отказ, а <b>переход в статую</b>. Наложение " +
        "сработало, просто иначе."
    },

    {
      kind: "head", id: "dot-tick", title: "Тик DoT · волна в цвете школы",
      lede:
        "Постоянный знак получает только тот статус, у кого есть состояние тела. У яда его нет — есть " +
        "тик и дебафф. Поэтому сам факт «его точит» несёт мягкая волна по силуэту, а различаются яды " +
        "своей дебафф-частью, у которой визуал уже решён."
    },
    {
      kind: "stands",
      items: [
        preview("wave-poison", "Яд", "Зелёная волна на каждом тике. Постоянного знака нет вовсе — и десять разных ядов выглядят одинаково, потому что делают одно.", "accepted"),
        preview("wave-burn", "Горение", "Волна плюс языки: у огня есть состояние тела, поэтому знак остаётся.", "accepted"),
        preview("wave-hit", "Для сравнения: удар", "Вспышка удара резкая, белая, три кадра. Волна DoT мягкая, цветная и вдвое длиннее — их нельзя спутать.")
      ]
    },
    {
      kind: "note",
      html:
        "Чего в сетке нет и почему: <b>барьер</b> — свой раздел; <b>ускорение</b> пока не отличается от " +
        "усиления ничем, кроме темпа собственных анимаций, и своего слоя может не требовать вовсе; " +
        "<b>парирование и уклонение</b> — событийные, а не постоянные, им место в словаре событий."
    }
  ]
};

export default section;
