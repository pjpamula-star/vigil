// Vigil on your phone. Three possible screens, in order:
//  1. Not on the Home Screen yet  -> how to add it (iPhone only allows notifications there)
//  2. Notifications not on yet    -> one button
//  3. Everything set              -> Vigil's card, your sessions, what happened
const $app = document.getElementById('app');
const COLORS = { mark: '--mark', lavender: '--mark', orange: '--orange', green: '--green', red: '--red', gray: '--gray', white: '--white', blue: '--blue' };
const STATE_WORD = { working: 'Working', needs: 'Needs you', done: 'Done', failed: 'Stopped', paused: 'Usage limit', quiet: 'No activity', ready: 'Ready', ended: 'Done' };
// "Quiet" means Claude said it was working but nothing has happened for a long time: worth a look, so amber.
const STATE_COLOR = { working: 'blue', needs: 'orange', done: 'green', failed: 'red', paused: 'orange', quiet: 'orange', ready: 'gray', ended: 'gray' };
// The list's sections, in the order you'd act on them.
const GROUP = { needs: 0, quiet: 0, paused: 0, failed: 0, working: 1 };

/** "fetch-artifact-profile-data" -> "Fetch artifact profile data". Real titles are left alone. */
function title(name) {
  const n = String(name || '');
  return /^[a-z0-9]+([-_][a-z0-9]+)+$/.test(n) ? n.replace(/[-_]+/g, ' ').replace(/^./, (c) => c.toUpperCase()) : n;
}

