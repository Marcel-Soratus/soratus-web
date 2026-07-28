/**
 * Controleert of tabwisselen geen lege pagina's oplevert.
 *
 * Loopt het klikpad Hoe -> Cases -> Hoe -> Cases af in een echte (headless)
 * Chrome en meet na elke stap of er content in beeld staat die onzichtbaar is.
 * In een niet-renderende browser vuurt IntersectionObserver niet en smooth
 * scroll loopt niet, dus meten in zo'n tab geeft valse alarmen.
 *
 *   node handoff/cases/controleer-navigatie.mjs
 */

import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { setTimeout as wait } from 'node:timers/promises';
import { tmpdir } from 'node:os';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9335;
const BASIS = 'http://localhost:5199';

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
    setTimeout(() => w.has(id) && (w.delete(id), reject(new Error('timeout ' + method))), 60000);
  });
}
const evalueer = (cdp, expr) =>
  cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }).then(r => r.result?.value);

const main = async () => {
  const chrome = spawn(CHROME, [
    '--headless=new', `--remote-debugging-port=${PORT}`, '--hide-scrollbars',
    '--window-size=1500,1000', '--force-device-scale-factor=1',
    '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${tmpdir()}/soratus-nav`, 'about:blank',
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
    await cdp('Page.enable'); await cdp('Runtime.enable');

    await cdp('Page.navigate', { url: BASIS + '/' });
    await wait(4000);

    const meet = () => evalueer(cdp, `(() => {
      const rev = Array.from(document.querySelectorAll('.reveal'));
      const leegInBeeld = rev.filter(e => {
        const r = e.getBoundingClientRect();
        return r.top < innerHeight * 0.85 && r.bottom > 0 && getComputedStyle(e).opacity === '0'
               && (e.textContent||'').trim().length > 0;
      });
      // hoeveel van het beeld is daadwerkelijk beschreven?
      const zichtbaar = rev.filter(e => {
        const r = e.getBoundingClientRect();
        return r.top < innerHeight && r.bottom > 0 && parseFloat(getComputedStyle(e).opacity) > 0.5;
      }).length;
      return { url: location.pathname + location.hash, scrollY: Math.round(scrollY),
               onthuld: rev.filter(e=>e.classList.contains('in')).length + '/' + rev.length,
               zichtbaarInBeeld: zichtbaar, LEEG_IN_BEELD: leegInBeeld.length };
    })()`);

    const klik = async (href) => {
      await evalueer(cdp, `document.querySelector('.nav-links a[href="${href}"]').click()`);
      await wait(3000);
    };

    const rapport = [{ stap: 'begin op /', ...(await meet()) }];
    for (const [naam, href] of [['Hoe', '/#hoe'], ['Cases', '/cases'], ['Hoe', '/#hoe'], ['Cases', '/cases'], ['Wat we doen', '/#wat']]) {
      await klik(href);
      rapport.push({ stap: 'klik ' + naam, ...(await meet()) });
      const { data } = await cdp('Page.captureScreenshot', { format: 'png' });
      writeFileSync(`${tmpdir()}/nav-${rapport.length}-${naam.replace(/ /g,'')}.png`, Buffer.from(data, 'base64'));
    }

    console.table(rapport);
    const stuk = rapport.filter(r => r.LEEG_IN_BEELD > 0 || r.zichtbaarInBeeld === 0);
    console.log(stuk.length ? `\nPROBLEEM bij ${stuk.length} stap(pen)` : '\nGeen lege pagina bij tabwisselen.');
    console.log(`Opnames: ${tmpdir()}/nav-*.png`);
    ws.close();
  } finally {
    chrome.kill();
  }
};

main().catch(e => { console.error('Mislukt:', e.message); process.exit(1); });
