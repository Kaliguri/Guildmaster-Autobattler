/* Семь пунктов плана «догнать рефы» — каждый крупным планом и с вариантами.

   Заказ Макса 21.08.2026: «Ты покажи сначала все пункты на сайте, пожалуйста, с вариантами и тп!».
   Раздел `ui-uplift` показывает ЭКРАНЫ целиком; здесь наоборот — ПРИЁМ крупно, чтобы спорить о
   форме, а не о раскладке.

   Порядок пунктов — из плана `tech/40-planning/ui-uplift.md`, по отношению «эффект к затратам».
   Пункт 1 уже сделан в игре, остальные ждут вердикта.

   Рисовалки нарочно крупные: элемент занимает половину сцены. Мелкая кнопка посреди чертежа кадра
   не даёт разглядеть ровно то, ради чего пункт и заведён. */

import * as w from "./ui-wire.js";
import type { SectionDef } from "../types.js";

/** Крупная кнопка по центру сцены: общая заготовка для пунктов про форму. */
function plate(ctx: CanvasRenderingContext2D, width: number, height: number,
                label: string, lit = false): w.Rect {
  const r: w.Rect = { x: 0.12, y: 0.36, w: 0.76, h: 0.28 };
  w.box(ctx, r, width, height, { label, lit, size: 14 });
  return r;
}

/* ── 1. Концы ─────────────────────────────────────────────────────────────── */

function capsNone(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  plate(ctx, width, height, "СОЗДАТЬ ИГРУ");
  w.callout(ctx, { x: 0.14, y: 0.50 }, { x: 0.04, y: 0.72 }, "кромка пустая", width, height, "left");
}

/** Шеврон остриями внутрь — то, что сделано. */
function capsChevron(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  const r = plate(ctx, width, height, "СОЗДАТЬ ИГРУ", true);
  const midY = (r.y + r.h * 0.5) * height;
  const size = height * 0.07;

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  for (const [x, dir] of [[0.17, 1], [0.83, -1]] as Array<[number, number]>) {
    ctx.beginPath();
    ctx.moveTo(x * width, midY - size);
    ctx.lineTo(x * width + dir * size * 0.7, midY);
    ctx.lineTo(x * width, midY + size);
    ctx.stroke();
  }
  w.callout(ctx, { x: 0.17, y: 0.72 }, { x: 0.06, y: 0.84 }, "шеврон внутрь", width, height, "left");
}

/** Скобки-уголки: две грани угла у каждой кромки. */
function capsBrackets(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  const r = plate(ctx, width, height, "СОЗДАТЬ ИГРУ");
  const top = (r.y + 0.05) * height;
  const bottom = (r.y + r.h - 0.05) * height;
  const arm = width * 0.035;

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  for (const [x, dir] of [[0.16, 1], [0.84, -1]] as Array<[number, number]>) {
    ctx.beginPath();
    ctx.moveTo(x * width + dir * arm, top);
    ctx.lineTo(x * width, top);
    ctx.lineTo(x * width, bottom);
    ctx.lineTo(x * width + dir * arm, bottom);
    ctx.stroke();
  }
  w.callout(ctx, { x: 0.16, y: 0.72 }, { x: 0.05, y: 0.84 }, "скобка", width, height, "left");
}

/* ── 2. Свечение вместо заливки ───────────────────────────────────────────── */

function litFill(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", lit: i === 2, size: 12 });
  w.callout(ctx, { x: 0.79, y: 0.56 }, { x: 0.60, y: 0.76 }, "плоская заливка", width, height, "left");
}

