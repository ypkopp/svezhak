(function () {
  'use strict';
  var root = document.documentElement;
  var stored = localStorage.getItem('sv-theme') || 'A';

  // Мини-лого в кнобе переключателя: A — скан, B — стебель
  var KNOB = { A: '/img/brand/logo-1a-sm.png', B: '/img/brand/logo-1b-sm.png' };

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
    document.querySelectorAll('[data-theme-knob]').forEach(function (el) {
      el.src = KNOB[isB ? 'B' : 'A'];
    });
    // Логотип в сайдбаре меняется по теме (данные data-src-a/b заданы в разметке)
    document.querySelectorAll('[data-theme-logo]').forEach(function (el) {
      var src = isB ? el.dataset.srcB : el.dataset.srcA;
      if (src) el.src = src;
    });
  }
  applyTheme(stored);

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-theme-toggle]');
    if (toggle) {
      applyTheme(root.dataset.theme === 'B' ? 'A' : 'B');
    }
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

  /* ================= ⌘K — command palette ================= */
  var searchInput = document.querySelector('[data-global-search]');
  var searchDrop = document.getElementById('searchDrop');
  var searchTimer = null;

  function closeSearch() { if (searchDrop) { searchDrop.hidden = true; searchDrop.innerHTML = ''; } }
  function renderResults(list) {
    if (!searchDrop) return;
    if (!list.length) {
      searchDrop.innerHTML = '<div class="sv-search-empty">Ничего не найдено</div>';
      searchDrop.hidden = false;
      return;
    }
    var icons = { dish: '▤', product: '▦', delivery: '⇩' };
    searchDrop.innerHTML = list.map(function (r) {
      return '<a class="sv-search-item" href="' + r.url + '"><span class="sv-search-ic">' + (icons[r.type] || '⌕') + '</span><span class="sv-search-name">' + r.name + '</span><span class="sv-search-meta">' + (r.meta || '') + '</span></a>';
    }).join('');
    searchDrop.hidden = false;
  }
  function doSearch(q) {
    if (!q || q.length < 2) { closeSearch(); return; }
    fetch('/Inventory/Search?q=' + encodeURIComponent(q))
      .then(function (r) { return r.json(); })
      .then(function (d) { renderResults(d.results || []); })
      .catch(function () { closeSearch(); });
  }
  if (searchInput) {
    searchInput.addEventListener('input', function () {
      clearTimeout(searchTimer);
      var q = this.value.trim();
      searchTimer = setTimeout(function () { doSearch(q); }, 200);
    });
    searchInput.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { this.blur(); closeSearch(); }
      if (e.key === 'Enter') {
        var first = searchDrop && searchDrop.querySelector('.sv-search-item');
        if (first) { window.location.href = first.href; }
      }
    });
    document.addEventListener('click', function (e) {
      if (!e.target.closest('#searchWrap')) closeSearch();
    });
  }
  document.addEventListener('keydown', function (event) {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (searchInput) { searchInput.focus(); searchInput.select(); }
    }
  });

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
      return '<a class="sv-bell-item" href="' + n.url + '" style="--c:' + sevColor(n.severity) + '">' +
        '<img src="/img/brand/' + n.icon + '" alt=""/>' +
        '<span class="sv-bell-tx"><b>' + n.title + '</b><small>' + n.text + '</small></span>' +
        '<i class="sv-bell-dotc"></i></a>';
    }).join('');
  }
  function unreadCount(items) {
    var read = Number(localStorage.getItem(NOTIF_KEY) || 0);
    return items.filter(function (n, i) { return i >= read; }).length;
  }
  function loadBell() {
    fetch('/Inventory/Notifications')
      .then(function (r) { return r.json(); })
      .then(function (d) {
        bellData = d.items || [];
        renderBell(bellData);
        var unread = unreadCount(bellData);
        if (bellDot) bellDot.hidden = unread === 0;
      })
      .catch(function () {
        if (bellList) bellList.innerHTML = '<div class="sv-bell-empty">Не удалось загрузить.</div>';
      });
  }
  if (bellBtn) {
    bellBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = bellDrop && !bellDrop.hidden;
      if (bellDrop) bellDrop.hidden = open;
      if (!open && !bellData) loadBell();
    });
    document.addEventListener('click', function (e) {
      if (bellDrop && !e.target.closest('.sv-bell-wrap')) bellDrop.hidden = true;
    });
    if (bellRead) bellRead.addEventListener('click', function (e) {
      e.stopPropagation();
      localStorage.setItem(NOTIF_KEY, String((bellData || []).length));
      if (bellDot) bellDot.hidden = true;
      if (bellDrop) bellDrop.hidden = true;
    });
    // подгружаем счётчик сразу
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
