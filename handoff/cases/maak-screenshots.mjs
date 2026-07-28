/**
 * Maakt de case-screenshots door de MBV-demo zelf te doorlopen.
 *
 * Stuurt een eigen headless Chrome aan via het DevTools-protocol. Node 24 heeft
 * WebSocket ingebouwd, dus er is niets te installeren.
 *
 * Vooraf moeten MBV.Web (7148) en MBV.Api (7021) draaien.
 *
 *   node handoff/cases/maak-screenshots.mjs
 *
 * De opnames worden bijgesneden tot de app-container, zodat er geen loze marge
 * omheen staat en alle beelden dezelfde opzet hebben.
 */

import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { setTimeout as wait } from 'node:timers/promises';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9333;
const UIT = 'Soratus.Web/wwwroot/img/cases';
const BASIS = 'https://localhost:7148';

let volgendId = 1;

function verbind(ws) {
  const wachtenden = new Map();
  ws.addEventListener('message', (e) => {
    const m = JSON.parse(e.data);
    if (m.id && wachtenden.has(m.id)) {
      const { resolve, reject } = wachtenden.get(m.id);
      wachtenden.delete(m.id);
      m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result);
    }
  });
  return (method, params = {}) => new Promise((resolve, reject) => {
    const id = volgendId++;
    wachtenden.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
    setTimeout(() => wachtenden.has(id) && (wachtenden.delete(id), reject(new Error('timeout ' + method))), 120000);
  });
}

const evalueer = (cdp, expr) =>
  cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true })
    .then((r) => r.result?.value);

async function knip(cdp, naam) {
  // container van de app opzoeken, zodat we strak bijsnijden
  const box = await evalueer(cdp, `(() => {
    const kaarten = Array.from(document.querySelectorAll('div,main,section'))
      .map(e => e.getBoundingClientRect())
      .filter(r => r.width > 400 && r.height > 200);
    if (!kaarten.length) return null;
    const l = Math.min(...kaarten.map(r => r.left));
    const t = Math.min(...kaarten.map(r => r.top));
    const r2 = Math.max(...kaarten.map(r => r.right));
    const b = Math.max(...kaarten.map(r => r.bottom));
    const m = 14;
    return { x: Math.max(0, l - m), y: Math.max(0, t - m),
             width: Math.min(innerWidth, r2 - l + m*2), height: Math.min(innerHeight, b - t + m*2) };
  })()`);

  const opts = { format: 'png', captureBeyondViewport: false };
  if (box && box.width > 100 && box.height > 100) {
    opts.clip = { ...box, scale: 2 };   // 2x voor scherpte
  }
  const { data } = await cdp('Page.captureScreenshot', opts);
  mkdirSync(UIT, { recursive: true });
  writeFileSync(`${UIT}/${naam}`, Buffer.from(data, 'base64'));
  console.log(`   ${naam}  (${box ? Math.round(box.width) + 'x' + Math.round(box.height) + ' @2x' : 'volledig venster'})`);
}

async function stuurVraag(cdp, tekst) {
  await evalueer(cdp, `(() => {
    const i = document.querySelector('input[placeholder*="vraag" i], textarea');
    const zet = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    zet.call(i, ${JSON.stringify(tekst)});
    i.dispatchEvent(new Event('input', { bubbles: true }));
    i.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', bubbles: true }));
    const knop = i.parentElement.querySelector('button');
    if (knop) knop.click();
    return true;
  })()`);
}

const main = async () => {
  const chrome = spawn(CHROME, [
    '--headless=new', `--remote-debugging-port=${PORT}`,
    '--ignore-certificate-errors', '--hide-scrollbars',
    '--window-size=1500,1080', '--force-device-scale-factor=1',
    '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${process.env.TEMP || '/tmp'}/mbv-shots`,
    'about:blank',
  ], { stdio: 'ignore' });

  try {
    let doel;
    for (let i = 0; i < 40; i++) {
      try {
        const lijst = await fetch(`http://localhost:${PORT}/json`).then((r) => r.json());
        doel = lijst.find((t) => t.type === 'page');
        if (doel) break;
      } catch { /* nog niet klaar */ }
      await wait(500);
    }
    if (!doel) throw new Error('Chrome kwam niet op');

    const ws = new WebSocket(doel.webSocketDebuggerUrl);
    await new Promise((r) => ws.addEventListener('open', r, { once: true }));
    const cdp = verbind(ws);
    await cdp('Page.enable');
    await cdp('Runtime.enable');

    const ga = async (pad) => {
      await cdp('Page.navigate', { url: BASIS + pad });
      await wait(4500);
    };

    console.log('Jaarverslag:');
    await ga('/jaarverslag');
    await knip(cdp, 'jaarverslag-start.png');

    await evalueer(cdp, `Array.from(document.querySelectorAll('button,a'))
      .find(b => /Stel het jaarverslag 2025 op/i.test(b.textContent||'')).click()`);
    await wait(30000);
    await knip(cdp, 'jaarverslag-rapport.png');

    await stuurVraag(cdp, 'Hoe staan liquiditeit en solvabiliteit ervoor? Zet de kengetallen in een tabel met per kengetal de formule erbij.');
    await wait(35000);
    // rapport-paneel naar de kengetallen scrollen
    await evalueer(cdp, `(() => {
      const t = Array.from(document.querySelectorAll('table')).pop();
      if (t) t.scrollIntoView({ block: 'center' });
      return !!t;
    })()`);
    await wait(1200);
    await knip(cdp, 'jaarverslag-kengetallen.png');

    console.log('Declaraties:');
    await ga('/declaraties');
    await knip(cdp, 'declaraties-betalingen.png');

    ws.close();
    console.log('\nKlaar. Declaraties-matching en -agent vragen een bestandsupload; die doen we apart.');
  } finally {
    chrome.kill();
  }
};

main().catch((e) => { console.error('Mislukt:', e.message); process.exit(1); });
