// Every moving part as a function of time, built from config beats. The scenes only read these.
import { beats as b, clockKeys, scenes, showBatteryBeat, slamDur } from './config';
import { clamp, easeIn, easeInOut, easeOut, keys, ramp, spring, wobble } from './motion';
import type { CardState } from './components/Screen';

// The battery beat, when on, pushes everything after the heat beat later.
export const batteryShift = showBatteryBeat ? 1.9 : 0;
const enterStart = scenes.enter.start;

/** Lid: 0 open, 1 shut. */
export function lid(t: number) {
  // Slam: accelerate shut, tiny bounce.
  if (t < b.slam) return 0;
  if (t < b.slam + slamDur) return easeIn(clamp((t - b.slam) / slamDur));
  const bounce = t < b.slam + slamDur + 0.22 ? Math.sin(((t - b.slam - slamDur) / 0.22) * Math.PI) * 0.035 : 0;
  if (t < b.morningOpen) return 1 - bounce;
  // Morning: the lid opens on the frozen bar.
  if (t < b.rewind) return 1 - ramp(t, b.morningOpen, 0.38, easeInOut);
  // Rewind: the morning open plays backwards, then the slam plays backwards: it snaps back open.
  if (t < scenes.rewind.end) {
    const close = ramp(t, b.rewind + 0.1, 0.2, easeInOut);
    const reopen = ramp(t, b.rewind + 0.95, 0.18, easeOut);
    return close * (1 - reopen);
  }
  if (t < b.closeGently) return 0;
  // Night: closes calmly this time.
  if (t < b.openMorning) return ramp(t, b.closeGently, 0.5, easeInOut);
  return 1 - ramp(t, b.openMorning, 0.45, easeInOut);
}

/** Squash and stretch on impact (-1..1). */
export function squash(t: number) {
  const hit = b.slam + slamDur;
  if (t < hit || t > hit + 0.3) return 0;
  const x = (t - hit) / 0.3;
  return Math.sin(x * Math.PI * 2) * (1 - x);
}

/** Camera shake in px right after the slam. */
export function shake(t: number) {
  if (t < b.shake || t > b.shake + 0.28) return { x: 0, y: 0 };
  const k = 1 - (t - b.shake) / 0.28;
  return { x: wobble(3, t, 38) * 16 * k, y: wobble(9, t, 41) * 12 * k };
}

/** Camera elevation: 3/4 view while the lid is open, looking down at the shut lid for the x-ray. */
export function tilt(t: number) {
  return keys(t, [
    [0, 9], [b.tiltUp, 9], [b.tiltUp + 0.5, 64], [b.morningOpen - 0.1, 64], [b.morningOpen + 0.3, 9],
    [b.closeGently + 0.15, 9], [b.cardsIn, 64], [b.openMorning, 64], [b.openMorning + 0.4, 9],
    [b.zoomScreen, 9], [b.zoomScreen + 0.8, 0],
  ], easeInOut);
}
export function yaw(t: number) {
  return keys(t, [
    [0, -24], [b.tiltUp, -24], [b.tiltUp + 0.5, 0], [b.morningOpen - 0.1, 0], [b.morningOpen + 0.3, -24],
    [b.closeGently + 0.15, -20], [b.cardsIn, 0], [b.openMorning, 0], [b.openMorning + 0.4, -10],
    [b.zoomScreen, -10], [b.zoomScreen + 0.8, 0],
  ], easeInOut);
}
/** Laptop size: bigger when we look down so the cards read. */
export function laptopWidth(t: number, base: number, top = 1.27) {
  const mid = 1 + (top - 1) * 0.75;
  return base * keys(t, [
    [0, 1], [b.tiltUp, 1], [b.tiltUp + 0.5, mid], [b.morningOpen - 0.1, mid], [b.morningOpen + 0.3, 1],
    [enterStart, 1], [b.closeGently + 0.15, 1], [b.cardsIn, top], [b.openMorning, top], [b.openMorning + 0.4, 1.0],
  ], easeInOut);
}

/** Screen brightness: the light squeezes out on the slam, and the screen is off while asleep. */
export function screenGlow(t: number) {
  if (t < b.slam) return 1;
  if (t < b.morningOpen) return 1 - ramp(t, b.slam + 0.05, 0.2);
  return 1;
}

