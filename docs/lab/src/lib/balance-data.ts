/* Ядро показа баланса: данные прогона, нормы, флаги, форматирование.

   Генерацию не трогаем: `BalanceReports/site/data.js` пишет прежний скрипт SimBench, сервер лишь
   снимает обёртку. Здесь — общее для всех разделов баланса чтение, чтобы «вне коридора» на обзоре,
   в таблице и на карточке кита означало ровно одно и то же. Разъехавшиеся определения — худшее,
   что может случиться с балансным инструментом: спорить начинают не о балансе, а о том, кто прав. */

import { toggle as flipToggle, isOn } from "../toggles.js";
import { METRICS, type Metric } from "./balance-metrics.js";

/* ---------- форма данных ---------- */

export interface Mode {
  title: string;
  headers: string[];
  units: Record<string, Record<string, unknown>>;
  rows: Array<Array<string | number | null>>;
}

export interface Matrix {
  title?: string;
  headers: string[];
  rows: Array<Array<string | number | null>>;
}

export interface Card {
  Name?: string;
  Class?: string;
  Kind?: string;
  Desc?: string;
  Tags?: string;
}

export interface Ability {
  Ability?: string;
  Cooldown?: number;
  Cost?: number;
  DmgMult?: number;
  Radius?: number;
  Heal?: number;
  Target?: string;
  Effects?: string;
  EffectDesc?: string;
}

export interface Norms {
  DPS_norm?: number;
  EHP_norm?: number;
  TTD_solo_norm?: number;
  TTD_focus3_norm?: number;
  HP_norm?: number;
  MaxHP?: number;
  Band?: number;
}

export interface Run {
  key: string;
  title: string;
  summary: string;
  modes: Record<string, Mode>;
  matrices?: Record<string, Matrix>;
  notes?: Record<string, string>;
  norms?: Record<string, Norms>;
  normsNote?: string;
  cards?: Record<string, Card>;
  abilities?: Record<string, Ability[]>;
}

export interface Issue {
  code: string;
  title: string;
  section: string;
  status: string;
  symptom: string;
  diagnosis: string;
  /** Что показал последний прогон по этой записи: подтвердилось, смягчилось, не воспроизводится. */
  recheck: string;
  options: string[];
  verdict: string;
}

export interface BalanceData {
  runs: Run[];
  modeTitles: Record<string, string>;
  issues: Issue[];
  missing?: string;
  error?: string;
}

/* ---------- загрузка ---------- */

const EMPTY: BalanceData = { runs: [], modeTitles: {}, issues: [] };

export const balance = {
  data: EMPTY,
  error: null as string | null,
  settled: Promise.resolve()
};

balance.settled = fetch("api/balance")
  .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
  .then((json: BalanceData) => { balance.data = json; })
  .catch((err: unknown) => {
    balance.error = err instanceof Error ? err.message : String(err);
  });

/**
 * Сообщение «данных нет» — ОДНО на весь сайт.
 *
 * Раньше строку писал каждый раздел сам, и все пять советовали `lab-serve.ps1 -Watch` — ключ,
 * которого у скрипта нет: наблюдение включено по умолчанию, отключает его `-NoWatch`. Пять копий
 * устарели молча и разом, потому что подсказку никто не выполняет — её читает Макс и получает
 * `NamedParameterNotFound` (05.08.2026).
 *
 * Владелец правды о командах — САМ `scripts/lab-serve.ps1`: его блок `param()` расходиться не умеет,
 * при расхождении команда просто падает. Здесь — единственное место, где сайт эту команду называет.
 *
 * @param what что именно недоступно: «Отчёты», «Реестр».
 */
export function noDataMessage(what: string): string {
  const why = balance.error ?? "нет данных";
  return `${what} недоступны: ${why}. Нужен ./scripts/lab-serve.ps1`;
}

