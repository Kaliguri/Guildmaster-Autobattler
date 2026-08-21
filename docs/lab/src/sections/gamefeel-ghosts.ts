/* Призрачные копии тела: шлейф за рывком и удар по иллюзии на уклонении.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Призрачные копии, journal-adr 2026-08-21/6.

   Копия — один приём на два события, поэтому оба разбираются в одном разделе: если они разъедутся
   по виду, игрок прочтёт их как две разные магии. Цвет копии здесь — свет самого юнита (медовый);
   холодный «призрачный» показан отдельной вариацией, потому что он спорит с правилом цвета. */

import { frame, tick, TOTAL, CONTACT } from "../clock.js";
import { COL, drawUnit, ground, jag, unitPath } from "../draw.js";
import type { DrawFn, SectionDef, StandDef } from "../types.js";

/** Свет юнита: цвет копии берётся с него, а не назначается эффекту. */
const OWN = "255,204,51";
/** Холодный призрак — альтернатива под сравнение, а не канон. */
const COLD = "77,242,255";

const UH = 96;          // рост тела на сцене
const DASH_PERIOD = 75; // рывок раз в 2.5 с
const DASH_TICKS = 12;  // сам рывок — 0.4 с

/* ---------- общее ---------- */

/** Фаза рывка: -1 = стоит, иначе доля 0..1 от начала до конца броска. */
function dashPhase(): number {
  const t = tick % DASH_PERIOD;
  return t < DASH_TICKS ? t / DASH_TICKS : -1;
}

/** Путь рывка: слева направо с замедлением к концу — так читается «оттолкнулся и приехал». */
function dashX(w: number, p: number): number {
  const e = 1 - Math.pow(1 - p, 2);
  return w * 0.22 + (w * 0.56) * e;
}

/**
 * Одна копия тела. Форма — тот же силуэт, что у юнита: копия обязана читаться как он сам, поэтому
 * своей геометрии у неё нет.
 */
function ghost(
  ctx: CanvasRenderingContext2D,
  x: number,
  groundY: number,
  alpha: number,
  rgb: string,
  mode: "fill" | "holo" | "outline" = "fill"
): void {
  if (alpha <= 0.004) return;
  const u = UH / 16;

  ctx.save();
  ctx.globalCompositeOperation = "lighter";

  if (mode === "outline") {
    ctx.strokeStyle = `rgba(${rgb},${alpha.toFixed(3)})`;
    ctx.lineWidth = 1.6;
    unitPath(ctx, x, groundY, UH);
    ctx.stroke();
    ctx.restore();
    return;
  }

  ctx.fillStyle = `rgba(${rgb},${(alpha * 0.55).toFixed(3)})`;
  unitPath(ctx, x, groundY, UH);
  ctx.fill();

  if (mode === "holo") {
    // Сканлайны — тот же режим шейдера, которым тело развоплощается в смерти.
    ctx.save();
    unitPath(ctx, x, groundY, UH);
    ctx.clip();
    ctx.fillStyle = `rgba(${rgb},${(alpha * 0.5).toFixed(3)})`;
    for (let y = groundY - UH; y < groundY; y += u * 0.9) ctx.fillRect(x - u * 3, y, u * 6, u * 0.35);
    ctx.restore();
  }

  ctx.restore();
}

/** Подпись сцены — одинаковая у всех стендов, чтобы сравнивались эффекты, а не оформление. */
function label(ctx: CanvasRenderingContext2D, text: string, h: number): void {
  ctx.font = "500 11px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.85)";
  ctx.fillText(text, 16, h - 18);
}

/* ---------- А. шлейф за рывком ---------- */

/**
 * Сцена шлейфа: копии снимаются по ходу броска через равные доли, каждая гаснет за свою жизнь.
 * Разница между вариациями — только в трёх числах и режиме копии, ровно как в feel-конфиге.
 */
