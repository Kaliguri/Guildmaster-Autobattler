/* Догнать рефы: чем Heroes Olden Era и Guildrun сильнее нас, экран за экраном.

   Заход 21.08.2026 по заказу Макса: «У нас UI намного слабее их. Выписываем, пытаемся понять как
   сделать наш лучше, взяв эти рефы». Ориентир основной — Heroes Olden Era («мне нравится даже
   больше»), второй — Guildrun.

   Оси разбора назвал Макс, все четыре сразу: плоско (нет глубины), пусто и рыхло, нет декора и
   деталей, слабая типографика.

   Главное, что дал разбор: по РАЗМЕРАМ мы уже совпадаем с рефом — колонка меню, высоты кнопок,
   кегли лежат в тех же долях кадра. Разрыв в другом, и он в четырёх вещах: за интерфейсом нет
   изображения, у элементов нет концов, нет группировки паузами, состояние показано заливкой
   вместо света.

   Числа и полный разбор — `Art_Dev/UI Refs/_teardowns/08-gap-vs-refs.md`. */

import * as w from "./ui-wire.js";
import type { SectionDef } from "../types.js";

/** Кадры сравнения: слева реф, справа наш экран того же назначения. */
interface Pair {
  ref: string;
  refCaption: string;
  ours: string;
  oursCaption: string;
}

function el(tag: string): HTMLElement {
  return document.createElement(tag);
}

/** Ряд «реф — наш» картинками. Чертёж такое не покажет: разговор здесь о свете и плотности. */
function shots(pair: Pair): (host: HTMLElement) => void {
  return host => {
    const row = el("div");
    row.style.display = "grid";
    row.style.gridTemplateColumns = "1fr 1fr";
    row.style.gap = "12px";

    for (const [file, caption, tag] of [
      [pair.ref, pair.refCaption, "реф"],
      [pair.ours, pair.oursCaption, "мы сейчас"]
    ] as Array<[string, string, string]>) {
      const cell = el("figure");
      cell.style.margin = "0";

      const img = el("img") as HTMLImageElement;
      img.src = `assets/ui-uplift/${file}`;
      img.alt = caption;
      img.loading = "lazy";
      img.style.width = "100%";
      img.style.display = "block";
      img.style.borderRadius = "4px";
      cell.appendChild(img);

      const cap = el("figcaption");
      cap.style.marginTop = "6px";
      cap.style.fontSize = "12px";
      cap.style.color = "#8A8A93";
      cap.innerHTML = `<b style="color:#C9C9D2">${tag}</b> · ${caption}`;
      cell.appendChild(cap);

      row.appendChild(cell);
    }

    host.appendChild(row);
  };
}

/* ── Главное меню ─────────────────────────────────────────────────────────── */

