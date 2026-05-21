// Live-tail terminal for the "AI Agents" card. Streams a long pool of
// commands that read like a multi-tenant agent in production — return
// requests, invoice ingestion, lead scoring, ERP syncs, etc. Loops.
//
// Coloring uses the existing app.css classes:
//   .cm  italic gray   (comments, // timestamps)
//   .ks  blue          (keywords / function names / vars)
//   .vs  green         (string values, OK results)

const LINE_INTERVAL = 900;   // ms between lines on average
const LINE_JITTER   = 350;   // ±ms jitter for human feel
const MAX_VISIBLE   = 12;    // DOM cap — oldest line drops off the top
const START_DELAY   = 500;   // wait after section in-view before first line

// Pool of ~70 lines split across realistic agent scenarios.
// Pre-rendered as HTML so coloring/spacing comes through as-is.
const LINES = [
  // ── 09:42 · return-request flow
  '<span class="cm">// 09:42 · new ticket · customer #C-1294</span>',
  '<span class="ks">intent</span> = <span class="vs">"return_request"</span>',
  '<span class="ks">order.fetch</span>(#SR-8420) → <span class="vs">ok</span>',
  '<span class="ks">policy.check</span>() → <span class="vs">eligible</span>',
  '<span class="ks">label.generate</span>() → <span class="vs">PDF + email sent</span>',
  '<span class="cm">// resolved · 09:42:08 · 8s</span>',

  // ── 09:42 · lead scoring
  '<span class="cm">// 09:42 · inbound · webform</span>',
  '<span class="ks">contact</span> = <span class="vs">"info@acme.nl"</span>',
  '<span class="ks">crm.lookup</span>() → <span class="vs">new contact</span>',
  '<span class="ks">enrich.linkedin</span>() → <span class="vs">VP Operations</span>',
  '<span class="ks">score</span> = <span class="vs">84 · sales-qualified</span>',
  '<span class="ks">slack.notify</span>(#sales) ✓',
  '<span class="cm">// queued · follow-up @ 11:00</span>',

  // ── 09:43 · invoice processing
  '<span class="cm">// 09:43 · invoice.ingest()</span>',
  '<span class="ks">file</span> = <span class="vs">"FA-2024-3318.pdf"</span>',
  '<span class="ks">ocr.extract</span>() → <span class="vs">14 fields</span>',
  '<span class="ks">match.po</span>() → <span class="vs">PO-2031 ✓</span>',
  '<span class="ks">exact.post</span>() → <span class="vs">€4.218,50 booked</span>',
  '<span class="cm">// posted · 09:43:11 · 6s</span>',

  // ── 09:44 · chat assist
  '<span class="cm">// 09:44 · chat · #C-1297</span>',
  '<span class="ks">intent</span> = <span class="vs">"track_order"</span>',
  '<span class="ks">order</span> = #SR-8451',
  '<span class="ks">carrier.fetch</span>() → <span class="vs">DHL · in transit</span>',
  '<span class="ks">eta</span> = <span class="vs">"morgen 10:00–12:00"</span>',
  '<span class="cm">// resolved · 4s</span>',

  // ── 09:45 · ERP sync
  '<span class="cm">// 09:45 · sync.afas → warehouse</span>',
  '<span class="ks">delta</span> = <span class="vs">142 SKUs</span>',
  '<span class="ks">push.batch</span>() → <span class="vs">142/142 ok</span>',
  '<span class="cm">// next run · 10:00</span>',

  // ── 09:46 · fraud check
  '<span class="cm">// 09:46 · payment.review</span>',
  '<span class="ks">amount</span> = <span class="vs">€18.940,00</span>',
  '<span class="ks">risk.score</span>() → <span class="vs">72 · elevated</span>',
  '<span class="ks">hold</span> + <span class="ks">notify</span>(finance) ✓',
  '<span class="cm">// awaiting review</span>',

  // ── 09:47 · planning agent
  '<span class="cm">// 09:47 · planning.optimize</span>',
  '<span class="ks">routes</span> = <span class="vs">18 stops</span>',
  '<span class="ks">solver.run</span>() → <span class="vs">3.4h saved</span>',
  '<span class="ks">drivers.dispatch</span>() ✓',
  '<span class="cm">// committed · 09:47:09</span>',

  // ── 09:48 · document Q&A
  '<span class="cm">// 09:48 · docs.ask</span>',
  '<span class="ks">user</span> = <span class="vs">"juridisch@klant.nl"</span>',
  '<span class="ks">q</span> = <span class="vs">"opzegtermijn raamcontract?"</span>',
  '<span class="ks">retrieve</span>() → 3 clauses',
  '<span class="ks">answer</span> → <span class="vs">"3 maanden · art. 12.4"</span>',
  '<span class="cm">// resolved · 2.1s</span>',

  // ── 09:49 · klant nps
  '<span class="cm">// 09:49 · nps.cron</span>',
  '<span class="ks">batch</span> = 240 klanten',
  '<span class="ks">send.whatsapp</span>() → 238 ok · 2 fail',
  '<span class="ks">retry.queue</span>(2) ✓',

  // ── 09:50 · onboarding
  '<span class="cm">// 09:50 · onboarding.new</span>',
  '<span class="ks">company</span> = <span class="vs">"Brunel"</span>',
  '<span class="ks">accounts.provision</span>() → <span class="vs">12 seats</span>',
  '<span class="ks">welcome.send</span>() ✓',
  '<span class="cm">// completed · 09:50:14</span>',

  // ── 09:51 · monitoring
  '<span class="cm">// 09:51 · health</span>',
  '<span class="ks">uptime</span> = <span class="vs">99.98% · 30d</span>',
  '<span class="ks">latency.p99</span> = <span class="vs">84ms</span>',
  '<span class="ks">queue.depth</span> = <span class="vs">3</span>',
  '<span class="cm">// all green</span>',

  // ── 09:52 · whatsapp inbound
  '<span class="cm">// 09:52 · whatsapp · +316…</span>',
  '<span class="ks">intent</span> = <span class="vs">"reschedule_visit"</span>',
  '<span class="ks">calendar.find</span>() → <span class="vs">do 14:30</span>',
  '<span class="ks">confirm.send</span>() ✓',
  '<span class="cm">// resolved · 3s</span>',

  // ── 09:53 · summary
  '<span class="cm">// 09:53 · daily.recap</span>',
  '<span class="ks">handled</span> = <span class="vs">214 tickets · 73% auto</span>',
  '<span class="ks">escalated</span> = <span class="vs">8 · human</span>',
  '<span class="ks">avg_resolve</span> = <span class="vs">11s</span>',
  '<span class="cm">// agent.online</span>'
];

