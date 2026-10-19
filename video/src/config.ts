// Everything you might want to change lives here: timings (in seconds), words, colors.
// "Make the heat spike longer" = move coolDown later. Scenes read these; nothing else hard-codes time.

export const FPS = 30;

/**
 * Timing. Everything is written on a base timeline below, then the holds add reading time: each hold
 * [at, seconds] pushes everything after "at" later. Motion keeps its speed; the words just stay up longer.
 * To give a line more time, add a hold right after it lands.
 */
const holds: Array<[number, number]> = [
  [2.9, 0.9], // You closed the lid. / Claude was not done.
  [4.85, 1.2], // Screen off. Windows suspends the app.
  [6.7, 0.9], // 41%. Nothing got done.
  [8.7, 0.9], // OK. Again. With Vigil.
  [11.3, 1.2], // Vigil keeps watch while Claude works.
  [14.3, 0.5], // Done.
  [16.5, 0.7], // Running hot.
  [18.0, 0.9], // Vigil slows the CPU.
  [20.6, 0.9], // This one needs you.
  [21.19, 7.4], // the phone scene plays here (see phone below)
  [22.0, 1.1], // Waits an hour. Then it sleeps.
];
const at = (x: number) => x + holds.reduce((sum, [h, d]) => (h <= x ? sum + d : sum), 0);
const shiftAll = <T extends Record<string, number>>(o: T): T => Object.fromEntries(Object.entries(o).map(([k, v]) => [k, at(v)])) as T;

/** Scene windows, in seconds from the start of the LinkedIn master. */
const baseScenes = {
  hook: [0.0, 3.3], problem: [3.3, 7.3], rewind: [7.3, 9.2], enter: [9.2, 12.4],
  night: [12.4, 24.0], payoff: [24.0, 33.8], trust: [33.8, 38.6], end: [38.6, 43.0],
} as const;
export const scenes = Object.fromEntries(Object.entries(baseScenes).map(([k, [a, b]]) => [k, { start: at(a), end: at(b) }])) as Record<keyof typeof baseScenes, { start: number; end: number }>;

export const DURATION = scenes.end.end;

/** Beats inside scenes, in absolute seconds (after the holds). */
export const beats = shiftAll({
  // 1. hook
  slam: 0.85, // lid starts closing (after a close look at the screen)
  shake: 1.05,
  hookLine1: 1.12, // "You closed the lid." word by word
  hookLine2: 1.78, // "Claude was not done."

  // 2. problem
  tiltUp: 3.3, // camera rises to look down at the closed lid
  xray: 3.75,
  suspendLabel: 3.85,
  drain: 4.1, // color drains, progress freezes
  glitch: 4.55,
  zzz: 4.7,
  clockWhip: 5.0, // 11:52 PM -> 7:14 AM
  morningOpen: 5.75, // lid opens on the frozen bar
  nothingDone: 5.95,

  // 3. rewind
  rewind: 7.3,
  againWords: 7.55,

  // 4. enter Vigil
  dark: 9.2,
  spark: 9.45,
  eyeOpen: 9.6,
  settle: 10.05,
  name: 10.62,
  watchLine: 10.4,

  // 5. night shift (clock times are in the clock track below)
  closeGently: 12.4,
  cardsIn: 12.85,
  docsDone: 13.6,
  pipelineDone: 14.8,
  heatRise: 15.7,
  coolDown: 16.9,
  apiDone: 18.7,
  needsYou: 19.4,
  phoneIn: 21.18, // the phone scene (7.4 s, from the hold above)
  waitHour: 21.2,
  sleep: 22.5, // eye closes, laptop sleeps
  toDawn: 23.25,

  // 6. payoff: open the lid on the desktop with Welcome back, then zoom into the specifics
  openMorning: 24.0,
  recordingIn: 24.6, // the real recap recording starts on the laptop's screen (its own t = 0)
  generalCaption: 24.3,
  zoomScreen: 26.9, // the camera turns to the screen and pushes in
  flatIn: 27.7, // the recording takes over the frame
  pushIn: 28.0,
  ring: 28.8,
  firstCaption: 28.5,
  secondCaption: 31.1,

  // 7. trust
  trust1: 33.8,
  trust2: 34.6,
  trust3: 35.4,

  // 8. end card
  swingIn: 38.6,
  endName: 39.0,
  endLine1: 39.3,
  endLine2: 39.7,
});
/** How long the slam takes (a duration, not a moment, so it is not shifted). */
export const slamDur = 0.2;

