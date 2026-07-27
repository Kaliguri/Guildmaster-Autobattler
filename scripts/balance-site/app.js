/* Сайт балансных отчётов: обзор по режимам, страница кита, теплокарта матчапов.
   Данные приходят из data.js (его пишет scripts/balance-site.py из JSON-снимков SimBench).

   Правило подачи: число само по себе почти ничего не значит — рядом с ним всегда стоит либо
   дельта с прошлым прогоном, либо отклонение от нормы. Голые колонки остаются доступными,
   но смысл несёт сравнение. */

const DATA = window.BALANCE_DATA || { runs: [], modeTitles: {} };

const state = {
  runA: 0,
  runB: 1,
  mode: null,
  unit: '',      // выбранный кит — пусто = обзор всех
};

// --- Метрики: как их читать ---

// Больше — лучше? Для дельты важен знак «хорошо/плохо», а не просто рост.
const HIGHER_IS_BETTER = {
  WinRate: true, Wins: true, 'TeamHpOnWin%': true, 'HeroSurvival%': true,
  AvgDmgDealt: true, BTStrength: true, Delta: true,
  DPS_solo: true, DPS_aoe: true, TTD_solo: true, EHP_solo: true,
  TTD_focus3: true, EHP_focus3: true,
  HealTaken: true, Mitigated: true, Evaded: true,
  ControlSec: true, ControlCount: true, Debuffs: true, DebuffSec: true, Dots: true,
  HealDone: true, Buffs: true, BuffSec: true, Cleanses: true,
  Losses: false, AvgDmgTaken: false, ControlTakenSec: false, Rank: false,
};

// Метрика → колонка классовой нормы в снимке balance_norms. Только то, что честно
// сравнивается с ролью: AoE-урон, доли и производные от них норм не имеют.
const NORM_OF = {
  DPS_solo: 'DPS_norm',
  EHP_solo: 'EHP_norm',
  EHP_focus3: 'EHP_norm',
  TTD_solo: 'TTD_solo_norm',
  TTD_focus3: 'TTD_focus3_norm',
};

// Корзины страницы кита: по каким колонкам собирается каждый раздел.
const BUCKETS = [
  { name: 'Урон', keys: ['DPS_solo', 'DPS_aoe', 'AoE_ratio', 'AutoPhys%', 'AutoMagic%', 'Ability%', 'DoT%', 'React%', 'Vuln%', 'SelfDmg%', 'AvgDmgDealt'] },
  { name: 'Выживаемость', keys: ['TTD_solo', 'EHP_solo', 'HpLeft_solo%', 'TTD_focus3', 'EHP_focus3', 'HpLeft_focus3%', 'AvgDmgTaken', 'HeroSurvival%', 'HealTaken', 'Mitigated', 'Evaded'] },
  { name: 'Контроль', keys: ['ControlSec', 'ControlCount', 'ControlTakenSec'] },
  { name: 'Проклятия', keys: ['Debuffs', 'DebuffSec', 'Dots'] },
  { name: 'Утилита', keys: ['HealDone', 'Buffs', 'BuffSec', 'Cleanses'] },
  { name: 'Итог боя', keys: ['WinRate', 'Wins', 'Losses', 'Draws', 'TeamHpOnWin%', 'BTStrength', 'Rank'] },
];

// --- Помощники ---

const $ = (sel) => document.querySelector(sel);
const el = (tag, cls, text) => {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined) n.textContent = text;
  return n;
};

const isNum = (v) => typeof v === 'number' && isFinite(v);
const fmt = (v) => (isNum(v) ? (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2).replace(/\.?0+$/, '')) : (v ?? '—'));

function runA() { return DATA.runs[state.runA]; }
function runB() { return DATA.runs[state.runB]; }

function modeTitle(key) { return DATA.modeTitles[key] || key; }

