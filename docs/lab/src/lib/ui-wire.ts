/* Примитивы чертежа интерфейса: рамка экрана, блоки, подписи, мерные линии.

   Почему СЕРЫЙ чертёж, а не наши цвета (выбор Макса 05.08.2026): на этапе раскладки цвет
   перетягивает внимание и разговор уезжает в оттенки, хотя решается «что где лежит и какого
   размера». Палитра у стенда своя — нейтральные ступени, ни одна из которых не встречается в игре,
   чтобы чертёж нельзя было принять за экран.

   Почему на canvas, а не DOM: чертёж читается целиком и не копируется по частям — ровно тот случай,
   когда canvas дешевле разметки. Плитки палитры, наоборот, живут DOM'ом, потому что их значения
   несут в код (см. style-palette.ts).

   Координаты внутри рисовалок задаются в ДОЛЯХ экрана 1920x1080, а не в пикселях сцены: разбор
   рефов меряет тем же (`Art_Dev/UI Refs/_teardowns/`), и число из разбора переносится сюда без
   пересчёта. */

/** Ступени чертежа: фон кадра, поле, рамка блока, заливка блока, текст, размерная линия. */
export const WIRE = {
  frame: "#1B1B1E",
  field: "#232327",
  line: "#6E6E76",
  lineLit: "#A8A8B2",
  fill: "#2C2C31",
  fillLit: "#38383F",
  text: "#C9C9D2",
  dim: "#8A8A93",
  accent: "#C8A24C",
  danger: "#B4564E"
} as const;

export interface Rect {
  /** Доли экрана 1920x1080: x, y, ширина, высота. */
  x: number;
  y: number;
  w: number;
  h: number;
}

/** Пересчёт долей экрана в пиксели сцены. Сцена всегда 16:9, поэтому масштаб один на обе оси. */
export function px(r: Rect, w: number, h: number): [number, number, number, number] {
  return [r.x * w, r.y * h, r.w * w, r.h * h];
}

/** Кадр экрана: тёмное поле 16:9 со светлой кромкой. Всё остальное рисуется внутри него. */
export function screen(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  ctx.fillStyle = WIRE.frame;
  ctx.fillRect(0, 0, w, h);
  ctx.strokeStyle = WIRE.line;
  ctx.lineWidth = 1;
  ctx.strokeRect(0.5, 0.5, w - 1, h - 1);
}

export interface BoxOpts {
  /** Подпись внутри блока, по центру. */
  label?: string;
  /** Вторая строка помельче: что это за элемент по сути. */
  sub?: string;
  /** Пунктирная рамка — «место под то, чего сейчас нет» (пустой слот, будущий блок). */
  dashed?: boolean;
  /** Подсвеченный блок: выбранный слот, первичное действие. */
  lit?: boolean;
  /** Цвет рамки поверх обычного: опасное действие, акцент. */
  stroke?: string;
  /** Заливки нет вовсе — блок обозначает область, а не предмет. */
  hollow?: boolean;
  /** Кегль подписи в пикселях сцены. */
  size?: number;
}

/** Блок чертежа. Единственный способ нарисовать прямоугольник в этом разделе: иначе подписи,
 *  скругления и толщины разъедутся между стендами, и сравнивать раскладки станет нельзя. */
export function box(
  ctx: CanvasRenderingContext2D,
  r: Rect,
  w: number,
  h: number,
  opts: BoxOpts = {}
): void {
  const [x, y, bw, bh] = px(r, w, h);

  if (!opts.hollow) {
    ctx.fillStyle = opts.lit ? WIRE.fillLit : WIRE.fill;
    ctx.fillRect(x, y, bw, bh);
  }

  ctx.save();
  if (opts.dashed) ctx.setLineDash([4, 3]);
  ctx.strokeStyle = opts.stroke ?? (opts.lit ? WIRE.lineLit : WIRE.line);
  ctx.lineWidth = opts.lit ? 1.6 : 1;
  ctx.strokeRect(x + 0.5, y + 0.5, bw - 1, bh - 1);
  ctx.restore();

  if (opts.label) {
    const size = opts.size ?? 10;
    ctx.fillStyle = opts.lit ? WIRE.text : WIRE.dim;
    ctx.font = `${size}px ui-monospace, monospace`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    const cx = x + bw / 2;
    const cy = y + bh / 2;
    if (opts.sub) {
      ctx.fillText(opts.label, cx, cy - size * 0.62);
      ctx.fillStyle = WIRE.dim;
      ctx.font = `${size - 2}px ui-monospace, monospace`;
      ctx.fillText(opts.sub, cx, cy + size * 0.72);
    } else {
      ctx.fillText(opts.label, cx, cy);
    }
  }
}