/** Какой прогон читаем и с каким сравниваем. Ноль — самый свежий; −1 в b значит «не сравнивать». */
export const state = { a: 0, b: 1 };

export function runA(): Run | undefined {
  return balance.data.runs[state.a];
}

export function runB(): Run | undefined {
  return state.b < 0 ? undefined : balance.data.runs[state.b];
}

export function modesOf(run: Run | undefined): string[] {
  return run ? Object.keys(run.modes) : [];
}

export function modeTitle(key: string): string {
  return balance.data.modeTitles[key] ?? runA()?.modes[key]?.title ?? key;
}

/* ---------- тумблеры показа ----------
   Настройки прежнего сайта переехали как есть: каждая отвечает на вопрос, который реально
   возникает при разборе, и ни одна не является «темой оформления». */

export const SETTINGS: Array<[string, string, string]> = [
  ["bal-zeros", "Показывать нули",
    "Пустая метрика — это сообщение «кит так не умеет». По умолчанию такие строки и колонки скрыты."],
  ["bal-keys", "Технические ключи",
    "Показывать английское имя метрики рядом с русским, а не только в подсказке."],
  ["bal-compact", "Плотные таблицы", "Строки ниже, шрифт мельче — больше данных на экран."],
  ["bal-bars", "Полоски отклонения", "Под числом полоска: насколько кит ушёл от классовой нормы."],
  ["bal-band", "Только выпавшие из коридора",
    "В таблицах остаются лишь киты, у которых хоть одна метрика вне нормы роли."]
];

export function setting(id: string): boolean {
  return isOn(id);
}

export function flipSetting(id: string): boolean {
  return flipToggle(id);
}

// Полоски отклонения включены по умолчанию: без них норма превращается в текст, который не читают.
if (!isOn("bal-bars")) flipToggle("bal-bars");

/* ---------- значения ---------- */

export const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);

export function meta(key: string): Metric {
  return METRICS[key] ?? { label: key, unit: "", note: "", dir: null };
}

export function valueOf(run: Run | undefined, mode: string, unit: string, key: string): unknown {
  return run?.modes[mode]?.units[unit]?.[key];
}

export function cardOf(name: string): Card | null {
  return runA()?.cards?.[name] ?? null;
}

/** Русское имя кита; техническое остаётся ключом сшивки и живёт рядом мелким шрифтом. */
export function displayName(name: string): string {
  return cardOf(name)?.Name ?? name;
}

export function normsOf(unit: string): Norms | null {
  return runA()?.norms?.[unit] ?? null;
}

/** Эталон ростера — Брузер по классовой норме без единого эффекта. Он точка отсчёта, а не
 *  участник баланса: по замыслу равен норме и «проблемой» быть не может. */
export function isReference(name: string): boolean {
  return cardOf(name)?.Kind === "Эталон";
}

/** Служебная строка таблицы: контрольный прогон PvE-бенча — отряд манекенов без испытуемого. */
export function isControlRow(name: string): boolean {
  return name.startsWith("(контроль");
}

export function unitsOf(run: Run | undefined): string[] {
  if (!run) return [];
  const names = new Set<string>();
  for (const mode of Object.values(run.modes)) {
    for (const unit of Object.keys(mode.units)) names.add(unit);
  }
  return [...names].sort();
}

export function fmt(value: number): string {
  if (!Number.isFinite(value)) return "—";
  if (Number.isInteger(value)) return String(value);
  return Math.abs(value) < 10 ? value.toFixed(2) : value.toFixed(Math.abs(value) < 100 ? 1 : 0);
}

/** Единица «доля→%» значит, что в данных лежит доля, а показывать надо проценты. */
export function fmtValue(key: string, value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "string") return value;
  if (!isNum(value)) return String(value);
  const unit = meta(key).unit;
  if (unit === "доля→%") return `${(value * 100).toFixed(2)}%`;
  if (unit === "%") return `${value.toFixed(2)}%`;
  return fmt(value);
}

