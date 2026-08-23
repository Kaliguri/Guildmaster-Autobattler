/* The Balance Desk — сайт балансных отчётов Guildmaster.
   Данные приходят из data.js (его пишет scripts/balance-site.py из JSON-снимков SimBench).

   Правило подачи: число само по себе почти ничего не значит — рядом с ним всегда либо дельта с
   прошлым прогоном, либо отклонение от классовой нормы. Пояснения к колонкам живут В колонках
   (подсказка по наведению и словарь под таблицей), а не полотном текста над ней. */

const DATA = window.BALANCE_DATA || { runs: [], modeTitles: {}, issues: [] };

// --- Настройки: живут между запусками, если браузер разрешает ---

const DEFAULT_SETTINGS = {
  showZeros: false,
  showKeys: false,
  compact: false,
  showBars: true,
  onlyOutOfBand: false,
};

const SETTINGS_META = [
  ['showZeros', 'Показывать нули', 'Пустая метрика — это сообщение «кит так не умеет». По умолчанию такие строки и колонки скрыты.'],
  ['showKeys', 'Технические ключи колонок', 'Показывать английское имя метрики рядом с русским, а не только в подсказке.'],
  ['compact', 'Плотные таблицы', 'Строки ниже, шрифт мельче — больше данных на экран.'],
  ['showBars', 'Полоски отклонения', 'Под числом рисуется полоска: насколько кит ушёл от классовой нормы.'],
  ['onlyOutOfBand', 'Только выпавшие из коридора', 'В таблицах режимов остаются лишь киты, у которых хоть одна метрика вне нормы роли.'],
];

const STORE_KEY = 'balance-desk-settings';
let storageWorks = true;

function loadSettings() {
  try {
    const raw = localStorage.getItem(STORE_KEY);
    return Object.assign({}, DEFAULT_SETTINGS, raw ? JSON.parse(raw) : {});
  } catch (e) {
    // file:// в некоторых браузерах запрещает localStorage. Настройки всё равно работают —
    // просто забудутся при закрытии вкладки, и об этом честно написано в самом окне настроек.
    storageWorks = false;
    return Object.assign({}, DEFAULT_SETTINGS);
  }
}

function saveSettings() {
  try {
    localStorage.setItem(STORE_KEY, JSON.stringify(settings));
  } catch (e) {
    storageWorks = false;
  }
}

const settings = loadSettings();

const state = {
  runA: 0,
  runB: 1,
  route: { page: 'overview' },
  sort: null,
  issueFilter: 'all',
};

// --- Словарь метрик: русское имя, единица, что значит ---

