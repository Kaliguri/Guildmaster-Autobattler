/* Реестр Лаборатории: области верхнего уровня и разделы внутри них.

   Два уровня, а не один, потому что разделов стало больше десятка и плоский ряд перестал отвечать
   на вопрос «где я»: у «Барьера» и «Указателя ГДД» нет ничего общего, кроме сайта. Верхний ряд
   выбирает область, нижний — раздел внутри неё.

   Раздел появляется здесь ТОГДА, когда у него есть содержимое: заявленный, но пустой пункт врёт
   дважды — обещает витриной и роняет загрузку. Заказанные, но не наполненные разделы перечислены
   в docs/lab-site-progress.md, там им место, пока их нет на диске.

   load — литерал import(), поэтому путь к файлу проверяет КОМПИЛЯТОР, а не браузер в рантайме. */

import type { AreaDef, PageDef } from "./types.js";

export const AREAS: AreaDef[] = [
  {
    id: "gamefeel",
    title: "Джус",
    blurb: "Как выглядит бой: удар, статусы, эффекты, барьер, зоны. Всё живое и покадровое."
  },
  {
    id: "map",
    title: "Карта",
    blurb: "Акт как местность: формы областей, зоны фракций, плотность узлов.",
    icon: "◈"
  },
  {
    id: "meta",
    title: "Мета",
    blurb: "Что копится между забегами: оси открытий, каналы оплаты и созвездия на экране хаба.",
    icon: "✦"
  },
  {
    id: "style",
    title: "Стиль",
    blurb: "Цвет, типографика, сетка спрайтов — то, что читается из проекта и показывается как есть."
  },
  {
    id: "ui",
    title: "Интерфейс",
    blurb: "Чертежи экранов: что на них лежит, какого размера и почему именно так у рефов.",
    icon: "▣"
  },
  {
    id: "balance",
    title: "Баланс",
    blurb: "Прогоны SimBench, дельты между ними, реестр проблем.",
    icon: "⚖"
  },
  {
    id: "docs",
    title: "Документация",
    blurb: "Карта ГДД и технической вики: где что лежит и в каком оно состоянии.",
    icon: "§"
  }
];

