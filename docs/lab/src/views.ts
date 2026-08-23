/* Отрисовка страниц из данных: витрина, раздел, музей отклонённого.

   Ни одна из трёх не ведётся руками. Витрина берёт превью из самого раздела, музей — фильтр по
   статусу стенда. Список, который ведут отдельно от содержимого, расходится с ним за неделю. */

import { clear, el, html } from "./dom.js";
import { watch } from "./stage.js";
import * as lightbox from "./lightbox.js";
import * as toggles from "./toggles.js";
import type { AreaDef, Block, PageDef, SectionDef, StandDef } from "./types.js";

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

/* ---------- главная: области ---------- */

/** Догрузка раздела по требованию. Витрина знает id разделов из реестра, но их СОДЕРЖИМОЕ (живое
 *  превью, счётчик стендов) лежит в модуле раздела — а модулей 64 штуки на 2.6 МБ. Раньше витрина
 *  ждала их все: главная грузила весь сайт ради пяти картинок. Теперь модуль грузится тогда,
 *  когда карточка доехала до экрана. */
export type Ensure = (pageId: string) => Promise<SectionDef | null>;

export function renderHome(
  view: HTMLElement, areas: AreaDef[], pages: PageDef[], loaded: Map<string, SectionDef>, ensure: Ensure
): void {
  beginScenes();
  view.appendChild(
    hero(
      "Лаборатория",
      "Всё, на что надо посмотреть, а не прочитать: как выглядит удар, что делает статус, чем занят " +
        "барьер, во что обходится баланс. Замысел и правила живут в ГДД — сюда приходит только то, " +
        "чего текст не умеет."
    )
  );

  const grid = el("div", "cards areas");
  for (const area of areas) {
    const inside = pages.filter((p) => p.area === area.id);
    const card = el("a", "card-link");
    card.href = `#/${area.id}`;

    // Превью области — первая живая сцена любого её раздела: карточка обязана показывать, что внутри.
    const slot = el("div", "card-slot");
    if (area.icon) slot.appendChild(el("div", "card-mark", area.icon));
    card.appendChild(slot);
    lazyCover(slot, inside.map((p) => p.id), loaded, ensure);

    const body = el("div", "card-text");
    body.appendChild(el("h3", null, area.title));
    body.appendChild(el("p", "dim", area.blurb));
    // Полки, а не разделы: у «Интерфейса» их двадцать шесть, и списком имён карточка превращалась
    // в простыню микрокапса на десять строк — прочитать её нельзя, а высоту сетки она ломала.
    const shelves = area.shelves ?? [];
    const names = shelves.length > 0
      ? shelves.filter((sh) => inside.some((p) => p.shelf === sh.id)).map((sh) => sh.title)
      : inside.map((p) => p.title);
    body.appendChild(el("p", "tag", names.join(" · ")));
    card.appendChild(body);
    grid.appendChild(card);
  }
  view.appendChild(grid);
  commitScenes();
}

/* Ленивое превью.

   Наблюдатель, а не загрузка всего: 64 модуля на 2.6 МБ парсились при каждом заходе на главную и
   на обзор ЛЮБОЙ области, хотя на экране пять-шесть карточек. Раздел грузится, когда его карточка
   доехала до окна, и ровно один раз.

   Порог 200 px: карточка успевает нарисоваться до того, как попадёт в поле зрения, и подстановка
   картинки не мелькает под курсором. */
const lazyWatcher = new IntersectionObserver(
  (entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      lazyWatcher.unobserve(entry.target);
      const job = lazyJobs.get(entry.target);
      lazyJobs.delete(entry.target);
      void job?.();
    }
  },
  { rootMargin: "200px" }
);
const lazyJobs = new Map<Element, () => Promise<void>>();

function lazyCover(slot: HTMLElement, ids: string[], loaded: Map<string, SectionDef>, ensure: Ensure): void {
  const fill = async (): Promise<void> => {
    for (const id of ids) {
      const def = loaded.get(id) ?? (await ensure(id));
      const stand = def ? coverStand(def) : null;
      if (!stand) continue;
      clear(slot);
      const box = coverBox(stand);
      slot.appendChild(box);
      // Сцена приехала после общей сборки страницы — лайтбокс узнаёт о ней отдельно.
      addLightboxItems([{ kind: "scene", stand, w: stand.size?.[0] ?? 320, h: stand.size?.[1] ?? 280 }]);
      return;
    }
  };
  lazyJobs.set(slot, fill);
  lazyWatcher.observe(slot);
}

/** Счётчик стендов на карточке раздела — тоже лениво: он живёт в модуле раздела. */
function lazyTally(host: HTMLElement, pageId: string, loaded: Map<string, SectionDef>, ensure: Ensure): void {
  const fill = async (): Promise<void> => {
    const def = loaded.get(pageId) ?? (await ensure(pageId));
    if (!def) return;
    const text = tallyText(def);
    if (text) host.textContent = text;
  };
  lazyJobs.set(host, fill);
  lazyWatcher.observe(host);
}

