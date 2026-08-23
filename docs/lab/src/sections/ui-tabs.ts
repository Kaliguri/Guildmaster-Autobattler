/* Лента разделов: как помечен активный таб.

   Раздел цветной, как и «Фон экранов меты», и по той же причине: раскладка ленты решена (сверху,
   по центру), а спорный вопрос — ЧЕМ отличается выбранный раздел. Это вопрос вида, и серый чертёж
   на него не отвечает.

   Числа рефа сняты пипеткой с `Art_Dev/UI Refs/Guildrun Settings.jpg` 05.08.2026 и приведены в
   таблице раздела. Табы у нас живут не только в настройках — те же классы носят лоадаут и
   реликвии, поэтому вердикт здесь касается всей игры. */

import type { SectionDef, DrawFn } from "../types.js";

const PATINA = {
  p950: "rgb(1, 12, 14)",
  p900: "rgb(6, 65, 71)",
  p800: "rgb(18, 62, 66)",
  p700: "rgb(24, 86, 92)",
  p600: "rgb(34, 121, 129)",
  p500: "rgb(49, 174, 185)",
  p400: "rgb(70, 194, 206)"
} as const;

const TEXT = "rgb(233, 226, 212)";
const TEXT_DIM = "rgba(233, 226, 212, 0.55)";
const BRASS = "rgb(198, 160, 84)";

const LABELS = ["ИГРА", "ГРАФИКА", "ЗВУК"];
const ACTIVE = 1;

/** Фон стенда — тот же задник меты, чтобы вариант оценивался на своём месте, а не на сером поле. */
function backdrop(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  const g = ctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, "rgb(12, 52, 56)");
  g.addColorStop(0.55, "rgb(10, 44, 48)");
  g.addColorStop(1, "rgb(5, 24, 27)");
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
  // диагональ света — намёк, чтобы фон не читался плоской заливкой
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const s = ctx.createLinearGradient(w * 0.2, 0, w, h * 0.8);
  s.addColorStop(0, "rgba(49, 174, 185, 0)");
  s.addColorStop(0.5, "rgba(49, 174, 185, 0.05)");
  s.addColorStop(1, "rgba(49, 174, 185, 0)");
  ctx.fillStyle = s;
  ctx.fillRect(0, 0, w, h);
  ctx.restore();
}

interface TabBox {
  x: number;
  w: number;
  label: string;
  active: boolean;
}

/** Раскладка ленты: три таба по центру, ширина по подписи с полями. Общая у всех вариантов —
 *  сравнивать надо ПОМЕТКУ активного, а не разные раскладки. */
function layout(ctx: CanvasRenderingContext2D, w: number, fontPx: number): TabBox[] {
  ctx.font = `${fontPx}px system-ui, sans-serif`;
  const pad = fontPx * 1.6;
  const gap = fontPx * 0.8;
  const total = LABELS.reduce((a, l) => a + ctx.measureText(l).width + pad * 2, 0) + gap * (LABELS.length - 1);
  let x = (w - total) / 2;
  const boxes: TabBox[] = [];
  for (let i = 0; i < LABELS.length; i++) {
    const text = LABELS[i] ?? "";
    const width = ctx.measureText(text).width + pad * 2;
    boxes.push({ x, w: width, label: text, active: i === ACTIVE });
    x += width + gap;
  }
  return boxes;
}

function label(ctx: CanvasRenderingContext2D, t: TabBox, cy: number, fontPx: number, color: string): void {
  ctx.fillStyle = color;
  ctx.font = `${fontPx}px system-ui, sans-serif`;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.fillText(t.label, t.x + t.w / 2, cy);
}

/** Содержимое под лентой — две строки настроек. Без них лента висит в воздухе, и не видно, ЧТО
 *  именно она возглавляет: у рефа рельса как раз и отделяет ленту от списка. */
function rows(ctx: CanvasRenderingContext2D, w: number, h: number, top: number): void {
  const fs = h * 0.055;
  for (let i = 0; i < 2; i++) {
    const y = top + i * h * 0.13;
    ctx.fillStyle = TEXT_DIM;
    ctx.font = `${fs}px system-ui, sans-serif`;
    ctx.textAlign = "left";
    ctx.textBaseline = "middle";
    ctx.fillText(i === 0 ? "Общий" : "Музыка", w * 0.22, y);
    ctx.strokeStyle = "rgba(233, 226, 212, 0.35)";
    ctx.lineWidth = Math.max(1, h * 0.008);
    ctx.beginPath();
    ctx.moveTo(w * 0.52, y);
    ctx.lineTo(w * 0.74, y);
    ctx.stroke();
    ctx.fillStyle = BRASS;
    ctx.fillRect(w * 0.6, y - h * 0.018, w * 0.012, h * 0.036);
  }
}

