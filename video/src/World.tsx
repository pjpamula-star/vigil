import React from 'react';
import { AbsoluteFill, useCurrentFrame, useVideoConfig } from 'remotion';
import { RecapVideo } from './scenes/Payoff';
import { CameraMotionBlur } from '@remotion/motion-blur';
import { beats as b, color, font, scenes } from './config';
import { Eye } from './components/Eye';
import { Laptop } from './components/Laptop';
import { SessionCard, Terminal } from './components/Screen';
import { Clock } from './components/Type';
import { Vhs } from './components/Finish';
import { clamp, clockText, keys, mix, noise, ramp, easeInOut, easeOut, spring, wobble } from './motion';
import * as k from './tracks';

/**
 * Layout for one frame size. The LinkedIn master is 1080 x 1350; the X cut is 1920 x 1080.
 * Positions are where things sit in the two camera set-ups: "open" (3/4 view, lid up) and "top"
 * (looking down through the shut lid).
 */
export type Layout = {
  W: number; H: number; landscape: boolean;
  laptopW: number;
  topScale: number; // how much bigger the laptop is when we look down through the lid
  arrive: { x: number; y: number }; // where the eye first opens
  open: { x: number; y: number }; top: { x: number; y: number };
  guardOpen: { x: number; y: number }; guardTop: { x: number; y: number };
  clockX: number; clockY: number; captionY: number; captionX: number; captionW: number;
};

export const layoutFor = (W: number, H: number): Layout =>
  W > H
    ? {
        // Wide frame: the words in a left column, the laptop and the eye on the right.
        W, H, landscape: true, laptopW: 860, topScale: 1.05, arrive: { x: W * 0.62, y: H * 0.17 },
        open: { x: W * 0.63, y: H * 0.74 }, top: { x: W * 0.665, y: H * 0.6 },
        guardOpen: { x: W * 0.9, y: H * 0.33 }, guardTop: { x: W * 0.91, y: H * 0.27 },
        clockX: W - 80, clockY: 56, captionY: H * 0.33, captionX: 110, captionW: W * 0.34,
      }
    : {
        W, H, landscape: false, laptopW: 720, topScale: 1.27, arrive: { x: W * 0.5, y: H * 0.225 },
        open: { x: W * 0.5 - 70, y: H * 0.67 }, top: { x: W * 0.5 + 50, y: H * 0.6 },
        guardOpen: { x: W - 112, y: H * 0.4 }, guardTop: { x: W * 0.5 + 330, y: H * 0.335 },
        clockX: W - 64, clockY: 64, captionY: 170, captionX: 60, captionW: W - 120,
      };

/** 0 = the open set-up, 1 = the top set-up. Follows the camera tilt. */
const topness = (t: number) => clamp((k.tilt(t) - 9) / 55);

const terminalFor = (t: number, w: number, h: number, frozen: boolean) => {
  const d = k.drain(t);
  const g = k.glitch(t);
  const term = <Terminal t={frozen ? b.slam : t} lines={k.terminalLines(t)} percent={k.percent(t)} w={w} h={h} />;
  return (
    <div style={{ width: w, height: h, position: 'relative', filter: `grayscale(${d}) brightness(${1 - d * 0.25})` }}>
      {g
        ? // Glitch: the frozen frame tears into bands and splits its color for a moment.
          [0, 1, 2, 3].map((i) => (
            <div key={i} style={{ position: 'absolute', inset: 0, clipPath: `inset(${i * 25}% 0 ${100 - (i + 1) * 25}% 0)`, transform: `translateX(${noise(i + 5, Math.floor(t * 30)) * 22}px)`, filter: i % 2 ? 'drop-shadow(4px 0 rgba(255,40,80,0.7))' : 'drop-shadow(-4px 0 rgba(0,220,255,0.7))' }}>
              {term}
            </div>
          ))
        : term}
    </div>
  );
};

