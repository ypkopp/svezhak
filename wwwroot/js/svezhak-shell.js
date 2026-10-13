(function () {
  'use strict';
  var root = document.documentElement;
  var stored = localStorage.getItem('sv-theme') || 'A';
  function applyTheme(theme) {
    var isB = theme === 'B';
    root.classList.toggle('theme-b', isB);
    root.classList.toggle('theme-a', !isB);
    root.dataset.theme = theme;
    localStorage.setItem('sv-theme', theme);
    document.querySelectorAll('[data-theme-label]').forEach(function (el) {
      el.textContent = isB ? 'Тема B · Стебель' : 'Тема A · Скан';
    });
    document.querySelectorAll('[data-theme-icon]').forEach(function (el) {
      el.textContent = isB ? '⌁' : '◉';
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

  document.addEventListener('keydown', function (event) {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      var search = document.querySelector('[data-global-search]');
      if (search) { search.focus(); search.select(); }
    }
  });

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
