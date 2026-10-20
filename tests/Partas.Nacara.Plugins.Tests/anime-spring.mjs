// Generates anime-spring.json: anime.js 4.5.0 spring solver output, the reference for Spring.fs.
import { spring } from '../../src/Partas.Nacara.Plugins.Motion/vendor/anime.esm.js';

const sets = [
  [1, 100, 10],
  [1, 300, 20],
  [1, 600, 15],
  [1, 80, 20],
  [1, 100, 20],
  [1, 100, 30],
  [2, 200, 10],
];

const out = sets.map(([mass, stiffness, damping]) => {
  const s = spring({ mass, stiffness, damping });
  const samples = [];
  for (let i = 1; i <= 9; i++) {
    const t = i / 10;
    samples.push({ t, solve: s.solve(s.solverDuration * t) });
  }
  return { mass, stiffness, damping, solverDuration: s.solverDuration, settlingDuration: s.settlingDuration, samples };
});

console.log(JSON.stringify(out, null, 2));
