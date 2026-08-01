/* Отрисовка страниц из данных: витрина, раздел, музей отклонённого.

   Ни одна из трёх не ведётся руками. Витрина берёт превью из самого раздела, музей — фильтр по
   статусу стенда. Список, который ведут отдельно от содержимого, расходится с ним за неделю. */

import { el, html } from "./dom.js";
import { watch } from "./stage.js";
import * as lightbox from "./lightbox.js";
import * as toggles from "./toggles.js";
import type { Block, PageDef, SectionDef, StandDef } from "./types.js";

/** Как подписан статус на карточке. У "note" подписи нет: это не развилка, а иллюстрация. */
const STATUS_LABEL: Record<StandDef["status"], string> = {
  accepted: "принято",
  rejected: "отклонено",
  waiting: "ждёт вердикта",
  note: ""
};

export function routeHref(pageId: string, anchor?: string | null): string {
  return `#/${pageId}${anchor ? `/${encodeURIComponent(anchor)}` : ""}`;
}

/** Все стенды раздела подряд — витрине, музею и поиску нужен именно плоский список. */
export function eachStand(def: SectionDef, fn: (stand: StandDef) => void): void {
  for (const block of def.blocks) {
    if (block.kind === "stands" || block.kind === "split") block.items.forEach(fn);
  }
}

export function hero(title: string, lede?: string, eyebrow = "Лаборатория Guildmaster"): HTMLElement {
  const head = el("header", "page-head");
  head.appendChild(el("p", "eyebrow", eyebrow));
  head.appendChild(el("h1", null, title));
  if (lede) {
    const p = html("p", lede, "lede");
    p.style.marginTop = "1rem";
    head.appendChild(p);
  }
  return head;
}

/* ---------- витрина ---------- */

export function renderIndex(view: HTMLElement, pages: PageDef[], loaded: Map<string, SectionDef>): void {
  beginScenes();
  view.appendChild(
    hero(
      "Лаборатория",
      "Всё, на что надо посмотреть, а не прочитать: как выглядит удар, что делает статус, чем занят " +
        "барьер, во что обходится баланс. Замысел и правила живут в ГДД — сюда приходит только то, " +
        "чего текст не умеет."
    )
  );

  for (const group of groupsOf(pages)) {
    const shelf = el("section", "shelf");
    shelf.appendChild(el("h2", null, group));
    const grid = el("div", "cards");

    for (const page of pages.filter((p) => p.group === group)) {
      grid.appendChild(indexCard(page, loaded.get(page.id)));
    }
    shelf.appendChild(grid);
    view.appendChild(shelf);
  }
  commitScenes();
}

function indexCard(page: PageDef, def: SectionDef | undefined): HTMLElement {
  const card = el("a", "card-link");
  card.href = page.href ?? routeHref(page.id);
  if (page.href) card.target = "_blank";

  // Живое превью, а не скриншот: список ссылок не говорит, что внутри, а снимок устаревает молча.
  const cover = def ? coverStand(def) : null;
  if (cover) {
    const canvas = el("canvas");
    canvas.width = 320;
    canvas.height = 200;
    card.appendChild(canvas);
    watch(canvas, cover, 320, 200);
    scenes.push({ stand: cover, w: 320, h: 200 });
  } else if (page.icon) {
    card.appendChild(el("div", "card-mark", page.icon));
  }

  const body = el("div", "card-text");
  body.appendChild(el("h3", null, page.title));
  body.appendChild(el("p", "dim", page.blurb));
  if (page.href) {
    body.appendChild(el("p", "tag", "отдельное приложение · откроется в новой вкладке"));
  } else if (def) {
    // Счётчик показываем ровно настолько, насколько ему есть что сказать: у раздела без развилок
    // «принято 0» читалось бы как «ничего не решено», хотя решать там нечего.
    const t = tally(def);
    const parts: string[] = [];
    if (t.total > 0) parts.push(`${t.total} стендов`);
    if (t.accepted > 0) parts.push(`принято ${t.accepted}`);
    if (t.waiting > 0) parts.push(`ждёт ${t.waiting}`);
    if (parts.length > 0) body.appendChild(el("p", "tag", parts.join(" · ")));
  }
  card.appendChild(body);
  return card;
}

