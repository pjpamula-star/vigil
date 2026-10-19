import React from 'react';
import { AbsoluteFill, Img, staticFile } from 'remotion';
import { beats as b, color, font, phone as P, words } from '../config';
import { Words } from '../components/Type';
import { clamp, easeInOut, keys, ramp, spring } from '../motion';
import type { Layout } from '../World';

/**
 * The night's updates on your phone. The notifications are the ones Vigil really sends (Engine.cs:
 * "<name> finished" with the summary, "<name> needs you" with the reason, and the heat note), sent
 * only while you are away. Then the real Vigil phone app, captured from phone/public in demo mode.
 */
const notes = [
  { title: 'api-server finished', body: 'Added rate limiting and 12 tests, all passing.', time: '2:58 AM' },
  { title: 'Running warm: easing off', body: "Windows is slowing the CPU to cool down, so Vigil capped its speed. If it's still throttling in a few minutes, the PC hibernates.", time: '2:10 AM' },
  { title: 'data-pipeline finished', body: 'Fixed crash on empty CSV rows.', time: '1:47 AM' },
  { title: 'docs-site finished', body: 'Rewrote the setup guide.', time: '12:36 AM' },
];
const ping = { title: 'landing-page needs you', body: 'Waiting for your approval to run npm install.', time: 'now' };

