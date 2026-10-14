(function () {
  'use strict';
  var root = document.documentElement;
  var stored = localStorage.getItem('sv-theme') || 'A';

  // Логотипы тем: A «Скан» — LED-сегменты, B «Стебель» — знак с листом.
  // Переключение подменой src (data-src-a/b в разметке), без CSS-фильтров.
  function applyTheme(theme) {
    var isB = theme === 'B';
    root.classList.toggle('theme-b', isB);
    root.classList.toggle('theme-a', !isB);
    root.dataset.theme = theme;
    localStorage.setItem('sv-theme', theme);
    document.querySelectorAll('[data-theme-label]').forEach(function (el) {
      el.textContent = isB ? 'Тема B' : 'Тема A';
    });
    document.querySelectorAll('[data-theme-sub]').forEach(function (el) {
      el.textContent = isB ? 'Стебель · зелень' : 'Скан · оранж';
    });
    // Фоновая иллюстрация логина: A — key-visual, B — kv-warehouse (зелёная)
    document.querySelectorAll('[data-theme-art]').forEach(function (el) {
      var src = isB ? el.dataset.srcB : el.dataset.srcA;
      if (src) el.src = src;
    });
  }
  applyTheme(stored);

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-theme-toggle]');
    if (toggle) applyTheme(root.dataset.theme === 'B' ? 'A' : 'B');
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
    var url = location.origin + '/Inventory/StopList';
    if (qrLink) { qrLink.href = url; qrLink.textContent = url; }
    if (qrBox && typeof QRCode !== 'undefined') {
      qrBox.innerHTML = '';
      new QRCode(qrBox, { text: url, width: 200, height: 200, colorDark: '#0f1115', colorLight: '#F3EEE4', correctLevel: QRCode.CorrectLevel.M });
    } else if (qrBox) {
      qrBox.innerHTML = '<div class="sv-bell-empty">QR-библиотека не загрузилась — ссылка ниже.</div>';
    }
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
