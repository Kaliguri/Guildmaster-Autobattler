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
}

export function setSpeed(value: number): void {
  speed = value;
}

/** Шаг вручную: транспорт ставит показ на паузу и двигает время сам. */
export function stepBy(delta: number): void {
  frame = (frame + delta + TOTAL) % TOTAL;
  tick = Math.max(0, tick + delta);
}

/** Крутит время и зовёт отрисовку. Владелец цикла один — иначе кадры пойдут вразнобой. */
export function start(render: () => void): void {
  const loop = (ts: number): void => {
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
    render();
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}
