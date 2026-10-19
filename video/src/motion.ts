// Small, pure helpers. Every value is a function of time t (seconds), so any frame renders on its own.
import { Easing } from 'remotion';

export const clamp = (x: number, a = 0, b = 1) => Math.min(b, Math.max(a, x));
export const lerp = (a: number, b: number, p: number) => a + (b - a) * p;

export const easeOut = Easing.bezier(0.16, 1, 0.3, 1); // calm, weighted landing
export const easeInOut = Easing.bezier(0.65, 0, 0.35, 1);
export const easeIn = Easing.bezier(0.5, 0, 0.75, 0);

/** 0 -> 1 between t0 and t0 + dur with an easing curve. */
export function ramp(t: number, t0: number, dur: number, ease: (x: number) => number = easeOut) {
  if (dur <= 0) return t >= t0 ? 1 : 0;
  return ease(clamp((t - t0) / dur));
}

/** Piecewise keyframes [[t, v], ...], eased between neighbours. */
export function keys(t: number, k: Array<[number, number]>, ease: (x: number) => number = easeInOut) {
  if (t <= k[0][0]) return k[0][1];
  for (let i = 1; i < k.length; i++) {
    const [t1, v1] = k[i];
    const [t0, v0] = k[i - 1];
    if (t <= t1) return t1 === t0 ? v1 : lerp(v0, v1, ease((t - t0) / (t1 - t0)));
  }
  return k[k.length - 1][1];
}

/** Damped spring 0 -> 1 started at t0 (no state: closed form). */
export function spring(t: number, t0: number, stiffness = 170, damping = 26) {
  const x = t - t0;
  if (x <= 0) return 0;
  const w0 = Math.sqrt(stiffness);
  const z = damping / (2 * w0);
  if (z < 1) {
    const wd = w0 * Math.sqrt(1 - z * z);
    return 1 - Math.exp(-z * w0 * x) * (Math.cos(wd * x) + ((z * w0) / wd) * Math.sin(wd * x));
  }
  return 1 - Math.exp(-w0 * x) * (1 + w0 * x);
}

/** Seeded noise in [-1, 1]: same seed and time, same value. */
export function noise(seed: number, t: number) {
  const s = Math.sin(seed * 127.1 + t * 311.7) * 43758.5453;
  return (s - Math.floor(s)) * 2 - 1;
}

/** Smooth seeded wobble for flicker and drift. */
export function wobble(seed: number, t: number, speed = 1) {
  const a = t * speed;
  const i = Math.floor(a);
  const f = a - i;
  const u = f * f * (3 - 2 * f);
  return lerp(noise(seed, i), noise(seed, i + 1), u);
}

/** "11:52 PM" from minutes after 11:52 PM. Same format as the app: h:mm AM. */
export function clockText(minutesAfter: number) {
  const total = (23 * 60 + 52 + Math.round(minutesAfter)) % (24 * 60);
  const h24 = Math.floor(total / 60);
  const m = total % 60;
  const h = h24 % 12 === 0 ? 12 : h24 % 12;
  return `${h}:${String(m).padStart(2, '0')} ${h24 < 12 ? 'AM' : 'PM'}`;
}

export const mix = (a: string, b: string, p: number) => {
  const pa = hex(a), pb = hex(b);
  const c = pa.map((v, i) => Math.round(lerp(v, pb[i], clamp(p))));
  return `rgb(${c[0]}, ${c[1]}, ${c[2]})`;
};
function hex(h: string) {
  if (h.startsWith('rgb')) return h.replace(/[^0-9,]/g, '').split(',').map(Number);
  const s = h.replace('#', '');
  return [0, 2, 4].map((i) => parseInt(s.slice(i, i + 2), 16));
}