const Cards: React.FC<{ t: number; w: number }> = ({ t, w }) => {
  const gap = w * 0.03;
  const cw = (w - gap * 3) / 2;
  return (
    <div style={{ position: 'absolute', inset: 0, padding: gap, display: 'grid', gridTemplateColumns: '1fr 1fr', alignContent: 'center', gap }}>
      {[0, 1, 2, 3].map((i) => {
        const c = k.card(i, t);
        const appear = spring(t, b.cardsIn + i * 0.08, 200, 24);
        return (
          <div key={i} style={{ opacity: clamp(appear * 1.5), transform: `translateY(${(1 - appear) * 20}px)` }}>
            <SessionCard i={i} state={c.state} t={t} at={c.at} time={c.time} w={cw} pulse={i === 0 ? k.needsPulse(t) : 0} />
          </div>
        );
      })}
    </div>
  );
};

/** The calm README cut: no twinkle (a GIF compresses still pixels far better). */
export const CalmMode = React.createContext(false);

/** The app's own night sky has stars (WelcomeBackWindow). They fade as it gets light. */
const Stars: React.FC<{ t: number; L: Layout; dawn: number }> = ({ t, L, dawn }) => {
  const calm = React.useContext(CalmMode);
  const n = L.landscape ? 60 : 46;
  const out = [];
  for (let i = 0; i < n; i++) {
    const x = (noise(i, 1) * 0.5 + 0.5) * L.W;
    const y = (noise(i, 2) * 0.5 + 0.5) * L.H * 0.62;
    const big = noise(i, 3) > 0.7;
    const tw = calm ? 0.8 : 0.65 + 0.35 * Math.sin(t * (1.3 + (i % 5) * 0.4) + i);
    const a = (0.25 + (noise(i, 4) * 0.5 + 0.5) * 0.55) * tw * (1 - dawn);
    out.push(<div key={i} style={{ position: 'absolute', left: x, top: y, width: big ? 3 : 2, height: big ? 3 : 2, borderRadius: 2, background: '#fff', opacity: a }} />);
  }
  return <>{out}</>;
};

/** Where everything sits at time t (shared by the sharp background and the blurred foreground). */
function stage(t: number, L: Layout) {
  const lw = k.laptopWidth(t, L.laptopW, L.topScale);
  const tp = topness(t);
  // Once the eye is standing guard, the open laptop steps aside to make room for it.
  const aside = (L.landscape ? 0 : 1) * ramp(t, b.settle - 0.2, 0.7, easeInOut) * (1 - tp) * (1 - ramp(t, b.openMorning, 0.4));
  const lx = L.open.x + (L.top.x - L.open.x) * tp - aside * 70;
  const ly = L.open.y + (L.top.y - L.open.y) * tp;
  return { lw, tp, lx, ly };
}

/**
 * Everything that moves fast: the laptop, the heat, the ripple, the "z z z". It has a transparent
 * background, so motion blur only ever stacks these, never the sky (stacking a smooth gradient 16 times
 * in 8 bits makes visible bands).
 */
