(function () {
  'use strict';
  var root = document.documentElement;

  /* ===== 5 тем v6: night / paper / mono / matcha / lilac ===== */
  var THEMES = [
    { id: 'night', n: 'Ночь', c: ['#0f1115', '#ED8936', '#48BB78'] },
    { id: 'paper', n: 'Бумага', c: ['#F6F4EF', '#E07524', '#2F9E5F'] },
    { id: 'mono', n: 'Ч/Б', c: ['#000', '#fff', '#777'] },
    { id: 'matcha', n: 'Матча', c: ['#0B1712', '#9BE15D', '#3FD3A0'] },
    { id: 'lilac', n: 'Лаванда', c: ['#120F1C', '#FF8FB1', '#8B7CF6'] },
  ];
  var stored = localStorage.getItem('sv6-theme');
  if (!stored) { var old = localStorage.getItem('sv-theme'); stored = old === 'B' ? 'matcha' : 'night'; }

  function applyTheme(id) {
    root.dataset.theme = id;
    try { localStorage.setItem('sv6-theme', id); } catch (e) { }
    document.querySelectorAll('#themeSwatches button, #themeSwatchesIn button').forEach(function (b) {
      b.classList.toggle('on', b.dataset.themeId === id);
    });
  }

  function buildSwatches(seg) {
    if (!seg) return;
    THEMES.forEach(function (t) {
      var b = document.createElement('button');
      b.type = 'button'; b.dataset.themeId = t.id; b.title = t.n;
      b.innerHTML = '<span style="width:14px;height:14px;border-radius:5px;display:block;background:conic-gradient(' + t.c[1] + ' 0 50%,' + t.c[2] + ' 0 75%,' + t.c[0] + ' 0);border:1px solid var(--stroke2)"></span>';
      b.addEventListener('click', function () { applyTheme(t.id); });
      seg.appendChild(b);
    });
  }
  buildSwatches(document.getElementById('themeSwatches'));
  buildSwatches(document.getElementById('themeSwatchesIn'));
  applyTheme(stored);

  // Логотипы тем для data-theme-art (логин): night/paper/mono — тёмный, matcha/lilac — светлый
  document.querySelectorAll('[data-theme-art]').forEach(function (el) {
    var dark = stored !== 'matcha' && stored !== 'lilac';
    var src = dark ? el.dataset.srcA : el.dataset.srcB;
    if (src) el.src = src;
  });

  function pad(value) { return String(value).padStart(2, '0'); }
  function updateClock() {
    var now = new Date();
    document.querySelectorAll('[data-led-clock]').forEach(function (el) {
      el.textContent = pad(now.getHours()) + (now.getSeconds() % 2 ? ' ' : ':') + pad(now.getMinutes());
    });
  }
  updateClock();
  window.setInterval(updateClock, 1000);

  /* ================= ⌘K — палитра быстрых действий ================= */
  var ACTIONS = [
    { t: 'Новый заказ', hint: 'оформить заказ', icon: '▤', url: '/Inventory/Order' },
    { t: 'Новая поставка', hint: 'принять партию', icon: '⇩', url: '/Inventory/Deliveries' },
    { t: 'Открыть склад', hint: 'остатки по партиям', icon: '▦', url: '/Inventory/Stock' },
    { t: 'Стоп-лист', hint: 'что нельзя продавать', icon: '⊘', url: '/Inventory/StopList' },
    { t: 'Советник', hint: 'ИИ-инсайты склада', icon: '✦', url: '/Inventory/Advisor' },
    { t: 'Дашборд', hint: 'обзор смены', icon: '⌂', url: '/Inventory' },
    { t: 'Сотрудники', hint: 'управление доступом', icon: '⚙', url: '/Admin/Employees' },
    { t: 'Расписание', hint: 'смены недели', icon: '⚙', url: '/Admin/Schedule' },
    { t: 'Рестораны', hint: 'управление точками', icon: '⚙', url: '/Admin/Restaurants' },
    { t: 'Сменить тему', hint: 'A «Скан» ↔ B «Стебель»', icon: '◐', action: 'theme' },
    { t: 'QR для кухни', hint: 'стоп-лист на телефон', icon: '⌗', action: 'qr' },
    { t: 'Сбросить демо', hint: 'перезагрузить данные', icon: '↺', action: 'reset' },
    { t: 'Выйти', hint: 'завершить смену', icon: '↪', action: 'logout' }
  ];
  var palette = document.getElementById('cmdPalette');
  var cmdInput = document.getElementById('cmdInput');
  var cmdList = document.getElementById('cmdList');
  var cmdSel = 0;

  function openPalette() {
    if (!palette) return;
    palette.hidden = false;
    cmdInput.value = '';
    renderCmd('');
    setTimeout(function () { cmdInput.focus(); }, 10);
  }
  function closePalette() { if (palette) palette.hidden = true; }
  function renderCmd(q) {
    q = (q || '').toLowerCase();
    var list = ACTIONS.filter(function (a) { return !q || a.t.toLowerCase().includes(q) || a.hint.toLowerCase().includes(q); });
    if (!list.length) { cmdList.innerHTML = '<div class="sv-cmd-empty">Ничего не найдено</div>'; return; }
    cmdSel = Math.min(cmdSel, list.length - 1);
    cmdList.innerHTML = list.map(function (a, i) {
      return '<button type="button" class="sv-cmd-item' + (i === cmdSel ? ' sel' : '') + '" data-i="' + i + '"><span class="sv-cmd-ic">' + a.icon + '</span><span class="sv-cmd-t">' + a.t + '</span><span class="sv-cmd-h">' + a.hint + '</span></button>';
    }).join('');
    cmdList.querySelectorAll('.sv-cmd-item').forEach(function (b, i) {
      b.addEventListener('click', function () { run(list[i]); });
      b.addEventListener('mouseenter', function () { cmdSel = i; markSel(list); });
    });
    cmdList._items = list;
  }
  function markSel(list) {
    cmdList.querySelectorAll('.sv-cmd-item').forEach(function (b, i) { b.classList.toggle('sel', i === cmdSel); });
  }
  function run(a) {
    if (!a) return;
    if (a.url) { window.location.href = a.url; return; }
    closePalette();
    if (a.action === 'theme') applyTheme(root.dataset.theme === 'B' ? 'A' : 'B');
    else if (a.action === 'qr') openQr();
    else if (a.action === 'reset') { var f = document.querySelector('form[action*="ResetDemo"]'); if (f) f.requestSubmit(); }
    else if (a.action === 'logout') { var lf = document.querySelector('form[action*="Logout"]'); if (lf) lf.requestSubmit(); }
  }

  var trigger = document.getElementById('cmdTrigger');
  if (trigger) {
    trigger.addEventListener('click', openPalette);
    trigger.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openPalette(); } });
  }
  if (cmdInput) {
    cmdInput.addEventListener('input', function () { cmdSel = 0; renderCmd(this.value); });
    cmdInput.addEventListener('keydown', function (e) {
      var list = cmdList._items || [];
      if (e.key === 'Escape') { closePalette(); }
      else if (e.key === 'ArrowDown') { e.preventDefault(); cmdSel = Math.min(cmdSel + 1, list.length - 1); markSel(list); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); cmdSel = Math.max(cmdSel - 1, 0); markSel(list); }
      else if (e.key === 'Enter') { e.preventDefault(); run(list[cmdSel]); }
    });
  }
  if (palette) palette.addEventListener('click', function (e) { if (e.target === palette) closePalette(); });
  document.addEventListener('keydown', function (e) {
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); if (palette && !palette.hidden) closePalette(); else openPalette(); }
    if (e.key === 'Escape' && palette && !palette.hidden) closePalette();
  });

  /* ================= QR для кухни ================= */
  var qrModal = document.getElementById('qrModal');
  var qrBox = document.getElementById('qrBox');
  var qrLink = document.getElementById('qrLink');
  function openQr() {
    if (!qrModal) return;
    qrModal.hidden = false;
    if (qrLink) qrLink.textContent = '…';
    // Терминал кухни — публичная read-only ссылка по токену ресторана
    fetch('/Inventory/KitchenLink').then(function (r) { return r.json(); }).then(function (d) {
      var url = d.url || (location.origin + '/Kitchen');
      if (qrLink) { qrLink.href = url; qrLink.textContent = url; }
      if (qrBox && typeof QRCode !== 'undefined') {
        qrBox.innerHTML = '';
        new QRCode(qrBox, { text: url, width: 200, height: 200, colorDark: '#0f1115', colorLight: '#F3EEE4', correctLevel: QRCode.CorrectLevel.M });
      } else if (qrBox) {
        qrBox.innerHTML = '<div class="sv-bell-empty">QR-библиотека не загрузилась — ссылка ниже.</div>';
      }
    }).catch(function () { if (qrLink) qrLink.textContent = 'не удалось получить ссылку'; });
  }
  var qrBtn = document.getElementById('qrBtn');
  if (qrBtn) qrBtn.addEventListener('click', openQr);
  var qrClose = document.getElementById('qrClose');
  if (qrClose) qrClose.addEventListener('click', function () { qrModal.hidden = true; });
  if (qrModal) qrModal.addEventListener('click', function (e) { if (e.target === qrModal) qrModal.hidden = true; });
  var qrPrint = document.getElementById('qrPrint');
  if (qrPrint) qrPrint.addEventListener('click', function () { window.print(); });

  /* ================= Колокольчик — уведомления ================= */
  var bellBtn = document.getElementById('bellBtn');
  var bellDrop = document.getElementById('bellDrop');
  var bellDot = document.getElementById('bellDot');
  var bellList = document.getElementById('bellList');
  var bellRead = document.getElementById('bellRead');
  var NOTIF_KEY = 'sv-notif-read';
  var bellData = null;
  function sevColor(s) { return s === 'high' ? 'var(--rd)' : s === 'medium' ? 'var(--ye)' : 'var(--mut)'; }
  function renderBell(items) {
    if (!bellList) return;
    if (!items.length) { bellList.innerHTML = '<div class="sv-bell-empty">Всё спокойно — нет новых событий.</div>'; return; }
    bellList.innerHTML = items.map(function (n) {
      return '<a class="sv-bell-item" href="' + n.url + '" style="--c:' + sevColor(n.severity) + '"><img src="/img/brand/' + n.icon + '" alt=""/><span class="sv-bell-tx"><b>' + n.title + '</b><small>' + n.text + '</small></span><i class="sv-bell-dotc"></i></a>';
    }).join('');
  }
  function loadBell() {
    fetch('/Inventory/Notifications')
      .then(function (r) { return r.json(); })
      .then(function (d) {
        bellData = d.items || [];
        renderBell(bellData);
        var read = Number(localStorage.getItem(NOTIF_KEY) || 0);
        if (bellDot) bellDot.hidden = bellData.length <= read;
      })
      .catch(function () { if (bellList) bellList.innerHTML = '<div class="sv-bell-empty">Не удалось загрузить.</div>'; });
  }
  if (bellBtn) {
    bellBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = bellDrop && !bellDrop.hidden;
      if (bellDrop) bellDrop.hidden = open;
      if (!open && !bellData) loadBell();
    });
    document.addEventListener('click', function (e) { if (bellDrop && !e.target.closest('.sv-bell-wrap')) bellDrop.hidden = true; });
    if (bellRead) bellRead.addEventListener('click', function (e) {
      e.stopPropagation();
      localStorage.setItem(NOTIF_KEY, String((bellData || []).length));
      if (bellDot) bellDot.hidden = true;
      if (bellDrop) bellDrop.hidden = true;
    });
    loadBell();
  }

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.fade-up').forEach(function (el, index) {
      window.setTimeout(function () { el.classList.add('in'); }, 55 + index * 65);
    });
    document.querySelectorAll('[data-count]').forEach(function (el) {
      var target = Number(el.dataset.count || 0), start = null;
      function tick(now) {
        if (start === null) start = now;
        var progress = Math.min(1, (now - start) / 800);
        el.textContent = Math.round(target * progress);
        if (progress < 1) window.requestAnimationFrame(tick);
      }
      window.requestAnimationFrame(tick);
    });
  });
})();