/** The phone scene, relative to beats.phoneIn. */
export const phone = {
  rise: 0.0, // the phone comes up, lock screen with the night's notifications
  ping: 0.9, // landing-page needs you arrives on top
  firstCaption: 1.0,
  unlock: 3.9, // the real Vigil phone app
  secondCaption: 4.1,
  leave: 6.9,
  dur: 7.4,
};

/** The corner clock: [seconds, minutes after 11:52 PM]. Eases between keys. 7:14 AM = 442. */
export const clockKeys: Array<[number, number]> = [
  [0, 0],
  [beats.clockWhip, 0],
  [beats.clockWhip + 0.55, 442], // whip forward to 7:14 AM
  [beats.rewind + 0.15, 442],
  [beats.rewind + 1.1, 0], // rewind to 11:52 PM
  [beats.cardsIn, 0],
  [beats.docsDone, 44], // 12:36 AM
  [beats.pipelineDone, 115], // 1:47 AM
  [beats.heatRise, 138], // 2:10 AM
  [beats.apiDone, 186], // 2:58 AM
  [beats.needsYou, 229], // 3:41 AM
  [beats.waitHour, 229],
  [beats.sleep + 0.4, 291], // 4:43 AM
  [beats.toDawn, 291],
  [beats.openMorning, 442], // 7:14 AM
];

/** Turn on to add "Battery low? It hibernates safely." after the heat beat (+1.9 s). */
export const showBatteryBeat = false;

/** The four demo projects, exactly as Vigil's --demo data has them. Hues match the app's avatars. */
export const sessions = [
  { name: 'landing-page', initials: 'LP', hue: ['#64D2FF', '#0A84FF'] },
  { name: 'api-server', initials: 'AS', hue: ['#66D4CF', '#1FA2B5'] },
  { name: 'data-pipeline', initials: 'DP', hue: ['#D4B48C', '#A2845E'] },
  { name: 'docs-site', initials: 'DS', hue: ['#DA8FFF', '#AF52DE'] },
] as const;

/** Words on screen. Captions stay at 7 words or fewer. */
export const words = {
  hook1: 'You closed the lid.',
  hook2: 'Claude was not done.',
  suspend: 'Screen off. Windows suspends the app.',
  nothing: '41%. Nothing got done.',
  again: 'OK. Again. With Vigil.',
  watch: 'Vigil keeps watch while Claude works.',
  done: 'Done.',
  hot: 'Running hot.',
  cool: 'Vigil slows the CPU.',
  battery: 'Battery low? It hibernates safely.',
  needs: 'This one needs you.',
  wait: 'Waits an hour. Then it sleeps.',
  general: 'Open the lid. Welcome back.',
  phonePing: 'Your phone gets every update.',
  phoneAnswer: 'Answer it from your phone.',
  first: 'What needs you, first.',
  then: 'Then what got done.',
  trust1: 'Puts your settings back after a crash.',
  trust2: 'A watchdog checks every 10 minutes.',
  trust3: 'Native Windows app, written in C#.',
  endLine1: 'Free and open source for Windows.',
  endLine2: 'Link in the comments.',
} as const;

/** Vigil's own palette (src/Vigil/UI/Theme.xaml and Sky.cs). */
export const color = {
  window: '#1C1C1E',
  card: '#2C2C2E',
  control: '#3A3A3C',
  text1: '#F5F5F7',
  text2: '#AEAEB2',
  text3: '#8E8E93',
  blue: '#0A84FF',
  cyan: '#2DD4E8',
  green: '#30D158',
  orange: '#FF9F0A',
  amberA: '#FFB340',
  amberB: '#FF7A2F',
  hotA: '#FF5A4F',
  hotB: '#FF8A3D',
  cool: '#64D2FF',
  flame: '#FFC27A',
  night: ['#070B1C', '#0F1A3A', '#1A2A55'],
  dawn: ['#1B2D5E', '#B35E6B', '#F4A06A'],
} as const;

export const font = {
  display: '"Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif',
  text: '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif',
  mono: '"Cascadia Code", Consolas, monospace',
} as const;

/**
 * The calm README cut (docs/recap.gif): pieces of the master timeline [start, end] in seconds,
 * cross-faded, then a hold on the finished recap. Scenes 4 to 6 only.
 */
export const readmeCut = {
  pieces: [
    [beats.dark + 0.1, scenes.payoff.end - 0.3], // scenes 4 to 6 in one continuous take: Vigil arrives, the night shift, the phone, the real recap
  ] as Array<[number, number]>,
  dip: 0.15, // each piece fades through the night sky into the next: no double exposures
  hold: 1.4, // 1 s on the finished recap, then fading to night so the loop starts where it ends
  loopFade: 0.4,
};
