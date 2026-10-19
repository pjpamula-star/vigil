import React from 'react';

/**
 * A flat-shaded laptop in real 3D (CSS transforms), so the lid can close continuously and the camera can
 * move around it. lid: 0 = open (leaning back a little), 1 = shut. The screen face holds any content.
 * xray: content drawn on top of the shut lid, as if we could see through it.
 */
export const Laptop: React.FC<{
  width: number; // base width in px
  lid: number;
  tilt: number; // camera elevation in degrees (0 = eye level, 70 = looking down)
  yaw: number; // camera turn in degrees
  screen: React.ReactNode;
  screenGlow?: number; // 0..1
  xray?: React.ReactNode;
  xrayAmount?: number;
  squash?: number; // -1..1 squash and stretch on the slam
  led?: number; // 0..1 the little sleep light
  lightLine?: number; // 0..1 the screen's light squeezed into the gap as the lid shuts
}> = ({ width, lid, tilt, yaw, screen, screenGlow = 1, xray, xrayAmount = 0, squash = 0, led = 0, lightLine = 0 }) => {
  const W = width;
  const D = W * 0.66; // base depth
  const T = W * 0.028; // base thickness
  const LH = W * 0.64; // lid height
  const open = 14; // degrees past upright when open
  const angle = open + (-90 - open) * lid; // rotateX
  const sy = 1 - squash * 0.08;
  const sx = 1 + squash * 0.04;

  const face = (extra: React.CSSProperties): React.CSSProperties => ({ position: 'absolute', backfaceVisibility: 'hidden', ...extra });
  const alu = 'linear-gradient(180deg, #4A4A50 0%, #3A3A3F 60%, #303034 100%)';

  return (
    <div style={{ position: 'relative', width: 0, height: 0, transformStyle: 'preserve-3d', transform: `rotateX(${-tilt}deg) rotateY(${yaw}deg) scale3d(${sx}, ${sy}, ${sx})` }}>
      {/* base top (keyboard deck) */}
      <div style={face({ width: W, height: D, left: -W / 2, top: -D / 2, transform: `rotateX(90deg)`, background: 'linear-gradient(180deg, #3C3C42, #34343A)', borderRadius: W * 0.02 })}>
        <div style={{ position: 'absolute', left: W * 0.08, right: W * 0.08, top: D * 0.1, height: D * 0.5, borderRadius: W * 0.01, background: 'repeating-linear-gradient(90deg, #26262A 0 5.5%, transparent 5.5% 6.4%), repeating-linear-gradient(0deg, transparent 0 17%, #3C3C42 17% 20%)', opacity: 0.9 }} />
        <div style={{ position: 'absolute', left: W * 0.34, right: W * 0.34, top: D * 0.66, height: D * 0.26, borderRadius: W * 0.012, background: '#2E2E33' }} />
      </div>
      {/* the screen's light on the keyboard, squeezed toward the front as the lid shuts */}
      {lightLine > 0.01 ? (
        <div style={{ position: 'absolute', width: W * 0.96, height: D * 0.14, left: -W * 0.48, top: -D * 0.07, transform: `rotateX(90deg) translateY(${D * 0.42}px) translateZ(2px)`, background: 'linear-gradient(0deg, rgba(190,225,255,0.95), rgba(120,180,255,0))', opacity: lightLine, filter: 'blur(3px)' }} />
      ) : null}
      {/* base front edge */}
      <div style={face({ width: W, height: T, left: -W / 2, top: 0, transform: `translateZ(${D / 2}px)`, background: '#2A2A2E', borderRadius: `0 0 ${T}px ${T}px` })}>
        {lightLine > 0.01 ? <div style={{ position: 'absolute', left: W * 0.02, right: W * 0.02, top: -2, height: 4, borderRadius: 2, background: '#DDEEFF', boxShadow: `0 0 ${18 * lightLine}px rgba(170,215,255,${lightLine})`, opacity: lightLine }} /> : null}
        <div style={{ position: 'absolute', right: W * 0.06, top: T * 0.35, width: T * 0.6, height: T * 0.3, borderRadius: T, background: `rgba(255,255,255,${0.15 + led * 0.8})` }} />
      </div>
      {/* base bottom shadow */}
      <div style={{ position: 'absolute', width: W * 1.1, height: D * 1.1, left: -W * 0.55, top: -D * 0.55, transform: `rotateX(90deg) translateZ(${-T}px)`, background: 'radial-gradient(closest-side, rgba(0,0,0,0.55), transparent)', filter: 'blur(8px)' }} />

      {/* lid, hinged at the back edge of the base */}
      <div style={{ position: 'absolute', left: -W / 2, top: -LH - 3, width: W, height: LH, transformStyle: 'preserve-3d', transformOrigin: '50% 100%', transform: `translateZ(${-D / 2}px) rotateX(${angle}deg)` }}>
        {/* screen side */}
        <div style={face({ inset: 0, background: '#111114', borderRadius: W * 0.022, padding: W * 0.018, boxSizing: 'border-box' })}>
          <div style={{ width: '100%', height: '100%', overflow: 'hidden', borderRadius: W * 0.008, position: 'relative' }}>
            {screen}
            <div style={{ position: 'absolute', inset: 0, background: '#000', opacity: 1 - screenGlow }} />
          </div>
        </div>
        {/* back of the lid */}
        <div style={face({ inset: 0, background: alu, borderRadius: W * 0.022, transform: 'rotateX(180deg)', overflow: 'hidden' })}>
          {xray && xrayAmount > 0 ? (
            <div style={{ position: 'absolute', inset: W * 0.03, opacity: xrayAmount }}>
              <div style={{ position: 'absolute', inset: 0, border: '3px dashed rgba(100,210,255,0.55)', borderRadius: W * 0.012, background: 'rgba(7,11,28,0.72)' }} />
              <div style={{ position: 'absolute', inset: 0, overflow: 'hidden', borderRadius: W * 0.012 }}>{xray}</div>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
};
