/* Замеры SimBench: таблицы по режимам, дельты с прошлым прогоном, отклонение от нормы роли.

   Генерация не тронута — `data.js` пишет прежний скрипт. Правило подачи сохранено, потому что оно
   верное: число само по себе почти ничего не значит, рядом с ним либо дельта, либо норма. */

import { el, html } from "../dom.js";
import type { SectionDef } from "../types.js";
import {
  balance, deviation, displayName, fmt, fmtValue, isNum, meta, modeTitle, modesOf, outOfBand,
  rich, runA, runB, setting, state, UNIT_COLUMNS, unitsOf, type Mode, type Run
} from "./balance-data.js";
import { balanceControls } from "./balance-ui.js";

const view = { mode: "", sort: null as { key: string; desc: boolean } | null };

/* ---------- ячейка ---------- */

/** Дельта с прошлым прогоном. Цвет — по направлению метрики, а не по знаку: рост «Получено урона»
 *  это ухудшение, и красить его зелёным значило бы соврать. */
function deltaNode(mode: string, unit: string, key: string): HTMLElement | null {
  const a = runA()?.modes[mode]?.units[unit]?.[key];
  const b = runB()?.modes[mode]?.units[unit]?.[key];
  if (!isNum(a) || !isNum(b)) return null;

  const diff = a - b;
  if (Math.abs(diff) < 1e-9) return el("span", "delta same", "=");

  const dir = meta(key).dir;
  const cls = dir === null ? "same" : (diff > 0) === dir ? "up" : "down";
  return el("span", `delta ${cls}`, `${diff > 0 ? "▲" : "▼"}${fmt(Math.abs(diff))}`);
}

/** Подпись коридора под числом и, если включено, полоска отклонения. */
function normNodes(unit: string, key: string, value: unknown): HTMLElement[] {
  const d = deviation(unit, key, value);
  if (!d) return [];

  const text = el("span", d.out ? "norm out-of-band" : "norm",
    `норма ${fmt(d.norm)} · ${d.dev >= 0 ? "+" : "−"}${fmt(Math.abs(d.dev) * 100)}%`);
  text.title = `Коридор роли ±${fmt(d.band * 100)}%`;
  if (!setting("bal-bars")) return [text];

  // Полоска: середина — норма, края — двойной коридор. Смещение читается за долю секунды.
  const bar = el("span", `bar ${d.dev > 0 ? "over" : "under"}`);
  const fill = el("i");
  const half = Math.min(Math.abs(d.dev) / (d.band * 2), 0.5);
  fill.style.left = d.dev >= 0 ? "50%" : `${(0.5 - half) * 100}%`;
  fill.style.width = `${half * 100}%`;
  bar.appendChild(fill);
  return [text, bar];
}

/* ---------- таблица ---------- */

/** Колонки, где у всех китов пусто или ноль: прячем, пока не попросили показать нули. */
function liveColumns(mode: Mode, names: string[]): string[] {
  return mode.headers.filter((h) => {
    if (setting("bal-zeros") || UNIT_COLUMNS.includes(h)) return true;
    return names.some((n) => {
      const v = mode.units[n]?.[h];
      return v !== 0 && v !== "" && v !== null && v !== undefined;
    });
  });
}

