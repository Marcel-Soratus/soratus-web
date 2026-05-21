const LINES = [
  'init.system();',
  'load.brain();',
  'agent.boot()',
  'tools.ready ✓',
  'context.warm()',
  'soratus.online',
  'thinking…',
  'orchestrate()',
  'plan → act → done',
  'observe.stream()',
  'memory.persist',
  'route.intent()'
];

export function initCodestrip(el) {
  if (!el) return;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reduced) { el.textContent = LINES[0]; return; }
  let i = 0;
  el.textContent = LINES[0];
  setInterval(() => {
    i = (i + 1) % LINES.length;
    el.style.opacity = '0';
    setTimeout(() => {
      el.textContent = LINES[i];
      el.style.opacity = '1';
    }, 250);
  }, 2200);
  el.style.transition = 'opacity .25s ease';
}