/** Ореол вокруг активного плюс шеврон снизу — приём Heroes. */
function litGlow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  const active: w.Rect = { x: 0.66, y: 0.30, w: 0.26, h: 0.22 };
  const [ax, ay, aw, ah] = w.px(active, width, height);
  const grad = ctx.createRadialGradient(ax + aw / 2, ay + ah / 2, 2, ax + aw / 2, ay + ah / 2, aw * 0.8);
  grad.addColorStop(0, "rgba(200,162,76,0.35)");
  grad.addColorStop(1, "rgba(200,162,76,0)");
  ctx.fillStyle = grad;
  ctx.fillRect(ax - aw * 0.5, ay - ah * 0.8, aw * 2, ah * 2.6);

  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", hollow: true, size: 12 });

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(ax + aw * 0.42, ay + ah + 8);
  ctx.lineTo(ax + aw * 0.5, ay + ah + 16);
  ctx.lineTo(ax + aw * 0.58, ay + ah + 8);
  ctx.stroke();

  w.callout(ctx, { x: 0.79, y: 0.62 }, { x: 0.56, y: 0.80 }, "ореол + шеврон", width, height, "left");
}

/** Только подчёркивание светом: заливки нет вовсе. */
function litUnderline(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", hollow: true, size: 12 });

  const grad = ctx.createLinearGradient(0.66 * width, 0, 0.92 * width, 0);
  grad.addColorStop(0, "rgba(200,162,76,0)");
  grad.addColorStop(0.5, "rgba(200,162,76,0.9)");
  grad.addColorStop(1, "rgba(200,162,76,0)");
  ctx.fillStyle = grad;
  ctx.fillRect(0.66 * width, 0.53 * height, 0.26 * width, 3);

  w.callout(ctx, { x: 0.79, y: 0.56 }, { x: 0.58, y: 0.78 }, "черта с растушёвкой", width, height, "left");
}

/* ── 3. Группировка паузами ───────────────────────────────────────────────── */

function rhythmFlat(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 5; i++)
    w.box(ctx, { x: 0.28, y: 0.12 + i * 0.16, w: 0.44, h: 0.12 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль", "Настройки", "Выход"][i] ?? "", size: 11 });
  w.callout(ctx, { x: 0.72, y: 0.50 }, { x: 0.86, y: 0.50 }, "шаг один на всех", width, height, "left");
}

function rhythmGrouped(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.28, y: 0.10 + i * 0.16, w: 0.44, h: 0.12 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль"][i] ?? "", lit: i === 0, size: 11 });
  for (let i = 0; i < 2; i++)
    w.box(ctx, { x: 0.33, y: 0.70 + i * 0.14, w: 0.34, h: 0.10 }, width, height,
      { label: ["Настройки", "Выход"][i] ?? "", size: 10 });

  ctx.strokeStyle = w.WIRE.line;
  ctx.setLineDash([4, 4]);
  ctx.beginPath();
  ctx.moveTo(0.24 * width, 0.60 * height);
  ctx.lineTo(0.76 * width, 0.60 * height);
  ctx.stroke();
  ctx.setLineDash([]);

  w.callout(ctx, { x: 0.24, y: 0.60 }, { x: 0.06, y: 0.60 }, "пауза 19%", width, height, "left");
}

/* ── 4. Заголовки секций ──────────────────────────────────────────────────── */

function sectionsLines(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 4; i++) {
    w.text(ctx, ["Общий", "Музыка", "Звук", "Голоса"][i] ?? "", { x: 0.14, y: 0.20 + i * 0.20 },
      width, height, { size: 12 });
    w.box(ctx, { x: 0.52, y: 0.185 + i * 0.20, w: 0.34, h: 0.03 }, width, height, {});
    if (i < 3) {
      ctx.strokeStyle = w.WIRE.line;
      ctx.beginPath();
      ctx.moveTo(0.12 * width, (0.30 + i * 0.20) * height);
      ctx.lineTo(0.88 * width, (0.30 + i * 0.20) * height);
      ctx.stroke();
    }
  }
  w.callout(ctx, { x: 0.50, y: 0.30 }, { x: 0.50, y: 0.90 }, "линии режут поровну", width, height, "center");
}

