// Renders stills from one bundle.
//   node scripts/stills.mjs <composition> <outDir> --times 0.5,1.2,...   (seconds)
//   node scripts/stills.mjs <composition> <outDir> --every 0.5           (whole video)
//   add --sheet name.png to also tile them into one contact sheet (needs ffmpeg)
import { bundle } from '@remotion/bundler';
import { renderStill, selectComposition } from '@remotion/renderer';
import { mkdirSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const [id, outDir, ...rest] = process.argv.slice(2);
const opt = (name) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : undefined; };
const root = path.resolve(import.meta.dirname, '..');
const serveUrl = await bundle({ entryPoint: path.join(root, 'src/index.ts'), publicDir: path.join(root, 'public') });
const composition = await selectComposition({ serveUrl, id });
const fps = composition.fps;
let times = opt('--times')?.split(',').map(Number);
if (!times) {
  const every = Number(opt('--every') || 0.5);
  times = Array.from({ length: Math.floor(composition.durationInFrames / fps / every) + 1 }, (_, i) => +(i * every).toFixed(3));
}
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });
const names = [];
for (const [i, t] of times.entries()) {
  const frame = Math.min(composition.durationInFrames - 1, Math.round(t * fps));
  const name = `${String(i).padStart(3, '0')}_${t.toFixed(2)}s.png`;
  await renderStill({ composition, serveUrl, output: path.join(outDir, name), frame, imageFormat: 'png' });
  names.push(name);
}
console.log(`${names.length} stills in ${outDir}`);

const sheet = opt('--sheet');
if (sheet) {
  const cols = Number(opt('--cols') || 5);
  const tw = Number(opt('--tw') || 360);
  const ff = process.env.FFMPEG || 'ffmpeg';
  const rows = Math.ceil(names.length / cols);
  const r = { status: 1 }; // Windows ffmpeg has no glob support; always use the numbered copy below.
  if (r.status !== 0) {
    // Windows ffmpeg builds have no glob support: fall back to a numbered copy.
    const seq = path.join(outDir, 'seq');
    mkdirSync(seq, { recursive: true });
    const { copyFileSync } = await import('node:fs');
    names.forEach((n, i) => copyFileSync(path.join(outDir, n), path.join(seq, `${String(i).padStart(3, '0')}.png`)));
    spawnSync(ff, ['-y', '-loglevel', 'error', '-framerate', '1', '-i', path.join(seq, '%03d.png'), '-vf', `scale=${tw}:-1,tile=${cols}x${rows}:padding=8:color=0x444444`, '-frames:v', '1', sheet], { stdio: 'inherit' });
    rmSync(seq, { recursive: true, force: true });
  }
  console.log(sheet);
}