/* ---------- коридоры роли ---------- */

/** Какая норма отвечает за какую метрику. Пары зафиксированы генератором, не нами. */
const NORM_OF: Record<string, keyof Norms> = {
  DPS_solo: "DPS_norm",
  EHP_solo: "EHP_norm",
  EHP_focus3: "EHP_norm",
  TTD_solo: "TTD_solo_norm",
  TTD_focus3: "TTD_focus3_norm"
};

export const NORM_KEYS = Object.keys(NORM_OF);

export interface Deviation {
  norm: number;
  dev: number;
  out: boolean;
  band: number;
}

export function deviation(unit: string, key: string, value: unknown): Deviation | null {
  const norms = normsOf(unit);
  const normKey = NORM_OF[key];
  if (!norms || !normKey || !isNum(value)) return null;

  const norm = norms[normKey];
  if (!isNum(norm) || norm <= 0) return null;

  const band = isNum(norms.Band) ? norms.Band : 0.3;
  const dev = (value - norm) / norm;
  return { norm, dev, out: Math.abs(dev) > band, band };
}

/* ---------- флаги ---------- */

export type Flag = ["info" | "warn" | "bad", string];

/** Приметы, по которым кит стоит посмотреть глазами. Список перенесён из прежнего показа целиком:
 *  каждая примета — вопрос, который иначе задаётся вручную по таблице. */
export function flagsFor(unit: string): Flag[] {
  const run = runA();
  if (!run) return [];
  if (isReference(unit)) return [["info", "эталон ростера — не участник баланса"]];

  const out: Flag[] = [];
  const win3 = valueOf(run, "trio_duel", unit, "WinRate%");
  const win4 = valueOf(run, "squad_duel", unit, "WinRate%");
  const dps = valueOf(run, "bench_dps", unit, "DPS_solo");
  const react = valueOf(run, "squad_duel", unit, "React%") ?? valueOf(run, "trio_duel", unit, "React%");
  const ehpSolo = valueOf(run, "bench_survivability", unit, "EHP_solo");
  const ehpFocus = valueOf(run, "bench_survivability", unit, "EHP_focus3");

  const wins = [win3, win4].filter(isNum);
  const avgWin = wins.length ? wins.reduce((s, v) => s + v, 0) / wins.length : undefined;

  const allDps = Object.values(run.modes["bench_dps"]?.units ?? {})
    .map((u) => u["DPS_solo"])
    .filter(isNum);
  const medianDps = allDps.length
    ? allDps.slice().sort((a, b) => a - b)[Math.floor(allDps.length / 2)]
    : undefined;

  if (isNum(avgWin) && avgWin >= 70 && isNum(dps) && isNum(medianDps) && dps < medianDps) {
    out.push(["warn", "выигрывает не своим уроном"]);
  }
  if (isNum(react) && react >= 30) out.push(["warn", `${fmt(react)}% урона — ответка`]);
  if (isNum(win3) && isNum(win4) && Math.abs(win3 - win4) >= 25) out.push(["warn", "форматозависимый"]);
  if (isNum(ehpSolo) && isNum(ehpFocus) && ehpFocus > 0 && ehpSolo / ehpFocus >= 3) {
    out.push(["warn", "бинарный по фокусу"]);
  }
  if (isNum(avgWin) && avgWin <= 25) out.push(["bad", "провал по результату"]);
  if (isNum(avgWin) && avgWin >= 85) out.push(["bad", "доминирует"]);

  for (const mode of modesOf(run)) {
    for (const key of NORM_KEYS) {
      const d = deviation(unit, key, valueOf(run, mode, unit, key));
      if (d?.out) {
        out.push(["warn", `${meta(key).label} ${d.dev > 0 ? "выше" : "ниже"} роли на ${fmt(Math.abs(d.dev) * 100)}%`]);
      }
    }
  }

  // Выбросы статического аудита приходят строкой вида «EHP:+2.1σ» — расшифровываем.
  const audit = valueOf(run, "audit_content", unit, "Flags");
  if (typeof audit === "string" && audit.trim()) {
    const NAMES: Record<string, string> = { EHP: "запас прочности", RawDPS: "голый урон", DPS: "урон" };
    for (const raw of audit.split(/[,;]\s*/)) {
      const m = /^(\w+):([+-][\d.]+)σ$/.exec(raw);
      if (m?.[1] && m[2]) {
        const what = NAMES[m[1]] ?? m[1];
        out.push(["info", `${what} ${m[2].startsWith("+") ? "выше" : "ниже"} ростера на ${Math.abs(parseFloat(m[2]))}σ`]);
      } else out.push(["info", raw]);
    }
  }
  return out;
}

