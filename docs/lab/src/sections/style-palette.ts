/* Палитра проекта: токены цвета такими, какие они в игре прямо сейчас.

   HARD-правило проекта: у цвета один владелец — `Assets/_Project/UI/Theme/tokens.*.uss`, мир читает
   снимок. Поэтому раздел ничего не хардкодит: сервер парсит те самые файлы на лету.

   Плитки — живой DOM, а не canvas. Цвет в канвасе нельзя ни выделить, ни скопировать, ни найти
   поиском по странице, а именно это с палитрой и делают: берут значение и несут в код. Свечение
   вспышек, наоборот, осталось на canvas — аддитивный цвет надо смотреть светом на тёмном, плиткой
   он врёт. */

import { drawFeedState, fetchPalette, type PaletteToken } from "../api.js";
import { el } from "../dom.js";
import * as lightbox from "../lightbox.js";
import { addLightboxItems } from "../views.js";
import type { DrawFn, SectionDef } from "../types.js";

const palette = fetchPalette();

/** Токен как цвет для CSS. USS пишет `rgb(184, 134, 59)` — это и так валидное значение. */
function cssOf(token: PaletteToken): string | null {
  const v = token.value.trim();
  return v.startsWith("rgb") || v.startsWith("#") ? v : null;
}

function tokensMatching(...prefixes: string[]): PaletteToken[] {
  const out: PaletteToken[] = [];
  for (const g of palette.data?.groups ?? []) {
    for (const t of g.tokens) {
      if (prefixes.some((p) => t.name.startsWith(p)) && cssOf(t)) out.push(t);
    }
  }
  return out;
}

/* ---------- плитки: живой DOM ---------- */

/** Клик по плитке открывает цвет во весь экран — на ноготь размером оттенки латуни неразличимы,
 *  а именно их и надо сравнивать. Копирование имени переехало внутрь крупного показа: там оно
 *  осознанное действие, а не случайный клик мимо. */
function swatch(token: PaletteToken): HTMLElement {
  const css = cssOf(token) ?? "transparent";
  const cell = el("button", "sw");
  cell.type = "button";
  cell.title = "Открыть крупно";

  const chip = el("span", "sw-chip");
  chip.style.background = css;

  const text = el("span", "sw-text");
  text.appendChild(el("b", null, token.name.replace("--gm-", "")));
  text.appendChild(el("code", null, token.value.replace(/\s+/g, " ")));
  if (token.note) text.appendChild(el("i", null, token.note));

  cell.append(chip, text);
  cell.addEventListener("click", () => lightbox.open(token));
  return cell;
}

function group(host: HTMLElement, title: string, note: string, prefixes: string[]): void {
  const list = tokensMatching(...prefixes);
  if (list.length === 0) return;

  // Плитки дописываются в общий список лайтбокса: стрелками можно пройти всю палитру подряд,
  // а это и есть способ сравнить два соседних оттенка.
  addLightboxItems(list.map((t) => ({ kind: "color" as const, token: t, css: cssOf(t) ?? "#000" })));

  const box = el("section", "sw-group");
  const head = el("h3", null, title);
  head.appendChild(el("span", "sw-count", String(list.length)));
  box.appendChild(head);
  if (note) box.appendChild(el("p", "dim", note));

  const grid = el("div", "sw-grid");
  for (const token of list) grid.appendChild(swatch(token));
  box.appendChild(grid);
  host.appendChild(box);
}

function renderTokens(host: HTMLElement): void {
  const status = el("p", "dim", "читаю палитру проекта…");
  host.appendChild(status);

  void palette.settled.then(() => {
    if (!palette.data) {
      status.textContent =
        `Палитра недоступна: ${palette.error ?? "нет ответа"}. Страница читает тему проекта через ` +
        "сервер — нужен ./scripts/lab-serve.ps1";
      return;
    }
    host.replaceChildren();

    const total = (palette.data.groups ?? []).reduce((n, g) => n + g.tokens.length, 0);
    host.appendChild(el("p", "dim",
      `${total} токенов прочитано с диска при открытии страницы. Клик по плитке открывает цвет во весь экран, стрелки листают всю палитру подряд.`));

    group(host, "Чернила", "Фон и контуры. Подпись у ink-100 — минимум, на котором контур ещё виден.",
      ["--gm-ink-"]);
    group(host, "Латунь", "Акцент интерфейса и рамки принятого.", ["--gm-brass-", "--gm-ember-"]);
    group(host, "Пергамент", "Текст и светлые поверхности.", ["--gm-parchment-", "--gm-neutral-"]);
    group(host, "Затемнение",
      "Три оттенка нужны потому, что одно универсальное не читается на любом арте: холодное гасит " +
      "тёплое, зеленца оставляет зелёный, жёлто-бурый работает на зелёных.", ["--gm-dim-"]);
    group(host, "Сигналы", "Опасность, рост, буря, аркана.",
      ["--gm-danger-", "--gm-moss-", "--gm-storm-", "--gm-wine-"]);
  });
}

/* ---------- вспышки: только светом ---------- */

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

/** Те же вспышки плиткой — чтобы значение можно было взять руками. */
function renderFlareList(host: HTMLElement): void {
  void palette.settled.then(() => {
    if (!palette.data) return;
    group(host, "Вспышки", "", ["--gm-flare-"]);
  });
}

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
        "Аддитивные цвета удара и эффектов. Плиткой их проверять бессмысленно: они складываются со " +
        "светом под собой, поэтому сверху они показаны свечением на тёмном — так же, как лягут в бою. " +
        "Список под ними — те же токены, но значение можно взять руками."
    },
    {
      kind: "split",
      items: [
        { id: "flare-glow", status: "note", tag: "снимок проекта", title: "Как они лягут в бою",
          size: [740, 300], draw: drawFlares }
      ]
    },
    { kind: "live", id: "flare-list", render: renderFlareList },

    {
      kind: "head", id: "tokens", title: "Интерфейс, затемнение, сигналы",
      lede: "Пояснения приходят из комментариев самого USS — их пишет тот же файл, что и цвета."
    },
    { kind: "live", id: "token-list", render: renderTokens },

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
