import React from 'react';
import { AbsoluteFill } from 'remotion';
import { beats as b, color, font, scenes, words } from '../config';
import { Eye } from '../components/Eye';
import { Words } from '../components/Type';
import { clamp, easeInOut, easeOut, keys, ramp, spring } from '../motion';
import type { Layout } from '../World';

const Backdrop: React.FC<{ t: number }> = ({ t }) => (
  <AbsoluteFill style={{ background: `radial-gradient(90% 60% at 50% 18%, #2A2F45 0%, ${color.window} 70%)`, opacity: ramp(t, scenes.trust.start - 0.2, 0.35) }} />
);

/** 7. Trust: three short lines, each with an icon that moves once. */
export const Trust: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  if (t < scenes.trust.start - 0.2 || t >= scenes.end.start + 0.3) return null;
  const leave = ramp(t, scenes.end.start - 0.15, 0.35);
  const items = [
    { at: b.trust1, text: words.trust1, icon: <Restore p={ramp(t, b.trust1 + 0.1, 0.7, easeInOut)} /> },
    { at: b.trust2, text: words.trust2, icon: <Watchdog p={ramp(t, b.trust2 + 0.15, 0.9, easeInOut)} /> },
    { at: b.trust3, text: words.trust3, icon: <Win p={ramp(t, b.trust3 + 0.1, 0.6)} /> },
  ];
  const landscape = L.W > L.H;
  return (
    <AbsoluteFill>
      <Backdrop t={t} />
      <div style={{ position: 'absolute', left: landscape ? L.W * 0.14 : 90, right: 70, top: landscape ? L.H * 0.24 : L.H * 0.3, opacity: 1 - leave, transform: `translateY(${-leave * 40}px)` }}>
        {items.map((it, i) => {
          const p = spring(t, it.at, 300, 24);
          return (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 34, marginBottom: 84, opacity: clamp(p * 2), transform: `translateY(${(1 - p) * 30}px)` }}>
              <div style={{ width: 96, height: 96, flex: 'none' }}>{it.icon}</div>
              <div style={{ fontFamily: font.display, fontWeight: 600, fontSize: landscape ? 80 : 66, color: color.text1, lineHeight: 1.1, maxWidth: landscape ? 1500 : 780 }}>{it.text}</div>
            </div>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

/** A circular arrow that turns once: your settings come back. */
const Restore: React.FC<{ p: number }> = ({ p }) => (
  <svg width="96" height="96" viewBox="0 0 96 96" style={{ transform: `rotate(${-300 * p}deg)` }}>
    <path d="M70 30 A28 28 0 1 0 76 52" fill="none" stroke={color.green} strokeWidth="8" strokeLinecap="round" />
    <path d="M58 26 L72 28 L70 14" fill="none" stroke={color.green} strokeWidth="8" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
/** A clock whose hand sweeps once around: the watchdog that checks every 10 minutes. */
const Watchdog: React.FC<{ p: number }> = ({ p }) => (
  <svg width="96" height="96" viewBox="0 0 96 96">
    <circle cx="48" cy="48" r="36" fill="none" stroke={color.amberA} strokeWidth="7" />
    <line x1="48" y1="48" x2="48" y2="22" stroke={color.amberA} strokeWidth="7" strokeLinecap="round" transform={`rotate(${p * 360} 48 48)`} />
    <circle cx="48" cy="48" r="5" fill={color.amberA} />
  </svg>
);

/** A window drawing itself: a native Windows app. */
const Win: React.FC<{ p: number }> = ({ p }) => (
  <svg width="96" height="96" viewBox="0 0 96 96">
    <rect x="10" y="16" width="76" height="64" rx="10" fill="none" stroke={color.blue} strokeWidth="7" strokeDasharray="280" strokeDashoffset={280 * (1 - p)} />
    <path d="M10 34 H86" stroke={color.blue} strokeWidth="7" opacity={p} />
  </svg>
);

/** 8. The end card: the eye swings in on an arc and settles; it keeps blinking so the last frame is alive. */
export const End: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  if (t < scenes.end.start - 0.2) return null;
  const p = ramp(t, b.swingIn, 0.75, easeOut);
  const landscape = L.W > L.H;
  const cx = landscape ? L.W * 0.5 : L.W * 0.5;
  const cy = landscape ? L.H * 0.27 : L.H * 0.36;
  // An arc in from the upper left.
  const ang = (1 - p) * -1.2;
  const x = cx + Math.sin(ang) * 420;
  const y = cy - (1 - Math.cos(ang)) * 420;
  const blink = (at: number) => (t > at && t < at + 0.24 ? Math.sin(((t - at) / 0.24) * Math.PI) : 0);
  const open = ramp(t, b.swingIn + 0.35, 0.35) * (1 - Math.max(blink(b.endLine2 + 0.9), blink(b.endLine2 + 2.2)));
  const look = keys(t, [[b.swingIn, 0.6], [b.swingIn + 0.9, 0], [b.endLine2 + 1.4, 0], [b.endLine2 + 1.7, 0.5], [b.endLine2 + 2.0, 0]], easeInOut);
  const size = landscape ? 250 : 210;
  return (
    <AbsoluteFill>
      <Backdrop t={t} />
      <div style={{ position: 'absolute', left: x - size / 2, top: y - size / 2, transform: `rotate(${ang * -18}deg)` }}>
        <Eye size={size} open={open} look={look} ink={color.text1} />
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: cy + 150 * (landscape ? 1.3 : 1) }}>
        <Words text="Vigil" t={t} start={b.endName} end={99} size={160} weight={650} align="center" />
      </div>
      <div style={{ position: 'absolute', left: 60, right: 60, top: cy + 345 * (landscape ? 1.3 : 1) }}>
        <Words text={words.endLine1} t={t} start={b.endLine1} end={99} size={58} weight={600} align="center" />
      </div>
      <div style={{ position: 'absolute', left: 60, right: 60, top: cy + 430 * (landscape ? 1.3 : 1) }}>
        <Words text={words.endLine2} t={t} start={b.endLine2} end={99} size={52} weight={500} ink={color.text2} align="center" />
      </div>
    </AbsoluteFill>
  );
};