export function outOfBand(unit: string): boolean {
  return flagsFor(unit).some(([kind]) => kind === "warn" || kind === "bad");
}

/* ---------- текст ---------- */

/** Минимальный markdown из заметок бенча и реестра: **жирный**, `код`, [ссылка](…). */
export function rich(text: string): string {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/\*\*(.+?)\*\*/g, "<b>$1</b>")
    .replace(/`(.+?)`/g, "<code>$1</code>")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "<i>$1</i>");
}

export const ROLE_NAMES: Record<string, string> = {
  Bruiser: "Брузер",
  Tank: "Танк",
  Assassin: "Убийца",
  Ranged: "Дальник",
  Support: "Поддержка",
  Summoner: "Призыватель"
};

export const TARGET_NAMES: Record<string, string> = {
  NearestEnemy: "ближайший враг",
  AllEnemiesWithTag: "все враги с меткой",
  LowestHpAlly: "самый раненый союзник",
  Self: "на себя"
};

/** Корзины страницы кита. PvE первой: игрок дерётся с энкаунтерами, и «прошёл ли бой» — первый
 *  вопрос о ките, а не его винрейт в зеркале. */
export const BUCKETS: Array<{ name: string; keys: string[] }> = [
  { name: "Бои с энкаунтерами (PvE)", keys: ["ClearRate%", "Cleared", "Fights", "HpCostOnClear%", "FallenOnClear", "HeroDeaths%", "AvgFightSec", "Timeout%", "Overtime%"] },
  { name: "Урон", keys: ["DPS_solo", "DPS_aoe", "AoE_ratio", "AvgDmgDealt", "AutoPhys%", "AutoMagic%", "Ability%", "DoT%", "React%", "Vuln%", "SelfDmg%"] },
  { name: "Выживаемость", keys: ["TTD_solo", "EHP_solo", "HpLeft_solo%", "TTD_focus3", "EHP_focus3", "HpLeft_focus3%", "AvgDmgTaken", "HeroSurvival%", "HealTaken", "Mitigated", "Evaded"] },
  { name: "Контроль", keys: ["ControlSec", "ControlCount", "ControlTakenSec"] },
  { name: "Проклятия", keys: ["Debuffs", "DebuffSec", "Dots"] },
  { name: "Поддержка", keys: ["HealDone", "ShieldHeld", "SupportHPS", "Buffs", "BuffSec", "Cleanses"] },
  { name: "Итог боя", keys: ["WinRate%", "Wins", "Losses", "Draws", "TeamHpOnWin%", "BTStrength", "Rank"] }
];

export const UNIT_COLUMNS = ["Relic", "Unit", "Kit", "Name"];

export function statusOf(issue: Issue): string {
  return String(issue.status || "").split("·")[0]?.trim().toLowerCase() ?? "";
}

export function issuesFor(name: string): Issue[] {
  const ru = displayName(name);
  return balance.data.issues.filter((i) => {
    const hay = `${i.title} ${i.symptom} ${i.diagnosis}`;
    return hay.includes(ru) || hay.includes(name);
  });
}
