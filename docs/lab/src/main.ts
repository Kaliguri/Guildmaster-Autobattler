/* Каркас Лаборатории: шапка, маршрут, транспорт показа, поиск.

   Почему одностраничное приложение, а не набор html-файлов: стенд держит под сотню canvas, и до
   роутера все они тикали каждый кадр, хотя видно два. Роутер оставляет в отрисовке только открытый
   раздел, IntersectionObserver внутри раздела — только то, что на экране (stage.ts).

   Почему без фреймворка: девяносто процентов содержимого — canvas, который рисуется императивно.
   React вокруг canvas был бы обёрткой ради обёртки, а роутер здесь занимает сорок строк. */

import * as clock from "./clock.js";
import { clear, el } from "./dom.js";
import { AREAS, PAGES, areaOf, pagesOf } from "./registry.js";
import { invalidateSizes, paint, reset } from "./stage.js";
import type { AreaDef, PageDef, SectionDef } from "./types.js";
import { eachStand, hero, renderArea, renderHome, renderLegacy, renderSection, routeHref } from "./views.js";

const loaded = new Map<string, SectionDef>();
const pending = new Map<string, Promise<SectionDef | null>>();

/* ---------- маршрут: #/раздел/якорь ----------
   Двухуровневый нарочно: ссылка на конкретный стенд — то, ради чего сайт и переписан. */

interface Route {
  page: string;
  anchor: string | null;
}

/** Плоские якоря из старых ссылок: адреса такого вида уехали в append-only журнал, где их не
 *  переписать. Раздел для них ищется по этой таблице, иначе ссылка привела бы в никуда. */
const LEGACY_ANCHORS: Record<string, string> = {
  melee: "hits",
  archetypes: "hits",
  hit: "hits",
  hybrid: "hits",
  ranged: "hits",
  zones: "zones",
  shield: "barrier",
  status: "status",
  effects: "status"
};

function parseRoute(): Route {
  const raw = location.hash;
  if (!raw.startsWith("#/")) {
    const flat = raw.slice(1);
    if (!flat) return { page: "index", anchor: null };
    return { page: LEGACY_ANCHORS[flat] ?? "index", anchor: flat };
  }
  const parts = raw.slice(2).split("/").filter(Boolean);
  return {
    page: parts[0] ?? "index",
    anchor: parts[1] ? decodeURIComponent(parts[1]) : null
  };
}

function pageById(id: string): PageDef | undefined {
  return PAGES.find((p) => p.id === id);
}

async function loadPage(id: string): Promise<SectionDef | null> {
  const existing = loaded.get(id);
  if (existing) return existing;

  const page = pageById(id);
  if (!page?.load) return null; // музей отклонённого собирает каркас, файла у него нет

  const inFlight = pending.get(id);
  if (inFlight) return inFlight;

  const job = page
    .load()
    .then((mod) => {
      loaded.set(id, mod.default);
      return mod.default;
    })
    .catch((err: unknown) => {
      console.error(`Лаборатория: раздел «${id}» не загрузился`, err);
      return null;
    });

  pending.set(id, job);
  return job;
}

function loadAll(): Promise<Array<SectionDef | null>> {
  return Promise.all(PAGES.map((p) => loadPage(p.id)));
}

/* ---------- шапка ---------- */

function buildBar(): void {
  const bar = el("header", "lab-bar");

  const top = el("div", "lab-row lab-row-top");
  const brand = el("a", "lab-brand");
  brand.href = "#/";
  brand.innerHTML = "<b>Guildmaster &middot; Лаборатория</b><span>как мы это видим</span>";
  top.appendChild(brand);

  // Верхний ряд: области. Он отвечает на вопрос «в какой части сайта я нахожусь».
  const areas = el("nav", "lab-areas");
  areas.setAttribute("aria-label", "Области");
  areas.id = "lab-areas";
  top.appendChild(areas);

  const find = el("button", "lab-find");
  find.type = "button";
  find.innerHTML = "поиск <kbd>K</kbd>";
  find.addEventListener("click", () => void openSearch());
  top.appendChild(find);

  // Нижний ряд: разделы текущей области. Пустеет на главной — там выбирать ещё нечего.
  const sections = el("nav", "lab-row lab-sections");
  sections.setAttribute("aria-label", "Разделы");
  sections.id = "lab-sections";

  bar.append(top, sections);
  document.body.insertBefore(bar, document.body.firstChild);
}

