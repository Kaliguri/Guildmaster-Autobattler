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


/* ── Лоадаут ──────────────────────────────────────────────────────────────── */

/** Как сейчас: сетка внутри панели, справа описание. */
function loadoutNow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  const panel: w.Rect = { x: 0.42, y: 0.08, w: 0.56, h: 0.86 };
  w.box(ctx, panel, width, height, {});
  for (let r = 0; r < 3; r++)
    for (let c = 0; c < 4; c++)
      w.box(ctx, { x: 0.44 + c * 0.075, y: 0.16 + r * 0.20, w: 0.065, h: 0.17 }, width, height,
        { lit: r === 0 && c === 0, size: 8 });
  w.box(ctx, { x: 0.76, y: 0.12, w: 0.21, h: 0.78 }, width, height,
    { label: "ОПИСАНИЕ", sub: "статы строками", size: 9 });
  w.callout(ctx, { x: 0.42, y: 0.50 }, { x: 0.22, y: 0.50 },
    "левая треть кадра пуста", width, height, "right");
}

/** Вариант А: витрина без панели — приём Guildrun. */
function loadoutShowcase(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  w.box(ctx, { x: 0.02, y: 0.03, w: 0.09, h: 0.06 }, width, height, { label: "Назад", size: 8 });
  for (let i = 0; i < 2; i++)
    w.box(ctx, { x: 0.38 + i * 0.13, y: 0.03, w: 0.12, h: 0.06 }, width, height,
      { label: ["Реликвии", "Предметы"][i] ?? "", lit: i === 0, size: 8 });
  w.box(ctx, { x: 0.72, y: 0.03, w: 0.16, h: 0.06 }, width, height, { label: "Поиск", size: 8 });
  w.box(ctx, { x: 0.90, y: 0.03, w: 0.08, h: 0.06 }, width, height, { label: "Все", size: 8 });

  w.text(ctx, "СОБРАНО 13 ИЗ 75", { x: 0.03, y: 0.17 }, width, height, { size: 13 });
  w.text(ctx, "реликвии открываются за победы над элитой", { x: 0.03, y: 0.22 }, width, height,
    { size: 9, color: "#8A8A93" });

  for (let r = 0; r < 3; r++)
    for (let c = 0; c < 9; c++)
      w.box(ctx, { x: 0.03 + c * 0.105, y: 0.28 + r * 0.22, w: 0.095, h: 0.19 }, width, height,
        { lit: r === 0 && c === 0, dashed: r === 2 && c > 4, size: 7 });

  w.callout(ctx, { x: 0.50, y: 0.25 }, { x: 0.66, y: 0.25 },
    "панели нет: сетка прямо на фоне", width, height, "left");
}

/* ── Боевой HUD ───────────────────────────────────────────────────────────── */

function hudNow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.box(ctx, { x: 0.30, y: 0.01, w: 0.40, h: 0.06 }, width, height, { label: "топбар", size: 8 });
  w.box(ctx, { x: 0.40, y: 0.40, w: 0.08, h: 0.02 }, width, height, {});
  w.box(ctx, { x: 0.56, y: 0.44, w: 0.08, h: 0.02 }, width, height, {});
  w.callout(ctx, { x: 0.10, y: 0.80 }, { x: 0.30, y: 0.80 },
    "трава до края кадра, углы пусты", width, height, "left");
}

/** Вариант А: обрамление поля и карточка выбранного — приём Guildrun. */
function hudFramed(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  // Обрамление: арт по краям кадра, поле боя в световом пятне посередине.
  w.box(ctx, { x: 0.00, y: 0.00, w: 0.16, h: 1.00 }, width, height,
    { label: "арт", sub: "камни, деревья", size: 8, hollow: true });
  w.box(ctx, { x: 0.84, y: 0.00, w: 0.16, h: 1.00 }, width, height,
    { label: "арт", size: 8, hollow: true });

  w.box(ctx, { x: 0.28, y: 0.01, w: 0.44, h: 0.06 }, width, height,
    { label: "ресурсы · статусы · таймер", sub: "торцы скошены", size: 8 });
  w.box(ctx, { x: 0.74, y: 0.60, w: 0.22, h: 0.34 }, width, height,
    { label: "ВЫБРАННЫЙ", sub: "портрет · HP · статы · слоты", lit: true, size: 9 });
  w.box(ctx, { x: 0.18, y: 0.90, w: 0.40, h: 0.08 }, width, height,
    { label: "отряд · резерв", size: 8 });

  w.callout(ctx, { x: 0.16, y: 0.45 }, { x: 0.30, y: 0.45 },
    "границу боя держит арт, а не пустота", width, height, "left");
}