function modeTable(run: Run, modeKey: string): HTMLElement {
  const mode = run.modes[modeKey];
  const wrap = el("div", "bal-mode");
  if (!mode) return wrap;

  let names = Object.keys(mode.units);
  if (setting("bal-band")) names = names.filter(outOfBand);
  const columns = liveColumns(mode, names);

  const scroller = el("div", "scroller");
  const table = el("table", `bal-table${setting("bal-compact") ? " compact" : ""}`);

  // Сортировка переставляет ГОТОВЫЕ строки, а не пересобирает страницу. Раньше клик по заголовку
  // звал redrawAll(), и таблица на 27 китов и 30 колонок строилась с нуля — тысячи узлов, дельты и
  // нормы заново на каждую ячейку. Числа при этом не менялись ни одного: менялся только порядок.
  const rowByName = new Map<string, HTMLElement>();
  const headByKey = new Map<string, HTMLElement>();

  const applySort = (): void => {
    const s = view.sort;
    const order = names.slice();
    if (s) {
      order.sort((x, y) => {
        const a = mode.units[x]?.[s.key];
        const b = mode.units[y]?.[s.key];
        if (!isNum(a) || !isNum(b)) return String(a ?? "").localeCompare(String(b ?? ""));
        return s.desc ? b - a : a - b;
      });
    }
    // appendChild перемещает уже существующий узел, а не копирует его: перестановка стоит строк,
    // а не ячеек.
    for (const name of order) {
      const row = rowByName.get(name);
      if (row) table.appendChild(row);
    }
    for (const [key, th] of headByKey) {
      if (s?.key === key) th.dataset["sorted"] = s.desc ? "desc" : "asc";
      else delete th.dataset["sorted"];
    }
  };

  const hr = el("tr");
  for (const key of columns) {
    const m = meta(key);
    const th = el("th");
    const btn = el("button", "th-sort", m.label);
    btn.type = "button";
    btn.title = m.note || key;
    btn.addEventListener("click", () => {
      view.sort = view.sort?.key === key ? { key, desc: !view.sort.desc } : { key, desc: true };
      applySort();
    });
    th.appendChild(btn);
    if (setting("bal-keys")) th.appendChild(el("span", "unit", ` ${key}`));
    else if (m.unit && m.unit !== "доля→%") th.appendChild(el("span", "unit", ` ${m.unit}`));
    headByKey.set(key, th);
    hr.appendChild(th);
  }
  table.appendChild(hr);

  for (const name of names) {
    const tr = el("tr");
    rowByName.set(name, tr);
    for (const key of columns) {
      const value = mode.units[name]?.[key];
      const td = el("td");

      if (UNIT_COLUMNS.includes(key)) {
        td.className = "bal-unit";
        const link = el("a", null, displayName(name));
        link.href = `#/balance-kits?kit=${encodeURIComponent(name)}`;
        td.appendChild(link);
        tr.appendChild(td);
        continue;
      }

      const main = el("span", "value", fmtValue(key, value));
      const delta = deltaNode(modeKey, name, key);
      if (delta) main.appendChild(delta);
      td.appendChild(main);
      for (const node of normNodes(name, key, value)) td.appendChild(node);
      tr.appendChild(td);
    }
    table.appendChild(tr);
  }

  applySort();   // порядок и стрелка в заголовке переживают перестроение страницы

  scroller.appendChild(table);
  wrap.appendChild(scroller);

  if (names.length === 0) {
    wrap.appendChild(el("p", "dim", "Под фильтр «только выпавшие из коридора» никто не попал."));
  }

  // Словарь под таблицей: пояснение живёт рядом с колонкой, а не полотном текста над ней.
  const gloss = el("details", "bal-gloss");
  gloss.appendChild(el("summary", null, "что значат колонки"));
  const list = el("dl");
  for (const key of columns) {
    const m = meta(key);
    if (!m.note) continue;
    list.appendChild(el("dt", null, m.label + (m.unit && m.unit !== "доля→%" ? `, ${m.unit}` : "")));
    list.appendChild(el("dd", null, m.note));
  }
  gloss.appendChild(list);
  wrap.appendChild(gloss);
  return wrap;
}

/** Заметка бенча приходит одним куском на пол-экрана. Первый абзац виден, остальное — под
 *  сворачивалкой: читать простыню никто не будет, а первая фраза обычно и есть ответ.
 *  Абзацы в исходнике не размечены, но каждый начинается с **жирного** после точки. */