export const Foreground: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  if (t >= scenes.trust.start) return null;
  const { lw, lx, ly } = stage(t, L);
  const sh = k.shake(t);
  const screenW = lw * 0.964;
  const screenH = lw * 0.64 * 0.944;
  const heat = k.heat(t);
  const rip = k.ripple(t);
  const fadeOut = ramp(t, b.flatIn, 0.35); // the world hands over to the real recording
  const sleeping = t > b.sleep + k.batteryShift + 0.6 && t < b.openMorning;
  // Opening close-up: zoom in on the screen and keep it near the middle of the frame.
  const z = k.hookZoom(t);
  const zf = t < b.morningOpen ? (z - 1) / 0.4 : t < b.openMorning ? ((z - 1) / 0.4) * 0.6 : clamp((z - 1) / 0.4);
  // The screen's centre (measured from a render in the 3/4 view; it moves to the middle as the camera turns to face it).
  const focusX = lx + lw * 0.19 * clamp(-k.yaw(t) / 24);
  const focusY = ly - lw * 0.39;
  // Where the screen ends up when the camera pushes in: the middle of the tall frame; on the wide frame, the
  // right side, clear of the word column (and where the flat recap frame sits).
  const camX = ((L.landscape ? L.W * 0.705 : L.W / 2) - focusX) * zf;
  const camY = (L.H * 0.5 - focusY) * zf;
  return (
    <AbsoluteFill style={{ opacity: 1 - fadeOut, transform: `translate(${sh.x + camX}px, ${sh.y + camY}px)` }}>
      {/* the laptop */}
      <div style={{ position: 'absolute', left: lx, top: ly, perspective: 2100, perspectiveOrigin: '50% 30%', transform: `scale(${z})`, transformOrigin: `${focusX - lx}px ${focusY - ly}px` }}>
        <Laptop
          width={lw}
          lid={k.lid(t)}
          tilt={k.tilt(t)}
          yaw={k.yaw(t)}
          squash={k.squash(t)}
          screen={t >= b.openMorning ? <DesktopScreen t={t} w={screenW} h={screenH} /> : terminalFor(t, screenW, screenH, t > b.slam && t < scenes.enter.start)}
          screenGlow={k.screenGlow(t)}
          xray={k.xrayShowsCards(t) ? <Cards t={t} w={lw * 0.94} /> : terminalFor(t, lw * 0.94, lw * 0.64 * 0.9, true)}
          xrayAmount={k.xray(t)}
          led={sleeping ? 0.5 + 0.5 * Math.sin((t - b.sleep) * 3) : 0}
          lightLine={k.lightLine(t)}
        />
      </div>

      {/* heat: a thermometer by the laptop, then a cool ripple when Vigil caps the CPU */}
      {heat > 0.01 ? <Thermometer x={Math.min(lx + lw * 0.56, L.W - 90)} y={ly - 120} level={heat} t={t} /> : null}
      {rip >= 0 ? (
        <div style={{ position: 'absolute', left: lx, top: ly - 40, width: 0, height: 0 }}>
          {[0, 0.2, 0.4].map((d) => {
            const p = clamp((rip - d) / (1 - d));
            return p > 0 && p < 1 ? <div key={d} style={{ position: 'absolute', left: -lw * 0.85 * p, top: -lw * 0.42 * p, width: lw * 1.7 * p, height: lw * 0.84 * p, borderRadius: '50%', border: `${8 * (1 - p) + 1.5}px solid ${color.cool}`, opacity: (1 - p) * 0.9 }} /> : null;
          })}
        </div>
      ) : null}
      {t > b.coolDown + 0.25 && t < b.apiDone + k.batteryShift ? (
        <Chip x={Math.max(40, lx - lw * 0.5)} y={ly - lw * 0.4 - 30} t={t} at={b.coolDown + 0.25} text="CPU capped at 60%" ink={color.cool} />
      ) : null}

      {/* "z z z" while Windows has it suspended */}
      {t > b.zzz && t < b.morningOpen ? <Zzz t={t} x={lx + lw * 0.18} y={ly - lw * 0.36} /> : null}
    </AbsoluteFill>
  );
};

/** The foreground at the current frame, for motion blur (which renders it between frames). */
const ForegroundNow: React.FC<{ L: Layout }> = ({ L }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  return <Foreground t={frame / fps} L={L} />;
};

