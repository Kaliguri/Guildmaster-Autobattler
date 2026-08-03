/* Акт целиком: НАСТОЯЩАЯ карта из настоящего генератора.

   Все прочие стенды карты рисуют замысел — схемы форм, варианты заливки, наброски подачи. Этот
   один рисует то, что игрок увидит: узлы, рёбра и пропорции приходят из ./scripts/map-dump.ps1,
   который гоняет MapGenerator мимо Unity и читает числа из ActConfig.asset и MapStyle.asset.

   Зачем отдельно: по схеме нельзя судить о композиции. «Мало воздуха», «имя не встаёт», «веер
   режет территорию на ленты» — всё это видно только в честных пропорциях и только на разных
   сидах. Схема же всегда красива, потому что её рисуют под вывод.

   Что здесь ЧЕСТНО, а что приблизительно, сказано на странице прямым текстом. Врать про источник
   картинки нельзя: по ней принимаются решения. */

import { fetchActMaps, drawFeedState, type ActMap, type ActMapDump, type ActProfile } from "../api.js";
import { COL } from "../draw.js";
import { el } from "../dom.js";
import * as stage from "../stage.js";
import type { SectionDef, StandDef } from "../types.js";

/* ---------- состояние разбора ---------- */

const feed = fetchActMaps();

const view = {
  index: 0,
  /** Индекс профиля в дампе. 0 — `asset`, то есть игра как она есть. */
  profile: 0,
  /** Ползунки: живут поверх профиля и сбрасываются при его смене. */
  stepX: 6.5,
  stepY: 4.2,
  /** Показ: знаки типов на узлах, номера этажей, лист под картой. */
  marks: true,
  floors: false,
  sheet: true
};

function current(dump: ActMapDump): ActProfile | undefined {
  return dump.profiles[view.profile];
}

/* ---------- как выглядит узел ----------
   Цвет и знак — не из игры: там узлы носят спрайты, а палитра приходит из темы по роли. Здесь
   нужна только различимость типов, чтобы читалась РАСКЛАДКА типов по акту. */

const LOOK: Record<string, { color: string; mark: string; title: string }> = {
  Start:     { color: COL.white, mark: "●", title: "старт" },
  Battle:    { color: COL.brass, mark: "×", title: "бой" },
  Elite:     { color: "#E2725B", mark: "✦", title: "элита" },
  TextEvent: { color: COL.muted, mark: "…", title: "текстовый ивент" },
  Shop:      { color: COL.honey, mark: "$", title: "лавка" },
  Boss:      { color: "#FF6B5A", mark: "★", title: "босс" },
  Chest:     { color: COL.honey, mark: "▣", title: "сундук" },
  Unknown:   { color: COL.muted, mark: "?", title: "неизвестность" },
  Camp:      { color: COL.mint,  mark: "▲", title: "привал" }
};

function look(dump: ActMapDump, type: number) {
  return LOOK[dump.nodeTypes[type] ?? ""] ?? { color: COL.muted, mark: "·", title: "?" };
}

/* ---------- раскладка ----------
   Формула та же, что в MapLayout.Resolve: этаж по X, ряд центрируется по фактической ширине этажа.
   Здесь она живёт КОПИЕЙ намеренно и временно: раскладка сейчас и есть предмет разбора — её
   переделывают, чтобы дать карте воздух. Как только шаги устоятся и уедут в MapLayout, копию надо
   убрать, а позиции считать в дампере. Пока правило простое: топология приходит из игры, раскладка
   крутится здесь. */

interface Placed {
  x: number;
  y: number;
  type: number;
}

function place(map: ActMap): Placed[] {
  const widthOf = new Map<number, number>();
  for (const [floor, row] of map.nodes) {
    widthOf.set(floor, Math.max(widthOf.get(floor) ?? 0, row + 1));
  }
  return map.nodes.map(([floor, row, type]) => {
    const width = widthOf.get(floor) ?? 1;
    return {
      x: floor * view.stepX,
      y: (row - (width - 1) * 0.5) * view.stepY,
      type
    };
  });
}

/* ---------- отрисовка ---------- */

