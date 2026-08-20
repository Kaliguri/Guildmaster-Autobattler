/* Свечение эффектов под блумом: один эффект за раз, рядом — его вариации настроек.

   Заказ Макса 06.08.2026: «все текущие блум эффекты слишком сильные». Первый заход мерил свечение
   клинка и промахнулся — мерить надо трейл за оружием и хит-эффект каждого типа. Второй заход
   показывал все эффекты разом на одном кадре, и это тоже оказалось не тем: смотрят и подбирают
   ПО ОДНОМУ. Отсюда нынешний вид — переключатель эффекта, под ним его вариации.

   Кадры снимает `Alebardium → VFX → Post FX Lab` (работает БЕЗ play: фаза каждого эффекта у нас
   параметр шейдера, а не состояние скрипта). Он же дописывает манифест — своего списка эффектов
   стенд не держит и разойтись с ним не может. Кадр квадратный и всегда одного размера в мире:
   подгонка под габарит эффекта плавила бы масштаб, и снимки перестали бы сравниваться.

   Профиль-ассет при съёмке не трогается: значения крутятся на временной копии. */

import { el, html } from "../dom.js";
import type { Feed } from "../api.js";
import type { SectionDef } from "../types.js";

interface Shot {
  file: string;
  intensity: number;
  scatter: number;
  /** Вариация, которая стоит в игре прямо сейчас. */
  current: boolean;
}

interface Effect {
  id: string;
  label: string;
  tone: string;
  shots: Shot[];
}

interface Manifest {
  effects: Effect[];
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

const manifest = feed<Manifest>("data/bloom-showcase.json");

function shotCard(shot: Shot): HTMLElement {
  const box = el("article", "card");

  const head = el("div", "i-head");
  head.appendChild(el("h3", null, `Яркость ${shot.intensity.toFixed(2)} · растекание ${shot.scatter.toFixed(2)}`));
  if (shot.current) head.appendChild(el("span", "chip", "сейчас в игре"));
  box.appendChild(head);

  // Кадр открывается в новой вкладке: ужатый в колонку он отвечает только на вопрос «ярче или
  // тусклее», а не «пересвечен ли цвет» — а идут сюда чаще за вторым.
  const link = el("a") as HTMLAnchorElement;
  link.href = `assets/bloom-showcase/${shot.file}`;
  link.target = "_blank";
  link.rel = "noopener";

  const img = el("img") as HTMLImageElement;
  img.src = `assets/bloom-showcase/${shot.file}`;
  img.alt = `Яркость ${shot.intensity}, растекание ${shot.scatter}`;
  img.loading = "lazy";
  img.style.width = "100%";
  img.style.display = "block";
  img.style.borderRadius = "4px";
  link.appendChild(img);
  box.appendChild(link);

  return box;
}

function render(host: HTMLElement): void {
  const box = el("div");
  box.appendChild(el("p", "dim", "Загружаю кадры…"));
  host.appendChild(box);

  void manifest.settled.then(() => {
    box.textContent = "";

    const effects = manifest.data?.effects ?? [];
    if (!effects.length) {
      box.appendChild(html("p",
        "Кадров нет. Их кладёт пункт <code>Alebardium → VFX → Post FX Lab</code> кнопкой " +
        "«Снять серию» — по одному эффекту за раз." +
        (manifest.error ? `<br><span class="dim">${manifest.error}</span>` : ""),
        "note"));
      return;
    }

    // Переключатель эффектов: показывается РОВНО ОДИН. Все сразу на экране — это уже пробовали,
    // и оно отвечает на другой вопрос: «кто ярче кого», а не «как выглядит вот этот».
    const tabs = el("div");
    tabs.style.display = "flex";
    tabs.style.flexWrap = "wrap";
    tabs.style.gap = "0.4rem";
    tabs.style.marginBottom = "var(--gap)";

    const stage = el("div");
    const buttons: HTMLButtonElement[] = [];

    function show(index: number): void {
      const effect = effects[index];
      if (!effect) return;
      buttons.forEach((b, i) => b.setAttribute("aria-pressed", String(i === index)));

      stage.textContent = "";
      stage.appendChild(html("p",
        `<b>${effect.label}</b> · тон свечения <code>${effect.tone}</code>. ` +
        `Один тон на все эффекты намеренно: сравнивается яркость, а разные оттенки превратили бы ` +
        `это в спор о цвете.`,
        "note"));

      const grid = el("div");
      grid.style.display = "grid";
      grid.style.gap = "var(--gap)";
      grid.style.gridTemplateColumns = "repeat(auto-fit, minmax(320px, 1fr))";
      for (const shot of effect.shots) grid.appendChild(shotCard(shot));
      stage.appendChild(grid);
    }

    effects.forEach((effect, i) => {
      const b = el("button", "chip") as HTMLButtonElement;
      b.type = "button";
      b.textContent = effect.label;
      b.addEventListener("click", () => show(i));
      buttons.push(b);
      tabs.appendChild(b);
    });

    box.appendChild(tabs);
    box.appendChild(stage);
    show(0);
  });
}

const section: SectionDef = {
  id: "gamefeel-bloom",
  title: "Свечение эффектов",
  lede: "Как каждый светящийся эффект боя выглядит под блумом и что с ним делают настройки свечения. " +
        "Эффект выбирается сверху, под ним — вариации: чтобы выбирать громкость не по памяти о " +
        "прошлом кадре, а сравнением.",
  transport: false,
  blocks: [
    {
      kind: "note",
      html: "Кадры сняты <b>по одному эффекту</b> и на чёрном фоне. В бою эффекты ложатся друг на " +
            "друга и на фон арены, поэтому здесь честно виден только сам эффект и его громкость; " +
            "на вопрос «не слишком ли много суммарно» отвечает живой бой."
    },
    { kind: "live", id: "bloom-showcase-gallery", render }
  ]
};

export default section;