/* ===== Онбординг менеджера: 5 шагов, показывается один раз (localStorage) ===== */
(function () {
  var modal = document.getElementById('onboard');
  if (!modal) return;
  var KEY = 'sv-onboard-done';
  try { if (localStorage.getItem(KEY)) return; } catch (e) { }

  var STEPS = [
    { img: 'chef-3-cut.png', t: 'Поставка', x: 'Принимайте партии на странице «Поставки»: номер, продукт, количество и срок в часах. Система сама ставит дату истечения.' },
    { img: 'st-fridge.png', t: 'Склад', x: 'На «Складе» партии сгруппированы по продукту и идут по FEFO — самые ранние первыми. Порчу списывайте кнопкой-стоп.' },
    { img: 'st-box.png', t: 'Заказ', x: 'В «Заказе» выбирайте блюда — списание идёт по FEFO, при дефиците заказ отклоняется со списком нехватки.' },
    { img: 'st-stop.png', t: 'Стоп-лист', x: 'Блюдо уходит в стоп, когда не хватает ингредиентов или вы остановили его вручную. Заказ с ним не пройдёт.' },
    { img: 'm3-avo.png', t: 'Советник', x: 'Юзу каждую смену подсказывает: что продать срочно, что заказать, что списать — с расходом и остатками.' },
  ];
  var i = 0;
  var img = document.getElementById('obImg'), step = document.getElementById('obStep'),
      name = document.getElementById('obName'), tx = document.getElementById('obTx'),
      next = document.getElementById('obNext'), prev = document.getElementById('obPrev');
  var imgBase = '/img/brand/';
  function render() {
    var s = STEPS[i];
    img.src = imgBase + s.img; step.textContent = 'шаг ' + (i + 1) + ' / ' + STEPS.length;
    name.textContent = s.t; tx.textContent = s.x;
    prev.disabled = i === 0;
    next.textContent = i === STEPS.length - 1 ? 'Начать работу →' : 'Далее →';
  }
  function done() { try { localStorage.setItem(KEY, '1'); } catch (e) { } modal.hidden = true; }
  next.addEventListener('click', function () { if (i < STEPS.length - 1) { i++; render(); } else done(); });
  prev.addEventListener('click', function () { if (i > 0) { i--; render(); } });
  ['obSkip', 'obSkip2'].forEach(function (id) { var b = document.getElementById(id); if (b) b.addEventListener('click', done); });
  modal.addEventListener('click', function (e) { if (e.target === modal) done(); });
  render();
  modal.hidden = false;
})();