function notesBlock(text: string): HTMLElement {
  const box = el("div", "bal-note");
  const parts = String(text).split(/(?<=\.)\s+(?=\*\*)/).filter((p) => p.trim());
  if (parts.length === 0) return box;

  box.appendChild(html("p", rich(parts[0] ?? ""), "note"));
  if (parts.length > 1) {
    const fold = el("details", "bal-gloss");
    fold.appendChild(el("summary", null, `подробности замера · ${parts.length - 1}`));
    const body = el("div", "bal-note-body");
    for (const part of parts.slice(1)) body.appendChild(html("p", rich(part), "note"));
    fold.appendChild(body);
    box.appendChild(fold);
  }
  return box;
}

/* ---------- страница ---------- */

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю отчёты SimBench…");
  host.appendChild(status);

  void balance.settled.then(() => {
    if (balance.data.runs.length === 0) {
      status.textContent = balance.data.missing
        ? `Отчётов ещё нет: ${balance.data.missing} не найден. Прогон делается через scripts/balance-headless.ps1.`
        : `Отчёты недоступны: ${balance.error ?? "нет ответа"}. Нужен ./scripts/lab-serve.ps1 -Watch`;
      return;
    }
    // Ссылка вида #/balance-runs?mode=squad_duel открывает сразу нужный режим: разговор идёт про
    // конкретную таблицу, и «зайди и переключи вкладку» — это ссылка не на неё.
    const asked = /mode=([^&]+)/.exec(location.hash);
    if (asked?.[1]) view.mode = decodeURIComponent(asked[1]);
    draw(host);
  });
}

function draw(host: HTMLElement): void {
  host.replaceChildren();
  const run = runA();
  if (!run) return;

  host.appendChild(balanceControls(() => draw(host)));

  const info = el("div", "bal-run");
  info.appendChild(el("h3", null, run.title || run.key));
  if (run.summary) info.appendChild(el("p", "dim", run.summary));
  const prev = runB();
  if (prev && prev !== run) info.appendChild(el("p", "tag", `дельты против: ${prev.title || prev.key}`));
  info.appendChild(el("p", "tag", `${unitsOf(run).length} китов · режимов ${modesOf(run).length}`));
  host.appendChild(info);

  const keys = modesOf(run);
  if (!view.mode || !keys.includes(view.mode)) view.mode = keys[0] ?? "";

  const tabs = el("div", "bal-tabs");
  for (const key of keys) {
    const btn = el("button", null, modeTitle(key));
    btn.type = "button";
    btn.dataset["active"] = String(key === view.mode);
    btn.addEventListener("click", () => { view.mode = key; view.sort = null; draw(host); });
    tabs.appendChild(btn);
  }
  host.appendChild(tabs);
  host.appendChild(modeTable(run, view.mode));

  const note = run.notes?.[view.mode];
  if (note) host.appendChild(notesBlock(note));
  if (run.normsNote) host.appendChild(html("p", rich(run.normsNote), "dim"));
}

const section: SectionDef = {
  id: "balance-runs",
  title: "Прогоны",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Замеры SimBench: что кит показал в бою, как это изменилось с прошлого раза и насколько он ушёл " +
    "от нормы своей роли. Данные пишет прежний скрипт — поменялся только показ.",

  blocks: [
    {
      kind: "head", id: "runs", title: "Замеры по режимам",
      lede:
        "Число само по себе почти ничего не значит: рядом с ним всегда либо дельта с прошлым " +
        "прогоном, либо отклонение от классовой нормы. Заголовок колонки сортирует, наведение " +
        "объясняет, имя кита ведёт на его страницу."
    },
    { kind: "live", id: "runs-table", render },
    {
      kind: "note",
      html:
        "Прогон делается скриптом <code>./scripts/balance-headless.ps1</code>, он же обновляет " +
        "<code>data.js</code>. Страница читает файл при каждом открытии, поэтому свежий прогон виден " +
        "сразу после F5."
    }
  ]
};

export default section;