const METRICS = {
  // Итог боя
  Rank:            ['Место', '', 'Позиция в рейтинге по силе Bradley-Terry.', false],
  Relic:           ['Кит', '', 'Испытуемый.', null],
  Wins:            ['Победы', '', 'Сколько боёв выиграно.', true],
  Losses:          ['Поражения', '', 'Сколько боёв проиграно.', false],
  Draws:           ['Ничьи', '', 'Бой не разрешился до потолка времени.', null],
  WinRate:         ['Винрейт', 'доля→%', 'Доля побед, ничья считается за половину. Бой детерминирован, поэтому значение дискретно.', true],
  'TeamHpOnWin%':  ['Запас победы', '%', 'Средний остаток HP команды в ВЫИГРАННЫХ боях. 80% — размазал не заметив, 10% — вытянул на последних каплях.', true],
  'HeroSurvival%': ['Выживание', '%', 'Как часто сам испытуемый доживал до конца боя, независимо от исхода.', true],
  AvgDmgDealt:     ['Урон за бой', '', 'Сколько кит в среднем нанёс за бой.', true],
  AvgDmgTaken:     ['Получено урона', '', 'Сколько кит в среднем поглотил за бой.', false],
  'React%':        ['Доля ответки', '%', 'Какая часть урона пришла шипами, то есть кит её не выбирал.', null],
  BTStrength:      ['Сила (BT)', '', 'Рейтинг Bradley-Terry, нормирован к единице. Относительная сила против всего ростера.', true],

  // Урон
  DPS_solo:        ['Урон в секунду', '', 'По одной цели, полный сим со способностями и on-hit.', true],
  DPS_aoe:         ['Урон по группе', '', 'То же против кластера из пяти целей.', true],
  AoE_ratio:       ['Разброс по площади', '×', 'Во сколько раз урон по группе выше одиночного. Больше единицы — кит реально бьёт по площади.', null],
  'AutoPhys%':     ['Автоатака, физика', '%', 'Доля урона от обычных ударов физической школой.', null],
  'AutoMagic%':    ['Автоатака, магия', '%', 'Доля урона от обычных ударов магической школой — расщеплённый кит бьёт одной атакой в две школы.', null],
  'Ability%':      ['Способности', '%', 'Доля урона от активных умений.', null],
  'DoT%':          ['Периодический', '%', 'Доля урона от ядов и горения.', null],
  'Vuln%':         ['От уязвимостей', '%', 'Сколько урона добавили наложенные уязвимости. В сумму не входит — сидит внутри строк выше.', null],
  'SelfDmg%':      ['Плата своим HP', '%', 'Сколько кит потратил собственного здоровья, в долях от нанесённого. Цена, а не вклад.', false],

  // Выживаемость
  TTD_solo:        ['Время до смерти', 'с', 'Против одного эталонного источника урона.', true],
  EHP_solo:        ['Поглощено урона', '', 'Суммарный урон, принятый до смерти, — прокси запаса прочности.', true],
  'HpLeft_solo%':  ['Остаток HP', '%', 'Сколько здоровья осталось на конец прогона.', true],
  TTD_focus3:      ['Время до смерти (фокус)', 'с', 'Против троих атакующих одновременно.', true],
  EHP_focus3:      ['Поглощено (фокус)', '', 'То же под фокус-огнём троих.', true],
  'HpLeft_focus3%':['Остаток HP (фокус)', '%', 'Остаток здоровья под фокусом.', true],
  HealTaken:       ['Получено лечения', '', 'Сколько кита вылечили за бой — отличает «выжил на броне» от «выжил под хилером».', true],
  Mitigated:       ['Срезано бронёй', '', 'Урон, который поглотила защита.', true],
  Evaded:          ['Уклонений', '', 'Сколько ударов кит вообще не получил.', true],

  // Контроль
  ControlSec:      ['Контроль', 'с', 'Секунды контроля, наложенные на врагов. С учётом сопротивления, а не по паспорту эффекта.', true],
  ControlCount:    ['Контролей', '', 'Сколько раз кит наложил контроль.', true],
  ControlTakenSec: ['Съел контроля', 'с', 'Сколько секунд контроля пришлось на самого кита.', false],

  // Проклятия
  Debuffs:         ['Дебаффов', '', 'Сколько ослабляющих эффектов кит наложил.', true],
  DebuffSec:       ['Дебаффы', 'с', 'Суммарная длительность наложенных дебаффов.', true],
  Dots:            ['Ядов и горений', '', 'Отдельно от прочих дебаффов: урон по времени.', true],

  // Утилита
  HealDone:        ['Вылечено', '', 'Сколько здоровья кит восстановил себе и союзникам.', true],
  Buffs:           ['Бафов', '', 'Сколько усилений выдано союзникам.', true],
  BuffSec:         ['Бафы', 'с', 'Суммарная длительность выданных усилений.', true],
  Cleanses:        ['Очисток', '', 'Сколько ЧУЖИХ дебаффов кит снял со СВОИХ.', true],

  // Замена в отряде
  Role:            ['Роль', '', 'В какой слот отряда кит подставлен.', null],
  Replaces:        ['Заменяет', '', 'Какого рядового кит вытесняет из состава.', null],
  Outcome:         ['Исход', '', 'Чем кончился бой: победа, поражение или упёрлось в потолок времени.', null],
  'TeamHpLeft%':   ['HP своих', '%', 'Остаток здоровья своей команды.', true],
  'EnemyHpLeft%':  ['HP врагов', '%', 'Остаток здоровья вражеской команды.', false],
  Delta:           ['Перевес', '', 'Разница остатков: насколько своя команда впереди.', true],
  Seconds:         ['Длительность', 'с', 'Сколько длился бой.', null],

  // Синергия пар
  PairA:           ['Кит A', '', 'Первый в паре.', null],
  PairB:           ['Кит B', '', 'Второй в паре.', null],
  Synergy_2v2:     ['Синергия 2v2', '', 'Вклад пары минус вклады обоих поодиночке. Плюс — усиливают друг друга.', true],
  Synergy_4v4:     ['Синергия 4v4', '', 'То же внутри штатного отряда.', true],
  Synergy_6v6:     ['Синергия 6v6', '', 'То же в бою крупнее штатного.', true],
  SynergyAvg:      ['Синергия, среднее', '', 'Среднее по трём размерам боя.', true],

  // Энкаунтеры (PvE): главная линза игры — «прошёл ли бой и какой ценой», а не «кто сильнее в зеркале».
  Cleared:         ['Пройдено', '', 'Сколько энкаунтеров кит прошёл.', true],
  Fights:          ['Боёв', '', 'Сколько энкаунтеров прогнано.', null],
  ClearRate:       ['Проходимость', 'доля→%', 'Доля пройденных боёв. Главная PvE-метрика: винрейт в зеркале отвечает на вопрос, которого в игре нет.', true],
  'HpCostOnClear%':['Цена победы', '%', 'Сколько HP отряда стоила ПОБЕДА. Считается только по пройденным боям: в проигранном остаток говорит о том, как отряд лёг, а не о цене.', false],
  'AvgHpCostOnClear%': ['Цена победы', '%', 'Средняя цена прохождения этого боя по всему ростеру.', false],
  AvgFightSec:     ['Длительность боя', 'с', 'Средняя длина боя по разрешившимся: потолок в среднее не входит.', null],
  'Timeout%':       ['Не разрешилось', '%', 'Доля боёв, упёршихся в потолок времени. Это не ничья, а отсутствие исхода.', false],
  'Overtime%':      ['Доехало до овертайма', '%', 'Доля боёв, дотянувших до порога антизатягивания. Овертайм — предохранитель и обязан быть редким.', false],
  'HeroDeaths%':    ['Смерти кита', '%', 'Как часто погибал сам испытуемый, независимо от исхода боя.', false],
  FallenOnClear:   ['Потерь за победу', '', 'Сколько бойцов из четырёх легло в среднем в ПРОЙДЕННОМ бою: победа с тремя трупами и победа без потерь стоят разного.', false],
  Encounter:       ['Энкаунтер', '', 'Авторенный бой.', null],
  Tier:            ['Тир', '', 'Метка сложности боя: рядовой, элита, финалист акта, служебный.', null],
  Enemies:         ['Врагов', '', 'Сколько врагов в составе.', null],
  Threat:          ['Очки опасности', '', 'Сумма ручных оценок автора — ЕДИНСТВЕННАЯ заявленная сложность боя. Расхождение с проходимостью читается как «оценка врёт».', null],
  EnemyHP:         ['HP врагов', '', 'Суммарный запас вражеской стороны — знаменатель разговора о TTK.', null],
  FailedBy:        ['Не прошли', '', 'Какие киты не справились. Провал одного-двух на элите — норма, провал всех — вопрос к энкаунтеру.', null],

  // Аудит контента
  Type:            ['Сторона', '', 'Мементо игрока или враг.', null],
  Name:            ['Кит', '', 'Испытуемый.', null],
  MaxHP:           ['Здоровье', '', 'Максимальный запас HP по стат-блоку.', true],
  AutoAtk:         ['Удар', '', 'Урон одной обычной атаки.', true],
  'Атк/сек':       ['Атак в секунду', '', 'Скорость атаки с учётом тиковой квантизации.', true],
  AtkRange:        ['Дальность', '', 'Дистанция обычной атаки.', null],
  MoveSpeed:       ['Скорость', '', 'Скорость передвижения.', null],
  PhysArmor:       ['Физброня', '', 'Защита от физического урона.', true],
  ElemArmor:       ['Магброня', '', 'Защита от магического урона.', true],
  DmgTakenEff:     ['Множитель входящего', '×', 'Насколько кит принимает больше или меньше урона.', false],
  DmgDealtEff:     ['Множитель исходящего', '×', 'Насколько кит наносит больше или меньше урона.', true],
  Lifesteal:       ['Вампиризм', '', 'Доля урона, возвращаемая здоровьем.', true],
  RawDPS:          ['Голый урон/сек', '', 'Приближение без способностей и on-hit: удар × скорость × множитель.', true],
  EHP_phys:        ['Прочность к физике', '', 'HP с учётом физброни.', true],
  EHP_elem:        ['Прочность к магии', '', 'HP с учётом магброни.', true],
  Flags:           ['Выбросы', '', 'Отклонение больше двух сигм по ростеру или подозрительный ноль. Повод посмотреть, а не приговор.', null],
};

// Метрика → колонка классовой нормы в снимке. Только то, что честно сравнивается с ролью.
const NORM_OF = {
  DPS_solo: 'DPS_norm',
  EHP_solo: 'EHP_norm',
  EHP_focus3: 'EHP_norm',
  TTD_solo: 'TTD_solo_norm',
  TTD_focus3: 'TTD_focus3_norm',
};