/** Color drains when Windows suspends the terminal; comes back in the rewind. */
export function drain(t: number) {
  if (t < b.drain) return 0;
  if (t < b.rewind) return ramp(t, b.drain, 0.45);
  return 1 - ramp(t, b.rewind + 0.4, 0.5);
}
export function glitch(t: number) {
  return t > b.glitch && t < b.glitch + 0.22 ? 1 : 0;
}
/** The test run: climbing to 41% before the slam, frozen after; after the rewind, Claude keeps going. */
export function percent(t: number) {
  if (t < b.slam) return 37 + 4 * ramp(t, 0, b.slam);
  if (t < enterStart) return 41;
  return Math.min(78, 41 + (t - enterStart) * 9);
}
export function terminalLines(t: number) {
  return 3 + clamp(t / 0.45) * 3;
}

/** X-ray through the shut lid. */
export function xray(t: number) {
  if (t >= b.xray && t < b.morningOpen) return ramp(t, b.xray, 0.3) * (1 - ramp(t, b.morningOpen - 0.25, 0.2));
  if (t >= b.cardsIn && t < b.sleep + 0.6) return ramp(t, b.cardsIn, 0.35) * (1 - ramp(t, b.sleep + 0.1, 0.5));
  return 0;
}
/** Which picture the x-ray shows: the frozen terminal (problem) or the session cards (night). */
export const xrayShowsCards = (t: number) => t >= b.closeGently;

/** The four cards' states. i follows config.sessions. */
export function card(i: number, t: number): { state: CardState; at: number; time?: string } {
  const s = batteryShift;
  if (i === 3 && t >= b.docsDone) return { state: 'done', at: b.docsDone, time: '12:36 AM' };
  if (i === 2 && t >= b.pipelineDone) return { state: 'done', at: b.pipelineDone, time: '1:47 AM' };
  if (i === 1 && t >= b.apiDone + s) return { state: 'done', at: b.apiDone + s, time: '2:58 AM' };
  if (i === 0 && t >= b.needsYou + s) return { state: 'needs', at: b.needsYou + s };
  return { state: 'working', at: 0 };
}
/** The amber pulse on the session that needs you. */
export function needsPulse(t: number) {
  const t0 = b.needsYou + batteryShift;
  if (t < t0) return 0;
  return 0.5 + 0.5 * Math.sin((t - t0) * Math.PI * 1.6);
}

/** Heat: the thermometer rises, then Vigil caps the CPU and it cools. */
export function heat(t: number) {
  return keys(t, [[b.heatRise, 0], [b.heatRise + 0.7, 1], [b.coolDown, 1], [b.coolDown + 0.8, 0.22], [b.apiDone, 0.22], [b.apiDone + 0.6, 0]], easeInOut);
}
/** The cool ripple's progress (0..1) after the cap goes on. */
export function ripple(t: number) {
  return t >= b.coolDown && t < b.coolDown + 1.1 ? (t - b.coolDown) / 1.1 : -1;
}

/** Darkness before Vigil arrives. */
export function dark(t: number) {
  if (t < b.dark) return 0;
  if (t < b.eyeOpen) return ramp(t, b.dark, 0.25) * 0.86;
  return 0.86 * (1 - ramp(t, b.eyeOpen, 0.6));
}

/** VHS amount during the rewind. */
export function vhs(t: number) {
  if (t < b.rewind || t > scenes.rewind.end) return 0;
  return ramp(t, b.rewind, 0.08) * (1 - ramp(t, scenes.rewind.end - 0.25, 0.25));
}

/** The clock, in minutes after 11:52 PM. */
export function clockMinutes(t: number) {
  const k = clockKeys.map(([s, m]) => [s >= b.apiDone ? s + batteryShift : s, m] as [number, number]);
  return keys(t, k, easeOut);
}
/** How fast the clock is moving (for its motion blur). */
export function clockSpeed(t: number) {
  return Math.abs(clockMinutes(t + 1 / 60) - clockMinutes(t - 1 / 60)) * 30;
}

/** Sky follows the clock: night until about 5 AM, dawn by 7:14 AM. */
export function skyDawn(t: number) {
  const m = clockMinutes(t);
  return clamp((m - 300) / 142);
}