/** Claude's last words, made readable: no markdown, and no raw data dumps ("key: value key: value"). */
function plain(text, fallback = '') {
  let t = String(text || '').replace(/```[\s\S]*?(```|$)/g, ' ').replace(/[*_`#>|]+/g, '').replace(/\s+/g, ' ').trim();
  const keys = (t.match(/(^|[\s,{])"?[A-Za-z_][A-Za-z0-9_]*"?\s*:\s/g) || []).length;
  const codey = (t.match(/(^|[\s,{])"?[a-z]+([A-Z][a-z0-9]*|_[a-z0-9]+)+"?\s*:/g) || []).length;
  if (/^[\[{]/.test(t) || (codey >= 1 && keys >= 2)) t = '';
  return t || fallback;
}

// ---------- the key (from the QR code) ----------
// The pairing link is "/?k=<key>". iPhone saves the page address (with its query) as the
// Home Screen app's start URL, so the installed app always launches with its key.
function readKey() {
  const fromLink = new URLSearchParams(location.search).get('k') || new URLSearchParams(location.hash.slice(1)).get('k');
  if (fromLink && /^[A-Za-z0-9_-]{32,128}$/.test(fromLink)) {
    try { localStorage.setItem('vigil.key', fromLink); } catch {}
    return fromLink;
  }
  try { return localStorage.getItem('vigil.key'); } catch { return null; }
}
const key = readKey();

// ---------- project colors: the same hash and palette as the PC, so a project looks the same everywhere ----------
const HUES = [
  ['#64D2FF', '#0A84FF'], ['#66D4CF', '#1FA2B5'], ['#FF6482', '#FF375F'], ['#FFE15C', '#F5B800'],
  ['#7D7AFF', '#4F6BFF'], ['#7FE0C2', '#20B39A'], ['#D4B48C', '#A2845E'], ['#DA8FFF', '#AF52DE'],
];
function hue(name) {
  let h = 2166136261;
  for (const ch of String(name).toLowerCase()) {
    for (let i = 0; i < ch.length; i++) { h ^= ch.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
  }
  return HUES[h % HUES.length];
}
/** A session's colors: the PC's pick (distinct per project), or the hash if it didn't send one. */
const pair = (s) => (Number.isInteger(s.hue) ? HUES[s.hue % HUES.length] : hue(s.folder || s.name));

function initials(name) {
  const parts = String(name).split(/[-_ ]+/).filter((p) => /^[\p{L}\p{N}]/u.test(p));
  if (!parts.length) return '?';
  return (parts[0][0] + (parts[1] ? parts[1][0] : '')).toUpperCase();
}
/** A round tile in the project's color; a spinning ring while working, a pulsing amber ring while waiting. */
function avatar(name, state, size = 34, hueIndex) {
  const [a, b] = Number.isInteger(hueIndex) ? HUES[hueIndex % HUES.length] : hue(name);
  const ini = initials(name);
  // A small dot in the state's color, like a status badge on a contact photo.
  const dot = state && STATE_COLOR[state] ? `<i class="badge" style="background:${cssVar(STATE_COLOR[state])}"></i>` : '';
  return `<span class="avatar ${esc(state || '')}" style="--a:${a};--b:${b};--size:${size}px"><span class="face" style="font-size:${Math.round(size * (ini.length > 1 ? 0.36 : 0.44))}px">${esc(ini)}</span>${dot}</span>`;
}

// ---------- the sky ----------
function skyName(d = new Date()) {
  const h = d.getHours();
  return h >= 5 && h < 8 ? 'dawn' : h >= 8 && h < 17 ? 'day' : h >= 17 && h < 21 ? 'dusk' : 'night';
}
const skyOverride = new URLSearchParams(location.search).get('sky');
document.documentElement.dataset.sky = skyOverride || skyName();
document.body.insertAdjacentHTML('afterbegin', '<div class="sky"></div><div class="night"></div><div class="stars"></div><div class="drift"><i></i><i></i><i></i></div><div class="sun"></div>');

/** Coming back: night brightens into the sky outside, the eye opens, things arrive in turn. */
function wake() {
  const root = document.documentElement;
  // Previews (?still) show the end of the sunrise straight away.
  if (new URLSearchParams(location.search).has('still')) { root.classList.add('still', 'awake', 'settled'); return; }
  root.dataset.sky = skyOverride || skyName();
  root.classList.remove('awake', 'settled');
  void root.offsetWidth; // restart the transitions
  setTimeout(() => root.classList.add('awake'), 30);
  // After the entrance, later refreshes swap content in place without replaying it.
  clearTimeout(wake.timer);
  wake.timer = setTimeout(() => root.classList.add('settled'), 1800);
}
let hiddenAt = 0;
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'hidden') hiddenAt = Date.now();
  else if (hiddenAt && Date.now() - hiddenAt > 2 * 60000) wake();
});
const standalone = window.navigator.standalone === true || matchMedia('(display-mode: standalone)').matches;
const isIOS = /iPhone|iPad|iPod/.test(navigator.userAgent);


// ---------- tiny helpers ----------
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const cssVar = (name) => `var(${COLORS[name] || '--gray'})`;

let eyeSeq = 0;
function eye(color = 'mark', { badge = false, filled = true } = {}) {
  const badgeColor = badge === 'red' ? 'var(--red)' : 'var(--orange)';
  const c = cssVar(color);
  const id = `em${++eyeSeq}`;
  // class "eye": it blinks and glances (see "The eye's little life" below).
  return `<svg class="eye" viewBox="0 0 100 100" aria-hidden="true">
    <defs><mask id="${id}"><rect x="-20" y="-20" width="140" height="140" fill="#fff"/><circle cx="57" cy="45" r="13" fill="#000"/></mask></defs>
    <path class="almond" d="${almond(1)}" fill="none" stroke="${c}" stroke-width="7.5" stroke-linejoin="round" stroke-linecap="round"/>
    <g class="pupil">${filled ? `<circle cx="50" cy="50" r="17" fill="${c}" mask="url(#${id})"/>` : `<circle cx="50" cy="50" r="15" fill="none" stroke="${c}" stroke-width="5" mask="url(#${id})"/>`}</g>
    ${badge ? `<circle cx="82" cy="22" r="16" fill="var(--card2)"/><circle cx="82" cy="22" r="11" fill="${badgeColor}"/>` : ''}
  </svg>`;
}

/** The almond, its lids closed to `open` (1 open, 0 shut): it flattens toward the middle, the line keeps its width. */
function almond(open) {
  const y = (v) => +(50 + (v - 50) * Math.max(0.02, open)).toFixed(2);
  return `M8 ${y(50)} C24 ${y(27)} 38 ${y(21)} 50 ${y(21)} C62 ${y(21)} 76 ${y(27)} 92 ${y(50)} C76 ${y(73)} 62 ${y(79)} 50 ${y(79)} C38 ${y(79)} 24 ${y(73)} 8 ${y(50)}Z`;
}

// ---------- the eye's little life ----------
// Every few seconds the eye blinks, sometimes twice, glances somewhere and back, or looks left and right.
// One brain for every eye on the page, the same as on the PC. Tap anywhere and the eyes glance at your finger.
// Holds still if you've asked your phone to reduce motion, and runs only while the app is on screen.
const EYE = { maxDx: 11, maxDy: 5, gapMin: 3.5, gapMax: 9 };
const eyeLife = {
  keys: [], start: 0, next: performance.now() + 1500 + Math.random() * 2000, move: null,
  begin(now, move) {
    const k = this.keys = [];
    this.start = now; this.move = move;
    const rest = { open: 1, dx: 0, dy: 0 };
    const key = (ms, pose) => k.push([ms, pose]);
    const blink = (from, look) => { key(from + 90, { ...look, open: 0.06 }); key(from + 150, { ...look, open: 0.06 }); key(from + 290, look); return from + 290; };
    key(0, rest);
    if (move === 'double') { const end = blink(0, rest); key(end + 110, rest); blink(end + 110, rest); }
    else if (move === 'glance') {
      const a = Math.random() * Math.PI * 2;
      const look = { open: 1, dx: Math.cos(a) * EYE.maxDx, dy: Math.sin(a) * EYE.maxDy };
      key(220, look);
      const hold = 700 + Math.random() * 900;
      let end = 220 + hold;
      if (Math.random() < 0.35) end = blink(220 + hold / 2, look) + hold / 2;
      key(end, look); key(end + 280, rest);
    } else if (move === 'scan') {
      const l = { open: 1, dx: -EYE.maxDx, dy: 0 }, r = { open: 1, dx: EYE.maxDx, dy: 0 };
      key(220, l); key(800, l); key(1150, r); key(1750, r); key(2050, rest);
    } else blink(0, rest);
  },
  at(now) {
    const k = this.keys;
    if (k.length && now - this.start > k[k.length - 1][0]) {
      this.keys = []; this.move = null;
      this.next = now + (EYE.gapMin + Math.random() * (EYE.gapMax - EYE.gapMin)) * 1000;
    }
    if (!this.keys.length && now >= this.next) {
      const r = Math.random();
      this.begin(now, r < 0.5 ? 'blink' : r < 0.62 ? 'double' : r < 0.92 ? 'glance' : 'scan');
    }
    if (!this.keys.length) return { open: 1, dx: 0, dy: 0 };
    const t = now - this.start;
    for (let i = 1; i < this.keys.length; i++) {
      const [t1, b] = this.keys[i];
      if (t > t1) continue;
      const [t0, a] = this.keys[i - 1];
      let x = t1 <= t0 ? 1 : Math.min(1, Math.max(0, (t - t0) / (t1 - t0)));
      x = x * x * (3 - 2 * x);
      return { open: a.open + (b.open - a.open) * x, dx: a.dx + (b.dx - a.dx) * x, dy: a.dy + (b.dy - a.dy) * x };
    }
    return this.keys[this.keys.length - 1][1];
  },
};
const stillEyes = matchMedia('(prefers-reduced-motion: reduce)');
let eyeTimer = 0, eyeFrameQueued = false;

function eyeTick() {
  eyeFrameQueued = false;
  clearTimeout(eyeTimer);
  if (document.visibilityState !== 'visible' || stillEyes.matches || new URLSearchParams(location.search).has('still')) return;
  const now = performance.now();
  const pose = eyeLife.at(now);
  let moving = eyeLife.keys.length > 0;
  for (const svg of document.querySelectorAll('svg.eye')) {
    const look = svg._look && now < svg._look.until ? svg._look : null;
    const tx = look ? look.dx : pose.dx, ty = look ? look.dy : pose.dy;
    svg._dx = (svg._dx || 0) + (tx - (svg._dx || 0)) * 0.3;
    svg._dy = (svg._dy || 0) + (ty - (svg._dy || 0)) * 0.3;
    if (Math.abs(tx - svg._dx) < 0.05) svg._dx = tx;
    if (Math.abs(ty - svg._dy) < 0.05) svg._dy = ty;
    if (svg._dx !== tx || svg._dy !== ty || look) moving = true;
    if (svg._open !== pose.open) {
      svg._open = pose.open;
      svg.querySelector('.almond')?.setAttribute('d', almond(pose.open));
    }
    const pupil = svg.querySelector('.pupil');
    if (pupil) {
      const o = Math.max(0.02, pose.open);
      pupil.setAttribute('transform', `translate(0 50) scale(1 ${o.toFixed(3)}) translate(${svg._dx.toFixed(2)} ${(svg._dy - 50).toFixed(2)})`);
      pupil.style.opacity = pose.open > 0.15 ? '' : '0';
    }
  }
  if (moving) queueEyeFrame();
  else eyeTimer = setTimeout(queueEyeFrame, Math.max(16, eyeLife.next - now));
}
function queueEyeFrame() {
  if (eyeFrameQueued) return;
  eyeFrameQueued = true;
  requestAnimationFrame(eyeTick);
}
document.addEventListener('visibilitychange', queueEyeFrame);
stillEyes.addEventListener?.('change', queueEyeFrame);
// For trying it out (and tests): vigilEye.play('blink' | 'double' | 'glance' | 'scan').
window.vigilEye = { play: (move) => { eyeLife.begin(performance.now(), move); queueEyeFrame(); } };
// Tap anywhere: every eye glances toward your finger for a moment.
addEventListener('pointerdown', (e) => {
  const now = performance.now();
  for (const svg of document.querySelectorAll('svg.eye')) {
    const r = svg.getBoundingClientRect();
    if (!r.width) continue;
    const vx = e.clientX - (r.left + r.width / 2), vy = e.clientY - (r.top + r.height / 2);
    const d = Math.hypot(vx, vy);
    if (d < 4) continue;
    const reach = Math.min(1, d / 60);
    svg._look = { dx: (vx / d) * EYE.maxDx * reach, dy: (vy / d) * EYE.maxDy * 1.6 * reach, until: now + 1300 };
  }
  queueEyeFrame();
}, { passive: true });

/** The eye, shut: the PC is asleep or off. */
function eyeShut(color = 'white') {
  const c = cssVar(color);
  return `<svg viewBox="0 0 100 100" aria-hidden="true">
    <path d="M10 44 C26 64 38 70 50 70 C62 70 74 64 90 44" fill="none" stroke="${c}" stroke-width="7.5" stroke-linecap="round"/>
    <path d="M30 63 L24 75 M50 70 L50 83 M70 63 L76 75" fill="none" stroke="${c}" stroke-width="6" stroke-linecap="round"/>
  </svg>`;
}

/** "3:53 AM", "yesterday 11:37 PM", or "Tue 9:10 PM". */
function whenOf(iso) {
  const d = new Date(iso), now = new Date();
  const days = Math.round((new Date(now.toDateString()) - new Date(d.toDateString())) / 86400000);
  if (days === 0) return timeOf(iso);
  if (days === 1) return `yesterday ${timeOf(iso)}`;
  return `${d.toLocaleDateString([], { weekday: 'short' })} ${timeOf(iso)}`;
}

function ago(iso) {
  if (!iso) return '';
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 45) return 'just now';
  if (s < 90) return '1 min ago';
  if (s < 3600) return `${Math.round(s / 60)} min ago`;
  // Hours, not a clock time: "5 h ago" says how old it is; "5:16 AM" makes you do the math.
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  return s < 172800 ? 'yesterday' : `${Math.floor(s / 86400)} days ago`;
}

function toast(text) {
  const t = document.createElement('div');
  t.className = 'toast';
  t.textContent = text;
  document.body.appendChild(t);
  setTimeout(() => t.remove(), 2600);
}

async function api(path, options = {}) {
  const res = await fetch(path, { ...options, headers: { ...(options.headers || {}), authorization: `Bearer ${key}`, 'content-type': 'application/json' } });
  if (!res.ok) throw new Error(`${res.status}`);
  return res.json();
}

// ---------- screens ----------
function screenNoKey() {
  $app.innerHTML = `
    <section class="hero">
      ${eye('mark').replace('<svg', '<svg class="big"')}
      <h1 style="margin-top:18px">Vigil on your phone</h1>
      <p class="sub" style="margin-top:10px">Open Vigil on your PC, go to <b>Settings > Connect my iPhone</b>, and scan the code with your iPhone's camera.</p>
    </section>`;
}

function screenInstall() {
  const share = `<svg class="share-glyph" viewBox="0 0 24 24" fill="none" stroke="var(--blue)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12"/><path d="M8 7l4-4 4 4"/><path d="M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7"/></svg>`;
  $app.innerHTML = `
    <section class="hero">
      ${eye('mark').replace('<svg', '<svg class="big"')}
      <h1 style="margin-top:18px">Put Vigil on your Home Screen</h1>
      <p class="sub" style="margin-top:10px">iPhone only sends notifications to apps on your Home Screen. It takes ten seconds.</p>
      <ol class="steps">
        <li><div class="num">1.</div><div><b>Tap Share ${share}</b><span>${isIOS ? 'At the bottom of Safari (or top, on iPad).' : 'Open this page in Safari on your iPhone first.'}</span></div></li>
        <li><div class="num">2.</div><div><b>Tap “Add to Home Screen”</b><span>Scroll down the list if you don't see it.</span></div></li>
        <li><div class="num">3.</div><div><b>Open Vigil from your Home Screen</b><span>Then turn on notifications. That's it.</span></div></li>
      </ol>
    </section>`;
}

function screenNotifications(error) {
  $app.innerHTML = `
    <section class="hero">
      ${eye('mark').replace('<svg', '<svg class="big"')}
      <h1 style="margin-top:18px">One last step</h1>
      <p class="sub" style="margin-top:10px">Turn on notifications so Vigil can tap you when it matters:</p>
      <ul class="bullets">
        <li>A session needs your answer</li>
        <li>Claude finished, with a one-line summary</li>
        <li>Your PC got too hot or the battery is low</li>
      </ul>
      <p class="tiny" style="margin-top:6px">Only while you're away from your PC. Nothing while you're sitting at it.</p>
      <button class="btn primary block" id="enable" style="margin-top:26px">Turn on notifications</button>
      ${error ? `<p class="tiny center" style="margin-top:12px;color:var(--red)">${esc(error)}</p>` : ''}
      <div class="center" style="margin-top:10px"><button class="link quiet" id="skip">Not now</button></div>
    </section>`;
  document.getElementById('enable').onclick = enableNotifications;
  document.getElementById('skip').onclick = () => { sessionStorage.setItem('vigil.skipNotify', '1'); render(); };
}

async function enableNotifications() {
  const button = document.getElementById('enable');
  button.disabled = true;
  button.textContent = 'Turning on…';
  try {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      return screenNotifications('Notifications are off for Vigil. Turn them on in iPhone Settings > Notifications > Vigil.');
    }
    const reg = await navigator.serviceWorker.ready;
    const { publicKey } = await (await fetch('/api/vapid')).json();
    const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlB64ToUint8Array(publicKey) });
    await api('/api/pair', { method: 'POST', body: JSON.stringify({ subscription: sub.toJSON(), device: navigator.userAgent.match(/iPhone|iPad|Android/)?.[0] || 'phone' }) });
    try { localStorage.setItem('vigil.notify', 'on'); } catch {}
    toast('Notifications are on');
    render();
  } catch (err) {
    screenNotifications(`Couldn't turn them on (${err.message}). Try again.`);
  }
}

