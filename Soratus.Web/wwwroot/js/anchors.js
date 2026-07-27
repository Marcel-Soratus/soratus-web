/**
 * In-page anchor navigatie.
 *
 * Nodig sinds de site meerdere pagina's heeft: nav-links zijn "/#hoe" in plaats
 * van "#hoe", zodat ze ook vanaf een case-pagina werken. Blazor's enhanced
 * navigation kan zo'n link als paginanavigatie behandelen, waarna er niet naar
 * het fragment gescrold wordt.
 *
 * Deze module pakt dat expliciet op en compenseert de sticky nav, zodat een
 * sectiekop niet achter de navbalk verdwijnt. Werkt op window en, mocht de
 * content ooit in een eigen scroll-container komen, ook daarop.
 */

/** Dichtstbijzijnde daadwerkelijk scrollende voorouder, anders window. */
function scrollerFor(el) {
  for (let node = el.parentElement; node; node = node.parentElement) {
    const overflowY = getComputedStyle(node).overflowY;
    const scrollable = overflowY === 'auto' || overflowY === 'scroll';
    if (scrollable && node.scrollHeight > node.clientHeight + 1) return node;
  }
  return window;
}

/** Hoogte van de sticky nav, zodat we daar niet onder scrollen. */
function navOffset() {
  const nav = document.querySelector('nav.top');
  if (!nav) return 0;
  return nav.getBoundingClientRect().height || 0;
}

function scrollToTarget(target, behavior = 'smooth') {
  const scroller = scrollerFor(target);
  const offset = navOffset() + 8;

  if (scroller === window) {
    const top = target.getBoundingClientRect().top + window.scrollY - offset;
    window.scrollTo({ top: Math.max(0, top), behavior });
    return;
  }

  // Positie van het doel binnen de scroll-container.
  const targetTop = target.getBoundingClientRect().top;
  const scrollerTop = scroller.getBoundingClientRect().top;
  const top = scroller.scrollTop + (targetTop - scrollerTop) - offset;
  scroller.scrollTo({ top: Math.max(0, top), behavior });
}

function targetFromHash(hash) {
  if (!hash || hash === '#') return null;
  let id = hash.slice(1);
  try { id = decodeURIComponent(id); } catch { /* laat id staan zoals het is */ }
  if (!id) return null;
  return document.getElementById(id);
}

export function initAnchors() {
  // Klik op een link die naar een fragment op DEZE pagina wijst.
  document.addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

    const link = e.target.closest?.('a[href]');
    if (!link || link.target === '_blank' || link.hasAttribute('download')) return;

    const raw = link.getAttribute('href');
    if (!raw || !raw.includes('#')) return;

    let url;
    try { url = new URL(link.href, location.href); } catch { return; }
    if (url.origin !== location.origin) return;

    // Alleen als het fragment op de huidige pagina zit.
    const samePath = url.pathname === location.pathname;
    if (!samePath) return;

    const target = targetFromHash(url.hash);
    if (!target) return;

    e.preventDefault();
    scrollToTarget(target);
    if (url.hash !== location.hash) history.pushState(null, '', url.hash);
  });

  // Binnenkomen met een fragment in de URL (bijv. /#hoe vanaf een case-pagina).
  // Native scroll faalt hier doordat het doel in een scroll-container zit.
  const initial = targetFromHash(location.hash);
  if (initial) {
    // Twee frames wachten zodat fonts en layout gezet zijn voor we meten.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      scrollToTarget(initial, 'auto');
    }));
  }

  // Terug/vooruit-knop met alleen een fragmentwissel.
  window.addEventListener('hashchange', () => {
    const target = targetFromHash(location.hash);
    if (target) scrollToTarget(target);
  });
}