function syncNav(routeId: string): void {
  const area = areaOf(routeId);
  const areasHost = document.getElementById("lab-areas");
  const sectionsHost = document.getElementById("lab-sections");
  if (!areasHost || !sectionsHost) return;

  clear(areasHost);
  for (const a of AREAS) {
    const link = el("a", null, a.title);
    link.href = `#/${a.id}`;
    if (a.id === area?.id) link.setAttribute("aria-current", "page");
    areasHost.appendChild(link);
  }

  clear(sectionsHost);
  if (!area) {
    sectionsHost.hidden = true;
    return;
  }
  sectionsHost.hidden = false;

  const overview = el("a", "lab-overview", area.title);
  overview.href = `#/${area.id}`;
  if (routeId === area.id) overview.setAttribute("aria-current", "page");
  sectionsHost.appendChild(overview);

  const pages = pagesOf(area.id);
  for (const page of pages) {
    const link = el("a", null, page.title);
    link.href = page.href ?? routeHref(page.id);
    if (page.href) link.target = "_blank";
    if (page.id === routeId) link.setAttribute("aria-current", "page");
    sectionsHost.appendChild(link);
  }

  // Музей общесайтовый: он собирает проигравшие варианты со ВСЕХ разделов, а объявлен внутри одной
  // области. Без этой ссылки отклонённое из молодых областей выглядит потерянным — что и случилось
  // с картой: мост и архипелаг лежали в музее, но дойти до него из «Карты» было нечем.
  if (!pages.some((p) => p.id === "legacy")) {
    const link = el("a", null, "Отклонённое");
    link.href = routeHref("legacy");
    if (routeId === "legacy") link.setAttribute("aria-current", "page");
    sectionsHost.appendChild(link);
  }
}

/* ---------- транспорт показа ---------- */

function buildTransport(): HTMLElement {
  const host = el("div", "transport");
  host.setAttribute("role", "group");
  host.setAttribute("aria-label", "Управление показом");
  host.innerHTML =
    '<button id="btn-play" type="button"></button>' +
    '<button id="btn-prev" type="button">&#8249; кадр</button>' +
    '<button id="btn-next" type="button">кадр &#8250;</button>' +
    '<label class="tag" for="speed">темп' +
    '<input id="speed" type="range" min="10" max="100" value="100" step="10" aria-label="Темп показа">' +
    '<span id="speed-out">100%</span></label>' +
    `<div class="readout"><span>кадр <b id="out-frame">00</b>/${clock.TOTAL - 1}</span></div>`;

  const play = host.querySelector<HTMLButtonElement>("#btn-play");
  const prev = host.querySelector<HTMLButtonElement>("#btn-prev");
  const next = host.querySelector<HTMLButtonElement>("#btn-next");
  const speed = host.querySelector<HTMLInputElement>("#speed");
  const speedOut = host.querySelector<HTMLElement>("#speed-out");
  if (!play || !prev || !next || !speed || !speedOut) return host;

  const syncPlay = (): void => {
    play.textContent = clock.isPlaying() ? "Пауза" : "Играть";
    play.dataset["active"] = String(!clock.isPlaying());
  };
  syncPlay();

  play.addEventListener("click", () => {
    clock.setPlaying(!clock.isPlaying());
    syncPlay();
  });

  const stepBy = (delta: number): void => {
    clock.setPlaying(false);
    syncPlay();
    clock.stepBy(delta);
    render();
  };
  prev.addEventListener("click", () => stepBy(-1));
  next.addEventListener("click", () => stepBy(1));

  speed.addEventListener("input", () => {
    clock.setSpeed(Number(speed.value) / 100);
    speedOut.textContent = `${speed.value}%`;
  });

  return host;
}

/* ---------- поиск ----------
   Стендов уже под сотню; листать глазами перестало работать примерно на сорока. */

async function openSearch(): Promise<void> {
  let box = document.getElementById("lab-search");
  if (box) {
    box.hidden = false;
    box.querySelector("input")?.focus();
    return;
  }

  box = el("div", "search");
  box.id = "lab-search";
  const card = el("div", "search-card");
  const input = el("input");
  input.type = "search";
  input.placeholder = "Стенд, вариант, эффект…";
  input.setAttribute("aria-label", "Поиск по стендам");
  const list = el("div", "search-list");
  card.append(input, list);
  box.appendChild(card);
  document.body.appendChild(box);

  const hide = (): void => {
    if (box) box.hidden = true;
  };
  box.addEventListener("click", (e) => {
    if (e.target === box) hide();
  });
  input.addEventListener("input", () => fillSearch(list, input.value, hide));
  input.addEventListener("keydown", (e) => {
    if (e.key === "Escape") hide();
    if (e.key === "Enter") list.querySelector("a")?.click();
  });

  fillSearch(list, "", hide);
  input.focus();
  await loadAll();
  fillSearch(list, input.value, hide); // разделы догрузились — показать полный список
}