/** Общая рельса под лентой: 2px у рефа при 720 высоты, поля по 2% ширины. */
function rail(ctx: CanvasRenderingContext2D, w: number, h: number, y: number, color: string, gap?: [number, number]): void {
  const t = Math.max(1, h * 0.008);
  ctx.fillStyle = color;
  if (!gap) {
    ctx.fillRect(w * 0.02, y, w * 0.96, t);
    return;
  }
  ctx.fillRect(w * 0.02, y, gap[0] - w * 0.02, t);
  ctx.fillRect(gap[1], y, w * 0.98 - gap[1], t);
}

/** Вертикальный градиент активной плашки: у рефа она СВЕТЛЕЕТ КНИЗУ (72 вверху, 117 внизу) —
 *  то же направление, что у наших кнопок: свет падает снизу. */
function activeFill(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number): CanvasGradient {
  const g = ctx.createLinearGradient(0, y, 0, y + h);
  g.addColorStop(0, PATINA.p800);
  g.addColorStop(1, PATINA.p600);
  return g;
}

const fontOf = (h: number) => h * 0.062;

// --- Реф, собранный из замеров ---

const drawRef: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.06;
  const tabH = h * 0.16;
  const railY = top + tabH + h * 0.02;

  for (const t of tabs) {
    if (t.active) {
      ctx.fillStyle = activeFill(ctx, t.x, top, t.w, tabH);
      ctx.fillRect(t.x, top, t.w, tabH);
      // светлая кромка по бокам — в замере края плашки ярче её заливки (121-128 против 105-114)
      ctx.fillStyle = "rgba(180, 220, 205, 0.5)";
      ctx.fillRect(t.x, top, 1.5, tabH);
      ctx.fillRect(t.x + t.w - 1.5, top, 1.5, tabH);
    }
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
  }
  rail(ctx, w, h, railY, "rgba(150, 175, 168, 0.75)");
  rows(ctx, w, h, railY + h * 0.16);
};

// --- Варианты ---

const drawA: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.06;
  const tabH = h * 0.16;
  const railY = top + tabH;

  for (const t of tabs) {
    if (t.active) {
      ctx.fillStyle = activeFill(ctx, t.x, top, t.w, tabH);
      ctx.fillRect(t.x, top, t.w, tabH);
    }
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
  }
  rail(ctx, w, h, railY, "rgba(150, 175, 168, 0.6)");
  rows(ctx, w, h, railY + h * 0.16);
};

const drawB: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.06;
  const tabH = h * 0.16;
  const railY = top + tabH;

  for (const t of tabs) {
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
    if (t.active) {
      ctx.fillStyle = PATINA.p500;
      ctx.fillRect(t.x + t.w * 0.12, railY - h * 0.012, t.w * 0.76, h * 0.014);
    }
  }
  rail(ctx, w, h, railY, "rgba(150, 175, 168, 0.45)");
  rows(ctx, w, h, railY + h * 0.16);
};

const drawC: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.09;
  const tabH = h * 0.16;

  for (const t of tabs) {
    ctx.fillStyle = t.active ? activeFill(ctx, t.x, top, t.w, tabH) : "rgba(6, 24, 27, 0.55)";
    ctx.fillRect(t.x, top, t.w, tabH);
    ctx.strokeStyle = t.active ? PATINA.p400 : "rgba(150, 175, 168, 0.3)";
    ctx.lineWidth = t.active ? 2 : 1;
    ctx.strokeRect(t.x + 0.5, top + 0.5, t.w - 1, tabH - 1);
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
  }
  rows(ctx, w, h, top + tabH + h * 0.18);
};

const drawD: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.06;
  const tabH = h * 0.16;
  const railY = top + tabH;
  const act = tabs[ACTIVE] ?? tabs[0]!;

  for (const t of tabs) {
    if (t.active) {
      // плашка цвета содержимого: вкладка «вырезает» рельсу и оказывается заодно с тем, что под ней
      ctx.fillStyle = "rgba(9, 40, 44, 0.9)";
      ctx.fillRect(t.x, top, t.w, tabH);
      ctx.strokeStyle = "rgba(150, 175, 168, 0.6)";
      ctx.lineWidth = Math.max(1, h * 0.008);
      ctx.beginPath();
      ctx.moveTo(t.x, railY);
      ctx.lineTo(t.x, top);
      ctx.lineTo(t.x + t.w, top);
      ctx.lineTo(t.x + t.w, railY);
      ctx.stroke();
    }
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
  }
  rail(ctx, w, h, railY, "rgba(150, 175, 168, 0.6)", [act.x, act.x + act.w]);
  rows(ctx, w, h, railY + h * 0.16);
};

