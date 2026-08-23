/* Значение-шум: общая математика для процедурной местности и фактуры.

   Была скопирована в трёх разделах — «Пол арены», «Земля и страна», «Зоны влияния» — побитово
   одинаковой, вплоть до множителей 374761 и 668265 и трёх октав с шагом 2.1. Скопированный шум
   расходится молча: правишь частоту в одном разделе, а два соседних продолжают рисовать по-старому,
   и разницу видно только глазами на сравнении двух картинок.

   Вынесено ровно то, что совпадало ЗНАК В ЗНАК. Всё, что отличалось хоть множителем, оставлено
   на месте: у шума нет «почти такого же» — другая формула даёт другую картинку, а часть этих
   картинок Макс уже принял. */

import { jag } from "../draw.js";

export function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

/** Хэш по двум координатам поверх общего дребезга: одна точка — одно значение, всегда то же. */
export function hash2(x: number, y: number, salt: number): number {
  return jag(x * 374761 + y * 668265, salt);
}

/** Значение-шум с плавной интерполяцией (кривая 3t²-2t³): основа fbm и доменного искажения. */
export function vnoise(x: number, y: number, salt: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const xf = x - xi;
  const yf = y - yi;
  const u = xf * xf * (3 - 2 * xf);
  const v = yf * yf * (3 - 2 * yf);
  const a = hash2(xi, yi, salt);
  const b = hash2(xi + 1, yi, salt);
  const c = hash2(xi, yi + 1, salt);
  const d = hash2(xi + 1, yi + 1, salt);
  return lerp(lerp(a, b, u), lerp(c, d, u), v);
}

/** Сумма трёх октав вокруг нуля: смещение, а не яркость. Шаг частоты 2.1, а не ровно 2 —
 *  на целом удвоении октавы садятся друг на друга и в шуме проступает решётка. */
export function fbm(x: number, y: number, salt: number): number {
  let sum = 0;
  let amp = 0.5;
  let freq = 1;
  for (let o = 0; o < 3; o++) {
    sum += (vnoise(x * freq, y * freq, salt + o * 17) - 0.5) * amp;
    amp *= 0.5;
    freq *= 2.1;
  }
  return sum;
}