function fillSearch(list: HTMLElement, query: string, hide: () => void): void {
  const q = query.trim().toLowerCase();
  clear(list);
  let hits = 0;

  for (const page of PAGES) {
    const def = loaded.get(page.id);
    if (!def) continue;
    eachStand(def, (stand) => {
      if (hits >= 40) return;
      const hay = `${stand.title} ${stand.note ?? ""} ${page.title}`.toLowerCase();
      if (q && !hay.includes(q)) return;
      hits++;
      const a = el("a");
      a.href = routeHref(page.id, stand.id);
      a.innerHTML = `<b>${stand.title}</b><span>${page.title} · ${stand.status}</span>`;
      a.addEventListener("click", hide);
      list.appendChild(a);
    });
  }

  if (hits === 0) list.appendChild(el("p", "dim", "Ничего не нашлось."));
}

/* ---------- отрисовка маршрута ---------- */

async function renderRoute(): Promise<void> {
  const route = parseRoute();
  const view = document.getElementById("lab-view");
  if (!view) return;

  reset();
  clear(view);
  syncNav(route.page);

  const area = AREAS.find((a) => a.id === route.page);
  // Главной и обзору области нужны все разделы: они показывают живые превью и счётчики.
  const wholeSite = route.page === "index" || route.page === "legacy" || area !== undefined;
  if (wholeSite) await loadAll();
  else await loadPage(route.page);

  // Пока грузились, могли уйти дальше: рисовать устаревший маршрут поверх нового нельзя.
  if (parseRoute().page !== route.page) return;

  reset();
  clear(view);

  if (route.page === "index") {
    renderHome(view, AREAS, PAGES, loaded);
  } else if (area) {
    renderArea(view, area, pagesOf(area.id), loaded);
  } else if (route.page === "legacy") {
    renderLegacy(view, PAGES, loaded);
  } else {
    const def = loaded.get(route.page);
    if (!def) {
      const page = pageById(route.page);
      view.appendChild(
        hero(
          page ? page.title : "Раздела нет",
          page ? "Раздел объявлен, но его файл не загрузился." : "Такого раздела в лаборатории нет."
        )
      );
    } else {
      if (def.transport !== false) view.appendChild(buildTransport());
      renderSection(view, def);
    }
  }

  paint();
  scrollToAnchor(route.anchor);
}

function scrollToAnchor(anchor: string | null): void {
  window.scrollTo({ top: 0 });
  if (!anchor) return;
  // Кадр на вёрстку: до неё scrollIntoView уедет по устаревшим координатам.
  requestAnimationFrame(() => {
    const target = document.getElementById(anchor);
    if (!target) return;
    target.scrollIntoView();
    // Подсветка: в сетке из двадцати карточек иначе непонятно, на какую именно тебя привели.
    target.dataset["flash"] = "true";
    setTimeout(() => target.removeAttribute("data-flash"), 1600);
  });
}

function render(): void {
  paint();
  const out = document.getElementById("out-frame");
  if (out) out.textContent = String(clock.frame).padStart(2, "0");
}

/* ---------- наверх ---------- */

function buildToTop(): void {
  const btn = el("button", "to-top", "наверх");
  btn.type = "button";
  btn.addEventListener("click", () => window.scrollTo({ top: 0, behavior: "smooth" }));
  document.body.appendChild(btn);

  const sync = (): void => {
    btn.dataset["visible"] = String(window.scrollY > 600);
  };
  window.addEventListener("scroll", sync, { passive: true });
  sync();
}

/* ---------- старт ---------- */

function boot(): void {
  buildBar();
  buildToTop();

  window.addEventListener("hashchange", () => void renderRoute());
  window.addEventListener("resize", () => {
    invalidateSizes();
    render();
  });
  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
      e.preventDefault();
      void openSearch();
    }
  });

  void renderRoute();
  clock.start(render);
}

if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
else boot();
