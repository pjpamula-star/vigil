import React from 'react';
import { color, font } from '../config';
import { clamp, ramp, spring } from '../motion';

/** Type scale for the frame size (landscape frames are bigger, so the words are too). */
export const TypeScale = React.createContext(1);
/** Default alignment for captions: centred on the tall frame, left column on the wide one. */
export const TypeAlign = React.createContext<'left' | 'center'>('center');

/**
 * Kinetic type: words arrive one at a time with a short rise and a slight scale, and the whole line
 * leaves upward at `end`. Pure function of t.
 */
export const Words: React.FC<{
  text: string;
  t: number;
  start: number;
  end: number;
  size: number;
  weight?: number;
  ink?: string;
  stagger?: number;
  align?: 'left' | 'center';
  maxWidth?: number;
  punch?: boolean; // hook: harder landing
  fromLeft?: boolean; // slides in from the left as one piece (labels)
  style?: React.CSSProperties;
}> = ({ text, t, start, end, size: size0, weight = 650, ink = color.text1, stagger = 0.085, align: align0, maxWidth: maxWidth0, punch, fromLeft, style }) => {
  const scale = React.useContext(TypeScale);
  const align = align0 ?? React.useContext(TypeAlign);
  const size = size0 * scale;
  const maxWidth = maxWidth0 ? maxWidth0 * scale : undefined;
  if (t < start - 0.01 || t > end + 0.5) return null;
  const out = ramp(t, end, 0.32);
  if (out >= 1) return null;
  const words = text.split(' ');
  if (fromLeft) {
    const p = spring(t, start, 220, 26);
    return (
      <div style={{ ...base(size, weight, ink, align, maxWidth), transform: `translate(${(1 - p) * -60}px, ${-out * 40}px)`, opacity: clamp(p * 2) * (1 - out), ...style }}>
        {text}
      </div>
    );
  }
  return (
    <div style={{ ...base(size, weight, ink, align, maxWidth), transform: `translateY(${-out * 40}px)`, opacity: 1 - out, ...style }}>
      {words.map((w, i) => {
        const t0 = start + i * stagger;
        const p = punch ? spring(t, t0, 420, 22) : spring(t, t0, 240, 26);
        const k = clamp((t - t0) / 0.12);
        return (
          <span key={i} style={{ display: 'inline-block', marginRight: i < words.length - 1 ? '0.26em' : 0, opacity: k, transform: `translateY(${(1 - p) * size * 0.32}px) scale(${0.94 + 0.06 * p})`, transformOrigin: '50% 80%' }}>
            {w}
          </span>
        );
      })}
    </div>
  );
};

const base = (size: number, weight: number, ink: string, align: 'left' | 'center', maxWidth?: number): React.CSSProperties => ({
  fontFamily: font.display,
  fontSize: size,
  fontWeight: weight,
  color: ink,
  lineHeight: 1.08,
  letterSpacing: size > 80 ? '-0.02em' : '-0.01em',
  textAlign: align,
  textWrap: 'balance',
  maxWidth,
  margin: align === 'center' ? '0 auto' : undefined,
});

/** The clock the whole night runs on. */
export const Clock: React.FC<{ text: string; size: number; blur?: number; ink?: string }> = ({ text, size, blur = 0, ink = color.text2 }) => (
  <div style={{ fontFamily: font.display, fontSize: size, fontWeight: 600, color: ink, fontVariantNumeric: 'tabular-nums', filter: blur > 0.3 ? `blur(${blur}px)` : undefined, letterSpacing: '0.01em' }}>
    {text}
  </div>
);
