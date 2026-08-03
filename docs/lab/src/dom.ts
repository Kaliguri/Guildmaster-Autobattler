/* Мелочи построения DOM. Отдельным модулем, чтобы каркас читался как «что рисуем», а не как
   бесконечное createElement. Своего состояния тут нет. */

export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  cls?: string | null,
  text?: string
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text != null) node.textContent = text;
  return node;
}

/** Узел с разметкой внутри: пояснения к стендам содержат <b>, <code> и <br>. */
export function html<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  markup: string,
  cls?: string
): HTMLElementTagNameMap[K] {
  const node = el(tag, cls);
  node.innerHTML = markup;
  return node;
}

export function clear(node: HTMLElement): void {
  node.replaceChildren();
}