function urlB64ToUint8Array(base64) {
  const padding = '='.repeat((4 - (base64.length % 4)) % 4);
  const raw = atob((base64 + padding).replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from([...raw].map((c) => c.charCodeAt(0)));
}

/** One session: the project's avatar, the title, then state and time on their own line, so nothing gets cut off. */
// Set while the PC has gone quiet mid-run: rows then say what was last seen, without live timers.
let lostSince = null;

function sessionRow(s) {
  if (lostSince && (s.state === 'working' || s.state === 'needs')) {
    s = { ...s, stateLabel: s.state === 'needs' ? 'Last seen needing you' : 'Last seen working', turnStartedAt: null, meta: ago(lostSince), url: null, stale: true };
  }
  const color = s.stale ? 'gray' : STATE_COLOR[s.state] || 'gray';
  const answer = s.state === 'needs' && s.url
    ? `<div class="action"><a class="btn primary small" href="${esc(s.url)}">Answer in Claude</a></div>` : '';
  const when = s.state === 'working' && s.turnStartedAt
    ? `<span class="tick" data-since="${esc(s.turnStartedAt)}">${clock(Date.now() - new Date(s.turnStartedAt))}</span>` : esc(s.meta || '');
  const word = s.state === 'quiet' ? 'Quiet' : (s.stateLabel || STATE_WORD[s.state] || '');
  const line = s.state === 'quiet'
    ? 'Nothing has happened for a while. It may be stuck or waiting on you.'
    : plain(s.line, s.state === 'done' ? 'Finished. Tap to see what Claude said.' : '');
  const open = !!s.id;
  return `<div class="row ${open ? 'tap' : ''}" ${open ? `data-open="${esc(s.id)}" role="button" tabindex="0" aria-label="${esc(title(s.name))}: details"` : ''}>
    ${avatar(s.folder || s.name, s.stale ? "ended" : s.state, 34, s.hue)}
    <div class="body">
      <div class="name">${esc(title(s.name))}</div>
      <div class="status-line"><span class="state-word" style="color:${cssVar(color)}">${esc(word)}</span>${s.agent ? `<span class="sep">·</span><span class="when">${esc(s.agent)}</span>` : ''}${when ? `<span class="sep">·</span><span class="when">${when}</span>` : ''}</div>
      ${line ? `<div class="what">${esc(line)}</div>` : ''}
      ${answer}
    </div>
  </div>`;
}

/** Needs you first, then working, then what finished today, then earlier. */
function sessionGroups(sessions) {
  const startOfToday = new Date(); startOfToday.setHours(0, 0, 0, 0);
  const at = (s) => new Date(s.lastActivityAt || s.since || 0).getTime();
  const groups = [['Needs you', []], ['Working', []], ['Done today', []], ['Earlier', []]];
  if (lostSince) { groups[0][0] = 'Before your PC went quiet'; groups[1] = groups[0]; }
  for (const s of [...sessions].sort((a, b) => at(b) - at(a))) {
    const g = GROUP[s.state] ?? (at(s) >= startOfToday.getTime() ? 2 : 3);
    groups[g][1].push(s);
  }
  return groups.filter(([, list], i) => list.length && groups.indexOf(groups[i]) === i);
}

/** A running stopwatch: "4:07", "1:02:33". */
function clock(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), s = t % 60;
  return h ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

// The header gets its hairline once the page scrolls under it, like iOS.
addEventListener('scroll', () => document.querySelector('.top')?.classList.toggle('scrolled', scrollY > 4), { passive: true });

// Working timers tick every second, like a Live Activity.
setInterval(() => {
  for (const el of document.querySelectorAll('.tick')) el.textContent = clock(Date.now() - new Date(el.dataset.since));
}, 1000);

function hourLabel(d) {
  const h = d.getHours() % 12 || 12, ampm = d.getHours() < 12 ? 'AM' : 'PM';
  return d.getMinutes() ? `${h}:${String(d.getMinutes()).padStart(2, '0')} ${ampm}` : `${h} ${ampm}`;
}

/** Round-hour marks between from and to (ms), at most max of them. Same rule as the PC. */
function ticks(from, to, max = 4) {
  const H = 3600000;
  const step = [0.25, 0.5, 1, 2, 3, 4, 6, 12, 24].map((h) => h * H).find((st) => (to - from) / st <= max) || 24 * H;
  const stepHours = Math.max(1, step / H);
  const first = new Date(from); first.setMinutes(0, 0, 0); first.setHours(first.getHours() - (first.getHours() % stepHours));
  const out = [];
  for (let t = first.getTime(); t <= to; t += step) if (t >= from) out.push(t);
  return out;
}

/** When each session worked or waited on you, side by side. */
function activityChart(sessions) {
  const lanes = sessions.filter((s) => s.spans?.length).slice(0, 6);
  if (!lanes.length) return '';
  const now = Date.now();
  const earliest = Math.min(...lanes.flatMap((s) => s.spans.map((x) => new Date(x.start).getTime())));
  const from = Math.max(now - 12 * 3600000, Math.min(earliest - 10 * 60000, now - 3600000));
  const to = now;
  const pct = (t) => Math.min(100, Math.max(0, ((t - from) / (to - from)) * 100));
  const marks = ticks(from, to);
  const waiting = lanes.some((s) => s.spans.some((x) => x.kind === 'waiting'));
  const lane = (s) => `<div class="lane"><div class="lane-name"><i class="hue-dot" style="--a:${pair(s)[0]};--b:${pair(s)[1]}"></i>${esc(title(s.name))}</div><div class="track">${s.spans.map((x) => {
    const a = pct(new Date(x.start).getTime()), b = pct(new Date(x.end).getTime());
    const live = now - new Date(x.end).getTime() < 5 * 60000 && (s.state === 'working' || s.state === 'needs');
    return `<span class="bar ${x.kind === 'waiting' ? 'waiting' : 'working'} ${live ? 'live' : ''}" style="left:${a}%;width:${Math.max(0, b - a)}%"></span>`;
  }).join('')}</div></div>`;
  const { busy, most } = overlap(lanes.map((s) => s.spans), from, to);
  const frac = Math.min(1, busy / (to - from));
  return `<h2>Activity</h2>
    <div class="card chart">
      <div class="chart-head">
        <div class="ring-sum">${ring(frac)}<div><div class="ring-big">${esc(durationShort(busy / 1000))}</div><div class="tiny">Claude at work${most > 1 ? ` · up to ${most} at once` : ''}</div></div></div>
      </div>
      <div class="legend"><span><i class="working"></i>Working</span>${waiting ? '<span><i class="waiting"></i>Waiting on you</span>' : ''}</div>
      <div class="lanes"><div class="gridlines">${marks.map((t) => `<i style="left:${pct(t)}%"></i>`).join('')}</div>${lanes.map(lane).join('')}</div>
      <div class="axis">${marks.map((t, i) => `<span class="${pct(t) < 6 ? 'first' : pct(t) > 94 ? 'last' : ''}" style="left:${pct(t)}%">${hourLabel(new Date(t))}</span>`).join('')}</div>
    </div>`;
}

/** Time with at least one session working, and the most at once (same rule as the PC). */
function overlap(laneSpans, from, to) {
  const edges = [];
  for (const spans of laneSpans) for (const x of spans) {
    if (x.kind !== 'working') continue;
    const a = Math.max(from, new Date(x.start).getTime()), b = Math.min(to, new Date(x.end).getTime());
    if (b > a) edges.push([a, 1], [b, -1]);
  }
  edges.sort((p, q) => p[0] - q[0] || p[1] - q[1]);
  let busy = 0, running = 0, most = 0, since = 0;
  for (const [at, d] of edges) { if (running > 0) busy += at - since; running += d; most = Math.max(most, running); since = at; }
  return { busy, most };
}

function durationShort(seconds) {
  const m = Math.round(seconds / 60);
  if (m < 60) return `${m} min`;
  return `${Math.floor(m / 60)} h ${m % 60 ? `${m % 60} min` : ''}`.trim();
}

/** An activity ring, like the Apple Watch: draws itself in. */
function ring(frac) {
  const r = 17, c = 2 * Math.PI * r;
  return `<svg class="ring" viewBox="0 0 44 44" aria-hidden="true">
    <defs><linearGradient id="ringg" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#0a84ff"/><stop offset="1" stop-color="#2dd4e8"/></linearGradient></defs>
    <circle cx="22" cy="22" r="${r}" fill="none" stroke="rgba(10,132,255,.22)" stroke-width="6"/>
    <circle class="ring-arc" cx="22" cy="22" r="${r}" fill="none" stroke="url(#ringg)" stroke-width="6" stroke-linecap="round"
      transform="rotate(-90 22 22)" style="--c:${c.toFixed(1)};--v:${(c * (1 - frac)).toFixed(1)}" stroke-dasharray="${c.toFixed(1)}" stroke-dashoffset="${(c * (1 - frac)).toFixed(1)}"/>
  </svg>`;
}

function recapCard(i) {
  const needs = i.state === 'needs';
  const color = STATE_COLOR[i.state] || 'gray';
  const facts = (i.chips || []).filter((c) => !c.text.startsWith('Asked:')).map((c) => c.text).join(' · ');
  const asked = (i.chips || []).find((c) => c.text.startsWith('Asked:'));
  const link = i.url ? `<div style="margin-top:12px"><a class="btn ${needs ? 'primary' : ''} small" href="${esc(i.url)}">${needs ? 'Answer in Claude' : 'Open in Claude'}</a></div>` : '';
  const open = i.id && (last?.status?.sessions || []).some((x) => x.id === i.id);
  return `<div class="card ${needs ? 'needs' : ''} ${open ? 'tap' : ''}" ${open ? `data-open="${esc(i.id)}" role="button" tabindex="0"` : ''}>
    <div class="name-row">${avatar(i.folder || i.name, i.state, 28, i.hue)}<div class="body"><div class="name">${esc(title(i.name))}</div><div class="state-word" style="color:${cssVar(color)}">${esc(i.stateText)}</div></div></div>
    <div class="headline">${esc(plain(i.headline, 'Finished. Open it in Claude to see the result.'))}</div>
    ${facts ? `<div class="facts-line">${esc(facts)}</div>` : ''}
    ${asked && !needs ? `<div class="asked">${esc(asked.text.replace('Asked:', 'Asked you:'))}</div>` : ''}
    ${link}
  </div>`;
}

/** An idle PC checks in every 5 minutes; past this without a word, it's asleep or off. */
const IDLE_GRACE_MIN = 12;

/**
 * What the PC itself is doing right now, when Claude isn't running: asleep (since when), off, or awake
 * (in use, idle, or with its screen off). Null for a PC running an older Vigil that doesn't say.
 */
function pcNow(st, minutesOld) {
  const sl = st.sleep;
  if (!sl) return null;
  if (sl.state === 'asleep') {
    return { title: 'Asleep', shut: true, facts: [`since ${whenOf(sl.since)}`, ...(sl.afterWork ? ['after Claude finished'] : []), ...powerFacts(st)] };
  }
  if (sl.state === 'off') {
    return sl.why === 'closed'
      ? { title: 'Vigil isn\'t running', shut: true, facts: [`closed ${whenOf(sl.since)}`] }
      : { title: 'Off', shut: true, facts: [`shut down ${whenOf(sl.since)}`] };
  }
  if (minutesOld > IDLE_GRACE_MIN) {
    return { title: 'Probably asleep', shut: true, facts: [`last heard ${ago(st.updatedAt || st.receivedAt)}`] };
  }
  const facts = [];
  if (sl.screen === 'off') facts.push('screen off');
  else if (sl.idleMinutes != null) facts.push(sl.idleMinutes < 2 ? 'in use' : `idle ${durationShort(sl.idleMinutes * 60)}`);
  const woke = sl.wokeAt && Date.now() - new Date(sl.wokeAt).getTime() < 2 * 3600000;
  if (woke) facts.push(`woke ${whenOf(sl.wokeAt)}${sl.sleptAt ? ` after ${durationShort((new Date(sl.wokeAt) - new Date(sl.sleptAt)) / 1000)} asleep` : ''}`);
  return { title: 'Awake', shut: false, facts: [...facts, ...powerFacts(st)] };
}

/** Battery, lid and heat, briefly. */
function powerFacts(st) {
  const p = st.power;
  const out = [];
  if (p) {
    if (p.battery != null) out.push(`${p.battery}%${p.source === 'plugged in' ? ' plugged in' : ''}`);
    if (p.lid === 'closed') out.push('Lid closed');
    if (p.heat && p.heat !== 'normal') out.push(p.heat === 'hot' ? 'Hot' : 'Warm');
  }
  return out;
}

/** The slim idle bar's facts: "35%", "Lid closed", and how old the news is when the PC has gone quiet. */
function idleFacts(st, minutesOld) {
  const p = st.power;
  const out = [];
  if (minutesOld > 15) out.push(`PC last heard ${ago(st.updatedAt)}`);
  if (p) {
    if (p.battery != null) out.push(`${p.battery}%${p.source === 'plugged in' ? ' plugged in' : ''}`);
    if (p.lid === 'closed') out.push('Lid closed');
    if (p.heat && p.heat !== 'normal') out.push(p.heat === 'hot' ? 'Hot' : 'Warm');
  } else if (st.facts) {
    out.push(...st.facts.split(/\s*·\s*/).filter((f) => f && !/temperature fine/i.test(f)));
  }
  return out;
}

/** The status card's color, from what the PC says. */
function tone(st) {
  if (st.tone) return st.tone;
  return { needs: 'needs', hot: 'danger', working: st.color === 'green' ? 'done' : 'working', manual: 'manual', paused: 'paused' }[st.icon] || 'idle';
}

function screenMain(data) {
  const st = data?.status;
  if (!st) {
    $app.innerHTML = `
      ${topBar(`<div class="brand">${eye('mark')}Vigil</div>`, null)}
      <div class="status"><div class="eye-tile" style="background:var(--card2)">${eye('gray', { filled: false })}</div>
        <div><div class="title">Waiting for your PC</div><div class="line">Vigil on your PC sends its first update in a moment.</div></div></div>
      ${footer()}`;
    wireFooter();
    wireTop();
    return;
  }
  const minutesOld = (Date.now() - new Date(st.updatedAt || st.receivedAt).getTime()) / 60000;
  // While Claude runs, the PC reports every 2 minutes. If it has gone quiet, what it last said ("Awake",
  // "Working") can't be trusted any more, so say that instead of repeating it.
  const lost = isLost(st);
  lostSince = lost ? (st.updatedAt || st.receivedAt) : null;
  const stale = lost
    ? `<div class="stale">Last heard from your PC ${esc(ago(st.updatedAt))}, when it said: ${esc(st.title)}. It may be asleep, closed or offline. This updates by itself when it's back.</div>` : '';
  const sessions = (st.sessions || []);
  const waiting = sessions.filter((s) => s.state === 'needs').length;
  if (navigator.setAppBadge) { try { waiting ? navigator.setAppBadge(waiting) : navigator.clearAppBadge(); } catch {} }
  const recap = st.recap;
  const t = lost ? 'idle' : tone(st);
  // Idle is the normal state, so it gets a slim bar saying what the PC itself is doing; the big card is
  // for when Claude is actually running.
  const gone = st.sleep && st.sleep.state !== 'awake';
  const pc = !lost && (t === 'idle' || gone) ? pcNow(st, minutesOld) : null;
  const bar = (icon, title, facts) => `<div class="status-bar reveal" style="--i:0">${icon}<div><b>${esc(title)}</b>${facts.map((f) => `<span class="sep">·</span>${esc(f)}`).join('')}</div></div>`;
  const status = pc
    ? bar(pc.shut ? eyeShut('white') : eye('white', { filled: false }), pc.title, pc.facts)
    : t === 'idle' || t === 'paused'
    ? bar(eye('white', { filled: false }), lost ? 'Not hearing from your PC' : t === 'paused' ? st.title : 'Idle', lost ? [] : idleFacts(st, minutesOld))
    : `<div class="status reveal tone-${esc(t)}" style="--i:0">
      <div class="eye-tile">${eye('white', { filled: true })}</div>
      <div><div class="title">${esc(st.title)}</div><div class="line">${esc(st.line)}</div>${st.facts ? `<div class="status-facts">${esc(st.facts)}</div>` : ''}</div>
    </div>`;
  $app.innerHTML = `
    ${topBar(`<div class="brand">${eye('mark')}Vigil</div>`, shownAt(st))}
    ${status}
    ${stale}
    ${sessionGroups(sessions).map(([name, list], g) => `<h2 class="reveal" style="--i:${1 + g}">${esc(name)}</h2><div class="list reveal" style="--i:${1 + g}">${list.map(sessionRow).join('')}</div>`).join('')}
    <div class="reveal" style="--i:3">${activityChart(sessions)}</div>
    ${recap?.items?.length ? `<h2 class="reveal" style="--i:4">What happened · ${esc(recap.range || 'last 24 hours')}</h2><div class="recap-title">${esc(recap.title)}</div>${recap.items.map(recapCard).join('')}` : ''}
    ${footer()}`;
  wireFooter();
  wireTop();
}

// ---------- header: a Refresh button and when the PC last reported ----------
const REFRESH_ICON = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 12a8 8 0 1 1-2.34-5.66" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/><path d="M20.5 3.5v5h-5" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

// When you tap Refresh and get an answer, the page is current as of that moment, so "Updated" restarts
// from "just now". The PC only sends when something changes (within seconds), plus every 2 minutes while
// Claude is running. So a check vouches for the page unless the PC should have sent a beat and hasn't:
// then it may be asleep or offline, and the time it last reported is the honest one.
let checkedAt = null;
const BEAT_GRACE_MIN = 4;
function busy(st) {
  return !!st?.holding || (st?.sessions || []).some((s) => s.state === 'working' || s.state === 'needs');
}
function isLost(st) {
  const sent = st?.updatedAt || st?.receivedAt;
  if (st?.sleep && st.sleep.state !== 'awake') return false; // it said goodbye: asleep or off
  return !!sent && busy(st) && (Date.now() - new Date(sent).getTime()) / 60000 > BEAT_GRACE_MIN;
}
function shownAt(st) {
  const sent = st?.updatedAt || st?.receivedAt || null;
  if (!checkedAt || !sent) return sent;
  const minutesOld = (Date.now() - new Date(sent).getTime()) / 60000;
  if (busy(st) && minutesOld > BEAT_GRACE_MIN) return sent;
  return new Date(Math.max(checkedAt, new Date(sent).getTime())).toISOString();
}

function topBar(left, updatedAt) {
  return `<div class="top">${left}<div class="top-right">
    <span class="tiny" id="updated" data-at="${esc(updatedAt || '')}">${updatedAt ? `Updated ${esc(ago(updatedAt))}` : ''}</span>
    <button class="refresh" id="refresh" aria-label="Refresh">${REFRESH_ICON}</button>
  </div></div>`;
}

function wireTop() {
  const b = document.getElementById('refresh');
  if (b) b.onclick = () => refresh({ manual: true });
  const back = document.getElementById('back');
  if (back) back.onclick = () => (history.state?.id ? history.back() : closeSession());
  for (const el of document.querySelectorAll('[data-open]')) {
    if (!el.dataset.open) continue;
    const go = (e) => { if (e.target.closest('a, button')) return; openSession(el.dataset.open); };
    el.onclick = go;
    el.onkeydown = (e) => { if (e.key === 'Enter') go(e); };
  }
}

// "Updated 2 min ago" keeps counting, so you can see how fresh the page is.
setInterval(() => {
  const u = document.getElementById('updated');
  if (u?.dataset.at) u.textContent = `Updated ${ago(u.dataset.at)}`;
}, 15000);

// ---------- one session, in full ----------
const STEP_COLOR = {
  commit: 'green', push: 'green', deploy: 'green', done: 'green', test: 'green',
  error: 'red', failed: 'red',
  needsyou: 'orange', question: 'orange', install: 'orange', delete: 'orange', paused: 'orange',
  prompt: 'white',
};

function duration(seconds) {
  const m = Math.round(seconds / 60);
  if (m < 1) return 'under a minute';
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60), r = m % 60;
  return r ? `${h} h ${r} min` : `${h} h`;
}