const drawE: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.09;
  const tabH = h * 0.16;
  const r = tabH / 2;

  for (const t of tabs) {
    if (t.active) {
      ctx.fillStyle = activeFill(ctx, t.x, top, t.w, tabH);
      ctx.beginPath();
      ctx.moveTo(t.x + r, top);
      ctx.lineTo(t.x + t.w - r, top);
      ctx.arc(t.x + t.w - r, top + r, r, -Math.PI / 2, Math.PI / 2);
      ctx.lineTo(t.x + r, top + tabH);
      ctx.arc(t.x + r, top + r, r, Math.PI / 2, -Math.PI / 2);
      ctx.closePath();
      ctx.fill();
    }
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : TEXT_DIM);
  }
  rows(ctx, w, h, top + tabH + h * 0.18);
};

const drawNow: DrawFn = (ctx, w, h) => {
  backdrop(ctx, w, h);
  const fs = fontOf(h);
  const tabs = layout(ctx, w, fs);
  const top = h * 0.09;
  const tabH = h * 0.16;

  for (const t of tabs) {
    // как в игре сегодня: дерево гроссбуха посреди меты
    ctx.fillStyle = "rgb(63, 54, 44)";
    ctx.fillRect(t.x, top, t.w, tabH);
    ctx.strokeStyle = t.active ? BRASS : "rgba(176, 159, 141, 0.6)";
    ctx.lineWidth = 1;
    ctx.strokeRect(t.x + 0.5, top + 0.5, t.w - 1, tabH - 1);
    label(ctx, t, top + tabH / 2, fs, t.active ? TEXT : "rgba(233, 226, 212, 0.5)");
  }
  rows(ctx, w, h, top + tabH + h * 0.18);
};