/** Крупная кнопка по центру сцены: общая заготовка для пунктов про форму. */
function plate(ctx: CanvasRenderingContext2D, width: number, height: number,
                label: string, lit = false): w.Rect {
  const r: w.Rect = { x: 0.12, y: 0.36, w: 0.76, h: 0.28 };
  w.box(ctx, r, width, height, { label, lit, size: 14 });
  return r;
}

/* ── 1. Концы ─────────────────────────────────────────────────────────────── */

function capsNone(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  plate(ctx, width, height, "СОЗДАТЬ ИГРУ");
  w.callout(ctx, { x: 0.14, y: 0.50 }, { x: 0.04, y: 0.72 }, "кромка пустая", width, height, "left");
}

/** Шеврон остриями внутрь — то, что сделано. */
function capsChevron(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  const r = plate(ctx, width, height, "СОЗДАТЬ ИГРУ", true);
  const midY = (r.y + r.h * 0.5) * height;
  const size = height * 0.07;

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  for (const [x, dir] of [[0.17, 1], [0.83, -1]] as Array<[number, number]>) {
    ctx.beginPath();
    ctx.moveTo(x * width, midY - size);
    ctx.lineTo(x * width + dir * size * 0.7, midY);
    ctx.lineTo(x * width, midY + size);
    ctx.stroke();
  }
  w.callout(ctx, { x: 0.17, y: 0.72 }, { x: 0.06, y: 0.84 }, "шеврон внутрь", width, height, "left");
}

/** Скобки-уголки: две грани угла у каждой кромки. */
function capsBrackets(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  const r = plate(ctx, width, height, "СОЗДАТЬ ИГРУ");
  const top = (r.y + 0.05) * height;
  const bottom = (r.y + r.h - 0.05) * height;
  const arm = width * 0.035;

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  for (const [x, dir] of [[0.16, 1], [0.84, -1]] as Array<[number, number]>) {
    ctx.beginPath();
    ctx.moveTo(x * width + dir * arm, top);
    ctx.lineTo(x * width, top);
    ctx.lineTo(x * width, bottom);
    ctx.lineTo(x * width + dir * arm, bottom);
    ctx.stroke();
  }
  w.callout(ctx, { x: 0.16, y: 0.72 }, { x: 0.05, y: 0.84 }, "скобка", width, height, "left");
}

/* ── 2. Свечение вместо заливки ───────────────────────────────────────────── */

function litFill(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", lit: i === 2, size: 12 });
  w.callout(ctx, { x: 0.79, y: 0.56 }, { x: 0.60, y: 0.76 }, "плоская заливка", width, height, "left");
}

/** Ореол вокруг активного плюс шеврон снизу — приём Heroes. */
function litGlow(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);

  const active: w.Rect = { x: 0.66, y: 0.30, w: 0.26, h: 0.22 };
  const [ax, ay, aw, ah] = w.px(active, width, height);
  const grad = ctx.createRadialGradient(ax + aw / 2, ay + ah / 2, 2, ax + aw / 2, ay + ah / 2, aw * 0.8);
  grad.addColorStop(0, "rgba(200,162,76,0.35)");
  grad.addColorStop(1, "rgba(200,162,76,0)");
  ctx.fillStyle = grad;
  ctx.fillRect(ax - aw * 0.5, ay - ah * 0.8, aw * 2, ah * 2.6);

  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", hollow: true, size: 12 });

  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(ax + aw * 0.42, ay + ah + 8);
  ctx.lineTo(ax + aw * 0.5, ay + ah + 16);
  ctx.lineTo(ax + aw * 0.58, ay + ah + 8);
  ctx.stroke();

  w.callout(ctx, { x: 0.79, y: 0.62 }, { x: 0.56, y: 0.80 }, "ореол + шеврон", width, height, "left");
}