const Note: React.FC<{ n: { title: string; body: string; time: string }; w: number; glow?: number }> = ({ n, w, glow = 0 }) => {
  const k = (w / 420) * 1.06;
  return (
    <div style={{ width: w, boxSizing: 'border-box', padding: `${14 * k}px ${16 * k}px`, borderRadius: 24 * k, background: 'rgba(52,54,64,0.78)', boxShadow: glow ? `0 0 0 ${2 * k}px rgba(255,179,64,${0.8 * glow}), 0 10px 30px rgba(0,0,0,0.35)` : '0 10px 30px rgba(0,0,0,0.25)', fontFamily: font.text, color: color.text1 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 * k, marginBottom: 6 * k }}>
        <Img src={staticFile('vigil-icon.png')} style={{ width: 30 * k, height: 30 * k, borderRadius: 7 * k }} />
        <div style={{ fontSize: 15 * k, letterSpacing: '0.06em', color: color.text2, fontWeight: 600, flex: 1 }}>VIGIL</div>
        <div style={{ fontSize: 15 * k, color: color.text2 }}>{n.time}</div>
      </div>
      <div style={{ fontSize: 20 * k, fontWeight: 650, lineHeight: 1.2 }}>{n.title}</div>
      <div style={{ fontSize: 18 * k, color: '#D6D6DC', lineHeight: 1.25, marginTop: 2 * k, display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>{n.body}</div>
    </div>
  );
};

export const PhoneScene: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  const t0 = b.phoneIn;
  const local = t - t0;
  if (local < -0.05 || local > P.dur + 0.05) return null;
  const rise = spring(t, t0 + P.rise, 150, 22);
  const away = ramp(t, t0 + P.leave, 0.45, easeInOut);
  const dim = clamp(rise * 1.2) * (1 - away);

  // The phone: tall frame, generous size so the notifications read on a phone too.
  const ph = L.landscape ? L.H * 0.95 : 1240;
  const pw = ph * 0.47;
  const px = L.landscape ? L.W * 0.66 - pw / 2 : L.W / 2 - pw / 2;
  const py0 = L.landscape ? L.H * 0.05 : 340;
  const py = py0 + (1 - rise) * (L.H - py0 + 40) + away * (L.H - py0 + 60);
  // A short buzz when the new notification lands.
  const bz = local > P.ping && local < P.ping + 0.45 ? Math.sin((local - P.ping) * 70) * 1.6 * (1 - (local - P.ping) / 0.45) : 0;
  const bezel = pw * 0.035;
  const sw = pw - bezel * 2;
  const sh = ph - bezel * 2;

  // Lock screen notifications: the new one slides in on top and pushes the rest down.
  const inP = spring(t, t0 + P.ping, 230, 22);
  const noteW = sw * 0.92;
  const gap = sw * 0.03;
  const noteH = noteW * 0.305;
  const unlock = ramp(t, t0 + P.unlock, 0.5, easeInOut);
  // In the app, a slow push-in on what needs you, so it reads.
  const appZoom = keys(t, [[t0 + P.unlock + 0.4, 1], [t0 + P.unlock + 1.6, 1.42]], easeInOut);

  return (
    <AbsoluteFill style={{ pointerEvents: 'none' }}>
      <AbsoluteFill style={{ background: '#03050C', opacity: 0.62 * dim }} />
      <div style={{ position: 'absolute', left: px, top: py, width: pw, height: ph, borderRadius: pw * 0.16, background: '#0B0B0E', boxShadow: '0 50px 140px rgba(0,0,0,0.6), inset 0 0 0 2px rgba(255,255,255,0.10)', transform: `rotate(${bz}deg)` }}>
        <div style={{ position: 'absolute', left: bezel, top: bezel, width: sw, height: sh, borderRadius: pw * 0.13, overflow: 'hidden', background: '#000' }}>
          {/* the app, under the lock screen */}
          <div style={{ position: 'absolute', inset: 0, transform: `scale(${appZoom})`, transformOrigin: '50% 34%' }}>
            <Img src={staticFile('phone-app.png')} style={{ width: sw, display: 'block' }} />
          </div>
          {/* the lock screen slides up to unlock */}
          <div style={{ position: 'absolute', inset: 0, transform: `translateY(${-unlock * sh}px)`, background: `linear-gradient(180deg, ${color.night[0]} 0%, ${color.night[1]} 55%, ${color.night[2]} 100%)` }}>
            <div style={{ position: 'absolute', top: sh * 0.07, width: '100%', textAlign: 'center', fontFamily: font.display, color: color.text1 }}>
              <div style={{ fontSize: sw * 0.055, fontWeight: 600, opacity: 0.85 }}>Tuesday, October 20</div>
              <div style={{ fontSize: sw * 0.27, fontWeight: 600, lineHeight: 1.05, letterSpacing: '-0.02em' }}>3:41</div>
            </div>
            <div style={{ position: 'absolute', left: (sw - noteW) / 2, top: sh * 0.3 }}>
              <div style={{ position: 'absolute', top: -noteH * 0.6 + inP * noteH * 0.6, opacity: clamp(inP * 1.6), transform: `scale(${0.94 + 0.06 * inP})`, transformOrigin: '50% 0%' }}>
                <Note n={ping} w={noteW} glow={Math.sin(clamp((local - P.ping) / 1.2) * Math.PI)} />
              </div>
              {notes.map((n, i) => (
                <div key={i} style={{ position: 'absolute', top: (i + inP) * (noteH + gap) }}>
                  <Note n={n} w={noteW} />
                </div>
              ))}
            </div>
            <div style={{ position: 'absolute', bottom: sh * 0.02, left: '35%', width: '30%', height: 6, borderRadius: 3, background: 'rgba(255,255,255,0.7)' }} />
          </div>
        </div>
      </div>
      <div style={L.landscape ? { position: 'absolute', left: L.captionX, width: L.W * 0.36, top: L.H * 0.38 } : { position: 'absolute', left: 60, right: 60, top: 150 }}>
        <Words text={words.phonePing} t={t} start={t0 + P.firstCaption} end={t0 + P.unlock - 0.1} size={68} maxWidth={900} />
      </div>
      <div style={L.landscape ? { position: 'absolute', left: L.captionX, width: L.W * 0.36, top: L.H * 0.38 } : { position: 'absolute', left: 60, right: 60, top: 150 }}>
        <Words text={words.phoneAnswer} t={t} start={t0 + P.secondCaption} end={t0 + P.leave} size={68} maxWidth={900} />
      </div>
    </AbsoluteFill>
  );
};

