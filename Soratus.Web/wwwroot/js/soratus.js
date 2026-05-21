import { initReveal } from './reveal.js';
import { initNeuralMesh } from './neural-mesh.js';
import { initCodestrip } from './codestrip.js';
import { initTestimonials } from './testimonials.js';
import { initTerminal } from './terminal.js';

let chatRef = null;
const pending = [];

function openOnRef(prefill) {
  if (chatRef) chatRef.invokeMethodAsync('JsOpenWith', prefill ?? null);
  else pending.push(prefill ?? null);
}

window.soratus = {
  init() {
    initReveal();
    initNeuralMesh(document.querySelector('#orbStage svg.mesh'));
    initCodestrip(document.getElementById('codestrip'));
    initTestimonials();
    initTerminal(document.getElementById('terminal'));
  },
  registerChat(ref) {
    chatRef = ref;
    while (pending.length) {
      const p = pending.shift();
      ref.invokeMethodAsync('JsOpenWith', p);
    }
  },
  openChat() { openOnRef(null); },
  openChatWith(prefill) { openOnRef(prefill || null); },
  toggleNavSheet() {
    const sheet = document.getElementById('navSheet');
    const burger = document.getElementById('navBurger');
    if (!sheet) return;
    const isOpen = sheet.classList.toggle('open');
    burger?.classList.toggle('open', isOpen);
    sheet.setAttribute('aria-hidden', String(!isOpen));
    burger?.setAttribute('aria-expanded', String(isOpen));
    document.body.style.overflow = isOpen ? 'hidden' : '';
  },
  closeNavSheet() {
    const sheet = document.getElementById('navSheet');
    const burger = document.getElementById('navBurger');
    if (!sheet) return;
    sheet.classList.remove('open');
    burger?.classList.remove('open');
    sheet.setAttribute('aria-hidden', 'true');
    burger?.setAttribute('aria-expanded', 'false');
    document.body.style.overflow = '';
  }
};

// Close sheet on Escape + on outside-click (overlay)
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') window.soratus?.closeNavSheet();
});
document.addEventListener('click', (e) => {
  const sheet = document.getElementById('navSheet');
  if (!sheet || !sheet.classList.contains('open')) return;
  // Click on overlay (not inside panel, not on burger) closes it
  if (e.target === sheet) window.soratus.closeNavSheet();
});

function boot() { window.soratus.init(); }

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot);
} else {
  boot();
}
