// Meldt aan LogsPanel.razor of dit tabblad op de voorgrond staat.
//
// Waarom dit bestaat: live tail haalt elke vijf seconden logregels op, en dat is een
// Cosmos-query per keer. Een tabblad dat op de achtergrond staat kijkt niemand naar, dus
// die query's zijn zuiver kosten. De Page Visibility API is de enige bron voor die vraag;
// vanuit .NET is hij niet te zien.
//
// Eén handler per componentinstantie, opgeslagen op een id. Meerdere agentdetails in
// verschillende tabbladen delen deze module niet, maar een gebruiker die binnen één circuit
// heen en weer navigeert maakt wél meerdere instanties, en die moeten elkaars listener niet
// afmelden.

const handlers = new Map();

export function watch(id, ref) {
  const handler = () => {
    // invokeMethodAsync kan afketsen als het circuit net weg is; dat is geen fout die de
    // pagina moet zien.
    ref.invokeMethodAsync('SetVisible', document.visibilityState === 'visible').catch(() => {});
  };

  handlers.set(id, handler);
  document.addEventListener('visibilitychange', handler);

  // Meteen één keer melden: het tabblad kan al op de achtergrond staan op het moment dat
  // dit component voor het eerst rendert.
  handler();
}

export function unwatch(id) {
  const handler = handlers.get(id);

  if (handler) {
    document.removeEventListener('visibilitychange', handler);
    handlers.delete(id);
  }
}