/* ===== ИИ-чат склада ===== */
(function () {
  var btn = document.getElementById('chatBtn'), panel = document.getElementById('chatPanel');
  if (!btn || !panel) return;
  var close = document.getElementById('chatClose'), msgs = document.getElementById('chatMsgs'),
      input = document.getElementById('chatInput'), send = document.getElementById('chatSend');
  function open() { panel.hidden = false; input.focus(); }
  function hide() { panel.hidden = true; }
  btn.addEventListener('click', open);
  close.addEventListener('click', hide);
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !panel.hidden) hide(); });

  function addMsg(role, text) {
    var div = document.createElement('div');
    div.className = 'sv-chat-msg ' + (role === 'user' ? 'sv-chat-user' : 'sv-chat-ai');
    if (role !== 'user') div.innerHTML = '<b>Юзу:</b> ';
    var span = document.createElement('span'); span.textContent = text;
    div.appendChild(span); msgs.appendChild(div); msgs.scrollTop = msgs.scrollHeight;
    return div;
  }

  // Опционально: блок ```chart в ответе → ECharts (если либа уже на странице)
  function tryChart(text) {
    var m = text.match(/```chart\s*([\s\S]*?)```/);
    if (!m) return text;
    try {
      var cfg = JSON.parse(m[1]);
      if (!cfg || !cfg.type || !cfg.data) return text;
      var box = document.createElement('div'); box.style.cssText = 'height:160px;margin-top:8px;';
      var msg = addMsg('ai', ''); msg.appendChild(box);
      if (window.echarts) { var c = echarts.init(box); c.setOption({ backgroundColor: 'transparent', textStyle: { color: '#8b93a7', fontSize: 10 }, title: { text: cfg.title || '', textStyle: { color: '#e8eaf0', fontSize: 11 } }, xAxis: cfg.data.x ? { type: 'category', data: cfg.data.x } : undefined, yAxis: cfg.data.x ? { type: 'value' } : undefined, series: [{ type: cfg.type || 'bar', data: cfg.data.series || cfg.data.y || [] }], grid: { left: 8, right: 8, top: 24, bottom: 8, containLabel: true } }); }
      return text.replace(m[0], '').trim();
    } catch (e) { return text; }
  }

  async function ask() {
    var q = input.value.trim(); if (!q) return;
    input.value = ''; addMsg('user', q);
    var t = addMsg('ai', 'думаю…'); t.classList.add('sv-chat-typing');
    try {
      var r = await fetch('/Inventory/Ask', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ question: q }) });
      var d = await r.json();
      t.remove();
      var answer = d.answer || '…';
      var rest = tryChart(answer);
      addMsg('ai', rest || answer);
    } catch (e) { t.remove(); addMsg('ai', 'Не удалось получить ответ — попробуй ещё раз.'); }
  }
  send.addEventListener('click', ask);
  input.addEventListener('keydown', function (e) { if (e.key === 'Enter') ask(); });

  // Тикет админу: текст последнего вопроса пользователя → POST /Inventory/SupportTicket
  var ticketBtn = document.getElementById('ticketBtn'), ticketStatus = document.getElementById('ticketStatus');
  if (ticketBtn) ticketBtn.addEventListener('click', async function () {
    var last = null;
    msgs.querySelectorAll('.sv-chat-user span').forEach(function (s) { last = s.textContent; });
    if (!last) { if (ticketStatus) ticketStatus.textContent = 'сначала напиши вопрос'; return; }
    if (ticketStatus) ticketStatus.textContent = 'отправляю…';
    try {
      var r = await fetch('/Inventory/SupportTicket', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: last }) });
      if (ticketStatus) ticketStatus.textContent = r.ok ? '✓ админ увидит тикет' : 'не получилось';
    } catch (e) { if (ticketStatus) ticketStatus.textContent = 'не получилось'; }
  });
})();

