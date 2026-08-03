/* Общая панель управления показом баланса: выбор прогонов и настройки таблиц.

   Одна на все разделы области, потому что выбор «какой прогон и с чем сравниваем» — это состояние
   разбора, а не свойство страницы. Переключил прогон на таблицах, ушёл на кита — он тот же. */

import { el } from "../dom.js";
import { balance, flipSetting, setting, SETTINGS, state, type Run } from "./balance-data.js";

/** Перерисовать всё, что сейчас показано: настройки меняют вид сразу, без перезагрузки маршрута. */
const painters = new Set<() => void>();

export function redrawAll(): void {
  for (const paint of painters) paint();
}

export function balanceControls(redraw: () => void): HTMLElement {
  painters.clear();
  painters.add(redraw);

  const box = el("div", "bal-controls");

  const bar = el("div", "bal-bar");
  bar.appendChild(picker("Прогон", balance.data.runs, state.a, (i) => { state.a = i; redraw(); }));
  bar.appendChild(picker("сравнить с", balance.data.runs, state.b, (i) => { state.b = i; redraw(); }, true));

  const gear = el("button", "bal-gear", "настройки показа");
  gear.type = "button";
  bar.appendChild(gear);
  box.appendChild(bar);

  const panel = el("div", "bal-settings");
  panel.hidden = true;
  for (const [id, label, note] of SETTINGS) {
    const row = el("label", "bal-setting");
    const input = el("input");
    input.type = "checkbox";
    input.checked = setting(id);
    input.addEventListener("change", () => { flipSetting(id); redraw(); });
    row.appendChild(input);
    const text = el("span");
    text.appendChild(el("b", null, label));
    text.appendChild(el("span", "dim", note));
    row.appendChild(text);
    panel.appendChild(row);
  }
  gear.addEventListener("click", () => {
    panel.hidden = !panel.hidden;
    gear.dataset["active"] = String(!panel.hidden);
  });
  box.appendChild(panel);
  return box;
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
    if (active < 0) none.selected = true;
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
