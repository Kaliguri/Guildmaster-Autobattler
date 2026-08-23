/* Каркас Лаборатории: шапка, маршрут, транспорт показа, поиск.

   Почему одностраничное приложение, а не набор html-файлов: стенд держит под сотню canvas, и до
   роутера все они тикали каждый кадр, хотя видно два. Роутер оставляет в отрисовке только открытый
   раздел, IntersectionObserver внутри раздела — только то, что на экране (stage.ts).

   Почему без фреймворка: девяносто процентов содержимого — canvas, который рисуется императивно.
   React вокруг canvas был бы обёрткой ради обёртки, а роутер здесь занимает сорок строк. */

import * as clock from "./clock.js";
import { clear, el } from "./dom.js";
import { AREAS, PAGES, areaOf, pagesOf } from "./registry.js";
import { hasLive, invalidateSizes, paint, register, reset } from "./stage.js";
import type { AreaDef, PageDef, SectionDef, StandDef } from "./types.js";
import { eachStand, hero, renderArea, renderHome, renderSection, renderSlice, routeHref } from "./views.js";

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
  // Хвост «?…» — параметры раздела (kit=, mode=): их читают сами разделы прямо из location.hash, а
  // маршруту он мешает. Без этой строки `#/balance-kits?kit=Assassin` искался как раздел с именем
  // «balance-kits?kit=Assassin» — то есть КАЖДАЯ ссылка на кита из таблиц вела на «Раздела нет».
  const raw = location.hash.split("?")[0] ?? "";
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

/* ---------- боковая колонка ----------
   Была шапка в два ряда: верхний — области, нижний — разделы. На двадцати шести разделах области
   «Интерфейс» нижний ряд расползался в три строки капса и переставал отвечать «где я». Колонка
   со своей прокруткой показывает дерево целиком: области, внутри текущей — полки и разделы. */

/** Сквозные страницы: собираются по ВСЕМ разделам, поэтому не принадлежат ни одной области.
 *  В дереве области они прячутся — иначе «Отклонённое» висело бы в «Джусе», где оно объявлено
 *  по историческим причинам, и выглядело бы его частью. */
const CROSS: Array<{ id: string; title: string }> = [
  { id: "waiting", title: "Ждут вердикта" },
  { id: "legacy", title: "Отклонённое" }
];
const CROSS_IDS = new Set(CROSS.map((c) => c.id));

function buildSide(): void {
  const side = document.getElementById("lab-side");
  if (!side) return;

  const brand = el("a", "lab-brand");
  brand.href = "#/";
  brand.innerHTML = "<b>Guildmaster &middot; Лаборатория</b><span>как мы это видим</span>";

  const find = el("button", "lab-find");
  find.type = "button";
  find.innerHTML = "поиск <kbd>K</kbd>";
  find.addEventListener("click", () => void openSearch());

  const nav = el("nav", "lab-nav");
  nav.id = "lab-nav";
  nav.setAttribute("aria-label", "Области и разделы");

  const cross = el("nav", "nav-cross");
  cross.id = "lab-cross";
  cross.setAttribute("aria-label", "Сквозные страницы");

  side.append(brand, find, nav, cross);

  // Кнопка меню нужна только там, где колонка уезжает за край экрана.
  const burger = el("button", "lab-burger", "меню");
  burger.type = "button";
  burger.addEventListener("click", () => {
    if (document.body.dataset["menu"] === "open") delete document.body.dataset["menu"];
    else document.body.dataset["menu"] = "open";
  });
  document.body.appendChild(burger);
}

function navPage(page: PageDef, routeId: string): HTMLElement {
  const link = el("a", "nav-page", page.title);
  link.href = page.href ?? routeHref(page.id);
  if (page.href) link.target = "_blank";
  if (page.id === routeId) link.setAttribute("aria-current", "page");
  return link;
}

/** Разделы области по полкам. Полка без разделов не рисуется: пустой заголовок обещает
 *  содержимое, которого нет. */
