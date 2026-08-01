/* Тумблеры стенда: маленькие переключатели, которые сами по себе предмет обсуждения.

   Информационный слой по Z — не удобство просмотра, а решение: «нажал и осталось», а не удержание,
   и на паузе он сам не всплывает. Такое проверяется только вживую, поэтому у стенда должна быть
   ручка, а не описание ручки.

   Состояние держится здесь, а не в разделе, потому что рисует кнопку каркас, а читает значение
   рисовалка: у факта один владелец, обе стороны ходят сюда. */

const state = new Map<string, boolean>();

export function isOn(id: string): boolean {
  return state.get(id) ?? false;
}

export function setOn(id: string, value: boolean): void {
  state.set(id, value);
}

export function toggle(id: string): boolean {
  const next = !isOn(id);
  state.set(id, next);
  return next;
}