/** Как сейчас: колонка слева, две трети кадра пусты. */
function menuNow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  w.text(ctx, "HAPPY GUILDMASTERS", { x: 0.075, y: 0.30 }, width, height, { size: 13 });
  for (let i = 0; i < 5; i++) {
    w.box(ctx, { x: 0.075, y: 0.43 + i * 0.075, w: 0.20, h: 0.06 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль", "Настройки", "Выход"][i] ?? "", size: 9 });
  }
  w.text(ctx, "0.0.4-dev", { x: 0.02, y: 0.95 }, width, height, { size: 8, color: "#8A8A93" });

  w.callout(ctx, { x: 0.62, y: 0.50 }, { x: 0.34, y: 0.50 },
    "60% ширины пусты", width, height, "right");
}

/** Вариант А: правую половину занимает живое — что происходит в гильдии. */
function menuPanel(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  w.box(ctx, { x: 0.06, y: 0.20, w: 0.24, h: 0.13 }, width, height,
    { label: "ВЫВЕСКА", sub: "в оправе, с лентой", size: 10 });

  for (let i = 0; i < 3; i++) {
    w.box(ctx, { x: 0.06, y: 0.40 + i * 0.075, w: 0.20, h: 0.06 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль"][i] ?? "", lit: i === 0, size: 9 });
  }
  // Служебное отбито паузой — приём рефа: 19% высоты кадра между группами.
  for (let i = 0; i < 2; i++) {
    w.box(ctx, { x: 0.06, y: 0.75 + i * 0.075, w: 0.20, h: 0.06 }, width, height,
      { label: ["Настройки", "Выход"][i] ?? "", size: 9 });
  }

  const panel: w.Rect = { x: 0.62, y: 0.22, w: 0.32, h: 0.56 };
  w.box(ctx, panel, width, height,
    { label: "ГИЛЬДИЯ", sub: "ростер · последний забег · раны", size: 10 });
  w.measure(ctx, panel, "32% x 56%", width, height);

  w.callout(ctx, { x: 0.30, y: 0.72 }, { x: 0.26, y: 0.72 },
    "пауза 19% высоты", width, height, "left");
}

/** Вариант Б: продолжить забег — крупной карточкой справа, остальное строками. */
function menuContinue(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  w.box(ctx, { x: 0.06, y: 0.18, w: 0.24, h: 0.13 }, width, height,
    { label: "ВЫВЕСКА", size: 10 });

  const card: w.Rect = { x: 0.55, y: 0.28, w: 0.38, h: 0.44 };
  w.box(ctx, card, width, height,
    { label: "ПРОДОЛЖИТЬ ЗАБЕГ", sub: "дом · акт · отряд · когда играли", lit: true, size: 11 });
  w.measure(ctx, card, "38% x 44%", width, height);

  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.06, y: 0.42 + i * 0.07, w: 0.18, h: 0.055 }, width, height,
      { label: ["Новый забег", "Присоединиться", "Профиль", "Настройки"][i] ?? "", size: 9 });
  }
  w.box(ctx, { x: 0.06, y: 0.80, w: 0.18, h: 0.055 }, width, height, { label: "Выход", size: 9 });

  w.callout(ctx, { x: 0.55, y: 0.24 }, { x: 0.74, y: 0.24 },
    "главное действие — самое крупное пятно", width, height, "left");
}

/** Вариант В: ничего не добавляем, только чиним ритм и концы. */
function menuRhythm(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  w.box(ctx, { x: 0.32, y: 0.16, w: 0.36, h: 0.16 }, width, height,
    { label: "ВЫВЕСКА ПО ЦЕНТРУ", sub: "в оправе", size: 11 });

  for (let i = 0; i < 3; i++) {
    w.box(ctx, { x: 0.38, y: 0.42 + i * 0.08, w: 0.24, h: 0.065 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль"][i] ?? "", lit: i === 0, size: 9 });
  }
  for (let i = 0; i < 2; i++) {
    w.box(ctx, { x: 0.42, y: 0.74 + i * 0.07, w: 0.16, h: 0.05 }, width, height,
      { label: ["Настройки", "Выход"][i] ?? "", size: 8 });
  }

  w.callout(ctx, { x: 0.42, y: 0.79 }, { x: 0.28, y: 0.79 },
    "служебное мельче и уже", width, height, "left");
}

/* ── Настройки ────────────────────────────────────────────────────────────── */

function settingsNow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  for (let i = 0; i < 3; i++) {
    w.box(ctx, { x: 0.24 + i * 0.18, y: 0.06, w: 0.16, h: 0.07 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", lit: i === 2, size: 9 });
  }
  for (let i = 0; i < 3; i++) {
    w.text(ctx, ["Общий", "Музыка", "Звук"][i] ?? "", { x: 0.27, y: 0.24 + i * 0.06 }, width, height,
      { size: 10 });
    w.box(ctx, { x: 0.49, y: 0.225 + i * 0.06, w: 0.17, h: 0.012 }, width, height, {});
  }
  w.callout(ctx, { x: 0.50, y: 0.60 }, { x: 0.50, y: 0.44 },
    "три строки на весь кадр", width, height, "left");
}

/** Вариант А: секции заголовками, две колонки, тумблеры-пилюли. */
function settingsSections(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.20 + i * 0.16, y: 0.05, w: 0.14, h: 0.065 }, width, height,
      { label: ["Игра", "Графика", "Звук", "Клавиши"][i] ?? "", lit: i === 2, hollow: i !== 2, size: 9 });
  }

  w.text(ctx, "ГРОМКОСТЬ", { x: 0.50, y: 0.19 }, width, height, { align: "center", size: 12 });
  for (let i = 0; i < 3; i++) {
    w.text(ctx, ["Общая", "Музыка", "Эффекты"][i] ?? "", { x: 0.30, y: 0.27 + i * 0.07 }, width, height,
      { size: 10 });
    w.box(ctx, { x: 0.55, y: 0.255 + i * 0.07, w: 0.15, h: 0.014 }, width, height, {});
    w.text(ctx, `${80 - i * 10}%`, { x: 0.73, y: 0.27 + i * 0.07 }, width, height, { size: 10 });
  }

  w.text(ctx, "ЗВУК В ИГРЕ", { x: 0.50, y: 0.56 }, width, height, { align: "center", size: 12 });
  for (let i = 0; i < 3; i++) {
    w.text(ctx, ["Голоса юнитов", "Звук интерфейса", "Приглушать в фоне"][i] ?? "",
      { x: 0.30, y: 0.64 + i * 0.07 }, width, height, { size: 10 });
    w.box(ctx, { x: 0.62, y: 0.625 + i * 0.07, w: 0.045, h: 0.035 }, width, height,
      { lit: i !== 1, size: 8 });
  }

  w.callout(ctx, { x: 0.50, y: 0.15 }, { x: 0.66, y: 0.15 },
    "секция — заголовком, не линией", width, height, "left");
}

