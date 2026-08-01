/* Общие рисовалки стенда джуса: сцена, силуэт юнита, детерминированный шум, палитра.
   Один владелец на все страницы — иначе тело юнита разойдётся между разделами, и сравнивать
   эффекты станет нельзя. Специфика раздела живёт в его собственном файле (barrier.js и т.д.). */

/* ---------- палитра ---------- */

var COL = {
  white: "#FFFFFF", holo: "#4DF2FF", honey: "#FFCC33", mint: "#8CFFA6",
  brass: "#B8863B", muted: "#93805E", edge: "#3A2C1E", body: "#5A4A34", bodyLit: "#7A6544"
};

var RED = "255,72,72";        // вскрытое — красный у всех юнитов (единая система)
var SHIELD = "138,206,255";   // барьер

// Оттенки статусов: цвет говорит, ЧТО происходит. Палитра бьющего сюда не приходит —
// на теле цели важно состояние, а не автор (решение 2026-07-31/72).
var ST = {
  burn:   "255,146,48",
  poison: "132,214,92",
  frost:  "138,214,255",
  stun:   "255,212,72",
  mark:   "255,96,80",
  slow:   "104,164,216"
};

/* ---------- детерминированный шум ----------
   Math.random в стенде запрещён: два прогона обязаны выглядеть одинаково, иначе сравнение
   вариантов превращается в спор о том, что кому показалось. */

function jag(i, salt) {
  var x = Math.sin((i + 1) * 12.9898 + (salt || 0) * 78.233) * 43758.5453;
  return x - Math.floor(x);
}

/* ---------- сцена ----------
   Канвас держит логический размер W×H0, а на экране растягивается по ширине контейнера.
   Пересчёт кэшируется в _cw: без него каждый кадр дёргал бы layout. */

function stageMini(canvas, W, H0) {
  var dpr = Math.min(window.devicePixelRatio || 1, 2);
  var cssW = canvas.clientWidth || W;
  var sc = cssW / W;
  if (canvas._cw !== cssW) {
    canvas.style.height = (H0 * sc) + "px";
    canvas.width = Math.round(cssW * dpr);
    canvas.height = Math.round(H0 * sc * dpr);
    canvas._cw = cssW;
  }
  var ctx = canvas.getContext("2d");
  ctx.setTransform(canvas.width / W, 0, 0, canvas.width / W, 0, 0);
  ctx.clearRect(0, 0, W, H0);
  ctx.strokeStyle = "rgba(58,44,30,.7)";
  ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(30, H0 - 56); ctx.lineTo(W - 30, H0 - 56); ctx.stroke();
  return ctx;
}

function miniLabel(ctx, name) {
  ctx.font = "500 13px ui-monospace, Consolas, monospace";
  ctx.fillStyle = "rgba(147,128,94,.9)";
  ctx.fillText(name, 26, 30);
  ctx.fillStyle = COL.holo;
  ctx.fillRect(14, 21, 6, 6);
}

/* ---------- силуэт юнита ----------
   Путь нужен и для рисовки, и для обрезки эффекта «внутри тела»: материальное состояние
   не имеет права вылезать за силуэт. */

function unitPath(ctx, x, groundY, h) {
  var u = h / 16;
  ctx.beginPath();
  ctx.rect(x - u * 2, groundY - h, u * 4, u * 4.5);                    // голова
  ctx.rect(x - u * 2.6, groundY - h + u * 4.5, u * 5.2, u * 6.5);      // корпус
  ctx.rect(x - u * 2.2, groundY - u * 5, u * 1.8, u * 5);              // нога
  ctx.rect(x + u * 0.5, groundY - u * 5, u * 1.8, u * 5);              // нога
}

// Схематичный юнит блоками: намёк на пиксель-арт, без претензии на арт.
function drawUnit(ctx, x, groundY, h, facing, lit) {
  var u = h / 16;
  ctx.fillStyle = lit ? COL.bodyLit : COL.body;
  ctx.fillRect(x - u * 2, groundY - h, u * 4, u * 4.5);
  ctx.fillRect(x - u * 2.6, groundY - h + u * 4.5, u * 5.2, u * 6.5);
  ctx.fillRect(x - u * 2.2, groundY - u * 5, u * 1.8, u * 5);
  ctx.fillRect(x + u * 0.5, groundY - u * 5, u * 1.8, u * 5);
  ctx.fillStyle = "rgba(0,0,0,.35)";
  ctx.beginPath();
  ctx.ellipse(x, groundY + u * 0.6, u * 3.4, u * 1.1, 0, 0, Math.PI * 2);
  ctx.fill();
}

/* ---------- тело со статусами ----------
   Материальное состояние подаётся ОДНИМ куском, как BodyVisualState в движке: тинт, налёт и
   дрожь приходят вместе, а не тремя независимыми писателями. Возвращает фактический X —
   стан тело трясёт, и всё, что цепляется к телу, обязано трястись вместе с ним. */

function statusBody(ctx, x, groundY, H, o) {
  var u = H / 16;
  o = o || {};
  var bx = x + (o.stun ? (jag(Lab.tick, 41) - 0.5) * u * 0.5 : 0);

  drawUnit(ctx, bx, groundY, H, 1, false);

  ctx.save();
  unitPath(ctx, bx, groundY, H);
  ctx.clip();

  if (o.burn > 0) {
    ctx.fillStyle = "rgba(" + ST.burn + "," + (0.08 + 0.24 * o.burn).toFixed(3) + ")";
    ctx.fillRect(bx - u * 3, groundY - H, u * 6, H + u);
  }
  if (o.poison > 0) {
    ctx.fillStyle = "rgba(" + ST.poison + "," + (0.10 + 0.18 * o.poison).toFixed(3) + ")";
    ctx.fillRect(bx - u * 3, groundY - H, u * 6, H + u);
  }
  if (o.frostbite > 0) {                       // изморозь: налёт снизу вверх, высота = стаки
    var top = groundY - H * o.frostbite;
    ctx.fillStyle = "rgba(" + ST.frost + ",.30)";
    ctx.fillRect(bx - u * 3, top, u * 6, groundY - top);
    ctx.fillStyle = "rgba(214,244,255,.7)";
    ctx.fillRect(bx - u * 3, top, u * 6, 2);
  }
  ctx.restore();
  return bx;
}
