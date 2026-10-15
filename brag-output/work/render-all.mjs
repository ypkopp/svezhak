import { chromium } from 'playwright';
import fs from 'fs';
const W='c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work';
fs.mkdirSync(W+'/frames',{recursive:true});
const DUR=20, FPS=30, N=DUR*FPS;
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe'});
const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
await page.goto('file:///'+W+'/render.html');
await page.waitForFunction('window.__ready===true',{timeout:20000});
const START=parseInt(process.env.START||'0',10);
for(let f=START;f<N;f++){
  const t=f/FPS;
  await page.evaluate(tt=>window.__frame(tt),t);
  const name=`${W}/frames/f${String(f).padStart(4,'0')}.jpg`;
  await page.screenshot({path:name,type:'jpeg',quality:92,animations:'disabled'});
  if(f%60===0)console.log('frame',f,'/',N);
}
await browser.close();
console.log('done frames');
