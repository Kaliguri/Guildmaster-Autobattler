/* Каркас локальных инструментов: шапка, навигация, якоря, оглавление, транспорт показа.
   Один владелец на все страницы — иначе шапка расползётся копиями и разойдётся на первой же правке.
   Обычный скрипт, не модуль: страницы открываются по file://, где ES-модули запрещены CORS-политикой.

   Страница объявляет себя через window.LAB_PAGE = { id, title, subtitle } ДО подключения shell.js. */

(function () {
  "use strict";

  var PAGES = [
    { id: "index",   href: "index.html",   label: "Витрина" },
    { id: "hits",    href: "hits.html",    label: "Удар" },
    { id: "status",  href: "status.html",  label: "Статусы" },
    { id: "barrier", href: "barrier.html", label: "Барьер" },
    { id: "zones",   href: "zones.html",   label: "Зоны" },
    { id: "legacy",  href: "legacy.html",  label: "Отклонённое" }
  ];

  var page = window.LAB_PAGE || { id: "index" };

  /* ---------- шапка ---------- */

  function buildBar() {
    var bar = document.createElement("header");
    bar.className = "lab-bar";

    var brand = document.createElement("a");
    brand.className = "lab-brand";
    brand.href = "index.html";
    brand.innerHTML = "<b>Guildmaster · Лаборатория</b><span>как мы это видим</span>";
    bar.appendChild(brand);

    var nav = document.createElement("nav");
    nav.className = "lab-nav";
    nav.setAttribute("aria-label", "Разделы");
    PAGES.forEach(function (p) {
      var a = document.createElement("a");
      a.href = p.href;
      a.textContent = p.label;
      if (p.id === page.id) a.setAttribute("aria-current", "page");
      nav.appendChild(a);
    });
    bar.appendChild(nav);

    document.body.insertBefore(bar, document.body.firstChild);
  }

  /* ---------- якоря: клик по решётке кладёт ссылку на РАЗДЕЛ в буфер ----------
     Ради этого весь заход и затевался: раньше ссылку на кусок стенда кинуть было нельзя. */

  function slugify(text, used) {
    var base = text.toLowerCase()
      .replace(/[^\wа-яё\s-]/gi, "")
      .trim()
      .replace(/\s+/g, "-")
      .slice(0, 48) || "section";
    var slug = base, n = 2;
    while (used[slug]) slug = base + "-" + n++;
    used[slug] = true;
    return slug;
  }

  function buildAnchors() {
    var used = {};
    // Идентификаторы, уже проставленные руками, занимают слот первыми: ссылки на них могли уйти в доки.
    Array.prototype.forEach.call(document.querySelectorAll("[id]"), function (el) { used[el.id] = true; });

    Array.prototype.forEach.call(document.querySelectorAll("main h2, main h3"), function (h) {
      if (!h.id) {
        var owner = h.closest("section");
        h.id = (owner && owner.id && h.tagName === "H2") ? owner.id : slugify(h.textContent, used);
      }
      var btn = document.createElement("button");
      btn.className = "anchor";
      btn.type = "button";
      btn.textContent = "#";
      btn.title = "Скопировать ссылку на этот раздел";
      btn.addEventListener("click", function () {
        var url = location.href.split("#")[0] + "#" + h.id;
        history.replaceState(null, "", "#" + h.id);
        var done = function () {
          btn.dataset.copied = "true";
          setTimeout(function () { btn.removeAttribute("data-copied"); }, 1400);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(url).then(done, done);
        } else done();   // file:// без разрешения на буфер: хеш в адресе всё равно обновлён
      });
      h.appendChild(btn);
    });
  }

  /* ---------- оглавление страницы: собирается из h2, руками не ведётся ---------- */

  function buildToc() {
    var host = document.querySelector("[data-toc]");
    if (!host) return;
    var items = document.querySelectorAll("main h2");
    if (items.length < 2) return;
    host.className = "page-toc";
    Array.prototype.forEach.call(items, function (h) {
      var a = document.createElement("a");
      a.href = "#" + h.id;
      a.textContent = h.firstChild ? h.firstChild.textContent.trim() : h.textContent.trim();
      host.appendChild(a);
    });
  }

  /* ---------- наверх ---------- */

  function buildToTop() {
    var btn = document.createElement("button");
    btn.className = "to-top";
    btn.type = "button";
    btn.textContent = "наверх";
    btn.addEventListener("click", function () { window.scrollTo({ top: 0, behavior: "smooth" }); });
    document.body.appendChild(btn);
    var sync = function () { btn.dataset.visible = String(window.scrollY > 600); };
    window.addEventListener("scroll", sync, { passive: true });
    sync();
  }

  /* ---------- транспорт и тикер показа ----------
     Время у стенда общее: показ идёт на 30 Гц, кадр 33 мс. Страницы подписываются через
     Lab.onRender — так у цикла один владелец, и покадровый разбор работает одинаково везде. */

  var FPS = 30;
  var listeners = [];
  var playing = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var speed = 1, acc = 0, last = 0;

  var Lab = window.Lab = {
    FPS: FPS,
    frame: 0,          // кадр внутри клипа атаки (0..TOTAL-1), владелец — страница удара
    tick: 0,           // монотонный кадр: статусы живут секундами и по кругу не ходят
    total: 30,
    contact: 16,
    onRender: function (fn) { listeners.push(fn); },
    onStep: null,      // страница может повесить свой шаг (например запись хита)
    isPlaying: function () { return playing; }
  };

  function step(ts) {
    if (!last) last = ts;
    var dt = ts - last;
    last = ts;
    if (playing) {
      acc += dt * speed;
      var per = 1000 / FPS;
      while (acc >= per) {
        acc -= per;
        Lab.frame = (Lab.frame + 1) % Lab.total;
        Lab.tick++;
        if (typeof Lab.onStep === "function") Lab.onStep(Lab.frame);
      }
    }
    render();
    requestAnimationFrame(step);
  }

  function render() {
    for (var i = 0; i < listeners.length; i++) listeners[i]();
    var out = document.getElementById("out-frame");
    if (out) out.textContent = String(Lab.frame).padStart(2, "0");
  }

  function buildTransport() {
    var host = document.querySelector("[data-transport]");
    if (!host) return;
    host.className = "transport";
    host.setAttribute("role", "group");
    host.setAttribute("aria-label", "Управление показом");
    host.innerHTML =
      '<button id="btn-play" type="button"></button>' +
      '<button id="btn-prev" type="button">&#8249; кадр</button>' +
      '<button id="btn-next" type="button">кадр &#8250;</button>' +
      '<label class="tag" for="speed" style="display:flex;align-items:center;gap:.6rem">темп' +
      '<input id="speed" type="range" min="10" max="100" value="100" step="10" aria-label="Темп показа">' +
      '<span id="speed-out" style="font-family:var(--mono);color:var(--text)">100%</span></label>' +
      '<div class="readout"><span>кадр <b id="out-frame">00</b>/' + (Lab.total - 1) + "</span></div>";

    var btnPlay = host.querySelector("#btn-play");
    var syncPlay = function () {
      btnPlay.textContent = playing ? "Пауза" : "Играть";
      btnPlay.dataset.active = String(!playing);
    };
    syncPlay();
    btnPlay.addEventListener("click", function () { playing = !playing; syncPlay(); });

    host.querySelector("#btn-prev").addEventListener("click", function () {
      playing = false; syncPlay();
      Lab.frame = (Lab.frame - 1 + Lab.total) % Lab.total;
      Lab.tick = Math.max(0, Lab.tick - 1);
      render();
    });
    host.querySelector("#btn-next").addEventListener("click", function () {
      playing = false; syncPlay();
      Lab.frame = (Lab.frame + 1) % Lab.total;
      Lab.tick++;
      if (typeof Lab.onStep === "function") Lab.onStep(Lab.frame);
      render();
    });

    var speedEl = host.querySelector("#speed");
    var speedOut = host.querySelector("#speed-out");
    speedEl.addEventListener("input", function () {
      speed = Number(speedEl.value) / 100;
      speedOut.textContent = speedEl.value + "%";
    });
  }

  /* ---------- старт ---------- */

  function boot() {
    buildBar();
    buildAnchors();
    buildToc();
    buildTransport();
    buildToTop();
    window.addEventListener("resize", function () {
      Array.prototype.forEach.call(document.querySelectorAll("canvas"), function (c) { c._cw = 0; c._cssW = 0; });
      render();
    });
    render();
    requestAnimationFrame(step);
    // Переход по ссылке с якорем: браузер прыгает до того, как шапка стала sticky-высотой.
    if (location.hash) {
      var target = document.querySelector(location.hash);
      if (target) setTimeout(function () { target.scrollIntoView(); }, 0);
    }
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
