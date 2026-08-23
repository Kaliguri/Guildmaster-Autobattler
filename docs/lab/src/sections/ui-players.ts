/* Список игроков: кто с нами и как у него дела.

   Разбор 20.08.2026. Список уже существует в игре — `ParticipantsPanelView`, класс
   `gm-participants`, левый верхний угол под верхней панелью, две группы (свои и противники в PvP).
   Сегодня в строке живут три вещи: точка мейн-цвета, ник и пометка «вы».

   Разбор не про то, где ему быть — место выбрано 03.08.2026 и подтверждено, — а про мелочи, ради
   которых список и заводят: связь, роль хозяина, где человек сейчас, готов ли он. */

import * as w from "../lib/ui-wire.js";
import type { SectionDef } from "../types.js";

/** Строка игрока в принятом варианте: цвет, ник, признаки, пинг числом справа. */
function playerRow(
  ctx: CanvasRenderingContext2D,
  y: number,
  width: number,
  height: number,
  opts: { name: string; host?: boolean; self?: boolean; ping: string; where?: string }
): void {
  w.box(ctx, { x: 0.045, y, w: 0.022, h: 0.038 }, width, height, { lit: true });

  w.text(ctx, opts.name + (opts.self ? "  (вы)" : ""), { x: 0.078, y: y + 0.019 }, width, height,
    { size: 10.5, color: opts.self ? "#E8E8EC" : "#8A8A93" });

  if (opts.host) {
    w.text(ctx, "хозяин", { x: 0.078 + 0.085, y: y + 0.019 }, width, height,
      { size: 8.5, color: "#C8A24C" });
  }

  if (opts.where) {
    w.text(ctx, opts.where, { x: 0.078, y: y + 0.052 }, width, height,
      { size: 8.5, color: "#5F5F68" });
  }

  w.text(ctx, opts.ping, { x: 0.245, y: y + 0.019 }, width, height,
    { size: 9.5, align: "right", color: "#8A8A93" });
}

/** Принято: строка с числом пинга. */
function drawRows(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  const panel: w.Rect = { x: 0.03, y: 0.07, w: 0.23, h: 0.30 };
  w.box(ctx, panel, width, height, { hollow: true, dashed: true });

  playerRow(ctx, 0.10, width, height,
    { name: "Максим", host: true, self: true, ping: "—", where: "во дворе" });
  playerRow(ctx, 0.19, width, height,
    { name: "Гилберт", ping: "48 ms", where: "в лавке" });
  playerRow(ctx, 0.28, width, height,
    { name: "Тея", ping: "212 ms", where: "выбирает" });

  w.callout(ctx, { x: 0.26, y: 0.209 }, { x: 0.42, y: 0.209 },
    "пинг числом всегда (вердикт 20.08)", width, height);
  w.callout(ctx, { x: 0.26, y: 0.299 }, { x: 0.42, y: 0.299 },
    "плохая связь красится, а не прячется", width, height);
  w.measure(ctx, panel, "23% x 30%", width, height);
}

/** Отклонено: карточка с портретом. */
function drawCards(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  const people: Array<[string, string]> = [
    ["Максим", "хозяин · —"],
    ["Гилберт", "48 ms"],
    ["Тея", "212 ms"]
  ];

  people.forEach(([name, meta], i) => {
    const y = 0.07 + i * 0.135;
    w.box(ctx, { x: 0.03, y, w: 0.23, h: 0.115 }, width, height, {});
    w.box(ctx, { x: 0.042, y: y + 0.018, w: 0.045, h: 0.08 }, width, height,
      { label: "портрет", size: 7.5 });
    w.text(ctx, name, { x: 0.10, y: y + 0.042 }, width, height, { size: 10, color: "#8A8A93" });
    w.text(ctx, meta, { x: 0.10, y: y + 0.078 }, width, height, { size: 8.5, color: "#5F5F68" });
  });
}

