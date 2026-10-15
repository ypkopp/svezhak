import { chromium } from 'playwright';
const W='c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work';
const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
const page = await browser.newPage({ viewport:{width:1920,height:1080}, deviceScaleFactor:1 });
await page.goto('file:///'+W+'/render.html');
await page.waitForFunction('window.__ready===true',{timeout:20000});
for (const t of [2.0, 4.5, 8.5, 12, 15.5, 18.5]) {
  await page.evaluate(tt=>window.__frame(tt), t);
  await page.waitForTimeout(120);
  await page.screenshot({ path:`${W}/probe-${t}.png` });
  console.log('shot', t);
}
await browser.close();
