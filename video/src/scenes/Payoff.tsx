import React from 'react';
import { AbsoluteFill, Img, OffthreadVideo, Sequence, staticFile, useCurrentFrame } from 'remotion';
import { beats as b, color, FPS, scenes, words } from '../config';
import { Words } from '../components/Type';
import { clamp, easeInOut, keys, ramp } from '../motion';
import type { Layout } from '../World';

/** How long the recording is (scripts/demo/record_recap.ps1 -Seconds). After it ends, its last frame holds. */
export const REC_SECONDS = 7;

/**
 * The real recording at recording time rt (seconds). Plays the clip, then holds its exact last frame
 * (recap-still.png, cut from the clip's end): by then the recap has settled and nothing moves.
 */
export const RecapVideo: React.FC<{ rt: number }> = ({ rt }) => {
  const frame = useCurrentFrame();
  const vf = Math.round(rt * FPS);
  if (rt < REC_SECONDS - 0.12) {
    return (
      <Sequence from={frame - vf} layout="none">
        <OffthreadVideo src={staticFile('recap.mp4')} muted style={{ width: '100%', height: '100%', display: 'block' }} />
      </Sequence>
    );
  }
  return <Img src={staticFile('recap-still.png')} style={{ width: '100%', height: '100%', display: 'block' }} />;
};

/** The real recording (video/public/recap.mp4, from scripts/demo/record_recap.ps1). */
export const REC = { w: 674, h: 1042 };
/** Where things sit in the recording, as fractions of its size (measured from the app's own screenshot). */
export const REC_SPOTS = {
  // Centre of the eye glyph and the width of its 100-unit box (the almond spans 84% of it).
  eye: { x: 0.1009, y: 0.0609, size: 0.0883 },
  needsRow: { top: 0.529, bottom: 0.603, left: 0.059, right: 0.942 },
  doneRows: { top: 0.605, bottom: 0.84 },
};

/** Camera over the recording: whole window, then in on what needs you, then down to what got done. */
/** The frame the recording plays in: below the captions, clear of the bottom 15%. */
export function recFrame(L: Layout) {
  return L.landscape ? { x: L.W * 0.47, y: 70, w: L.W * 0.47, h: L.H - 140 } : { x: 50, y: 300, w: L.W - 100, h: 830 };
}

export function recCamera(t: number, L: Layout) {
  const F = recFrame(L);
  const fit = F.h / REC.h;
  // Close enough on a phone to read the rows: the needs-you row fills the frame, then the done rows.
  const near = L.landscape ? 1.45 : 1.62;
  const rows = L.landscape ? 1.25 : 1.42;
  const s = keys(t, [[b.flatIn, fit], [b.pushIn, fit], [b.firstCaption + 0.8, near], [b.secondCaption, near], [b.secondCaption + 1.0, rows], [scenes.payoff.end, rows * 1.04]], easeInOut);
  const fy = keys(t, [[b.flatIn, 0.5], [b.pushIn, 0.5], [b.firstCaption + 0.8, 0.566], [b.secondCaption, 0.566], [b.secondCaption + 1.0, 0.72], [scenes.payoff.end, 0.72]], easeInOut);
  const screenY = F.y + F.h / 2;
  // At the start the whole window shows; as the camera pushes in, the frame is the edge.
  const top = Math.min(F.y, Math.max(F.y + F.h - REC.h * s, screenY - fy * REC.h * s));
  const cx = F.x + F.w / 2;
  return { s, left: cx - (REC.w * s) / 2, top };
}

export const Payoff: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  // The flat recording takes over once the push into the laptop's screen has filled the frame.
  if (t < b.flatIn - 0.1 || t >= scenes.trust.start + 0.4) return null;
  const cam = recCamera(t, L);
  const appear = ramp(t, b.flatIn - 0.05, 0.35);
  const leave = ramp(t, scenes.payoff.end - 0.2, 0.4);
  const ring = t >= b.ring ? ramp(t, b.ring, 0.5) : 0;
  const pulse = t >= b.ring ? Math.sin(clamp((t - b.ring) / 0.9) * Math.PI) : 0;
  const r = REC_SPOTS.needsRow;
  return (
    <AbsoluteFill style={{ opacity: appear * (1 - leave) }}>
      {(() => {
        // Visible part = the recording, cut to the frame.
        const F = recFrame(L);
        const vx = Math.max(F.x, cam.left), vy = Math.max(F.y, cam.top);
        const vr = Math.min(F.x + F.w, cam.left + REC.w * cam.s), vb = Math.min(F.y + F.h, cam.top + REC.h * cam.s);
        return (
          <div style={{ position: 'absolute', left: vx, top: vy, width: vr - vx, height: vb - vy, borderRadius: 18, overflow: 'hidden', boxShadow: '0 40px 120px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.12)' }}>
            <div style={{ position: 'absolute', left: cam.left - vx, top: cam.top - vy, width: REC.w * cam.s, height: REC.h * cam.s }}>
              <RecapVideo rt={t - b.recordingIn} />
            </div>
          </div>
        );
      })()}
      {/* the soft amber ring on what needs you, pulsing once */}
      {ring > 0 ? (
        <div
          style={{
            position: 'absolute',
            left: cam.left + r.left * REC.w * cam.s - 10,
            top: cam.top + r.top * REC.h * cam.s - 8,
            width: (r.right - r.left) * REC.w * cam.s + 20,
            height: (r.bottom - r.top) * REC.h * cam.s + 16,
            borderRadius: 18 * cam.s,
            border: `${3 + pulse * 3}px solid ${color.amberA}`,
            boxShadow: `0 0 ${30 * pulse}px rgba(255,179,64,${0.5 * pulse})`,
            opacity: ring * (1 - ramp(t, b.secondCaption - 0.3, 0.4) * 0.7),
            transform: `scale(${1 + pulse * 0.015})`,
          }}
        />
      ) : null}
      <div style={captionBox(L)}>
        <Words text={words.first} t={t} start={b.firstCaption} end={b.secondCaption - 0.1} size={L.landscape ? 84 : 72} align={L.landscape ? 'left' : 'center'} />
      </div>
      <div style={captionBox(L)}>
        <Words text={words.then} t={t} start={b.secondCaption} end={scenes.payoff.end - 0.15} size={L.landscape ? 84 : 72} align={L.landscape ? 'left' : 'center'} />
      </div>
    </AbsoluteFill>
  );
};

const captionBox = (L: Layout): React.CSSProperties =>
  L.landscape ? { position: 'absolute', left: 110, width: L.W * 0.33, top: L.H * 0.4 } : { position: 'absolute', left: 60, right: 60, top: recFrame(L).y - 150 };

/** Where the recording's own eye is on screen when it appears: centre and box size (for the match cut). */
export function recordingEye(L: Layout) {
  const cam = recCamera(b.recordingIn, L);
  const e = REC_SPOTS.eye;
  return { x: cam.left + e.x * REC.w * cam.s, y: cam.top + e.y * REC.h * cam.s, size: e.size * REC.w * cam.s };
}
