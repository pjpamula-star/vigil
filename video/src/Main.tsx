import React from 'react';
import { AbsoluteFill, useCurrentFrame, useVideoConfig } from 'remotion';
import { beats as b, readmeCut, scenes } from './config';
import { Finish } from './components/Finish';
import { Enter, Hook, Morning, Night, Problem, Rewind } from './scenes/Captions';
import { Payoff } from './scenes/Payoff';
import { PhoneScene } from './scenes/Phone';
import { End, Trust } from './scenes/Closing';
import { CalmMode, Watcher, World, layoutFor } from './World';
import { clamp } from './motion';
import { TypeAlign, TypeScale } from './components/Type';

/** Moments that move fast enough to need motion blur: the slam, the camera moves, the whip, the rewind. */
const fastWindows: Array<[number, number]> = [
  [b.slam - 0.05, b.slam + 0.55],
  [b.tiltUp, b.tiltUp + 0.55],
  [b.clockWhip - 0.05, b.clockWhip + 0.65],
  [b.morningOpen - 0.1, b.morningOpen + 0.4],
  [scenes.rewind.start, scenes.rewind.end - 0.4],
  [b.settle, b.settle + 0.6],
  [b.closeGently, b.cardsIn + 0.3],
  [b.openMorning, b.openMorning + 0.5],
  [b.zoomScreen, b.flatIn + 0.35],
  [b.swingIn, b.swingIn + 0.6],
];

/** One moment of the story at master time t. Pure: same t, same picture. */
export const Story: React.FC<{ t: number; blur?: boolean }> = ({ t, blur = false }) => {
  const { width, height } = useVideoConfig();
  const L = layoutFor(width, height);
  return (
    <TypeScale.Provider value={L.landscape ? 1.22 : 1}>
    <TypeAlign.Provider value={L.landscape ? 'left' : 'center'}>
    <AbsoluteFill style={{ background: '#070B1C', overflow: 'hidden' }}>
      <World t={t} L={L} blur={blur} />
      <Hook t={t} L={L} />
      <Problem t={t} L={L} />
      <Rewind t={t} L={L} />
      <Enter t={t} L={L} />
      <Night t={t} L={L} />
      <Morning t={t} L={L} />
      <Payoff t={t} L={L} />
      {/* The eye is on top of everything: it rises out of the dark and hands over to the app's own eye. */}
      <Watcher t={t} L={L} />
      {/* Updates on your phone during the night */}
      <PhoneScene t={t} L={L} />
      <Trust t={t} L={L} />
      <End t={t} L={L} />
    </AbsoluteFill>
    </TypeAlign.Provider>
    </TypeScale.Provider>
  );
};

/** The launch video. Same timeline at every size; the layout adapts (1080 x 1350 or 1920 x 1080). */
export const Main: React.FC<{ blur?: boolean }> = ({ blur = true }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const t = frame / fps;
  // Motion blur on the fast moves, applied to the moving foreground only (see World).
  const fast = blur && fastWindows.some(([a, z]) => t >= a && t <= z);
  return (
    <AbsoluteFill>
      <Story t={t} blur={fast} />
      <Finish frame={frame} />
    </AbsoluteFill>
  );
};

/**
 * The calm README cut: scenes 4 to 6 only, no shake, glitch or rewind. Pieces of the master timeline
 * play at normal speed and cross-fade into each other, then it holds on the finished recap.
 */
export const ReadmeCut: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const tau = frame / fps;
  const D = readmeCut.dip;
  const n = readmeCut.pieces.length;
  // Pieces play back to back; each dips to the night sky at its edges (never two pictures at once).
  const layers: Array<{ t: number; o: number }> = [];
  let at = 0;
  readmeCut.pieces.forEach(([a, z], i) => {
    const len = z - a;
    const local = tau - at;
    const last = i === n - 1;
    if (local >= 0 && (local < len || (last && local >= len))) {
      const fadeIn = i === 0 ? 1 : clamp(local / D);
      const fadeOut = last ? 1 : clamp((len - local) / D);
      layers.push({ t: a + Math.min(local, len), o: Math.min(fadeIn, fadeOut) });
    }
    at += len;
  });
  return (
    <CalmMode.Provider value>
      <AbsoluteFill style={{ background: '#070B1C' }}>
        {layers.map((l, i) => (
          <AbsoluteFill key={i} style={{ opacity: l.o }}>
            <Story t={l.t} />
          </AbsoluteFill>
        ))}
        {/* The loop: fade to the night sky the cut opens on. */}
        <AbsoluteFill style={{ background: '#070B1C', opacity: clamp((tau - (readmeDuration() - readmeCut.loopFade)) / readmeCut.loopFade) }} />
        {/* No grain in the GIF: it changes every pixel every frame. */}
        <Finish frame={frame} grain={0} vignette={0.3} />
      </AbsoluteFill>
    </CalmMode.Provider>
  );
};

export const readmeDuration = () => readmeCut.pieces.reduce((s, [a, z]) => s + (z - a), 0) + readmeCut.hold;
