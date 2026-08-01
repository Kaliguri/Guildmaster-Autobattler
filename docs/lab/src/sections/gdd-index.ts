/* Указатель по ГДД: КАРТА vault, а не его копия.

   Граница проведена сознательно (решение 2026-08-01): текст дизайн-документации живёт в
   `docs/wiki` и публикуется Quartz'ом, а Лаборатория держит только то, на что надо СМОТРЕТЬ.
   Копия текста дала бы второму владельцу право врать. Поэтому здесь заголовки, кластеры, статусы
   и теги — метаданные, по которым ищут, — и ссылка «открыть в Obsidian» на сам файл. */

import { el } from "../dom.js";
import { fetchWikiIndex, type WikiNote } from "../api.js";
import type { SectionDef } from "../types.js";

const wikiIndex = fetchWikiIndex();

/** Человеческие имена кластеров: цифровой префикс задаёт порядок, но читать его глазами больно. */
const CLUSTER_NAMES: Record<string, string> = {
  "00-meta": "мета: журналы, развилки, инбокс",
  "10-vision": "видение и питч",
  "20-combat": "бой",
  "30-progression": "прогрессия",
  "40-content": "контент",
  "50-ui": "интерфейс",
  "60-narrative": "нарратив",
  "70-gamefeel": "джус",
  "10-reference": "справочник",
  "30-how-to": "инструкции",
  "40-planning": "планы"
};

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю вики…");
  host.appendChild(status);

  const paint = (): void => {
    const data = wikiIndex.data;
    if (!data) {
      status.textContent = wikiIndex.error
        ? `Указатель недоступен: ${wikiIndex.error}. Страница читает вики через сервер — нужен ./scripts/lab-serve.ps1`
        : "вики не ответила";
      return;
    }

    host.replaceChildren();

    const notes = data.notes;
    const summary = el("p", "dim");
    const archived = notes.filter((n) => n.status === "archive").length;
    summary.textContent =
      `${data.count} заметок · ${archived} заморожено · дизайн в gdd, инженерия в tech. ` +
      "Текст живёт в vault: сайт показывает карту, а не копию.";
    host.appendChild(summary);

    const search = el("input");
    search.type = "search";
    search.placeholder = "Заголовок, кластер, тег…";
    search.className = "gdd-search";
    search.setAttribute("aria-label", "Поиск по вики");
    host.appendChild(search);

    const list = el("div", "gdd-tree");
    host.appendChild(list);

    const draw = (query: string): void => {
      const q = query.trim().toLowerCase();
      list.replaceChildren();

      const byVault = new Map<string, Map<string, WikiNote[]>>();
      for (const note of notes) {
        const hay = `${note.title} ${note.path} ${note.tags.join(" ")}`.toLowerCase();
        if (q && !hay.includes(q)) continue;
        const clusters = byVault.get(note.vault) ?? new Map<string, WikiNote[]>();
        const bucket = clusters.get(note.cluster) ?? [];
        bucket.push(note);
        clusters.set(note.cluster, bucket);
        byVault.set(note.vault, clusters);
      }

      if (byVault.size === 0) {
        list.appendChild(el("p", "dim", "Ничего не нашлось."));
        return;
      }

      for (const [vault, clusters] of byVault) {
        const vaultBox = el("section", "gdd-vault");
        vaultBox.appendChild(el("h3", null, vault === "gdd" ? "Дизайн (gdd)" : "Инженерия (tech)"));

        for (const [cluster, items] of clusters) {
          const box = el("details", "gdd-cluster");
          if (q) box.open = true;
          const head = el("summary");
          head.innerHTML =
            `<b>${cluster || "корень"}</b> <span>${CLUSTER_NAMES[cluster] ?? ""}</span>` +
            `<i>${items.length}</i>`;
          box.appendChild(head);

          for (const note of items) {
            const row = el("a", "gdd-note");
            // obsidian:// открывает заметку в самом vault — читать текст полагается там.
            row.href = `obsidian://open?path=${encodeURIComponent(note.path)}`;
            row.title = note.path;
            row.innerHTML =
              `<span class="t">${note.title}</span>` +
              (note.status ? `<em class="s-${note.status}">${note.status}</em>` : "") +
              `<span class="w">${note.words} сл.</span>`;
            box.appendChild(row);
          }
          vaultBox.appendChild(box);
        }
        list.appendChild(vaultBox);
      }
    };

    search.addEventListener("input", () => draw(search.value));
    draw("");
  };

  void wikiIndex.settled.then(paint);
}

const section: SectionDef = {
  id: "gdd",
  title: "Указатель ГДД",
  eyebrow: "Лаборатория · карта документации",
  transport: false,
  lede:
    "Карта дизайн-документации: кластеры, заголовки, статусы и объём. <b>Текста здесь нет " +
    "намеренно</b> — он живёт в vault и публикуется отдельно, а у факта должен быть один владелец. " +
    "Сайт отвечает на вопрос «где это лежит», а не пересказывает содержимое.",

  blocks: [
    {
      kind: "head", id: "map", title: "Что где лежит",
      lede: "Клик по строке открывает заметку в Obsidian. Поиск идёт по заголовку, пути и тегам."
    },
    { kind: "live", id: "tree", render },
    {
      kind: "note",
      html:
        "Статус <code>archive</code> значит «документ заморожен»: он пересказывает код, и правда " +
        "уехала в сам код. Читать такой док можно как замысел, но не как описание того, что есть."
    }
  ]
};

export default section;