/** Строка текста в кадре: заголовок экрана, подпись, служебная строка. Рисуется без рамки —
 *  чертёж обязан отличать «блок» от «просто текста», иначе всё выглядит одинаково кликабельным. */
export function text(
  ctx: CanvasRenderingContext2D,
  s: string,
  at: { x: number; y: number },
  w: number,
  h: number,
  opts: { size?: number; align?: CanvasTextAlign; color?: string } = {}
): void {
  const size = opts.size ?? 11;
  ctx.fillStyle = opts.color ?? WIRE.text;
  ctx.font = `${size}px ui-monospace, monospace`;
  ctx.textAlign = opts.align ?? "left";
  ctx.textBaseline = "middle";
  ctx.fillText(s, at.x * w, at.y * h);
}

/** Выноска: подпись сбоку от блока с ниткой к нему. Нужна там, где имя элемента в блок не влезает
 *  (иконка, узкая кнопка), а без имени чертёж превращается в набор квадратов. */
export function callout(
  ctx: CanvasRenderingContext2D,
  from: { x: number; y: number },
  to: { x: number; y: number },
  s: string,
  w: number,
  h: number,
  align: CanvasTextAlign = "left"
): void {
  ctx.strokeStyle = WIRE.line;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(from.x * w, from.y * h);
  ctx.lineTo(to.x * w, to.y * h);
  ctx.stroke();

  ctx.fillStyle = WIRE.dim;
  ctx.font = "9px ui-monospace, monospace";
  ctx.textAlign = align;
  ctx.textBaseline = "middle";
  ctx.fillText(s, to.x * w + (align === "right" ? -4 : 4), to.y * h);
}

/** Мерная линия с числом: доля экрана, занятая блоком. Ради неё чертёж и делается — раскладку
 *  спорят числами разбора, а не ощущением («колонка кажется широкой»). */
export function measure(
  ctx: CanvasRenderingContext2D,
  r: Rect,
  s: string,
  w: number,
  h: number,
  axis: "x" | "y" = "x",
  /** Куда отложить линию по горизонтали: под блок или НАД ним. Нужно потому, что под нижней
   *  кромкой кадра линия обрезается, а под слотом там обычно стоит кнопка удаления. */
  side: "after" | "before" = "after"
): void {
  const [x, y, bw, bh] = px(r, w, h);
  ctx.strokeStyle = WIRE.accent;
  ctx.lineWidth = 1;
  ctx.beginPath();
  if (axis === "x") {
    const ly = side === "after" ? y + bh + 7 : y - 11;
    ctx.moveTo(x, ly);
    ctx.lineTo(x + bw, ly);
    ctx.moveTo(x + 0.5, ly - 3);
    ctx.lineTo(x + 0.5, ly + 3);
    ctx.moveTo(x + bw - 0.5, ly - 3);
    ctx.lineTo(x + bw - 0.5, ly + 3);
    ctx.stroke();
    ctx.fillStyle = WIRE.accent;
    ctx.font = "9px ui-monospace, monospace";
    ctx.textAlign = "center";
    ctx.textBaseline = side === "after" ? "top" : "bottom";
    ctx.fillText(s, x + bw / 2, ly + (side === "after" ? 4 : -4));
  } else {
    const lx = x + bw + 7;
    ctx.moveTo(lx, y);
    ctx.lineTo(lx, y + bh);
    ctx.moveTo(lx - 3, y + 0.5);
    ctx.lineTo(lx + 3, y + 0.5);
    ctx.moveTo(lx - 3, y + bh - 0.5);
    ctx.lineTo(lx + 3, y + bh - 0.5);
    ctx.stroke();
    ctx.fillStyle = WIRE.accent;
    ctx.font = "9px ui-monospace, monospace";
    ctx.textAlign = "left";
    ctx.textBaseline = "middle";
    ctx.fillText(s, lx + 5, y + bh / 2);
  }
}

/** Кнопка удаления: корзина рисуется линиями, а не эмодзи. Моноширинная гарнитура чертежа глифа
 *  корзины не содержит, и вместо иконки печатался пустой квадрат — на снимке это читалось как
 *  «блок без подписи», то есть чертёж врал в самом опасном месте. */
