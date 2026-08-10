/* ============================================================================
   JobHunter runbook — shared behaviour
   - theme (dark default, OS-aware, persisted)   key: jh-theme
   - language (en/uk, duplicated blocks, persisted) key: jh-lang
   - sidebar navigation injected from a shared site map
   - Mermaid: vendored, rendered on the visible-language page only, and
     re-rendered whenever theme or language changes.
   The <head> of every page pins theme+lang before first paint (no flash).
   ========================================================================== */
(function () {
  'use strict';

  /* ---- persistence helpers (localStorage may throw in private mode) ---- */
  function save(k, v) { try { localStorage.setItem(k, v); } catch (e) {} }
  function load(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }

  /* ---- the site map: groups → pages. `id` matches <body data-page>. ---- */
  var SITE = [
    { group: { en: 'Start here', uk: 'Початок' }, items: [
      { id: 'index',        href: 'index.html',        en: 'Overview',                 uk: 'Огляд' },
      { id: 'concepts',     href: 'concepts.html',     en: 'Concepts & vocabulary',    uk: 'Поняття й словник' }
    ]},
    { group: { en: 'How it is built', uk: 'Як це побудовано' }, items: [
      { id: 'architecture', href: 'architecture.html', en: 'Architecture',             uk: 'Архітектура' },
      { id: 'decisions',    href: 'decisions.html',    en: 'Decisions (ADRs)',         uk: 'Рішення (ADR)' }
    ]},
    { group: { en: 'Run it', uk: 'Запуск' }, items: [
      { id: 'setup',        href: 'setup.html',        en: 'Setup & configuration',    uk: 'Встановлення й конфіг' },
      { id: 'operation',    href: 'operation.html',    en: 'End-to-end operation',     uk: 'Робота від А до Я' }
    ]},
    { group: { en: 'Operate it', uk: 'Експлуатація' }, items: [
      { id: 'runbook',      href: 'runbook.html',      en: 'Deploy & operate',         uk: 'Деплой та експлуатація' },
      { id: 'cost',         href: 'cost.html',         en: 'Cost',                     uk: 'Вартість' }
    ]}
  ];

  /* ---- current UI state ---- */
  var lang = document.documentElement.getAttribute('lang') === 'uk' ? 'uk' : 'en';
  var currentPage = document.body.getAttribute('data-page') || 'index';

  /* ---- language: show/hide the duplicated .page blocks ---- */
  function applyLang(l) {
    var pages = document.querySelectorAll('.page[data-lang]');
    for (var i = 0; i < pages.length; i++) {
      var isActive = pages[i].getAttribute('data-lang') === l;
      pages[i].classList.toggle('i18n-hide', !isActive);
    }
    document.documentElement.setAttribute('lang', l);
    // Localise elements that live outside the page blocks (toggle labels stay fixed).
    var localisable = document.querySelectorAll('[data-en][data-uk]');
    for (var j = 0; j < localisable.length; j++) {
      localisable[j].textContent = localisable[j].getAttribute('data-' + l) || localisable[j].textContent;
    }
  }
  function setLang(l) {
    if (l !== 'en' && l !== 'uk') return;
    lang = l;
    save('jh-lang', l);
    applyLang(l);
    syncLangButtons();
    buildNav();
    renderMermaid();
  }
  function syncLangButtons() {
    var en = document.getElementById('lang-en'), uk = document.getElementById('lang-uk');
    if (en) en.classList.toggle('on', lang === 'en');
    if (uk) uk.classList.toggle('on', lang === 'uk');
  }

  /* ---- theme ---- */
  function setTheme(t) {
    if (t !== 'light' && t !== 'dark') return;
    document.documentElement.setAttribute('data-theme', t);
    save('jh-theme', t);
    syncThemeButtons();
    renderMermaid();
  }
  function effectiveTheme() {
    var pinned = document.documentElement.getAttribute('data-theme');
    if (pinned === 'light' || pinned === 'dark') return pinned;
    return (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches) ? 'light' : 'dark';
  }
  function syncThemeButtons() {
    var eff = effectiveTheme();
    var l = document.getElementById('th-light'), d = document.getElementById('th-dark');
    if (l) l.classList.toggle('on', eff === 'light');
    if (d) d.classList.toggle('on', eff === 'dark');
  }

  /* ---- sidebar navigation ---- */
  function buildNav() {
    var nav = document.getElementById('toc');
    if (!nav) return;
    nav.innerHTML = '';
    SITE.forEach(function (grp) {
      var g = document.createElement('div');
      g.className = 'grp';
      g.textContent = grp.group[lang] || grp.group.en;
      nav.appendChild(g);
      grp.items.forEach(function (it) {
        var a = document.createElement('a');
        a.href = it.href;
        if (it.id === currentPage) a.className = 'active';
        a.innerHTML = '<span>' + (it[lang] || it.en) + '</span>';
        nav.appendChild(a);
      });
    });
  }

  /* ---- Mermaid ---- */
  function mermaidThemeVars() {
    var light = effectiveTheme() === 'light';
    // Palette mirrors the site CSS variables so diagrams sit in the page, not on it.
    var clay = '#cc785c';
    if (light) {
      return {
        background: '#ffffff', primaryColor: '#fbf3ef', primaryBorderColor: clay,
        primaryTextColor: '#1f1e1d', lineColor: '#b98a78', secondaryColor: '#f2f0e9',
        tertiaryColor: '#faf9f5', tertiaryBorderColor: '#e7e3d9', tertiaryTextColor: '#1f1e1d',
        noteBkgColor: '#f7efdd', noteTextColor: '#6b4e14', noteBorderColor: '#9a6a1c',
        actorBkg: '#fbf3ef', actorBorder: clay, actorTextColor: '#1f1e1d',
        signalColor: '#55524c', signalTextColor: '#1f1e1d', labelBoxBkgColor: '#f2f0e9',
        labelBoxBorderColor: '#e7e3d9', labelTextColor: '#1f1e1d', clusterBkg: '#f5f2ea',
        clusterBorder: '#e0d8c8', edgeLabelBackground: '#faf9f5', fontSize: '14px'
      };
    }
    return {
      background: '#24221f', primaryColor: '#2b2320', primaryBorderColor: clay,
      primaryTextColor: '#f0ede4', lineColor: '#8f8a80', secondaryColor: '#2d2b28',
      tertiaryColor: '#292826', tertiaryBorderColor: '#3a3833', tertiaryTextColor: '#f0ede4',
      noteBkgColor: '#33291a', noteTextColor: '#e8c98a', noteBorderColor: '#ca9a4e',
      actorBkg: '#2b2320', actorBorder: clay, actorTextColor: '#f0ede4',
      signalColor: '#c4bfb4', signalTextColor: '#f0ede4', labelBoxBkgColor: '#2d2b28',
      labelBoxBorderColor: '#3a3833', labelTextColor: '#f0ede4', clusterBkg: '#26241f',
      clusterBorder: '#3a3833', edgeLabelBackground: '#24221f', fontSize: '14px'
    };
  }

  var mermaidReady = false;
  function renderMermaid() {
    if (typeof window.mermaid === 'undefined') return;
    if (!mermaidReady) {
      window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', fontFamily: 'inherit' });
      mermaidReady = true;
    }
    window.mermaid.initialize({
      startOnLoad: false, securityLevel: 'strict', fontFamily: 'inherit',
      theme: 'base', themeVariables: mermaidThemeVars(),
      flowchart: { useMaxWidth: true, htmlLabels: true, curve: 'basis' },
      sequence: { useMaxWidth: true, mirrorActors: false }
    });
    // Only render diagrams inside the currently-visible language page.
    var nodes = [];
    var frames = document.querySelectorAll('.page:not(.i18n-hide) pre.mermaid');
    for (var i = 0; i < frames.length; i++) {
      var el = frames[i];
      var src = el.getAttribute('data-src');
      if (src === null) { el.setAttribute('data-src', el.textContent); src = el.textContent; }
      el.removeAttribute('data-processed');
      el.innerHTML = src;
      nodes.push(el);
    }
    if (nodes.length) {
      try { window.mermaid.run({ nodes: nodes }); } catch (e) { /* leave source visible on failure */ }
    }
  }

  /* ---- wiring ---- */
  function wire() {
    var map = { 'th-light': function () { setTheme('light'); }, 'th-dark': function () { setTheme('dark'); },
                'lang-en': function () { setLang('en'); }, 'lang-uk': function () { setLang('uk'); } };
    Object.keys(map).forEach(function (id) { var el = document.getElementById(id); if (el) el.onclick = map[id]; });

    if (window.matchMedia) {
      var mq = window.matchMedia('(prefers-color-scheme: light)');
      var onMq = function () {
        if (!document.documentElement.getAttribute('data-theme')) { syncThemeButtons(); renderMermaid(); }
      };
      if (mq.addEventListener) mq.addEventListener('change', onMq); else if (mq.addListener) mq.addListener(onMq);
    }
  }

  /* ---- boot (state already applied to <html> by the head script) ---- */
  function boot() {
    buildNav();
    syncThemeButtons();
    syncLangButtons();
    applyLang(lang);
    wire();
    renderMermaid();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
