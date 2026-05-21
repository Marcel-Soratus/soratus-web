export function initTestimonials() {
  const stage = document.getElementById('quoteStage');
  if (!stage) return;
  const cards = stage.querySelectorAll('.qcard');
  const dotsEl = document.getElementById('quoteDots');
  const dots = dotsEl ? dotsEl.querySelectorAll('.qdot') : [];
  const count = document.getElementById('qcount');
  const prev = document.getElementById('qPrev');
  const next = document.getElementById('qNext');
  if (cards.length === 0) return;

  let i = 0;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  let timer = null;

  function show(n) {
    i = (n + cards.length) % cards.length;
    cards.forEach((c, idx) => c.classList.toggle('active', idx === i));
    dots.forEach((d, idx) => d.classList.toggle('active', idx === i));
    if (count) count.textContent = String(i + 1).padStart(2, '0') + ' / ' + String(cards.length).padStart(2, '0');
  }

  function start() {
    if (reduced) return;
    stop();
    timer = setInterval(() => show(i + 1), 6400);
  }
  function stop() { if (timer) { clearInterval(timer); timer = null; } }

  prev?.addEventListener('click', () => { show(i - 1); start(); });
  next?.addEventListener('click', () => { show(i + 1); start(); });
  dots.forEach((d, idx) => d.addEventListener('click', () => { show(idx); start(); }));

  show(0);
  start();
}