function trail(count: number, life: number, mode: "fill" | "holo" | "outline", rgb: string, tail = false): DrawFn {
  return (ctx, w, h) => {
    const groundY = ground(ctx, w, h, 62);
    const p = dashPhase();

    if (p < 0) {
      drawUnit(ctx, w * 0.22, groundY, UH);
      label(ctx, "ждёт рывка", h);
      return;
    }

    const x = dashX(w, p);

    if (tail) {
      // Смазанная лента между копиями: след как одна фигура, а не как цепочка отдельных тел.
      const from = dashX(w, Math.max(0, p - life));
      const g = ctx.createLinearGradient(from, 0, x, 0);
      g.addColorStop(0, `rgba(${rgb},0)`);
      g.addColorStop(1, `rgba(${rgb},.22)`);
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      ctx.fillStyle = g;
      ctx.fillRect(Math.min(from, x), groundY - UH * 0.82, Math.abs(x - from), UH * 0.62);
      ctx.restore();
    }

    for (let i = 1; i <= count; i++) {
      const back = p - (life / count) * i;
      if (back < 0) continue;
      const age = (p - back) / life;         // 0 — только что снята, 1 — отжила
      ghost(ctx, dashX(w, back), groundY, 1 - age, rgb, mode);
    }

    drawUnit(ctx, x, groundY, UH, true);
    label(ctx, `копий ${count} · жизнь ${(life * DASH_TICKS / 30).toFixed(2)} с`, h);
  };
}

const TRAIL_STANDS: StandDef[] = [
  {
    id: "trail-sparse",
    status: "waiting",
    title: "А1 · Редкие копии",
    note: "Три копии за бросок, каждая живёт треть пути. Читается как «был там, там и там».",
    facts: [["копий разом", "3"], ["интервал", "~0.13 с"], ["режим", "заливка"]],
    verdict: "Дёшево и разборчиво, но на коротком рывке успевает встать одна копия.",
    decision: "2026-08-21/6",
    draw: trail(3, 0.9, "fill", OWN)
  },
  {
    id: "trail-dense",
    status: "waiting",
    title: "А2 · Густой шлейф",
    note: "Восемь копий, каждая живёт недолго. Ближе к «мазку», чем к цепочке.",
    facts: [["копий разом", "8"], ["интервал", "~0.05 с"], ["режим", "заливка"]],
    verdict: "Плотнее и «магичнее», но восемь тел по шестнадцать частей — самая дорогая вариация.",
    decision: "2026-08-21/6",
    draw: trail(8, 0.85, "fill", OWN)
  },
  {
    id: "trail-smear",
    status: "waiting",
    title: "А3 · Копии плюс лента",
    note: "Четыре копии и смазанная полоса между ними: след — одна фигура, а не пунктир из тел.",
    facts: [["копий разом", "4"], ["лента", "градиент по пути"], ["режим", "заливка"]],
    verdict: "Единственная вариация, где рывок читается как ДВИЖЕНИЕ, а не как серия поз.",
    decision: "2026-08-21/6",
    draw: trail(4, 0.95, "fill", OWN, true)
  },
  {
    id: "trail-holo",
    status: "waiting",
    title: "А4 · Копии голограммой",
    note: "Тот же режим шейдера, которым тело развоплощается в смерти: копия просвечивает полосами.",
    facts: [["копий разом", "5"], ["режим", "голограмма"], ["цвет", "свет юнита"]],
    verdict: "Сильнее говорит «это не тело», но полосы на мелком юните мылят силуэт.",
    decision: "2026-08-21/6",
    draw: trail(5, 0.9, "holo", OWN)
  },
  {
    id: "trail-cold",
    status: "waiting",
    title: "А5 · Холодный призрак",
    tag: "спорит с правилом цвета",
    note: "Копия уходит в голубой вместо света юнита. Показано ради сравнения: правило палитры " +
          "говорит, что цвет несёт «чей это юнит», а нематериальность — прозрачность.",
    facts: [["копий разом", "5"], ["цвет", "холодный, общий"], ["режим", "контур"]],
    verdict: "Читается мгновенно, но у всех юнитов одинаково: рывок перестаёт быть ИХ рывком.",
    decision: "2026-08-21/6",
    draw: trail(5, 0.9, "outline", COLD)
  }
];

/* ---------- Б. удар по иллюзии ---------- */

/** Серп: короткий росчерк по цели. Своя рисовалка, потому что стенду хватает знака формы. */
function slash(ctx: CanvasRenderingContext2D, x: number, groundY: number, k: number): void {
  if (k <= 0 || k >= 1) return;
  const u = UH / 16;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.strokeStyle = `rgba(255,255,255,${(1 - k).toFixed(3)})`;
  ctx.lineWidth = 3 * (1 - k) + 1;
  ctx.beginPath();
  ctx.arc(x, groundY - UH * 0.55, u * 4.2, -Math.PI * 0.75, Math.PI * 0.05);
  ctx.stroke();
  ctx.restore();
}