function sectionsHeads(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.text(ctx, "ГРОМКОСТЬ", { x: 0.50, y: 0.14 }, width, height, { align: "center", size: 14 });
  for (let i = 0; i < 2; i++) {
    w.text(ctx, ["Общая", "Музыка"][i] ?? "", { x: 0.16, y: 0.30 + i * 0.14 }, width, height, { size: 12 });
    w.box(ctx, { x: 0.54, y: 0.285 + i * 0.14, w: 0.30, h: 0.03 }, width, height, {});
  }
  w.text(ctx, "ЗВУК В ИГРЕ", { x: 0.50, y: 0.62 }, width, height, { align: "center", size: 14 });
  for (let i = 0; i < 2; i++) {
    w.text(ctx, ["Голоса юнитов", "Звук интерфейса"][i] ?? "", { x: 0.16, y: 0.76 + i * 0.13 },
      width, height, { size: 12 });
    w.box(ctx, { x: 0.66, y: 0.735 + i * 0.13, w: 0.10, h: 0.07 }, width, height, { lit: i === 0 });
  }
  w.callout(ctx, { x: 0.50, y: 0.55 }, { x: 0.86, y: 0.50 }, "делит заголовок", width, height, "left");
}

/* ── 5. Углы кадра ────────────────────────────────────────────────────────── */

function cornersEmpty(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08, y: 0.36 + i * 0.14, w: 0.26, h: 0.10 }, width, height, { size: 9 });
  w.text(ctx, "0.0.4-dev", { x: 0.03, y: 0.94 }, width, height, { size: 8, color: "#8A8A93" });
  w.callout(ctx, { x: 0.80, y: 0.14 }, { x: 0.62, y: 0.14 }, "пусто", width, height, "right");
  w.callout(ctx, { x: 0.80, y: 0.88 }, { x: 0.62, y: 0.88 }, "пусто", width, height, "right");
}

function cornersFilled(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08, y: 0.36 + i * 0.14, w: 0.26, h: 0.10 }, width, height, { size: 9 });

  w.box(ctx, { x: 0.86, y: 0.04, w: 0.10, h: 0.10 }, width, height, { label: "?", size: 12 });
  w.box(ctx, { x: 0.72, y: 0.86, w: 0.10, h: 0.10 }, width, height, { label: "D", size: 10 });
  w.box(ctx, { x: 0.85, y: 0.86, w: 0.10, h: 0.10 }, width, height, { label: "YT", size: 10 });
  w.text(ctx, "0.0.4-dev", { x: 0.03, y: 0.94 }, width, height, { size: 8, color: "#8A8A93" });

  w.callout(ctx, { x: 0.86, y: 0.09 }, { x: 0.66, y: 0.09 }, "справка · клавиши", width, height, "right");
  w.callout(ctx, { x: 0.72, y: 0.91 }, { x: 0.56, y: 0.91 }, "ссылки сообщества", width, height, "right");
}

/* ── 6. Обрамление арены ──────────────────────────────────────────────────── */

function arenaBare(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.30 + (i % 2) * 0.24, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.04, y: 0.20 }, { x: 0.22, y: 0.20 }, "поле уходит в край кадра", width, height, "left");
}

/** Вариант арта: обрамление предметами по краям. */
function arenaArt(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.box(ctx, { x: 0.00, y: 0.00, w: 0.15, h: 1.00 }, width, height,
    { label: "арт", sub: "камни · листва", hollow: true, size: 9 });
  w.box(ctx, { x: 0.85, y: 0.00, w: 0.15, h: 1.00 }, width, height, { label: "арт", hollow: true, size: 9 });
  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.34 + (i % 2) * 0.20, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.15, y: 0.20 }, { x: 0.30, y: 0.18 }, "рамка из предметов", width, height, "left");
}

/** Вариант без художника: свет и виньетка. */
function arenaVignette(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  const grad = ctx.createRadialGradient(width * 0.5, height * 0.5, height * 0.2,
                                        width * 0.5, height * 0.5, width * 0.62);
  grad.addColorStop(0, "rgba(0,0,0,0)");
  grad.addColorStop(1, "rgba(0,0,0,0.72)");
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, width, height);

  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.34 + (i % 2) * 0.20, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.12, y: 0.18 }, { x: 0.30, y: 0.14 }, "виньетка светом", width, height, "left");
}

