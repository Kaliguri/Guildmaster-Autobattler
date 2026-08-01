/* Реестр балансных проблем: что сломано, чем это видно, какие есть правки и что решил Макс.

   Данные — тот же `data.js`, что и у прогонов; генерацию не трогаем. Ведётся реестр в
   `docs/balance-issues.md`, оттуда его и забирает скрипт.

   Вердикт вынесен отдельной строкой и НИКОГДА не прячется: проблема без вердикта и проблема с
   вердиктом — разные состояния работы, и путать их дороже, чем показать лишнюю строку. */

import { el, html } from "../dom.js";
import type { SectionDef } from "../types.js";
import { balance, rich, statusOf, type Issue } from "./balance-data.js";

/** Статус пишется свободным текстом с уточнением после точки — класс берём по первому слову. */
const STATUS_CLASS: Record<string, string> = {
  "открыта": "st-open",
  "требует дизайна": "st-design",
  "решение принято": "st-design",
  "правка внесена": "st-applied",
  "закрыта": "st-closed",
  "отклонена": "st-closed"
};

function issueCard(issue: Issue): HTMLElement {
  const status = statusOf(issue);
  const box = el("article", `card issue ${STATUS_CLASS[status] ?? ""}`);

  const head = el("div", "i-head");
  head.appendChild(el("span", "i-code", issue.code));
  head.appendChild(el("h3", null, issue.title));
  box.appendChild(head);

  // Статус — отдельной строкой под заголовком: он бывает длиннее самого заголовка и содержит
  // markdown (ссылку на порождённую проблему, жирный), поэтому в шапку его втискивать нельзя.
  if (issue.status) {
    box.appendChild(html("p",
      rich(issue.status), `i-status ${status === "закрыта" || status === "отклонена" ? "good" : "warn"}`));
  }

  const body = el("div", "i-body");
  if (issue.symptom) body.appendChild(html("p", `<span class="lbl">Симптом. </span>${rich(issue.symptom)}`));
  if (issue.diagnosis) body.appendChild(html("p", `<span class="lbl">Диагноз. </span>${rich(issue.diagnosis)}`));

  if (issue.options?.length) {
    body.appendChild(html("p", '<span class="lbl">Варианты правки</span>'));
    const ol = el("ol");
    for (const opt of issue.options) ol.appendChild(html("li", rich(opt)));
    body.appendChild(ol);
  }

  const waiting = !issue.verdict || issue.verdict === "—";
  body.appendChild(el("div", `i-verdict${waiting ? " waiting" : ""}`,
    waiting ? "Вердикт Макса: ждёт." : `Вердикт Макса: ${issue.verdict}`));

  box.appendChild(body);
  return box;
}

function render(host: HTMLElement): void {
  const status = el("p", "dim", "читаю реестр…");
  host.appendChild(status);

  void balance.settled.then(() => {
    const issues = balance.data.issues;
    if (issues.length === 0) {
      status.textContent = balance.error
        ? `Реестр недоступен: ${balance.error}. Нужен ./scripts/lab-serve.ps1 -Watch`
        : "В реестре пусто.";
      return;
    }
    host.replaceChildren();

    const open = issues.filter((i) => !["закрыта", "отклонена"].includes(statusOf(i)));
    const closed = issues.filter((i) => ["закрыта", "отклонена"].includes(statusOf(i)));
    const noVerdict = open.filter((i) => !i.verdict || i.verdict === "—").length;

    host.appendChild(el("p", "dim",
      `${issues.length} проблем · ${open.length} в работе · ${closed.length} закрыто` +
      (noVerdict ? ` · ${noVerdict} ждут вердикта` : "")));

    const filters = el("div", "bal-tabs");
    const groups: Array<[string, Issue[]]> = [
      ["В работе", open],
      ["Ждут вердикта", open.filter((i) => !i.verdict || i.verdict === "—")],
      ["Закрытые", closed],
      ["Все", issues]
    ];
    const list = el("div", "issues");

    const show = (items: Issue[]): void => {
      list.replaceChildren();
      for (const issue of items) list.appendChild(issueCard(issue));
      if (items.length === 0) list.appendChild(el("p", "dim", "Пусто."));
    };

    groups.forEach(([label, items], i) => {
      const btn = el("button", null, `${label} · ${items.length}`);
      btn.type = "button";
      btn.dataset["active"] = String(i === 0);
      btn.addEventListener("click", () => {
        filters.querySelectorAll("button").forEach((b) => { b.dataset["active"] = "false"; });
        btn.dataset["active"] = "true";
        show(items);
      });
      filters.appendChild(btn);
    });

    host.append(filters, list);
    show(open);
  });
}

const section: SectionDef = {
  id: "balance-issues",
  title: "Реестр проблем",
  eyebrow: "Лаборатория · баланс",
  transport: false,
  lede:
    "Что в балансе сломано, чем это видно в замерах, какие есть правки и что по ним решено. " +
    "Реестр ведётся в <code>docs/balance-issues.md</code> — здесь он только показан.",

  blocks: [
    {
      kind: "head", id: "issues", title: "Проблемы и вердикты",
      lede:
        "Вердикт показывается всегда, даже когда его нет: «ждёт» — это состояние работы, а не " +
        "пустое место. Проблема без вердикта не может быть закрыта правкой."
    },
    { kind: "live", id: "issues-list", render }
  ]
};

export default section;