export const PAGES: PageDef[] = [
  {
    id: "hits",
    area: "gamefeel",
    title: "Удар",
    blurb: "Взмах, три архетипа формы, слои попадания, гибриды двух стихий и дальний бой.",
    load: () => import("./sections/gamefeel-hits.js")
  },
  {
    id: "geometry",
    area: "gamefeel",
    title: "Геометрия удара",
    blurb: "Замер живых ассетов: якоря вида, путь клинка из клипа, длина и толщина всех четырёх форм.",
    load: () => import("./sections/gamefeel-geometry.js")
  },
  {
    id: "lineart",
    area: "gamefeel",
    title: "Лайн у эффектов",
    blurb: "Нужна ли форме удара тёмная кайма: четыре способа обвести серп, свалка из восьми и цена приёма.",
    load: () => import("./sections/gamefeel-lineart.js")
  },
  {
    id: "barrier",
    area: "gamefeel",
    title: "Барьер",
    blurb: "Форма купола, узор трещин, стопка оболочек по типам, два финала и блок как режим.",
    load: () => import("./sections/gamefeel-barrier.js")
  },
  {
    id: "status",
    area: "gamefeel",
    title: "Статусы",
    blurb: "Постоянный слой на теле: четыре канала, ступени стака и пасхалка на сломе шкалы.",
    load: () => import("./sections/gamefeel-status.js")
  },
  {
    id: "effects",
    area: "gamefeel",
    title: "Эффекты",
    blurb: "Словарь общих эффектов по каналам, кровь по DPS, жизнь эффекта от наложения до снятия.",
    load: () => import("./sections/gamefeel-effects.js")
  },
  {
    id: "ghosts",
    area: "gamefeel",
    title: "Призрачные копии",
    blurb: "Шлейф за рывком и удар по иллюзии на уклонении: пять вариаций хвоста и пять способов разрушить копию.",
    load: () => import("./sections/gamefeel-ghosts.js")
  },
  {
    id: "zones",
    area: "gamefeel",
    title: "Зоны",
    blurb: "Телеграф площади: подводка, срабатывание, след. Круг, линия и то, что стоит на арене.",
    load: () => import("./sections/gamefeel-zones.js")
  },
  {
    id: "bloom",
    area: "gamefeel",
    title: "Витрина блума",
    blurb: "Все светящиеся эффекты боя в одном кадре, по кадру на вариацию настроек свечения.",
    load: () => import("./sections/gamefeel-bloom.js")
  },
  {
    id: "floor",
    area: "gamefeel",
    title: "Пол арены",
    blurb: "Плита в пустоте: поверхность биома, борт сбоку, ничего под ней. Два вопроса под вердикт.",
    load: () => import("./sections/gamefeel-floor.js")
  },
  {
    id: "legacy",
    area: "gamefeel",
    title: "Отклонённое",
    blurb: "Проигравшие варианты живьём: чем именно они были хуже.",
    load: null, // страницу собирает каркас из статусов стендов, своего файла у неё нет
    icon: "×"
  },

  {
    id: "map-act",
    area: "map",
    title: "Акт целиком",
    blurb: "Настоящая карта из настоящего генератора: шестьдесят сидов, честные пропорции, ручки раскладки.",
    load: () => import("./sections/map-act.js"),
    icon: "◈"
  },
  {
    id: "map-shapes",
    area: "map",
    title: "Формы областей",
    blurb: "Область = форма × свойства: поле, остров, тропа, гребёнка, рукава, подземелье. Зоны поверх.",
    load: () => import("./sections/map-shapes.js"),
    icon: "◈"
  },

  {
    id: "map-zones",
    area: "map",
    title: "Зоны влияния",
    blurb: "Чем рисовать чужую землю: шесть заливок, три способа пометить узел, запланированные эффекты живьём.",
    load: () => import("./sections/map-zones-lab.js"),
    icon: "◈"
  },
  {
    id: "map-terrain",
    area: "map",
    title: "Земля и страна",
    blurb: "ОТЛОЖЕНО: рельеф землёй, фракция страной на ней. Берег, ореол, отмывка, штриховка — требует доработки.",
    load: () => import("./sections/map-terrain.js"),
    icon: "◈"
  },
  {
    id: "map-feel",
    area: "map",
    title: "Подача",
    blurb: "Два слоя на одном листе: чья земля и какой рельеф. Плюс реестр — что уже в игре, что впереди.",
    load: () => import("./sections/map-feel.js"),
    icon: "◈"
  },

  {
    id: "meta-unlocks",
    area: "meta",
    title: "Открытия и созвездия",
    blurb: "Восемь осей, три канала оплаты, все открытия списком и то, как они видны на экране хаба.",
    load: () => import("./sections/meta-unlocks.js"),
    icon: "✦"
  },

  {
    id: "palette",
    area: "style",
    title: "Палитра",
    blurb: "Токены цвета как они в игре сейчас: страница читает тему проекта с диска.",
    load: () => import("./sections/style-palette.js")
  },

  {
    id: "balance-overview",
    area: "balance",
    title: "Обзор",
    blurb: "Состояние ростера с одного взгляда: кто вне коридора, кто сильнее всех, что сдвинулось.",
    load: () => import("./sections/balance-overview.js"),
    icon: "⚖"
  },
  {
    id: "balance-runs",
    area: "balance",
    title: "Прогоны",
    blurb: "Замеры SimBench по режимам, дельты с прошлым прогоном, словарь метрик.",
    load: () => import("./sections/balance-runs.js"),
    icon: "⚖"
  },
  {
    id: "balance-kits",
    area: "balance",
    title: "Киты",
    blurb: "Кит целиком: роль, способности с числами, все замеры по корзинам и приметы к проверке.",
    load: () => import("./sections/balance-kits.js"),
    icon: "◆"
  },
  {
    id: "balance-matrix",
    area: "balance",
    title: "Кто кого бьёт",
    blurb: "Матрица исходов: строка встречает столбец. То, чего не видно в средних.",
    load: () => import("./sections/balance-matrix.js"),
    icon: "▦"
  },
  {
    id: "balance-issues",
    area: "balance",
    title: "Реестр проблем",
    blurb: "Что сломано, чем это видно, какие правки предложены и что решил Макс.",
    load: () => import("./sections/balance-issues.js"),
    icon: "!"
  },

  {
    id: "ui-mainmenu",
    area: "ui",
    title: "Главное меню",
    blurb: "Шесть раскладок по четырнадцати рефам: колонка у кромки, плита, центр, обложка, низ, два списка.",
    load: () => import("./sections/ui-mainmenu.js"),
    icon: "▤"
  },
  {
    id: "ui-settings",
    area: "ui",
    title: "Настройки",
    blurb: "Три схемы раскладки категорий: горизонтальные табы, вертикальные, две колонки без табов.",
    load: () => import("./sections/ui-settings.js"),
    icon: "▦"
  },
  {
    id: "ui-states",
    area: "ui",
    title: "Элементы интерфейса",
    blurb: "Каждый элемент во всех состояниях: покой, наведение, нажатие, фокус, выключено, отмечено. Снято из живой игры контактным листом.",
    load: () => import("./sections/ui-states.js"),
    icon: "▦"
  },
  {
    id: "ui-tabs",
    area: "ui",
    title: "Лента разделов",
    blurb: "Чем помечен активный таб: реф промерен, пять вариантов пометки. Вердикт общий для настроек, отряда и реликвий.",
    load: () => import("./sections/ui-tabs.js"),
    icon: "▭"
  },
  {
    id: "ui-backdrop",
    area: "ui",
    title: "Фон экранов меты",
    blurb: "Из чего сделана плоскость под настройками и меню: реф промерен пипеткой, четыре варианта в патине.",
    load: () => import("./sections/ui-backdrop.js"),
    icon: "◑"
  },
  {
    id: "ui-loadout",
    area: "ui",
    title: "Сбор отряда",
    blurb: "Единственный экран забега с решениями и единственный без рефов: вкладки, отряд сверху, боец слева.",
    load: () => import("./sections/ui-loadout.js"),
    icon: "⚔"
  },
  {
    id: "ui-party-items",
    area: "ui",
    title: "Отряд и предметы",
    blurb: "Две страницы одного экрана подготовки: состав из четырёх на каждой, восемь мест отряда, три слота предмета и четвёртый закрытый. По четыре раскладки на страницу.",
    load: () => import("./sections/ui-party-items.js"),
    icon: "◫"
  },
  {
    id: "ui-lobby",
    area: "ui",
    title: "Создать игру и лобби",
    blurb: "Настройки сессии и список игроков: панели рядом, два таба, развилка карточками.",
    load: () => import("./sections/ui-lobby.js"),
    icon: "◈"
  },
  {
    id: "ui-outcome",
    area: "ui",
    title: "Итоги боя",
    blurb: "Исход крупнее чисел, полосы на бойца, разбор по наведению: две панели, одна, лента.",
    load: () => import("./sections/ui-outcome.js"),
    icon: "✦"
  },
  {
    id: "ui-pause",
    area: "ui",
    title: "Пауза",
    blurb: "Пять строк и вопрос, сколько кадра закрыть: центр, колонка слева, полоса поперёк.",
    load: () => import("./sections/ui-pause.js"),
    icon: "‖"
  },
  {
    id: "ui-reward",
    area: "ui",
    title: "Награда",
    blurb: "Выбор из трёх: карточки с плашкой последствия, лежачие строки Hades, самодостаточные карточки.",
    load: () => import("./sections/ui-reward.js"),
    icon: "◆"
  },
  {
    id: "ui-shop",
    area: "ui",
    title: "Лавка",
    blurb: "Товары, кошелёк, реролл и продажа: ряд с панелью статов, плотная сетка, наша полка.",
    load: () => import("./sections/ui-shop.js"),
    icon: "¤"
  },
  {
    id: "ui-chest",
    area: "ui",
    title: "Сундук",
    blurb: "Рефов класса нет: два такта, раскрытие на месте или отказ от отдельного экрана.",
    load: () => import("./sections/ui-chest.js"),
    icon: "▣"
  },
  {
    id: "ui-event",
    area: "ui",
    title: "Событие",
    blurb: "Панель с заходящим артом, текст без панели вовсе, модалка по центру. И что обещает плашка.",
    load: () => import("./sections/ui-event.js"),
    icon: "§"
  },
  {
    id: "ui-camp",
    area: "ui",
    title: "Привал",
    blurb: "Бюджет действий и адресат: список как у события, отряд телами, карточки как у награды.",
    load: () => import("./sections/ui-camp.js"),
    icon: "▲"
  },
  {
    id: "ui-hud",
    area: "ui",
    title: "Боевой HUD",
    blurb: "Единственный экран, который меряется долей кадра: полная периферия, минимум, лента снизу.",
    load: () => import("./sections/ui-hud.js"),
    icon: "◍"
  },
  {
    id: "ui-courtyard",
    area: "ui",
    title: "Двор гильдии",
    blurb: "Заготовка под разговор: сцена с телами, сцена с панелью, витрина разделов без места.",
    load: () => import("./sections/ui-courtyard.js"),
    icon: "⌂"
  },
  {
    id: "ui-profile",
    area: "ui",
    title: "Выбор профиля",
    blurb: "Три раскладки экрана «кто я»: ряд карточек, ряд на подложке, слоты рядом с идентичностью.",
    load: () => import("./sections/ui-profile.js"),
    icon: "▤"
  },
  {
    id: "ui-guilds",
    area: "ui",
    title: "Выбор гильдии",
    blurb: "Дом как слот сохранения: ряд карточек, список с панелью дома, один раскрытый при свёрнутых.",
    load: () => import("./sections/ui-guilds.js"),
    icon: "▥"
  },
  {
    id: "ui-uplift",
    area: "ui",
    title: "Догнать рефы",
    blurb: "План работ по UI: семь пунктов приём за приёмом плюс раскладки пяти экранов — кадры рефов рядом с нашими.",
    load: () => import("./sections/ui-uplift.js"),
    icon: "△"
  },
  {
    id: "ui-notice",
    area: "ui",
    title: "Сообщения игроку",
    blurb: "Одна модель, два облика: лента для того, что не спрашивает, модалка для того, что ждёт ответа.",
    load: () => import("./sections/ui-notice.js"),
    icon: "!"
  },
  {
    id: "ui-log",
    area: "ui",
    title: "Журнал событий",
    blurb: "Кто кого убил и кто куда ушёл: угол HUD с угасанием, окно по клавише, оба режима сразу.",
    load: () => import("./sections/ui-log.js"),
    icon: "≡"
  },
  {
    id: "ui-players",
    area: "ui",
    title: "Список игроков",
    blurb: "Строка человека: цвет, ник, хозяин, пинг числом, где он сейчас. Плюс две отклонённые раскладки.",
    load: () => import("./sections/ui-players.js"),
    icon: "☰"
  },

  {
    id: "gdd",
    area: "docs",
    title: "Указатель ГДД",
    blurb: "Где что лежит в дизайн-документации: кластеры, статусы, объём. Текст остаётся в vault.",
    load: () => import("./sections/gdd-index.js"),
    icon: "§"
  }
];

export function areaOf(pageId: string): AreaDef | undefined {
  const page = PAGES.find((p) => p.id === pageId);
  return AREAS.find((a) => a.id === (page?.area ?? pageId));
}

export function pagesOf(areaId: string): PageDef[] {
  return PAGES.filter((p) => p.area === areaId);
}