/* ===== Настройки интерфейса — скрыть элементы, тема перенесена сюда ===== */
(function () {
  var btn = document.getElementById('settingsBtn'), drop = document.getElementById('settingsDrop');
  if (!btn || !drop) return;
  var reset = document.getElementById('settingsReset');
  var K = function (n) { return 'sv-ui-' + n; };
  var get = function (n) { try { return localStorage.getItem(K(n)) === '1'; } catch (e) { return false; } };
  var set = function (n, v) { try { localStorage.setItem(K(n), v ? '1' : '0'); } catch (e) { } };

  function apply() {
    // Скрыть Дашборд из бокового меню
    document.querySelectorAll('.sv-handoff-nav-item').forEach(function (a) {
      var txt = (a.textContent || '').trim();
      if (txt === 'Дашборд' || a.getAttribute('data-i18n') === 'nav.dash' || a.querySelector('[data-i18n="nav.dash"]'))
        a.style.display = get('hideDash') ? 'none' : '';
    });
    // Скрыть свотчи тем в топбаре (тема всё равно доступна в настройках)
    var seg = document.getElementById('themeSwatches');
    if (seg) seg.style.display = get('hideTheme') ? 'none' : '';
    // Скрыть QR (для поваров и вообще)
    var qr = document.getElementById('qrBtn');
    if (qr) qr.style.display = get('hideQr') ? 'none' : '';
    // Скрыть часы
    var clk = document.querySelector('[data-led-clock]');
    if (clk) clk.style.display = get('hideClock') ? 'none' : '';
  }

  btn.addEventListener('click', function (e) { e.stopPropagation(); drop.hidden = !drop.hidden; });
  document.addEventListener('click', function (e) { if (!e.target.closest('.sv-bell-wrap')) drop.hidden = true; });
  if (reset) reset.addEventListener('click', function () {
    ['hideDash', 'hideTheme', 'hideQr', 'hideClock'].forEach(function (n) { set(n, false); });
    document.querySelectorAll('[data-set]').forEach(function (c) { c.checked = false; });
    apply();
  });
  document.querySelectorAll('[data-set]').forEach(function (c) {
    c.checked = get(c.dataset.set);
    c.addEventListener('change', function () { set(c.dataset.set, c.checked); apply(); });
  });
  apply();
})();