/** The world: sky, stars and light drawn once and sharp; the moving foreground blurred when it is fast. */
export const World: React.FC<{ t: number; L: Layout; blur?: boolean }> = ({ t, L, blur = false }) => {
  if (t >= scenes.trust.start) return null;
  const dawn = k.skyDawn(t);
  const sky = [0, 1, 2].map((i) => mix(color.night[i], color.dawn[i], dawn));
  const { lw, tp, lx, ly } = stage(t, L);
  const minutes = k.clockMinutes(t);
  const fadeOut = ramp(t, b.recordingIn - 0.3, 0.35);
  const pulse = k.clockPulse(t);
  return (
    <AbsoluteFill style={{ background: `linear-gradient(180deg, ${sky[0]} 0%, ${sky[1]} 55%, ${sky[2]} 100%)` }}>
      <Stars t={t} L={L} dawn={dawn} />
      {/* the screen's glow on the room while it is on and open */}
      <AbsoluteFill style={{ background: `radial-gradient(40% 30% at ${(lx / L.W) * 100}% ${((ly - lw * 0.3) / L.H) * 100}%, rgba(90,140,255,${0.22 * (1 - k.lid(t)) * k.screenGlow(t) * (1 - tp)}), transparent)` }} />
      {blur ? (
        <CameraMotionBlur shutterAngle={180} samples={16}>
          <ForegroundNow L={L} />
        </CameraMotionBlur>
      ) : (
        <Foreground t={t} L={L} />
      )}

      {/* the clock the night runs on */}
      <div style={{ position: 'absolute', right: L.W - L.clockX, top: L.clockY, opacity: 1 - fadeOut, transform: `scale(${1 + pulse * 0.08})`, transformOrigin: '100% 0%' }}>
        <Clock text={clockText(minutes)} size={58} blur={Math.min(6, k.clockSpeed(t) / 120)} ink={mix(color.text2, '#FFFFFF', 0.55 + pulse * 0.45)} />
      </div>

      {/* morning: a soft bloom of light from the screen as the lid opens */}
      {t > b.openMorning && t < b.recordingIn + 0.4 ? (
        <AbsoluteFill style={{ pointerEvents: 'none', background: `radial-gradient(45% 32% at ${(lx / L.W) * 100}% ${((ly - lw * 0.3) / L.H) * 100}%, rgba(255,236,214,${0.75 * Math.sin(clamp((t - b.openMorning) / 0.9) * Math.PI)}), transparent)` }} />
      ) : null}
      <AbsoluteFill style={{ background: '#000', opacity: k.dark(t), pointerEvents: 'none' }} />
      <Vhs amount={k.vhs(t)} t={t} />
    </AbsoluteFill>
  );
};

