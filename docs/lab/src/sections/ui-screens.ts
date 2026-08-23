/* Экраны игры целиком: по кадру на экран, снято из ЖИВОЙ игры.

   Заказ Макса 23.08.2026: «дай мне ссылку на сайте где лежат всегда обновленные экраны». Соседний
   раздел «Элементы интерфейса» отвечает на вопрос «как выглядит КНОПКА», а приёмка идёт по экранам —
   и три самых частых претензии (реф не отработан, метрика внутри элемента разъехалась, кусок молча
   пропал) не ловит ни один из статических гейтов. Их видно только на кадре целого экрана.

   Кадры снимает `Alebardium → UI → Screen Sheet` (пункт в Unity, работает в play): он проходит по
   `UiPreviewCatalog`, собирает каждый экран со стендовыми данными ПОВЕРХ настоящей панели и кладёт
   PNG прямо сюда. Поверх живой панели, а не на стенде, потому что своей заливки у экранов нет —
   задник рисует презентация, и снимок в изоляции показал бы экран на пустоте.

   Список НЕ захардкожен: он приходит манифестом того же прогона. Захардкоженный список разошёлся бы
   с каталогом на первом же новом экране — ровно эта болезнь была у прежней витрины компонентов. */

import { el, html } from "../dom.js";
import type { Feed } from "../api.js";
import type { SectionDef } from "../types.js";

interface Screen {
  id: string;
  file: string;
}

interface Manifest {
  screens: Screen[];
}

/** Копия локального `feed` из api.ts: тянуть туда раздел ради одного файла данных незачем. */
function feed<T>(url: string): Feed<T> {
  const state: Feed<T> = { data: null, error: null, settled: Promise.resolve() };
  state.settled = fetch(url)
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
    .then((json: T) => { state.data = json; })
    .catch((err: unknown) => {
      state.error = err instanceof Error ? err.message : String(err);
    });
  return state;
}

/** Человеческое имя экрана. Неизвестный id печатается как есть — новый экран не должен пропадать. */
const TITLE: Record<string, string> = {
  mainmenu: "Главное меню",
  newgame: "Выбор режима",
  profile: "Выбор профиля",
  slotcreate: "Создание гильдии",
  guilds: "Выбор гильдии",
  "guild-hub": "Двор гильдии",
  party: "Подготовка · Отряд",
  items: "Подготовка · Предметы",
  loadout: "Снаряжение Сосуда",
  "loadout-inventory": "Снаряжение · Реликвии",
  "vessel-card": "Карточка Сосуда",
  titlecard: "Заставка узла",
  shop: "Лавка",
  chest: "Сундук",
  event: "Событие",
  camp: "Привал",
  reward: "Награда",
  outcome: "Исход забега",
  settings: "Настройки",
  pause: "ESC-меню",
  devconsole: "Дев · Команды (F1)",
  "dev-log": "Дев · Лог движка (F2)",
  "dev-battles": "Дев · Витрина боёв (F3)"
};

/** Группы в порядке пути игрока: первый id группы открывает её заголовком. */
const GROUPS: { at: string; title: string }[] = [
  { at: "mainmenu",   title: "Вход в игру" },
  { at: "guild-hub",  title: "Гильдия и подготовка" },
  { at: "titlecard",  title: "Забег: узлы и исход" },
  { at: "settings",   title: "Служебное" },
  { at: "devconsole", title: "Дев-полки" }
];

/** Зачем экран нужен — одной строкой. Заказ Макса 23.08.2026: «Нужны описания что и есть что». */
const PURPOSE: Record<string, string> = {
  mainmenu: "точка входа: продолжить, начать, присоединиться",
  newgame: "выбор режима забега и того, открывать ли лобби",
  profile: "какой Гильдмастер играет; отсюда же создают и удаляют профиль",
  slotcreate: "заведение нового дома: имя, знак, цвет",
  guilds: "какой гильдией играем и что с её забегом",
  "guild-hub": "дом между забегами: отсюда уходят в акт",
  party: "кто идёт в бой — состав четвёрки и лента отряда",
  items: "что на ком надето — слоты вещей у каждого и склад",
  loadout: "снаряжение одного Сосуда: Реликвия, вещи, улучшения, поведение",
  "loadout-inventory": "склад гильдии: Реликвии, предметы, знамёна",
  "vessel-card": "всё об одном человеке: лор, травмы, снаряжение, Судьба",
  titlecard: "заставка узла — куда игрок пришёл",
  shop: "лавка узла: купить и продать",
  chest: "сундук узла",
  event: "текстовое событие с выбором",
  camp: "привал: чем занять передышку",
  reward: "награда за узел — выбор одной Реликвии",
  outcome: "итог забега: чем кончилось и что засчитано",
  settings: "настройки игры, графики и звука",
  pause: "меню по ESC: продолжить, настройки, выход",
  devconsole: "дев-полка F1: команды с подсказкой и историей",
  "dev-log": "дев-полка F2: хвост сообщений движка",
  "dev-battles": "дев-полка F3: витрина боёв с поиском"
};

