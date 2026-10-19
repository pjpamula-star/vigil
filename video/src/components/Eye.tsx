import React from 'react';
import { color } from '../config';

/**
 * The Vigil eye, drawn from the app's brand glyph (assets/brand/vigil-glyph.svg): an almond outline and a
 * crescent-moon pupil. Same moves as the app's EyeView: open/shut, glance, and a status dot
 * (amber = needs you, red = too hot), so the character on screen is the one people get when they install.
 */
export const Eye: React.FC<{
  size: number;
  open?: number; // 1 open, 0 shut
  look?: number; // -1 left .. 1 right
  lookY?: number; // -1 up .. 1 down
  ink?: string;
  dot?: string | null;
  dotScale?: number;
  squint?: number; // 0..1, narrows the eye (heat)
}> = ({ size, open = 1, look = 0, lookY = 0, ink = color.text1, dot = null, dotScale = 1, squint = 0 }) => {
  const o = Math.max(0.05, open * (1 - squint * 0.42));
  const px = 50 + look * 9;
  const py = 50 + lookY * 5;
  const id = React.useId().replace(/:/g, '');
  return (
    <svg width={size} height={size} viewBox="0 0 100 100" style={{ overflow: 'visible', display: 'block' }}>
      <defs>
        <clipPath id={`lid-${id}`}>
          <path d="M8 50 C24 27 38 21 50 21 C62 21 76 27 92 50 C76 73 62 79 50 79 C38 79 24 73 8 50Z" />
        </clipPath>
        <mask id={`moon-${id}`}>
          <rect width="100" height="100" fill="#fff" />
          <circle cx={px + 7} cy={py - 5} r="13" fill="#000" />
        </mask>
      </defs>
      <g transform={`translate(0 50) scale(1 ${o}) translate(0 -50)`}>
        <path
          d="M8 50 C24 27 38 21 50 21 C62 21 76 27 92 50 C76 73 62 79 50 79 C38 79 24 73 8 50Z"
          fill="none"
          stroke={ink}
          strokeWidth={7}
          strokeLinejoin="round"
        />
        <g clipPath={`url(#lid-${id})`}>
          <circle cx={px} cy={py} r="17" fill={ink} mask={`url(#moon-${id})`} />
        </g>
      </g>
      {dot ? <circle cx="88" cy="16" r={9 * dotScale} fill={dot} /> : null}
    </svg>
  );
};

/**
 * The lantern alternative (for the character sheet). Same stroke weight as the eye glyph, and its glass is
 * the eye's almond, so it reads as the same family. The flame carries the mood.
 */
export const Lantern: React.FC<{
  size: number;
  flame?: number; // 0 out .. 1 normal .. 1.6 tall
  flameColor?: string;
  shut?: number; // 0 open .. 1 shutters closed
  lean?: number; // -1 .. 1, flame leans toward something
  ink?: string;
  t?: number;
}> = ({ size, flame = 1, flameColor = color.flame, shut = 0, lean = 0, ink = color.text1, t = 0 }) => {
  const flick = 1 + Math.sin(t * 13.1) * 0.04 + Math.sin(t * 7.3) * 0.03;
  const h = 26 * flame * flick;
  const id = React.useId().replace(/:/g, '');
  return (
    <svg width={size} height={size * 1.3} viewBox="0 0 100 130" style={{ overflow: 'visible', display: 'block' }}>
      <defs>
        <clipPath id={`glass-${id}`}>
          <rect x="22" y="40" width="56" height="62" rx="16" />
        </clipPath>
      </defs>
      {/* handle */}
      <path d="M38 22 C38 8 62 8 62 22" fill="none" stroke={ink} strokeWidth={6} strokeLinecap="round" />
      {/* cap */}
      <path d="M26 38 L34 24 L66 24 L74 38 Z" fill={ink} />
      {/* glass + flame */}
      <g clipPath={`url(#glass-${id})`}>
        <rect x="22" y="40" width="56" height="62" fill="rgba(255,194,122,0.10)" />
        {flame > 0.02 ? (
          <path
            d={`M50 ${92 - h * 2.2} C${58 + lean * 8} ${80 - h} ${62} 84 ${60} 90 C58 96 42 96 40 90 C38 84 ${42 + lean * 6} ${80 - h} 50 ${92 - h * 2.2}Z`}
            fill={flameColor}
          />
        ) : null}
        {/* shutters close like an eyelid */}
        <rect x="22" y="40" width="56" height={62 * shut} fill={ink} />
      </g>
      <rect x="22" y="40" width="56" height="62" rx="16" fill="none" stroke={ink} strokeWidth={6} />
      {/* base */}
      <rect x="28" y="104" width="44" height="10" rx="4" fill={ink} />
    </svg>
  );
};