/** The eye: arrives in the middle of the dark, then stands guard by the laptop, then hands over to the app. */
export const Watcher: React.FC<{ t: number; L: Layout }> = ({ t, L }) => {
  const e = k.eye(t);
  if (e.appear <= 0) return null;
  const tp = topness(t);
  const guard = { x: L.guardOpen.x + (L.guardTop.x - L.guardOpen.x) * tp, y: L.guardOpen.y + (L.guardTop.y - L.guardOpen.y) * tp };
  // Morning: the eye glides into the spot where the app's own eye opens in the recording (the match cut),
  // stays over it while that eye opens, then lets the real one take over.
  // Morning: the eye flies into the laptop's screen, onto the eye of the Welcome back window that opens there.
  const hand = ramp(t, b.openMorning + 0.1, 0.45, easeInOut);
  const st = stage(b.recordingIn, L);
  const rec = { x: st.lx + st.lw * (0.079 - 0.131), y: st.ly - st.lw * (0.39 + 0.23), size: st.lw * 0.03 };
  const settleSize = keys(t, [[b.eyeOpen, 240], [b.settle, 240], [b.settle + 0.6, 136]], easeInOut);
  const size = settleSize * (1 - hand) + rec.size * hand;
  const x = keys(t, [[b.settle, L.arrive.x], [b.settle + 0.6, guard.x]], easeInOut) * (1 - hand) + rec.x * hand;
  const y = keys(t, [[b.settle, L.arrive.y], [b.settle + 0.6, guard.y]], easeInOut) * (1 - hand) + rec.y * hand;
  const gone = ramp(t, b.recordingIn + 0.45, 0.25);
  if (gone >= 1) return null;
  const lean = e.needs * (L.landscape ? -30 : -26) * (1 - hand);
  const bob = wobble(21, t, 0.6) * 4 * (1 - hand);
  const spark = t > b.spark && t < b.eyeOpen + 0.35 ? 1 - ramp(t, b.eyeOpen + 0.05, 0.3) : 0;
  const light = e.appear * (1 - hand);
  const ink = mix(mix(color.text1, color.hotA, e.hot), color.cool, e.cool * 0.8);
  const arc = k.waitArc(t);
  const R = size * 0.62;
  return (
    <div style={{ position: 'absolute', left: x + lean - size / 2, top: y + bob - size / 2, opacity: 1 - gone }}>
      {/* the one warm light in the night: Vigil is watching */}
      <div style={{ position: 'absolute', left: -size * 0.7, top: -size * 0.6, width: size * 2.4, height: size * 2.2, borderRadius: '50%', background: `radial-gradient(closest-side, rgba(255,194,122,${0.17 * light}), transparent)` }} />
      {spark > 0 ? (
        <div style={{ position: 'absolute', left: size / 2 - 7, top: size / 2 - 7, width: 14, height: 14, borderRadius: 7, background: color.flame, transform: `scale(${1 + spring(t, b.spark, 300, 12) * 1.8})`, opacity: spark, boxShadow: `0 0 30px ${color.flame}` }} />
      ) : null}
      {arc >= 0 ? (
        // The hour it waits for you, filling around the eye.
        <svg width={R * 2 + 12} height={R * 2 + 12} style={{ position: 'absolute', left: size / 2 - R - 6, top: size / 2 - R - 6, opacity: 1 - ramp(t, b.sleep + k.batteryShift + 0.3, 0.5) }}>
          <circle cx={R + 6} cy={R + 6} r={R} fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth={5} />
          <circle cx={R + 6} cy={R + 6} r={R} fill="none" stroke={color.amberA} strokeWidth={5} strokeLinecap="round" strokeDasharray={`${2 * Math.PI * R * arc} ${2 * Math.PI * R}`} transform={`rotate(-90 ${R + 6} ${R + 6})`} />
        </svg>
      ) : null}
      <Eye
        size={size}
        open={e.open}
        look={e.look * (1 - hand)}
        lookY={e.lookY * (1 - hand)}
        ink={ink}
        squint={e.hot}
        dot={e.needs > 0.05 ? color.amberA : e.hot > 0.3 ? color.hotA : null}
        dotScale={e.needs > 0.05 ? 0.85 + 0.25 * Math.sin(t * 5) : 1}
      />
      {t > b.name && t < b.closeGently + 0.4 ? (
        <div style={{ position: 'absolute', left: size / 2 - 120, width: 240, textAlign: 'center', top: size * 0.9, fontFamily: font.display, fontWeight: 650, fontSize: 60, color: color.text1, opacity: ramp(t, b.name, 0.3) * (1 - ramp(t, b.closeGently, 0.4)), transform: `translateY(${(1 - ramp(t, b.name, 0.4, easeOut)) * 12}px)` }}>
          Vigil
        </div>
      ) : null}
    </div>
  );
};

const Thermometer: React.FC<{ x: number; y: number; level: number; t: number }> = ({ x, y, level, t }) => {
  const c = mix(color.amberA, color.hotA, level);
  const fill = 0.18 + level * 0.77;
  return (
    <div style={{ position: 'absolute', left: x, top: y, opacity: clamp(level * 4) }}>
      <svg width={74} height={190} viewBox="0 0 64 170">
        <rect x="20" y="6" width="24" height="124" rx="12" fill="rgba(255,255,255,0.10)" stroke={color.text1} strokeWidth="4" />
        <rect x="26" y={12 + 112 * (1 - fill)} width="12" height={112 * fill + 10} rx="6" fill={c} />
        <circle cx="32" cy="142" r="20" fill={c} stroke={color.text1} strokeWidth="4" />
        {level > 0.7 ? [0, 1, 2].map((i) => <path key={i} d={`M${52 + i * 6} ${40 + i * 16} q 6 -6 0 -12 q -6 -6 0 -12`} stroke={c} strokeWidth="3.5" fill="none" opacity={0.5 + 0.5 * Math.sin(t * 9 + i)} />) : null}
      </svg>
    </div>
  );
};