/** Что на экране собрано заглушкой, а не настоящим содержимым: иначе кадр читается как поломка. */
const STUB: Record<string, string> = {
  chest: "экран-заглушка: содержимое сундука ещё не сделано",
  "guild-hub": "экран-заглушка: двор ещё обустраивают",
  party: "лиц нет: у архетипов Реликвий не заведено ни одного портрета",
  items: "значков нет: у предметов в базе не заведено ни одного"
};

const manifest = feed<Manifest>("data/ui-screens.json");

function screenCard(screen: Screen): HTMLElement {
  const box = el("article", "card");

  const head = el("div", "i-head");
  head.appendChild(el("h3", null, TITLE[screen.id] ?? screen.id));
  head.appendChild(el("span", "dim", screen.id));
  box.appendChild(head);

  // Зачем экран нужен — до кадра, а не после: список открывают, чтобы понять, ЧТО смотришь.
  const purpose = PURPOSE[screen.id];
  if (purpose) box.appendChild(el("p", "dim", purpose));

  // Кадр открывается в новой вкладке по клику: снят в 1920×1080, и внутри страницы неизбежно ужат —
  // мелкий текст и метрику на ужатом кадре не разглядеть, а именно за ними сюда и идут.
  const link = el("a") as HTMLAnchorElement;
  link.href = `assets/ui-screens/${screen.file}`;
  link.target = "_blank";
  link.rel = "noopener";

  const img = el("img") as HTMLImageElement;
  img.src = `assets/ui-screens/${screen.file}`;
  img.alt = TITLE[screen.id] ?? screen.id;
  img.loading = "lazy";
  img.style.width = "100%";
  img.style.display = "block";
  img.style.borderRadius = "4px";
  link.appendChild(img);
  box.appendChild(link);

  const note = STUB[screen.id];
  if (note) box.appendChild(el("p", "dim", note));

  return box;
}

function render(host: HTMLElement): void {
  const box = el("div");
  box.appendChild(el("p", "dim", "Загружаю кадры…"));
  host.appendChild(box);

  void manifest.settled.then(() => {
    box.textContent = "";

    if (!manifest.data?.screens?.length) {
      box.appendChild(html("p",
        "Кадров нет. Их кладёт пункт <code>Alebardium → UI → Screen Sheet</code> — " +
        "он работает <b>в play</b>: вне игры живой панели не существует, и снимать нечего." +
        (manifest.error ? `<br><span class="dim">${manifest.error}</span>` : ""),
        "note"));
      return;
    }

    box.appendChild(el("p", "dim", `Экранов в прогоне: ${manifest.data.screens.length}`));

    // ОДНА КОЛОНКА, а не сетка: кадр снят в 1920×1080, и в узкой карточке от экрана остаётся пятно.
    // Здесь смотрят детали — ради них прогон и делается.
    const grid = el("div");
    grid.style.display = "grid";
    grid.style.gap = "var(--gap)";
    for (const screen of manifest.data.screens) {
      // Заголовок группы стоит ПЕРЕД своим первым экраном, а не задаёт порядок сам: порядок держит
      // манифест, то есть каталог в игре. Заведи витрина свой — она разошлась бы с прогоном.
      const group = GROUPS.find((g) => g.at === screen.id);
      if (group) grid.appendChild(el("h2", null, group.title));
      grid.appendChild(screenCard(screen));
    }
    box.appendChild(grid);
  });
}

const section: SectionDef = {
  id: "ui-screens",
  title: "Экраны игры",
  lede: "По кадру на каждый экран, снято из живой игры поверх настоящего задника. Сюда смотрят, " +
        "когда вопрос про экран целиком: сошлась ли метрика, не пропал ли кусок, похоже ли на реф. " +
        "Состояния отдельных элементов — в соседнем разделе «Элементы интерфейса».",
  transport: false,
  blocks: [
    {
      kind: "note",
      html: "Обновляется прогоном <code>Alebardium → UI → Screen Sheet</code> в play. Кадры лежат " +
            "в репозитории, поэтому историю держит git: пропавший задник виден диффом картинки. " +
            "Список экранов приходит из <code>UiPreviewCatalog</code>, а гейт " +
            "<code>UiScreenCatalogGateTests</code> краснеет, если экран игры остался без записи — " +
            "стенд не может тихо разойтись с игрой."
    },
    { kind: "live", id: "ui-screens-gallery", render }
  ]
};

export default section;