/* Превью для карточки. Сцена рисуется в СВОЁМ логическом размере, иначе стенд, рассчитанный на
   320×280, получал чужие 320×200 и подписи с барами уезжали за край. Окно фиксированной высоты
   с overflow кропит сцену симметрично и заодно равняет карточки между собой. */
function coverBox(stand: StandDef): HTMLElement {
  const [w, h] = stand.size ?? [320, 280];
  const box = el("div", "card-cover");
  const canvas = el("canvas");
  canvas.width = w;
  canvas.height = h;
  box.appendChild(canvas);
  watch(canvas, stand, w, h);
  scenes.push({ kind: "scene", stand, w, h });
  return box;
}

/* ---------- обзор области ---------- */

export function renderArea(
  view: HTMLElement, area: AreaDef, pages: PageDef[], loaded: Map<string, SectionDef>, ensure: Ensure
): void {
  beginScenes();
  view.appendChild(hero(area.title, area.blurb, "Лаборатория · область"));

  const shelves = area.shelves ?? [];
  const loose = pages.filter((p) => !p.shelf);
  if (loose.length > 0 || shelves.length === 0) {
    view.appendChild(cardGrid(loose.length > 0 ? loose : pages, loaded, ensure));
  }

  // Полки повторяют раскладку боковой колонки: если в навигации «Узлы карты» — это пять экранов,
  // на витрине они обязаны стоять теми же пятью, а не вперемешку по алфавиту.
  for (const shelf of shelves) {
    const items = pages.filter((p) => p.shelf === shelf.id);
    if (items.length === 0) continue;
    const box = el("section");
    const head = el("h2", null, shelf.title);
    head.id = shelf.id;
    box.appendChild(head);
    box.appendChild(cardGrid(items, loaded, ensure));
    view.appendChild(box);
  }

  commitScenes();
}

function cardGrid(pages: PageDef[], loaded: Map<string, SectionDef>, ensure: Ensure): HTMLElement {
  const grid = el("div", "cards");
  for (const page of pages) grid.appendChild(indexCard(page, loaded, ensure));
  return grid;
}

