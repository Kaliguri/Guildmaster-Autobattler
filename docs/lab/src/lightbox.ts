/* Лайтбокс: стенд крупно, поверх страницы.

   Стенды в сетке маленькие — по три в ряд, — и разглядеть в них тонкую графику нельзя: подтон
   пропитывания, узор трещин, ступень искр. Раньше это чинилось только тем, что стенду задавали
   большой `size`, то есть страница раздувалась ради одной детали.

   Клик по сцене открывает её во весь экран, стрелки листают соседние сцены раздела, Escape
   закрывает. Анимация продолжает идти: смотреть в замершую картинку смысла нет — вопрос почти
   всегда в движении. */

import { el } from "./dom.js";
import { register, unregister } from "./stage.js";
import type { StandDef } from "./types.js";

interface Entry {
  stand: StandDef;
  w: number;
  h: number;
}

let entries: Entry[] = [];
let box: HTMLElement | null = null;
let canvas: HTMLCanvasElement | null = null;
let index = -1;

/** Список сцен текущей страницы: по нему листают стрелки. Пересобирается при смене маршрута. */
export function setEntries(list: Entry[]): void {
  entries = list;
}

export function open(stand: StandDef): void {
  const at = entries.findIndex((e) => e.stand === stand);
  if (at < 0) return;
  build();
  show(at);
}

function build(): void {
  if (box) {
    box.hidden = false;
    return;
  }

  box = el("div", "lightbox");
  const frame = el("div", "lightbox-frame");
  canvas = el("canvas");
  const caption = el("div", "lightbox-caption");

  const close = el("button", "lightbox-close", "закрыть");
  close.type = "button";
  const prev = el("button", "lightbox-nav prev", "‹");
  prev.type = "button";
  prev.setAttribute("aria-label", "Предыдущая сцена");
  const next = el("button", "lightbox-nav next", "›");
  next.type = "button";
  next.setAttribute("aria-label", "Следующая сцена");

  frame.append(canvas, caption);
  box.append(prev, frame, next, close);
  document.body.appendChild(box);

  close.addEventListener("click", hide);
  prev.addEventListener("click", () => step(-1));
  next.addEventListener("click", () => step(1));
  box.addEventListener("click", (e) => {
    // Клик мимо кадра закрывает: так же ведёт себя любой просмотрщик, и это ожидаемо.
    if (e.target === box || e.target === frame) hide();
  });
  document.addEventListener("keydown", onKey);
}

function onKey(e: KeyboardEvent): void {
  if (!box || box.hidden) return;
  if (e.key === "Escape") hide();
  else if (e.key === "ArrowRight") step(1);
  else if (e.key === "ArrowLeft") step(-1);
}

function step(delta: number): void {
  if (entries.length === 0) return;
  show((index + delta + entries.length) % entries.length);
}

function show(at: number): void {
  if (!box || !canvas) return;
  const entry = entries[at];
  if (!entry) return;

  if (index >= 0) unregister(canvas);
  index = at;
  box.hidden = false;

  canvas.width = entry.w;
  canvas.height = entry.h;
  // Кадр держит пропорции сцены и не вылезает за экран ни по одной стороне.
  canvas.style.aspectRatio = `${entry.w} / ${entry.h}`;

  const caption = box.querySelector(".lightbox-caption");
  if (caption) {
    caption.innerHTML =
      `<b>${entry.stand.title}</b>` +
      (entry.stand.note ? `<span>${entry.stand.note}</span>` : "") +
      `<i>${at + 1} / ${entries.length} · стрелки листают, Esc закрывает</i>`;
  }

  register(canvas, entry.stand, entry.w, entry.h);
}

function hide(): void {
  if (!box || !canvas) return;
  if (index >= 0) unregister(canvas);
  index = -1;
  box.hidden = true;
}