/* ── 7. Знак на исходе ────────────────────────────────────────────────────── */

function crestNone(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height);
  w.box(ctx, { x: 0.24, y: 0.34, w: 0.52, h: 0.32 }, width, height, { label: "ПОБЕДА", size: 16 });
}

/** Эмблема с лучами — приём Heroes. */
function crestRays(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.72);

  const cx = width * 0.5;
  const cy = height * 0.38;
  ctx.strokeStyle = "rgba(200,162,76,0.45)";
  ctx.lineWidth = 2;
  for (let i = 0; i < 12; i++) {
    const a = (Math.PI * 2 * i) / 12;
    ctx.beginPath();
    ctx.moveTo(cx + Math.cos(a) * height * 0.16, cy + Math.sin(a) * height * 0.16);
    ctx.lineTo(cx + Math.cos(a) * height * 0.30, cy + Math.sin(a) * height * 0.30);
    ctx.stroke();
  }
  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.arc(cx, cy, height * 0.15, 0, Math.PI * 2);
  ctx.stroke();
  w.text(ctx, "знак", { x: 0.50, y: 0.38 }, width, height, { align: "center", size: 11 });

  w.box(ctx, { x: 0.26, y: 0.70, w: 0.48, h: 0.12 }, width, height, { label: "ПОБЕДА", size: 14 });
  w.callout(ctx, { x: 0.68, y: 0.30 }, { x: 0.86, y: 0.24 }, "лучи из-за знака", width, height, "left");
}

/** Печать-штамп: знак ложится ПОВЕРХ картуша, как оттиск. */
function crestStamp(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.72);

  w.box(ctx, { x: 0.20, y: 0.42, w: 0.60, h: 0.16 }, width, height, { label: "ПОБЕДА", size: 16 });

  ctx.save();
  ctx.translate(width * 0.76, height * 0.50);
  ctx.rotate(-0.22);
  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.arc(0, 0, height * 0.13, 0, Math.PI * 2);
  ctx.stroke();
  ctx.restore();
  w.text(ctx, "печать", { x: 0.76, y: 0.50 }, width, height, { align: "center", size: 10 });

  w.callout(ctx, { x: 0.76, y: 0.66 }, { x: 0.86, y: 0.80 }, "оттиск поверх", width, height, "left");
}