export function trash(ctx: CanvasRenderingContext2D, r: Rect, w: number, h: number): void {
  box(ctx, r, w, h, { stroke: WIRE.danger });
  const [x, y, bw, bh] = px(r, w, h);
  const cx = x + bw / 2;
  const cy = y + bh / 2;
  const s = Math.min(bw, bh) * 0.42;

  ctx.save();
  ctx.strokeStyle = WIRE.danger;
  ctx.lineWidth = 1.2;
  // крышка с ручкой
  ctx.beginPath();
  ctx.moveTo(cx - s, cy - s * 0.55);
  ctx.lineTo(cx + s, cy - s * 0.55);
  ctx.moveTo(cx - s * 0.35, cy - s * 0.55);
  ctx.lineTo(cx - s * 0.35, cy - s * 0.85);
  ctx.lineTo(cx + s * 0.35, cy - s * 0.85);
  ctx.lineTo(cx + s * 0.35, cy - s * 0.55);
  // корпус
  ctx.moveTo(cx - s * 0.75, cy - s * 0.35);
  ctx.lineTo(cx - s * 0.55, cy + s);
  ctx.lineTo(cx + s * 0.55, cy + s);
  ctx.lineTo(cx + s * 0.75, cy - s * 0.35);
  ctx.stroke();
  ctx.restore();
}

/** Живой кадр игры за интерфейсом: редкая сетка, обозначающая «здесь видно мир».
 *  Нужна, потому что половина решений этого класса экранов — про то, закрываем мы кадр или нет. */
export function worldBehind(ctx: CanvasRenderingContext2D, w: number, h: number): void {
  ctx.save();
  ctx.strokeStyle = "#2E2E33";
  ctx.lineWidth = 1;
  const step = Math.round(w / 16);
  for (let x = step; x < w; x += step) {
    ctx.beginPath();
    ctx.moveTo(x + 0.5, 1);
    ctx.lineTo(x + 0.5, h - 1);
    ctx.stroke();
  }
  for (let y = step; y < h; y += step) {
    ctx.beginPath();
    ctx.moveTo(1, y + 0.5);
    ctx.lineTo(w - 1, y + 0.5);
    ctx.stroke();
  }
  ctx.restore();
}

/** Затемнение поверх кадра: экран, который гасит мир под собой. Рисуется ПОСЛЕ worldBehind. */
export function scrim(ctx: CanvasRenderingContext2D, w: number, h: number, alpha = 0.55): void {
  ctx.fillStyle = `rgba(12,12,14,${alpha})`;
  ctx.fillRect(0, 0, w, h);
}

/** Круглый портрет: место под лицо. Отдельный примитив, а не блок со скруглением, потому что
 *  круг у нас несёт смысл — «это человек», в отличие от квадрата слота под вещь. */
export function disc(
  ctx: CanvasRenderingContext2D,
  at: { x: number; y: number; r: number },
  w: number,
  h: number,
  opts: { label?: string; lit?: boolean; dashed?: boolean } = {}
): void {
  const cx = at.x * w;
  const cy = at.y * h;
  const rad = at.r * h;

  ctx.save();
  ctx.beginPath();
  ctx.arc(cx, cy, rad, 0, Math.PI * 2);
  ctx.fillStyle = opts.lit ? WIRE.fillLit : WIRE.fill;
  ctx.fill();
  if (opts.dashed) ctx.setLineDash([4, 3]);
  ctx.strokeStyle = opts.lit ? WIRE.lineLit : WIRE.line;
  ctx.lineWidth = opts.lit ? 1.6 : 1;
  ctx.stroke();
  ctx.restore();

  if (opts.label) {
    ctx.fillStyle = WIRE.dim;
    ctx.font = "7px ui-monospace, monospace";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(opts.label, cx, cy);
  }
}

/** Замок на закрытом слоте: дужка и корпус линиями. Как и корзина, рисуется вершинами —
 *  моноширинная гарнитура чертежа глифа замка не содержит и печатает пустой квадрат. */
export function lock(ctx: CanvasRenderingContext2D, r: Rect, w: number, h: number): void {
  const [x, y, bw, bh] = px(r, w, h);
  const cx = x + bw / 2;
  const cy = y + bh / 2;
  const s = Math.min(bw, bh) * 0.22;

  ctx.save();
  ctx.strokeStyle = WIRE.dim;
  ctx.lineWidth = 1.2;
  ctx.beginPath();
  ctx.arc(cx, cy - s * 0.35, s * 0.6, Math.PI, 0);
  ctx.stroke();
  ctx.strokeRect(cx - s * 0.9, cy - s * 0.35, s * 1.8, s * 1.3);
  ctx.restore();
}
