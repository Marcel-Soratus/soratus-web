const SVG_NS = 'http://www.w3.org/2000/svg';

// "Lit" nodes — sparse colored light-sources, not planets. Just a soft
// diffuse radial-gradient glow with a tiny bright pinpoint at center,
// matching the way the original reads.
const LITS = [
  { x: 230, y: 305, color: '52,226,122', radius: 70, pinR: 2.6 }, // green, mid-left
  { x: 425, y: 230, color: '92,130,255', radius: 55, pinR: 2.2 }, // blue, upper-right
  { x: 285, y: 165, color: '42,47,204',  radius: 62, pinR: 2.4 }  // navy, upper-left
];

export function initNeuralMesh(svg) {
  if (!svg) return;
  const defs = svg.querySelector('defs');
  const nodesG = svg.querySelector('#meshNodes');
  const edgesG = svg.querySelector('#meshEdges');
  const pulsesG = svg.querySelector('#meshPulses');
  if (!nodesG || !edgesG || !pulsesG || !defs) return;

  // Soft-glow gradients per lit node — fade to transparent, no hard edge.
  LITS.forEach((l, i) => {
    const grad = document.createElementNS(SVG_NS, 'radialGradient');
    grad.setAttribute('id', `lit-${i}`);
    grad.innerHTML = `
      <stop offset="0%"   stop-color="rgba(${l.color},0.85)"/>
      <stop offset="18%"  stop-color="rgba(${l.color},0.55)"/>
      <stop offset="50%"  stop-color="rgba(${l.color},0.12)"/>
      <stop offset="100%" stop-color="rgba(${l.color},0)"/>`;
    defs.appendChild(grad);
  });

  // Sparse perimeter constellation
  const N = 14;
  const cx = 300, cy = 300;
  const small = [];
  for (let i = 0; i < N; i++) {
    const t = (i / N) * Math.PI * 2;
    const angle = t + (Math.random() - 0.5) * 0.5;
    const ringy = Math.random() < 0.78
      ? 235 + Math.random() * 40
      : 150 + Math.random() * 60;
    small.push({
      x: cx + Math.cos(angle) * ringy,
      y: cy + Math.sin(angle) * ringy,
      r: 1.6 + Math.random() * 1.6
    });
  }

  // Treat lit-pinpoints as edge endpoints too so the graph weaves through them
  const all = [
    ...LITS.map(l => ({ x: l.x, y: l.y, lit: true })),
    ...small
  ];

  const edges = [];
  function addEdge(a, b) {
    if (a === b) return;
    if (edges.some(e => (e.a === a && e.b === b) || (e.a === b && e.b === a))) return;
    edges.push({ a, b });
  }
  for (let i = 0; i < all.length; i++) {
    const k = all[i].lit ? 3 : 2;
    const dists = [];
    for (let j = 0; j < all.length; j++) {
      if (i === j) continue;
      const dx = all[i].x - all[j].x, dy = all[i].y - all[j].y;
      dists.push({ j, d: dx * dx + dy * dy });
    }
    dists.sort((a, b) => a.d - b.d);
    for (let n = 0; n < k; n++) addEdge(i, dists[n].j);
  }

  for (const e of edges) {
    const line = document.createElementNS(SVG_NS, 'line');
    line.setAttribute('x1', all[e.a].x);
    line.setAttribute('y1', all[e.a].y);
    line.setAttribute('x2', all[e.b].x);
    line.setAttribute('y2', all[e.b].y);
    line.setAttribute('class', 'edge');
    edgesG.appendChild(line);
  }

  // Lit nodes: a soft diffuse halo + a tiny bright pinpoint (no hard disk)
  LITS.forEach((l, i) => {
    const glow = document.createElementNS(SVG_NS, 'circle');
    glow.setAttribute('cx', l.x);
    glow.setAttribute('cy', l.y);
    glow.setAttribute('r', l.radius);
    glow.setAttribute('fill', `url(#lit-${i})`);
    glow.setAttribute('class', 'lit-glow');
    nodesG.appendChild(glow);

    const pin = document.createElementNS(SVG_NS, 'circle');
    pin.setAttribute('cx', l.x);
    pin.setAttribute('cy', l.y);
    pin.setAttribute('r', l.pinR);
    pin.setAttribute('fill', '#fff');
    pin.setAttribute('class', 'node-core');
    nodesG.appendChild(pin);
  });

  // Plain white pinpoints
  for (const n of small) {
    const c = document.createElementNS(SVG_NS, 'circle');
    c.setAttribute('cx', n.x);
    c.setAttribute('cy', n.y);
    c.setAttribute('r', n.r);
    c.setAttribute('fill', '#fff');
    c.setAttribute('class', 'node-core');
    nodesG.appendChild(c);
  }

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reducedMotion) return;

  function pulse() {
    const e = edges[Math.floor(Math.random() * edges.length)];
    const a = all[e.a], b = all[e.b];
    const dot = document.createElementNS(SVG_NS, 'circle');
    dot.setAttribute('r', 2.4);
    dot.setAttribute('class', 'pulse');
    dot.setAttribute('cx', a.x);
    dot.setAttribute('cy', a.y);
    pulsesG.appendChild(dot);

    const start = performance.now();
    const dur = 800 + Math.random() * 700;
    function step(now) {
      const t = Math.min(1, (now - start) / dur);
      dot.setAttribute('cx', a.x + (b.x - a.x) * t);
      dot.setAttribute('cy', a.y + (b.y - a.y) * t);
      if (t < 1) requestAnimationFrame(step);
      else dot.remove();
    }
    requestAnimationFrame(step);
  }
  setInterval(pulse, 440);
}