/** Отклонено: компактный ряд поперёк верха. */
function drawStrip(ctx: CanvasRenderingContext2D, width: number, height: number): void {
  w.screen(ctx, width, height);
  w.worldBehind(ctx, width, height);

  ["Максим", "Гилберт", "Тея"].forEach((name, i) => {
    w.box(ctx, { x: 0.03 + i * 0.135, y: 0.05, w: 0.125, h: 0.065 }, width, height,
      { label: name, size: 9 });
  });
  w.callout(ctx, { x: 0.30, y: 0.082 }, { x: 0.46, y: 0.082 },
    "на четвёртом игроке дотянется до центра", width, height);
}

export const section: SectionDef = {
  id: "ui-players",
  title: "Список игроков",
  eyebrow: "Лаборатория Guildmaster · Интерфейс",
  lede:
    "Кто с нами, у кого связь плохая, кто хозяин и куда человек ушёл. Панель живёт в игре с " +
    "03.08.2026; разбор — про мелочи строки, а не про место.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "what",
      title: "Что уже есть и чего не хватает",
      lede:
        "В игре: точка мейн-цвета, ник, пометка «вы», две группы (свои и противники). В одиночной " +
        "игре панель скрыта целиком — список из одного себя не сообщает ничего."
    },
    {
      kind: "table",
      head: ["Поле строки", "Решение", "Почему так"],
      rows: [
        ["Мейн-цвет", "точка слева — есть", "тем же цветом красится курсор игрока и его метки"],
        ["Ник", "есть", "берётся из Steam либо задан в профиле"],
        ["Пинг", "числом всегда (Макс, 20.08)", "«48 ms» честнее рисок: у рисок нет порога, о котором договорились"],
        ["Хозяин", "подписью, не короной", "корона у нас нигде не язык; подпись читается и в локали, и слепым к цвету"],
        ["Где он сейчас", "второй строкой помельче", "присутствие уже ездит по проводу — «в лавке», «на карте», «выбирает»"],
        ["Готовность", "открыто", "гейт готовности показывает «(N/M)» на самой кнопке — дублировать или нет, не решено"],
        ["Портрет", "нет", "портрет — про отряд, а не про человека за клавиатурой"]
      ]
    },
    {
      kind: "head",
      id: "layouts",
      title: "Три раскладки",
      lede: "Выбран вариант А: строка. Он же продолжает то, что уже стоит в игре."
    },
    {
      kind: "stands",
      items: [
        {
          id: "rows",
          status: "accepted",
          title: "А · Строка с числом",
          decision: "2026-08-20",
          tag: "продолжает нынешний вид",
          note:
            "Точка цвета, ник, подпись роли, пинг справа числом. Под ником — вторая строка помельче " +
            "«где он», когда человек ушёл с общего экрана.",
          facts: [
            ["панель", "23% x 30%"],
            ["строка", "три поля + пинг"],
            ["своя строка", "светлее прочих"],
            ["в одиночке", "скрыта целиком"]
          ],
          verdict:
            "Растёт до восьми человек без перекройки и не занимает угол больше, чем нужно. Цена — " +
            "числа пинга шумят в коопе на двоих, где и так всё видно.",
          size: [520, 293],
          draw: drawRows
        },
        {
          id: "cards",
          status: "rejected",
          title: "Б · Карточки с портретом",
          note: "Каждый игрок — карточка с портретом своего Сосуда и двумя строками под ним.",
          verdict:
            "Портрет отвечает на вопрос «кем он играет», а список отвечает на «кто с нами». " +
            "Смешение стоит втрое больше места и врёт при смене Сосуда посреди забега.",
          size: [480, 270],
          draw: drawCards
        },
        {
          id: "strip",
          status: "rejected",
          title: "В · Ряд поперёк верха",
          note: "Компактные плашки в строку под верхней панелью.",
          verdict:
            "На двоих смотрится опрятно, но на четвёртом игроке ряд дотягивается до центра кадра и " +
            "начинает спорить с верхней панелью. Раскладка, которая ломается от роста состава, — " +
            "не раскладка.",
          size: [480, 270],
          draw: drawStrip
        }
      ]
    },
    {
      kind: "note",
      html:
        "<b>Открыто:</b> порог, с которого пинг красится тревожным · дублировать ли готовность в " +
        "строке, если она уже видна на кнопке «(N/M)» · показывать ли в PvP пинг противника (это " +
        "информация о сопернике, а не только о связи)."
    }
  ]
};

export default section;