const Chip: React.FC<{ x: number; y: number; t: number; at: number; text: string; ink: string }> = ({ x, y, t, at, text, ink }) => {
  const p = spring(t, at, 260, 20);
  return (
    <div style={{ position: 'absolute', left: x - 10, top: y, transform: `scale(${0.6 + 0.4 * p})`, transformOrigin: '0% 50%', opacity: clamp(p * 2), padding: '12px 22px', borderRadius: 999, background: 'rgba(28,28,30,0.92)', border: `2px solid ${ink}`, color: ink, fontFamily: font.text, fontWeight: 600, fontSize: 32, whiteSpace: 'nowrap' }}>
      {text}
    </div>
  );
};

/**
 * The laptop's screen in the morning: the desktop, and the real Welcome back window (the recording)
 * opening on it, at the size it really has on a 16:10 screen (834 of 1024 px tall, centred above the taskbar).
 */
const DesktopScreen: React.FC<{ t: number; w: number; h: number }> = ({ t, w, h }) => {
  const winH = h * 0.814;
  const winW = (winH * 674) / 1042;
  const task = h * 0.047;
  const show = t >= b.recordingIn - 0.02;
  return (
    <div style={{ width: w, height: h, position: 'relative', overflow: 'hidden', background: `linear-gradient(180deg, ${color.dawn[0]} 0%, ${color.dawn[1]} 60%, ${color.dawn[2]} 100%)` }}>
      {/* soft light as the screen wakes */}
      <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(70% 60% at 50% 45%, rgba(255,240,224,0.35), transparent)', opacity: 1 - ramp(t, b.openMorning + 0.3, 0.8) * 0.6 }} />
      {show ? (
        <div style={{ position: 'absolute', left: (w - winW) / 2, top: h * 0.069, width: winW, height: winH, borderRadius: 6, overflow: 'hidden', boxShadow: '0 12px 40px rgba(0,0,0,0.45), 0 0 0 1px rgba(255,255,255,0.12)' }}>
          <RecapVideo rt={t - b.recordingIn} />
        </div>
      ) : null}
      {/* taskbar, with Vigil's eye by the clock */}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: task, background: 'rgba(28,28,32,0.86)', display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: task * 0.35, paddingRight: task * 0.5, boxSizing: 'border-box' }}>
        <div style={{ position: 'absolute', left: '50%', transform: 'translateX(-50%)', display: 'flex', gap: task * 0.25 }}>
          {[0, 1, 2, 3, 4].map((i) => <div key={i} style={{ width: task * 0.55, height: task * 0.55, borderRadius: task * 0.12, background: 'rgba(255,255,255,0.22)' }} />)}
        </div>
        <Eye size={task * 0.7} ink={color.text1} />
        <div style={{ fontFamily: font.text, fontSize: task * 0.38, color: color.text1 }}>7:14 AM</div>
      </div>
    </div>
  );
};

const Zzz: React.FC<{ t: number; x: number; y: number }> = ({ t, x, y }) => (
  <div style={{ position: 'absolute', left: x, top: y }}>
    {[0, 1, 2].map((i) => {
      const d = t - b.zzz - i * 0.28;
      if (d < 0) return null;
      const p = (d % 1.2) / 1.2;
      return (
        <div key={i} style={{ position: 'absolute', left: i * 40 + Math.sin(d * 3) * 8, top: -p * 160, fontFamily: font.display, fontWeight: 700, fontSize: 76 + i * 22, color: color.text1, opacity: Math.min(1, Math.sin(p * Math.PI) * 1.3) }}>
          z
        </div>
      );
    })}
  </div>
);
