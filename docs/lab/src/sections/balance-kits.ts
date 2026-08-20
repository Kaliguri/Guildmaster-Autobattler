/* Страница кита: кто он, что умеет, какие у него числа и чем он подозрителен.

   Собрана из тех же данных, что таблицы, но с другого конца: таблица отвечает «кто из ростера
   выделяется», карточка — «что не так конкретно с этим». Второй вопрос возникает сразу после
   первого, и раньше ради него приходилось листать таблицу глазами. */

import { el, html } from "../dom.js";
import type { SectionDef } from "../types.js";
import {
  balance, BUCKETS, cardOf, deviation, displayName, fmt, fmtValue, flagsFor, isControlRow, isNum,
  issuesFor, meta,
  modeTitle, modesOf, noDataMessage, normsOf, rich, ROLE_NAMES, runA, runB, setting, statusOf, TARGET_NAMES,
  UNIT_COLUMNS, unitsOf, valueOf, type Ability, type Flag
} from "./balance-data.js";
import { balanceControls } from "./balance-ui.js";

const view = { kit: "" };

function flagsNode(unit: string, limit?: number): HTMLElement {
  const list = flagsFor(unit);
  const box = el("div", "flags");
  const shown = limit ? list.slice(0, limit) : list;
  for (const [kind, text] of shown as Flag[]) box.appendChild(el("span", `flag ${kind}`, text));
  if (limit && list.length > shown.length) {
    box.appendChild(el("span", "flag info", `+${list.length - shown.length}`));
  }
  return box;
}

function abilityNode(a: Ability): HTMLElement {
  const box = el("div", "ability");
  box.appendChild(el("div", "a-name", a.Ability ?? "—"));

  const parts: Array<[string, string]> = [];
  if (isNum(a.Cooldown) && a.Cooldown > 0) parts.push(["кулдаун", `${fmt(a.Cooldown)} с`]);
  if (isNum(a.Cost) && a.Cost > 0) parts.push(["стоимость", fmt(a.Cost)]);
  if (isNum(a.DmgMult) && a.DmgMult > 0) parts.push(["урон", `×${fmt(a.DmgMult)}`]);
  if (isNum(a.Radius) && a.Radius > 0) parts.push(["радиус", fmt(a.Radius)]);
  if (isNum(a.Heal) && a.Heal > 0) parts.push(["лечение", fmt(a.Heal)]);
  if (a.Target) parts.push(["цель", TARGET_NAMES[a.Target] ?? a.Target]);

  const nums = el("div", "a-nums");
  parts.forEach(([k, v], i) => {
    if (i) nums.appendChild(document.createTextNode(" · "));
    nums.appendChild(document.createTextNode(`${k} `));
    nums.appendChild(el("b", null, v));
  });
  box.appendChild(nums);

  if (a.Effects) box.appendChild(el("div", "a-eff", `Накладывает: ${a.Effects}`));
  if (a.EffectDesc) box.appendChild(el("div", "a-eff dim", a.EffectDesc));
  return box;
}

/** Строка числа: метрика, режим, значение, дельта и норма — всё в одну строку. */
function statLine(unit: string, mode: string, key: string, value: unknown): HTMLElement {
  const m = meta(key);
  const line = el("div", "stat");

  const left = el("span", "k");
  left.appendChild(document.createTextNode(m.label));
  left.appendChild(el("span", "mode", ` · ${modeTitle(mode)}`));
  left.title = m.note || key;
  line.appendChild(left);

  const right = el("span", "v");
  const main = el("span", "value", fmtValue(key, value) + (m.unit && !["%", "доля→%"].includes(m.unit) ? ` ${m.unit}` : ""));

  const prev = valueOf(runB(), mode, unit, key);
  if (isNum(value) && isNum(prev) && Math.abs(value - prev) > 1e-9) {
    const diff = value - prev;
    const dir = m.dir;
    const cls = dir === null ? "same" : (diff > 0) === dir ? "up" : "down";
    main.appendChild(el("span", `delta ${cls}`, `${diff > 0 ? "▲" : "▼"}${fmt(Math.abs(diff))}`));
  }
  right.appendChild(main);

  const d = deviation(unit, key, value);
  if (d) {
    const norm = el("span", d.out ? "norm out-of-band" : "norm",
      `норма ${fmt(d.norm)} · ${d.dev >= 0 ? "+" : "−"}${fmt(Math.abs(d.dev) * 100)}%`);
    norm.title = `Коридор роли ±${fmt(d.band * 100)}%`;
    right.appendChild(norm);
  }
  line.appendChild(right);
  return line;
}

