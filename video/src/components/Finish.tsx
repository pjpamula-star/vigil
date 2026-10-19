import React from 'react';
import { AbsoluteFill } from 'remotion';

/** Film grain (seeded per frame) and a gentle vignette. Kept subtle: it should be felt, not seen. */
export const Finish: React.FC<{ frame: number; grain?: number; vignette?: number }> = ({ frame, grain = 0.07, vignette = 0.42 }) => (
  <AbsoluteFill style={{ pointerEvents: 'none' }}>
    <AbsoluteFill style={{ background: `radial-gradient(120% 90% at 50% 45%, transparent 55%, rgba(0,0,0,${vignette}) 100%)` }} />
    <AbsoluteFill style={{ opacity: grain, mixBlendMode: 'overlay' }}>
      <svg width="100%" height="100%">
        <filter id="grain">
          <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves="2" seed={frame % 97} stitchTiles="stitch" />
          <feColorMatrix type="saturate" values="0" />
        </filter>
        <rect width="100%" height="100%" filter="url(#grain)" />
      </svg>
    </AbsoluteFill>
  </AbsoluteFill>
);

/** VHS rewind: scan lines, a slight color split and a rewind mark. Only in the rewind gag. */
export const Vhs: React.FC<{ amount: number; t: number }> = ({ amount, t }) => {
  if (amount <= 0.001) return null;
  const roll = (t * 900) % 1350;
  return (
    <AbsoluteFill style={{ pointerEvents: 'none', opacity: amount }}>
      <AbsoluteFill style={{ background: 'repeating-linear-gradient(0deg, rgba(0,0,0,0.28) 0 2px, transparent 2px 5px)' }} />
      <div style={{ position: 'absolute', left: 0, right: 0, top: roll - 80, height: 80, background: 'linear-gradient(180deg, transparent, rgba(255,255,255,0.10), transparent)' }} />
      <div style={{ position: 'absolute', left: 64, top: 70, color: '#fff', fontFamily: 'Consolas, monospace', fontSize: 54, fontWeight: 700, letterSpacing: '-0.08em', textShadow: '3px 0 rgba(255,0,60,0.7), -3px 0 rgba(0,220,255,0.7)' }}>
        ◀◀
      </div>
    </AbsoluteFill>
  );
};