const section: SectionDef = {
  id: "ui-tabs",
  title: "Лента разделов",
  eyebrow: "Интерфейс",
  transport: false,
  lede:
    "Чем помечен выбранный раздел. Вопрос общий для игры: те же классы носят настройки, сбор отряда " +
    "и реликвии, так что вердикт здесь ставится один на всех.",
  blocks: [
    {
      kind: "head",
      id: "ref",
      title: "Что у рефа",
      lede:
        "Промерено пипеткой: <code>Guildrun Settings.jpg</code>, 05.08.2026. Доли даны от кадра " +
        "1280×720, чтобы переносились в наши 1920×1080 без пересчёта."
    },
    {
      kind: "table",
      head: ["Что мерили", "Значение", "Что из этого следует"],
      rows: [
        ["Неактивный таб", "плашки нет вовсе, только текст", "лента не выглядит рядом кнопок"],
        ["Активный таб", "залит, 163×41px = 12.7%×5.7%", "плашка ШИРЕ своего слова, поля щедрые"],
        ["Заливка активного", "яркость 72 сверху → 117 снизу", "градиент СВЕТЛЕЕТ КНИЗУ — как у наших кнопок"],
        ["Кромка активного", "121-128 против заливки 105-114", "по бокам светлая грань в пиксель"],
        ["Рельса под лентой", "2px, x от 1.9% до 98% ширины", "тянется почти во всю ширину, а не под табами"],
        ["Цвет рельсы", "#5B706B, H=166, S=0.10", "тот же тон, что фон, но обесцвеченный и светлее"],
        ["Стык плашки и рельсы", "низ плашки 8.7%, рельса 9.9%", "плашка СТОИТ на рельсе, как ярлык папки"],
        ["Служебный пункт", "отделён вертикальной чертой", "«сообщить об ошибке» — не раздел настроек"]
      ]
    },
    {
      kind: "note",
      html:
        "<b>Приём, которого у нас нет:</b> рельса. Она превращает ленту в край листа, а активный таб — " +
        "в выступающий из него ярлык, и заодно отделяет заголовок экрана от содержимого. Без неё " +
        "три плашки висят в пустоте и читаются как ряд кнопок — ровно то, что у нас сейчас."
    },
    {
      kind: "split",
      items: [
        {
          id: "tabs-ref-rebuild",
          status: "note",
          title: "Реф, собранный из замеров",
          tag: "эталон",
          size: [480, 270],
          note: "Не скриншот: та же лента, построенная по числам таблицы.",
          facts: [
            ["рельса", "2px, 96% ширины"],
            ["активный", "залит, светлеет книзу"],
            ["неактивный", "только текст"]
          ],
          draw: drawRef
        },
        {
          id: "now",
          status: "note",
          title: "Что в игре сегодня",
          tag: "как есть",
          size: [480, 270],
          note:
            "Три плашки дерева с каймой, активный отличается латунной рамкой. Регистр гроссбуха " +
            "посреди меты плюс отсутствие рельсы.",
          facts: [
            ["рельса", "нет"],
            ["активный", "только цвет каймы"],
            ["поверхность", "дерево (чужой регистр)"]
          ],
          verdict: "Читается рядом кнопок, а не разделами одного экрана.",
          draw: drawNow
        }
      ]
    },
    {
      kind: "head",
      id: "variants",
      title: "Пять вариантов",
      lede:
        "Раскладка ленты у всех одна — сравнивается ровно то, чем помечен активный раздел. Под лентой " +
        "нарисованы две строки настроек: без них не видно, что именно лента возглавляет."
    },
    {
      kind: "stands",
      items: [
        {
          id: "tabs-var-a",
          status: "waiting",
          title: "A · Вкладка на рельсе",
          tag: "перенос рефа",
          size: [360, 203],
          note: "Прямой перенос: рельса во всю ширину, активный залит патиной и примыкает к ней снизу.",
          facts: [
            ["новых сущностей", "рельса"],
            ["неактивный", "только текст"],
            ["ближе всего к", "рефу"]
          ],
          verdict: "Проверенный приём и структура экрана заодно. Требует завести рельсу как элемент.",
          draw: drawA
        },
        {
          id: "tabs-var-b",
          status: "waiting",
          title: "B · Подчёркивание",
          tag: "скромный",
          size: [360, 203],
          note: "Рельса та же, но активный помечен толстой чертой патины под словом, без заливки.",
          facts: [
            ["новых сущностей", "рельса + черта"],
            ["краски на экране", "минимум"],
            ["риск", "метка мелкая"]
          ],
          verdict: "Тише всех и не спорит с фоном. На расстоянии дивана метка может потеряться.",
          draw: drawB
        },
        {
          id: "tabs-var-c",
          status: "waiting",
          title: "C · Плашки без рельсы",
          tag: "минимальная правка",
          size: [360, 203],
          note:
            "Сегодняшняя лента, переведённая в мету: тёмное стекло вместо дерева, активный залит " +
            "патиной и обведён.",
          facts: [
            ["новых сущностей", "нет"],
            ["правка", "две строки USS"],
            ["рельса", "нет"]
          ],
          verdict: "Дешевле всех и лечит регистр, но лента остаётся рядом кнопок.",
          draw: drawC
        },
        {
          id: "tabs-var-d",
          status: "waiting",
          title: "D · Вырез в рельсе",
          tag: "ярлык папки",
          size: [360, 203],
          note:
            "Активная вкладка того же цвета, что содержимое, и рельса под ней ПРЕРЫВАЕТСЯ — буквальная " +
            "папка. Метафора честнее, чем у рефа.",
          facts: [
            ["новых сущностей", "рельса с разрывом"],
            ["активный", "цветом не выделен"],
            ["сложность", "разрыв считается по табу"]
          ],
          verdict: "Самый «бумажный» вариант. Активный держится формой, а не цветом — это и сила, и риск.",
          draw: drawD
        },
        {
          id: "var-e",
          status: "waiting",
          title: "E · Капсула",
          tag: "современный",
          size: [360, 203],
          note: "Активный — залитая капсула со скруглением в полвысоты, рельсы нет.",
          facts: [
            ["новых сущностей", "нет"],
            ["радиус", "половина высоты"],
            ["язык", "чужой"]
          ],
          verdict:
            "Выпадает из языка: у нас везде фаска и прямой угол, круглых форм в интерфейсе нет ни одной.",
          draw: drawE
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Рекомендация: A.</b> Рельса — то единственное, чего у нашей ленты нет по существу, а не " +
        "по цвету: она отделяет ленту от списка и даёт активной вкладке на что опереться. " +
        "<b>B</b> хорош, если Максу покажется, что заливки на экране стало много — он снимается " +
        "одной строкой поверх A. <b>C</b> — запасной ход на случай «не сейчас»: чинит регистр за две " +
        "строки, но оставляет ленту рядом кнопок. <b>D</b> красив и честен по метафоре, однако " +
        "активный там не отличается цветом вовсе — на беглый взгляд выбранным читается любой. " +
        "<b>E</b> отвергаю: круглых форм у нас нет нигде."
    }
  ]
};

export default section;