// Корзины страницы кита.
const BUCKETS = [
  // PvE идёт первой корзиной: игрок дерётся с энкаунтерами, и «прошёл ли бой» — первый вопрос о ките.
  { name: 'Бои с энкаунтерами (PvE)', keys: ['ClearRate', 'Cleared', 'Fights', 'HpCostOnClear%', 'FallenOnClear', 'HeroDeaths%', 'AvgFightSec', 'Timeout%', 'Overtime%'] },
  { name: 'Урон', keys: ['DPS_solo', 'DPS_aoe', 'AoE_ratio', 'AvgDmgDealt', 'AutoPhys%', 'AutoMagic%', 'Ability%', 'DoT%', 'React%', 'Vuln%', 'SelfDmg%'] },
  { name: 'Выживаемость', keys: ['TTD_solo', 'EHP_solo', 'HpLeft_solo%', 'TTD_focus3', 'EHP_focus3', 'HpLeft_focus3%', 'AvgDmgTaken', 'HeroSurvival%', 'HealTaken', 'Mitigated', 'Evaded'] },
  { name: 'Контроль', keys: ['ControlSec', 'ControlCount', 'ControlTakenSec'] },
  { name: 'Проклятия', keys: ['Debuffs', 'DebuffSec', 'Dots'] },
  { name: 'Утилита', keys: ['HealDone', 'Buffs', 'BuffSec', 'Cleanses'] },
  { name: 'Итог боя', keys: ['WinRate', 'Wins', 'Losses', 'Draws', 'TeamHpOnWin%', 'BTStrength', 'Rank'] },
];

const UNIT_COLUMNS = ['Relic', 'Unit', 'Kit', 'Name'];

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

function meta(key) {
  const m = METRICS[key];
  if (!m) return { ru: key, unit: '', desc: '', higher: undefined, ratio: false };
  // «доля→%» — значение хранится долей 0..1, а читается процентом: винрейт 0.65 это 65.00%.
  const ratio = m[1] === 'доля→%';
  return { ru: m[0], unit: ratio ? '%' : m[1], desc: m[2], higher: m[3], ratio };
}

/** Значение метрики так, как его читает человек: доли разворачиваются в проценты. */
function fmtValue(key, v) {
  const m = meta(key);
  if (!isNum(v)) return v ?? '—';
  if (m.ratio) return (v * 100).toFixed(2) + '%';
  if (m.unit === '%') return fmt(v) + '%';
  return fmt(v);
}

/** Изменение метрики: у долей это процентные пункты, а не «плюс ноль целых пять». */
function fmtDelta(key, d) {
  const m = meta(key);
  if (m.ratio) return (Math.abs(d) * 100).toFixed(2) + ' п.п.';
  if (m.unit === '%') return fmt(Math.abs(d)) + ' п.п.';
  return fmt(Math.abs(d));
}

function label(key) {
  const m = meta(key);
  return settings.showKeys && m.ru !== key ? `${m.ru} · ${key}` : m.ru;
}

function runA() { return DATA.runs[state.runA]; }
function runB() { return DATA.runs[state.runB]; }
function modeTitle(key) { return DATA.modeTitles[key] || key; }

