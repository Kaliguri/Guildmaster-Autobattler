/* Тепловые карты: кто кого бьёт.

   Матрица отвечает на вопрос, которого не видно в средних: «что будет, если этот встретит того».
   Кит может держать хороший винрейт и при этом гарантированно проигрывать одному конкретному —
   в средних это тонет, в клетке видно сразу. */

import { el } from "../dom.js";
import type { SectionDef } from "../types.js";
import { balance, displayName, runA, type Matrix } from "./balance-data.js";
import { balanceControls } from "./balance-ui.js";

const view = { matrix: "" };

function heatmap(matrix: Matrix): HTMLElement {
  const wrap = el("div", "bal-mode");

  const legend = el("div", "legend");
  for (const [cls, text] of [["w", "слева выиграл"], ["l", "слева проиграл"], ["d", "ничья — бой не разрешился"]]) {
    const span = el("span");
    span.appendChild(el("i", `heat-key ${cls}`));
    span.appendChild(document.createTextNode(` ${text}`));
    legend.appendChild(span);
  }
  wrap.appendChild(legend);
  wrap.appendChild(el("p", "dim",
    "Каждая строка — кит слева, каждый столбец — его противник. Клетка отвечает на вопрос «что будет, " +
    "если строка встретит столбец». Процент под исходом — остаток HP победившей команды: цена победы."));

  const scroller = el("div", "scroller");
  const table = el("table", "heat");

  const head = el("tr");
  matrix.headers.forEach((h, i) => head.appendChild(el("th", null, i === 0 ? "" : displayName(String(h)))));
  table.appendChild(head);

  for (const row of matrix.rows) {
    const tr = el("tr");
    row.forEach((cell, j) => {
      const text = cell === null || cell === undefined ? "" : String(cell);
      const td = el("td");

      if (j === 0) {
        td.className = "unit";
        const link = el("a", null, displayName(text));
        link.href = `#/balance-kits?kit=${encodeURIComponent(text)}`;
        td.appendChild(link);
        tr.appendChild(td);
        return;
      }

      const head0 = text.trim().charAt(0).toUpperCase();
      const pct = /(\d+)%/.exec(text);
      if (matrix.headers[j] === String(row[0])) td.className = "self";
      else if (head0 === "W") td.className = "w";
      else if (head0 === "L") td.className = "l";
      else if (head0 === "D") td.className = "d";

      td.appendChild(document.createTextNode(
        head0 === "W" ? "победа" : head0 === "L" ? "пораж." : head0 === "D" ? "ничья" : text));
      if (pct) td.appendChild(el("span", "pct", pct[0]));
      tr.appendChild(td);
    });
    table.appendChild(tr);
  }

  scroller.appendChild(table);
  wrap.appendChild(scroller);
  return wrap;
}

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю отчёты…");
  host.appendChild(status);

  void balance.settled.then(() => {
    if (balance.data.runs.length === 0) {
      status.textContent = `Отчёты недоступны: ${balance.error ?? "нет данных"}. Нужен ./scripts/lab-serve.ps1 -Watch`;
      return;
    }
    draw(host);
  });
}

function draw(host: HTMLElement): void {
  host.replaceChildren();
  const run = runA();
  if (!run) return;

  host.appendChild(balanceControls(() => draw(host)));

  const keys = Object.keys(run.matrices ?? {});
  if (keys.length === 0) {
    host.appendChild(el("p", "dim",
      "В этом прогоне матриц нет: их пишет дуэльный бенч, а он гонялся не всегда. " +
      "Переключи прогон выше."));
    return;
  }

  if (!view.matrix || !keys.includes(view.matrix)) view.matrix = keys[0] ?? "";

  if (keys.length > 1) {
    const tabs = el("div", "bal-tabs");
    for (const key of keys) {
      const btn = el("button", null, run.matrices?.[key]?.title ?? key);
      btn.type = "button";
      btn.dataset["active"] = String(key === view.matrix);
      btn.addEventListener("click", () => { view.matrix = key; draw(host); });
      tabs.appendChild(btn);
    }
    host.appendChild(tabs);
  }

  const matrix = run.matrices?.[view.matrix];
  if (matrix) host.appendChild(heatmap(matrix));
}

const section: SectionDef = {
  id: "balance-matrix",
  title: "Кто кого бьёт",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Матрица исходов: строка встречает столбец. Отвечает на то, чего не видно в средних — кит с " +
    "приличным винрейтом может гарантированно проигрывать одному конкретному противнику.",

  blocks: [
    {
      kind: "head", id: "matrix", title: "Матрица исходов",
      lede: "Клик по имени кита слева ведёт на его страницу со всеми числами."
    },
    { kind: "live", id: "matrix-table", render }
  ]
};

export default section;
