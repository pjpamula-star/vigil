import React from 'react';
import { AbsoluteFill, Img, staticFile } from 'remotion';
import { color, font, words } from './config';
import { Eye } from './components/Eye';
import { Finish } from './components/Finish';

/** docs/recap.png is the app's own screenshot of the finished recap: 1350 x 2086 (2x of a 540 px window at 125%). */
const RECAP = { w: 1350, h: 2086 };
const NEEDS = { top: 0.529, bottom: 0.603, left: 0.059, right: 0.942 };

/** A window showing part of the recap: rows y0..y1 (fractions), scaled to `width`. */
const RecapCrop: React.FC<{ y0: number; y1: number; width: number; ring?: boolean }> = ({ y0, y1, width, ring }) => {
  const s = width / RECAP.w;
  const h = (y1 - y0) * RECAP.h * s;
  return (
    <div style={{ position: 'relative', width, height: h, borderRadius: 16, overflow: 'hidden', boxShadow: '0 30px 90px rgba(0,0,0,0.5), 0 0 0 1px rgba(255,255,255,0.12)' }}>
      <Img src={staticFile('recap.png')} style={{ position: 'absolute', left: 0, top: -y0 * RECAP.h * s, width, height: RECAP.h * s }} />
      {ring ? (
        <div style={{ position: 'absolute', left: NEEDS.left * width - 8, top: (NEEDS.top - y0) * RECAP.h * s - 6, width: (NEEDS.right - NEEDS.left) * width + 16, height: (NEEDS.bottom - NEEDS.top) * RECAP.h * s + 12, borderRadius: 16, border: `4px solid ${color.amberA}`, boxShadow: '0 0 26px rgba(255,179,64,0.45)' }} />
      ) : null}
    </div>
  );
};

/** LinkedIn cover: the eye next to the recap, "What needs you, first." */
export const Cover: React.FC = () => (
  <AbsoluteFill style={{ background: `linear-gradient(180deg, ${color.dawn[0]} 0%, ${color.dawn[1]} 62%, ${color.dawn[2]} 100%)` }}>
    <div style={{ position: 'absolute', left: 60, right: 60, top: 120, fontFamily: font.display, fontWeight: 650, fontSize: 92, lineHeight: 1.05, color: color.text1, textAlign: 'center', letterSpacing: '-0.02em' }}>
      {words.first}
    </div>
    <div style={{ position: 'absolute', left: 70, top: 395 }}>
      <Eye size={190} look={0.9} lookY={0.4} dot={color.amberA} />
    </div>
    <div style={{ position: 'absolute', left: 300, top: 300 }}>
      <RecapCrop y0={0} y1={0.84} width={700} ring />
    </div>
    <Finish frame={3} grain={0.05} vignette={0.3} />
  </AbsoluteFill>
);

/** GitHub social preview, 1280 x 640, 80 px margins: the eye and name left, the recap right. */
export const Social: React.FC = () => (
  <AbsoluteFill style={{ background: color.window }}>
    <div style={{ position: 'absolute', left: 80, top: 80, bottom: 80, width: 470, display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 22 }}>
        <Eye size={118} look={0.5} />
        <div style={{ fontFamily: font.display, fontWeight: 650, fontSize: 112, color: color.text1, letterSpacing: '-0.02em', lineHeight: 1 }}>Vigil</div>
      </div>
      <div style={{ fontFamily: font.display, fontWeight: 500, fontSize: 38, lineHeight: 1.22, color: color.text2, marginTop: 30 }}>
        See what needs you when you open your laptop.
      </div>
    </div>
    <div style={{ position: 'absolute', right: 80, top: 80 }}>
      <RecapCrop y0={0.232} y1={0.763} width={RECAP.w * (480 / ((0.763 - 0.232) * RECAP.h))} />
    </div>
  </AbsoluteFill>
);