function timeOf(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

function stepColor(step, i, s) {
  if (i === 0 && s.state === 'working') return 'blue';
  if (step.kind === 'test' && /fail/i.test(step.text)) return 'red';
  return STEP_COLOR[step.kind] || 'gray';
}

function screenSession(s, st) {
  // The PC went quiet mid-run: show what was last seen, without a live timer or an "answer" call to action.
  const lostAt = isLost(st) && (s.state === 'working' || s.state === 'needs') ? (st.updatedAt || st.receivedAt) : null;
  const color = lostAt ? 'gray' : STATE_COLOR[s.state] || 'gray';
  const word = lostAt ? (s.state === 'needs' ? 'Last seen needing you' : 'Last seen working') : s.stateLabel || STATE_WORD[s.state] || '';
  const status = lostAt
    ? `<span class="state-word" style="color:${cssVar(color)}">${esc(word)}</span><span class="detail-since">${esc(ago(lostAt))}</span>`
    : s.state === 'working' && s.turnStartedAt
    ? `<span class="state-word" style="color:${cssVar(color)}">${esc(word)}</span><span class="detail-since">for <span class="tick" data-since="${esc(s.turnStartedAt)}">${clock(Date.now() - new Date(s.turnStartedAt))}</span></span>`
    : `<span class="state-word" style="color:${cssVar(color)}">${esc(word)}</span><span class="detail-since">${s.state === 'needs' ? `waiting since ${esc(timeOf(s.since))}` : s.state === 'done' && s.since ? `at ${esc(timeOf(s.since))}` : s.since ? `since ${esc(timeOf(s.since))}` : ''}</span>`;
  const asking = s.state === 'needs' && !lostAt;
  const action = s.url
    ? `<a class="btn ${asking ? 'primary' : ''} block" href="${esc(s.url)}">${asking ? 'Answer in Claude' : 'Open in Claude'}</a>` : '';

  // What's happening, in the words that matter for this state.
  let now = '';
  if (lostAt) {
    now = `<div class="stale">Last heard from your PC ${esc(ago(lostAt))}. It may be asleep, closed or offline. This updates by itself when it's back.</div>`
      + section('Last seen', `<div class="big-line">${esc(s.state === 'needs' ? (s.reason || s.question || s.line || 'Waiting for your answer') : (s.now || s.line || 'Working'))}</div>`);
  } else if (s.state === 'working') {
    const extra = [
      s.helpers ? `${s.helpers} helper${s.helpers === 1 ? '' : 's'} running` : '',
      ...(s.background || []).map((b) => `In the background: ${b}`),
    ].filter(Boolean);
    now = section('Working on', `<div class="big-line">${esc(s.now || s.line || 'Working')}</div>${extra.map((x) => `<div class="tiny" style="margin-top:6px">${esc(x)}</div>`).join('')}`);
  } else if (s.state === 'needs') {
    now = section('Claude is asking', `<div class="big-line" style="color:var(--orange)">${esc(s.reason || s.question || s.line || 'Waiting for your answer')}</div>`);
  } else if (s.reason && (s.state === 'failed' || s.state === 'paused' || s.state === 'quiet')) {
    now = section('What happened', `<div class="big-line">${esc(s.reason)}</div>`);
  }

  const asked = s.task ? section(s.prompts > 1 ? `You asked · request ${s.prompts}` : 'You asked', `<div class="quote">${esc(s.task)}</div>`) : '';
  const said = s.said ? section('Claude said', `<div class="said">${esc(s.said)}</div>`) : (s.summary ? section('Summary', `<div class="said">${esc(s.summary)}</div>`) : '');
  const question = s.question && s.state !== 'needs' ? `<div class="asked" style="margin-top:10px">Asked you: ${esc(s.question)}</div>` : '';

  const steps = (s.steps || []).length ? section('Steps', `<ol class="timeline">${s.steps.map((x, i) => `
      <li><span class="dot" style="background:${cssVar(stepColor(x, i, s))}"></span><span class="step-text ${x.kind === 'prompt' ? 'strong' : ''}">${esc(x.text)}</span><span class="step-time">${esc(ago(x.at))}</span></li>`).join('')}</ol>`, true) : '';

  const files = s.files || [];
  const commits = s.commits || [];
  const changes = files.length || commits.length || s.facts ? section('Changes', `
      ${s.facts ? `<div class="facts-line" style="margin-top:0">${esc(s.facts)}</div>` : ''}
      ${files.length ? `<ul class="files">${files.map((f) => `<li>${esc(f)}</li>`).join('')}${s.moreFiles ? `<li class="tiny">and ${s.moreFiles} more</li>` : ''}</ul>` : ''}
      ${commits.length ? `<div class="sub-head">Commits</div><ul class="commits">${commits.map((c) => `<li>${esc(c)}</li>`).join('')}</ul>` : ''}`) : '';

  const meta = [
    s.startedAt ? `Started ${timeOf(s.startedAt)}` : '',
    s.lastActivityAt ? `last activity ${ago(s.lastActivityAt)}` : '',
    s.worked ? `worked ${duration(s.worked)} today` : '',
    s.folder && s.folder !== s.name ? `folder ${s.folder}` : '',
  ].filter(Boolean).join(' · ');

  $app.innerHTML = `
    ${topBar(`<button class="back" id="back"><span class="chev"></span>Vigil</button>`, shownAt(st))}
    <div class="session-hero tone-${esc(lostAt ? 'idle' : { working: 'working', needs: 'needs', done: 'done', failed: 'danger', paused: 'warning' }[s.state] || 'idle')}">
      <div class="hero-head">${avatar(s.folder || s.name, lostAt ? 'ended' : s.state, 44, s.hue)}<h1 class="detail-title">${esc(title(s.name))}</h1></div>
      <div class="detail-status">${status}</div>
      ${action}
    </div>
    ${now}
    ${asked}
    ${said}${question}
    ${steps}
    ${changes}
    ${s.spans?.length ? activityChart([s]).replace('<h2>Activity</h2>', '<h2>Today</h2>') : ''}
    ${meta ? `<p class="tiny detail-meta">${esc(meta[0].toUpperCase() + meta.slice(1))}</p>` : ''}`;
  wireTop();
}

function section(title, body, flush = false) {
  return `<h2>${esc(title)}</h2><div class="card ${flush ? 'flush' : ''}" style="margin-top:0">${body}</div>`;
}

function standOut() {
  return `<details class="tips">
    <summary>Make notifications impossible to miss</summary>
    <p class="tiny" style="margin:8px 0 12px">iPhone decides how loud a notification is. Set these once:</p>
    <ol class="steps small">
      <li><div class="num">1.</div><div><b>Settings > Notifications > Vigil</b><span>Turn on <b>Lock Screen</b>, <b>Notification Center</b>, <b>Banners</b> and <b>Sounds</b>.</span></div></li>
      <li><div class="num">2.</div><div><b>Banner Style > Persistent</b><span>Banners stay at the top until you swipe them away.</span></div></li>
      <li><div class="num">3.</div><div><b>Show Previews > Always</b><span>So you can read the summary on the Lock Screen.</span></div></li>
      <li><div class="num">4.</div><div><b>Settings > Focus > Sleep</b> (and Do Not Disturb) <b>> Apps > Add Vigil</b><span>So Vigil still gets through overnight.</span></div></li>
      <li><div class="num">5.</div><div><b>Settings > Notifications > Scheduled Summary</b><span>Make sure Vigil is <b>not</b> in it, so nothing gets held back.</span></div></li>
    </ol>
  </details>`;
}

function siri() {
  return `<details class="tips">
    <summary>“Hey Siri, Claude status”</summary>
    <p class="tiny" style="margin:8px 0 12px">Ask Siri what Claude is doing, without opening anything. One-time setup, about a minute:</p>
    <ol class="steps small">
      <li><div class="num">1.</div><div><b>Copy your Siri link</b><span><button class="btn small primary" id="copySiri" style="margin-top:8px">Copy Siri link</button></span></div></li>
      <li><div class="num">2.</div><div><b>Shortcuts > + > Add Action > “Get Contents of URL”</b><span>Paste the link where it says URL. <a class="link" href="shortcuts://create-shortcut" style="padding:0">Open Shortcuts</a></span></div></li>
      <li><div class="num">3.</div><div><b>Add Action > “Speak Text”</b><span>It picks up the contents automatically.</span></div></li>
      <li><div class="num">4.</div><div><b>Name it “Claude status”</b><span>Now say “Hey Siri, Claude status”. You can also put it on your Home Screen or Lock Screen as a button.</span></div></li>
    </ol>
    <p class="tiny" style="margin-top:4px">The link contains your private key. Keep it in your shortcut only.</p>
  </details>`;
}

function footer() {
  const on = typeof Notification !== 'undefined' && Notification.permission === 'granted';
  return `${on ? standOut() : ''}${siri()}<div class="footer">
    ${on ? '<button class="link" id="test">Send a test notification</button>' : '<button class="link" id="turnon">Turn on notifications</button>'}
    <span class="tiny">${on ? 'Notifications on · only while you\'re away from your PC' : 'Notifications are off'}</span>
  </div>`;
}

function wireFooter() {
  const copy = document.getElementById('copySiri');
  if (copy) copy.onclick = async () => {
    const link = `${location.origin}/api/say?k=${key}`;
    try { await navigator.clipboard.writeText(link); toast('Copied. Paste it into Shortcuts.'); }
    catch { prompt('Copy this link:', link); }
  };
  const test = document.getElementById('test');
  if (test) test.onclick = async () => {
    try {
      const r = await api('/api/update', { method: 'POST', body: JSON.stringify({ notify: { title: 'Vigil', body: 'Notifications work. You\'ll hear from me when Claude needs you or finishes.', tag: 'test' } }) });
      toast(r.pushed ? 'Sent. Look at your lock screen.' : 'Couldn\'t send. Try turning notifications on again.');
    } catch { toast('Couldn\'t reach Vigil. Check your connection.'); }
  };
  const on = document.getElementById('turnon');
  if (on) on.onclick = () => { sessionStorage.removeItem('vigil.skipNotify'); screenNotifications(); };
}

// ---------- preview (/?demo=main|install|notify): fake data, no key, nothing sent ----------
const demo = new URLSearchParams(location.search).get('demo');
const minsAgo = (m) => new Date(Date.now() - m * 60000).toISOString();
const span = (a, b, kind = 'working') => ({ start: minsAgo(a), end: minsAgo(b), kind });
// The same demo night as the PC app (src/Vigil/UI/DemoData.cs): it is 3:41 AM, the lid is closed,
// landing-page has just stopped to ask, and three projects finished earlier. Made-up data only.
const DEMO_STATUS = {
  updatedAt: new Date(Date.now() - 60000).toISOString(), title: 'Awake', line: 'Claude is waiting on you.',
  color: 'orange', icon: 'needs', tone: 'needs', holding: true, facts: 'Lid closed  ·  Plugged in  ·  Temperature fine',
  sessions: [
    { id: 'landing', hue: 0, name: 'landing-page', state: 'needs', line: 'Waiting for your approval to run npm install.', meta: 'just now', url: 'https://claude.ai/code',
      since: minsAgo(0.5), startedAt: minsAgo(229), lastActivityAt: minsAgo(0.5), prompts: 1, worked: 13700, reason: 'Waiting for your approval to run npm install.',
      task: 'add the pricing section and install what it needs', files: ['Pricing.tsx', 'pricing.module.css', 'page.tsx'], facts: '3 files changed',
      steps: [{ at: minsAgo(0.5), kind: 'needsyou', text: 'Waiting for your approval to run npm install.' }, { at: minsAgo(40), kind: 'edit', text: 'Edited 3 files' }, { at: minsAgo(229), kind: 'prompt', text: 'You asked: “add the pricing section and install what it needs”' }],
      spans: [span(229, 0.5), span(0.5, 0, 'waiting')] },
    { id: 'api', hue: 1, name: 'api-server', state: 'done', line: 'Added rate limiting and 12 tests, all passing.', meta: '2:58 AM',
      since: minsAgo(43), startedAt: minsAgo(229), lastActivityAt: minsAgo(43), prompts: 1, worked: 11160, task: 'add rate limiting with tests',
      said: 'Added rate limiting to the API and 12 tests for it. All tests pass.', files: ['server.ts', 'rateLimit.ts', 'rateLimit.test.ts'], facts: '3 files changed · 12 tests passing',
      steps: [{ at: minsAgo(43), kind: 'done', text: 'Done' }, { at: minsAgo(229), kind: 'prompt', text: 'You asked: “add rate limiting with tests”' }],
      spans: [span(229, 43)] },
    { id: 'pipeline', hue: 6, name: 'data-pipeline', state: 'done', line: 'Fixed crash on empty CSV rows.', meta: '1:47 AM',
      since: minsAgo(114), startedAt: minsAgo(229), lastActivityAt: minsAgo(114), prompts: 1, worked: 6900, task: 'fix the crash on empty CSV rows',
      files: ['parse.py', 'test_parse.py'], facts: '2 files changed',
      steps: [{ at: minsAgo(114), kind: 'done', text: 'Done' }, { at: minsAgo(229), kind: 'prompt', text: 'You asked: “fix the crash on empty CSV rows”' }],
      spans: [span(229, 114)] },
    { id: 'docs', hue: 7, name: 'docs-site', state: 'done', line: 'Rewrote the setup guide.', meta: '12:36 AM',
      since: minsAgo(185), startedAt: minsAgo(229), lastActivityAt: minsAgo(185), prompts: 1, worked: 2640, task: 'rewrite the setup guide',
      files: ['setup.md'], facts: '1 file changed',
      steps: [{ at: minsAgo(185), kind: 'done', text: 'Done' }, { at: minsAgo(229), kind: 'prompt', text: 'You asked: “rewrite the setup guide”' }],
      spans: [span(229, 185)] },
  ],
  recap: { title: '3 done. 1 needs you.', range: 'last 24 hours', items: [
    { id: 'api', hue: 1, name: 'api-server', state: 'done', stateText: 'Done 2:58 AM', headline: 'Added rate limiting and 12 tests, all passing.', chips: [{ text: '12 tests passing', tone: 'good' }, { text: '3 files changed', tone: 'neutral' }] },
    { id: 'pipeline', hue: 6, name: 'data-pipeline', state: 'done', stateText: 'Done 1:47 AM', headline: 'Fixed crash on empty CSV rows.', chips: [{ text: '2 files changed', tone: 'neutral' }] },
    { id: 'docs', hue: 7, name: 'docs-site', state: 'done', stateText: 'Done 12:36 AM', headline: 'Rewrote the setup guide.', chips: [{ text: '1 file changed', tone: 'neutral' }] },
  ] },
};

// /?demo=idle: the morning after, PC asleep. Long names and lines, to keep the layout honest.
const DEMO_IDLE = {
  updatedAt: minsAgo(151), title: 'Asleep when idle', line: 'Turns on by itself when Claude starts working.', icon: 'idle', tone: 'idle', holding: false,
  sleep: { state: 'asleep', since: minsAgo(151), afterWork: true, screen: 'off' },
  power: { lid: 'closed', source: 'ac', heat: 'normal' }, facts: 'Lid closed · Plugged in · Temperature fine',
  sessions: [
    { id: 'a', hue: 0, name: 'landing-page', folder: 'landing-page', state: 'needs', meta: '3:41 AM', lastActivityAt: minsAgo(213), line: 'Waiting for your approval to run npm install.' },
    { id: 'b', hue: 1, name: 'api-server', folder: 'api-server', state: 'done', meta: '2:58 AM', lastActivityAt: minsAgo(256), line: 'Added rate limiting and 12 tests, all passing.' },
    { id: 'c', hue: 6, name: 'data-pipeline', folder: 'data-pipeline', state: 'done', meta: '1:47 AM', lastActivityAt: minsAgo(327), line: 'Fixed crash on empty CSV rows.' },
    { id: 'd', hue: 7, name: 'A very long session title that has to be clipped neatly on a small phone', folder: 'docs-site', state: 'done', meta: '12:36 AM', lastActivityAt: minsAgo(398),
      line: 'Rewrote the setup guide, with the install steps, the first run and a troubleshooting section for the three questions people ask most often.' },
  ],
};

// ---------- main loop ----------
// No timer: the page updates when you open it, when a notification arrives, and when you tap Refresh.
let last = null;
let openId = null;

function draw() {
  const st = last?.status;
  const s = openId && (st?.sessions || []).find((x) => x.id === openId);
  if (s) screenSession(s, st);
  else screenMain(last);
}

function openSession(id) {
  openId = id;
  history.pushState({ id }, '');
  draw();
  scrollTo(0, 0);
}

function closeSession() {
  openId = null;
  draw();
}

addEventListener('popstate', (e) => {
  openId = e.state?.id || null;
  draw();
});

let refreshing = false;
async function refresh({ manual = false, quiet = false } = {}) {
  if (refreshing) return;
  refreshing = true;
  const button = document.getElementById('refresh');
  button?.classList.add('spinning');
  const started = Date.now();
  try {
    await render({ manual });
  } finally {
    // Let the arrow make at least one turn, so a fast refresh still reads as "done".
    const wait = Math.max(0, 600 - (Date.now() - started));
    setTimeout(() => { document.getElementById('refresh')?.classList.remove('spinning'); refreshing = false; }, wait);
    // A pull shows its own spinner, so it only speaks up when something's wrong.
    if (manual && (!quiet || navigator.onLine === false)) toast(navigator.onLine === false ? 'You\'re offline' : 'Up to date');
  }
}

async function render({ manual = false } = {}) {
  if (demo === 'main' || demo === 'session') {
    last = { paired: true, status: { ...DEMO_STATUS, updatedAt: new Date().toISOString() } };
    if (demo === 'session' && !openId) openId = new URLSearchParams(location.search).get('id') || 'landing';
    return draw();
  }
  if (demo === 'idle') { last = { paired: true, status: DEMO_IDLE }; return draw(); }
  if (demo === 'install') return screenInstall();
  if (demo === 'notify') return screenNotifications();
  if (!key) return screenNoKey();
  if (isIOS && !standalone) return screenInstall();
  const canNotify = 'Notification' in window && 'serviceWorker' in navigator && 'PushManager' in window;
  if (canNotify && Notification.permission !== 'granted' && !sessionStorage.getItem('vigil.skipNotify')) return screenNotifications();
  try {
    const data = await api('/api/status');
    // Phone was removed on the server side (e.g. disconnected from the PC): ask again.
    if (canNotify && Notification.permission === 'granted' && !data.paired && localStorage.getItem('vigil.notify') === 'on') {
      localStorage.removeItem('vigil.notify');
      return screenNotifications();
    }
    // A tap on Refresh restarts "Updated"; automatic updates keep the PC's own time.
    if (manual) checkedAt = Date.now();
    // Quiet checks only redraw when something changed, so the page never flickers under your thumb.
    const view = (d) => `${d?.paired}|${d?.status?.receivedAt}|${isLost(d?.status)}`;
    const same = !manual && last && view(last) === view(data);
    last = data;
    if (!same) draw();
  } catch (err) {
    if (String(err.message) === '401') return screenNoKey();
    if (!last) $app.innerHTML = `<p class="sub">Can't reach Vigil right now.</p><p style="margin-top:14px"><button class="btn" id="retry">Try again</button></p>`;
    const retry = document.getElementById('retry');
    if (retry) retry.onclick = () => refresh({ manual: true });
    throw err;
  }
}

// ---------- pull to refresh ----------
// Like Mail on iPhone: pull the page down from the top and a spinner fills in, tick by tick, in the
// space above it. Let go once it's full and the page stays open a moment while it refreshes, then
// slides back. Let go early and it just bounces back. On iPhone the page keeps its own bounce and we
// only watch how far it moved; elsewhere we move it ourselves, with the same rubber-band feel.
const PULL_AT = 70;  // how far the page has to come down
const HOLD = 54;     // where it rests while refreshing
const pull = document.createElement('div');
pull.className = 'pull';
pull.setAttribute('aria-hidden', 'true');
pull.innerHTML = `<svg viewBox="0 0 28 28">${Array.from({ length: 8 }, (_, i) =>
  `<rect x="12.7" y="1.5" width="2.6" height="7.5" rx="1.3" transform="rotate(${i * 45} 14 14)"/>`).join('')}</svg>`;
document.body.appendChild(pull);
const pullTicks = [...pull.querySelectorAll('rect')];
const safeTop = (() => {
  const probe = document.createElement('div');
  probe.style.cssText = 'position:fixed;top:0;height:env(safe-area-inset-top);visibility:hidden';
  document.body.appendChild(probe);
  const h = probe.getBoundingClientRect().height;
  probe.remove();
  return h;
})();

let pullFrom = null;   // finger's starting y, while a pull may be happening
let pullDist = 0;      // how far the page is down
let pullReady = false;
let pullBusy = false;

// The spinner rides in the gap above the page and never reaches the header.
function drawPull(d) {
  pullDist = d;
  const p = Math.min(1, d / PULL_AT);
  pull.style.transform = `translate(-50%, ${safeTop + d / 2 - 14}px)`;
  pull.style.opacity = d > 8 ? '1' : '0';
  if (!pullBusy) pullTicks.forEach((t, i) => { t.style.opacity = i < Math.round(p * 8) ? '1' : '0'; });
  if (!pullReady && d >= PULL_AT) { pullReady = true; navigator.vibrate?.(8); }
  else if (pullReady && d < PULL_AT - 6) pullReady = false;
}

const rubber = (x) => (1 - 1 / (x * 0.55 / innerHeight + 1)) * innerHeight;
const movePage = (y, ease) => {
  $app.classList.toggle('settle', ease);
  $app.style.transform = y ? `translateY(${y}px)` : '';
};

async function pullRefresh() {
  pullBusy = true;
  pull.classList.add('spinning');
  pullTicks.forEach((t) => { t.style.opacity = ''; });
  movePage(HOLD, true);
  drawPull(HOLD);
  const started = Date.now();
  try { await refresh({ manual: true, quiet: true }); } catch { /* the page shows its own error */ }
  await new Promise((r) => setTimeout(r, Math.max(0, 700 - (Date.now() - started))));
  pull.classList.add('closing');
  movePage(0, true);
  setTimeout(() => {
    pull.classList.remove('spinning', 'closing');
    pullBusy = false;
    pullReady = false;
    drawPull(0);
  }, 380);
}

addEventListener('touchstart', (e) => {
  // Only from the very top, one finger, on screens with a Refresh button, and not mid-refresh.
  if (pullBusy || refreshing || e.touches.length !== 1 || scrollY > 0 || !document.getElementById('refresh')) return;
  pullFrom = e.touches[0].clientY;
}, { passive: true });

addEventListener('touchmove', (e) => {
  if (pullFrom === null) return;
  if (isIOS) return drawPull(Math.max(0, -scrollY)); // the page bounces by itself; just follow it
  const dy = e.touches[0].clientY - pullFrom;
  if (dy <= 0) { if (pullDist) { movePage(0, false); drawPull(0); } return; }
  e.preventDefault();
  const d = rubber(dy);
  movePage(d, false);
  drawPull(d);
}, { passive: isIOS }); // on iPhone we only watch, so scrolling never waits on us

// iPhone keeps moving the page after the finger lifts; follow it back up.
addEventListener('scroll', () => { if (isIOS && !pullBusy && pullFrom === null && pullDist) drawPull(Math.max(0, -scrollY)); }, { passive: true });

function endPull() {
  if (pullFrom === null) return;
  pullFrom = null;
  if (pullReady) return pullRefresh();
  if (!isIOS) movePage(0, true);
  drawPull(isIOS ? Math.max(0, -scrollY) : 0);
}
addEventListener('touchend', endPull);
addEventListener('touchcancel', endPull);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
  navigator.serviceWorker.addEventListener('message', (e) => { if (e.data?.type === 'refresh') render().catch(() => {}); });
}
document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') render().catch(() => {}); });
// While the app is open, check quietly every 20 seconds, so it never sits on an old answer.
setInterval(() => {
  if (document.visibilityState === 'visible' && !refreshing && pullFrom === null && !pullBusy) render().catch(() => {});
}, 20000);
render().catch(() => {}).finally(() => { wake(); queueEyeFrame(); });
