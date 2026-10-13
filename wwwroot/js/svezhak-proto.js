/* СВЕЖАК — визуализации из прототипа (design/v2/charts.jsx) на чистом DOM/SVG.
   Радар, осциллограф, LED-водопад, терминальная карта просрочки, LED-цифры. */
(function () {
  'use strict';

  var SEM = function (h) { return h < 12 ? '#F56565' : h < 24 ? '#ECC94B' : '#48BB78'; };
  var ACC = function () { return getComputedStyle(document.documentElement).getPropertyValue('--a1').trim() || '#ED8936'; };
  function el(id) { return document.getElementById(id); }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function fmtH(h) { return h < 24 ? String(Math.round(h * 10) / 10).replace('.', ',') + ' ч' : Math.floor(h / 24) + ' дн ' + Math.round(h % 24) + ' ч'; }
  function num(x) { return String(x).replace('.', ','); }

  /* ---------- LED цифры (упрощённые: glow + mono, не 14-сегмент) ---------- */
  function led(text, color) {
    return '<b class="sv-led" style="color:' + color + ';text-shadow:0 0 12px ' + color + '">' + esc(text) + '</b>';
  }

  /* ---------- Радар героя (v3 Radar3) ---------- */
  function renderRadar(node, batches) {
    var R = 170, rings = [12, 24, 48, 72];
    var rr = function (h) { return 30 + Math.min(h, 72) / 72 * (R - 30); };
    var list = (batches || []).filter(function (b) { return b.hoursLeft <= 72; }).sort(function (a, b) { return a.hoursLeft - b.hoursLeft; });
    var dots = list.map(function (b, i) {
      var a = Math.PI + (i + .5) / Math.max(1, list.length) * Math.PI;
      return { b: b, x: Math.cos(a) * rr(b.hoursLeft), y: Math.sin(a) * rr(b.hoursLeft) };
    });
    var acc = ACC();
    var svg = '<svg viewBox="' + (-R - 10) + ' ' + (-R - 14) + ' ' + (2 * R + 20) + ' ' + (R + 24) + '" class="sv-radar-svg">';
    svg += '<defs><linearGradient id="svsw" x1="0" x2="1"><stop offset="0" stop-color="' + acc + '" stop-opacity="0"/><stop offset="1" stop-color="' + acc + '" stop-opacity=".35"/></linearGradient>'
        + '<radialGradient id="svhot"><stop offset="0" stop-color="#F56565" stop-opacity=".35"/><stop offset="1" stop-color="#F56565" stop-opacity="0"/></radialGradient></defs>';
    svg += '<circle r="' + rr(12) + '" fill="url(#svhot)"/>';
    rings.forEach(function (h) {
      var c = h === 12 ? '#F56565' : h === 24 ? '#ECC94B' : '#fff';
      svg += '<path d="M' + (-rr(h)) + ',0 A' + rr(h) + ',' + rr(h) + ' 0 0 1 ' + rr(h) + ',0" fill="none" stroke="' + c + '" stroke-opacity="' + (h <= 24 ? .55 : .1) + '" ' + (h <= 24 ? '' : 'stroke-dasharray="2 5"') + '/>'
          + '<text x="' + (rr(h) + 3) + '" y="11" class="sv-radar-ax">' + h + 'ч</text>';
    });
    svg += '<clipPath id="svrclip"><rect x="' + (-R - 10) + '" y="' + (-R - 14) + '" width="' + (2 * R + 20) + '" height="' + (R + 14) + '"/></clipPath>'
        + '<g clip-path="url(#svrclip)"><g class="sv-radar-sweep"><path d="M0,0 L' + R + ',0 A' + R + ',' + R + ' 0 0 0 ' + (Math.cos(-.5) * R) + ',' + (Math.sin(-.5) * R) + ' Z" fill="url(#svsw)"/></g></g>';
    dots.forEach(function (d) {
      var c = SEM(d.b.hoursLeft), lab = d.b.hoursLeft < 12, right = d.x > 0;
      svg += '<g transform="translate(' + d.x + ',' + d.y + ')">'
          + (d.b.hoursLeft < 24 ? '<circle r="10" fill="none" stroke="' + c + '" class="sv-radar-ping"/>' : '')
          + '<circle r="' + (d.b.hoursLeft < 24 ? 5 : 3.5) + '" fill="' + c + '" style="filter:drop-shadow(0 0 5px ' + c + ')"/>'
          + (lab ? '<text x="' + (right ? 9 : -9) + '" y="4" text-anchor="' + (right ? 'start' : 'end') + '" class="sv-radar-lb" fill="' + c + '">' + esc(d.b.name) + ' · ' + fmtH(d.b.hoursLeft) + '</text>' : '')
          + '</g>';
    });
    svg += '<line x1="' + (-R - 6) + '" x2="' + (R + 6) + '" y1="0" y2="0" style="stroke:' + acc + '" stroke-opacity=".6"/></svg>';
    node.innerHTML = svg;
  }

  /* ---------- Осциллограф заказов (v2 Oscilloscope) ---------- */
  function smoothPath(pts) {
    if (!pts.length) return '';
    var d = 'M' + pts[0][0] + ',' + pts[0][1];
    for (var i = 0; i < pts.length - 1; i++) {
      var p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
      d += ' C' + (p1[0] + (p2[0] - p0[0]) / 6) + ',' + (p1[1] + (p2[1] - p0[1]) / 6) + ' ' + (p2[0] - (p3[0] - p1[0]) / 6) + ',' + (p2[1] - (p3[1] - p1[1]) / 6) + ' ' + p2[0] + ',' + p2[1];
    }
    return d;
  }

  function renderOsc(node, labels, today, yesterday) {
    var OC = ACC(), HI = document.documentElement.classList.contains('theme-b') ? '#A6F0C3' : '#FFC48F';
    var W = node.clientWidth || 600, H = 210, pad = 28, maxV = Math.max(4, Math.max.apply(null, today.concat(yesterday)));
    var X = function (i) { return pad + (i / (today.length - 1)) * (W - pad * 2); };
    var Y = function (v) { return H - 26 - (v / maxV) * (H - 50); };
    var pT = today.map(function (v, i) { return [X(i), Y(v)]; });
    var pY = yesterday.map(function (v, i) { return [X(i), Y(v)]; });
    var dT = smoothPath(pT);
    var area = dT + ' L' + X(today.length - 1) + ',' + (H - 26) + ' L' + X(0) + ',' + (H - 26) + ' Z';
    var last = pT[pT.length - 1];
    var svg = '<svg width="' + W + '" height="' + H + '" style="display:block">'
        + '<defs><linearGradient id="osca" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="' + OC + '" stop-opacity=".55"/><stop offset=".6" stop-color="' + OC + '" stop-opacity=".08"/><stop offset="1" stop-color="' + OC + '" stop-opacity="0"/></linearGradient>'
        + '<linearGradient id="oscs" x1="0" x2="1"><stop offset="0" stop-color="' + OC + '" stop-opacity="0"/><stop offset="1" stop-color="' + OC + '" stop-opacity=".35"/></linearGradient>'
        + '<clipPath id="oscc"><rect x="0" y="0" width="0" height="' + H + '"/></clipPath></defs>';
    for (var i = 0; i < 9; i++) svg += '<line x1="' + (pad + i * (W - pad * 2) / 8) + '" x2="' + (pad + i * (W - pad * 2) / 8) + '" y1="10" y2="' + (H - 26) + '" stroke="#ffffff" stroke-opacity="' + (i % 4 === 0 ? .1 : .04) + '"/>';
    [0, Math.ceil(maxV / 3), Math.ceil(2 * maxV / 3), maxV].forEach(function (v) {
      svg += '<line x1="' + pad + '" x2="' + (W - pad) + '" y1="' + Y(v) + '" y2="' + Y(v) + '" stroke="#fff" stroke-opacity=".05" stroke-dasharray="2 4"/>'
          + '<text x="' + (pad - 8) + '" y="' + (Y(v) + 3) + '" text-anchor="end" class="sv-osc-ax">' + v + '</text>';
    });
    labels.forEach(function (h, i) { if (i % 4 === 0) svg += '<text x="' + X(i) + '" y="' + (H - 8) + '" text-anchor="middle" class="sv-osc-ax">' + esc(h.slice(0, 2)) + 'ч</text>'; });
    svg += '<path d="' + smoothPath(pY) + '" fill="none" stroke="#7C8595" stroke-opacity=".5" stroke-width="1.2" stroke-dasharray="3 4"/>'
        + '<path d="' + area + '" fill="url(#osca)"/>'
        + '<path d="' + dT + '" fill="none" stroke="' + OC + '" stroke-width="6" stroke-opacity=".18" style="filter:blur(4px)"/>'
        + '<path d="' + dT + '" fill="none" stroke="' + HI + '" stroke-width="2"/>'
        + '<g clip-path="url(#oscc)"><path d="' + dT + '" fill="none" stroke="#fff" stroke-width="2.4" stroke-opacity=".85" style="filter:drop-shadow(0 0 6px ' + OC + ')"/></g>'
        + '<g class="sv-osc-sweepg"><rect x="-80" y="10" width="80" height="' + (H - 36) + '" fill="url(#oscs)" class="sv-osc-sweeprect"/><line x1="0" x2="0" y1="10" y2="' + (H - 26) + '" stroke="' + OC + '" stroke-opacity=".8" class="sv-osc-sweepline"/></g>'
        + '<circle cx="' + last[0] + '" cy="' + last[1] + '" r="5" fill="' + OC + '" class="sv-osc-head"/>';
    svg += '</svg><div class="sv-osc-scan"></div>';
    node.innerHTML = svg;
    // развёртка: бегущий луч + clipPath-подсветка пройденного участка
    var clip = node.querySelector('#oscc rect'), sweep = node.querySelector('.sv-osc-sweepg');
    var start = null;
    function frame(now) {
      if (!node.isConnected) return;
      if (start === null) start = now;
      var sweep01 = (((now - start) / 1000) * 0.12) % 1;
      var sx = pad + sweep01 * (W - pad * 2);
      clip.setAttribute('width', sx);
      sweep.setAttribute('transform', 'translate(' + sx + ',0)');
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  /* ---------- LED-водопад массы (v2 LedWaterfall) ---------- */
  function renderWaterfall(node, steps) {
    var max = Math.max.apply(null, steps.map(function (s) { return s.v; }).concat([1]));
    var CELLS = 40, run = 0;
    var html = '<div class="sv-wf">';
    steps.forEach(function (s, ri) {
      var from, to;
      if (s.kind === 'total') { from = 0; to = s.v; run = s.v; } else { to = run; from = run - s.v; run = from; }
      var a = Math.round(from / max * CELLS), b = Math.round(to / max * CELLS);
      html += '<div class="sv-wf-row"><div class="sv-wf-l">' + esc(s.l) + '</div><div class="sv-wf-cells">';
      for (var i = 0; i < CELLS; i++) {
        var on = i >= a && i < b;
        html += '<i' + (on ? ' class="on" style="background:' + s.c + ';box-shadow:0 0 6px ' + s.c + ';animation-delay:' + (ri * 120 + (i - a) * 14) + 'ms"' : '') + '></i>';
      }
      html += '</div><div class="sv-wf-v" style="color:' + s.c + '">' + (s.kind === 'total' ? '' : '−') + num(Math.round(s.v * 100) / 100) + '<small> кг</small></div></div>';
    });
    html += '</div>';
    node.innerHTML = html;
  }

  /* ---------- Терминальная карта просрочки (v2 TermHeat) ---------- */
  var GLYPHS = [' ', '·', '∘', '░', '▒', '▓', '█'];
  function renderTermHeat(node, products, days, matrix) {
    // matrix[productIdx][dayIdx] = qty
    var max = 0;
    matrix.forEach(function (row) { row.forEach(function (v) { if (v > max) max = v; }); });
    max = max || 1;
    var gridCols = '130px repeat(' + days.length + ',1fr)';
    var html = '<div class="sv-th"><div class="sv-th-grid" style="grid-template-columns:' + gridCols + '">'
        + '<span></span>' + days.map(function (d) { return '<span class="sv-th-d">' + esc(d) + '</span>'; }).join('');
    products.forEach(function (p, ri) {
      html += '<span class="sv-th-p" data-row="' + ri + '">' + esc(p) + '</span>';
      days.forEach(function (d, di) {
        var v = matrix[ri][di] || 0;
        var k = v ? Math.max(3, Math.ceil(Math.sqrt(v / max) * 6)) : 0;
        var c = di === 0 ? '#F56565' : di === 1 ? '#ECC94B' : '#48BB78';
        html += '<span class="sv-th-c" data-r="' + ri + '" data-d="' + di + '" data-v="' + v + '" data-p="' + esc(p) + '" data-day="' + esc(d) + '" '
            + 'style="color:' + (v ? c : '#2e3440') + ';text-shadow:' + (v ? '0 0 ' + (4 + k * 2) + 'px ' + c : 'none') + '">' + (GLYPHS[k] || '·').repeat(3) + '</span>';
      });
    });
    html += '</div><div class="sv-th-foot"><span class="sv-th-prompt">svezhak@kitchen:~$</span> <span class="sv-th-out">watch --expiry --days 7<span class="sv-th-caret">▍</span></span></div></div>';
    node.innerHTML = html;
    var out = node.querySelector('.sv-th-out');
    node.querySelectorAll('.sv-th-c').forEach(function (cell) {
      cell.addEventListener('mouseenter', function () {
        node.querySelectorAll('.sv-th-c').forEach(function (x) { x.classList.remove('hv'); });
        node.querySelectorAll('.sv-th-p').forEach(function (x) { x.classList.remove('on'); });
        cell.classList.add('hv');
        var p = cell.querySelector ? null : null;
        node.querySelector('.sv-th-p[data-row="' + cell.dataset.r + '"]')?.classList.add('on');
        out.innerHTML = 'query --item "' + esc(cell.dataset.p) + '" --day ' + esc(cell.dataset.day) + ' → <b>' + esc(num(cell.dataset.v)) + ' ' + esc(cell.dataset.unit || '') + '</b> истекает';
      });
      cell.addEventListener('mouseleave', function () {
        out.innerHTML = 'watch --expiry --days 7<span class="sv-th-caret">▍</span>';
      });
    });
  }

  /* ---------- Экспорт + загрузчики ---------- */
  window.SvProto = {
    radar: function (id, url) { fetch(url).then(function (r) { return r.json(); }).then(function (d) { var n = el(id); if (n) renderRadar(n, d); }); },
    osc: function (id, url) {
      fetch(url).then(function (r) { return r.json(); }).then(function (d) {
        var n = el(id); if (!n) return;
        function draw() { renderOsc(n, d.labels, d.values, d.yesterday || d.values.map(function () { return 0; })); }
        draw();
        if (typeof ResizeObserver !== 'undefined') new ResizeObserver(function () { draw(); }).observe(n);
      });
    },
    waterfall: function (id, url) {
      fetch(url).then(function (r) { return r.json(); }).then(function (d) {
        var n = el(id); if (!n) return;
        renderWaterfall(n, [
          { l: 'Утро', v: d.start, c: '#48BB78', kind: 'total' },
          { l: 'По заказам', v: d.byOrder, c: '#ED8936' },
          { l: 'Просрочка', v: d.byExpired, c: '#F56565' },
          { l: 'Сейчас', v: d.current, c: '#48BB78', kind: 'total' }
        ]);
        var foot = el(id + 'Foot');
        if (foot && d.start > 0) {
          var pct = Math.round(d.byExpired / d.start * 1000) / 10;
          foot.innerHTML = '<span class="sv-wf3-ic">🔥</span><span>Списано ' + num(Math.round(d.byExpired * 100) / 100) + ' кг — <b>' + pct + '%</b> массы. <span style="white-space:nowrap">Цель ≤ 5%</span></span>';
        }
      });
    },
    termHeat: function (id, url) {
      fetch(url).then(function (r) { return r.json(); }).then(function (d) {
        var n = el(id); if (!n) return;
        // d: {products[], days[], data[[prodIdx? or [x,y,v]]} — приводим к matrix
        var matrix = d.products.map(function () { return d.days.map(function () { return 0; }); });
        (d.data || []).forEach(function (cell) {
          var x = cell[0], y = cell[1], v = cell[2];
          if (matrix[y] && matrix[y][x] !== undefined) matrix[y][x] = v;
        });
        renderTermHeat(n, d.products, d.days, matrix);
      });
    },
    led: led, SEM: SEM, fmtH: fmtH
  };
})();
