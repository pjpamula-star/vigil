import React from 'react';
import { AbsoluteFill } from 'remotion';
import { color, font } from './config';
import { Eye, Lantern } from './components/Eye';

/** The two character options side by side, in each mood the story needs. */
const moods = [
  { name: 'Calm: Claude is working', eye: { open: 1, look: -0.4, lookY: 0.6 }, lantern: { flame: 1 } },
  { name: 'Hot', eye: { open: 1, squint: 1, ink: color.hotA, dot: color.hotA }, lantern: { flame: 1.55, flameColor: color.hotA } },
  { name: 'Cooled down', eye: { open: 0.85, ink: color.cool }, lantern: { flame: 0.9, flameColor: color.cool } },
  { name: 'Needs you', eye: { open: 1, look: -1, dot: color.amberA }, lantern: { flame: 1.1, flameColor: color.amberA, lean: -1 } },
  { name: 'Done: it can sleep', eye: { open: 0.06 }, lantern: { flame: 0.3, shut: 0.85 } },
];

export const CharacterSheet: React.FC = () => (
  <AbsoluteFill style={{ background: `radial-gradient(80% 60% at 50% 20%, #1A2A55, ${color.night[0]})`, padding: 60, fontFamily: font.display, color: color.text1 }}>
    <div style={{ fontSize: 46, fontWeight: 650 }}>Character options</div>
    <div style={{ fontSize: 26, color: color.text2, marginTop: 8 }}>A: the app's own eye (recommended). B: a lantern drawn to match it.</div>
    {['A', 'B'].map((row) => (
      <div key={row} style={{ display: 'flex', gap: 24, marginTop: 50, alignItems: 'flex-end' }}>
        <div style={{ width: 40, fontSize: 40, fontWeight: 700, color: color.text3 }}>{row}</div>
        {moods.map((m) => (
          <div key={m.name} style={{ width: 172, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 14 }}>
            {row === 'A' ? <Eye size={150} {...m.eye} /> : <Lantern size={120} t={1.3} {...m.lantern} />}
            <div style={{ fontSize: 20, color: color.text2, textAlign: 'center', minHeight: 50 }}>{m.name}</div>
          </div>
        ))}
      </div>
    ))}
  </AbsoluteFill>
);
