/* Палитра проекта: токены цвета такими, какие они в игре прямо сейчас.

   HARD-правило проекта: у цвета один владелец — `Assets/_Project/UI/Theme/tokens.*.uss`, мир читает
   снимок. Поэтому раздел ничего не хардкодит: сервер парсит те самые файлы на лету. Если сайт
   открыт без сервера, стенды честно скажут «нет данных» вместо того, чтобы показать старую копию. */

import { drawFeedState, fetchPalette, type PaletteToken } from "../api.js";
import type { DrawFn, SectionDef } from "../types.js";

const palette = fetchPalette();

/** Токен как цвет для canvas. USS пишет `rgb(184, 134, 59)` — это и так валидный CSS. */
function cssOf(token: PaletteToken): string | null {
  const v = token.value.trim();
  return v.startsWith("rgb") || v.startsWith("#") ? v : null;
}

function tokensMatching(prefix: string): PaletteToken[] {
  const groups = palette.data?.groups ?? [];
  const out: PaletteToken[] = [];
  for (const g of groups) {
    for (const t of g.tokens) {
      if (t.name.startsWith(prefix) && cssOf(t)) out.push(t);
    }
  }
  return out;
}

/** Плитки токенов: имя, образец, пояснение из самого USS. */
function drawSwatches(prefix: string, columns: number): DrawFn {
  return (ctx, w, h) => {
    if (drawFeedState(ctx, w, h, palette, "палитру")) return;

    const list = tokensMatching(prefix);
    if (list.length === 0) {
      ctx.font = "500 13px ui-monospace, Consolas, monospace";
      ctx.fillStyle = "rgba(147,128,94,.8)";
      ctx.fillText(`токенов ${prefix}* в теме нет`, 22, h / 2);
      return;
    }

    const pad = 16;
    const gap = 8;
    const cellW = (w - pad * 2 - gap * (columns - 1)) / columns;
    const rows = Math.ceil(list.length / columns);
    const cellH = Math.min(52, (h - pad * 2 - gap * (rows - 1)) / rows);

    list.forEach((token, i) => {
      const col = i % columns;
      const row = Math.floor(i / columns);
      const x = pad + col * (cellW + gap);
      const y = pad + row * (cellH + gap);
      const css = cssOf(token);
      if (!css) return;

      ctx.fillStyle = css;
      ctx.fillRect(x, y, cellH * 0.85, cellH * 0.85);
      ctx.strokeStyle = "rgba(58,44,30,.9)";
      ctx.lineWidth = 1;
      ctx.strokeRect(x + 0.5, y + 0.5, cellH * 0.85, cellH * 0.85);

      const textX = x + cellH * 0.85 + 8;
      ctx.font = "500 11px ui-monospace, Consolas, monospace";
      ctx.fillStyle = "rgba(232,220,196,.92)";
      ctx.fillText(token.name.replace("--gm-", ""), textX, y + 14);
      ctx.fillStyle = "rgba(147,128,94,.85)";
      ctx.fillText(token.value.replace(/\s+/g, ""), textX, y + 28);
      if (token.note) {
        ctx.fillStyle = "rgba(147,128,94,.6)";
        const note = token.note.length > 34 ? `${token.note.slice(0, 33)}…` : token.note;
        ctx.fillText(note, textX, y + 41);
      }
    });
  };
}

/** Вспышки на тёмном: они аддитивные, и проверять их надо не плиткой, а свечением. */
const drawFlares: DrawFn = (ctx, w, h) => {
  if (drawFeedState(ctx, w, h, palette, "палитру")) return;

  const list = tokensMatching("--gm-flare-");
  const columns = 6;
  const pad = 30;
  const cellW = (w - pad * 2) / columns;
  const cellH = 88;

  list.forEach((token, i) => {
    const css = cssOf(token);
    if (!css) return;
    const cx = pad + (i % columns) * cellW + cellW / 2;
    const cy = pad + Math.floor(i / columns) * cellH + 30;

    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, 26);
    g.addColorStop(0, css);
    g.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(cx, cy, 26, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();

    ctx.fillStyle = css;
    ctx.beginPath();
    ctx.arc(cx, cy, 4, 0, Math.PI * 2);
    ctx.fill();

    ctx.font = "500 10px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    const name = token.name.replace("--gm-flare-", "");
    ctx.fillText(name, cx - ctx.measureText(name).width / 2, cy + 42);
  });
};

