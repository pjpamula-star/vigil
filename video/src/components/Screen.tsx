import React from 'react';
import { color, font, sessions } from '../config';
import { clamp, ramp, spring } from '../motion';

/** What Claude Code looks like working in a terminal: one long task, a test run at some percent. */
export const Terminal: React.FC<{ t: number; lines: number; percent: number; w: number; h: number }> = ({ t, lines, percent, w, h }) => {
  const rows = [
    { c: color.text3, s: 'PS C:\\Users\\sam\\api-server> claude' },
    { c: color.text1, s: '> add rate limiting with tests' },
    { c: color.text2, s: '● Read src/server.ts' },
    { c: color.text2, s: '● Edit src/middleware/rateLimit.ts' },
    { c: color.text2, s: '● Write tests/rateLimit.test.ts' },
  ];
  const cells = 20;
  const filled = Math.round((percent / 100) * cells);
  const blink = Math.floor(t * 2) % 2 === 0;
  const fs = h * 0.062;
  return (
    <div style={{ width: w, height: h, background: '#0C0C0C', fontFamily: font.mono, fontSize: fs, lineHeight: 1.55, padding: h * 0.07, boxSizing: 'border-box', color: color.text1 }}>
      <div style={{ display: 'flex', gap: fs * 0.5, marginBottom: fs * 0.9, alignItems: 'center' }}>
        {['#FF5F57', '#FEBC2E', '#28C840'].map((c) => (
          <div key={c} style={{ width: fs * 0.55, height: fs * 0.55, borderRadius: '50%', background: c, opacity: 0.0 }} />
        ))}
        <div style={{ fontFamily: font.text, fontSize: fs * 0.8, color: color.text3 }}>api-server</div>
      </div>
      {rows.slice(0, Math.max(0, Math.floor(lines))).map((r, i) => (
        <div key={i} style={{ color: r.c, whiteSpace: 'nowrap' }}>{r.s}</div>
      ))}
      {lines >= 5.5 ? (
        <div style={{ whiteSpace: 'nowrap', color: color.text1, marginTop: fs * 0.2 }}>
          <span style={{ color: color.orange }}>● </span>Running tests{' '}
          <span style={{ color: color.blue }}>{'▓'.repeat(filled)}</span>
          <span style={{ color: '#3A3A3C' }}>{'░'.repeat(cells - filled)}</span>{' '}
          <span style={{ fontWeight: 600 }}>{Math.round(percent)}%</span>
          <span style={{ opacity: blink ? 1 : 0 }}> ▌</span>
        </div>
      ) : null}
    </div>
  );
};

export type CardState = 'working' | 'done' | 'needs';

/** The avatar the app draws: a gradient circle with initials, plus its state ring. */
export const Avatar: React.FC<{ i: number; size: number; state: CardState; t: number; pulse?: number }> = ({ i, size, state, t, pulse = 0 }) => {
  const s = sessions[i];
  const ring = size + size * 0.32;
  const spin = (t * 300) % 360;
  return (
    <div style={{ position: 'relative', width: ring, height: ring, flex: 'none' }}>
      {state === 'working' ? (
        <svg width={ring} height={ring} style={{ position: 'absolute', inset: 0, transform: `rotate(${spin}deg)` }}>
          <circle cx={ring / 2} cy={ring / 2} r={ring / 2 - 3} fill="none" stroke={color.blue} strokeWidth={4} strokeDasharray={`${ring * 0.9} ${ring * 3}`} strokeLinecap="round" />
        </svg>
      ) : null}
      {state === 'needs' ? (
        <div style={{ position: 'absolute', inset: 0, borderRadius: '50%', border: `${4 + pulse * 2}px solid ${color.amberA}`, boxSizing: 'border-box', transform: `scale(${1 + pulse * 0.12})` }} />
      ) : null}
      <div
        style={{
          position: 'absolute', left: (ring - size) / 2, top: (ring - size) / 2, width: size, height: size, borderRadius: '50%',
          background: `linear-gradient(135deg, ${s.hue[0]}, ${s.hue[1]})`, display: 'flex', alignItems: 'center', justifyContent: 'center',
          color: '#fff', fontFamily: font.text, fontWeight: 600, fontSize: size * 0.36,
        }}
      >
        {s.initials}
      </div>
    </div>
  );
};

/** One session card, in the app's card style. `at` is when it last changed state (for the pop). */
export const SessionCard: React.FC<{ i: number; state: CardState; t: number; at: number; time?: string; w: number; pulse?: number }> = ({ i, state, t, at, time, w, pulse = 0 }) => {
  const s = sessions[i];
  const pop = state === 'working' ? 0 : spring(t, at, 260, 14);
  const scale = state === 'working' ? 1 : 1 + (1 - pop) * 0.0 + Math.sin(clamp((t - at) / 0.35) * Math.PI) * 0.07;
  const label = state === 'working' ? 'Working' : state === 'done' ? `Done${time ? ` ${time}` : ''}` : 'Needs you';
  const labelColor = state === 'working' ? color.blue : state === 'done' ? color.green : color.amberA;
  const fs = w * 0.07;
  return (
    <div
      style={{
        position: 'relative', width: w, boxSizing: 'border-box', padding: `${fs * 0.9}px ${fs}px`, borderRadius: fs * 1.1, background: 'rgba(44,44,46,0.92)',
        border: `2px solid ${state === 'needs' ? `rgba(255,179,64,${0.35 + pulse * 0.5})` : 'rgba(255,255,255,0.08)'}`,
        display: 'flex', alignItems: 'center', gap: fs * 0.8, transform: `scale(${scale})`, fontFamily: font.text,
      }}
    >
      <Avatar i={i} size={fs * 2.3} state={state} t={t} pulse={pulse} />
      <div style={{ minWidth: 0 }}>
        <div style={{ color: color.text1, fontWeight: 600, fontSize: fs * 1.05, whiteSpace: 'nowrap' }}>{s.name}</div>
        <div style={{ color: labelColor, fontSize: fs * 0.9, marginTop: fs * 0.15, whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: fs * 0.3 }}>
          {state === 'done' ? <Check size={fs * 0.9} p={ramp(t, at, 0.35)} /> : null}
          {label}
        </div>
      </div>
      {state === 'done' ? <Sparks t={t} at={at} size={w} /> : null}
    </div>
  );
};

const Check: React.FC<{ size: number; p: number }> = ({ size, p }) => (
  <svg width={size} height={size} viewBox="0 0 20 20">
    <path d="M4 10.5 L8.2 14.5 L16 6" fill="none" stroke={color.green} strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" strokeDasharray={20} strokeDashoffset={20 * (1 - p)} />
  </svg>
);

/** Three or four tiny sparks off the card's corner when it finishes. Seeded by card position. */
const Sparks: React.FC<{ t: number; at: number; size: number }> = ({ t, at, size }) => {
  const d = t - at;
  if (d < 0 || d > 0.6) return null;
  const p = d / 0.6;
  const dirs = [[-0.9, -1], [0.2, -1.2], [1, -0.6], [1.1, 0.3]];
  return (
    <div style={{ position: 'absolute', left: size * 0.12, top: size * 0.1, pointerEvents: 'none' }}>
      {dirs.map(([x, y], k) => (
        <div key={k} style={{ position: 'absolute', width: 7, height: 7, borderRadius: 4, background: color.green, opacity: 1 - p, transform: `translate(${x * p * size * 0.12}px, ${y * p * size * 0.12}px) scale(${1 - p * 0.5})` }} />
      ))}
    </div>
  );
};