/** Только подчёркивание светом: заливки нет вовсе. */
function litUnderline(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08 + i * 0.29, y: 0.30, w: 0.26, h: 0.22 }, width, height,
      { label: ["Игра", "Графика", "Звук"][i] ?? "", hollow: true, size: 12 });

  const grad = ctx.createLinearGradient(0.66 * width, 0, 0.92 * width, 0);
  grad.addColorStop(0, "rgba(200,162,76,0)");
  grad.addColorStop(0.5, "rgba(200,162,76,0.9)");
  grad.addColorStop(1, "rgba(200,162,76,0)");
  ctx.fillStyle = grad;
  ctx.fillRect(0.66 * width, 0.53 * height, 0.26 * width, 3);

  w.callout(ctx, { x: 0.79, y: 0.56 }, { x: 0.58, y: 0.78 }, "черта с растушёвкой", width, height, "left");
}

/* ── 3. Группировка паузами ───────────────────────────────────────────────── */

function rhythmFlat(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 5; i++)
    w.box(ctx, { x: 0.28, y: 0.12 + i * 0.16, w: 0.44, h: 0.12 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль", "Настройки", "Выход"][i] ?? "", size: 11 });
  w.callout(ctx, { x: 0.72, y: 0.50 }, { x: 0.86, y: 0.50 }, "шаг один на всех", width, height, "left");
}

function rhythmGrouped(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.28, y: 0.10 + i * 0.16, w: 0.44, h: 0.12 }, width, height,
      { label: ["Создать игру", "Присоединиться", "Профиль"][i] ?? "", lit: i === 0, size: 11 });
  for (let i = 0; i < 2; i++)
    w.box(ctx, { x: 0.33, y: 0.70 + i * 0.14, w: 0.34, h: 0.10 }, width, height,
      { label: ["Настройки", "Выход"][i] ?? "", size: 10 });

  ctx.strokeStyle = w.WIRE.line;
  ctx.setLineDash([4, 4]);
  ctx.beginPath();
  ctx.moveTo(0.24 * width, 0.60 * height);
  ctx.lineTo(0.76 * width, 0.60 * height);
  ctx.stroke();
  ctx.setLineDash([]);

  w.callout(ctx, { x: 0.24, y: 0.60 }, { x: 0.06, y: 0.60 }, "пауза 19%", width, height, "left");
}

/* ── 4. Заголовки секций ──────────────────────────────────────────────────── */

function sectionsLines(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  for (let i = 0; i < 4; i++) {
    w.text(ctx, ["Общий", "Музыка", "Звук", "Голоса"][i] ?? "", { x: 0.14, y: 0.20 + i * 0.20 },
      width, height, { size: 12 });
    w.box(ctx, { x: 0.52, y: 0.185 + i * 0.20, w: 0.34, h: 0.03 }, width, height, {});
    if (i < 3) {
      ctx.strokeStyle = w.WIRE.line;
      ctx.beginPath();
      ctx.moveTo(0.12 * width, (0.30 + i * 0.20) * height);
      ctx.lineTo(0.88 * width, (0.30 + i * 0.20) * height);
      ctx.stroke();
    }
  }
  w.callout(ctx, { x: 0.50, y: 0.30 }, { x: 0.50, y: 0.90 }, "линии режут поровну", width, height, "center");
}

function sectionsHeads(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.text(ctx, "ГРОМКОСТЬ", { x: 0.50, y: 0.14 }, width, height, { align: "center", size: 14 });
  for (let i = 0; i < 2; i++) {
    w.text(ctx, ["Общая", "Музыка"][i] ?? "", { x: 0.16, y: 0.30 + i * 0.14 }, width, height, { size: 12 });
    w.box(ctx, { x: 0.54, y: 0.285 + i * 0.14, w: 0.30, h: 0.03 }, width, height, {});
  }
  w.text(ctx, "ЗВУК В ИГРЕ", { x: 0.50, y: 0.62 }, width, height, { align: "center", size: 14 });
  for (let i = 0; i < 2; i++) {
    w.text(ctx, ["Голоса юнитов", "Звук интерфейса"][i] ?? "", { x: 0.16, y: 0.76 + i * 0.13 },
      width, height, { size: 12 });
    w.box(ctx, { x: 0.66, y: 0.735 + i * 0.13, w: 0.10, h: 0.07 }, width, height, { lit: i === 0 });
  }
  w.callout(ctx, { x: 0.50, y: 0.55 }, { x: 0.86, y: 0.50 }, "делит заголовок", width, height, "left");
}

