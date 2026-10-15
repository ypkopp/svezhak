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

  /* ---------- Спираль свежести (v6 FreshSpiral): 3 витка = 72 ч, центр = сейчас ---------- */
  function renderSpiral(node, batches) {
    var size = 330, c = size / 2, r0 = size * .1, rMax = size * .46, turns = 3;
    function pos(h) { var t = Math.min(h, 72) / 72; var a = -Math.PI / 2 + t * turns * Math.PI * 2; var r = r0 + t * (rMax - r0); return [c + r * Math.cos(a), c + r * Math.sin(a)]; }
    function seg(h1, h2) { var d = ''; for (var h = h1; h <= h2; h += .5) { var p = pos(h); d += (h === h1 ? 'M' : 'L') + p[0].toFixed(1) + ' ' + p[1].toFixed(1); } return d; }
    var list = (batches || []).filter(function (b) { return b.hoursLeft <= 72; });
    var under24 = list.filter(function (b) { return b.hoursLeft < 24; }).length;
    var acc = ACC();
    var svg = '<svg width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '" style="overflow:visible;display:block;margin:0 auto">';
    svg += '<defs><radialGradient id="spGlow"><stop offset="0" stop-color="#F56565" stop-opacity=".3"/><stop offset=".45" stop-color="#ECC94B" stop-opacity=".08"/><stop offset="1" stop-color="#48BB78" stop-opacity="0"/></radialGradient>'
        + '<linearGradient id="spSweep" x1="0" x2="1"><stop offset="0" stop-color="' + acc + '" stop-opacity="0"/><stop offset="1" stop-color="' + acc + '" stop-opacity=".5"/></linearGradient></defs>';
    svg += '<circle cx="' + c + '" cy="' + c + '" r="' + (rMax + 6) + '" fill="url(#spGlow)"/>';
    [1, 2, 3].forEach(function (k) { svg += '<circle cx="' + c + '" cy="' + c + '" r="' + (r0 + k / 3 * (rMax - r0)) + '" fill="none" stroke="var(--stroke)" stroke-dasharray="2 6"/>'; });
    svg += '<path d="' + seg(0, 12) + '" fill="none" stroke="#F56565" stroke-width="10" stroke-linecap="round" opacity=".28"/>'
        + '<path d="' + seg(12, 24) + '" fill="none" stroke="#ECC94B" stroke-width="10" stroke-linecap="round" opacity=".22"/>'
        + '<path d="' + seg(24, 72) + '" fill="none" stroke="#48BB78" stroke-width="6" stroke-linecap="round" opacity=".14"/>';
    svg += '<g style="transform-origin:' + c + 'px ' + c + 'px;animation:spin 6s linear infinite">'
        + '<path d="M' + c + ' ' + c + ' L' + c + ' ' + (c - rMax - 4) + ' A' + (rMax + 4) + ' ' + (rMax + 4) + ' 0 0 1 ' + (c + (rMax + 4) * Math.sin(.6)) + ' ' + (c - (rMax + 4) * Math.cos(.6)) + ' Z" fill="url(#spSweep)" opacity=".35"/>'
        + '<line x1="' + c + '" y1="' + c + '" x2="' + c + '" y2="' + (c - rMax - 4) + '" stroke="' + acc + '" stroke-width="1.5" opacity=".8"/></g>';
    ['+24ч', '+48ч', '+72ч'].forEach(function (t, i) { var r = r0 + (i + 1) / 3 * (rMax - r0); svg += '<text x="' + (c + 4) + '" y="' + (c - r - 4) + '" fill="var(--tx3)" style="font:600 9.5px \'JetBrains Mono\'">' + t + '</text>'; });
    list.forEach(function (b) {
      var p = pos(b.hoursLeft), col = SEM(b.hoursLeft), s = 5 + Math.min(9, Math.sqrt(b.qty || 1) * 2.4);
      svg += '<g><circle cx="' + p[0].toFixed(1) + '" cy="' + p[1].toFixed(1) + '" r="' + (s + 6) + '" fill="' + col + '" opacity=".14"/>'
          + '<circle cx="' + p[0].toFixed(1) + '" cy="' + p[1].toFixed(1) + '" r="' + s + '" fill="' + col + '" style="filter:drop-shadow(0 0 6px ' + col + ')"/>';
      if (b.hoursLeft < 12) svg += '<circle cx="' + p[0].toFixed(1) + '" cy="' + p[1].toFixed(1) + '" r="' + s + '" fill="none" stroke="' + col + '" stroke-width="2" class="sp-ping"/>';
      svg += '</g>';
    });
    svg += '<circle cx="' + c + '" cy="' + c + '" r="' + (r0 - 8) + '" fill="var(--pn)" stroke="var(--stroke2)"/>'
        + '<text x="' + c + '" y="' + (c - 2) + '" text-anchor="middle" fill="var(--tx)" style="font:700 20px Unbounded">' + under24 + '</text>'
        + '<text x="' + c + '" y="' + (c + 14) + '" text-anchor="middle" fill="var(--tx3)" style="font:700 8px \'JetBrains Mono\';letter-spacing:.12em">&lt;24 Ч</text>';
    svg += '</svg>';
    node.innerHTML = svg;
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
    var OC = ACC();
    var W = node.clientWidth || 600, H = 210, pad = 28, maxV = Math.max(4, Math.max.apply(null, today.concat(yesterday)));
    var X = function (i) { return pad + (i / (today.length - 1)) * (W - pad * 2); };
    var Y = function (v) { return H - 26 - (v / maxV) * (H - 50); };
    var pT = today.map(function (v, i) { return [X(i), Y(v)]; });
    var pY = yesterday.map(function (v, i) { return [X(i), Y(v)]; });
    var dT = smoothPath(pT);
    var area = dT + ' L' + X(today.length - 1) + ',' + (H - 26) + ' L' + X(0) + ',' + (H - 26) + ' Z';
    var last = pT[pT.length - 1];
    var svg = '<svg width="' + W + '" height="' + H + '" style="display:block;overflow:visible">'
        + '<defs><linearGradient id="pcF" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="' + OC + '" stop-opacity=".45"/><stop offset="1" stop-color="' + OC + '" stop-opacity="0"/></linearGradient>'
        + '<linearGradient id="pcS" x1="0" x2="1"><stop offset="0" stop-color="var(--gr)"/><stop offset=".6" stop-color="' + OC + '"/><stop offset="1" stop-color="var(--li,#A78BFA)"/></linearGradient></defs>';
    for (var i = 0; i < 9; i++) svg += '<line x1="' + (pad + i * (W - pad * 2) / 8) + '" x2="' + (pad + i * (W - pad * 2) / 8) + '" y1="10" y2="' + (H - 26) + '" stroke="var(--stroke)" stroke-opacity="1"/>';
    [0, Math.ceil(maxV / 3), Math.ceil(2 * maxV / 3), maxV].forEach(function (v) {
      svg += '<line x1="' + pad + '" x2="' + (W - pad) + '" y1="' + Y(v) + '" y2="' + Y(v) + '" stroke="var(--stroke)" stroke-dasharray="2 4"/>'
          + '<text x="' + (pad - 8) + '" y="' + (Y(v) + 3) + '" text-anchor="end" class="sv-osc-ax" fill="var(--tx3)">' + v + '</text>';
    });
    labels.forEach(function (h, i) { if (i % 4 === 0) svg += '<text x="' + X(i) + '" y="' + (H - 8) + '" text-anchor="middle" class="sv-osc-ax" fill="var(--tx3)">' + esc(h.slice(0, 2)) + 'ч</text>'; });
    svg += '<path d="' + smoothPath(pY) + '" fill="none" stroke="var(--tx3)" stroke-width="1.5" stroke-dasharray="4 5"/>'
        + '<path d="' + area + '" fill="url(#pcF)"/>'
        + '<path d="' + dT + '" fill="none" stroke="url(#pcS)" stroke-width="2.6" style="filter:drop-shadow(0 0 8px ' + OC + ')"/>'
        + '<rect class="pc-scan" x="' + pad + '" y="10" width="70" height="' + (H - 36) + '" fill="url(#pcF)" opacity=".25"/>'
        + '<circle cx="' + last[0] + '" cy="' + last[1] + '" r="5" fill="' + OC + '" style="filter:drop-shadow(0 0 6px ' + OC + ')"/>';
    svg += '</svg>';
    node.innerHTML = svg;
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
        var row = node.querySelector('.sv-th-p[data-row="' + cell.dataset.r + '"]');
        if (row) row.classList.add('on');
        out.innerHTML = 'query --item "' + esc(cell.dataset.p) + '" --day ' + esc(cell.dataset.day) + ' → <b>' + esc(num(cell.dataset.v)) + '</b> истекает';
      });
      cell.addEventListener('mouseleave', function () {
        out.innerHTML = 'watch --expiry --days 7<span class="sv-th-caret">▍</span>';
      });
    });
  }

  /* ---------- Экспорт + загрузчики ---------- */
  window.SvProto = {
    radar: function (id, url) { fetch(url).then(function (r) { return r.json(); }).then(function (d) { var n = el(id); if (n) renderRadar(n, d); }); },
    spiral: function (id, url) { fetch(url).then(function (r) { return r.json(); }).then(function (d) { var n = el(id); if (n) renderSpiral(n, d); }); },
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
