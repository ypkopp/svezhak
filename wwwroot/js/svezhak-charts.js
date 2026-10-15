/* СВЕЖАК — ECharts helpers: state, dark theme, resize, custom visuals. */
(function () {
  'use strict';

  // Тема: читаем токены из CSS-переменных при каждом init (переключение темы пересоздаёт график)
  function tk(n, fb) { var v = getComputedStyle(document.documentElement).getPropertyValue(n).trim(); return v || fb; }
  function isLight() { return document.documentElement.dataset.theme === 'paper'; }
  function axColor() { return tk('--tx3', '#7C8595'); }
  function txColor() { return tk('--tx', '#E2E8F0'); }
  function gridColor() { return isLight() ? 'rgba(0,0,0,.08)' : 'rgba(255,255,255,.05)'; }
  function tipStyle() { return { backgroundColor: tk('--pn2', '#151923'), borderColor: tk('--stroke', '#2a2f3a'), borderWidth: 1, textStyle: { color: txColor(), fontSize: 12 }, confine: true }; }
  var charts = [];

  function el(id) { return document.getElementById(id); }
  function show(id) { var e = el(id); if (e) e.style.display = 'flex'; }
  function hide(id) { var e = el(id); if (e) e.style.display = 'none'; }
  function colorFor(h) { return h < 12 ? '#F56565' : h < 24 ? '#ED8936' : '#48BB78'; }
  function hasData(v) { return Array.isArray(v) && v.some(function (x) { return Number(x) !== 0; }); }

  function initChart(cfg) {
    var elNode = el(cfg.el);
    if (!elNode || typeof echarts === 'undefined') return null;
    return new Promise(function (resolve) {
      fetch(cfg.url)
        .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
        .then(function (data) {
          hide(cfg.skel);
          if (cfg.empty && cfg.isEmpty && cfg.isEmpty(data)) { show(cfg.empty); resolve(null); return; }
          elNode.style.display = 'block';
          var chart = echarts.init(elNode);
          cfg.render(chart, data);
          attachResize(chart, elNode);
          resolve(chart);
        })
        .catch(function (err) {
          hide(cfg.skel);
          var node = el(cfg.error);
          if (node) { node.textContent = 'Не удалось загрузить данные: ' + err.message; node.style.display = 'flex'; }
          resolve(null);
        });
    });
  }

  function attachResize(chart, node) {
    if (typeof ResizeObserver === 'undefined') { window.addEventListener('resize', chart.resize); return; }
    var observer = new ResizeObserver(function () { chart.resize(); });
    observer.observe(node);
    charts.push({ chart: chart, observer: observer });
  }

  /* Река свежести: glowing points + labels + mark areas/now */
  function renderRiver(chart, batches) {
    var rows = (batches || []).slice().sort(function (a, b) { return a.hoursLeft - b.hoursLeft; });
    var laneCount = Math.min(6, Math.max(3, Math.ceil(rows.length / 3)));
    var lanes = [];
    var data = [];
    rows.forEach(function (batch) {
      var lane = 0;
      for (var i = 0; i < laneCount; i++) {
        var right = lanes[i] == null ? -Infinity : lanes[i];
        if (batch.hoursLeft > right + 7) { lane = i; break; }
      }
      lanes[lane] = batch.hoursLeft + 7;
      data.push({ value: [batch.hoursLeft, lane], name: batch.name, batch: batch, itemStyle: { color: colorFor(batch.hoursLeft) } });
    });
    chart.setOption({
      grid: { left: 48, right: 28, top: 34, bottom: 34 },
      xAxis: { type: 'value', min: 0, max: 72, name: 'часов до истечения', nameTextStyle: { color: axColor() }, axisLabel: { color: axColor(), formatter: function (v) { return v === 0 ? 'сейчас' : '+' + v + 'ч'; } }, axisLine: { lineStyle: { color: gridColor() } }, splitLine: { lineStyle: { color: gridColor() } } },
      yAxis: { type: 'value', min: -0.5, max: laneCount - .4, axisLabel: { show: false }, splitLine: { show: false }, axisTick: { show: false } },
      tooltip: Object.assign({ trigger: 'item', formatter: function (p) { var b = p.data.batch; return '<b>' + b.name + '</b><br>' + b.delivery + ' · ' + b.qty + ' ' + b.unit + '<br><b style="color:' + colorFor(b.hoursLeft) + '">' + b.hoursLeft + ' ч</b>'; } }, tipStyle()),
      series: [{
        type: 'scatter', symbolSize: 16, data: data,
        // Подписи не слипаются: hideOverlap + при столкновении ECharts разводит их
        labelLayout: { hideOverlap: true, moveOverlap: 'shiftY' },
        label: { show: true, position: 'right', distance: 9, formatter: function (p) { return p.data.name + ' · ' + p.data.batch.hoursLeft + ' ч'; }, color: txColor(), fontSize: 10, fontWeight: 700 },
        labelLine: { show: true, length2: 8, lineStyle: { color: 'rgba(255,255,255,.25)' } },
        itemStyle: { shadowBlur: 13, shadowColor: 'rgba(237,137,54,.55)', borderColor: '#0b0d11', borderWidth: 2 },
        emphasis: { scale: true, scaleSize: 8, label: { fontSize: 11 } },
        markArea: { silent: true, data: [
          [{ name: '< 12 ч', xAxis: 0, itemStyle: { color: 'rgba(245,101,101,.09)' } }, { xAxis: 12, label: { color: '#F56565', fontSize: 10, position: 'insideTop' } }],
          [{ name: '12–24 ч', xAxis: 12, itemStyle: { color: 'rgba(237,137,54,.07)' } }, { xAxis: 24, label: { color: '#ED8936', fontSize: 10, position: 'insideTop' } }]
        ] },
        markLine: { silent: true, symbol: 'none', lineStyle: { color: '#ED8936', width: 2 }, label: { formatter: 'сейчас', color: '#ED8936', fontSize: 10 }, data: [{ xAxis: 0 }] }
      }]
    });
  }

  function renderPulse(chart, d) {
    chart.setOption({
      grid: { left: 8, right: 14, top: 20, bottom: 24, containLabel: true },
      xAxis: { type: 'category', data: d.labels, boundaryGap: false, axisLabel: { color: axColor() }, axisLine: { lineStyle: { color: gridColor() } } },
      yAxis: { type: 'value', axisLabel: { color: axColor() }, splitLine: { lineStyle: { color: gridColor() } } },
      tooltip: Object.assign({ trigger: 'axis' }, tipStyle()),
      series: [{
        name: 'заказы', type: 'line', smooth: true, symbol: 'circle', symbolSize: 8, data: d.values,
        lineStyle: { width: 3, color: '#ED8936', shadowColor: 'rgba(237,137,54,.7)', shadowBlur: 14 },
        itemStyle: { color: '#F6AD55', borderColor: '#ED8936', borderWidth: 2, shadowBlur: 10, shadowColor: 'rgba(237,137,54,.9)' },
        areaStyle: { color: { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: 'rgba(237,137,54,.45)' }, { offset: 1, color: 'rgba(237,137,54,0)' }] } }
      }]
    });
  }

  function gradient(c1, c2) { return { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: c1 }, { offset: 1, color: c2 }] }; }
  function renderWaterfall(chart, d) {
    var base = [0, d.start - d.byOrder, d.start - d.byOrder - d.byExpired, 0];
    chart.setOption({
      grid: { left: 8, right: 8, top: 24, bottom: 24, containLabel: true },
      xAxis: { type: 'category', data: ['Утро', 'По заказам', 'Просрочка', 'Сейчас'], axisLabel: { color: axColor() }, axisLine: { lineStyle: { color: gridColor() } } },
      yAxis: { type: 'value', axisLabel: { color: axColor() }, splitLine: { lineStyle: { color: gridColor() } } },
      tooltip: Object.assign({ trigger: 'axis', axisPointer: { type: 'shadow' } }, tipStyle()),
      series: [
        { type: 'bar', stack: 'w', itemStyle: { color: 'rgba(255,255,255,0.03)' }, data: base, tooltip: { show: false }, silent: true },
        { type: 'bar', stack: 'w', barWidth: '48%', itemStyle: { borderRadius: [8, 8, 0, 0] },
          data: [
            { value: d.start, itemStyle: { color: gradient('#48BB78', '#2f9e63') } },
            { value: d.byOrder, itemStyle: { color: gradient('#ED8936', '#c56a21') } },
            { value: d.byExpired, itemStyle: { color: gradient('#F56565', '#c93e3e') } },
            { value: d.current, itemStyle: { color: gradient('#48BB78', '#2f9e63') } }
          ], label: { show: true, position: 'top', color: txColor(), fontSize: 10 } }
      ]
    });
  }

  function renderHeatmap(chart, d) {
    var max = d.data && d.data.length ? Math.max.apply(null, d.data.map(function (x) { return x[2]; })) : 1;
    chart.setOption({
      grid: { left: 10, right: 14, top: 10, bottom: 40, containLabel: true },
      xAxis: { type: 'category', data: d.days, axisLabel: { color: axColor() }, splitArea: { show: true, areaStyle: { color: ['rgba(255,255,255,0.015)', 'rgba(255,255,255,0)'] } } },
      yAxis: { type: 'category', data: d.products, axisLabel: { color: axColor(), fontSize: 10 } },
      visualMap: { min: 0, max: max, calculable: true, orient: 'horizontal', left: 'center', bottom: 0, inRange: { color: ['#1d3a2a', '#ED8936', '#F56565'] }, textStyle: { color: axColor() } },
      tooltip: Object.assign({ position: 'top' }, tipStyle()),
      series: [{ type: 'heatmap', data: d.data, label: { show: true, color: txColor(), fontSize: 9 }, itemStyle: { borderRadius: 4, borderColor: '#111318', borderWidth: 1 }, emphasis: { itemStyle: { shadowBlur: 12, shadowColor: 'rgba(0,0,0,.6)' } } }]
    });
  }

  function renderStopForecast(chart, d) {
    var rows = (d.dishes || []).slice(0, 12);
    chart.setOption({
      grid: { left: 10, right: 12, top: 18, bottom: 24, containLabel: true },
      xAxis: { type: 'category', data: rows.map(function (x) { return x.dish; }), axisLabel: { color: axColor(), fontSize: 10, interval: 0, rotate: 20 }, axisLine: { lineStyle: { color: gridColor() } } },
      yAxis: { type: 'value', name: 'дней до стопа', nameTextStyle: { color: axColor() }, axisLabel: { color: axColor() }, splitLine: { lineStyle: { color: gridColor() } } },
      tooltip: Object.assign({ trigger: 'axis', formatter: function (params) { var x = rows[params[0].dataIndex]; return '<b>' + x.dish + '</b><br>' + x.limitingProduct + '<br>запас ' + x.stock + ' · расход ' + x.dailyUse + '/день<br>' + x.daysUntilStop + ' дн.'; } }, tipStyle()),
      series: [{ type: 'bar', barWidth: '48%', data: rows.map(function (x) { return { value: x.daysUntilStop, itemStyle: { color: x.alreadyStopped ? '#F56565' : x.daysUntilStop < 2 ? '#ED8936' : '#48BB78', borderRadius: [8,8,0,0] } }; }), label: { show: true, position: 'top', color: txColor(), fontSize: 9, formatter: function (p) { return p.value + 'д'; } } }]
    });
  }

  function renderLossShare(chart, d) {
    chart.setOption({
      tooltip: Object.assign({ trigger: 'item', formatter: function (p) { return p.name + '<br><b>' + p.value + ' ₽</b> · ' + p.percent + '%'; } }, tipStyle()),
      legend: { bottom: 4, left: 'center', orient: 'horizontal', itemGap: 18, padding: [6, 0], textStyle: { color: axColor(), fontSize: 11 } },
      series: [{ type: 'pie', radius: ['48%', '72%'], center: ['50%', '44%'],
        itemStyle: { borderRadius: 6, borderColor: '#111318', borderWidth: 2 },
        label: { color: txColor(), fontSize: 10 },
        data: [
          { name: 'Использовано в заказах', value: d.values[0], itemStyle: { color: '#48BB78' } },
          { name: 'Списано просроченным', value: d.values[1], itemStyle: { color: '#F56565' } },
          { name: 'Остаток на складе', value: d.values[2], itemStyle: { color: '#ED8936' } }
        ] }]
    });
  }

  function drawSparkline(canvas) {
    var ctx = canvas.getContext('2d');
    var w = canvas.width = 100, h = canvas.height = 34;
    ctx.clearRect(0, 0, w, h);
    ctx.beginPath();
    var seed = Number(canvas.closest('[data-count]')?.dataset.count || 4);
    for (var x = 0; x <= w; x += 3) {
      var y = h - 3 - Math.abs(Math.sin(x * .14 + seed) * (h - 9));
      x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    }
    ctx.strokeStyle = getComputedStyle(canvas).color || '#ED8936';
    ctx.lineWidth = 1.8;
    ctx.stroke();
  }

  var renders = {
    river: renderRiver,
    pulse: renderPulse,
    waterfall: renderWaterfall,
    heatmap: renderHeatmap,
    stopForecast: renderStopForecast,
    lossShare: renderLossShare,
    productionHeatmap: renderHeatmap
  };

  var empties = {
    river: function (d) { return !d || !d.length; },
    pulse: function (d) { return !d || !d.values || !hasData(d.values); },
    waterfall: function (d) { return !d || (d.byOrder === 0 && d.byExpired === 0); },
    heatmap: function (d) { return !d || !d.data || !d.data.length; },
    stopForecast: function (d) { return !d || !d.dishes || !d.dishes.length; },
    lossShare: function (d) { return !d || !d.values || !hasData(d.values); },
    productionHeatmap: function (d) { return !d || !d.data || !d.data.length; }
  };

  window.SvCharts = {
    _map: null,
    initAll: function (map) {
      this._map = map;
      Object.keys(map).forEach(function (key) {
        var cfg = map[key];
        initChart({ el: cfg.el, skel: cfg.skel, empty: cfg.empty, error: cfg.error, url: cfg.url, render: renders[key], isEmpty: empties[key] });
      });
    },
    // при смене темы пересоздаём графики, чтобы цвета осей/сетки/лейблов перечитались из новых токенов
    retheme: function () {
      charts.forEach(function (c) { try { c.observer && c.observer.disconnect(); c.chart.dispose(); } catch (e) { } });
      charts.length = 0;
      if (this._map) this.initAll(this._map);
    },
    drawSparkline: drawSparkline
  };
  // реагируем на смену data-theme у <html>
  if (typeof MutationObserver !== 'undefined') {
    new MutationObserver(function (m) {
      if (m.some(function (x) { return x.attributeName === 'data-theme'; }) && window.SvCharts._map) window.SvCharts.retheme();
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
  }
})();