/** Сколько всего токенов и откуда они пришли — чтобы было видно, что это снимок, а не список. */
const drawSummary: DrawFn = (ctx, w, h) => {
  if (drawFeedState(ctx, w, h, palette, "палитру")) return;
  const groups = palette.data?.groups ?? [];

  ctx.font = "500 14px ui-monospace, Consolas, monospace";
  let y = 40;
  for (const g of groups) {
    ctx.fillStyle = "rgba(198,154,75,.95)";
    ctx.fillText(g.file, 24, y);
    ctx.fillStyle = "rgba(147,128,94,.85)";
    ctx.fillText(`${g.tokens.length} токенов`, w - 140, y);
    y += 26;
  }
  ctx.fillStyle = "rgba(147,128,94,.7)";
  ctx.font = "500 12px ui-monospace, Consolas, monospace";
  ctx.fillText("прочитано с диска при открытии страницы", 24, y + 12);
};

const section: SectionDef = {
  id: "palette",
  title: "Палитра",
  eyebrow: "Лаборатория · стиль · цвет",
  transport: false,
  lede:
    "Токены цвета такими, какие они в игре <b>прямо сейчас</b>: страница читает " +
    "<code>tokens.primitives.uss</code> и <code>tokens.semantic.uss</code> с диска. Своего списка у " +
    "сайта нет намеренно — у цвета один владелец, и вторая копия начала бы врать молча.",

  blocks: [
    {
      kind: "head", id: "flares", title: "Вспышки",
      lede:
        "Аддитивные цвета удара и эффектов. Проверять их плиткой бессмысленно: они складываются со " +
        "светом под собой, поэтому здесь показаны свечением на тёмном — так же, как лягут в бою."
    },
    {
      kind: "split",
      items: [
        { id: "flare-grid", status: "note", tag: "снимок проекта", title: "Вспышки свечением",
          size: [740, 300], draw: drawFlares }
      ]
    },

    {
      kind: "head", id: "ink", title: "Чернила и латунь",
      lede: "База интерфейса: фон, контуры, акцент. Пояснения приходят из комментариев самого USS."
    },
    {
      kind: "stands",
      items: [
        { id: "ink", status: "note", tag: "--gm-ink-*", title: "Чернила", size: [420, 300],
          draw: drawSwatches("--gm-ink-", 1) },
        { id: "brass", status: "note", tag: "--gm-brass-*", title: "Латунь", size: [420, 300],
          draw: drawSwatches("--gm-brass-", 1) },
        { id: "parchment", status: "note", tag: "--gm-parchment-*", title: "Пергамент", size: [420, 300],
          draw: drawSwatches("--gm-parchment-", 1) }
      ]
    },

    {
      kind: "head", id: "dim", title: "Затемнение и сигналы",
      lede:
        "Три оттенка затемнения нужны потому, что одно универсальное не читается на любом арте: " +
        "холодное гасит тёплое, зеленца оставляет зелёный, жёлто-бурый работает на зелёных артах."
    },
    {
      kind: "stands",
      items: [
        { id: "dim", status: "note", tag: "--gm-dim-*", title: "Затемнение", size: [420, 220],
          draw: drawSwatches("--gm-dim-", 1) },
        { id: "signal", status: "note", tag: "--gm-danger / moss / storm / wine", title: "Сигналы", size: [420, 300],
          draw: drawSwatches("--gm-d", 1) },
        { id: "summary", status: "note", tag: "источник", title: "Откуда взято", size: [420, 220],
          draw: drawSummary }
      ]
    },
    {
      kind: "note",
      html:
        "Правило проекта: <b>цвет не хардкодится нигде</b>, ни в шейдере, ни в коде презентера, ни " +
        "здесь. Если оттенка не хватает — он заводится токеном в теме, и тогда его увидят разом игра, " +
        "инспектор и эта страница."
    }
  ]
};

export default section;