/**
 * Сцена уклонения: слева бьющий, справа цель. На кадре контакта тело выпадает из точки, а копия
 * остаётся и принимает удар — дальше каждая вариация разрушает её по-своему.
 */
function illusion(kind: "shatter" | "ripple" | "glitch" | "puff" | "none", moves = true): DrawFn {
  return (ctx, w, h) => {
    const groundY = ground(ctx, w, h, 62);
    const tx = w * 0.62;
    const after = frame - CONTACT;
    const k = after <= 0 ? -1 : after / (TOTAL - CONTACT); // 0..1 после попадания

    drawUnit(ctx, w * 0.28, groundY, UH * 0.95);           // бьющий: он свой удар доводит всегда

    if (k < 0) {
      drawUnit(ctx, tx, groundY, UH);
      label(ctx, "замах", h);
      return;
    }

    // Живое тело: ушло вбок (или осталось на месте — «Уклонение» тело не двигает).
    const bodyX = moves ? tx + w * 0.16 * (1 - Math.pow(1 - Math.min(1, k * 2.2), 2)) : tx;
    drawUnit(ctx, bodyX, groundY, UH, true);

    const u = UH / 16;
    const fade = 1 - k;

    if (kind === "none") {
      ctx.font = "500 13px ui-monospace, Consolas, monospace";
      ctx.fillStyle = `rgba(217,226,235,${fade.toFixed(3)})`;
      ctx.fillText("evade", tx - 18, groundY - UH - 8 - k * 14);
      label(ctx, "как сейчас: только надпись", h);
      return;
    }

    slash(ctx, tx, groundY, k);

    if (kind === "shatter") {
      // Копия осыпается кусками — тот же язык, что у разлёта на осколки в смерти.
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      for (let i = 0; i < 14; i++) {
        const sx = tx + (jag(i, 11) - 0.5) * u * 5.2;
        const sy = groundY - UH * (0.15 + jag(i, 12) * 0.8);
        const dx = (jag(i, 13) - 0.5) * u * 7 * k;
        const dy = u * 6 * k * k;
        ctx.fillStyle = `rgba(${OWN},${(fade * 0.8).toFixed(3)})`;
        ctx.fillRect(sx + dx, sy + dy, u * 0.9, u * 0.9);
      }
      ctx.restore();
      ghost(ctx, tx, groundY, fade * 0.5, OWN);
      label(ctx, "копия осыпается осколками", h);
      return;
    }

    if (kind === "ripple") {
      // Копия расходится кругами от точки удара — «удар пришёл в воду».
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      for (let r = 0; r < 3; r++) {
        const rk = Math.min(1, k * 1.6 - r * 0.18);
        if (rk <= 0) continue;
        ctx.strokeStyle = `rgba(${OWN},${((1 - rk) * 0.6).toFixed(3)})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.ellipse(tx, groundY - UH * 0.55, u * 3 + rk * u * 7, u * 4 + rk * u * 8, 0, 0, Math.PI * 2);
        ctx.stroke();
      }
      ctx.restore();
      ghost(ctx, tx, groundY, fade * 0.8, OWN, "holo");
      label(ctx, "копия расходится рябью", h);
      return;
    }

    if (kind === "glitch") {
      // Копия разъезжается горизонтальными полосами: «изображение», а не тело.
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      ctx.beginPath();
      unitPath(ctx, tx, groundY, UH);
      ctx.clip();
      for (let i = 0; i < 10; i++) {
        const y = groundY - UH + (UH / 10) * i;
        const off = (jag(i, 21) - 0.5) * u * 10 * k;
        ctx.fillStyle = `rgba(${OWN},${(fade * 0.55).toFixed(3)})`;
        ctx.fillRect(tx - u * 3 + off, y, u * 6, UH / 10 - 1);
      }
      ctx.restore();
      label(ctx, "копия разъезжается полосами", h);
      return;
    }

    // puff: копия схлопывается в облачко — самый мягкий вариант.
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    for (let i = 0; i < 9; i++) {
      const a = (i / 9) * Math.PI * 2;
      const rr = u * (1.5 + k * 5 + jag(i, 31) * 2);
      ctx.fillStyle = `rgba(${OWN},${(fade * 0.35).toFixed(3)})`;
      ctx.beginPath();
      ctx.arc(tx + Math.cos(a) * rr, groundY - UH * 0.55 + Math.sin(a) * rr * 0.8, u * (1.2 + k * 1.4), 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
    ghost(ctx, tx, groundY, fade * 0.35, OWN);
    label(ctx, "копия схлопывается в дымку", h);
  };
}

const ILLUSION_STANDS: StandDef[] = [
  {
    id: "illusion-now",
    status: "note",
    title: "Как сейчас",
    tag: "ситуация",
    note: "Уклонение показывается словом «evade» и больше ничем. Кит отработал главную способность, " +
          "а на экране не произошло ничего.",
    draw: illusion("none")
  },
  {
    id: "illusion-shatter",
    status: "waiting",
    title: "Б1 · Копия осыпается",
    note: "Удар доводится по копии, она разлетается кусками — тот же язык, что у смерти.",
    facts: [["форма удара", "как обычно"], ["распад", "осколками"], ["длительность", "~0.45 с"]],
    verdict: "Самый «весомый» вариант: удар выглядит состоявшимся. Риск спутать с настоящей смертью.",
    decision: "2026-08-21/6",
    draw: illusion("shatter")
  },
  {
    id: "illusion-ripple",
    status: "waiting",
    title: "Б2 · Рябь по копии",
    note: "Копия расходится кругами от точки удара: било по отражению, а не по телу.",
    facts: [["форма удара", "как обычно"], ["распад", "волной наружу"], ["режим копии", "голограмма"]],
    verdict: "Яснее всех говорит «это была не он». Мягче по весу — уклонение читается как магия.",
    decision: "2026-08-21/6",
    draw: illusion("ripple")
  },
  {
    id: "illusion-glitch",
    status: "waiting",
    title: "Б3 · Полосы",
    note: "Копия разъезжается горизонтальными полосами — язык «цифровой подмены» арены.",
    facts: [["форма удара", "как обычно"], ["распад", "сдвигом полос"], ["родство", "цифровой переход"]],
    verdict: "Ложится на цифровую линию мира, но у обычного мечника выглядит инородно.",
    decision: "2026-08-21/6",
    draw: illusion("glitch")
  },
  {
    id: "illusion-puff",
    status: "waiting",
    title: "Б4 · Дымка",
    note: "Копия схлопывается облачком. Самый тихий вариант из четырёх.",
    facts: [["форма удара", "как обычно"], ["распад", "облачком"], ["вес", "низкий"]],
    verdict: "Не спорит ни с чем на экране, но и уклонение почти не празднует.",
    decision: "2026-08-21/6",
    draw: illusion("puff")
  },
  {
    id: "illusion-standing",
    status: "waiting",
    title: "Б5 · Тело осталось на месте",
    tag: "случай",
    note: "«Уклонение» (каждая X-я атака мимо) тело не двигает. Копия всё равно остаётся в точке, " +
          "и удар проходит по ней — иначе этот вид уклонения объяснить на экране нечем.",
    facts: [["тело", "стоит"], ["копия", "в той же точке"], ["распад", "рябью"]],
    verdict: "Проверка решения «иллюзия на всех уклонениях»: смотреть, не читается ли как ошибка.",
    decision: "2026-08-21/6",
    draw: illusion("ripple", false)
  }
];

const section: SectionDef = {
  id: "ghosts",
  title: "Призрачные копии",
  lede: "Один приём на два события: шлейф за рывком и удар по иллюзии на уклонении. " +
        "Копия — поза тела, замороженная в момент и гаснущая на месте.",
  blocks: [
    {
      kind: "head",
      id: "trail",
      title: "Шлейф за рывком",
      lede: "Тянется только за СВОИМ движением: кувырок, телепорт, рывок способности. " +
            "За телом, которое унесло чужим толчком, шлейфа нет — копия говорит о намерении."
    },
    {
      kind: "text",
      html: "Длину хвоста задают два числа: как часто снимается копия и сколько она живёт. " +
            "Их частное и есть «сколько копий видно разом» — на стендах ниже это первая строка фактов."
    },
    { kind: "stands", items: TRAIL_STANDS },
    {
      kind: "head",
      id: "illusion",
      title: "Удар по иллюзии",
      lede: "Тело выпадает из точки, копия остаётся, и удар доводится по ней обычным порядком: " +
            "ядро, серп, искры. Это единственное исключение из правила «нет контакта — нет серпа»."
    },
    {
      kind: "note",
      html: "Первая карточка — <b>как сейчас</b>: уклонение показано словом «evade» и больше ничем. " +
            "Сравнивать варианты имеет смысл именно с ней."
    },
    { kind: "stands", items: ILLUSION_STANDS }
  ]
};

export default section;
