import { chromium } from 'playwright';
import fs from 'fs';

const OUT = 'c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work';
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });

await page.goto('https://umirhack.tech', { waitUntil: 'domcontentloaded', timeout: 30000 }).catch(e => console.log('nav err', e.message));
await page.waitForTimeout(4000);

// dump title, meta, styles, computed colors/fonts
const meta = await page.evaluate(() => {
  const g = s => { const e = document.querySelector(s); return e ? (e.content || e.textContent) : null; };
  const cs = getComputedStyle(document.body);
  const roots = {};
  const rs = getComputedStyle(document.documentElement);
  for (const n of rs) { /* noop */ }
  return {
    title: document.title,
    desc: g('meta[name=description]') || g('meta[property="og:description"]'),
    ogTitle: g('meta[property="og:title"]'),
    bg: cs.backgroundColor, color: cs.color, fontFamily: cs.fontFamily,
    text: document.body.innerText.slice(0, 4000),
    h1: [...document.querySelectorAll('h1,h2,h3')].map(h => h.innerText.trim()).slice(0, 30),
    links: [...document.querySelectorAll('a[href]')].map(a => a.href).slice(0, 40),
    imgs: [...document.querySelectorAll('img')].map(i => i.src).slice(0, 40),
  };
});
fs.writeFileSync(OUT + '/site-meta.json', JSON.stringify(meta, null, 2));

// full-page scroll capture: screenshot hero + sections
await page.screenshot({ path: OUT + '/site-hero.png' });
// scroll to bottom progressively to trigger lazy content
await page.evaluate(async () => {
  await new Promise(res => { let y = 0; const t = setInterval(() => { y += 600; window.scrollTo(0, y); if (y > document.body.scrollHeight) { clearInterval(t); res(); } }, 60); });
});
await page.waitForTimeout(1200);
const h = await page.evaluate(() => document.body.scrollHeight);
console.log('page height', h);
fs.writeFileSync(OUT + '/site-html.html', await page.content());
console.log('TITLE:', meta.title);
console.log('H1/H2:', JSON.stringify(meta.h1));
await browser.close();
