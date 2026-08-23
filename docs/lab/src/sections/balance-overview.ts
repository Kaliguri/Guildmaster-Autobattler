/* Обзор прогона: что важно знать про ростер, не открывая ни одной таблицы.

   Плитки отвечают на первые вопросы («кто сильнее всех», «сколько вне коридора», «сколько открытых
   проблем»), два списка — на следующие: кто выпал из роли и что сдвинулось с прошлого раза.

   PvE-плитки идут отдельно и по делу: игрок дерётся с энкаунтерами, а не в зеркале, поэтому
   «кто не проходит бои» — вопрос более ранний, чем винрейт против своих. */

import { el } from "../dom.js";
import type { SectionDef } from "../types.js";
import {
  balance, deviation, displayName, fmt, fmtValue, isControlRow, isNum, isReference, meta,
  modesOf, noDataMessage, NORM_KEYS, outOfBand, runA, runB, state, statusOf, unitsOf, valueOf
} from "../lib/balance-data.js";
import { balanceControls } from "../lib/balance-ui.js";

function tile(label: string, value: string | number, note: string, cls = ""): HTMLElement {
  const box = el("div", `tile ${cls}`);
  box.appendChild(el("span", "t-label", label));
  box.appendChild(el("span", `t-value${typeof value === "number" ? "" : " text"}`, String(value)));
  if (note) box.appendChild(el("span", "t-note", note));
  return box;
}

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю отчёты…");
  host.appendChild(status);

  void balance.settled.then(() => {
    if (balance.data.runs.length === 0) {
      status.textContent = noDataMessage("Отчёты");
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

  const info = el("div", "bal-run");
  info.appendChild(el("h3", null, run.title || run.key));
  if (run.summary) info.appendChild(el("p", "dim", run.summary));
  host.appendChild(info);

  const names = unitsOf(run);
  // Эталон и контрольная строка — точки отсчёта, а не участники: в аутсайдеры им нельзя.
  const judged = names.filter((n) => !isReference(n) && !isControlRow(n));
  const outs = judged.filter(outOfBand);
  const open = balance.data.issues.filter((i) => !["закрыта", "отклонена"].includes(statusOf(i)));

  const mainMode = run.modes["squad_duel"] ? "squad_duel" : modesOf(run)[0] ?? "";
  const wins = judged
    .map((n) => ({ name: n, wr: valueOf(run, mainMode, n, "WinRate%") }))
    .filter((x): x is { name: string; wr: number } => isNum(x.wr))
    .sort((a, b) => b.wr - a.wr);

  const tiles = el("div", "tiles");
  tiles.appendChild(tile("Китов в прогоне", names.length, mainMode ? `${modesOf(run).length} линз` : ""));
  tiles.appendChild(tile("Вне коридора роли", outs.length, outs.length ? "смотреть ниже" : "все в норме",
    outs.length ? "alarm" : ""));
  tiles.appendChild(tile("Открытых проблем", open.length, "ждут вердикта", open.length ? "alarm" : ""));

  const best = wins[0];
  const worst = wins[wins.length - 1];
  if (best) tiles.appendChild(tile("Сильнейший", displayName(best.name), `винрейт ${fmtValue("WinRate%", best.wr)}`));
  if (worst && worst !== best) {
    tiles.appendChild(tile("Слабейший", displayName(worst.name), `винрейт ${fmtValue("WinRate", worst.wr)}`, "bad"));
  }

  // PvE: если энкаунтеры прогнаны, первый вопрос о ростере — кто не проходит бои и кто платит дороже.
  if (run.modes["encounter_kits"]) {
    const clears = judged
      .map((n) => ({
        name: n,
        rate: valueOf(run, "encounter_kits", n, "ClearRate%"),
        cost: valueOf(run, "encounter_kits", n, "HpCostOnClear%")
      }))
      .filter((x): x is { name: string; rate: number; cost: unknown } => isNum(x.rate));

    if (clears.length) {
      const worstClear = clears.slice().sort((a, b) => a.rate - b.rate)[0];
      if (worstClear) {
        tiles.appendChild(tile("Хуже всех в PvE", displayName(worstClear.name),
          `проходимость ${fmtValue("ClearRate%", worstClear.rate)}`, worstClear.rate < 50 ? "bad" : ""));
      }
      const priciest = clears.filter((x) => isNum(x.cost)).sort((a, b) => (b.cost as number) - (a.cost as number))[0];
      if (priciest) {
        tiles.appendChild(tile("Дороже всех бои", displayName(priciest.name),
          `цена победы ${fmtValue("HpCostOnClear%", priciest.cost)}`));
      }
      // Планка: тот же отряд без испытуемого. Кит ниже неё отряд ослабляет.
      const control = Object.keys(run.modes["encounter_kits"].units).find(isControlRow);
      if (control) {
        const rate = valueOf(run, "encounter_kits", control, "ClearRate%");
        const cost = valueOf(run, "encounter_kits", control, "HpCostOnClear%");
        tiles.appendChild(tile("Отряд без кита", fmtValue("ClearRate%", rate),
          isNum(cost) ? `цена победы ${fmtValue("HpCostOnClear%", cost)}` : "точка отсчёта"));
      }
    }
  }
  host.appendChild(tiles);

  const cols = el("div", "split");
  cols.appendChild(bandCard(judged));
  cols.appendChild(movedCard(judged));
  host.appendChild(cols);
}

/** Кто выпал из коридора. По строке на КИТА, а не на метрику: время до смерти и поглощённый урон —
 *  одно и то же отклонение в двух видах, и вместе они вытесняли из списка остальных китов. */
function bandCard(judged: string[]): HTMLElement {
  const run = runA();
  const card = el("div", "card");
  card.appendChild(el("h3", null, "Кто выпадает из роли"));
  const list = el("ul", "lead");

  const worst = new Map<string, { key: string; dev: number; also: number }>();
  for (const name of judged) {
    for (const mode of modesOf(run)) {
      for (const key of NORM_KEYS) {
        const d = deviation(name, key, valueOf(run, mode, name, key));
        if (!d?.out) continue;
        const prev = worst.get(name);
        if (!prev) worst.set(name, { key, dev: d.dev, also: 0 });
        else {
          prev.also++;
          if (Math.abs(d.dev) > Math.abs(prev.dev)) { prev.key = key; prev.dev = d.dev; }
        }
      }
    }
  }

  const rows = [...worst.entries()].sort((a, b) => Math.abs(b[1].dev) - Math.abs(a[1].dev));
  for (const [name, r] of rows) {
    const li = el("li");
    const link = el("a", "name", displayName(name));
    link.href = `#/balance-kits?kit=${encodeURIComponent(name)}`;
    li.appendChild(link);
    li.appendChild(el("span", "why", meta(r.key).label + (r.also ? ` и ещё ${r.also}` : "")));
    li.appendChild(el("span", `num${Math.abs(r.dev) > 1 ? " out-of-band" : ""}`,
      `${r.dev > 0 ? "+" : "−"}${fmt(Math.abs(r.dev) * 100)}%`));
    list.appendChild(li);
  }
  if (rows.length === 0) list.appendChild(el("li", "empty", "Все киты внутри своих коридоров."));
  card.appendChild(list);
  return card;
}

/** Что сдвинулось с прошлого прогона: только крупные метрики, иначе список превращается в шум. */
function movedCard(judged: string[]): HTMLElement {
  const run = runA();
  const prev = runB();
  const card = el("div", "card");
  card.appendChild(el("h3", null, "Что сдвинулось"));
  const list = el("ul", "lead");

  if (!prev || prev === run) {
    list.appendChild(el("li", "empty",
      state.b < 0 ? "Сравнение выключено." : "Сравнивать не с чем: это единственный прогон в истории."));
    card.appendChild(list);
    return card;
  }

  const moved: Array<{ name: string; key: string; mode: string; rel: number }> = [];
  for (const name of judged) {
    for (const mode of modesOf(run)) {
      for (const key of ["WinRate", "DPS_solo", "EHP_solo", "AvgDmgDealt"]) {
        const a = valueOf(run, mode, name, key);
        const b = valueOf(prev, mode, name, key);
        if (isNum(a) && isNum(b) && Math.abs(a - b) > 1e-9) {
          moved.push({ name, key, mode, rel: b !== 0 ? (a - b) / Math.abs(b) : 1 });
        }
      }
    }
  }

  moved.sort((x, y) => Math.abs(y.rel) - Math.abs(x.rel));
  for (const m of moved.slice(0, 12)) {
    const li = el("li");
    const link = el("a", "name", displayName(m.name));
    link.href = `#/balance-kits?kit=${encodeURIComponent(m.name)}`;
    li.appendChild(link);
    li.appendChild(el("span", "why", meta(m.key).label));
    const dir = meta(m.key).dir;
    const better = dir === null ? null : (m.rel > 0) === dir;
    li.appendChild(el("span", `num delta ${better === null ? "same" : better ? "up" : "down"}`,
      `${m.rel > 0 ? "+" : "−"}${fmt(Math.abs(m.rel) * 100)}%`));
    list.appendChild(li);
  }
  if (moved.length === 0) list.appendChild(el("li", "empty", "Ничего не изменилось."));
  card.appendChild(list);
  return card;
}

const section: SectionDef = {
  id: "balance-overview",
  title: "Обзор",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Состояние ростера с одного взгляда: сколько китов вне коридора роли, кто сильнее и слабее всех, " +
    "кто не проходит энкаунтеры и что сдвинулось с прошлого прогона.",

  blocks: [
    {
      kind: "head", id: "overview", title: "Первые вопросы к прогону",
      lede:
        "Эталон ростера и контрольная строка в аутсайдеры не попадают: они точки отсчёта, а не " +
        "участники баланса. Эталон по замыслу равен норме и «проблемой» быть не может."
    },
    { kind: "live", id: "overview-tiles", render }
  ]
};

export default section;