/** Числа кита по корзинам. Пустые метрики и целиком пустые корзины скрыты. */
function bucketsNode(name: string): HTMLElement {
  const run = runA();
  const grid = el("div", "bal-buckets");
  if (!run) return grid;

  const values: Record<string, Array<{ mode: string; value: unknown }>> = {};
  for (const mode of modesOf(run)) {
    const unit = run.modes[mode]?.units[name];
    if (!unit) continue;
    for (const [key, value] of Object.entries(unit)) {
      if (UNIT_COLUMNS.includes(key) || ["Role", "Replaces", "Type", "Kind"].includes(key)) continue;
      if (!setting("bal-zeros") && (value === 0 || value === "" || value === null || value === undefined)) continue;
      (values[key] ??= []).push({ mode, value });
    }
  }

  const shown = new Set<string>();
  for (const bucket of BUCKETS) {
    const card = el("div", "card bucket");
    card.appendChild(el("h3", null, bucket.name));
    let rows = 0;
    for (const key of bucket.keys) {
      for (const entry of values[key] ?? []) {
        card.appendChild(statLine(name, entry.mode, key, entry.value));
        shown.add(key);
        rows++;
      }
    }
    if (rows > 0) grid.appendChild(card);
  }

  const rest = Object.keys(values).filter((k) => !shown.has(k));
  if (rest.length) {
    const card = el("div", "card bucket");
    card.appendChild(el("h3", null, "Прочее"));
    for (const key of rest) {
      for (const entry of values[key] ?? []) card.appendChild(statLine(name, entry.mode, key, entry.value));
    }
    grid.appendChild(card);
  }
  return grid;
}

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю отчёты…");
  host.appendChild(status);

  void balance.settled.then(() => {
    if (balance.data.runs.length === 0) {
      status.textContent = noDataMessage("Отчёты");
      return;
    }
    // Ссылка вида #/balance-kits?kit=Assassin приходит из таблицы прогонов.
    const asked = /kit=([^&]+)/.exec(location.hash);
    if (asked?.[1]) view.kit = decodeURIComponent(asked[1]);
    draw(host);
  });
}

function draw(host: HTMLElement): void {
  host.replaceChildren();
  const run = runA();
  if (!run) return;

  host.appendChild(balanceControls(() => draw(host)));

  // Контрольная строка — не кит, а точка отсчёта: у неё нет ни роли, ни способностей, и открывать
  // её страницу по умолчанию значило бы встречать читателя пустой карточкой.
  const names = unitsOf(run).filter((n) => !isControlRow(n));
  if (!view.kit || !names.includes(view.kit)) view.kit = names[0] ?? "";

  const chips = el("div", "bal-kits");
  for (const name of names) {
    const btn = el("button", null, displayName(name));
    btn.type = "button";
    btn.dataset["active"] = String(name === view.kit);
    btn.addEventListener("click", () => { view.kit = name; draw(host); });
    chips.appendChild(btn);
  }
  host.appendChild(chips);

  const name = view.kit;
  if (!name) return;
  const card = cardOf(name);
  const norms = normsOf(name);

  const head = el("div", "card kit-head");
  const title = el("div", "kit-title");
  title.appendChild(el("h3", null, displayName(name)));
  if (displayName(name) !== name) title.appendChild(el("span", "tech", name));
  head.appendChild(title);

  if (card?.Class) {
    head.appendChild(el("p", "tag", [card.Kind, ROLE_NAMES[card.Class] ?? card.Class].filter(Boolean).join(" · ")));
  }
  if (card?.Desc) head.appendChild(el("p", "dim", card.Desc));
  if (card?.Tags) {
    const tags = el("div", "flags");
    for (const t of String(card.Tags).split("·")) {
      if (t.trim()) tags.appendChild(el("span", "flag info", t.trim()));
    }
    head.appendChild(tags);
  }

  if (norms) {
    head.appendChild(el("p", "dim",
      `Ожидаем по роли: урон ${fmt(norms.DPS_norm ?? NaN)} в секунду, запас прочности ` +
      `${fmt(norms.EHP_norm ?? NaN)} (голый, без лечения и щитов). Коридор ±${fmt((norms.Band ?? 0.3) * 100)}%.`));

    if (isNum(norms.MaxHP) && isNum(norms.HP_norm) && Math.abs(norms.MaxHP - norms.HP_norm) > 1) {
      const dev = (norms.MaxHP - norms.HP_norm) / norms.HP_norm;
      head.appendChild(el("p", "norm out-of-band",
        `Здоровье персоны ${fmt(norms.MaxHP)} против классовых ${fmt(norms.HP_norm)} — ` +
        `${dev > 0 ? "+" : "−"}${fmt(Math.abs(dev) * 100)}% ещё до боя.`));
    }
  }
  head.appendChild(flagsNode(name));
  host.appendChild(head);

  const issues = issuesFor(name);
  if (issues.length) {
    const box = el("div", "card");
    box.appendChild(el("h3", null, "Открытые вопросы по киту"));
    for (const issue of issues) {
      const row = el("div", `issue-line ${statusOf(issue) === "закрыта" ? "closed" : ""}`);
      row.appendChild(el("span", "i-code", issue.code));
      row.appendChild(html("span", rich(issue.title)));
      row.appendChild(el("span", "i-status warn", issue.status || "—"));
      box.appendChild(row);
    }
    host.appendChild(box);
  }

  const abilities = run.abilities?.[name] ?? [];
  if (abilities.length) {
    const box = el("div", "card");
    box.appendChild(el("h3", null, "Способности"));
    for (const a of abilities) box.appendChild(abilityNode(a));
    host.appendChild(box);
  }

  host.appendChild(bucketsNode(name));
}

const section: SectionDef = {
  id: "balance-kits",
  title: "Киты",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Кит целиком: роль и ожидания по ней, способности с числами, все замеры по корзинам и приметы, " +
    "по которым его стоит посмотреть глазами. Имя кита в таблице прогонов ведёт сюда.",

  blocks: [
    {
      kind: "head", id: "kit", title: "Кто он и что показал",
      lede:
        "Слева метрика и режим, справа значение с дельтой и нормой. Корзины пустыми не показываются: " +
        "«кит так не умеет» — это тоже сообщение, и место оно занимать не должно."
    },
    { kind: "live", id: "kit-card", render }
  ]
};

export default section;