/** Все режимы прогона в стабильном порядке: сначала бои, потом стендовые линзы. */
function modesOf(run) {
  if (!run) return [];
  // PvE впереди круговых форматов: игра — PvE, зеркальные бои остаются вспомогательной линзой.
  const order = ['encounter_kits', 'encounter_difficulty',
    'duel', 'solo_duel', 'trio_duel', 'squad_duel', 'super_team_duel', 'team_duel',
    'squad_swap', 'pair_synergy', 'bench_dps', 'bench_survivability', 'audit_content'];
  return Object.keys(run.modes).sort((a, b) => {
    const ia = order.indexOf(a), ib = order.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
}

function valueOf(run, mode, unit, key) {
  const m = run && run.modes[mode];
  const u = m && m.units[unit];
  return u ? u[key] : undefined;
}

/** Карточка кита: русское имя, описание, роль, теги. Пусто — карточек в прогоне нет. */
function cardOf(name) {
  const run = runA();
  return (run && run.cards && run.cards[name]) || null;
}

/** Русское имя кита; техническое остаётся ключом сшивки и живёт рядом мелким шрифтом. */
function displayName(name) {
  const card = cardOf(name);
  return (card && card.Name) || name;
}

function normsOf(unit) {
  const run = runA();
  return (run && run.norms && run.norms[unit]) || null;
}

/**
 * Эталон ростера — «Пустой сосуд»: Брузер по классовой норме без единого эффекта. Он точка
 * отсчёта, а не участник баланса, поэтому не попадает ни в аутсайдеры, ни в выбросы: он по
 * замыслу равен норме, и «проблемой» быть не может. В таблицах остаётся — там он полезен.
 */
function isReference(name) {
  const card = cardOf(name);
  return !!card && card.Kind === 'Эталон';
}

/**
 * Служебные строки таблиц — не участники ростера. Пока такая одна: контрольный прогон PvE-бенча
 * (отряд из эталонных манекенов без испытуемого). Он точка отсчёта цены боя, поэтому в аутсайдеры,
 * выбросы и «сильнейший/слабейший» не попадает, но в таблицах и на плитке остаётся.
 */
function isControlRow(name) {
  return typeof name === 'string' && name.startsWith('(контроль');
}

/** Все киты прогона, о которых есть хоть какие-то числа. */
function unitsOf(run) {
  const names = new Set();
  if (!run) return [];
  Object.values(run.modes).forEach((m) => Object.keys(m.units).forEach((u) => names.add(u)));
  return [...names].sort();
}

// --- Коридоры и дельты ---

function deviation(unit, key, value) {
  const n = normsOf(unit);
  if (!n || !isNum(value)) return null;

  const norm = n[NORM_OF[key]];
  if (!isNum(norm) || norm <= 0) return null;

  const band = isNum(n.Band) ? n.Band : 0.3;
  const dev = (value - norm) / norm;
  return { norm, dev, out: Math.abs(dev) > band, band };
}

function deltaNode(mode, unit, key) {
  const a = valueOf(runA(), mode, unit, key);
  const b = valueOf(runB(), mode, unit, key);
  if (!isNum(a) || !isNum(b)) return null;

  const d = a - b;
  if (Math.abs(d) < 1e-9) return el('span', 'delta same', '=');

  const better = meta(key).higher;
  const cls = better === null || better === undefined ? 'same' : (d > 0) === better ? 'up' : 'down';
  return el('span', `delta ${cls}`, `${d > 0 ? '▲' : '▼'}${fmtDelta(key, d)}`);
}

/** Подпись коридора под числом плюс, если включено, полоска отклонения. */
function normNodes(unit, key, value) {
  const d = deviation(unit, key, value);
  if (!d) return [];

  const sign = d.dev >= 0 ? '+' : '−';
  const text = el('span', d.out ? 'norm out-of-band' : 'norm',
    `норма ${fmt(d.norm)} · ${sign}${fmt(Math.abs(d.dev) * 100)}%`);
  text.title = `Коридор роли ±${fmt(d.band * 100)}%`;

  if (!settings.showBars) return [text];

  // Полоска: середина — норма, края — двойной коридор. Смещение читается за долю секунды.
  const bar = el('span', `bar ${d.dev > 0 ? 'over' : 'under'}`);
  const fill = el('i');
  const half = Math.min(Math.abs(d.dev) / (d.band * 2), 0.5);
  fill.style.left = d.dev >= 0 ? '50%' : `${(0.5 - half) * 100}%`;
  fill.style.width = `${half * 100}%`;
  bar.appendChild(fill);
  return [text, bar];
}

// --- Флаги ---

function flagsFor(unit) {
  const run = runA();
  const out = [];
  if (!run) return out;
  if (isReference(unit)) return [['info', 'эталон ростера — не участник баланса']];

  const win3 = valueOf(run, 'trio_duel', unit, 'WinRate');
  const win4 = valueOf(run, 'squad_duel', unit, 'WinRate');
  const dps = valueOf(run, 'bench_dps', unit, 'DPS_solo');
  const react = valueOf(run, 'squad_duel', unit, 'React%') ?? valueOf(run, 'trio_duel', unit, 'React%');
  const ehpSolo = valueOf(run, 'bench_survivability', unit, 'EHP_solo');
  const ehpFocus = valueOf(run, 'bench_survivability', unit, 'EHP_focus3');

  const wins = [win3, win4].filter(isNum);
  const avgWin = wins.length ? wins.reduce((s, v) => s + v, 0) / wins.length : undefined;

  const allDps = Object.values(run.modes.bench_dps ? run.modes.bench_dps.units : {})
    .map((u) => u.DPS_solo).filter(isNum);
  const medianDps = allDps.length ? allDps.slice().sort((a, b) => a - b)[Math.floor(allDps.length / 2)] : undefined;

  if (isNum(avgWin) && avgWin >= 0.7 && isNum(dps) && isNum(medianDps) && dps < medianDps) {
    out.push(['warn', 'выигрывает не своим уроном']);
  }
  if (isNum(react) && react >= 30) out.push(['warn', `${fmt(react)}% урона — ответка`]);
  if (isNum(win3) && isNum(win4) && Math.abs(win3 - win4) >= 0.25) out.push(['warn', 'форматозависимый']);
  if (isNum(ehpSolo) && isNum(ehpFocus) && ehpFocus > 0 && ehpSolo / ehpFocus >= 3) out.push(['warn', 'бинарный по фокусу']);
  if (isNum(avgWin) && avgWin <= 0.25) out.push(['bad', 'провал по результату']);
  if (isNum(avgWin) && avgWin >= 0.85) out.push(['bad', 'доминирует']);

  modesOf(run).forEach((mode) => {
    Object.keys(NORM_OF).forEach((key) => {
      const d = deviation(unit, key, valueOf(run, mode, unit, key));
      if (d && d.out) {
        out.push(['warn', `${meta(key).ru} ${d.dev > 0 ? 'выше' : 'ниже'} роли на ${fmt(Math.abs(d.dev) * 100)}%`]);
      }
    });
  });

  // Выбросы статического аудита приходят строкой вида «EHP:+2.1σ» — расшифровываем.
  const audit = valueOf(run, 'audit_content', unit, 'Flags');
  if (typeof audit === 'string' && audit.trim()) {
    audit.split(/[,;]\s*/).forEach((raw) => {
      const m = raw.match(/^(\w+):([+-][\d.]+)σ$/);
      if (m) {
        const what = { EHP: 'запас прочности', RawDPS: 'голый урон', DPS: 'урон' }[m[1]] || m[1];
        const dir = m[2].startsWith('+') ? 'выше' : 'ниже';
        out.push(['info', `${what} ${dir} ростера на ${Math.abs(parseFloat(m[2]))}σ`]);
      } else {
        out.push(['info', raw]);
      }
    });
  }
  return out;
}

function outOfBand(unit) {
  return flagsFor(unit).some(([kind]) => kind === 'warn' || kind === 'bad');
}

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

// --- Общие блоки ---

/** Минимальный markdown из заметок бенча: **жирный**. Полноценный парсер тут ни к чему. */
function richText(node, text) {
  String(text).split(/\*\*(.+?)\*\*/g).forEach((part, i) => {
    if (i % 2 === 1) node.appendChild(el('strong', null, part));
    else node.appendChild(document.createTextNode(part));
  });
  return node;
}

/** Заметка бенча: первый абзац виден, остальное — под сворачивалкой. */
function notesBlock(text) {
  const box = document.createDocumentFragment();
  const paragraphs = String(text).split(/(?<=\.)\s+(?=\*\*)/).filter((p) => p.trim());
  if (!paragraphs.length) return box;

  box.appendChild(richText(el('p', 'hint'), paragraphs[0]));
  if (paragraphs.length > 1) {
    const fold = el('details', 'fold');
    fold.appendChild(el('summary', null, 'Подробности замера'));
    const body = el('div', 'body');
    paragraphs.slice(1).forEach((p) => body.appendChild(richText(el('p', 'hint'), p)));
    fold.appendChild(body);
    box.appendChild(fold);
  }
  return box;
}

/** Словарь колонок — то, что раньше было простынёй текста над таблицей. */
function glossaryBlock(headers) {
  const known = headers.filter((h) => METRICS[h] && METRICS[h][2]);
  if (!known.length) return null;

  const fold = el('details', 'fold');
  fold.appendChild(el('summary', null, `Что значат колонки (${known.length})`));
  const grid = el('div', 'glossary body');
  known.forEach((h) => {
    const m = meta(h);
    const item = el('div', 'g-item');
    const name = el('span', 'g-name', m.ru + (m.unit ? `, ${m.unit}` : ''));
    item.appendChild(name);
    item.appendChild(el('span', 'g-key', h));
    item.appendChild(el('span', 'g-desc', m.desc));
    grid.appendChild(item);
  });
  fold.appendChild(grid);
  return fold;
}

// --- Таблица режима ---

/** Колонки, где у всех китов пусто или ноль: их прячем, пока не попросили показать нули. */
function liveColumns(m, names) {
  return m.headers.filter((h) => {
    if (settings.showZeros || UNIT_COLUMNS.includes(h)) return true;
    return names.some((n) => {
      const v = m.units[n][h];
      if (isNum(v)) return Math.abs(v) > 1e-9;
      return v !== undefined && v !== null && String(v).trim() !== '';
    });
  });
}

function renderModeTable(mode) {
  const run = runA();
  const m = run.modes[mode];
  const view = $('#view');

  if (!m) {
    view.appendChild(el('p', 'empty', 'В этом прогоне такого отчёта нет.'));
    return;
  }

  const card = el('div', 'card');
  const h = el('h2', null, modeTitle(mode));
  card.appendChild(h);
  if (run.notes[mode]) card.appendChild(notesBlock(run.notes[mode]));

  // Отчёт без колонки кита (синергия пар) сшивать по китам нечем — показываем строки как есть.
  if (!Object.keys(m.units).length) {
    card.appendChild(rawTable(m));
    const gl = glossaryBlock(m.headers);
    if (gl) card.appendChild(gl);
    view.appendChild(card);
    return;
  }

  let names = Object.keys(m.units);
  if (settings.onlyOutOfBand) names = names.filter(outOfBand);
  if (!names.length) {
    card.appendChild(el('p', 'empty', 'Ни один кит не выпадает из коридора — снимите фильтр в настройках.'));
    view.appendChild(card);
    return;
  }

  const headers = liveColumns(m, names);
  const unitCol = headers.find((x) => UNIT_COLUMNS.includes(x));

  if (state.sort && state.sort.mode === mode) {
    const key = state.sort.key, dir = state.sort.dir;
    names.sort((x, y) => {
      const a = m.units[x][key], b = m.units[y][key];
      if (isNum(a) && isNum(b)) return (a - b) * dir;
      return String(a).localeCompare(String(b)) * dir;
    });
  }

  const scroll = el('div', 'scroll' + (settings.compact ? ' compact' : ''));
  const table = el('table');
  const thead = el('thead');
  const htr = el('tr');
  headers.forEach((key) => {
    const th = el('th', 'sortable');
    th.appendChild(document.createTextNode(label(key)));
    if (state.sort && state.sort.mode === mode && state.sort.key === key) {
      th.appendChild(el('span', 'arrow', state.sort.dir < 0 ? '▼' : '▲'));
    }
    th.title = meta(key).desc || key;
    th.onclick = () => sortBy(mode, key);
    htr.appendChild(th);
  });
  htr.appendChild(el('th', null, 'Флаги'));
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  names.forEach((name) => {
    const tr = el('tr');
    headers.forEach((key) => {
      const td = el('td');
      if (key === unitCol) {
        td.className = 'unit';
        const a = el('a', null, displayName(name));
        a.href = `#/kit/${encodeURIComponent(name)}`;
        td.appendChild(a);
        if (displayName(name) !== name) td.appendChild(el('span', 'tech', name));
      } else {
        const value = m.units[name][key];
        td.appendChild(el('span', 'cell-main', fmtValue(key, value)));
        const d = deltaNode(mode, name, key);
        if (d) td.lastChild.appendChild(d);
        normNodes(name, key, value).forEach((n) => td.appendChild(n));
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

  const gl = glossaryBlock(headers);
  if (gl) card.appendChild(gl);
  view.appendChild(card);

  const matrix = run.matrices[mode];
  if (matrix) view.appendChild(renderHeatmap(matrix));
}

/** Таблица «как есть»: заголовки и строки снимка, без сшивки по китам. */
function rawTable(m) {
  const scroll = el('div', 'scroll' + (settings.compact ? ' compact' : ''));
  const table = el('table');
  const thead = el('thead');
  const htr = el('tr');
  m.headers.forEach((h) => {
    const th = el('th', null, label(h));
    th.title = meta(h).desc || h;
    htr.appendChild(th);
  });
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  (m.rows || []).forEach((row) => {
    const tr = el('tr');
    row.forEach((cell, i) => {
      const td = el('td');
      const isUnit = UNIT_COLUMNS.includes(m.headers[i]) || ['PairA', 'PairB'].includes(m.headers[i]);
      if (isUnit && cardOf(String(cell))) {
        td.className = 'unit';
        const a = el('a', null, displayName(String(cell)));
        a.href = `#/kit/${encodeURIComponent(String(cell))}`;
        td.appendChild(a);
      } else {
        td.textContent = fmt(cell);
      }
      tr.appendChild(td);
    });
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
  card.appendChild(el('h2', null, 'Кто кого бьёт'));

  const legend = el('div', 'legend');
  [['w', 'слева выиграл'], ['l', 'слева проиграл'], ['d', 'ничья — бой не разрешился']].forEach(([cls, text]) => {
    const s = el('span');
    s.appendChild(el('i', cls));
    s.appendChild(document.createTextNode(text));
    legend.appendChild(s);
  });
  card.appendChild(legend);
  card.appendChild(el('p', 'hint',
    'Каждая строка — кит слева, каждый столбец — его противник. Клетка отвечает на вопрос «что будет, ' +
    'если строка встретит столбец». Процент под исходом — остаток HP победившей команды: цена победы.'));

  const scroll = el('div', 'scroll');
  const table = el('table', 'heat');
  const thead = el('thead');
  const htr = el('tr');
  matrix.headers.forEach((h, i) => htr.appendChild(el('th', null, i === 0 ? '' : displayName(h))));
  thead.appendChild(htr);
  table.appendChild(thead);

  const tbody = el('tbody');
  matrix.rows.forEach((row) => {
    const tr = el('tr');
    row.forEach((cell, j) => {
      const text = cell === null || cell === undefined ? '' : String(cell);
      const td = el('td');
      if (j === 0) {
        td.className = 'unit';
        const a = el('a', null, displayName(text));
        a.href = `#/kit/${encodeURIComponent(text)}`;
        td.appendChild(a);
        tr.appendChild(td);
        return;
      }

      const head = text.trim().charAt(0).toUpperCase();
      const pct = text.match(/(\d+)%/);
      if (matrix.headers[j] === String(row[0])) td.className = 'self';
      else if (head === 'W') td.className = 'w';
      else if (head === 'L') td.className = 'l';
      else if (head === 'D') td.className = 'd';

      td.appendChild(document.createTextNode(
        head === 'W' ? 'победа' : head === 'L' ? 'пораж.' : head === 'D' ? 'ничья' : text));
      if (pct) td.appendChild(el('span', 'pct', pct[0]));
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  });

  table.appendChild(tbody);
  scroll.appendChild(table);
  card.appendChild(scroll);
  return card;
}

// --- Страница кита ---

function renderKit(name) {
  const run = runA();
  const view = $('#view');
  const card = cardOf(name);
  const n = normsOf(name);

  const head = el('div', 'card kit-head');
  const main = el('div', 'kit-main');

  const title = el('div', 'kit-title');
  title.appendChild(el('h2', null, displayName(name)));
  if (displayName(name) !== name) title.appendChild(el('span', 'tech', name));
  main.appendChild(title);

  if (card && card.Class) {
    const role = [card.Kind, roleName(card.Class)].filter(Boolean).join(' · ');
    main.appendChild(el('div', 'kit-role', role));
  }
  if (card && card.Desc) main.appendChild(el('p', 'kit-desc', card.Desc));

  if (card && card.Tags) {
    const tags = el('div', 'flags kit-tags');
    String(card.Tags).split('·').forEach((t) => {
      if (t.trim()) tags.appendChild(el('span', 'flag info', t.trim()));
    });
    main.appendChild(tags);
  }

  if (n) {
    const expect = el('p', 'hint');
    expect.appendChild(document.createTextNode(
      `Ожидаем по роли: урон ${fmt(n.DPS_norm)} в секунду, запас прочности ${fmt(n.EHP_norm)} ` +
      `(голый, без лечения и щитов). Коридор ±${fmt((n.Band ?? 0.3) * 100)}%.`));
    main.appendChild(expect);

    if (isNum(n.MaxHP) && isNum(n.HP_norm) && Math.abs(n.MaxHP - n.HP_norm) > 1) {
      const dev = (n.MaxHP - n.HP_norm) / n.HP_norm;
      main.appendChild(el('p', 'hint out-of-band',
        `Здоровье персоны ${fmt(n.MaxHP)} против классовых ${fmt(n.HP_norm)} — ` +
        `${dev > 0 ? '+' : '−'}${fmt(Math.abs(dev) * 100)}% ещё до боя.`));
    }
  }

  main.appendChild(flagsNode(name));
  head.appendChild(main);
  view.appendChild(head);

  const issues = issuesFor(name);
  if (issues.length) {
    const box = el('div', 'card');
    box.appendChild(el('h2', null, 'Открытые вопросы по киту'));
    issues.forEach((i) => box.appendChild(issueNode(i, true)));
    view.appendChild(box);
  }

  const abilities = (run.abilities && run.abilities[name]) || [];
  if (abilities.length) {
    const box = el('div', 'card');
    box.appendChild(el('h2', null, 'Способности'));
    abilities.forEach((a) => box.appendChild(abilityNode(a)));
    view.appendChild(box);
  }

  view.appendChild(bucketsNode(name));
}

function roleName(cls) {
  return {
    Bruiser: 'Брузер', Tank: 'Танк', Assassin: 'Убийца',
    Ranged: 'Дальник', Support: 'Поддержка', Summoner: 'Призыватель',
  }[cls] || cls;
}

function abilityNode(a) {
  const box = el('div', 'ability');
  box.appendChild(el('div', 'a-name', a.Ability || '—'));

  const nums = el('div', 'a-nums');
  const parts = [];
  if (isNum(a.Cooldown) && a.Cooldown > 0) parts.push(['кулдаун', `${fmt(a.Cooldown)} с`]);
  if (isNum(a.Cost) && a.Cost > 0) parts.push(['стоимость', fmt(a.Cost)]);
  if (isNum(a.DmgMult) && a.DmgMult > 0) parts.push(['урон', `×${fmt(a.DmgMult)}`]);
  if (isNum(a.Radius) && a.Radius > 0) parts.push(['радиус', fmt(a.Radius)]);
  if (isNum(a.Heal) && a.Heal > 0) parts.push(['лечение', fmt(a.Heal)]);
  if (a.Target) parts.push(['цель', targetName(a.Target)]);

  parts.forEach(([k, v], i) => {
    if (i) nums.appendChild(document.createTextNode(' · '));
    nums.appendChild(document.createTextNode(k + ' '));
    nums.appendChild(el('b', null, v));
  });
  box.appendChild(nums);

  if (a.Effects) box.appendChild(el('div', 'a-eff', `Накладывает: ${a.Effects}`));
  if (a.EffectDesc) box.appendChild(el('div', 'a-eff hint', a.EffectDesc));
  return box;
}

function targetName(mode) {
  return {
    NearestEnemy: 'ближайший враг',
    AllEnemiesWithTag: 'все враги с меткой',
    LowestHpAlly: 'самый раненый союзник',
    Self: 'на себя',
  }[mode] || mode;
}

/** Числа кита по корзинам. Пустые метрики и целиком пустые корзины скрыты. */
function bucketsNode(name) {
  const run = runA();
  const values = {};
  modesOf(run).forEach((mode) => {
    const u = run.modes[mode].units[name];
    if (!u) return;
    Object.entries(u).forEach(([k, v]) => {
      if (UNIT_COLUMNS.includes(k) || ['Role', 'Replaces', 'Type', 'Kind'].includes(k)) return;
      if (!settings.showZeros && (v === 0 || v === '' || v === null || v === undefined)) return;
      (values[k] = values[k] || []).push({ mode, value: v, key: k });
    });
  });

  const grid = el('div', 'unit-grid');
  const shown = new Set();

  BUCKETS.forEach((bucket) => {
    const rows = [];
    bucket.keys.forEach((k) => (values[k] || []).forEach((entry) => { rows.push(entry); shown.add(k); }));
    if (!rows.length) return;

    const card = el('div', 'card bucket');
    card.appendChild(el('h3', null, bucket.name));
    rows.forEach((r) => card.appendChild(statLine(name, r)));
    grid.appendChild(card);
  });

  const rest = Object.keys(values).filter((k) => !shown.has(k));
  if (rest.length) {
    const card = el('div', 'card bucket');
    card.appendChild(el('h3', null, 'Прочее'));
    rest.forEach((k) => values[k].forEach((r) => card.appendChild(statLine(name, r))));
    grid.appendChild(card);
  }
  return grid;
}

function statLine(name, { mode, value, key }) {
  const m = meta(key);
  const line = el('div', 'stat');

  const k = el('span', 'k');
  k.appendChild(document.createTextNode(m.ru));
  k.appendChild(el('span', 'mode', ` · ${modeTitle(mode)}`));
  k.title = m.desc || key;
  line.appendChild(k);

  const v = el('span', 'v');
  v.appendChild(el('span', 'cell-main',
    fmtValue(key, value) + (m.unit && m.unit !== '%' ? ` ${m.unit}` : '')));
  const d = deltaNode(mode, name, key);
  if (d) v.lastChild.appendChild(d);
  normNodes(name, key, value).forEach((x) => v.appendChild(x));
  line.appendChild(v);
  return line;
}

// --- Реестр проблем ---

const STATUS_CLASS = {
  'открыта': 'st-open',
  'требует дизайна': 'st-design',
  'решение принято': 'st-design',
  'правка внесена': 'st-applied',
  'закрыта': 'st-closed',
  'отклонена': 'st-closed',
};

function statusOf(issue) {
  return String(issue.status || '').split('·')[0].trim().toLowerCase();
}

/** Проблемы, где кит упомянут: по русскому имени или по техническому. */
function issuesFor(name) {
  const ru = displayName(name);
  return (DATA.issues || []).filter((i) => {
    const hay = `${i.title} ${i.symptom} ${i.diagnosis}`;
    return hay.includes(ru) || hay.includes(name);
  });
}

function issueNode(issue, compact) {
  const box = el('div', `card issue ${STATUS_CLASS[statusOf(issue)] || ''}`);

  const head = el('div', 'i-head');
  head.appendChild(el('span', 'i-code', issue.code));
  head.appendChild(el('h3', null, issue.title));
  head.appendChild(el('span', `flag ${statusOf(issue) === 'закрыта' ? 'good' : 'warn'}`, issue.status || '—'));
  box.appendChild(head);

  const body = el('div', 'i-body');
  if (issue.symptom) {
    const p = el('p');
    p.appendChild(el('span', 'lbl', 'Симптом. '));
    richText(p, issue.symptom);
    body.appendChild(p);
  }
  if (!compact && issue.diagnosis) {
    const p = el('p');
    p.appendChild(el('span', 'lbl', 'Диагноз. '));
    richText(p, issue.diagnosis);
    body.appendChild(p);
  }
  if (!compact && issue.options.length) {
    const p = el('p');
    p.appendChild(el('span', 'lbl', 'Варианты правки'));
    body.appendChild(p);
    const ol = el('ol');
    issue.options.forEach((o) => ol.appendChild(richText(el('li'), o)));
    body.appendChild(ol);
  }

  const verdict = el('div', `i-verdict${!issue.verdict || issue.verdict === '—' ? ' waiting' : ''}`);
  verdict.textContent = !issue.verdict || issue.verdict === '—'
    ? 'Вердикт Макса: ждёт.'
    : `Вердикт Макса: ${issue.verdict}`;
  body.appendChild(verdict);

  box.appendChild(body);
  return box;
}

function renderIssues() {
  const view = $('#view');
  const issues = DATA.issues || [];

  if (!issues.length) {
    view.appendChild(el('p', 'empty', 'Реестр пуст или не найден: docs/balance-issues.md.'));
    return;
  }

  const filters = el('div', 'filters');
  const counts = { all: issues.length };
  issues.forEach((i) => { counts[statusOf(i)] = (counts[statusOf(i)] || 0) + 1; });

  [['all', 'все'], ...Object.keys(counts).filter((k) => k !== 'all').map((k) => [k, k])]
    .forEach(([key, text]) => {
      const b = el('button', 'chip-btn' + (state.issueFilter === key ? ' on' : ''), `${text} (${counts[key]})`);
      if (state.issueFilter === key) b.style.borderColor = 'var(--accent)';
      b.onclick = () => { state.issueFilter = key; render(); };
      filters.appendChild(b);
    });
  view.appendChild(filters);

  let section = '';
  issues
    .filter((i) => state.issueFilter === 'all' || statusOf(i) === state.issueFilter)
    .forEach((i) => {
      if (i.section && i.section !== section) {
        section = i.section;
        view.appendChild(el('h2', null, section));
      }
      view.appendChild(issueNode(i, false));
    });
}

// --- Обзор: здоровье ростера ---

function renderOverview() {
  const run = runA();
  const view = $('#view');
  const names = unitsOf(run);

  const judged = names.filter((n) => !isReference(n) && !isControlRow(n));
  const outs = judged.filter(outOfBand);
  const open = (DATA.issues || []).filter((i) => !['закрыта', 'отклонена'].includes(statusOf(i)));

  // Главный формат — отряды 4v4; если его нет, берём что есть.
  const mainMode = run.modes.squad_duel ? 'squad_duel' : (modesOf(run)[0] || null);
  const wins = judged
    .map((n) => ({ name: n, wr: valueOf(run, mainMode, n, 'WinRate') }))
    .filter((x) => isNum(x.wr))
    .sort((a, b) => b.wr - a.wr);

  const tiles = el('div', 'tiles');
  tiles.appendChild(tile('Китов в прогоне', names.length, mainMode ? modeTitle(mainMode) + ' и другие линзы' : ''));
  tiles.appendChild(tile('Вне коридора роли', outs.length, outs.length ? 'смотреть ниже' : 'все в норме',
    outs.length ? 'alarm' : ''));
  tiles.appendChild(tile('Открытых проблем', open.length, 'ждут вердикта', open.length ? 'alarm' : ''));
  if (wins.length) {
    tiles.appendChild(tile('Сильнейший', displayName(wins[0].name),
      `винрейт ${fmtValue('WinRate', wins[0].wr)}`));
    tiles.appendChild(tile('Слабейший', displayName(wins[wins.length - 1].name),
      `винрейт ${fmtValue('WinRate', wins[wins.length - 1].wr)}`, 'bad'));
  }

  // PvE-плитки: если энкаунтеры прогнаны, первый вопрос о ростере — кто не проходит бои и кто платит
  // за них дороже всех. Винрейт в зеркале этого не показывает.
  if (run.modes.encounter_kits) {
    const clears = judged
      .map((n) => ({ name: n, rate: valueOf(run, 'encounter_kits', n, 'ClearRate'),
        cost: valueOf(run, 'encounter_kits', n, 'HpCostOnClear%') }))
      .filter((x) => isNum(x.rate));

    if (clears.length) {
      const worstClear = clears.slice().sort((a, b) => a.rate - b.rate)[0];
      tiles.appendChild(tile('Хуже всех в PvE', displayName(worstClear.name),
        `проходимость ${fmtValue('ClearRate', worstClear.rate)}`, worstClear.rate < 0.5 ? 'bad' : ''));

      const priciest = clears.filter((x) => isNum(x.cost)).sort((a, b) => b.cost - a.cost)[0];
      if (priciest) {
        tiles.appendChild(tile('Дороже всех бои', displayName(priciest.name),
          `цена победы ${fmtValue('HpCostOnClear%', priciest.cost)}`));
      }

      // Планка: тот же отряд без испытуемого. Кит ниже неё отряд ослабляет.
      const ctrlName = Object.keys(run.modes.encounter_kits.units).find(isControlRow);
      if (ctrlName) {
        const ctrlRate = valueOf(run, 'encounter_kits', ctrlName, 'ClearRate');
        const ctrlCost = valueOf(run, 'encounter_kits', ctrlName, 'HpCostOnClear%');
        tiles.appendChild(tile('Отряд без кита', fmtValue('ClearRate', ctrlRate),
          isNum(ctrlCost) ? `цена победы ${fmtValue('HpCostOnClear%', ctrlCost)}` : 'точка отсчёта'));
      }
    }
  }

  view.appendChild(tiles);

  const cols = el('div', 'cols2');

  // Кто выпал из коридора и насколько.
  const bandCard = el('div', 'card');
  bandCard.appendChild(el('h2', null, 'Кто выпадает из роли'));
  const bandList = el('ul', 'lead');

  // По строке на КИТА, а не на метрику: время до смерти и поглощённый урон — одно и то же
  // отклонение в двух видах, и вместе они вытесняли из списка остальных китов.
  const worst = new Map();
  judged.forEach((n) => {
    modesOf(run).forEach((mode) => {
      Object.keys(NORM_OF).forEach((key) => {
        const d = deviation(n, key, valueOf(run, mode, n, key));
        if (!d || !d.out) return;
        const prev = worst.get(n);
        if (!prev) worst.set(n, { name: n, key, dev: d.dev, also: 0 });
        else {
          prev.also++;
          if (Math.abs(d.dev) > Math.abs(prev.dev)) { prev.key = key; prev.dev = d.dev; }
        }
      });
    });
  });

  const rows = [...worst.values()].sort((a, b) => Math.abs(b.dev) - Math.abs(a.dev));
  rows.forEach((r) => {
    const li = el('li');
    const a = el('a', 'name', displayName(r.name));
    a.href = `#/kit/${encodeURIComponent(r.name)}`;
    li.appendChild(a);
    li.appendChild(el('span', 'why', meta(r.key).ru + (r.also ? ` и ещё ${r.also}` : '')));
    li.appendChild(el('span', 'num' + (Math.abs(r.dev) > 1 ? ' out-of-band' : ''),
      `${r.dev > 0 ? '+' : '−'}${fmt(Math.abs(r.dev) * 100)}%`));
    bandList.appendChild(li);
  });
  if (!rows.length) bandList.appendChild(el('li', 'empty', 'Все киты внутри своих коридоров.'));
  bandCard.appendChild(bandList);
  cols.appendChild(bandCard);

  // Что сдвинулось с прошлого прогона.
  const diffCard = el('div', 'card');
  const b = runB();
  diffCard.appendChild(el('h2', null, 'Что сдвинулось'));
  const diffList = el('ul', 'lead');
  if (!b || b === run) {
    diffList.appendChild(el('li', 'empty', 'Сравнивать не с чем: это единственный прогон в истории.'));
  } else {
    const moved = [];
    judged.forEach((n) => {
      modesOf(run).forEach((mode) => {
        ['WinRate', 'DPS_solo', 'EHP_solo', 'AvgDmgDealt'].forEach((key) => {
          const va = valueOf(run, mode, n, key), vb = valueOf(b, mode, n, key);
          if (isNum(va) && isNum(vb) && Math.abs(va - vb) > 1e-9) {
            moved.push({ name: n, key, mode, rel: vb !== 0 ? (va - vb) / Math.abs(vb) : 1, abs: va - vb });
          }
        });
      });
    });
    moved.sort((x, y) => Math.abs(y.rel) - Math.abs(x.rel)).slice(0, 8).forEach((r) => {
      const li = el('li');
      const a = el('a', 'name', displayName(r.name));
      a.href = `#/kit/${encodeURIComponent(r.name)}`;
      li.appendChild(a);
      li.appendChild(el('span', 'why', `${meta(r.key).ru} · ${modeTitle(r.mode)}`));
      const better = meta(r.key).higher;
      const cls = better === undefined || better === null ? '' : (r.abs > 0) === better ? 'up' : 'down';
      li.appendChild(el('span', `num delta ${cls}`, `${r.abs > 0 ? '+' : '−'}${fmtDelta(r.key, r.abs)}`));
      diffList.appendChild(li);
    });
    if (!moved.length) diffList.appendChild(el('li', 'empty', 'Числа не изменились: правок между прогонами не было.'));
  }
  diffCard.appendChild(diffList);
  cols.appendChild(diffCard);

  view.appendChild(cols);

  // Ближайшие вопросы из реестра.
  if (open.length) {
    const card = el('div', 'card');
    card.appendChild(el('h2', null, 'Ждёт твоего вердикта'));
    const list = el('ul', 'lead');
    open.slice(0, 5).forEach((i) => {
      const li = el('li');
      li.appendChild(el('span', 'why', i.code));
      const a = el('a', 'name', i.title);
      a.href = '#/issues';
      li.appendChild(a);
      li.appendChild(el('span', 'flag warn', i.status));
      list.appendChild(li);
    });
    card.appendChild(list);
    view.appendChild(card);
  }
}

function tile(label, value, note, cls) {
  const box = el('div', 'tile' + (cls ? ' ' + cls : ''));
  box.appendChild(el('div', 't-label', label));
  box.appendChild(el('div', 't-value' + (typeof value === 'number' ? '' : ' text'), String(value)));
  if (note) box.appendChild(el('div', 't-note', note));
  return box;
}

// --- Каркас ---

function parseRoute() {
  const raw = (location.hash || '#/').slice(2).split('/');
  if (raw[0] === 'mode' && raw[1]) return { page: 'mode', key: decodeURIComponent(raw[1]) };
  if (raw[0] === 'kit' && raw[1]) return { page: 'kit', key: decodeURIComponent(raw[1]) };
  if (raw[0] === 'issues') return { page: 'issues' };
  return { page: 'overview' };
}

function renderNav() {
  const nav = $('#mainnav');
  nav.innerHTML = '';
  const run = runA();
  const route = state.route;

  const items = [['#/', 'Обзор', route.page === 'overview']];
  modesOf(run).forEach((mode) => {
    items.push([`#/mode/${encodeURIComponent(mode)}`, modeTitle(mode), route.page === 'mode' && route.key === mode]);
  });
  items.push(['#/issues', `Реестр проблем${(DATA.issues || []).length ? ` (${DATA.issues.length})` : ''}`, route.page === 'issues']);

  items.forEach(([href, text, on]) => {
    const a = el('a', on ? 'on' : null, text);
    a.href = href;
    nav.appendChild(a);
  });
}

function renderCrumbs() {
  const box = $('#crumbs');
  box.innerHTML = '';
  const route = state.route;
  if (route.page === 'overview') return;

  const home = el('a', null, 'Обзор');
  home.href = '#/';
  box.appendChild(home);
  box.appendChild(el('span', 'sep', '/'));

  if (route.page === 'mode') box.appendChild(el('span', 'here', modeTitle(route.key)));
  if (route.page === 'issues') box.appendChild(el('span', 'here', 'Реестр проблем'));
  if (route.page === 'kit') {
    box.appendChild(el('span', 'here', displayName(route.key)));
  }
}

function renderRunInfo(view) {
  const a = runA();
  if (!a) return;

  const card = el('div', 'run-info');
  card.appendChild(el('h2', null, a.title || 'Прогон без названия'));
  if (a.summary) card.appendChild(el('p', null, a.summary));
  const b = runB();
  if (b && b !== a) card.appendChild(el('p', 'against', `сравнение с: ${b.title || b.key}`));
  view.appendChild(card);
}

function render() {
  state.route = parseRoute();
  renderNav();
  renderCrumbs();

  const view = $('#view');
  view.innerHTML = '';
  renderRunInfo(view);

  if (state.route.page === 'overview') renderOverview();
  else if (state.route.page === 'mode') renderModeTable(state.route.key);
  else if (state.route.page === 'kit') renderKit(state.route.key);
  else if (state.route.page === 'issues') renderIssues();

  const a = runA(), b = runB();
  $('#foot').textContent = b && b !== a
    ? `Прогон ${a.key} против ${b.key}. Всего прогонов в истории: ${DATA.runs.length}.`
    : `Прогон ${a ? a.key : '—'}. Сравнивать не с чем: это единственный прогон в истории.`;
}

function renderSelectors() {
  const a = $('#runA'), b = $('#runB');
  [a, b].forEach((sel, i) => {
    sel.innerHTML = '';
    DATA.runs.forEach((run, idx) => {
      const o = el('option', null, run.title ? `${run.title} — ${run.key.slice(0, 16)}` : run.key);
      o.value = String(idx);
      sel.appendChild(o);
    });
    sel.value = String(i === 0 ? state.runA : state.runB);
  });
}

function renderSettings() {
  const list = $('#settingsList');
  list.innerHTML = '';
  SETTINGS_META.forEach(([key, name, desc]) => {
    const row = el('label', 'setting');
    const input = el('input');
    input.type = 'checkbox';
    input.checked = !!settings[key];
    input.onchange = () => { settings[key] = input.checked; saveSettings(); render(); };
    row.appendChild(input);
    const text = el('div', 's-text');
    text.appendChild(el('div', 's-name', name));
    text.appendChild(el('div', 's-desc', desc));
    row.appendChild(text);
    list.appendChild(row);
  });

  $('#settingsStorage').textContent = storageWorks
    ? 'Настройки сохраняются между запусками.'
    : 'Браузер запретил хранилище для локального файла — настройки забудутся при закрытии вкладки.';
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

  $('#settingsBtn').onclick = () => { renderSettings(); $('#settingsModal').hidden = false; };
  $('#settingsClose').onclick = () => { $('#settingsModal').hidden = true; };
  $('#settingsModal').onclick = (e) => { if (e.target.id === 'settingsModal') $('#settingsModal').hidden = true; };

  window.addEventListener('hashchange', render);
  render();
}

boot();
