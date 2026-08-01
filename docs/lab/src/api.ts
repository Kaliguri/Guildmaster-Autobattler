/* Данные, которые сайт НЕ хранит у себя, а спрашивает у проекта.

   Палитра, указатель по вики — всё это имеет владельца вне сайта, и копия разошлась бы с
   оригиналом на первой же правке. Сервер (scripts/lab_server.py) читает их с диска на лету;
   без сервера раздел честно говорит, что данных нет, а не показывает устаревший снимок. */

export interface PaletteToken {
  name: string;
  value: string;
  note: string;
}

export interface PaletteGroup {
  file: string;
  tokens: PaletteToken[];
}

export interface WikiIndex {
  /** Абсолютный путь к vault: без него Obsidian не находит хранилище по ссылке. */
  root: string;
  vault: string;
  count: number;
  notes: WikiNote[];
}

export interface WikiNote {
  path: string;
  vault: string;
  cluster: string;
  file: string;
  title: string;
  status: string;
  tags: string[];
  words: number;
}

/** Состояние загрузки: показать надо и «жду», и «не получилось», а не пустой холст.
 *  `settled` резолвится в обоих случаях — рисовалке на canvas хватает опроса каждый кадр, а блоку
 *  из живого DOM нужен именно сигнал: он строится один раз и сам себя не перерисовывает. */
export interface Feed<T> {
  data: T | null;
  error: string | null;
  settled: Promise<void>;
}

function feed<T>(url: string): Feed<T> {
  const state: Feed<T> = { data: null, error: null, settled: Promise.resolve() };
  state.settled = fetch(url)
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
    .then((json: T) => { state.data = json; })
    .catch((err: unknown) => {
      state.error = err instanceof Error ? err.message : String(err);
      console.warn(`Лаборатория: ${url} недоступен — запусти ./scripts/lab-serve.ps1`, err);
    });
  return state;
}

let paletteFeed: Feed<{ groups: PaletteGroup[] }> | null = null;
let wikiFeed: Feed<WikiIndex> | null = null;

export function fetchPalette(): Feed<{ groups: PaletteGroup[] }> {
  paletteFeed ??= feed("api/palette");
  return paletteFeed;
}

export function fetchWikiIndex(): Feed<WikiIndex> {
  wikiFeed ??= feed("api/gdd-index");
  return wikiFeed;
}

/** Общая заглушка на канвасе, пока данных нет или их не будет. */
export function drawFeedState(
  ctx: CanvasRenderingContext2D, w: number, h: number, feed: Feed<unknown>, what: string
): boolean {
  if (feed.data) return false;
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = feed.error ? "rgba(165,118,94,1)" : "rgba(147,128,94,.8)";
  ctx.fillText(
    feed.error ? `${what}: нет данных — нужен ./scripts/lab-serve.ps1` : `читаю ${what}…`,
    22, h / 2
  );
  return true;
}