function indexCard(page: PageDef, loaded: Map<string, SectionDef>, ensure: Ensure): HTMLElement {
  const card = el("a", "card-link");
  card.href = page.href ?? routeHref(page.id);
  if (page.href) card.target = "_blank";

  // Живое превью, а не скриншот: список ссылок не говорит, что внутри, а снимок устаревает молча.
  const slot = el("div", "card-slot");
  if (page.icon) slot.appendChild(el("div", "card-mark", page.icon));
  card.appendChild(slot);
  if (!page.href) lazyCover(slot, [page.id], loaded, ensure);

  const body = el("div", "card-text");
  body.appendChild(el("h3", null, page.title));
  body.appendChild(el("p", "dim", page.blurb));
  if (page.href) {
    body.appendChild(el("p", "tag", "отдельное приложение · откроется в новой вкладке"));
  } else {
    const tag = el("p", "tag");
    body.appendChild(tag);
    lazyTally(tag, page.id, loaded, ensure);
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

/** Счётчик показываем ровно настолько, насколько ему есть что сказать: у раздела без развилок
 *  «принято 0» читалось бы как «ничего не решено», хотя решать там нечего. */
function tallyText(def: SectionDef): string {
  let total = 0;
  let accepted = 0;
  let waiting = 0;
  eachStand(def, (s) => {
    total++;
    if (s.status === "accepted") accepted++;
    else if (s.status === "waiting") waiting++;
  });
  const parts: string[] = [];
  if (total > 0) parts.push(`${total} стендов`);
  if (accepted > 0) parts.push(`принято ${accepted}`);
  if (waiting > 0) parts.push(`ждёт ${waiting}`);
  return parts.join(" · ");
}

/* ---------- сквозные срезы по статусу ----------
   Статус у стенда был с первого дня, а вопрос «что от меня ждут» сайт не умел: «ждёт 5» стояло на
   карточке раздела, и собрать эти пятёрки в одно место было нечем. Срез собирается тем же
   проходом, что и музей отклонённого, — поэтому расходиться им не с чем. */

interface SliceCopy {
  title: string;
  lede: string;
  empty: string;
}

const SLICE: Record<"waiting" | "rejected", SliceCopy> = {
  waiting: {
    title: "Ждут вердикта",
    lede:
      "Всё нарисованное, по чему решения ещё нет. Это единственная страница сайта, обращённая к " +
      "Максу с вопросом, а не с ответом: вариант без вердикта не мёртв и не принят — он ждёт.",
    empty: "Ни одного стенда без вердикта — всё решено."
  },
  rejected: {
    title: "Отклонённое",
    lede:
      "Варианты, которые проиграли, — живыми, а не описанием. Музей полезен ровно тем, что показывает, " +
      "ЧЕМ проигравший был хуже: через полгода «мы это уже пробовали» без картинки звучит " +
      "неубедительно и пробуется заново.",
    empty: "Пока ни один вариант не отклонён."
  }
};

export function renderSlice(
  view: HTMLElement, status: "waiting" | "rejected", pages: PageDef[], loaded: Map<string, SectionDef>
): void {
  beginScenes();
  const copy = SLICE[status];
  view.appendChild(hero(copy.title, copy.lede));

  // Счётчик в подзаголовке: «сколько всего ждёт меня» — первый вопрос к этой странице, а
  // пересчитывать карточки глазами в списке на полсотни штук никто не станет.
  const stat = el("p", "tag");
  view.appendChild(stat);

  let count = 0;
  let sections = 0;
  for (const page of pages) {
    const def = loaded.get(page.id);
    if (!def) continue;
    const items: StandDef[] = [];
    eachStand(def, (s) => {
      if (s.status === status) items.push(s);
    });
    if (items.length === 0) continue;
    count += items.length;
    sections++;

    const wrap = el("section");
    const head = el("h2", null, page.title);
    const back = el("a", "h-link", "к разделу →");
    back.href = routeHref(page.id);
    head.appendChild(back);
    wrap.appendChild(head);
    wrap.appendChild(standsGrid({ kind: "stands", items }, page.id));
    view.appendChild(wrap);
  }

  stat.textContent = count === 0 ? "" : `${count} стендов в ${sections} разделах`;
  if (count === 0) view.appendChild(el("p", "dim", copy.empty));
  commitScenes();
}

/* ---------- раздел ---------- */

/** Всё крупноувеличиваемое на текущей странице по порядку: его листает лайтбокс. Собирается тем же
 *  проходом, что и разметка, поэтому разойтись с ней не может. Раздел может дописать сюда своё —
 *  плитки цвета делают именно это. */
let scenes: lightbox.Item[] = [];

function beginScenes(): void {
  scenes = [];
}

function commitScenes(): void {
  lightbox.setItems(scenes);
}

/** Живой блок наполняется асинхронно и попадает в список позже остальных. */
export function addLightboxItems(extra: lightbox.Item[]): void {
  scenes = scenes.concat(extra);
  lightbox.setItems(scenes);
}

/** Раздел. Оглавление уходит в СВОЮ колонку (tocHost), а не в поток страницы: чипами над текстом
 *  оно уезжало вверх при первой же прокрутке, а разделы бывают на двенадцать блоков. */
export function renderSection(view: HTMLElement, def: SectionDef, tocHost: HTMLElement | null): void {
  beginScenes();
  view.appendChild(hero(def.title, def.lede, def.eyebrow));

  const tocLinks: HTMLAnchorElement[] = [];
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
        tocLinks.push(link);
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

  view.appendChild(body);
  // Оглавление на один пункт не оглавление, а повтор заголовка.
  if (tocCount > 1 && tocHost) {
    tocHost.appendChild(el("p", "lab-toc-title", "На этой странице"));
    for (const link of tocLinks) tocHost.appendChild(link);
    spyHeadings(body, tocLinks);
  }
  commitScenes();
}

/* Подсветка текущего места в оглавлении.

   Наблюдатель, а не расчёт по scrollY: у нас страница с сотней канвасов, и считать положение
   заголовков на каждый кадр прокрутки — та же цена, что и рисовать сцену. Верхняя граница окна
   поднята на 45% высоты, чтобы активным становился заголовок, ДОШЕДШИЙ до верха экрана, а не тот,
   что едва показался снизу. */
let spy: IntersectionObserver | null = null;

function spyHeadings(body: HTMLElement, links: HTMLAnchorElement[]): void {
  spy?.disconnect();
  const heads = Array.from(body.querySelectorAll("h2[id]"));
  if (heads.length === 0) return;

  const byId = new Map<string, HTMLAnchorElement>();
  for (const link of links) {
    const id = decodeURIComponent(link.href.split("/").pop() ?? "");
    if (id) byId.set(id, link);
  }

  const seen = new Set<string>();
  const mark = (): void => {
    // Активен последний из уже пройденных заголовков: между двумя видимыми глаз читает верхний.
    let current: string | null = null;
    for (const head of heads) if (seen.has(head.id)) current = head.id;
    for (const [id, link] of byId) {
      if (id === current) link.dataset["active"] = "true";
      else link.removeAttribute("data-active");
    }
  };

  spy = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) seen.add(entry.target.id);
        else seen.delete(entry.target.id);
      }
      mark();
    },
    { rootMargin: "-45% 0px -50% 0px" }
  );
  for (const head of heads) spy.observe(head);
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
    card.appendChild(canvas);
    watch(canvas, stand, w, h);
    scenes.push({ kind: "scene", stand, w, h });
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

  if (stand.draw) {
    // Нажать можно куда угодно в карточке: искать глазами кликабельную область — лишняя работа.
    // Кнопка-якорь внутри гасит своё событие сама, поэтому ссылку она по-прежнему копирует.
    card.classList.add("zoomable");
    card.title = "Открыть крупно";
    card.addEventListener("click", () => lightbox.open(stand));
  }
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
