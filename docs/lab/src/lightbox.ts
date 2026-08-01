/* Лайтбокс: показать крупно то, на что смотрят.

   Всё визуальное в Лаборатории мелкое — сцены идут по три в ряд, плитки цвета размером с ноготь, —
   и тонкую работу в них не разглядеть: подтон пропитывания, узор трещин, разницу двух соседних
   оттенков латуни. Раньше это чинилось только тем, что стенду задавали большой размер, то есть
   страница раздувалась ради одной детали.

   Клик открывает во весь экран, стрелки листают соседей, Escape закрывает. Сцена продолжает
   двигаться: смотреть в замерший кадр смысла нет, вопрос почти всегда в движении. */

import { el } from "./dom.js";
import { register, unregister } from "./stage.js";
import type { PaletteToken } from "./api.js";
import type { StandDef } from "./types.js";

/** Крупно показываем два разных предмета, и они не сводятся друг к другу: сцена живая и её надо
 *  тикать, образец цвета статичен и его надо мочь скопировать. */
export type Item =
  | { kind: "scene"; stand: StandDef; w: number; h: number }
  | { kind: "color"; token: PaletteToken; css: string };

let items: Item[] = [];
let box: HTMLElement | null = null;
let frame: HTMLElement | null = null;
let index = -1;
let liveCanvas: HTMLCanvasElement | null = null;

/** Список того, что можно листать на текущей странице. Пересобирается при смене маршрута. */
export function setItems(list: Item[]): void {
  items = list;
}

export function open(target: StandDef | PaletteToken): void {
  const at = items.findIndex((it) =>
    it.kind === "scene" ? it.stand === target : it.token === target
  );
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
  box.setAttribute("role", "dialog");
  box.setAttribute("aria-modal", "true");

  frame = el("div", "lightbox-frame");

  const close = el("button", "lightbox-close", "×");
  close.type = "button";
  close.setAttribute("aria-label", "Закрыть");
  const prev = el("button", "lightbox-nav prev", "‹");
  prev.type = "button";
  prev.setAttribute("aria-label", "Предыдущее");
  const next = el("button", "lightbox-nav next", "›");
  next.type = "button";
  next.setAttribute("aria-label", "Следующее");

  box.append(prev, frame, next, close);
  document.body.appendChild(box);

  close.addEventListener("click", hide);
  prev.addEventListener("click", () => step(-1));
  next.addEventListener("click", () => step(1));
  // Клик по подложке закрывает — так ведёт себя любой просмотрщик, и этого ждут.
  box.addEventListener("mousedown", (e) => {
    if (e.target === box) hide();
  });
  document.addEventListener("keydown", onKey);
  // Размер кадра зависит от окна, поэтому пересчитывается вместе с ним.
  window.addEventListener("resize", () => {
    if (box && !box.hidden && index >= 0) show(index);
  });
}

function onKey(e: KeyboardEvent): void {
  if (!box || box.hidden) return;
  if (e.key === "Escape") hide();
  else if (e.key === "ArrowRight") step(1);
  else if (e.key === "ArrowLeft") step(-1);
}

function step(delta: number): void {
  if (items.length === 0) return;
  show((index + delta + items.length) % items.length);
}

function show(at: number): void {
  if (!box || !frame) return;
  const item = items[at];
  if (!item) return;

  release();
  index = at;
  box.hidden = false;
  frame.replaceChildren();
  // Перезапуск анимации появления: без сброса класса второй показ подряд прошёл бы молча.
  frame.classList.remove("pop");
  void frame.offsetWidth;
  frame.classList.add("pop");

  if (item.kind === "scene") showScene(item);
  else showColor(item);

  const nav = box.querySelectorAll<HTMLElement>(".lightbox-nav");
  nav.forEach((b) => { b.hidden = items.length < 2; });
}

function showScene(item: Extract<Item, { kind: "scene" }>): void {
  if (!frame) return;
  sizeFrame(item.w / item.h);
  const canvas = el("canvas");
  canvas.width = item.w;
  canvas.height = item.h;
  frame.appendChild(canvas);
  frame.appendChild(caption(item.stand.title, item.stand.note ?? ""));
  liveCanvas = canvas;
  register(canvas, item.stand, item.w, item.h);
}

function showColor(item: Extract<Item, { kind: "color" }>): void {
  if (!frame) return;
  sizeFrame(16 / 9);
  const field = el("div", "lightbox-color");
  field.style.background = item.css;
  field.style.height = `${Math.round(frameWidth * 0.5)}px`;
  frame.appendChild(field);

  const cap = caption(item.token.name, item.token.note);
  const value = el("button", "lightbox-copy", item.token.value.replace(/\s+/g, " "));
  value.type = "button";
  value.title = "Скопировать имя токена";
  value.addEventListener("click", () => {
    const done = (): void => {
      value.dataset["copied"] = "true";
      setTimeout(() => value.removeAttribute("data-copied"), 1200);
    };
    if (navigator.clipboard?.writeText) navigator.clipboard.writeText(item.token.name).then(done, done);
    else done();
  });
  cap.appendChild(value);
  frame.appendChild(cap);
}

/** Ширина кадра в пикселях: столько, чтобы картинка влезла и по ширине, и по высоте экрана.
 *  Считается ЗДЕСЬ, а не в CSS, потому что зависит сразу от обеих сторон и от пропорций сцены. */
let frameWidth = 0;

function sizeFrame(ratio: number): void {
  if (!frame) return;
  const gutter = window.innerWidth < 720 ? 32 : 190; // место под круглые кнопки по бокам
  const availW = window.innerWidth - gutter;
  const availH = window.innerHeight - 190; // подпись, отступы и кнопка закрытия
  frameWidth = Math.max(280, Math.min(availW, availH * ratio, 1600));
  frame.style.width = `${Math.round(frameWidth)}px`;
}

function caption(title: string, note: string): HTMLElement {
  const cap = el("div", "lightbox-caption");
  cap.appendChild(el("b", null, title));
  if (note) {
    const span = el("span");
    span.innerHTML = note;
    cap.appendChild(span);
  }
  cap.appendChild(el("i", null, `${index + 1} / ${items.length} · стрелки листают, Esc закрывает`));
  return cap;
}

/** Снять живую сцену с отрисовки: иначе она продолжит тикать за закрытым лайтбоксом. */
function release(): void {
  if (liveCanvas) unregister(liveCanvas);
  liveCanvas = null;
}

function hide(): void {
  if (!box) return;
  release();
  index = -1;
  box.hidden = true;
}
