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
    id: "style",
    title: "Стиль",
    blurb: "Цвет, типографика, сетка спрайтов — то, что читается из проекта и показывается как есть."
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
    id: "zones",
    area: "gamefeel",
    title: "Зоны",
    blurb: "Телеграф площади: подводка, срабатывание, след. Круг, линия и то, что стоит на арене.",
    load: () => import("./sections/gamefeel-zones.js")
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
    id: "map-shapes",
    area: "map",
    title: "Формы областей",
    blurb: "Область = форма × свойства: поле, остров, тропа, гребёнка, рукава, подземелье. Зоны поверх.",
    load: () => import("./sections/map-shapes.js"),
    icon: "◈"
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
