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

/* Разброс ростера по норме: точка на кита, полоса посередине — коридор роли.

   Заведён как стенд, а не как ещё одна плитка, по двум причинам. Во-первых, это единственная
   картинка на всю область баланса — карточка «Баланса» на главной показывала иконку-весы, то есть
   не показывала ничего. Во-вторых, разброс отвечает на вопрос, которого нет у плиток: «ростер
   собран кучно или расползся», а его цифрой не передать.

   Рисовалка опрашивает фид каждый кадр и до его прихода честно рисует пустую шкалу: canvas
   перерисовывается сам, и ждать загрузки ему незачем. */
function spread(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  const run = runA();
  const midY = h * 0.54;
  const padX = w * 0.1;
  const span = w - padX * 2;

  // Коридор: середина — норма, края — двойная полоса допуска.
  ctx.fillStyle = "rgba(184,134,59,.14)";
  ctx.fillRect(padX + span * 0.3, midY - h * 0.16, span * 0.4, h * 0.32);
  ctx.strokeStyle = "rgba(184,134,59,.5)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(padX, midY);
  ctx.lineTo(padX + span, midY);
  ctx.stroke();

  ctx.fillStyle = "#93805E";
  ctx.font = "10px ui-monospace, monospace";
  ctx.fillText("норма", padX + span * 0.5 - 16, midY + h * 0.26);

  const units = unitsOf(run).filter((u) => !isReference(u) && !isControlRow(u));
  if (units.length === 0) {
    ctx.fillText("нет прогонов", padX, midY - h * 0.24);
    return;
  }

  const modes = modesOf(run);
  let out = 0;
  // Сначала считаем, потом рисуем. Порядок важен: у части прогонов норм нет вовсе, и точки на
  // нуле шкалы читались бы как «весь ростер идеально в норме» — самая дорогая ложь, какую этот
  // стенд мог бы сказать.
  let measured = 0;
  const points: Array<{ rel: number; bad: boolean }> = [];
  for (const unit of units) {
    // Худшее отклонение по ключевым метрикам, В ДОЛЯХ КОРИДОРА: коридор у ролей разный, и десять
    // процентов у танка не то же самое, что десять у лекаря. Единица шкалы — край коридора.
    let worst = 0;
    for (const key of NORM_KEYS) {
      for (const mode of modes) {
        const d = deviation(unit, key, valueOf(run, mode, unit, key));
        if (!d || d.band <= 0) continue;
        measured++;
        const rel = d.dev / d.band;
        if (Math.abs(rel) > Math.abs(worst)) worst = rel;
      }
    }
    // Красным метим по ТОЙ ЖЕ мерке, что и рисуем: |rel| > 1 — за краем коридора. Брать сюда
    // outOfBand() нельзя, хотя он и рядом: он считает флаги по винрейту и урону, а не по норме,
    // и точка внутри полосы могла оказаться красной — картинка спорила бы сама с собой.
    const bad = Math.abs(worst) > 1;
    if (bad) out++;
    points.push({ rel: worst, bad });
  }

  if (measured === 0) {
    ctx.fillStyle = "rgba(255,146,48,.9)";
    ctx.font = "11px ui-monospace, monospace";
    ctx.fillText("норм в прогоне нет — мерить нечем", padX, midY - h * 0.24);
  } else {
    points.forEach((p, i) => {
      const x = padX + span * (0.5 + Math.max(-0.46, Math.min(0.46, p.rel * 0.2)));
      const y = midY - h * 0.2 + (h * 0.4 * (i + 0.5)) / points.length;
      ctx.fillStyle = p.bad ? "rgba(255,96,80,.75)" : "rgba(200,162,76,.55)";
      ctx.beginPath();
      ctx.arc(x, y, 2.4, 0, Math.PI * 2);
      ctx.fill();
    });
  }

  ctx.fillStyle = "#C4B393";
  ctx.font = "11px ui-monospace, monospace";
  ctx.fillText(`${units.length} китов`, padX, h * 0.14);
  if (measured > 0) {
    ctx.fillStyle = out > 0 ? "rgba(255,96,80,.9)" : "#93805E";
    ctx.fillText(out > 0 ? `${out} вне коридора` : "все в коридоре", padX + span * 0.52, h * 0.14);
  }
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
  // Норм у прогона может не быть вовсе — их пишет не всякий бенч. Тогда пустой список значит
  // «сравнивать не с чем», и печатать «все внутри коридоров» нельзя: это ответ на незаданный
  // вопрос, а выглядит как зелёный свет.
  let measured = 0;
  for (const name of judged) {
    for (const mode of modesOf(run)) {
      for (const key of NORM_KEYS) {
        const d = deviation(name, key, valueOf(run, mode, name, key));
        if (d) measured++;
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
  if (rows.length === 0) {
    list.appendChild(el("li", "empty", measured === 0
      ? "В этом прогоне норм нет — сравнивать не с чем. Выбери прогон, где бенч их посчитал."
      : "Все киты внутри своих коридоров."));
  }
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
      kind: "stands",
      items: [
        {
          id: "bal-spread",
          status: "note",
          title: "Разброс по норме",
          tag: "живой прогон",
          note:
            "Точка на кита, полоса посередине — коридор роли. Красным — те, у кого хоть одна " +
            "метрика вне коридора. Отвечает на то, чего не видно в цифрах: ростер собран кучно " +
            "или расползся.",
          size: [320, 200],
          draw: spread
        }
      ]
    },
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
