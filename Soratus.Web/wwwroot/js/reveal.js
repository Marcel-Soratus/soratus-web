export function initReveal() {
  const els = document.querySelectorAll('.reveal');
  if (!('IntersectionObserver' in window)) {
    els.forEach(el => el.classList.add('in'));
    return;
  }
  const io = new IntersectionObserver((entries, obs) => {
    for (const e of entries) {
      if (e.isIntersecting) {
        e.target.classList.add('in');
        obs.unobserve(e.target);
      }
    }
    // Start de fade vóórdat een element in beeld komt, niet erna. Met de oude
    // waarden (12% zichtbaar én 8% boven de onderrand) begon de animatie van
    // .9s pas als je het element al zag, waardoor je op langere pagina's de
    // fade voorbijscrolde en tegen halfzichtbare tekst aankeek.
  }, { threshold: 0, rootMargin: '0px 0px 15% 0px' });
  els.forEach(el => io.observe(el));
}
