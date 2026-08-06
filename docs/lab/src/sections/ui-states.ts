/* Элементы интерфейса и их состояния: контактный лист, снятый из ЖИВОЙ игры.

   Заказ Макса 06.08.2026: «чтобы я всегда мог и отдельные элементы глядеть, смотреть что ты
   сделала, добавила». До этого кадры уходили в чат и терялись в переписке — посмотреть «а как
   сейчас выглядит слот» было негде.

   Кадры снимает `Alebardium → UI → Contact Sheet` (пункт в Unity, работает в play): он строит
   образец каждого элемента из `UiComponentRegistry` во всех его состояниях поверх настоящей панели
   и кладёт PNG прямо сюда. Состояния навязываются рефлексией `VisualElement.pseudoStates` —
   форсировать их иначе Unity не даёт, и на обычном скриншоте наведения с нажатием просто нет.

   Список кадров НЕ захардкожен: он приходит манифестом из того же прогона. Иначе стенд разойдётся
   с реестром на первом же новом компоненте — ровно так врала прежняя витрина UiPreviewCatalog,
   которая показывала 7 контролов из 14 и собирала вкладку не тем контролом, что игра. */

import { el, html } from "../dom.js";
import type { Feed } from "../api.js";
import type { SectionDef } from "../types.js";

interface FrameElement {
  label: string;
  block: string;
  states: string;
  /** Цветовые метки роли через запятую: вторая ось текста. Пусто, если роль их не носит. */
  tones?: string;
  /** Замер с живого образца: гарнитура, кегль, цвет, разрядка. Пусто у нетекстовых. */
  type?: string;
}

interface Frame {
  group: string;
  file: string;
  elements: FrameElement[];
}

interface Manifest {
  frames: Frame[];
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

const GROUP_TITLE: Record<string, string> = {
  Buttons: "Кнопки",
  Tabs: "Вкладки и чипы",
  Cards: "Карточки и слоты",
  Rows: "Строки настроек",
  Panels: "Панели и декор",
  Overlays: "Поверх всего",
  Typography: "Текст",
  Dev: "Дев-тулинг"
};

/** Состояния из реестра — флагами через запятую; человеку нужны слова. */
const STATE_WORD: Record<string, string> = {
  Hover: "наведение",
  Active: "нажатие",
  Focus: "фокус",
  Disabled: "выключено",
  Checked: "отмечено"
};

function stateWords(states: string): string {
  if (!states || states === "None") return "декоративный";
  return states
    .split(",")
    .map((s) => STATE_WORD[s.trim()] ?? s.trim())
    .join(" · ");
}

const manifest = feed<Manifest>("data/ui-states.json");

function frameCard(frame: Frame): HTMLElement {
  const box = el("article", "card");

  const head = el("div", "i-head");
  head.appendChild(el("h3", null, GROUP_TITLE[frame.group] ?? frame.group));
  box.appendChild(head);

  // Кадр открывается в новой вкладке по клику: лист снят в 1920×1080, и внутри страницы он
  // неизбежно ужат — детали состояния на ужатом кадре не разглядеть, а именно за ними сюда и идут.
  const link = el("a") as HTMLAnchorElement;
  link.href = `assets/ui-states/${frame.file}`;
  link.target = "_blank";
  link.rel = "noopener";

  const img = el("img") as HTMLImageElement;
  img.src = `assets/ui-states/${frame.file}`;
  img.alt = `Состояния: ${frame.group}`;
  img.loading = "lazy";
  img.style.width = "100%";
  img.style.display = "block";
  img.style.borderRadius = "4px";
  link.appendChild(img);
  box.appendChild(link);

  // Что именно на кадре — списком под ним: по картинке 1920px, ужатой в ширину страницы, класс
  // элемента не прочитать, а искать по нему приходится чаще всего.
  const list = el("ul");
  list.style.margin = "0.8rem 0 0";
  list.style.paddingLeft = "1.2rem";
  for (const item of frame.elements) {
    // У текстовой роли осмысленны замер и метки, у интерактивного элемента — состояния. Печатается
    // то, что у записи есть: показывать «декоративный» рядом с замером шрифта значит отвечать на
    // вопрос, которого к тексту не было.
    const parts: string[] = [];
    if (item.type) parts.push(item.type);
    else parts.push(stateWords(item.states));
    if (item.tones) parts.push(`метки: ${item.tones}`);

    const tail = `<span class="dim">· ${parts.join(" · ")}</span>`;
    list.appendChild(html("li", `<code>${item.block}</code> — ${item.label} ${tail}`));
  }
  box.appendChild(list);

  return box;
}

function render(host: HTMLElement): void {
  const box = el("div");
  box.appendChild(el("p", "dim", "Загружаю снимок…"));
  host.appendChild(box);

  void manifest.settled.then(() => {
    box.textContent = "";

    if (!manifest.data?.frames?.length) {
      box.appendChild(html("p",
        "Снимка нет. Кадры кладёт пункт <code>Alebardium → UI → Contact Sheet</code> — " +
        "он работает <b>в play</b>: вне игры живой панели не существует, и снимать нечего." +
        (manifest.error ? `<br><span class="dim">${manifest.error}</span>` : ""),
        "note"));
      return;
    }

    // ОДНА КОЛОНКА, а не сетка карточек: кадр снят в 1920×1080, и в колонке 290px от состояния
    // остаётся пятно. Здесь смотрят детали, ради них лист и снимается.
    const grid = el("div");
    grid.style.display = "grid";
    grid.style.gap = "var(--gap)";
    for (const frame of manifest.data.frames) grid.appendChild(frameCard(frame));
    box.appendChild(grid);
  });
}

const section: SectionDef = {
  id: "ui-states",
  title: "Элементы интерфейса",
  lede: "Каждый элемент во всех своих состояниях — покой, наведение, нажатие, фокус, выключено, " +
        "отмечено. У текста вместо состояний — метки: приглушённый, латунь, прирост, убыль, " +
        "опасность. Снято из живой игры, а не со стенда: важно, как элемент выглядит на настоящей " +
        "подложке и в настоящем масштабе.",
  transport: false,
  blocks: [
    {
      kind: "note",
      html: "Обновляется прогоном <code>Alebardium → UI → Contact Sheet</code> в play. Кадры лежат " +
            "в репозитории, поэтому историю держит git: видно и что менялось, и вместе с каким " +
            "коммитом. Перечень элементов приходит из <code>UiComponentRegistry</code> — стенд не " +
            "может разойтись с игрой, потому что не хранит своего списка."
    },
    { kind: "live", id: "ui-states-gallery", render }
  ]
};

export default section;
