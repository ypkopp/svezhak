/* СВЕЖАК — общие ECharts-хелперы: состояния, ресайз, стили, спарклайны. */
(function () {
    'use strict';

    var AX = '#8b93a3', GRID = 'rgba(255,255,255,.05)';
    var TIP = { backgroundColor: '#1d212b', borderColor: '#2a2f3a', borderWidth: 1, textStyle: { color: '#E2E8F0', fontSize: 12 }, confine: true };

    function show(id) { var e = document.getElementById(id); if (e) e.style.display = 'flex'; }
    function hide(id) { var e = document.getElementById(id); if (e) e.style.display = 'none'; }

    function urgencyColor(h) { return h < 12 ? '#F56565' : h < 24 ? '#ED8936' : '#48BB78'; }

    function attachResizer(chart, el) {
        if (typeof ResizeObserver === 'undefined') {
            window.addEventListener('resize', function () { chart.resize(); });
            return;
        }
        new ResizeObserver(function () { chart.resize(); }).observe(el);
    }

    /* ---------- Река свежести: партии как glowing-точки на оси 0–72ч ---------- */
    function renderRiver(chart, batches) {
        var MAX = 72;
        // раскладываем по дорожкам: партии с близкими сроками — в соседние ряды
        var sorted = (batches || []).slice().sort(function (a, b) { return a.hoursLeft - b.hoursLeft; });
        var lanes = [[], [], [], []];
        sorted.forEach(function (b, i) { lanes[i % 4].push(b); });
        var data = [];
        lanes.forEach(function (lane, li) {
            lane.forEach(function (b) {
                data.push({
                    value: [Math.min(MAX, b.hoursLeft), li],
                    name: b.name,
                    itemStyle: { color: urgencyColor(b.hoursLeft) },
                    b: b
                });
            });
        });

        chart.setOption({
            grid: { left: 40, right: 24, top: 30, bottom: 28 },
            xAxis: {
                type: 'value', min: 0, max: MAX,
                name: 'часов до истечения', nameTextStyle: { color: AX, fontSize: 10 },
                axisLabel: { color: AX, formatter: function (v) { return v === 0 ? 'сейчас' : '+' + v + 'ч'; } },
                axisLine: { lineStyle: { color: GRID } }, splitLine: { lineStyle: { color: GRID } }
            },
            yAxis: { type: 'value', min: -0.5, max: 3.6, interval: 1, axisLabel: { show: false }, axisTick: { show: false }, splitLine: { show: false } },
            tooltip: Object.assign({
                trigger: 'item',
                formatter: function (p) {
                    var b = p.data.b;
                    return '<b>' + b.name + '</b><br>' + b.delivery + ' · ' + b.qty + ' ' + b.unit +
                        '<br><span style="color:' + urgencyColor(b.hoursLeft) + '">' + String(b.hoursLeft).replace('.', ',') + ' ч</span>';
                }
            }, TIP),
            series: [{
                type: 'scatter', symbolSize: 14, data: data,
                label: {
                    show: true, position: 'right', distance: 8, color: '#E2E8F0', fontSize: 10,
                    formatter: function (p) { return p.name + ' · ' + String(p.data.b.hoursLeft).replace('.', ',') + ' ч'; }
                },
                itemStyle: { shadowBlur: 10, borderColor: '#0f1115', borderWidth: 2 },
                emphasis: { scale: true, scaleSize: 6, itemStyle: { shadowBlur: 16, shadowColor: 'rgba(237,137,54,.6)' } },
                markArea: {
                    silent: true,
                    data: [
                        [{ name: '< 12 ч', xAxis: 0, itemStyle: { color: 'rgba(245,101,101,.09)' } },
                         { xAxis: 12, label: { show: true, color: '#F56565', fontSize: 10, position: 'insideTop' } }],
                        [{ name: '12–24 ч', xAxis: 12, itemStyle: { color: 'rgba(237,137,54,.07)' } },
                         { xAxis: 24, label: { show: true, color: '#ED8936', fontSize: 10, position: 'insideTop' } }]
                    ]
                },
                markLine: {
                    silent: true, symbol: 'none',
                    label: { formatter: 'сейчас', color: '#ED8936', fontSize: 10, position: 'insideEndTop' },
                    lineStyle: { color: '#ED8936', width: 1.5 }, data: [{ xAxis: 0 }]
                }
            }]
        });
    }

    /* ---------- Пульс заказов: smooth area + градиент + glow ---------- */
    function renderPulse(chart, d) {
        chart.setOption({
            grid: { left: 8, right: 14, top: 18, bottom: 24, containLabel: true },
            xAxis: { type: 'category', data: d.labels, boundaryGap: false, axisLabel: { color: AX }, axisLine: { lineStyle: { color: GRID } } },
            yAxis: { type: 'value', axisLabel: { color: AX }, splitLine: { lineStyle: { color: GRID } } },
            tooltip: Object.assign({ trigger: 'axis' }, TIP),
            series: [{
                name: 'заказы', type: 'line', smooth: true, symbol: 'circle', symbolSize: 8, data: d.values,
                lineStyle: { width: 3, color: '#ED8936', shadowColor: 'rgba(237,137,54,.6)', shadowBlur: 14 },
                itemStyle: { color: '#F6AD55', borderColor: '#ED8936', borderWidth: 2, shadowColor: 'rgba(237,137,54,.8)', shadowBlur: 10 },
                areaStyle: { color: { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: 'rgba(237,137,54,.45)' }, { offset: 1, color: 'rgba(237,137,54,0)' }] } }
            }]
        });
    }

    /* ---------- Водопад списаний ---------- */
    function renderWaterfall(chart, d) {
        var base = [0, d.start - d.byOrder, d.start - d.byOrder - d.byExpired, 0];
        var vals = [d.start, d.byOrder, d.byExpired, d.current];
        function grad(c1, c2) { return { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: c1 }, { offset: 1, color: c2 }] }; }
        chart.setOption({
            grid: { left: 8, right: 8, top: 24, bottom: 24, containLabel: true },
            xAxis: { type: 'category', data: ['Утро', 'По заказам', 'Просрочка', 'Сейчас'], axisLabel: { color: AX }, axisLine: { lineStyle: { color: GRID } } },
            yAxis: { type: 'value', axisLabel: { color: AX }, splitLine: { lineStyle: { color: GRID } } },
            tooltip: Object.assign({ trigger: 'axis', axisPointer: { type: 'shadow' } }, TIP),
            series: [
                { type: 'bar', stack: 'w', itemStyle: { color: 'rgba(255,255,255,0.03)' }, data: base, tooltip: { show: false }, silent: true },
                {
                    type: 'bar', stack: 'w', barWidth: '46%', itemStyle: { borderRadius: [8, 8, 0, 0] },
                    data: [
                        { value: vals[0], itemStyle: { color: grad('#48BB78', '#2f9e63') } },
                        { value: vals[1], itemStyle: { color: grad('#ED8936', '#c56a21') } },
                        { value: vals[2], itemStyle: { color: grad('#F56565', '#c93e3e') } },
                        { value: vals[3], itemStyle: { color: grad('#48BB78', '#2f9e63') } }
                    ],
                    label: { show: true, position: 'top', color: '#E2E8F0', fontSize: 10 }
                }
            ]
        });
    }

    /* ---------- Карта просрочки ---------- */
    function renderHeatmap(chart, d) {
        var max = d.data.length ? Math.max.apply(null, d.data.map(function (x) { return x[2]; })) : 1;
        chart.setOption({
            grid: { left: 8, right: 14, top: 8, bottom: 34, containLabel: true },
            xAxis: { type: 'category', data: d.days, axisLabel: { color: AX }, splitArea: { show: true, areaStyle: { color: ['rgba(255,255,255,0.015)', 'rgba(255,255,255,0)'] } } },
            yAxis: { type: 'category', data: d.products, axisLabel: { color: AX, fontSize: 10 } },
            visualMap: {
                min: 0, max: max, calculable: true, orient: 'horizontal', left: 'center', bottom: 0,
                inRange: { color: ['#1d3a2a', '#ED8936', '#F56565'] }, textStyle: { color: AX }
            },
            tooltip: Object.assign({ position: 'top' }, TIP),
            series: [{
                type: 'heatmap', data: d.data, label: { show: true, color: '#E2E8F0', fontSize: 9 },
                itemStyle: { borderRadius: 4, borderColor: '#171a21', borderWidth: 1 },
                emphasis: { itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0,0,0,.6)' } }
            }]
        });
    }

    var renders = { river: renderRiver, pulse: renderPulse, waterfall: renderWaterfall, heatmap: renderHeatmap };

    function isEmpty(d, type) {
        if (type === 'river') return !d || !d.length;
        if (type === 'heatmap') return !d || !d.data || !d.data.length;
        if (type === 'pulse') return !d || !d.values || !d.values.some(function (v) { return v > 0; });
        if (type === 'waterfall') return !d || (d.byOrder === 0 && d.byExpired === 0);
        return false;
    }

    function initOne(cfg) {
        var el = document.getElementById(cfg.el);
        if (!el) return;
        fetch(cfg.url)
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (d) {
                hide(cfg.skel);
                if (isEmpty(d, cfg.type)) { show(cfg.empty); return; }
                el.style.display = 'block';
                var chart = echarts.init(el);
                renders[cfg.type](chart, d);
                attachResizer(chart, el);
            })
            .catch(function (e) {
                hide(cfg.skel);
                var err = document.getElementById(cfg.err);
                if (err) { err.textContent = 'Не удалось загрузить данные: ' + e.message; err.style.display = 'flex'; }
            });
    }

    /* ---------- мини-спарклайн KPI (canvas, случайная волна для объёма) ---------- */
    function drawSparkline(cv) {
        var c = cv.getContext('2d'), W = cv.width = 90, H = cv.height = 34;
        c.clearRect(0, 0, W, H);
        c.beginPath();
        var seed = cv.closest('.sv-kpi-p').querySelector('[data-count]')?.dataset.count || '3';
        var n = parseInt(seed, 10) || 1;
        for (var x = 0; x <= W; x += 3) {
            var y = H - 4 - (Math.sin(x * 0.15 + n) * 0.5 + 0.5) * (H - 10);
            x === 0 ? c.moveTo(x, y) : c.lineTo(x, y);
        }
        c.strokeStyle = '#ED8936'; c.lineWidth = 1.5; c.stroke();
    }

    window.SvCharts = {
        initAll: function (map) { Object.keys(map).forEach(function (k) { initOne(map[k]); }); },
        drawSparkline: drawSparkline
    };
})();
