/* Барьер: форма купола, узор трещин, стопка оболочек по типам, финалы и блок.
   Вынесено из монолитного стенда 2026-08-01. Общие рисовалки — draw.js, время и транспорт — shell.js.
   Канон решений: docs/wiki/gdd/70-gamefeel/vfx-language.md §Барьер. */

(function () {
  "use strict";

  var PW = 260, PH = 220;   // размер превью-сцены, общий с остальными разделами

  /* ---------- форма купола: круглая, без углов ---------- */

  // Одна параметризация на все три формы: контур, узор трещин и осколки обязаны жить в одной
  // геометрии, иначе трещины пойдут мимо поверхности.
  // n — показатель суперэллипса (2 = эллипс, больше = прямее бока), taper — сужение к верху.
  const DOME = {
    ellipse: { n: 2.0,  taper: 0.00, label: "эллипс" },
    dome:    { n: 2.15, taper: 0.26, label: "купол-овоид" },
    cocoon:  { n: 2.7,  taper: 0.10, label: "кокон" }
  };

  function domePoint(kind, cx, cy, rx, ry, a, r) {
    const d = DOME[kind] || DOME.dome;
    if (r === undefined) r = 1;
    const ca = Math.cos(a), sa = Math.sin(a);
    // Суперэллипс: |x|^n + |y|^n = 1. При n=2 это обычный эллипс, без углов при любом n.
    const k = Math.pow(Math.pow(Math.abs(ca), d.n) + Math.pow(Math.abs(sa), d.n), -1 / d.n);
    let x = ca * k, y = sa * k;
    x *= 1 - d.taper * Math.max(0, -y);              // сужение к верху: облегает голову
    return { x: cx + x * rx * r, y: cy + y * ry * r };
  }

  function domePath(ctx, kind, cx, cy, rx, ry, r) {
    ctx.beginPath();
    const steps = 48;
    for (let i = 0; i <= steps; i++) {
      const p = domePoint(kind, cx, cy, rx, ry, (i / steps) * Math.PI * 2 - Math.PI / 2, r);
      if (i === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
    }
    ctx.closePath();
  }

  /* ---------- узор трещин: заготовлен целиком, проявляется частями ---------- */

  // Трещины барьера — НЕ счётные события (в отличие от порезов на теле): у барьера смысл «насколько
  // цел», то есть площадь, а не счёт. Поэтому узор генерируется целиком при рождении щита, а показ
  // проявляет его долю. Заодно снимается лимит: двадцатый удар не вытесняет первую трещину.
  const CRACK_SEGMENTS = 16;
  const _patternCache = {};

  function crackPattern(seed) {
    if (_patternCache[seed]) return _patternCache[seed];
    const segs = [];
    for (let i = 0; i < CRACK_SEGMENTS; i++) {
      const ang = (i / CRACK_SEGMENTS) * 360 + (jag(i, seed) - 0.5) * 18;
      const pts = [];
      let r = 1, a = ang * Math.PI / 180;                 // от кромки внутрь, с ветвлением
      pts.push({ r: r, a: a });
      const steps = 2 + Math.floor(jag(i, seed + 3) * 3);
      for (let s = 0; s < steps; s++) {
        a += (jag(i * 5 + s, seed + 7) - 0.5) * 0.9;
        r -= 0.16 + jag(i * 5 + s, seed + 11) * 0.2;
        if (r < 0.12) break;
        pts.push({ r: r, a: a });
      }
      segs.push({ ang: ang, pts: pts });
    }
    _patternCache[seed] = segs;
    return segs;
  }

  // Порядок проявления идёт ОТ ТОЧЕК УДАРА, а не по индексу: узор остаётся связным, но помнит,
  // откуда били. Без этого сегменты вылезали бы в произвольных местах.
  function crackOrder(segs, hitAngles) {
    const scored = segs.map(function (s, i) {
      let best = 180;
      for (let k = 0; k < hitAngles.length; k++) {
        const d = Math.abs(((s.ang - hitAngles[k] + 540) % 360) - 180);
        if (d < best) best = d;
      }
      return { i: i, score: best };
    });
    scored.sort(function (a, b) { return a.score - b.score || a.i - b.i; });
    return scored.map(function (s) { return s.i; });
  }

  /// Геометрия узора монотонна (история), яркость — текущая целостность: сеть широкая и бледная
  /// читается как «его сильно били, но сейчас он крепкий». Убирать проявленное нельзя — трещины не
  /// заживают; поэтому добавленный щит гасит узор, а не стирает.
  /// Узор живёт в геометрии купола (та же <see cref="domePoint"/>), иначе трещины поедут мимо поверхности.
  function drawPattern(ctx, kind, cx, cy, rx, ry, seed, shown, hitAngles, color, alpha) {
    if (shown <= 0) return;
    const segs = crackPattern(seed);
    const order = crackOrder(segs, hitAngles.length ? hitAngles : [-90]);
    const count = Math.min(segs.length, Math.max(1, Math.round(shown * segs.length)));
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    ctx.lineCap = "round";
    for (let k = 0; k < count; k++) {
      const s = segs[order[k]];
      const edge = k / Math.max(1, count - 1);            // последние проявленные ещё тонкие
      ctx.strokeStyle = "rgba(" + color + "," + (alpha * (1 - edge * 0.35)).toFixed(3) + ")";
      ctx.lineWidth = 1.7 - edge * 0.5;
      ctx.beginPath();
      for (let p = 0; p < s.pts.length; p++) {
        const pt = s.pts[p];
        const q = domePoint(kind, cx, cy, rx, ry, pt.a, pt.r);
        if (p === 0) ctx.moveTo(q.x, q.y); else ctx.lineTo(q.x, q.y);
      }
      ctx.stroke();
    }
    ctx.restore();
  }

  /* ---------- поверхность купола: в покое силуэт, под удар волна ---------- */

  // Яркость участка поверхности. В покое — почти ноль (силуэт). Под удар пятно в точке контакта
  // расходится волной: чем слабее остаточная вспышка, тем шире фронт.
  function domeGlow(angDeg, st, base) {
    if (st.glow <= 0) return base;
    if (st.hitAng === null) return base + st.glow * 0.42;          // урон эффектом: ровно по всей
    const d = Math.abs(((angDeg - st.hitAng + 540) % 360) - 180);
    const spread = 52 + (1 - Math.min(1, st.glow)) * 115;
    const near = Math.max(0, 1 - d / spread);
    return base + st.glow * (0.18 + 0.82 * near * near);
  }

  function domeSurface(ctx, kind, cx, cy, rx, ry, color, st, base, groundY) {
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    if (groundY !== undefined) {                                   // низ подрезан землёй: купол стоит
      ctx.beginPath();
      ctx.rect(cx - rx * 2.5, cy - ry * 2.5, rx * 5, groundY - (cy - ry * 2.5));
      ctx.clip();
    }

    domePath(ctx, kind, cx, cy, rx, ry);                           // объём: едва-едва, тело важнее
    ctx.fillStyle = "rgba(" + color + "," + (base * 0.55 + st.glow * 0.09).toFixed(3) + ")";
    ctx.fill();

    const steps = 64;                                              // контур сегментами, каждый со своей яркостью
    ctx.lineWidth = 1.8;
    for (let i = 0; i < steps; i++) {
      const a0 = (i / steps) * Math.PI * 2 - Math.PI / 2;
      const a1 = ((i + 1) / steps) * Math.PI * 2 - Math.PI / 2;
      const mid = ((a0 + a1) / 2) * 180 / Math.PI;
      const al = domeGlow(mid, st, base);
      const p0 = domePoint(kind, cx, cy, rx, ry, a0);
      const p1 = domePoint(kind, cx, cy, rx, ry, a1);
      ctx.strokeStyle = "rgba(" + color + "," + Math.min(1, al * 2.4).toFixed(3) + ")";
      ctx.beginPath(); ctx.moveTo(p0.x, p0.y); ctx.lineTo(p1.x, p1.y); ctx.stroke();
    }

    if (st.glow > 0 && st.hitAng !== null) {                       // само место контакта
      const p = domePoint(kind, cx, cy, rx, ry, st.hitAng * Math.PI / 180);
      const pr = rx * (0.3 + 0.4 * st.glow);
      const g = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, pr);
      g.addColorStop(0, "rgba(236,250,255," + (0.7 * st.glow).toFixed(3) + ")");
      g.addColorStop(1, "rgba(" + color + ",0)");
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(p.x, p.y, pr, 0, Math.PI * 2); ctx.fill();
    }
    ctx.restore();
  }

  // Пробитие: скорлупа расходится дугами ПО УЗОРУ — узор задаёт линии разлома, поэтому осколки
  // выглядят как куски именно этой поверхности, а не как случайные обломки.
  // Разлёт РАДИАЛЬНЫЙ — каждый кусок уходит прочь от юнита, наружу по своему углу (решение Макса
  // 31.07.2026). Направления «в одну сторону от последнего удара» у слома нет: барьер лопается
  // целиком, а не сдувается. Гаснут быстро, по прозрачности.
  function domeShards(ctx, kind, cx, cy, rx, ry, t, hitAng, color) {
    const fade = Math.pow(1 - t, 1.7);                   // быстрое угасание: скорлупы уже нет
    const pieces = 10;
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    ctx.lineCap = "round";
    ctx.lineWidth = 1.8;
    for (let i = 0; i < pieces; i++) {
      const a0 = (i / pieces) * Math.PI * 2 - Math.PI / 2;
      const a1 = ((i + 1) / pieces) * Math.PI * 2 - Math.PI / 2;
      const mid = (a0 + a1) / 2;
      const d = rx * 0.85 * t * (0.7 + jag(i, 82) * 0.6);
      const ox = Math.cos(mid) * d;
      const oy = Math.sin(mid) * d * (ry / rx);          // наружу по своему углу, с той же сплюснутостью
      ctx.strokeStyle = "rgba(" + color + "," + (0.9 * fade).toFixed(3) + ")";
      ctx.beginPath();
      for (let s = 0; s <= 5; s++) {
        const p = domePoint(kind, cx, cy, rx, ry, a0 + (a1 - a0) * (s / 5));
        if (s === 0) ctx.moveTo(p.x + ox, p.y + oy); else ctx.lineTo(p.x + ox, p.y + oy);
      }
      ctx.stroke();
    }
    ctx.restore();
  }

  /* ---------- стопка оболочек: тип, очередь, все ситуации ---------- */

  const SHELL_COLOR = { plain: "176,182,190", phys: "255,150,60", mag: "196,124,255" };
  const SHELL_DEPTH = { plain: 0, mag: 1, phys: 2 };      // вложенность = очередь трат, школьные снаружи

  // Кейсы: набор оболочек плюс лента событий. Урон типа T ест сначала школьную оболочку этого типа,
  // потом обычную — тот же порядок, что в симуляции (pre-damage раньше вычета общего пула).
  const CASES = {
    calm:    { cycle: 60,  shells: [{ t: "plain", nom: 200 }], ev: [] },
    weak:    { cycle: 80,  shells: [{ t: "plain", nom: 200 }], ev: [{ f: 24, dmg: 25, type: "phys", ang: -168 }] },
    strong:  { cycle: 80,  shells: [{ t: "plain", nom: 200 }], ev: [{ f: 24, dmg: 120, type: "phys", ang: -22 }] },
    dot:     { cycle: 80,  shells: [{ t: "plain", nom: 200 }], ev: [{ f: 22, dmg: 18, dot: true }, { f: 52, dmg: 18, dot: true }] },
    two:     { cycle: 90,  shells: [{ t: "plain", nom: 150 }, { t: "mag", nom: 120 }],
               ev: [{ f: 26, dmg: 60, type: "mag", ang: -150 }] },
    three:   { cycle: 100, shells: [{ t: "plain", nom: 150 }, { t: "mag", nom: 120 }, { t: "phys", nom: 100 }],
               ev: [{ f: 30, dmg: 55, type: "phys", ang: -40 }] },
    wrong:   { cycle: 90,  shells: [{ t: "mag", nom: 200 }], ev: [{ f: 28, dmg: 70, type: "phys", ang: -135, toBody: true }] },
    stack:   { cycle: 70,  shells: [{ t: "mag", nom: 480, stacks: 4 }], ev: [{ f: 26, dmg: 90, type: "mag", ang: -95 }] },
    layer:   { cycle: 130, shells: [{ t: "plain", nom: 150 }, { t: "phys", nom: 60 }],
               ev: [{ f: 24, dmg: 40, type: "phys", ang: -60 }, { f: 54, dmg: 40, type: "phys", ang: -70 },
                    { f: 90, dmg: 50, type: "phys", ang: -110 }] },
    through: { cycle: 100, shells: [{ t: "plain", nom: 20 }], ev: [{ f: 30, dmg: 100, type: "phys", ang: -50, toBody: true }] },
    expire:  { cycle: 110, shells: [{ t: "plain", nom: 200 }],
               ev: [{ f: 22, dmg: 70, type: "phys", ang: -150 }, { f: 60, expire: "plain" }] },
    addover: { cycle: 120, shells: [{ t: "plain", nom: 100 }],
               ev: [{ f: 20, dmg: 80, type: "phys", ang: -140 }, { f: 56, add: { t: "phys", nom: 200 } }] },

    // Блок — тот же барьер, только на 0.4 с и с жестом: BlockComponent накладывает короткий щит,
    // который гасит тот самый удар. Узор проявиться не успевает, и это и есть различитель.
    block:    { cycle: 96, shells: [{ t: "plain", nom: 60, born: 22 }],
                ev: [{ f: 24, dmg: 45, type: "phys", ang: -150, gesture: true }, { f: 36, expire: "plain" }] },
    // Удар со спины: сим блок направлением не ограничивает, поэтому урон погашен, а жеста нет —
    // рисовать «подставил щит» значило бы соврать про разворот.
    // Блок стал направленным: удар со спины его не будит вовсе, поэтому оболочка не поднимается и
    // урон идёт в тело. Жест теперь честен всегда — он есть ровно там, где блок сработал.
    backstab: { cycle: 96, shells: [], note: "блок не сработал · со спины",
                ev: [{ f: 26, dmg: 60, type: "phys", ang: 25 }] }
  };

  function runCase(def, c) {
    const shells = def.shells.map(function (s, i) {
      return { t: s.t, nom: s.nom, rest: s.nom, eaten: 0, shown: 0, hits: [], glow: 0,
               hitAng: null, dot: false, broken: -1, seed: 31 + i * 7, stacks: s.stacks || 1,
               gone: false, born: s.born || 0, unborn: c < (s.born || 0) };
    });
    let bodyHit = -1, bodyAng = -90, gestureAge = -1, gestureAng = -90;

    function shellFor(type) {
      for (let i = 0; i < shells.length; i++)                    // школьная своего типа — первой
        if (!shells[i].gone && !shells[i].unborn && shells[i].t === type && shells[i].rest > 0) return shells[i];
      for (let i = 0; i < shells.length; i++)
        if (!shells[i].gone && !shells[i].unborn && shells[i].t === "plain" && shells[i].rest > 0) return shells[i];
      return null;
    }

    for (let k = 0; k < def.ev.length; k++) {
      const e = def.ev[k];
      if (c < e.f) break;
      const age = c - e.f;

      if (e.add) {
        shells.push({ t: e.add.t, nom: e.add.nom, rest: e.add.nom, eaten: 0, shown: 0, hits: [],
                      glow: age < 10 ? 1 - age / 10 : 0, hitAng: null, dot: false, broken: -1,
                      seed: 97, stacks: 1, gone: false });
        continue;
      }
      if (e.expire) {
        for (let i = 0; i < shells.length; i++)
          if (shells[i].t === e.expire) { shells[i].gone = true; shells[i].fade = Math.min(1, age / 22); }
        continue;
      }

      if (e.dot) {                                               // урон всюду: трещин не оставляет
        const sh = shellFor("plain");
        if (sh) {
          sh.rest = Math.max(0, sh.rest - e.dmg); sh.eaten += e.dmg;
          if (age < 18) { const g = Math.exp(-age / 5) * 0.35; if (g > sh.glow) { sh.glow = g; sh.dot = true; sh.hitAng = null; } }
        }
        continue;
      }

      if (e.gesture && age < 16) { gestureAge = age; gestureAng = e.ang; }

      const sh = shellFor(e.type);
      if (!sh) { if (age < 20) { bodyHit = age; bodyAng = e.ang; } continue; }

      const absorbed = Math.min(sh.rest, e.dmg);
      sh.rest -= absorbed;
      sh.eaten += absorbed;
      sh.hits.push(e.ang);
      sh.shown = Math.max(sh.shown, sh.eaten / (sh.eaten + sh.rest));
      if (age < 22) {
        const g = Math.exp(-age / 5.5) * Math.min(1, e.dmg / 100);
        if (g > sh.glow) { sh.glow = g; sh.hitAng = e.ang; sh.dot = false; }
      }
      if (sh.rest <= 0) { sh.broken = Math.min(1, age / 20); sh.lastAng = e.ang; }
      if (absorbed < e.dmg && age < 20) { bodyHit = age; bodyAng = e.ang; }   // остаток ушёл в тело
    }

    return { shells: shells, bodyHit: bodyHit, bodyAng: bodyAng,
             gestureAge: gestureAge, gestureAng: gestureAng };
  }

  // Жест блока: пластина щита подставляется В ТОЧКУ удара — направление берётся оттуда же, откуда
  // точка попадания. На скелетном юните это базовая поза плюс Aim части-щита, не отдельный клип.
  function blockGesture(ctx, cx, cy, rx, ry, angDeg, age) {
    const k = age < 4 ? age / 4 : 1 - (age - 4) / 12;            // быстро вышел, мягко ушёл
    if (k <= 0) return;
    const a = angDeg * Math.PI / 180;
    const px = cx + Math.cos(a) * rx * 0.72, py = cy + Math.sin(a) * ry * 0.72;
    ctx.save();
    ctx.translate(px, py);
    ctx.rotate(a);
    ctx.fillStyle = "rgba(255,214,120," + (0.5 * k).toFixed(3) + ")";
    ctx.fillRect(-2, -ry * 0.3, 4.5, ry * 0.6);                  // сама пластина
    ctx.strokeStyle = "rgba(255,236,190," + (0.85 * k).toFixed(3) + ")";
    ctx.lineWidth = 1.6;
    ctx.beginPath(); ctx.moveTo(2.5, -ry * 0.3); ctx.lineTo(2.5, ry * 0.3); ctx.stroke();
    ctx.restore();
  }

  function drawShieldCase(canvas, key) {
    const W = 320, H0 = 280;
    const ctx = stageMini(canvas, W, H0);
    const def = CASES[key];
    const c = Lab.tick % def.cycle;
    const st = runCase(def, c);
    const groundY = H0 - 76, H = 132, x = W / 2, u = H / 16;
    const cx = x, cy = groundY - H * 0.52, rx0 = u * 5.5, ry0 = H * 0.68;

    statusBody(ctx, x, groundY, H, { stun: false, burn: 0, poison: 0, frostbite: 0 });

    // Порез на теле: он есть только там, где урон дошёл до тела.
    if (st.bodyHit >= 0) {
      const k = 1 - st.bodyHit / 20;
      ctx.save();
      ctx.globalCompositeOperation = "lighter";
      const a = st.bodyAng * Math.PI / 180;
      const px = cx + Math.cos(a) * u * 1.6, py = cy + Math.sin(a) * u * 1.2;
      ctx.strokeStyle = "rgba(" + RED + "," + (0.75 * k + 0.25).toFixed(3) + ")";
      ctx.lineWidth = 3; ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(px - Math.cos(a) * u * 1.4, py - Math.sin(a) * u * 1.4);
      ctx.lineTo(px + Math.cos(a) * u * 1.4, py + Math.sin(a) * u * 1.4);
      ctx.stroke();
      ctx.restore();
    }

    for (let i = 0; i < st.shells.length; i++) {
      const sh = st.shells[i];
      if (sh.unborn) continue;                                   // блок: оболочки ещё нет, щит не поднят
      const depth = SHELL_DEPTH[sh.t] !== undefined ? SHELL_DEPTH[sh.t] : 0;
      const rx = rx0 * (1 + depth * 0.075), ry = ry0 * (1 + depth * 0.075);
      const color = SHELL_COLOR[sh.t];
      const base = SHIELD_BASE * (0.8 + 0.25 * Math.min(3, sh.stacks));   // стаки типа: толще кайма

      if (sh.gone) {
        const f = 1 - (sh.fade || 0);
        if (f <= 0.02) continue;
        ctx.save(); ctx.globalAlpha = f;
        domeSurface(ctx, "ellipse", cx, cy, rx * (1 + 0.1 * (1 - f)), ry * (1 + 0.1 * (1 - f)), color,
                    { glow: 0, hitAng: null, dot: false }, base * f, groundY + u * 0.8);
        ctx.restore();
        continue;
      }
      if (sh.broken >= 0) {
        if (sh.broken < 1) domeShards(ctx, "ellipse", cx, cy, rx, ry, sh.broken, sh.lastAng || -90, color);
        continue;
      }

      domeSurface(ctx, "ellipse", cx, cy, rx, ry, color, sh, base, groundY + u * 0.8);
      // Яркость узора = текущая целостность; геометрия — история. Широко и бледно = «били, но крепок».
      const integrity = sh.rest / Math.max(1e-4, sh.rest + sh.eaten);
      drawPattern(ctx, "ellipse", cx, cy, rx, ry, sh.seed, sh.shown, sh.hits, color,
                  0.12 + 0.5 * (1 - integrity) + sh.glow * 0.4);
    }

    if (st.gestureAge >= 0) blockGesture(ctx, cx, cy, rx0 * 1.1, ry0 * 1.1, st.gestureAng, st.gestureAge);

    // Показания: что именно происходит в этом кадре — иначе кейсы не отличить друг от друга.
    ctx.font = "500 12px ui-monospace, Consolas, monospace";
    const live = st.shells.filter(function (s) { return !s.gone && !s.unborn && s.broken < 0; });
    const busy = live.filter(function (s) { return s.glow > 0.03; })[0];
    let line;
    if (st.gestureAge >= 0)                                                        line = "БЛОК · щит в точку удара";
    else if (st.shells.some(function (s) { return s.broken >= 0 && s.broken < 1; })) line = "ПРОБИТ";
    else if (st.shells.some(function (s) { return s.gone && (s.fade || 0) < 1; }))   line = "РАЗВЕЯЛСЯ";
    else if (busy && def.note)                                                      line = def.note;
    else if (busy) line = busy.dot ? "тик эффекта · всюду слабее" : "поглощает · " + shellName(busy.t);
    else if (st.bodyHit >= 0) line = "урон дошёл до ТЕЛА";
    else if (live.length === 0) line = "барьера нет";
    else line = live.length + (live.length === 1 ? " оболочка · покой" : " оболочки · покой");
    ctx.fillStyle = line.indexOf("БЛОК") === 0 ? "rgba(255,214,120,1)"
                  : line.indexOf("ПРОБИТ") === 0 ? "rgba(255,146,48,1)"
                  : line.indexOf("РАЗВЕЯЛСЯ") === 0 ? "rgba(147,128,94,1)"
                  : busy ? "rgba(232,220,196,1)" : "rgba(147,128,94,.8)";
    ctx.fillText(line, 18, H0 - 28);

    ctx.fillStyle = "rgba(147,128,94,.75)";
    const shownPct = Math.round(100 * Math.max.apply(null, st.shells.map(function (s) { return s.shown; }).concat([0])));
    ctx.fillText("узор " + shownPct + "%", W - 84, H0 - 28);
  }

  function shellName(t) { return t === "plain" ? "обычный" : t === "phys" ? "физ" : "маг"; }

  /* ---------- барьер: постоянный, но тихий; помнит удары трещинами ---------- */

  const SHIELD = "138,206,255";

  // Жизнь барьера одним циклом: пассив, слабый удар, тик DoT, два сильных, пробитие, пауза.
  // strength — доля поглощённого урона; dot бьёт «всюду», поэтому своей точки не имеет.
  const SHIELD_EVENTS = [
    { f: 34,  str: 0.28, ang: -168 },
    { f: 66,  str: 0.14, dot: true },
    { f: 98,  str: 0.62, ang: -22  },
    { f: 132, str: 0.55, ang: -142 },
    { f: 166, str: 1.00, ang: -64, breaks: true }
  ];
  const SHIELD_CYCLE = 232;

  function shieldState(cycleLen, events, allowBreak) {
    const c = Lab.tick % cycleLen;
    let glow = 0, hitAng = null, cracks = 0, dot = false, broken = -1, eaten = 0;
    const hits = [];
    const hittingCount = events.filter(function (e) { return !e.dot; }).length;
    for (let i = 0; i < events.length; i++) {
      const e = events[i];
      if (c < e.f) break;
      const age = c - e.f;
      if (!e.dot) { cracks++; hits.push(e.ang); eaten += e.str; }
      if (age < 22) {
        const k = Math.exp(-age / 5.5);
        if (e.str * k > glow) { glow = e.str * k; hitAng = e.dot ? null : e.ang; dot = !!e.dot; }
      }
      if (e.breaks && allowBreak) broken = Math.min(1, age / 22);
    }
    // Доля проявленного узора: монотонна по ходу цикла, полный узор совпадает с пробитием.
    const shown = hittingCount > 0 ? cracks / hittingCount : 0;
    return { glow: glow, hitAng: hitAng, cracks: cracks, dot: dot, broken: broken, cycle: c,
             shown: shown, hits: hits, eaten: eaten };
  }

  function facetPoint(cx, cy, rx, ry, i, n) {
    const a = (i / n) * Math.PI * 2 - Math.PI / 2;
    return { x: cx + Math.cos(a) * rx, y: cy + Math.sin(a) * ry, a: a };
  }

  function shieldReadout(ctx, W, H0, st, label) {
    ctx.font = "500 13px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.fillText(label, 26, H0 - 34);
    const state = st.broken >= 0 ? "пробит"
                : st.glow > 0.4 ? "сильный удар"
                : st.dot && st.glow > 0 ? "тик эффекта"
                : st.glow > 0 ? "слабый удар" : "покой";
    ctx.fillStyle = st.broken >= 0 ? "rgba(255,146,48,1)"
                  : st.glow > 0.4 ? "rgba(230,248,255,1)"
                  : st.glow > 0 ? "rgba(" + SHIELD + ",1)" : "rgba(147,128,94,.75)";
    ctx.fillText(state, 150, H0 - 34);
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.fillText("трещин " + Math.min(st.cracks, SHIELD_EVENTS.length), 300, H0 - 34);
  }

  const SHIELD_BASE = 0.055;   // «очень-очень незаметный» в покое: барьер не мутит тело под собой

  function drawShieldStand(canvas, kind) {
    const W = 480, H0 = 380;
    const ctx = stageMini(canvas, W, H0);
    const groundY = H0 - 92, H = 168, x = 240;
    const u = H / 16;
    const st = shieldState(SHIELD_CYCLE, SHIELD_EVENTS, true);

    statusBody(ctx, x, groundY, H, { stun: false, burn: 0, poison: 0, frostbite: 0 });

    const cx = x, cy = groundY - H * 0.52, rx = u * 5.5, ry = H * 0.68;

    if (st.broken < 0) {
      domeSurface(ctx, kind, cx, cy, rx, ry, SHIELD, st, SHIELD_BASE, groundY + u * 0.8);
      drawPattern(ctx, kind, cx, cy, rx, ry, 31, st.shown, st.hits, SHIELD,
                  0.14 + st.shown * 0.5 + st.glow * 0.4);
    } else if (st.broken < 1) {
      domeShards(ctx, kind, cx, cy, rx, ry, st.broken,
                 SHIELD_EVENTS[SHIELD_EVENTS.length - 1].ang, SHIELD);
    }

    shieldReadout(ctx, W, H0, st, "состояние");
    miniLabel(ctx, (DOME[kind] || DOME.dome).label);
  }

  const BREAK_CYCLE = 96;
  function drawShieldBreak(canvas) {
    const W = 480, H0 = 330;
    const ctx = stageMini(canvas, W, H0);
    const groundY = H0 - 74, H = 150, x = 240;
    const u = H / 16;
    const c = Lab.tick % BREAK_CYCLE;
    const cx = x, cy = groundY - H * 0.52, rx = u * 5.5, ry = H * 0.68;

    statusBody(ctx, x, groundY, H, { stun: false, burn: 0, poison: 0, frostbite: 0 });

    if (c < 30) {                                   // целый, с трещинами от прошлых ударов
      const st = { glow: c > 24 ? (c - 24) / 6 : 0, hitAng: -64, cracks: 4, dot: false, broken: -1 };
      domeSurface(ctx, "ellipse", cx, cy, rx, ry, SHIELD, st, SHIELD_BASE, groundY + u * 0.8);
      drawPattern(ctx, "ellipse", cx, cy, rx, ry, 31, 0.95, [-64, -150, -20], SHIELD, 0.6 + st.glow * 0.4);
    } else if (c < 56) {
      domeShards(ctx, "ellipse", cx, cy, rx, ry, (c - 30) / 26, -64, SHIELD);
    }

    ctx.font = "500 13px ui-monospace, Consolas, monospace";
    ctx.fillStyle = c >= 30 && c < 56 ? "rgba(255,146,48,1)" : "rgba(147,128,94,.9)";
    ctx.fillText(c < 30 ? "трещин 4 · держится" : c < 56 ? "ПРОБИТ · осколки прочь от юнита" : "барьера нет",
                 26, H0 - 34);
  }

  const FADE_CYCLE = 96;
  function drawShieldFade(canvas) {
    const W = 480, H0 = 330;
    const ctx = stageMini(canvas, W, H0);
    const groundY = H0 - 74, H = 150, x = 240;
    const u = H / 16;
    const c = Lab.tick % FADE_CYCLE;
    const cx = x, cy = groundY - H * 0.52, rx = u * 5.5, ry = H * 0.68;

    statusBody(ctx, x, groundY, H, { stun: false, burn: 0, poison: 0, frostbite: 0 });

    if (c < 60) {
      const t = c < 30 ? 0 : (c - 30) / 30;         // развеивание: ровно, без направления, вверх
      const st = { glow: 0, hitAng: null, cracks: 2, dot: false, broken: -1 };
      ctx.save();
      ctx.globalAlpha = 1 - t;
      ctx.translate(0, -ry * 0.35 * t);
      domeSurface(ctx, "ellipse", cx, cy, rx, ry * (1 + 0.12 * t), SHIELD, st,
                  SHIELD_BASE * (1 - t * 0.4), groundY + u * 0.8);
      drawPattern(ctx, "ellipse", cx, cy, rx, ry, 31, 0.35, [-150], SHIELD, 0.3 * (1 - t));
      ctx.restore();
    }

    ctx.font = "500 13px ui-monospace, Consolas, monospace";
    ctx.fillStyle = "rgba(147,128,94,.9)";
    ctx.fillText(c < 30 ? "трещин 2 · время идёт" : c < 60 ? "РАЗВЕЯЛСЯ · ровно, вверх, без осколков" : "барьера нет",
                 26, H0 - 34);
  }

  /* ---------- сборка страницы ---------- */

  var SURFACES = [["b-ellipse", "ellipse"], ["b-dome", "dome"], ["b-cocoon", "cocoon"]]
    .map(function (p) { return { el: document.getElementById(p[0]), kind: p[1] }; })
    .filter(function (p) { return p.el; });

  var CASE_IDS = ["calm", "weak", "strong", "dot", "two", "three", "wrong", "stack",
                  "layer", "through", "expire", "addover", "block", "backstab"];
  var CASE_UI = CASE_IDS
    .map(function (k) { return { el: document.getElementById("k-" + k), key: k }; })
    .filter(function (p) { return p.el; });

  var bBreak = document.getElementById("b-break");
  var bFade  = document.getElementById("b-fade");

  Lab.onRender(function () {
    for (var i = 0; i < SURFACES.length; i++) drawShieldStand(SURFACES[i].el, SURFACES[i].kind);
    for (var j = 0; j < CASE_UI.length; j++) drawShieldCase(CASE_UI[j].el, CASE_UI[j].key);
    if (bBreak) drawShieldBreak(bBreak);
    if (bFade) drawShieldFade(bFade);
  });
})();
