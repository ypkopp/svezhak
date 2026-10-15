// synth a 20s track: A-minor pad + soft pulse kick + arp + chime, all in-key.
import { chromium } from 'playwright';
import fs from 'fs';
const W='c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work';
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe'});
const page=await browser.newPage();
const wav=await page.evaluate(async()=>{
  const SR=44100,DUR=20;
  const ctx=new OfflineAudioContext(2,SR*DUR,SR);
  const master=ctx.createDynamicsCompressor();
  const g=ctx.createGain();g.gain.value=0.9;
  g.connect(master);master.connect(ctx.destination);
  const A=110; // A2 root
  const padGain=ctx.createGain();padGain.gain.value=0.14;padGain.connect(g);
  // warm pad: detuned saws through lowpass, slow swell
  const filt=ctx.createBiquadFilter();filt.type='lowpass';filt.frequency.value=900;filt.Q.value=0.6;filt.connect(padGain);
  [0,4,7,12].forEach(st=>{ // Am7 voicing
    [0,3].forEach(det=>{
      const o=ctx.createOscillator();o.type='sawtooth';
      o.frequency.value=A*Math.pow(2,st/12)*Math.pow(2,det/1200);
      const og=ctx.createGain();og.gain.setValueAtTime(0,0);
      og.gain.linearRampToValueAtTime(0.10,2.5);
      og.gain.setValueAtTime(0.10,17);og.gain.linearRampToValueAtTime(0,20);
      o.connect(og);og.connect(filt);o.start(0);o.stop(20);
    });
  });
  // soft pulse kick every beat (120bpm => 0.5s) from 2.5s
  for(let t=2.5;t<17.5;t+=0.5){
    const o=ctx.createOscillator();o.type='sine';
    o.frequency.setValueAtTime(150,t);o.frequency.exponentialRampToValueAtTime(45,t+0.12);
    const og=ctx.createGain();og.gain.setValueAtTime(0.22,t);og.gain.exponentialRampToValueAtTime(0.001,t+0.16);
    o.connect(og);og.connect(g);o.start(t);o.stop(t+0.2);
  }
  // arpeggio for theme snaps 14->17
  const arpN=[0,4,7,12,16];
  for(let i=0;i<20;i++){
    const t=14+i*0.15,st=arpN[i%arpN.length];
    const o=ctx.createOscillator();o.type='triangle';
    o.frequency.value=A*4*Math.pow(2,st/12);
    const og=ctx.createGain();og.gain.setValueAtTime(0.12,t);og.gain.exponentialRampToValueAtTime(0.001,t+0.14);
    o.connect(og);og.connect(g);o.start(t);o.stop(t+0.16);
  }
  // chime on outro logo 17.2
  [[0,0.0],[7,0.0],[12,0.0],[16,0.0],[19,0.0]].forEach(([st])=>{
    const o=ctx.createOscillator();o.type='sine';o.frequency.value=A*8*Math.pow(2,st/12);
    const og=ctx.createGain();og.gain.setValueAtTime(0.18,17.2);og.gain.exponentialRampToValueAtTime(0.001,19.5);
    o.connect(og);og.connect(g);o.start(17.2);o.stop(19.6);
  });
  // scene-accent blips on each LED hit / transition
  [2.5,6,10,14].forEach(t=>{
    const o=ctx.createOscillator();o.type='sine';o.frequency.setValueAtTime(880,t);o.frequency.exponentialRampToValueAtTime(660,t+0.1);
    const og=ctx.createGain();og.gain.setValueAtTime(0.10,t);og.gain.exponentialRampToValueAtTime(0.001,t+0.12);
    o.connect(og);og.connect(g);o.start(t);o.stop(t+0.14);
  });
  const buf=await ctx.startRendering();
  // encode WAV 16-bit stereo
  const ch0=buf.getChannelData(0),ch1=buf.getChannelData(1),n=buf.length;
  const ab=new ArrayBuffer(44+n*4),dv=new DataView(ab);
  const ws=(o,s)=>{for(let i=0;i<s.length;i++)dv.setUint8(o+i,s.charCodeAt(i))};
  ws(0,'RIFF');dv.setUint32(4,36+n*4,true);ws(8,'WAVEfmt ');dv.setUint32(16,16,true);
  dv.setUint16(20,1,true);dv.setUint16(22,2,true);dv.setUint32(24,SR,true);
  dv.setUint32(28,SR*4,true);dv.setUint16(32,4,true);dv.setUint16(34,16,true);
  ws(36,'data');dv.setUint32(40,n*4,true);
  let o=44;
  for(let i=0;i<n;i++){
    let a=Math.max(-1,Math.min(1,ch0[i])),b=Math.max(-1,Math.min(1,ch1[i]));
    dv.setInt16(o,a*32767,true);dv.setInt16(o+2,b*32767,true);o+=4;
  }
  return Array.from(new Uint8Array(ab));
});
fs.writeFileSync(W+'/audio.wav',Buffer.from(wav));
await browser.close();
console.log('audio.wav',wav.length,'bytes');