/** The eye: where it is, how open, where it looks, what it feels. */
export function eye(t: number) {
  const s = batteryShift;
  const appear = ramp(t, b.eyeOpen, 0.45);
  const blink = (at: number) => (t > at && t < at + 0.24 ? Math.sin(((t - at) / 0.24) * Math.PI) : 0);
  let open = appear * (1 - Math.max(blink(b.settle + 0.9), blink(b.docsDone + 0.4), blink(b.coolDown + 0.9), blink(b.apiDone + s + 0.5)));
  // Sleep: closes slowly when the last hour of waiting runs out.
  open *= 1 - ramp(t, b.sleep + s, 0.8, easeInOut);
  // Morning: opens again with the lid.
  if (t >= b.openMorning) open = ramp(t, b.openMorning + 0.1, 0.4);
  const hot = clamp(heat(t) * 1.25 - 0.25);
  const cool = t >= b.coolDown ? 1 - ramp(t, b.coolDown + 1.0, 0.8) : 0;
  const needs = t >= b.needsYou + s && t < b.sleep + s + 0.8 ? ramp(t, b.needsYou + s, 0.4) : 0;
  // Looks at the laptop, glances at each card as it finishes, then at the one that needs you.
  const look = keys(t, [
    [b.settle, 0], [b.settle + 0.3, -0.5], [b.docsDone - 0.1, -0.5], [b.docsDone + 0.1, 0.2], [b.pipelineDone, -0.2],
    [b.needsYou + s, -0.5], [b.needsYou + s + 0.3, -1], [b.sleep + s, -1], [b.sleep + s + 0.6, 0],
  ], easeInOut);
  const lookY = keys(t, [[b.settle, 0], [b.settle + 0.3, 0.8], [b.sleep + s, 0.8], [b.sleep + s + 0.6, 0]], easeInOut);
  return { open, look, lookY, hot, cool, needs, appear };
}

/** Spring helper for scene overlays. */
export const pop = (t: number, at: number) => spring(t, at, 220, 24);


/** The open laptop sits lower in the frame (its lid rises above the base); the shut one, seen from above, higher. */
export function laptopDrop(t: number) {
  return keys(t, [
    [0, 0.07], [b.tiltUp, 0.07], [b.tiltUp + 0.5, 0], [b.morningOpen - 0.1, 0], [b.morningOpen + 0.3, 0.07],
    [b.closeGently + 0.15, 0.07], [b.cardsIn, 0], [b.openMorning, 0], [b.openMorning + 0.4, 0.07],
  ], easeInOut);
}

/** Opening close-up: the camera starts close on the screen, then pulls back as the lid slams. */
export function hookZoom(t: number) {
  // ...and later a slow, deadpan push-in on the frozen 41% in the morning.
  return keys(t, [[0, 1.4], [b.slam - 0.05, 1.3], [b.slam + 0.32, 1], [b.morningOpen + 0.3, 1], [b.rewind, 1.16], [b.rewind + 0.2, 1],
    // ...and in the morning, a push into the screen until the Welcome back window fills the frame.
    [b.openMorning + 0.5, 1], [b.zoomScreen, 1.12], [b.flatIn + 0.3, 2.35]], easeInOut);
}

/** The light between lid and keyboard: a line that flashes as the lid passes the last few degrees. */
export function lightLine(t: number) {
  const l = lid(t);
  const on = t < scenes.payoff.end ? 1 : 0;
  return on * Math.sin(clamp((l - 0.72) / 0.28) * Math.PI);
}

/** The hour Vigil waits for you, as a fraction (drawn as an arc around the eye). */
export function waitArc(t: number) {
  const s = batteryShift;
  if (t < b.waitHour + s || t > b.sleep + s + 0.9) return -1;
  return clamp((clockMinutes(t) - 229) / 60);
}

/** The clock brightens for a moment each time something happens at a time worth reading. */
export function clockPulse(t: number) {
  const s = batteryShift;
  const at = [b.docsDone, b.pipelineDone, b.heatRise, b.apiDone + s, b.needsYou + s, b.sleep + s + 0.4, b.clockWhip + 0.55, b.openMorning];
  let p = 0;
  for (const a of at) {
    const d = t - a;
    if (d > 0 && d < 0.6) p = Math.max(p, Math.sin((d / 0.6) * Math.PI));
  }
  return p;
}