/* ── 5. Углы кадра ────────────────────────────────────────────────────────── */

function cornersEmpty(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08, y: 0.36 + i * 0.14, w: 0.26, h: 0.10 }, width, height, { size: 9 });
  w.text(ctx, "0.0.4-dev", { x: 0.03, y: 0.94 }, width, height, { size: 8, color: "#8A8A93" });
  w.callout(ctx, { x: 0.80, y: 0.14 }, { x: 0.62, y: 0.14 }, "пусто", width, height, "right");
  w.callout(ctx, { x: 0.80, y: 0.88 }, { x: 0.62, y: 0.88 }, "пусто", width, height, "right");
}

function cornersFilled(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 3; i++)
    w.box(ctx, { x: 0.08, y: 0.36 + i * 0.14, w: 0.26, h: 0.10 }, width, height, { size: 9 });

  w.box(ctx, { x: 0.86, y: 0.04, w: 0.10, h: 0.10 }, width, height, { label: "?", size: 12 });
  w.box(ctx, { x: 0.72, y: 0.86, w: 0.10, h: 0.10 }, width, height, { label: "D", size: 10 });
  w.box(ctx, { x: 0.85, y: 0.86, w: 0.10, h: 0.10 }, width, height, { label: "YT", size: 10 });
  w.text(ctx, "0.0.4-dev", { x: 0.03, y: 0.94 }, width, height, { size: 8, color: "#8A8A93" });

  w.callout(ctx, { x: 0.86, y: 0.09 }, { x: 0.66, y: 0.09 }, "справка · клавиши", width, height, "right");
  w.callout(ctx, { x: 0.72, y: 0.91 }, { x: 0.56, y: 0.91 }, "ссылки сообщества", width, height, "right");
}

/* ── 6. Обрамление арены ──────────────────────────────────────────────────── */

function arenaBare(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.30 + (i % 2) * 0.24, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.04, y: 0.20 }, { x: 0.22, y: 0.20 }, "поле уходит в край кадра", width, height, "left");
}

/** Вариант арта: обрамление предметами по краям. */
function arenaArt(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.box(ctx, { x: 0.00, y: 0.00, w: 0.15, h: 1.00 }, width, height,
    { label: "арт", sub: "камни · листва", hollow: true, size: 9 });
  w.box(ctx, { x: 0.85, y: 0.00, w: 0.15, h: 1.00 }, width, height, { label: "арт", hollow: true, size: 9 });
  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.34 + (i % 2) * 0.20, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.15, y: 0.20 }, { x: 0.30, y: 0.18 }, "рамка из предметов", width, height, "left");
}

/** Вариант без художника: свет и виньетка. */
function arenaVignette(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  const grad = ctx.createRadialGradient(width * 0.5, height * 0.5, height * 0.2,
                                        width * 0.5, height * 0.5, width * 0.62);
  grad.addColorStop(0, "rgba(0,0,0,0)");
  grad.addColorStop(1, "rgba(0,0,0,0.72)");
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, width, height);

  for (let i = 0; i < 4; i++)
    w.box(ctx, { x: 0.34 + (i % 2) * 0.20, y: 0.40 + Math.floor(i / 2) * 0.14, w: 0.06, h: 0.08 },
      width, height, { size: 8 });
  w.callout(ctx, { x: 0.12, y: 0.18 }, { x: 0.30, y: 0.14 }, "виньетка светом", width, height, "left");
}

/* ── 7. Знак на исходе ────────────────────────────────────────────────────── */

function crestNone(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height);
  w.box(ctx, { x: 0.24, y: 0.34, w: 0.52, h: 0.32 }, width, height, { label: "ПОБЕДА", size: 16 });
}

