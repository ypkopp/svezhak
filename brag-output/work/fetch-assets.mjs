import { chromium } from 'playwright';
import fs from 'fs';

const OUT = 'c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work';
const urls = [
  'https://umirhack.tech/img/shots/01-dashboard.png',
  'https://umirhack.tech/img/shots/02-sklad.png',
  'https://umirhack.tech/img/shots/05-stop.png',
  'https://umirhack.tech/img/shots/16-dashboard-paper.png',
  'https://umirhack.tech/img/shots/17-dashboard-matcha.png',
  'https://umirhack.tech/img/shots/18-dashboard-lilac.png',
  'https://umirhack.tech/img/brand/m-avo.png',
  'https://umirhack.tech/img/brand/m-salmon.png',
  'https://umirhack.tech/img/brand/m-lemon.png',
  'https://umirhack.tech/img/brand/m-onigiri.png',
  'https://umirhack.tech/img/brand/st-hot.png',
  'https://umirhack.tech/img/brand/st-stop.png',
  'https://umirhack.tech/img/brand/st-check.png',
  'https://umirhack.tech/img/brand/svezhak-logo-light.svg',
  'https://umirhack.tech/img/brand/kv-collab.png',
];
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
const page = await browser.newPage();
for (const u of urls) {
  const name = u.split('/').pop();
  try {
    const resp = await page.request.get(u);
    const buf = await resp.body();
    fs.writeFileSync(`${OUT}/${name}`, buf);
    console.log('ok', name, buf.length);
  } catch (e) { console.log('FAIL', name, e.message); }
}
// capture mid-page sections of the live site for texture
const p2 = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
await p2.goto('https://umirhack.tech', { waitUntil: 'networkidle', timeout: 30000 }).catch(()=>{});
await p2.waitForTimeout(2500);
const stops = [['prod',900],['iface',1900],['chars',3000],['lead',3900]];
for (const [n,y] of stops) {
  await p2.evaluate(v => window.scrollTo(0, v), y);
  await p2.waitForTimeout(900);
  await p2.screenshot({ path: `${OUT}/site-${n}.png` });
}
await browser.close();
console.log('done');