/** Вариант Б: одна колонка по центру, узкая мера строки. */
function settingsColumn(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  for (let i = 0; i < 4; i++) {
    w.box(ctx, { x: 0.28 + i * 0.12, y: 0.05, w: 0.11, h: 0.06 }, width, height,
      { label: ["Игра", "Вид", "Звук", "Клавиши"][i] ?? "", lit: i === 2, hollow: i !== 2, size: 8 });
  }

  const col: w.Rect = { x: 0.30, y: 0.18, w: 0.40, h: 0.66 };
  w.box(ctx, col, width, height, { hollow: true });
  w.measure(ctx, col, "40% ширины", width, height);

  w.text(ctx, "ГРОМКОСТЬ", { x: 0.33, y: 0.24 }, width, height, { size: 11 });
  for (let i = 0; i < 3; i++) {
    w.text(ctx, ["Общая", "Музыка", "Эффекты"][i] ?? "", { x: 0.33, y: 0.32 + i * 0.07 }, width, height,
      { size: 10 });
    w.box(ctx, { x: 0.52, y: 0.305 + i * 0.07, w: 0.13, h: 0.014 }, width, height, {});
  }
  w.text(ctx, "ЗВУК В ИГРЕ", { x: 0.33, y: 0.58 }, width, height, { size: 11 });
  for (let i = 0; i < 2; i++) {
    w.text(ctx, ["Голоса юнитов", "Звук интерфейса"][i] ?? "", { x: 0.33, y: 0.66 + i * 0.07 },
      width, height, { size: 10 });
    w.box(ctx, { x: 0.60, y: 0.645 + i * 0.07, w: 0.045, h: 0.035 }, width, height, { lit: i === 0 });
  }
}

/* ── Исход забега ─────────────────────────────────────────────────────────── */

function outcomeNow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height);

  const panel: w.Rect = { x: 0.28, y: 0.28, w: 0.44, h: 0.44 };
  w.box(ctx, panel, width, height, { label: "ПОБЕДА", sub: "строкой на панели", size: 12 });
  w.box(ctx, { x: 0.42, y: 0.62, w: 0.16, h: 0.06 }, width, height, { label: "В меню", size: 9 });
}

/** Вариант А: знак и картуш — приём Heroes. */
function outcomeCrest(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.7);

  w.box(ctx, { x: 0.40, y: 0.20, w: 0.20, h: 0.28 }, width, height,
    { label: "ЗНАК", sub: "эмблема гильдии, лучи", lit: true, size: 11 });
  const cartouche: w.Rect = { x: 0.30, y: 0.50, w: 0.40, h: 0.10 };
  w.box(ctx, cartouche, width, height, { label: "ПОБЕДА", size: 14 });
  w.measure(ctx, cartouche, "40% x 10%", width, height);

  w.box(ctx, { x: 0.44, y: 0.78, w: 0.12, h: 0.055 }, width, height, { label: "В меню", size: 8 });
  w.callout(ctx, { x: 0.30, y: 0.55 }, { x: 0.16, y: 0.55 },
    "картуш: концы фигурные", width, height, "left");
}