/** Эмблема с лучами — приём Heroes. */
function crestRays(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.72);

  const cx = width * 0.5;
  const cy = height * 0.38;
  ctx.strokeStyle = "rgba(200,162,76,0.45)";
  ctx.lineWidth = 2;
  for (let i = 0; i < 12; i++) {
    const a = (Math.PI * 2 * i) / 12;
    ctx.beginPath();
    ctx.moveTo(cx + Math.cos(a) * height * 0.16, cy + Math.sin(a) * height * 0.16);
    ctx.lineTo(cx + Math.cos(a) * height * 0.30, cy + Math.sin(a) * height * 0.30);
    ctx.stroke();
  }
  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.arc(cx, cy, height * 0.15, 0, Math.PI * 2);
  ctx.stroke();
  w.text(ctx, "знак", { x: 0.50, y: 0.38 }, width, height, { align: "center", size: 11 });

  w.box(ctx, { x: 0.26, y: 0.70, w: 0.48, h: 0.12 }, width, height, { label: "ПОБЕДА", size: 14 });
  w.callout(ctx, { x: 0.68, y: 0.30 }, { x: 0.86, y: 0.24 }, "лучи из-за знака", width, height, "left");
}

/** Печать-штамп: знак ложится ПОВЕРХ картуша, как оттиск. */
function crestStamp(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);
  w.scrim(ctx, width, height, 0.72);

  w.box(ctx, { x: 0.20, y: 0.42, w: 0.60, h: 0.16 }, width, height, { label: "ПОБЕДА", size: 16 });

  ctx.save();
  ctx.translate(width * 0.76, height * 0.50);
  ctx.rotate(-0.22);
  ctx.strokeStyle = w.WIRE.accent;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.arc(0, 0, height * 0.13, 0, Math.PI * 2);
  ctx.stroke();
  ctx.restore();
  w.text(ctx, "печать", { x: 0.76, y: 0.50 }, width, height, { align: "center", size: 10 });

  w.callout(ctx, { x: 0.76, y: 0.66 }, { x: 0.86, y: 0.80 }, "оттиск поверх", width, height, "left");
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

    { kind: "head", id: "order", title: "Порядок работ", lede: "По отношению «эффект к затратам», а не по важности." },
    {
      kind: "table",
      head: ["№", "Что", "Цена", "Статус"],
      rows: [
        ["1", "Концы у кнопок и панелей (шевроны, скобки, срезы)", "правка одного контрола", "в игре с 21.08"],
        ["2", "Свечение вместо заливки у активного и выбранного", "правило темы", "ждёт вердикта"],
        ["3", "Группировка паузами", "отступы, ноль кода", "ждёт вердикта"],
        ["4", "Заголовки секций вместо линий", "разметка экрана", "ждёт вердикта"],
        ["5", "Занять углы кадра", "мелкие блоки", "ждёт вердикта"],
        ["6", "Обрамление арены артом", "нужен художник", "ждёт вердикта"],
        ["7", "Знак на экране исхода", "нужен художник", "ждёт вердикта"]
      ]
    },
    { kind: "head", id: "caps", title: "1 · Концы у кнопок", lede: "Сделано 21.08.2026. Варианты формы — на будущее." },
    {
      kind: "stands",
      items: [
        {
          id: "caps-none",
          status: "note",
          title: "Как было",
          note: "Пластина с фаской и каймой, кромка ничем не занята.",
          verdict: "При равных размерах именно это отличало нашу кнопку от кнопки Heroes.",
          size: [420, 200],
          draw: capsNone
        },
        {
          id: "caps-chevron",
          status: "accepted",
          title: "1А · шеврон остриями внутрь",
          tag: "в игре",
          note: "По шеврону у каждой кромки, острия смотрят на подпись.",
          facts: [["размер", "9 (--gm-plate-cap)"], ["ширина пункта", "380 → 430"]],
          verdict:
            "Наружу шевроны читались бы стрелками «сюда» и обещали переход, которого кнопка не " +
            "делает. Внутрь — обрамляют подпись и сводят взгляд к центру.",
          decision: "2026-08-21",
          size: [420, 200],
          draw: capsChevron
        },
        {
          id: "caps-brackets",
          status: "waiting",
          title: "1Б · скобки-уголки",
          note: "Вместо шеврона — две грани угла у каждой кромки, как уголковая рамка Guildrun.",
          verdict:
            "Строже и спокойнее шеврона, лучше ложится на широкие панели. На кнопке рядом с фаской " +
            "спорит с ней: два угла в одном месте.",
          size: [420, 200],
          draw: capsBrackets
        }
      ]
    },

    { kind: "head", id: "glow", title: "2 · Свечение вместо заливки", lede: "Следующий пункт очереди." },
    {
      kind: "stands",
      items: [
        {
          id: "glow-now",
          status: "note",
          title: "Как сейчас",
          note: "Активная вкладка залита цветом действия, соседние — пустые прямоугольники.",
          verdict: "Заливка держит только один уровень: «этот» против «остальных». Света нет.",
          size: [420, 200],
          draw: litFill
        },
        {
          id: "glow-halo",
          status: "waiting",
          title: "2А · ореол и шеврон",
          note: "Вокруг активного мягкое свечение, под ним маленький шеврон — прямой приём Heroes.",
          verdict:
            "Ближе всего к рефу и читается даже на пёстром фоне. Дороже в реализации: UITK не " +
            "умеет тени вокруг элемента, потребуется рисовать градиент контролом.",
          size: [420, 200],
          draw: litGlow
        },
        {
          id: "glow-underline",
          status: "waiting",
          title: "2Б · черта с растушёвкой",
          note: "Заливки нет вовсе: под активным светящаяся линия, гаснущая к краям.",
          verdict:
            "Дешевле и тише, работает и на вкладках, и на строках списка. Слабее на фоне с " +
            "рисунком — линия теряется там, где ореол ещё виден.",
          size: [420, 200],
          draw: litUnderline
        }
      ]
    },

    { kind: "head", id: "rhythm", title: "3 · Группировка паузами" },
    {
      kind: "split",
      items: [
        {
          id: "rhythm-flat",
          status: "note",
          title: "Как сейчас",
          note: "Пять пунктов с одинаковым шагом: игра и служебное неразличимы.",
          verdict: "Ровный столбик читается списком, а не меню с иерархией.",
          size: [420, 240],
          draw: rhythmFlat
        },
        {
          id: "rhythm-grouped",
          status: "waiting",
          title: "3А · пауза перед служебным",
          note:
            "Три пункта про игру, пауза в 19% высоты кадра, затем «Настройки» и «Выход» — мельче и " +
            "уже. Числа сняты с рефа.",
          facts: [["пауза", "19% высоты"], ["служебные", "уже на 23%"]],
          verdict:
            "Правка одних отступов, а меню сразу перестаёт быть рыхлым. Требует решить, что " +
            "считать служебным: сейчас это «Настройки» и «Выход», но «Профиль» спорный.",
          size: [420, 240],
          draw: rhythmGrouped
        }
      ]
    },

    { kind: "head", id: "sections", title: "4 · Заголовки секций вместо линий" },
    {
      kind: "split",
      items: [
        {
          id: "sections-lines",
          status: "note",
          title: "Как сейчас",
          note: "Строки настроек разделены линиями через равные промежутки.",
          verdict:
            "Линия режет поровну и потому ничего не группирует: соседние по смыслу строки выглядят " +
            "так же далеко, как чужие друг другу.",
          size: [420, 240],
          draw: sectionsLines
        },
        {
          id: "sections-heads",
          status: "waiting",
          title: "4А · заголовок делит, пустота отделяет",
          note: "Секция объявлена крупным заголовком по центру, линий нет вовсе — приём Heroes.",
          verdict:
            "Даёт настройкам структуру и заодно место под будущие строки. Пока их три, секции " +
            "выглядят обещанием — пункт лучше делать вместе с наполнением настроек.",
          size: [420, 240],
          draw: sectionsHeads
        }
      ]
    },

    { kind: "head", id: "corners", title: "5 · Занять углы кадра" },
    {
      kind: "split",
      items: [
        {
          id: "corners-empty",
          status: "note",
          title: "Как сейчас",
          note: "Занят один угол из четырёх — версия внизу слева.",
          verdict: "Пустые углы делают кадр незаконченным даже при плотном центре.",
          size: [420, 240],
          draw: cornersEmpty
        },
        {
          id: "corners-filled",
          status: "waiting",
          title: "5А · справка и сообщество",
          note:
            "Справа вверху — справка и раскладка клавиш, справа внизу — ссылки сообщества, версия " +
            "остаётся слева.",
          verdict:
            "Дёшево и сразу узнаётся: так делают оба рефа. Требует решить, что у нас в углах " +
            "вообще должно быть — Discord у проекта есть, справки пока нет.",
          size: [420, 240],
          draw: cornersFilled
        }
      ]
    },

    { kind: "head", id: "arena", title: "6 · Обрамление арены", lede: "Первый из двух пунктов, ждущих художника." },
    {
      kind: "stands",
      items: [
        {
          id: "arena-bare",
          status: "note",
          title: "Как сейчас",
          note: "Поле боя уходит в край кадра, границы держит только камера.",
          verdict: "Именно это делает фон меню зелёным шумом вместо картины.",
          size: [420, 200],
          draw: arenaBare
        },
        {
          id: "arena-art",
          status: "waiting",
          title: "6А · рамка из предметов",
          note: "По краям кадра камни, листва, руины — приём Guildrun. Поле в световом пятне.",
          facts: [["обрамление", "по 15% с каждой стороны"]],
          verdict: "Сильнее всего меняет ощущение готовой игры. Нужен художник.",
          size: [420, 200],
          draw: arenaArt
        },
        {
          id: "arena-vignette",
          status: "waiting",
          title: "6Б · виньетка светом",
          note: "Без арта: тёмная виньетка по краям и светлое пятно на месте боя.",
          verdict:
            "Делается постобработкой сегодня же и уже собирает кадр. Слабее рамки из предметов: " +
            "тьма по краям читается «экономией», а не миром.",
          size: [420, 200],
          draw: arenaVignette
        }
      ]
    },

    { kind: "head", id: "crest", title: "7 · Знак на экране исхода", lede: "Второй пункт, ждущий художника." },
    {
      kind: "stands",
      items: [
        {
          id: "crest-none",
          status: "note",
          title: "Как сейчас",
          note: "Панель со словом «Победа» и кнопкой.",
          verdict: "Победа и поражение отличаются одним словом.",
          size: [420, 200],
          draw: crestNone
        },
        {
          id: "crest-rays",
          status: "waiting",
          title: "7А · эмблема с лучами",
          note: "Круглый знак и расходящиеся лучи, под ним картуш с заголовком — приём Heroes.",
          verdict:
            "Самый близкий к рефу. Знак может быть гербом гильдии — тогда он не просто украшение, " +
            "а «победила ИМЕННО ТВОЯ гильдия».",
          size: [420, 200],
          draw: crestRays
        },
        {
          id: "crest-stamp",
          status: "waiting",
          title: "7Б · печать поверх",
          note: "Знак ложится оттиском поверх картуша, слегка под углом.",
          verdict:
            "Дешевле лучей и лучше ложится на нашу тему гроссбуха: печать в документе. Слабее " +
            "работает на поражении — оттиск читается наградой.",
          size: [420, 200],
          draw: crestStamp
        }
      ]
    },

    {
      kind: "note",
      html:
        "<b>Как читать статусы.</b> «в игре» — сделано и живёт в билде. «ждёт» — нарисовано, вердикта " +
        "нет. «ситуация» — как оно выглядит сегодня, для сравнения."
    },

    { kind: "head", id: "menu", title: "Экран I · главное меню", lede: "Реф держится не на кнопках, а на том, что за ними." },
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
          title: "I-А · правую половину занимает гильдия",
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
          title: "I-Б · продолжить забег крупной карточкой",
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
          title: "I-В · только ритм и концы",
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

    { kind: "head", id: "settings", title: "Экран II · настройки", lede: "У рефа плотный экран без единой линии-разделителя." },
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
          title: "II-А · секции заголовками, две колонки",
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
          title: "II-Б · одна колонка по центру",
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

    { kind: "head", id: "outcome", title: "Экран III · исход забега", lede: "У момента триумфа должен быть знак, а не строка." },
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
          title: "III-А · знак и картуш",
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
          title: "III-Б · знак плюс итоги забега",
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

    { kind: "head", id: "loadout", title: "Экран IV · лоадаут и витрина", lede: "У Guildrun витрина живёт без панели вовсе." },
    {
      kind: "live",
      id: "loadout-shots",
      render: shots({
        ref: "ref-loadout.jpg",
        refCaption: "Guildrun: сетка на фоне, заголовок с прогрессом, поиск и фильтр в полосе табов",
        ours: "ours-loadout.jpg",
        oursCaption: "наш лоадаут: сетка внутри панели, описание справа"
      })
    },
    {
      kind: "stands",
      items: [
        {
          id: "loadout-now",
          status: "note",
          title: "Как сейчас",
          note: "Панель занимает правые 56% кадра, левая треть пуста, статы строками.",
          verdict: "Читается прилично, особенно после холодной гаммы. Но кадр занят наполовину.",
          size: [480, 270],
          draw: loadoutNow
        },
        {
          id: "loadout-showcase",
          status: "waiting",
          title: "IV-А · витрина без панели",
          note:
            "Сетка ложится прямо на фон, сверху одна полоса: «Назад», табы, поиск, фильтр. Заголовок " +
            "секции несёт прогресс («собрано 13 из 75») и строку-объяснение, откуда берутся новые.",
          facts: [["сетка", "9 в ряд"], ["карточка", "9.5% x 19%"]],
          verdict:
            "Занимает весь кадр и даёт место прогрессу — тому, ради чего игрок сюда и заходит. " +
            "Требует, чтобы описание выбранного переехало в тултип или на второй экран.",
          size: [480, 270],
          draw: loadoutShowcase
        }
      ]
    },

    { kind: "head", id: "hud", title: "Экран V · боевой HUD", lede: "Границу поля у Guildrun держит арт, а не край экрана." },
    {
      kind: "live",
      id: "hud-shots",
      render: shots({
        ref: "ref-hud.jpg",
        refCaption: "Guildrun: поле обрамлено артом, панели со скошенными торцами, карточка выбранного справа внизу",
        ours: "ours-menu.jpg",
        oursCaption: "наша арена (кадр из фона меню): трава до края кадра, обрамления нет"
      })
    },
    {
      kind: "stands",
      items: [
        {
          id: "hud-now",
          status: "note",
          title: "Как сейчас",
          note: "Полоски HP над юнитами, топбар сверху. Углы кадра пусты, поле уходит в край.",
          verdict: "Бой читается, но кадр не выглядит собранным: нет ни рамки, ни фокуса.",
          size: [480, 270],
          draw: hudNow
        },
        {
          id: "hud-framed",
          status: "waiting",
          title: "V-А · обрамление и карточка выбранного",
          note:
            "По краям кадра арт (камни, деревья), поле боя в световом пятне посередине. Верхняя " +
            "полоса со скошенными торцами, справа внизу карточка выбранного юнита.",
          facts: [["обрамление", "по 16% с каждой стороны"], ["карточка", "22% x 34%"]],
          verdict:
            "Сильнее всего меняет ощущение «игра, а не прототип». Самая дорогая позиция списка: " +
            "нужен арт обрамления, без него останется тёмной виньеткой.",
          size: [480, 270],
          draw: hudFramed
        }
      ]
    },

    {
      kind: "note",
      html:
        "Первые пять делаются темой и раскладкой, без художника. Последние два ждут арта — и это " +
        "честная граница: их «ПОСЛЕ» останется макетом, пока арта нет.<br><br>" +
        "<b>Как названы карточки ниже.</b> Пункты плана — цифрой и буквой варианта (<code>2А</code> " +
        "— ореол, <code>2Б</code> — черта). Раскладки целых экранов — римской цифрой " +
        "(<code>I-Б</code> — меню с карточкой «продолжить забег»). Так вердикт можно сказать одним " +
        "словом: «беру 2А и I-Б»."
    }
  ]
};

export default section;
