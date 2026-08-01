/* Отчёты SimBench: прогоны, режимы, дельты между прогонами.

   Генерацию не трогаем — `BalanceReports/site/data.js` пишет тот же скрипт, что и раньше. Здесь
   только показ, переехавший в общий стиль Лаборатории.

   Правило подачи сохранено от прежнего сайта, потому что оно верное: число само по себе почти
   ничего не значит — рядом с ним либо дельта с прошлым прогоном, либо отклонение от нормы.
   Пояснения к колонкам живут В колонках (подсказка по наведению и словарь под таблицей), а не
   полотном текста над ней. */

import { drawFeedState, type Feed } from "../api.js";
import { el } from "../dom.js";
import type { SectionDef } from "../types.js";
import { METRICS } from "./balance-metrics.js";

/* ---------- данные ---------- */

interface Mode {
  title: string;
  headers: string[];
  units?: string[];
  rows: Array<Array<string | number | null>>;
}

interface Run {
  key: string;
  title: string;
  summary: string;
  modes: Record<string, Mode>;
  notes?: Record<string, string>;
  normsNote?: string;
}

interface BalanceData {
  runs: Run[];
  modeTitles: Record<string, string>;
  issues: unknown[];
  missing?: string;
}

const balance: Feed<BalanceData> = { data: null, error: null, settled: Promise.resolve() };

balance.settled = fetch("api/balance")
  .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
  .then((json: BalanceData) => { balance.data = json; })
  .catch((err: unknown) => {
    balance.error = err instanceof Error ? err.message : String(err);
  });

/** Какой прогон показываем и с каким сравниваем. Ноль — самый свежий. */
const state = { a: 0, b: 1, mode: "" };

/* ---------- числа ---------- */

function metricOf(key: string): { label: string; unit: string; note: string; dir: boolean | null } {
  return METRICS[key] ?? { label: key, unit: "", note: "", dir: null };
}

/** Единица «доля→%» значит, что в данных лежит доля, а показывать надо проценты. */
function format(value: string | number | null, unit: string): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "string") return value;
  if (unit === "доля→%") return `${(value * 100).toFixed(0)}%`;
  if (unit === "%") return `${value.toFixed(value < 10 ? 1 : 0)}%`;
  if (unit === "с") return `${value.toFixed(1)} с`;
  if (unit === "×") return `${value.toFixed(2)}×`;
  if (Number.isInteger(value)) return String(value);
  return value.toFixed(Math.abs(value) < 10 ? 2 : 1);
}

/** Строка кита в том же режиме прошлого прогона: без неё дельту не с чем считать. */
function rowIn(run: Run | undefined, modeKey: string, unit: string): Array<string | number | null> | null {
  const mode = run?.modes[modeKey];
  if (!mode) return null;
  const at = mode.rows.findIndex((r) => r[0] === unit);
  return at < 0 ? null : (mode.rows[at] ?? null);
}

/** Дельта с прошлым прогоном. Цвет — по направлению метрики, а не по знаку числа. */
function deltaNode(cur: unknown, prev: unknown, key: string): HTMLElement | null {
  if (typeof cur !== "number" || typeof prev !== "number") return null;
  const diff = cur - prev;
  if (Math.abs(diff) < 1e-9) return null;

  const dir = metricOf(key).dir;
  const better = dir === null ? null : dir ? diff > 0 : diff < 0;
  const node = el("span", `delta ${better === null ? "flat" : better ? "up" : "down"}`);
  const rel = prev !== 0 ? ` (${diff > 0 ? "+" : ""}${((diff / Math.abs(prev)) * 100).toFixed(0)}%)` : "";
  node.textContent = `${diff > 0 ? "+" : ""}${Math.abs(diff) < 10 ? diff.toFixed(2) : diff.toFixed(0)}${rel}`;
  return node;
}

/* ---------- таблица режима ---------- */

function modeTable(run: Run, prev: Run | undefined, modeKey: string): HTMLElement {
  const mode = run.modes[modeKey];
  const wrap = el("div", "bal-mode");
  if (!mode) return wrap;

  const head = el("h3", null, mode.title);
  wrap.appendChild(head);

  const scroller = el("div", "scroller");
  const table = el("table", "bal-table");

  const hr = el("tr");
  for (const key of mode.headers) {
    const m = metricOf(key);
    const th = el("th", null, m.label);
    if (m.unit && m.unit !== "доля→%") th.appendChild(el("span", "unit", ` ${m.unit}`));
    if (m.note) th.title = m.note;
    hr.appendChild(th);
  }
  table.appendChild(hr);

  for (const row of mode.rows) {
    const tr = el("tr");
    const unit = String(row[0] ?? "");
    const before = rowIn(prev, modeKey, unit);

    row.forEach((cell, i) => {
      const key = mode.headers[i] ?? "";
      const td = el("td");
      if (i === 0) {
        td.className = "bal-unit";
        td.textContent = String(cell ?? "");
      } else {
        td.appendChild(el("span", "value", format(cell, metricOf(key).unit)));
        const delta = before ? deltaNode(cell, before[i], key) : null;
        if (delta) td.appendChild(delta);
      }
      tr.appendChild(td);
    });
    table.appendChild(tr);
  }

  scroller.appendChild(table);
  wrap.appendChild(scroller);

  // Словарь под таблицей: пояснение живёт рядом с колонкой, а не полотном текста над ней.
  const gloss = el("details", "bal-gloss");
  gloss.appendChild(el("summary", null, "что значат колонки"));
  const list = el("dl");
  for (const key of mode.headers) {
    const m = metricOf(key);
    if (!m.note) continue;
    list.appendChild(el("dt", null, m.label));
    list.appendChild(el("dd", null, m.note));
  }
  gloss.appendChild(list);
  wrap.appendChild(gloss);
  return wrap;
}