export function initTerminal(root) {
  if (!root) return;

  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Clear the static placeholder lines that ship in the Razor markup —
  // they're only there for SSR / no-JS scenarios. The live feed takes over.
  root.innerHTML = '';

  // Re-create the blinking caret as a child that we'll keep parented to the
  // most recent line.
  const caret = document.createElement('span');
  caret.className = 'caret';

  if (reduced) {
    // Show the first scenario worth of lines, no animation, no looping.
    for (let i = 0; i < 6; i++) {
      const ln = document.createElement('div');
      ln.className = 'ln';
      ln.innerHTML = LINES[i];
      root.appendChild(ln);
    }
    root.lastElementChild?.appendChild(caret);
    return;
  }

  let cursor = 0;
  let timer = null;
  let running = false;

  function appendLine() {
    const ln = document.createElement('div');
    ln.className = 'ln';
    ln.innerHTML = LINES[cursor];
    ln.style.opacity = '0';
    ln.style.transition = 'opacity .25s ease';
    root.appendChild(ln);

    // Re-parent caret to the new tail
    ln.appendChild(caret);

    // Trigger the fade-in on next frame so the transition actually plays
    requestAnimationFrame(() => { ln.style.opacity = '1'; });

    // Cap the DOM — drop the oldest line off the top
    while (root.children.length > MAX_VISIBLE) {
      root.firstElementChild?.remove();
    }

    cursor = (cursor + 1) % LINES.length;

    const next = LINE_INTERVAL + (Math.random() - 0.5) * LINE_JITTER;
    timer = setTimeout(appendLine, Math.max(350, next));
  }

  function start() {
    if (running) return;
    running = true;
    timer = setTimeout(appendLine, START_DELAY);
  }

  function stop() {
    running = false;
    if (timer) { clearTimeout(timer); timer = null; }
  }

  // Only run while the card is on screen — kinder to CPU and battery on
  // long pages. Resumes when scrolled back into view.
  if ('IntersectionObserver' in window) {
    const io = new IntersectionObserver((entries) => {
      for (const e of entries) {
        if (e.isIntersecting) start();
        else stop();
      }
    }, { threshold: 0.15 });
    io.observe(root);
  } else {
    start();
  }
}