function drawMap(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  if (drawFeedState(ctx, w, h, feed, "дамп карт")) return;
  const dump = feed.data;
  if (!dump) return;

  const profile = current(dump);
  if (!profile) return;
  const map = profile.maps[view.index % profile.maps.length];
  if (!map) return;

  // Радиус узла — из префаба через дамп: от него зависит, тесно ли выглядит сетка.
  const nodeR = profile.style["nodeRadius"] ?? 0.6;

  const nodes = place(map);
  const minX = Math.min(...nodes.map((n) => n.x));
  const maxX = Math.max(...nodes.map((n) => n.x));
  const minY = Math.min(...nodes.map((n) => n.y));
  const maxY = Math.max(...nodes.map((n) => n.y));

  // Лист обтягивает граф с полями из MapStyle: по ширине и высоте они РАЗНЫЕ, сверху и снизу
  // нужно место под подписи. Узел ещё и торчит своим радиусом за крайнюю точку.
  const padX = (profile.style["sheetPadX"] ?? 1.08) - 1;
  const padY = (profile.style["sheetPadY"] ?? 1.35) - 1;
  const graphW = maxX - minX + nodeR * 2;
  const graphH = maxY - minY + nodeR * 2;
  const sheetW = graphW * (1 + padX);
  const sheetH = graphH * (1 + padY);

  const margin = 14;
  const k = Math.min((w - margin * 2) / sheetW, (h - margin * 2) / sheetH);
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;
  const sx = (x: number) => w / 2 + (x - cx) * k;
  const sy = (y: number) => h / 2 + (y - cy) * k;

  if (view.sheet) {
    ctx.fillStyle = "rgba(232,220,196,.06)";
    ctx.strokeStyle = "rgba(184,134,59,.35)";
    ctx.lineWidth = 1;
    const x0 = sx(cx) - (sheetW * k) / 2;
    const y0 = sy(cy) - (sheetH * k) / 2;
    ctx.fillRect(x0, y0, sheetW * k, sheetH * k);
    ctx.strokeRect(x0 + 0.5, y0 + 0.5, sheetW * k - 1, sheetH * k - 1);
  }

  if (view.floors) {
    ctx.strokeStyle = "rgba(147,128,94,.16)";
    ctx.lineWidth = 1;
    ctx.font = "500 9px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.5)";
    ctx.textAlign = "center";
    const floors = new Set(map.nodes.map((n) => n[0]));
    for (const floor of floors) {
      const x = sx(floor * view.stepX);
      ctx.beginPath();
      ctx.moveTo(x, sy(minY) - 18);
      ctx.lineTo(x, sy(maxY) + 18);
      ctx.stroke();
      ctx.fillText(String(floor), x, sy(maxY) + 30);
    }
  }

  // Дорожки — точками, как в игре: это не украшение, а плотность, от которой зависит, останется
  // ли между путями место под имя зоны. Шаг и зазор от края узла берутся из MapStyle.
  const spacing = profile.style["dotSpacing"] ?? 0.32;
  const clearance = (profile.style["dotClearance"] ?? 1.25) * nodeR;
  const dotR = Math.max(0.9, (profile.style["dotRadius"] ?? 0.07) * k);
  ctx.fillStyle = "rgba(184,134,59,.55)";
  for (const [from, to] of map.edges) {
    const a = nodes[from];
    const b = nodes[to];
    if (!a || !b) continue;
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const len = Math.hypot(dx, dy);
    if (len <= clearance * 2) continue;
    const ux = dx / len;
    const uy = dy / len;
    for (let t = clearance; t <= len - clearance; t += spacing) {
      ctx.beginPath();
      ctx.arc(sx(a.x + ux * t), sy(a.y + uy * t), dotR, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  const r = nodeR * k;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  for (const node of nodes) {
    const skin = look(dump, node.type);
    const x = sx(node.x);
    const y = sy(node.y);

    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fillStyle = "rgba(24,20,14,.82)";
    ctx.fill();
    ctx.strokeStyle = skin.color;
    ctx.lineWidth = Math.max(1, r * 0.14);
    ctx.stroke();

    if (view.marks && r > 5) {
      ctx.fillStyle = skin.color;
      ctx.font = `600 ${Math.round(r * 1.1)}px ui-monospace, Consolas, monospace`;
      ctx.fillText(skin.mark, x, y + r * 0.06);
    }
  }
  ctx.textBaseline = "alphabetic";
}

const stand: StandDef = {
  id: "act-live",
  status: "note",
  title: "Акт целиком",
  draw: drawMap
};

/* ---------- панель ---------- */

function slider(
  label: string, value: number, min: number, max: number, step: number,
  onInput: (v: number) => void
): HTMLElement {
  const box = el("label", "act-ctl");
  box.appendChild(el("span", "act-ctl-label", label));
  const input = el("input");
  input.type = "range";
  input.min = String(min);
  input.max = String(max);
  input.step = String(step);
  input.value = String(value);
  const read = el("span", "act-ctl-value", value.toFixed(1));
  input.addEventListener("input", () => {
    const v = Number(input.value);
    read.textContent = v.toFixed(1);
    onInput(v);
  });
  box.appendChild(input);
  box.appendChild(read);
  return box;
}

function checkbox(label: string, initial: boolean, onChange: (v: boolean) => void): HTMLElement {
  const box = el("label", "act-check");
  const input = el("input");
  input.type = "checkbox";
  input.checked = initial;
  input.addEventListener("change", () => onChange(input.checked));
  box.appendChild(input);
  box.appendChild(el("span", null, label));
  return box;
}

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю дамп карт…");
  host.appendChild(status);

  void feed.settled.then(() => {
    const dump = feed.data;
    if (!dump || dump.profiles.length === 0) {
      status.textContent =
        `Дампа нет: ${feed.error ?? "файл пуст"}. Собери его — ./scripts/map-dump.ps1 — и подними сайт через ./scripts/lab-serve.ps1`;
      return;
    }

    host.replaceChildren();

    /* --- шапка: чем нарисовано --- */
    const meta = el("p", "dim act-meta");
    meta.textContent =
      `${dump.seeds.length} сидов · ${dump.profiles.length} профиля · снято ${dump.generated} · ${dump.source.join(" + ")}`;
    host.appendChild(meta);

    /* --- выбор карты и профиля --- */
    const bar = el("div", "act-bar");
    const prev = el("button", null, "‹");
    const next = el("button", null, "другая карта ›");
    prev.type = "button";
    next.type = "button";
    const label = el("span", "act-seed");
    const about = el("p", "dim act-hint");
    const sliders = el("div", "act-controls");

    // Профили переключаются на ОДНОМ И ТОМ ЖЕ сиде: индекс карты не сбрасывается. Иначе сравнение
    // подменяется удачей, и «этот профиль лучше» означает всего лишь «эта карта попалась ровнее».
    const tabs = el("div", "act-tabs");
    const buttons: HTMLButtonElement[] = [];

    const refresh = (): void => {
      const profile = current(dump);
      const map = profile?.maps[view.index];
      label.textContent =
        `карта ${view.index + 1} из ${dump.seeds.length} · сид ${map?.seed ?? "?"} · узлов ${map?.nodes.length ?? 0}`;
      buttons.forEach((b, i) => { b.dataset["active"] = String(i === view.profile); });
      if (!profile) return;
      about.innerHTML =
        `<b>${profile.title}</b> — ${profile.note}. Шаги ${profile.style["stepX"]}×${profile.style["stepY"]}, ` +
        `${profile.config["columns"]} этажей, ширина ${profile.config["minColumnWidth"]}–${profile.config["maxColumnWidth"]}, ` +
        `веер до ${profile.config["maxEdgesPerNode"]}. Ползунки ниже крутят копию поверх профиля — ` +
        `понравившиеся числа переносятся в <code>profiles.json</code> или в <code>MapStyle.asset</code> руками.`;
    };

    const applyProfile = (index: number): void => {
      view.profile = index;
      const profile = current(dump);
      view.stepX = profile?.style["stepX"] ?? view.stepX;
      view.stepY = profile?.style["stepY"] ?? view.stepY;
      sliders.replaceChildren(...layoutControls());
      refresh();
    };

    function layoutControls(): HTMLElement[] {
      return [
        slider("шаг по длине", view.stepX, 3, 16, 0.5, (v) => { view.stepX = v; }),
        slider("шаг поперёк", view.stepY, 2, 14, 0.5, (v) => { view.stepY = v; }),
        checkbox("знаки типов", view.marks, (v) => { view.marks = v; }),
        checkbox("номера этажей", view.floors, (v) => { view.floors = v; }),
        checkbox("лист", view.sheet, (v) => { view.sheet = v; })
      ];
    }

    dump.profiles.forEach((profile, i) => {
      const button = el("button", null, profile.title);
      button.type = "button";
      button.addEventListener("click", () => applyProfile(i));
      buttons.push(button);
      tabs.appendChild(button);
    });
    host.appendChild(tabs);

    prev.addEventListener("click", () => {
      view.index = (view.index - 1 + dump.seeds.length) % dump.seeds.length;
      refresh();
    });
    next.addEventListener("click", () => {
      view.index = (view.index + 1) % dump.seeds.length;
      refresh();
    });

    bar.appendChild(prev);
    bar.appendChild(next);
    bar.appendChild(label);
    host.appendChild(bar);

    /* --- канвас --- */
    const canvas = el("canvas", "act-canvas");
    host.appendChild(canvas);
    stage.watch(canvas, stand, 1000, 380);

    host.appendChild(sliders);
    host.appendChild(about);
    applyProfile(view.profile);

    /* --- легенда типов --- */
    const legend = el("div", "act-legend");
    for (const name of dump.nodeTypes) {
      const skin = LOOK[name];
      if (!skin) continue;
      const item = el("span", "act-legend-item");
      const mark = el("b", null, skin.mark);
      mark.style.color = skin.color;
      item.appendChild(mark);
      item.appendChild(el("span", null, skin.title));
      legend.appendChild(item);
    }
    host.appendChild(legend);
  });
}

const section: SectionDef = {
  id: "map-act",
  title: "Акт целиком",
  lede:
    "Карта из настоящего генератора: топология и числа приходят дампом из проекта, а не нарисованы " +
    "под вывод. Здесь проверяется композиция — хватает ли воздуха, во что превращается веер дорог, " +
    "останется ли место под имена зон.",
  transport: false,
  blocks: [
    {
      kind: "head",
      id: "live",
      title: "Живая карта",
      lede:
        "Шестьдесят сидов подряд, профили переключаются на одном и том же сиде. Сид показан — кривую " +
        "карту можно назвать числом и повторить в редакторе."
    },
    { kind: "live", id: "act-live-map", render },
    {
      kind: "head",
      id: "truth",
      title: "Чему здесь верить",
      lede: "Стенд отвечает на вопросы о композиции и молчит о материалах."
    },
    {
      kind: "table",
      head: ["Что", "Откуда", "Верить?"],
      rows: [
        ["Узлы, рёбра, типы", "MapGenerator, скомпилированный из исходников", "да, это ровно то, что будет в игре"],
        ["Ширины этажей, якоря, веса", "ActConfig.asset", "да, играет ассет"],
        ["Шаги сетки, поля листа, шаг дорожки", "MapStyle.asset", "да — но ползунки их временно переопределяют"],
        ["Радиус узла", "прикидка 0.45, в игре — масштаб префаба", "приблизительно"],
        ["Цвета и знаки типов", "своя палитра стенда", "нет: в игре узлы носят спрайты, цвет идёт из темы по роли"],
        ["Шейдер зон, лист, туман, камера", "не показано вовсе", "нет — это увидится только в игре"]
      ]
    },
    {
      kind: "note",
      html:
        "Дамп — единственные данные Лаборатории, которые лежат снимком, а не читаются с диска на лету: " +
        "генератор надо собрать и выполнить. Отсюда правило — <b>тронул MapGenerator или конфиг акта, " +
        "прогони <code>./scripts/map-dump.ps1</code></b>. Дата снимка стоит в шапке именно для этого."
    }
  ]
};

export default section;
