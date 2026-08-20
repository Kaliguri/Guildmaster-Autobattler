/* Часы показа: одни на весь сайт.

   Показ идёт на 30 Гц — та же частота, что у симуляции в движке. Покадровый разбор обязан работать
   одинаково во всех разделах, иначе сравнивать варианты нельзя: «на пятом кадре» должно значить
   одно и то же на стенде удара и на стенде барьера.

   Отдельным модулем, а не внутри каркаса, чтобы разделы могли читать время, не завися от шапки,
   роутера и витрины. */

export const FPS = 30;

/** Кадр внутри клипа атаки, 0..TOTAL-1. Ходит по кругу: клип повторяется. */
export let frame = 0;

/** Монотонный кадр от загрузки страницы. Статусы живут секундами и по кругу не ходят,
 *  поэтому им нужно время, которое не сбрасывается. */
export let tick = 0;

export const TOTAL = 30;

/** Кадр контакта в клипе атаки: до него замах, после — проводка. */
export const CONTACT = 16;

let playing = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
let speed = 1;
let acc = 0;
let last = 0;

export function isPlaying(): boolean {
  return playing;
}

export function setPlaying(value: boolean): void {
  playing = value;
  if (value) requestPaint();
}

export function setSpeed(value: number): void {
  speed = value;
}

/** Встать на конкретный кадр. Нужен съёмке: снимок обязан показывать заданный момент, а не тот,
 *  на котором браузер успел остановиться, — иначе два снимка подряд нельзя сравнивать. */
export function setFrame(value: number): void {
  frame = ((value % TOTAL) + TOTAL) % TOTAL;
  tick = Math.max(0, value);
  requestPaint();
}

/** Шаг вручную: транспорт ставит показ на паузу и двигает время сам. */
export function stepBy(delta: number): void {
  frame = (frame + delta + TOTAL) % TOTAL;
  tick = Math.max(0, tick + delta);
  requestPaint();
}

// Цикл спит, когда рисовать некому. Раньше он крутился ВСЕГДА: на разделе с таблицами сцен нет
// вовсе, а сайт всё равно занимал кадр шестьдесят раз в секунду и писал в DOM показание часов.
// Отсюда и тормоза там, где ничего не движется, — платил за анимацию каждый раздел.
let awake = false;
let renderFn: (() => void) | null = null;
let hasScenes: () => boolean = () => false;

/**
 * Разбудить показ: нарисовать кадр и, если есть что анимировать, продолжить крутить время.
 * Зовётся из всего, что меняет картинку, — появления сцены, шага транспорта, смены раздела.
 */
export function requestPaint(): void {
  if (awake || renderFn == null) return;
  awake = true;
  last = 0;
  requestAnimationFrame(step);
}

function step(ts: number): void {
  if (renderFn == null) { awake = false; return; }

  if (!last) last = ts;
  const dt = ts - last;
  last = ts;

  if (playing) {
    acc += dt * speed;
    const per = 1000 / FPS;
    while (acc >= per) {
      acc -= per;
      frame = (frame + 1) % TOTAL;
      tick++;
    }
  }

  renderFn();

  // Спим, если нечего двигать: часы на паузе или на странице нет ни одной сцены. Показание кадра
  // при этом остаётся верным — оно и не менялось.
  if (playing && hasScenes()) requestAnimationFrame(step);
  else awake = false;
}

/** Крутит время и зовёт отрисовку. Владелец цикла один — иначе кадры пойдут вразнобой. */
export function start(render: () => void, scenesAlive: () => boolean): void {
  renderFn = render;
  hasScenes = scenesAlive;
  requestPaint();
}