function coverStand(def: SectionDef): StandDef | null {
  let accepted: StandDef | null = null;
  let any: StandDef | null = null;
  eachStand(def, (s) => {
    if (!s.draw) return;
    if (!any) any = s;
    if (!accepted && s.status === "accepted") accepted = s;
  });
  return accepted ?? any;
}

function tally(def: SectionDef): { total: number; accepted: number; rejected: number; waiting: number } {
  const t = { total: 0, accepted: 0, rejected: 0, waiting: 0 };
  eachStand(def, (s) => {
    t.total++;
    if (s.status === "accepted") t.accepted++;
    else if (s.status === "rejected") t.rejected++;
    else if (s.status === "waiting") t.waiting++;
  });
  return t;
}

function groupsOf(pages: PageDef[]): string[] {
  const seen: string[] = [];
  for (const p of pages) if (!seen.includes(p.group)) seen.push(p.group);
  return seen;
}

/* ---------- музей отклонённого ---------- */

export function renderLegacy(view: HTMLElement, pages: PageDef[], loaded: Map<string, SectionDef>): void {
  beginScenes();
  view.appendChild(
    hero(
      "Отклонённое",
      "Варианты, которые проиграли, — живыми, а не описанием. Музей полезен ровно тем, что показывает, " +
        "ЧЕМ проигравший был хуже: через полгода «мы это уже пробовали» без картинки звучит " +
        "неубедительно и пробуется заново."
    )
  );

  let any = false;
  for (const page of pages) {
    const def = loaded.get(page.id);
    if (!def) continue;
    const items: StandDef[] = [];
    eachStand(def, (s) => {
      if (s.status === "rejected") items.push(s);
    });
    if (items.length === 0) continue;
    any = true;

    const wrap = el("section");
    const head = el("h2", null, page.title);
    const back = el("a", "h-link", "к разделу →");
    back.href = routeHref(page.id);
    head.appendChild(back);
    wrap.appendChild(head);
    wrap.appendChild(standsGrid({ kind: "stands", items }, page.id));
    view.appendChild(wrap);
  }

  if (!any) view.appendChild(el("p", "dim", "Пока ни один вариант не отклонён."));
  commitScenes();
}

/* ---------- раздел ---------- */

/** Сцены текущей страницы по порядку: их листает лайтбокс. Собирается тем же проходом, что и
 *  разметка, поэтому разойтись с ней не может. */
let scenes: Array<{ stand: StandDef; w: number; h: number }> = [];

function beginScenes(): void {
  scenes = [];
}

function commitScenes(): void {
  lightbox.setEntries(scenes);
}

export function renderSection(view: HTMLElement, def: SectionDef): void {
  beginScenes();
  view.appendChild(hero(def.title, def.lede, def.eyebrow));

  const toc = el("nav", "page-toc");
  toc.setAttribute("aria-label", "На этой странице");
  let tocCount = 0;

  const body = el("div", "page-body");

  for (const block of def.blocks) {
    switch (block.kind) {
      case "head": {
        const h = el("h2", null, block.title);
        h.id = block.id;
        h.appendChild(anchorButton(def.id, block.id));
        body.appendChild(h);
        const link = el("a", null, block.title);
        link.href = routeHref(def.id, block.id);
        toc.appendChild(link);
        tocCount++;
        if (block.lede) body.appendChild(html("p", block.lede, "lede"));
        break;
      }
      case "text":
        body.appendChild(html("p", block.html, block.cls ?? "dim"));
        break;
      case "note":
        body.appendChild(html("p", block.html, "note"));
        break;
      case "legend":
        body.appendChild(legend(block.items));
        break;
      case "toggle":
        body.appendChild(toggleRow(block));
        break;
      case "table":
        body.appendChild(table(block));
        break;
      case "stands":
      case "split":
        body.appendChild(standsGrid(block, def.id));
        break;
      case "live": {
        const host = el("div", "live-block");
        host.id = block.id;
        block.render(host);
        body.appendChild(host);
        break;
      }
    }
  }

  if (tocCount > 1) view.appendChild(toc);
  view.appendChild(body);
  commitScenes();
}

