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
  scrollChatToBottom() {
    const el = document.getElementById('chatBody');
    if (!el) return;
    // Only auto-scroll if the user is already near the bottom — leave them
    // alone if they scrolled up to read the history.
    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (distance < 160) {
      el.scrollTop = el.scrollHeight;
    }
  },
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

// Delegated handler: any element (or ancestor) with data-chat-prompt opens
// the chat preloaded with that question. Lets us sprinkle ask-Tempo affordances
// across the page without per-component JS.
function fireChatPromptFrom(target) {
  const trigger = target?.closest?.('[data-chat-prompt]');
  if (!trigger) return false;
  const prompt = trigger.getAttribute('data-chat-prompt');
  if (!prompt) return false;
  window.soratus?.openChatWith(prompt);
  return true;
}

document.addEventListener('click', (e) => {
  // Don't hijack clicks on real links/buttons nested inside the trigger
  const nested = e.target.closest('a[href], button:not([data-chat-prompt])');
  if (nested && nested.closest('[data-chat-prompt]') !== nested) {
    // The nested link/button is INSIDE a data-chat-prompt element — let it win
    return;
  }
  if (fireChatPromptFrom(e.target)) e.preventDefault();
});

// Keyboard activation for tabindex=0 askable elements
document.addEventListener('keydown', (e) => {
  if (e.key !== 'Enter' && e.key !== ' ') return;
  const trigger = e.target?.closest?.('[data-chat-prompt]');
  if (!trigger) return;
  e.preventDefault();
  fireChatPromptFrom(e.target);
});

function boot() { window.soratus.init(); }

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot);
} else {
  boot();
}
