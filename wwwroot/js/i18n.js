/* СВЕЖАК — лёгкий i18n: data-i18n атрибуты + словарь, localStorage['sv-lang'], RU по умолчанию. */
(function () {
  'use strict';
  var DICT = {
    // навигация
    'nav.dash': { ru: 'Дашборд', en: 'Dashboard' },
    'nav.stock': { ru: 'Склад', en: 'Stock' },
    'nav.deliv': { ru: 'Поставки', en: 'Deliveries' },
    'nav.order': { ru: 'Заказ', en: 'Order' },
    'nav.stop': { ru: 'Стоп-лист', en: 'Stop-list' },
    'nav.advisor': { ru: 'Советник', en: 'Advisor' },
    'nav.admin': { ru: 'Админ', en: 'Admin' },
    'nav.logout': { ru: 'Выйти', en: 'Sign out' },
    'nav.reset': { ru: 'Сбросить демо', en: 'Reset demo' },
    // топбар
    'theme.switch': { ru: 'СМЕНИТЬ ТЕМУ', en: 'SWITCH THEME' },
    'cmd.hint': { ru: 'Быстрое действие…', en: 'Quick action…' },
    // KPI / дашборд
    'kpi.pos': { ru: 'позиций на складе', en: 'items in stock' },
    'kpi.exp': { ru: 'истекает <24 ч', en: 'expiring <24 h' },
    'kpi.stop': { ru: 'в стоп-листе', en: 'in stop-list' },
    'kpi.hot': { ru: 'hot offers', en: 'hot offers' },
    // hero
    'hero.threat': { ru: 'Под угрозой', en: 'At risk' },
    'hero.batches': { ru: 'партий', en: 'batches' },
    // кнопки/общие
    'btn.save': { ru: 'Сохранить', en: 'Save' },
    'btn.cancel': { ru: 'Отмена', en: 'Cancel' },
    'btn.submit': { ru: 'Отправить на кухню', en: 'Send to kitchen' },
    // страницы
    'page.stock': { ru: 'Склад', en: 'Stock' },
    'page.deliv': { ru: 'Поставки', en: 'Deliveries' },
    'page.order': { ru: 'Оформление заказа', en: 'New order' },
    'page.stop': { ru: 'Стоп-лист & Hot Offers', en: 'Stop-list & Hot Offers' },
    'page.advisor': { ru: 'Советник склада', en: 'Stock advisor' },
    'page.employees': { ru: 'Сотрудники', en: 'Employees' },
    'page.schedule': { ru: 'Расписание', en: 'Schedule' },
    'page.restaurants': { ru: 'Рестораны', en: 'Restaurants' },
    'page.profile': { ru: 'Профиль сотрудника', en: 'Employee profile' }
  };

  function lang() { return localStorage.getItem('sv-lang') || 'ru'; }
  function apply(l) {
    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      var key = el.dataset.i18n, entry = DICT[key];
      if (entry && entry[l]) el.textContent = entry[l];
    });
    var cur = document.getElementById('langCur');
    if (cur) cur.textContent = l.toUpperCase();
    document.documentElement.lang = l === 'en' ? 'en' : 'ru';
    localStorage.setItem('sv-lang', l);
  }

  document.addEventListener('DOMContentLoaded', function () {
    apply(lang());
    var sw = document.getElementById('langSwitch');
    if (sw) sw.addEventListener('click', function () { apply(lang() === 'ru' ? 'en' : 'ru'); });
  });
  window.SvI18n = { apply: apply, lang: lang };
})();