function areaBranch(area: AreaDef, routeId: string): HTMLElement {
  const box = el("div", "nav-inner");
  const pages = pagesOf(area.id).filter((p) => !CROSS_IDS.has(p.id));
  const shelves = area.shelves ?? [];

  for (const page of pages.filter((p) => !p.shelf)) box.appendChild(navPage(page, routeId));

  for (const shelf of shelves) {
    const items = pages.filter((p) => p.shelf === shelf.id);
    if (items.length === 0) continue;
    box.appendChild(el("p", "nav-shelf", shelf.title));
    for (const page of items) box.appendChild(navPage(page, routeId));
  }

  // Полка, которой нет в объявлении области: без этой ветки раздел пропал бы из навигации МОЛЧА,
  // а опечатка в id полки — ровно та ошибка, которую глазами не видно.
  const orphans = pages.filter((p) => p.shelf && !shelves.some((sh) => sh.id === p.shelf));
  if (orphans.length > 0) {
    box.appendChild(el("p", "nav-shelf", "полка не объявлена"));
    for (const page of orphans) box.appendChild(navPage(page, routeId));
  }
  return box;
}

function syncNav(routeId: string): void {
  const nav = document.getElementById("lab-nav");
  const cross = document.getElementById("lab-cross");
  if (!nav || !cross) return;

  // Раскрыта ровно одна область — та, в которой ты стоишь. Раскрыть все значило бы вернуть
  // простыню, от которой ушли, только вертикальную.
  const openId = areaOf(routeId)?.id ?? (AREAS.some((a) => a.id === routeId) ? routeId : null);

  clear(nav);
  for (const area of AREAS) {
    const link = el("a", "nav-area");
    link.href = `#/${area.id}`;
    link.appendChild(el("i", null, "\u25B8"));
    link.appendChild(el("span", null, area.title));
    if (area.id === openId) link.dataset["open"] = "true";
    if (area.id === routeId) link.setAttribute("aria-current", "page");
    nav.appendChild(link);
    if (area.id === openId) nav.appendChild(areaBranch(area, routeId));
  }

  clear(cross);
  for (const item of CROSS) {
    const link = el("a", null, item.title);
    link.href = routeHref(item.id);
    if (item.id === routeId) link.setAttribute("aria-current", "page");
    cross.appendChild(link);
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

/* ---------- режим съёмки ----------
   `?shot=<id,id>&scale=2&frame=0` — голая страница из одних канвасов, без шапки, транспорта и
   карточек. Заведён потому, что показать Максу картинку стенда стоило полутора минут: браузер
   поднимал ВЕСЬ раздел (сорок с лишним сцен), а нужна была одна.

   Ещё это адрес, который можно дать человеку: «посмотри вот на эти три варианта рядом» без
   прокрутки страницы и без объяснений, куда смотреть. */

interface ShotSpec {
  /** Что снимаем. Пусто или `*` — все рисующие стенды раздела из хеша. */
  ids: string[];
  /** Во сколько раз крупнее логического размера. Мелкие детали иначе не видно на снимке. */
  scale: number;
  frame: number;
}

function parseShot(): ShotSpec | null {
  const q = new URLSearchParams(location.search);
  const raw = q.get("shot");
  if (raw === null) return null;
  const ids = raw
    .split(",")
    .map((s) => s.trim())
    .filter((s) => s.length > 0 && s !== "*");
  const scale = Math.min(Math.max(Number(q.get("scale")) || 1, 0.25), 4);
  return { ids, scale, frame: Number(q.get("frame")) || 0 };
}

/** Стенды по id. Ищем сначала в разделе из хеша, а не найдя — по всем: помнить, в каком разделе
 *  лежит стенд, ради снимка не должно быть обязательным. */
function findStands(spec: ShotSpec, pageId: string): StandDef[] {
  const fromPage: StandDef[] = [];
  const def = loaded.get(pageId);
  if (def) eachStand(def, (s) => { if (s.draw) fromPage.push(s); });
  if (spec.ids.length === 0) return fromPage;

  const pool = new Map<string, StandDef>();
  for (const s of fromPage) pool.set(s.id, s);
  for (const page of PAGES) {
    const other = loaded.get(page.id);
    if (!other) continue;
    eachStand(other, (s) => { if (s.draw && !pool.has(s.id)) pool.set(s.id, s); });
  }
  return spec.ids.map((id) => pool.get(id)).filter((s): s is StandDef => s !== undefined);
}

async function renderShot(spec: ShotSpec): Promise<void> {
  const view = document.getElementById("lab-view");
  if (!view) return;

  document.body.dataset["shot"] = "true";
  clock.setPlaying(false);
  clock.setFrame(spec.frame);

  const pageId = parseRoute().page;
  await loadPage(pageId);
  let stands = findStands(spec, pageId);
  if (stands.length < spec.ids.length) {
    await loadAll(); // не всё нашлось в разделе — добираем по остальным
    stands = findStands(spec, pageId);
  }

  reset();
  clear(view);

  if (stands.length === 0) {
    view.appendChild(el("p", "dim", `Нечего снимать: ${spec.ids.join(", ") || pageId}`));
    document.body.dataset["ready"] = "true";
    return;
  }

  for (const stand of stands) {
    const [w, h] = stand.size ?? [320, 280];
    const box = el("figure", "shot-item");
    const canvas = el("canvas");
    canvas.width = w;
    canvas.height = h;
    canvas.style.width = `${Math.round(w * spec.scale)}px`;
    canvas.style.height = `${Math.round(h * spec.scale)}px`;
    box.appendChild(canvas);
    box.appendChild(el("figcaption", null, `${stand.id} · ${stand.title}`));
    view.appendChild(box);
    // register, а не watch: колонка снимков выше экрана, и наблюдатель погасил бы нижние —
    // на снимке они вышли бы пустыми.
    register(canvas, stand, w, h);
  }

  paint();
  // Флаг для скриншотера: рисовать больше нечего. Читается как body[data-ready] из devtools-протокола.
  document.body.dataset["ready"] = "true";
}

/* ---------- отрисовка маршрута ---------- */

/** Сквозные страницы-срезы: адрес -> статус, по которому собираются стенды со всего сайта.
 *  `legacy`, а не `rejected`, потому что на этот адрес ссылается append-only журнал решений,
 *  где ссылку не переписать. */
const SLICES: Record<string, "waiting" | "rejected" | undefined> = {
  waiting: "waiting",
  legacy: "rejected"
};


async function renderRoute(): Promise<void> {
  const route = parseRoute();
  const view = document.getElementById("lab-view");
  if (!view) return;
  const toc = document.getElementById("lab-toc");
  const shell = document.getElementById("lab-shell");

  reset();
  clear(view);
  if (toc) clear(toc);
  // Переход по ссылке из выехавшего меню обязан его закрыть: иначе оно накрывает то,
  // ради чего по ссылке и шли.
  delete document.body.dataset["menu"];
  syncNav(route.page);

  const area = AREAS.find((a) => a.id === route.page);
  // Сквозной срез — единственное место, которому честно нужны ВСЕ разделы: он собирает стенды по
  // статусу со всего сайта. Витрина обходится реестром: превью и счётчики она догружает лениво,
  // по появлению карточки в окне (раньше главная тянула все 64 модуля ради пяти картинок).
  const slice = SLICES[route.page];
  if (slice) {
    view.appendChild(el("p", "dim", "Собираю стенды по всем разделам…"));
    await loadAll();
  } else if (!area) {
    await loadPage(route.page);
  }

  // Пока грузились, могли уйти дальше: рисовать устаревший маршрут поверх нового нельзя.
  if (parseRoute().page !== route.page) return;

  reset();
  clear(view);
  if (toc) clear(toc);

  if (route.page === "index") {
    renderHome(view, AREAS, PAGES, loaded, loadPage);
  } else if (area) {
    renderArea(view, area, pagesOf(area.id), loaded, loadPage);
  } else if (slice) {
    renderSlice(view, slice, PAGES, loaded);
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
      renderSection(view, def, toc);
      // Транспорт ПОСЛЕ заголовка, а не перед ним: панель управления впереди названия страницы
      // читается как часть каркаса сайта, хотя управляет она этим разделом. Липкость от места
      // в потоке не страдает — при прокрутке он всё равно уходит к верхней кромке.
      if (def.transport !== false) view.insertBefore(buildTransport(), view.children[1] ?? null);
    }
  }

  // Колонка оглавления схлопывается, когда оглавления нет: пустая полоса справа читается как
  // сломанная вёрстка, а не как «у этой страницы нечего оглавлять».
  shell?.setAttribute("data-toc", String(toc !== null && toc.childElementCount > 0));

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
  const shot = parseShot();
  if (shot) {
    // Ни шапки, ни часов: снимку нужен один кадр, а не цикл отрисовки.
    void renderShot(shot);
    return;
  }

  buildSide();
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
  clock.start(render, hasLive);
}

if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
else boot();