/** Вариант Б: знак плюс итоги забега — то, что игрок захочет прочитать. */
function outcomeStats(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.7);

  w.box(ctx, { x: 0.42, y: 0.13, w: 0.16, h: 0.22 }, width, height,
    { label: "ЗНАК", lit: true, size: 10 });
  w.box(ctx, { x: 0.32, y: 0.37, w: 0.36, h: 0.09 }, width, height, { label: "ПОБЕДА", size: 13 });

  for (let i = 0; i < 4; i++) {
    w.text(ctx, ["Узлов пройдено", "Врагов повержено", "Ран получено", "Время забега"][i] ?? "",
      { x: 0.34, y: 0.53 + i * 0.06 }, width, height, { size: 10 });
    w.text(ctx, ["12", "48", "3", "41 мин"][i] ?? "", { x: 0.66, y: 0.53 + i * 0.06 }, width, height,
      { align: "right", size: 10 });
  }

  w.box(ctx, { x: 0.36, y: 0.82, w: 0.13, h: 0.055 }, width, height, { label: "Во двор", size: 8 });
  w.box(ctx, { x: 0.51, y: 0.82, w: 0.13, h: 0.055 }, width, height, { label: "В меню", size: 8 });
}

const section: SectionDef = {
  id: "ui-uplift",
  title: "Догнать рефы",
  blocks: [
    {
      kind: "head",
      id: "gap",
      title: "Откуда разрыв",
      lede:
        "Сравнение наших экранов с Heroes Olden Era и Guildrun, кадр к кадру. Оси разбора назвал " +
        "Макс: плоско, пусто, голо, слабый текст."
    },
    {
      kind: "text",
      html:
        "<b>Главное, что дал разбор.</b> По размерам мы уже совпадаем с рефом: колонка меню, " +
        "высоты кнопок и кегли лежат в тех же долях кадра — 15–20% ширины под меню, 4–5% высоты " +
        "под кнопку. Значит дело не в габаритах. Разрыв в четырёх вещах: <b>за интерфейсом нет " +
        "изображения</b>, <b>у элементов нет концов</b>, <b>нет группировки паузами</b> и " +
        "<b>состояние показано заливкой вместо света</b>."
    },
    {
      kind: "table",
      head: ["Приём Heroes", "Что он делает", "Как у нас"],
      rows: [
        ["За интерфейсом всегда изображение",
         "меню — панорама замка, настройки — звёздное небо, победа — карта мира",
         "ровная заливка либо голое травяное поле дуэли"],
        ["Картуш для важного",
         "заголовок в рамке с фигурными концами, логотип в орнаменте с лентой",
         "заголовок — просто текст, вывеска без оправы"],
        ["Свечение как выделение",
         "активный таб светится ореолом и получает шеврон снизу",
         "активный таб — плоский залитый прямоугольник"],
        ["Концы у элементов",
         "каждая кнопка кончается уголком-стрелкой с двух сторон",
         "пластина с фаской, концов нет"],
        ["Группировка паузами",
         "между основными пунктами и служебными — 19% высоты кадра",
         "шесть пунктов одним равномерным столбиком"],
        ["Секции заголовками",
         "«ОБЩИЕ», «ИГРОВОЙ ПРОЦЕСС» крупно по центру, между строками пусто",
         "секций нет, есть линии-разделители"],
        ["Углы кадра заняты",
         "кнопка «назад», отчёт об ошибке, панель новостей, версия",
         "занят один угол из четырёх"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Guildrun добавляет два своих приёма:</b> игровое поле обрамлено артом (камни, деревья, " +
        "кристаллы по краям кадра — граница боя читается окружением, а не пустотой до края экрана) " +
        "и скошенные торцы у панелей HUD."
    },

    { kind: "head", id: "menu", title: "Главное меню", lede: "Реф держится не на кнопках, а на том, что за ними." },
    {
      kind: "live",
      id: "menu-shots",
      render: shots({
        ref: "ref-menu.jpg",
        refCaption: "Heroes: панорама на две трети кадра, меню узкой колонкой слева, справа панель новостей",
        ours: "ours-menu.jpg",
        oursCaption: "наше меню с работающим боем-фоном: четыре бойца на голом травяном поле"
      })
    },
    {
      kind: "text",
      html:
        "Замер рефа: колонка кнопок 305 px (15.9% ширины), кнопка 48 px (4.4% высоты), зазор 19 px, " +
        "логотип 365×150. У нас колонка 380 px (19.8%), кнопка 50 px, зазор 22 px — мы даже крупнее. " +
        "Не хватает не размера, а <b>занятого кадра</b>, <b>иерархии в столбце</b> и <b>оправы у вывески</b>."
    },
    {
      kind: "stands",
      items: [
        {
          id: "menu-now",
          status: "note",
          title: "Как сейчас",
          tag: "снято 21.08.2026",
          note: "Колонка слева, вывеска над ней, версия внизу. Правые 60% ширины не заняты ничем.",
          verdict: "Бой за меню идёт, но арена без обрамления читается зелёным шумом, а не картиной.",
          size: [480, 270],
          draw: menuNow
        },
        {
          id: "menu-panel",
          status: "waiting",
          title: "А · правую половину занимает гильдия",
          note:
            "Приём Heroes и Guildrun: справа живая панель. У них новости и таблица лидеров, у нас " +
            "естественнее ростер — кто в гильдии, чем кончился последний забег, у кого раны.",
          facts: [["панель", "32% x 56%"], ["пауза перед служебным", "19% высоты"]],
          verdict:
            "Занимает кадр тем, что игроку интересно, и заодно чинит ритм столбца. Требует экрана " +
            "гильдии в готовом виде — данные для панели берутся оттуда.",
          size: [480, 270],
          draw: menuPanel
        },
        {
          id: "menu-continue",
          status: "waiting",
          title: "Б · продолжить забег крупной карточкой",
          note:
            "Главное действие получает самое крупное пятно кадра, остальные пункты уходят в узкий " +
            "столбик слева. Карточка показывает дом, акт, отряд и когда играли.",
          facts: [["карточка", "38% x 44%"], ["строки меню", "18% ширины"]],
          verdict:
            "Сильнее всех отвечает на «что мне тут делать» и не требует нового арта. Но пустует у " +
            "того, кто ещё не начинал забег — нужен второй вид карточки для чистого профиля.",
          size: [480, 270],
          draw: menuContinue
        },
        {
          id: "menu-rhythm",
          status: "waiting",
          title: "В · только ритм и концы",
          note:
            "Ничего не добавляем: вывеска по центру в оправе, три основных пункта, пауза, служебные " +
            "мельче и уже. Плюс концы у кнопок.",
          verdict:
            "Самый дешёвый вариант — правка отступов и одного контрола. Кадр остаётся полупустым, " +
            "но перестаёт быть рыхлым.",
          size: [480, 270],
          draw: menuRhythm
        }
      ]
    },

    { kind: "head", id: "settings", title: "Настройки", lede: "У рефа плотный экран без единой линии-разделителя." },
    {
      kind: "live",
      id: "settings-shots",
      render: shots({
        ref: "ref-settings.jpg",
        refCaption: "Heroes: звёздное небо, секции заголовками, активный таб со свечением и шевроном",
        ours: "ours-settings.jpg",
        oursCaption: "наши настройки: задник есть, но на вкладке три строки и таб залит плашкой"
      })
    },
    {
      kind: "stands",
      items: [
        {
          id: "settings-now",
          status: "note",
          title: "Как сейчас",
          note: "Три ползунка на весь кадр, активный таб залит, секций нет.",
          verdict: "Экран читается недоделанным, а не просторным.",
          size: [480, 270],
          draw: settingsNow
        },
        {
          id: "settings-sections",
          status: "waiting",
          title: "А · секции заголовками, две колонки",
          note:
            "Подпись слева, контрол справа, между ними чистая пустота — линии не нужны. Секция " +
            "объявлена крупным заголовком по центру. Тумблер — пилюля вместо квадрата.",
          facts: [["строка", "подпись 30% · контрол 55%"], ["шаг строк", "7% высоты"]],
          verdict:
            "Прямой перенос приёма рефа. Требует, чтобы настроек стало больше: на трёх строках " +
            "секции выглядят пустым обещанием.",
          size: [480, 270],
          draw: settingsSections
        },
        {
          id: "settings-column",
          status: "waiting",
          title: "Б · одна колонка по центру",
          note:
            "Мера строки ограничена 40% ширины кадра — глазу не приходится ехать через весь экран " +
            "от подписи к контролу.",
          facts: [["колонка", "40% ширины"]],
          verdict:
            "Читается лучше на широком мониторе и не требует новых настроек. Зато кадр по бокам " +
            "пустеет ещё сильнее — просит задника с рисунком.",
          size: [480, 270],
          draw: settingsColumn
        }
      ]
    },

    { kind: "head", id: "outcome", title: "Исход забега", lede: "У момента триумфа должен быть знак, а не строка." },
    {
      kind: "live",
      id: "outcome-shots",
      render: shots({
        ref: "ref-outcome.jpg",
        refCaption: "Heroes: карта мира приглушена, в центре эмблема с лучами, поверх картуш «ПОБЕДА»",
        ours: "ours-reward.jpg",
        oursCaption: "наш ближайший аналог — экран награды: панель, карточки, кнопки"
      })
    },
    {
      kind: "stands",
      items: [
        {
          id: "outcome-now",
          status: "note",
          title: "Как сейчас",
          note: "Панель со scrim, заголовок строкой, кнопка выхода.",
          verdict: "Победа и поражение отличаются одним словом в заголовке.",
          size: [480, 270],
          draw: outcomeNow
        },
        {
          id: "outcome-crest",
          status: "waiting",
          title: "А · знак и картуш",
          note:
            "Панели нет вовсе: приглушённый мир, крупная эмблема с лучами, заголовок в картуше с " +
            "фигурными концами, одна скромная кнопка внизу.",
          facts: [["знак", "20% x 28%"], ["картуш", "40% x 10%"]],
          verdict:
            "Самый близкий к рефу и самый дорогой: нужен арт эмблемы. Без него знак придётся " +
            "собирать из формы и света.",
          size: [480, 270],
          draw: outcomeCrest
        },
        {
          id: "outcome-stats",
          status: "waiting",
          title: "Б · знак плюс итоги забега",
          note:
            "То же, но под картушем — четыре строки итогов: узлов, врагов, ран, время. Числа у нас " +
            "уже считаются для статистики профиля.",
          verdict:
            "Даёт игроку что почитать в момент, когда он готов читать. Рискует превратить триумф " +
            "в отчёт — цифры должны быть тише знака.",
          size: [480, 270],
          draw: outcomeStats
        }
      ]
    },

    { kind: "head", id: "order", title: "Порядок работ", lede: "По отношению «эффект к затратам», а не по важности." },
    {
      kind: "table",
      head: ["Что", "Цена", "Где видно"],
      rows: [
        ["Концы у кнопок и панелей (уголки, шевроны, срезы)", "правка одного контрола", "на каждом экране"],
        ["Свечение вместо заливки у активного и выбранного", "одно правило темы", "табы, слоты, карточки"],
        ["Группировка паузами", "отступы, ноль кода", "меню, настройки"],
        ["Заголовки секций вместо линий", "разметка экрана", "настройки, профиль"],
        ["Занять углы кадра", "мелкие блоки", "меню, HUD"],
        ["Обрамление арены артом", "нужен художник", "бой, меню-фон"],
        ["Знак на экране исхода", "нужен художник", "исход забега"]
      ]
    },
    {
      kind: "note",
      html:
        "Первые пять делаются темой и раскладкой, без художника. Последние два ждут арта — и это " +
        "честная граница: их «ПОСЛЕ» останется макетом, пока арта нет."
    }
  ]
};

export default section;