/* ---------- страница ---------- */

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю отчёты SimBench…");
  host.appendChild(status);

  void balance.settled.then(() => {
    const data = balance.data;
    if (!data || data.runs.length === 0) {
      status.textContent = data?.missing
        ? `Отчётов ещё нет: ${data.missing} не найден. Прогон делается через scripts/balance-headless.ps1.`
        : `Отчёты недоступны: ${balance.error ?? "нет ответа"}. Нужен ./scripts/lab-serve.ps1 -Watch`;
      return;
    }
    host.replaceChildren();
    draw(host, data);
  });
}

function draw(host: HTMLElement, data: BalanceData): void {
  host.replaceChildren();

  const runA = data.runs[state.a];
  const runB = data.runs[state.b];
  if (!runA) return;

  // Выбор прогонов: сравнение — не украшение, а способ прочитать число, поэтому оно сверху.
  const bar = el("div", "bal-bar");
  bar.appendChild(picker("Прогон", data.runs, state.a, (i) => { state.a = i; draw(host, data); }));
  bar.appendChild(picker("сравнить с", data.runs, state.b, (i) => { state.b = i; draw(host, data); }, true));
  host.appendChild(bar);

  const info = el("div", "bal-run");
  info.appendChild(el("h3", null, runA.title || runA.key));
  if (runA.summary) info.appendChild(el("p", "dim", runA.summary));
  if (runB && runB !== runA) {
    info.appendChild(el("p", "tag", `дельты считаются против: ${runB.title || runB.key}`));
  }
  host.appendChild(info);

  const keys = Object.keys(runA.modes);
  if (!state.mode || !keys.includes(state.mode)) state.mode = keys[0] ?? "";

  const tabs = el("div", "bal-tabs");
  for (const key of keys) {
    const btn = el("button", null, data.modeTitles[key] ?? runA.modes[key]?.title ?? key);
    btn.type = "button";
    btn.dataset["active"] = String(key === state.mode);
    btn.addEventListener("click", () => { state.mode = key; draw(host, data); });
    tabs.appendChild(btn);
  }
  host.appendChild(tabs);
  host.appendChild(modeTable(runA, runB, state.mode));

  const note = runA.notes?.[state.mode];
  if (note) host.appendChild(el("p", "note", note));
}

function picker(
  label: string, runs: Run[], active: number, onPick: (i: number) => void, allowNone = false
): HTMLElement {
  const box = el("label", "bal-picker");
  box.appendChild(el("span", "tag", label));
  const select = el("select");
  if (allowNone) {
    const none = el("option", null, "— не сравнивать —");
    none.value = "-1";
    select.appendChild(none);
  }
  runs.forEach((run, i) => {
    const opt = el("option", null, run.title || run.key);
    opt.value = String(i);
    if (i === active) opt.selected = true;
    select.appendChild(opt);
  });
  select.addEventListener("change", () => onPick(Number(select.value)));
  box.appendChild(select);
  return box;
}

const section: SectionDef = {
  id: "balance-runs",
  title: "Прогоны",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Замеры SimBench: что кит показал в бою и как это изменилось с прошлого раза. Данные приходят " +
    "из <code>BalanceReports/site/data.js</code> — их пишет прежний скрипт, и он не тронут: " +
    "поменялся только показ.",

  blocks: [
    {
      kind: "head", id: "runs", title: "Замеры по режимам",
      lede:
        "Число само по себе почти ничего не значит: рядом с ним всегда либо дельта с прошлым " +
        "прогоном, либо отклонение от классовой нормы. Направление «лучше» у каждой метрики своё — " +
        "рост «Получено урона» красится как ухудшение, а не как успех."
    },
    { kind: "live", id: "runs-table", render },
    {
      kind: "note",
      html:
        "Прогон делается скриптом <code>./scripts/balance-headless.ps1</code>, он же обновляет " +
        "<code>data.js</code>. Страница читает файл при каждом открытии, поэтому свежий прогон " +
        "виден сразу после F5."
    }
  ]
};

export default section;