/** Все режимы прогона, в стабильном порядке: сначала бои, потом стендовые линзы. */
function modesOf(run) {
  if (!run) return [];
  const order = ['duel', 'solo_duel', 'trio_duel', 'squad_duel', 'super_team_duel', 'team_duel', 'squad_swap', 'pair_synergy', 'bench_dps', 'bench_survivability', 'audit_content'];
  const keys = Object.keys(run.modes);
  keys.sort((a, b) => {
    const ia = order.indexOf(a), ib = order.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
  return keys;
}

/** Значение метрики кита в режиме конкретного прогона. */
function valueOf(run, mode, unit, key) {
  const m = run && run.modes[mode];
  const u = m && m.units[unit];
  return u ? u[key] : undefined;
}

/** Дельта между прогонами: элемент со стрелкой и знаком «лучше/хуже». */
function deltaNode(mode, unit, key) {
  const a = valueOf(runA(), mode, unit, key);
  const b = valueOf(runB(), mode, unit, key);
  if (!isNum(a) || !isNum(b)) return null;

  const d = a - b;
  if (Math.abs(d) < 1e-9) return el('span', 'delta same', '=');

  const better = HIGHER_IS_BETTER[key];
  const cls = better === undefined ? 'same' : (d > 0) === better ? 'up' : 'down';
  return el('span', `delta ${cls}`, `${d > 0 ? '▲' : '▼'}${fmt(Math.abs(d))}`);
}

// --- Классовые коридоры: чего ждём от кита по его роли ---

/** Нормы кита в прогоне A. Прогон, снятый до появления линейки, норм не имеет — это не ошибка. */
function normsOf(unit) {
  const run = runA();
  return (run && run.norms && run.norms[unit]) || null;
}

/** Отклонение метрики от классовой нормы: {norm, dev, out} или null, если нормы для неё нет. */
function deviation(unit, key, value) {
  const n = normsOf(unit);
  if (!n || !isNum(value)) return null;

  const norm = n[NORM_OF[key]];
  if (!isNum(norm) || norm <= 0) return null;

  const band = isNum(n.Band) ? n.Band : 0.3;
  const dev = (value - norm) / norm;
  return { norm, dev, out: Math.abs(dev) > band, band };
}

/** Подпись «норма N · ±X%» рядом с числом. Выход за коридор подсвечен. */
function normNode(unit, key, value) {
  const d = deviation(unit, key, value);
  if (!d) return null;

  const sign = d.dev >= 0 ? '+' : '−';
  const node = el('span', d.out ? 'norm out-of-band' : 'norm',
    `норма ${fmt(d.norm)} · ${sign}${fmt(Math.abs(d.dev) * 100)}%`);
  node.title = `Коридор роли ±${fmt(d.band * 100)}%`;
  return node;
}

// --- Автофлаги: то, что раньше приходилось замечать глазами ---

function flagsFor(unit) {
  const run = runA();
  const out = [];

  const win3 = valueOf(run, 'trio_duel', unit, 'WinRate');
  const win4 = valueOf(run, 'squad_duel', unit, 'WinRate');
  const dps = valueOf(run, 'bench_dps', unit, 'DPS_solo');
  const react = valueOf(run, 'squad_duel', unit, 'React%') ?? valueOf(run, 'trio_duel', unit, 'React%');
  const ehpSolo = valueOf(run, 'bench_survivability', unit, 'EHP_solo');
  const ehpFocus = valueOf(run, 'bench_survivability', unit, 'EHP_focus3');

  const wins = [win3, win4].filter(isNum);
  const avgWin = wins.length ? wins.reduce((s, v) => s + v, 0) / wins.length : undefined;

  // Все DPS прогона — чтобы понять, низкий ли у кита урон относительно ростера.
  const allDps = Object.values(run.modes.bench_dps ? run.modes.bench_dps.units : {})
    .map((u) => u.DPS_solo).filter(isNum);
  const medianDps = allDps.length ? allDps.slice().sort((a, b) => a - b)[Math.floor(allDps.length / 2)] : undefined;

  if (isNum(avgWin) && avgWin >= 0.7 && isNum(dps) && isNum(medianDps) && dps < medianDps) {
    out.push(['warn', 'выигрывает не своим уроном']);
  }
  if (isNum(react) && react >= 30) {
    out.push(['warn', `${fmt(react)}% урона — ответка`]);
  }
  if (isNum(win3) && isNum(win4) && Math.abs(win3 - win4) >= 0.25) {
    out.push(['warn', 'форматозависимый']);
  }
  if (isNum(ehpSolo) && isNum(ehpFocus) && ehpFocus > 0 && ehpSolo / ehpFocus >= 3) {
    out.push(['warn', 'бинарный по фокусу']);
  }
  if (isNum(avgWin) && avgWin <= 0.25) {
    out.push(['bad', 'провал по результату']);
  }

  // Выход за классовый коридор — отдельным флагом на каждую метрику: «Танк бьёт как Убийца»
  // и «Убийца держит как Танк» — разные диагнозы, слипаться в один они не должны.
  modesOf(run).forEach((mode) => {
    Object.keys(NORM_OF).forEach((key) => {
      const d = deviation(unit, key, valueOf(run, mode, unit, key));
      if (d && d.out) {
        out.push(['warn', `${key} ${d.dev > 0 ? 'выше' : 'ниже'} роли на ${fmt(Math.abs(d.dev) * 100)}%`]);
      }
    });
  });
  return out;
}

/**
 * Флаги кита. В таблице режима показываем первые `limit` и счётчик остальных: строка таблицы —
 * не место для простыни, полный список ждёт на странице кита.
 */
function flagsNode(unit, limit) {
  const list = flagsFor(unit);
  const box = el('div', 'flags');
  const shown = limit ? list.slice(0, limit) : list;
  shown.forEach(([kind, text]) => box.appendChild(el('span', `flag ${kind}`, text)));
  if (limit && list.length > shown.length) {
    box.appendChild(el('span', 'flag info', `+${list.length - shown.length}`));
  }
  return box;
}

/** Минимальный markdown из заметок бенча: **жирный** и переносы. Полноценный парсер тут ни к чему. */
function notesNode(text) {
  const p = el('p', 'hint');
  const parts = String(text).split(/\*\*(.+?)\*\*/g);
  parts.forEach((part, i) => {
    if (i % 2 === 1) p.appendChild(el('strong', null, part));
    else p.appendChild(document.createTextNode(part));
  });
  return p;
}

// --- Обзор режима ---

function renderMode(mode) {
  const run = runA();
  const m = run.modes[mode];
  const view = $('#view');
  view.innerHTML = '';

  if (!m) {
    view.appendChild(el('p', 'empty', 'В этом прогоне такого отчёта нет.'));
    return;
  }

  const card = el('div', 'card');
  card.appendChild(el('h2', null, m.title));
  if (run.notes[mode]) card.appendChild(notesNode(run.notes[mode]));

  // Отчёт без колонки кита (синергия пар) сшивать по китам нечем — показываем строки как есть.
  const byUnit = Object.keys(m.units).length > 0;
  if (!byUnit) {
    card.appendChild(renderRawTable(m));
    view.appendChild(card);
    return;
  }

  const scroll = el('div', 'scroll');
  const table = el('table');
  const thead = el('thead');
  const htr = el('tr');
  m.headers.forEach((h) => {
    const th = el('th', null, h);
    th.onclick = () => sortBy(mode, h);
    htr.appendChild(th);
  });
  htr.appendChild(el('th', null, 'флаги'));
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  const unitCol = m.headers.find((h) => ['Relic', 'Unit', 'Kit', 'Name'].includes(h));
  let names = Object.keys(m.units);
  if (state.unit) names = names.filter((n) => n === state.unit);
  if (state.sort && state.sort.mode === mode) {
    const key = state.sort.key, dir = state.sort.dir;
    names.sort((x, y) => {
      const a = m.units[x][key], b = m.units[y][key];
      if (isNum(a) && isNum(b)) return (a - b) * dir;
      return String(a).localeCompare(String(b)) * dir;
    });
  }

  names.forEach((name) => {
    const tr = el('tr');
    m.headers.forEach((h) => {
      const td = el('td');
      if (h === unitCol) {
        td.className = 'unit';
        const a = el('a', null, name);
        a.href = '#';
        a.onclick = (e) => { e.preventDefault(); openUnit(name); };
        td.appendChild(a);
      } else {
        const value = m.units[name][h];
        td.appendChild(document.createTextNode(fmt(value)));
        const d = deltaNode(mode, name, h);
        if (d) td.appendChild(d);
        const n = normNode(name, h, value);
        if (n) td.appendChild(n);
      }
      tr.appendChild(td);
    });
    const ftd = el('td');
    ftd.appendChild(flagsNode(name, 2));
    tr.appendChild(ftd);
    tbody.appendChild(tr);
  });

  table.appendChild(tbody);
  scroll.appendChild(table);
  card.appendChild(scroll);
  view.appendChild(card);

  const matrix = run.matrices[mode];
  if (matrix) view.appendChild(renderHeatmap(matrix));
}

/** Таблица «как есть»: заголовки и строки снимка, без сшивки по китам, дельт и норм. */
function renderRawTable(m) {
  const scroll = el('div', 'scroll');
  const table = el('table');
  const thead = el('thead');
  const htr = el('tr');
  m.headers.forEach((h) => htr.appendChild(el('th', null, h)));
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  (m.rows || []).forEach((row) => {
    const tr = el('tr');
    row.forEach((cell) => tr.appendChild(el('td', null, fmt(cell))));
    tbody.appendChild(tr);
  });
  table.appendChild(tbody);
  scroll.appendChild(table);
  return scroll;
}

function sortBy(mode, key) {
  if (state.sort && state.sort.mode === mode && state.sort.key === key) state.sort.dir *= -1;
  else state.sort = { mode, key, dir: -1 };
  render();
}

// --- Теплокарта матчапов ---

function renderHeatmap(matrix) {
  const card = el('div', 'card');
  card.appendChild(el('h2', null, 'Матчапы'));
  card.appendChild(el('p', 'hint',
    'Строка — левый, столбец — правый. Зелёное — левый выиграл, красное — проиграл, серое — ничья. ' +
    'Число рядом с исходом: остаток HP команды победителя, то есть цена победы.'));

  const scroll = el('div', 'scroll');
  const table = el('table', 'heat');
  const thead = el('thead');
  const htr = el('tr');
  matrix.headers.forEach((h) => htr.appendChild(el('th', null, h)));
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  matrix.rows.forEach((row, i) => {
    const tr = el('tr');
    row.forEach((cell, j) => {
      const text = cell === null || cell === undefined ? '' : String(cell);
      const td = el('td', null, j === 0 ? text : text);
      if (j > 0) {
        const head = String(text).trim().charAt(0).toUpperCase();
        if (matrix.headers[j] === String(row[0])) td.className = 'self';
        else if (head === 'W') td.className = 'w';
        else if (head === 'L') td.className = 'l';
        else if (head === 'D') td.className = 'd';
      }
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  });

  table.appendChild(tbody);
  scroll.appendChild(table);
  card.appendChild(scroll);
  return card;
}

// --- Страница кита: все линзы разом ---

function openUnit(name) {
  state.unit = name;
  $('#unitFilter').value = name;
  render();
}

function renderUnit(name) {
  const run = runA();
  const view = $('#view');
  view.innerHTML = '';

  const head = el('div', 'card');
  head.appendChild(el('h2', null, name));

  const n = normsOf(name);
  if (n) {
    const role = el('p', 'hint');
    role.appendChild(document.createTextNode(`Роль: ${n.Class ?? '—'}. Коридор ±${fmt((n.Band ?? 0.3) * 100)}%. `));
    role.appendChild(document.createTextNode(
      `Ожидаем: DPS ${fmt(n.DPS_norm)}, запас прочности ${fmt(n.EHP_norm)} (голый, без лечения и щитов).`));
    head.appendChild(role);

    // MaxHP против классовой нормы: расхождение здесь значит, что стат-блок персоны
    // перекрывает классовую базу — до всяких боевых механик.
    if (isNum(n.MaxHP) && isNum(n.HP_norm) && Math.abs(n.MaxHP - n.HP_norm) > 1) {
      const dev = (n.MaxHP - n.HP_norm) / n.HP_norm;
      head.appendChild(el('p', 'hint out-of-band',
        `HP персоны ${fmt(n.MaxHP)} против классовых ${fmt(n.HP_norm)} — ${dev > 0 ? '+' : '−'}${fmt(Math.abs(dev) * 100)}% ещё до боя.`));
    }
  }

  head.appendChild(flagsNode(name));
  view.appendChild(head);

  // Собираем все колонки кита из всех режимов, чтобы разложить их по корзинам.
  const values = {};   // key -> [{mode, value}]
  modesOf(run).forEach((mode) => {
    const u = run.modes[mode].units[name];
    if (!u) return;
    Object.entries(u).forEach(([k, v]) => {
      if (['Relic', 'Unit', 'Kit', 'Name', 'Role', 'Replaces'].includes(k)) return;
      (values[k] = values[k] || []).push({ mode, value: v, key: k });
    });
  });

  const grid = el('div', 'unit-grid');
  let shown = new Set();

  BUCKETS.forEach((bucket) => {
    const rows = [];
    bucket.keys.forEach((k) => {
      (values[k] || []).forEach((entry) => { rows.push(entry); shown.add(k); });
    });
    if (!rows.length) return;

    const card = el('div', 'card bucket');
    card.appendChild(el('h3', null, bucket.name));
    rows.forEach(({ mode, value, key }) => {
      const line = el('div', 'stat');
      line.appendChild(el('span', 'k', `${key} · ${modeTitle(mode)}`));
      const v = el('span', 'v');
      v.appendChild(document.createTextNode(fmt(value)));
      const d = deltaNode(mode, name, key);
      if (d) v.appendChild(d);
      const n = normNode(name, key, value);
      if (n) v.appendChild(n);
      line.appendChild(v);
      card.appendChild(line);
    });
    grid.appendChild(card);
  });

  // Всё, что не попало ни в одну корзину, показываем отдельно — чтобы данные не терялись молча.
  const rest = Object.keys(values).filter((k) => !shown.has(k));
  if (rest.length) {
    const card = el('div', 'card bucket');
    card.appendChild(el('h3', null, 'Прочее'));
    rest.forEach((k) => {
      values[k].forEach(({ mode, value }) => {
        const line = el('div', 'stat');
        line.appendChild(el('span', 'k', `${k} · ${modeTitle(mode)}`));
        const v = el('span', 'v');
        v.appendChild(document.createTextNode(fmt(value)));
        const d = deltaNode(mode, name, k);
        if (d) v.appendChild(d);
        line.appendChild(v);
        card.appendChild(line);
      });
    });
    grid.appendChild(card);
  }

  view.appendChild(grid);
}

// --- Каркас ---

function renderTabs() {
  const tabs = $('#tabs');
  tabs.innerHTML = '';
  const modes = modesOf(runA());
  if (!state.mode || !modes.includes(state.mode)) state.mode = modes[0];

  modes.forEach((mode) => {
    const b = el('button', mode === state.mode ? 'on' : null, modeTitle(mode));
    b.onclick = () => { state.mode = mode; render(); };
    tabs.appendChild(b);
  });
}

function renderSelectors() {
  const a = $('#runA'), b = $('#runB');
  [a, b].forEach((sel, i) => {
    sel.innerHTML = '';
    DATA.runs.forEach((run, idx) => {
      const o = el('option', null, run.key);
      o.value = String(idx);
      sel.appendChild(o);
    });
    sel.value = String(i === 0 ? state.runA : state.runB);
  });

  const units = new Set();
  DATA.runs.forEach((run) => Object.values(run.modes).forEach((m) => Object.keys(m.units).forEach((u) => units.add(u))));
  const f = $('#unitFilter');
  f.innerHTML = '<option value="">все</option>';
  [...units].sort().forEach((u) => {
    const o = el('option', null, u);
    o.value = u;
    f.appendChild(o);
  });
  f.value = state.unit;
}

function render() {
  $('#backBtn').hidden = !state.unit;
  renderTabs();

  if (state.unit) renderUnit(state.unit);
  else renderMode(state.mode);

  const a = runA(), b = runB();
  $('#foot').textContent = b && b !== a
    ? `Прогон ${a.key} против ${b.key}. Всего прогонов в истории: ${DATA.runs.length}.`
    : `Прогон ${a ? a.key : '—'}. Сравнивать не с чем: это единственный прогон в истории.`;
}

function boot() {
  if (!DATA.runs.length) {
    $('#view').appendChild(el('p', 'empty', 'Снимков нет. Прогоните бенчи в Unity: Alebardium → Balance.'));
    return;
  }
  if (DATA.runs.length < 2) state.runB = 0;

  renderSelectors();
  $('#runA').onchange = (e) => { state.runA = +e.target.value; renderSelectors(); render(); };
  $('#runB').onchange = (e) => { state.runB = +e.target.value; render(); };
  $('#unitFilter').onchange = (e) => { state.unit = e.target.value; render(); };
  $('#backBtn').onclick = () => { state.unit = ''; $('#unitFilter').value = ''; render(); };

  render();
}

boot();