const section: SectionDef = {
  id: "ui-uplift-steps",
  title: "Догнать рефы: семь пунктов",
  blocks: [
    {
      kind: "head",
      id: "intro",
      title: "Каждый пункт крупным планом",
      lede:
        "Раздел «Догнать рефы» показывает экраны целиком; здесь тот же план, но приём за приёмом — " +
        "чтобы спорить о форме, а не о раскладке."
    },
    {
      kind: "text",
      html:
        "Порядок — из плана <code>tech/40-planning/ui-uplift.md</code>, по отношению «эффект к " +
        "затратам». Первые пять пунктов делаются темой и раскладкой, последние два ждут художника. " +
        "Пункт 1 уже в игре, остальные ждут вердикта."
    },

    { kind: "head", id: "caps", title: "1 · Концы у кнопок", lede: "Сделано 21.08.2026. Варианты формы — на будущее." },
    {
      kind: "stands",
      items: [
        {
          id: "caps-none",
          status: "note",
          title: "Как было",
          note: "Пластина с фаской и каймой, кромка ничем не занята.",
          verdict: "При равных размерах именно это отличало нашу кнопку от кнопки Heroes.",
          size: [420, 200],
          draw: capsNone
        },
        {
          id: "caps-chevron",
          status: "accepted",
          title: "Шеврон остриями внутрь",
          tag: "в игре",
          note: "По шеврону у каждой кромки, острия смотрят на подпись.",
          facts: [["размер", "9 (--gm-plate-cap)"], ["ширина пункта", "380 → 430"]],
          verdict:
            "Наружу шевроны читались бы стрелками «сюда» и обещали переход, которого кнопка не " +
            "делает. Внутрь — обрамляют подпись и сводят взгляд к центру.",
          decision: "2026-08-21",
          size: [420, 200],
          draw: capsChevron
        },
        {
          id: "caps-brackets",
          status: "waiting",
          title: "Скобки-уголки",
          note: "Вместо шеврона — две грани угла у каждой кромки, как уголковая рамка Guildrun.",
          verdict:
            "Строже и спокойнее шеврона, лучше ложится на широкие панели. На кнопке рядом с фаской " +
            "спорит с ней: два угла в одном месте.",
          size: [420, 200],
          draw: capsBrackets
        }
      ]
    },

    { kind: "head", id: "glow", title: "2 · Свечение вместо заливки", lede: "Следующий пункт очереди." },
    {
      kind: "stands",
      items: [
        {
          id: "glow-now",
          status: "note",
          title: "Как сейчас",
          note: "Активная вкладка залита цветом действия, соседние — пустые прямоугольники.",
          verdict: "Заливка держит только один уровень: «этот» против «остальных». Света нет.",
          size: [420, 200],
          draw: litFill
        },
        {
          id: "glow-halo",
          status: "waiting",
          title: "А · ореол и шеврон",
          note: "Вокруг активного мягкое свечение, под ним маленький шеврон — прямой приём Heroes.",
          verdict:
            "Ближе всего к рефу и читается даже на пёстром фоне. Дороже в реализации: UITK не " +
            "умеет тени вокруг элемента, потребуется рисовать градиент контролом.",
          size: [420, 200],
          draw: litGlow
        },
        {
          id: "glow-underline",
          status: "waiting",
          title: "Б · черта с растушёвкой",
          note: "Заливки нет вовсе: под активным светящаяся линия, гаснущая к краям.",
          verdict:
            "Дешевле и тише, работает и на вкладках, и на строках списка. Слабее на фоне с " +
            "рисунком — линия теряется там, где ореол ещё виден.",
          size: [420, 200],
          draw: litUnderline
        }
      ]
    },

    { kind: "head", id: "rhythm", title: "3 · Группировка паузами" },
    {
      kind: "split",
      items: [
        {
          id: "rhythm-flat",
          status: "note",
          title: "Как сейчас",
          note: "Пять пунктов с одинаковым шагом: игра и служебное неразличимы.",
          verdict: "Ровный столбик читается списком, а не меню с иерархией.",
          size: [420, 240],
          draw: rhythmFlat
        },
        {
          id: "rhythm-grouped",
          status: "waiting",
          title: "Пауза перед служебным",
          note:
            "Три пункта про игру, пауза в 19% высоты кадра, затем «Настройки» и «Выход» — мельче и " +
            "уже. Числа сняты с рефа.",
          facts: [["пауза", "19% высоты"], ["служебные", "уже на 23%"]],
          verdict:
            "Правка одних отступов, а меню сразу перестаёт быть рыхлым. Требует решить, что " +
            "считать служебным: сейчас это «Настройки» и «Выход», но «Профиль» спорный.",
          size: [420, 240],
          draw: rhythmGrouped
        }
      ]
    },

    { kind: "head", id: "sections", title: "4 · Заголовки секций вместо линий" },
    {
      kind: "split",
      items: [
        {
          id: "sections-lines",
          status: "note",
          title: "Как сейчас",
          note: "Строки настроек разделены линиями через равные промежутки.",
          verdict:
            "Линия режет поровну и потому ничего не группирует: соседние по смыслу строки выглядят " +
            "так же далеко, как чужие друг другу.",
          size: [420, 240],
          draw: sectionsLines
        },
        {
          id: "sections-heads",
          status: "waiting",
          title: "Заголовок делит, пустота отделяет",
          note: "Секция объявлена крупным заголовком по центру, линий нет вовсе — приём Heroes.",
          verdict:
            "Даёт настройкам структуру и заодно место под будущие строки. Пока их три, секции " +
            "выглядят обещанием — пункт лучше делать вместе с наполнением настроек.",
          size: [420, 240],
          draw: sectionsHeads
        }
      ]
    },

    { kind: "head", id: "corners", title: "5 · Занять углы кадра" },
    {
      kind: "split",
      items: [
        {
          id: "corners-empty",
          status: "note",
          title: "Как сейчас",
          note: "Занят один угол из четырёх — версия внизу слева.",
          verdict: "Пустые углы делают кадр незаконченным даже при плотном центре.",
          size: [420, 240],
          draw: cornersEmpty
        },
        {
          id: "corners-filled",
          status: "waiting",
          title: "Справка и сообщество",
          note:
            "Справа вверху — справка и раскладка клавиш, справа внизу — ссылки сообщества, версия " +
            "остаётся слева.",
          verdict:
            "Дёшево и сразу узнаётся: так делают оба рефа. Требует решить, что у нас в углах " +
            "вообще должно быть — Discord у проекта есть, справки пока нет.",
          size: [420, 240],
          draw: cornersFilled
        }
      ]
    },

    { kind: "head", id: "arena", title: "6 · Обрамление арены", lede: "Первый из двух пунктов, ждущих художника." },
    {
      kind: "stands",
      items: [
        {
          id: "arena-bare",
          status: "note",
          title: "Как сейчас",
          note: "Поле боя уходит в край кадра, границы держит только камера.",
          verdict: "Именно это делает фон меню зелёным шумом вместо картины.",
          size: [420, 200],
          draw: arenaBare
        },
        {
          id: "arena-art",
          status: "waiting",
          title: "А · рамка из предметов",
          note: "По краям кадра камни, листва, руины — приём Guildrun. Поле в световом пятне.",
          facts: [["обрамление", "по 15% с каждой стороны"]],
          verdict: "Сильнее всего меняет ощущение готовой игры. Нужен художник.",
          size: [420, 200],
          draw: arenaArt
        },
        {
          id: "arena-vignette",
          status: "waiting",
          title: "Б · виньетка светом",
          note: "Без арта: тёмная виньетка по краям и светлое пятно на месте боя.",
          verdict:
            "Делается постобработкой сегодня же и уже собирает кадр. Слабее рамки из предметов: " +
            "тьма по краям читается «экономией», а не миром.",
          size: [420, 200],
          draw: arenaVignette
        }
      ]
    },

    { kind: "head", id: "crest", title: "7 · Знак на экране исхода", lede: "Второй пункт, ждущий художника." },
    {
      kind: "stands",
      items: [
        {
          id: "crest-none",
          status: "note",
          title: "Как сейчас",
          note: "Панель со словом «Победа» и кнопкой.",
          verdict: "Победа и поражение отличаются одним словом.",
          size: [420, 200],
          draw: crestNone
        },
        {
          id: "crest-rays",
          status: "waiting",
          title: "А · эмблема с лучами",
          note: "Круглый знак и расходящиеся лучи, под ним картуш с заголовком — приём Heroes.",
          verdict:
            "Самый близкий к рефу. Знак может быть гербом гильдии — тогда он не просто украшение, " +
            "а «победила ИМЕННО ТВОЯ гильдия».",
          size: [420, 200],
          draw: crestRays
        },
        {
          id: "crest-stamp",
          status: "waiting",
          title: "Б · печать поверх",
          note: "Знак ложится оттиском поверх картуша, слегка под углом.",
          verdict:
            "Дешевле лучей и лучше ложится на нашу тему гроссбуха: печать в документе. Слабее " +
            "работает на поражении — оттиск читается наградой.",
          size: [420, 200],
          draw: crestStamp
        }
      ]
    },

    {
      kind: "note",
      html:
        "<b>Как читать статусы.</b> «в игре» — сделано и живёт в билде. «ждёт» — нарисовано, вердикта " +
        "нет. «ситуация» — как оно выглядит сегодня, для сравнения."
    }
  ]
};

export default section;