function toggleRow(block: Extract<Block, { kind: "toggle" }>): HTMLElement {
  if (block.initial !== undefined && !toggles.isOn(block.id)) toggles.setOn(block.id, block.initial);

  const row = el("div", "toggle-row");
  const btn = el("button", null, block.label);
  btn.type = "button";
  btn.dataset["active"] = String(toggles.isOn(block.id));
  btn.addEventListener("click", () => {
    btn.dataset["active"] = String(toggles.toggle(block.id));
  });
  row.appendChild(btn);
  if (block.note) row.appendChild(html("span", block.note, "dim"));
  return row;
}

function legend(items: Array<{ color: string; text: string }>): HTMLElement {
  const box = el("div", "legend");
  for (const it of items) {
    const span = el("span");
    const swatch = el("i", "swatch");
    swatch.style.background = it.color;
    span.appendChild(swatch);
    span.appendChild(document.createTextNode(` ${it.text}`));
    box.appendChild(span);
  }
  return box;
}

function table(block: Extract<Block, { kind: "table" }>): HTMLElement {
  const scroller = el("div", "scroller");
  const t = el("table");
  if (block.head) {
    const tr = el("tr");
    for (const h of block.head) tr.appendChild(el("th", null, h));
    t.appendChild(tr);
  }
  for (const row of block.rows) {
    const tr = el("tr");
    for (const cell of row) tr.appendChild(html("td", cell));
    t.appendChild(tr);
  }
  scroller.appendChild(t);
  return scroller;
}

function standsGrid(block: Extract<Block, { kind: "stands" | "split" }>, pageId: string): HTMLElement {
  const wide = block.kind === "split";
  const grid = el("div", wide ? "split" : "stands");
  for (const stand of block.items) grid.appendChild(standCard(stand, pageId, wide));
  return grid;
}

function standCard(stand: StandDef, pageId: string, wide: boolean): HTMLElement {
  const card = el("article", `stand ${stand.status}`);
  card.id = stand.id;

  if (stand.draw) {
    const [w, h] = stand.size ?? (wide ? [480, 330] : [320, 280]);
    const canvas = el("canvas");
    canvas.width = w;
    canvas.height = h;
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", stand.title);
    // Сцена в сетке мелкая — тонкую графику в ней не разглядеть, поэтому она открывается крупно.
    canvas.title = "Открыть крупно";
    canvas.addEventListener("click", () => lightbox.open(stand));
    card.appendChild(canvas);
    watch(canvas, stand, w, h);
    scenes.push({ stand, w, h });
  }

  const body = el("div", "stand-body");

  const tagText = stand.tag ?? STATUS_LABEL[stand.status];
  if (tagText) body.appendChild(el("p", `tag st-${stand.status}`, tagText));

  const title = el("h3", null, stand.title);
  title.appendChild(anchorButton(pageId, stand.id));
  body.appendChild(title);

  if (stand.note) body.appendChild(html("p", stand.note, "dim"));

  if (stand.facts) {
    const ul = el("ul", "facts");
    for (const [name, value] of stand.facts) {
      const li = el("li");
      li.appendChild(el("span", null, name));
      li.appendChild(el("span", null, value));
      ul.appendChild(li);
    }
    body.appendChild(ul);
  }

  if (stand.verdict) {
    body.appendChild(html("p", stand.verdict, `verdict${stand.status === "accepted" ? " pick" : ""}`));
  }
  // Сшивка с каноном: из картинки должно находиться «почему так», иначе решение будет принято заново.
  if (stand.decision) body.appendChild(el("p", "decision", `решение ${stand.decision}`));

  card.appendChild(body);
  return card;
}

/* ---------- якорь: клик кладёт ссылку на стенд в буфер ----------
   Ради этого сайт и переписан: раньше ссылку на кусок стенда кинуть было нельзя. */

function anchorButton(pageId: string, anchor: string): HTMLElement {
  const btn = el("button", "anchor", "#");
  btn.type = "button";
  btn.title = "Скопировать ссылку на этот стенд";
  btn.addEventListener("click", (e) => {
    e.preventDefault();
    e.stopPropagation();
    const href = routeHref(pageId, anchor);
    const url = location.href.split("#")[0] + href;
    history.replaceState(null, "", href);
    const done = (): void => {
      btn.dataset["copied"] = "true";
      setTimeout(() => btn.removeAttribute("data-copied"), 1400);
    };
    if (navigator.clipboard?.writeText) navigator.clipboard.writeText(url).then(done, done);
    else done(); // без разрешения на буфер: адрес всё равно обновлён, копировать руками
  });
  return btn;
}
