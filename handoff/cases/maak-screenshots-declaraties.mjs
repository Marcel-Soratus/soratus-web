/**
 * Maakt de twee declaratie-screenshots die een bestandsupload vereisen.
 *
 * Vooraf: MBV.Web (7148) en MBV.Api (7021) draaien, en het voorbeeldbestand
 * staat lokaal (wordt hieronder opgehaald als het ontbreekt).
 *
 *   node handoff/cases/maak-screenshots-declaraties.mjs
 */

import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { setTimeout as wait } from 'node:timers/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9334;
const UIT = 'Soratus.Web/wwwroot/img/cases';
const XLSX = join(tmpdir(), 'declaraties-voorbeeld.xlsx');

let volgendId = 1;
function verbind(ws) {
  const w = new Map();
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && w.has(m.id)) {
      const { resolve, reject } = w.get(m.id); w.delete(m.id);
      m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result);
    }
  });
  return (method, params = {}) => new Promise((resolve, reject) => {
    const id = volgendId++; w.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
    setTimeout(() => w.has(id) && (w.delete(id), reject(new Error('timeout ' + method))), 180000);
  });
}
const evalueer = (cdp, expr) =>
  cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }).then(r => r.result?.value);

async function knip(cdp, naam) {
  const box = await evalueer(cdp, `(() => {
    const r = Array.from(document.querySelectorAll('div,main,section'))
      .map(e => e.getBoundingClientRect()).filter(r => r.width > 400 && r.height > 200);
    if (!r.length) return null;
    const l = Math.min(...r.map(x=>x.left)), t = Math.min(...r.map(x=>x.top));
    const rr = Math.max(...r.map(x=>x.right)), b = Math.max(...r.map(x=>x.bottom));
    const m = 14;
    return { x: Math.max(0,l-m), y: Math.max(0,t-m),
             width: Math.min(innerWidth, rr-l+m*2), height: Math.min(innerHeight, b-t+m*2) };
  })()`);
  const opts = { format: 'png', captureBeyondViewport: false };
  if (box && box.width > 100 && box.height > 100) opts.clip = { ...box, scale: 2 };
  const { data } = await cdp('Page.captureScreenshot', opts);
  mkdirSync(UIT, { recursive: true });
  writeFileSync(`${UIT}/${naam}`, Buffer.from(data, 'base64'));
  console.log(`   ${naam}  (${box ? Math.round(box.width)+'x'+Math.round(box.height)+' @2x' : 'venster'})`);
}

const main = async () => {
  if (!existsSync(XLSX)) {
    const r = await fetch('https://localhost:7021/api/declaraties/voorbeeldbestand');
    writeFileSync(XLSX, Buffer.from(await r.arrayBuffer()));
  }

  const chrome = spawn(CHROME, [
    '--headless=new', `--remote-debugging-port=${PORT}`,
    '--ignore-certificate-errors', '--hide-scrollbars',
    '--window-size=1500,1200', '--force-device-scale-factor=1',
    '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${tmpdir()}/mbv-shots-decl`, 'about:blank',
  ], { stdio: 'ignore' });

  try {
    let doel;
    for (let i = 0; i < 40; i++) {
      try {
        doel = (await fetch(`http://localhost:${PORT}/json`).then(r => r.json())).find(t => t.type === 'page');
        if (doel) break;
      } catch {}
      await wait(500);
    }
    const ws = new WebSocket(doel.webSocketDebuggerUrl);
    await new Promise(r => ws.addEventListener('open', r, { once: true }));
    const cdp = verbind(ws);
    await cdp('Page.enable'); await cdp('Runtime.enable'); await cdp('DOM.enable');

    await cdp('Page.navigate', { url: 'https://localhost:7148/declaraties' });
    await wait(5000);

    // bestand in de verborgen file-input zetten
    const doc = await cdp('DOM.getDocument');
    const node = await cdp('DOM.querySelector', { nodeId: doc.root.nodeId, selector: 'input[type=file]' });
    await cdp('DOM.setFileInputFiles', { nodeId: node.nodeId, files: [XLSX] });
    console.log('   bestand aangeboden, wachten op matching...');
    await wait(15000);
    await knip(cdp, 'declaraties-matching.png');

    // de agent daadwerkelijk laten afhandelen
    const gestart = await evalueer(cdp, `(() => {
      const b = Array.from(document.querySelectorAll('button'))
        .find(b => /Handel \\d+ bevindingen af/i.test(b.textContent||''));
      if (b) { b.click(); return b.textContent.trim(); }
      return null;
    })()`);
    console.log('   agent gestart: ' + (gestart ?? 'knop niet gevonden'));
    if (gestart) await wait(75000);   // per bevinding een LLM-ronde

    // naar de voorstellen scrollen
    const gevonden = await evalueer(cdp, `(() => {
      const k = Array.from(document.querySelectorAll('h1,h2,h3,h4,div,section'))
        .find(e => /Afhandeling door AI-agent/i.test(e.textContent||'') && e.getBoundingClientRect().height < 900);
      if (k) { k.scrollIntoView({ block: 'start' }); return true; }
      return false;
    })()`);
    console.log('   agent-sectie ' + (gevonden ? 'gevonden' : 'NIET gevonden'));
    await wait(2000);
    await knip(cdp, 'declaraties-agent.png');

    ws.close();
  } finally {
    chrome.kill();
  }
};

main().catch(e => { console.error('Mislukt:', e.message); process.exit(1); });
