// "Hey Siri, Claude status": one or two plain sentences, made to be read aloud.
// Used by an Apple Shortcut (Get Contents of URL → Speak Text). Plain text, no JSON.
import { idFromKey, loadStatus } from './_lib.js';

function text(body, status = 200) {
  return new Response(body, { status, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' } });
}

function minutesAgo(iso) {
  const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (!Number.isFinite(m) || m < 1) return 'just now';
  if (m === 1) return 'a minute ago';
  if (m < 60) return `${m} minutes ago`;
  const h = Math.round(m / 60);
  return h === 1 ? 'an hour ago' : `${h} hours ago`;
}

export async function GET(request) {
  const url = new URL(request.url);
  const auth = request.headers.get('authorization') || '';
  const key = auth.startsWith('Bearer ') ? auth.slice(7).trim() : url.searchParams.get('k') || '';
  const id = idFromKey(key);
  if (!id) return text('Vigil is not connected. Open Vigil on your PC to set it up.', 401);

  const st = await loadStatus(id);
  if (!st) return text('Vigil has not heard from your PC yet.');

  const parts = [`${st.title}. ${st.line}`];
  const sessions = st.sessions || [];
  const needs = sessions.filter((s) => s.state === 'needs');
  const working = sessions.filter((s) => s.state === 'working');
  for (const s of needs.slice(0, 2)) parts.push(`${s.name} needs you: ${s.line}.`);
  for (const s of working.slice(0, 2)) parts.push(`${s.name}: ${String(s.line || 'working').replace(/^\w/, (c) => c.toLowerCase())}.`);
  if (!needs.length && !working.length && st.recap?.items?.length) {
    const done = st.recap.items.filter((i) => i.state === 'done').slice(0, 2);
    for (const d of done) parts.push(`${d.name}: ${d.headline}`);
  }
  parts.push(`Updated ${minutesAgo(st.updatedAt || st.receivedAt)}.`);
  return text(parts.join(' ').replace(/\s+/g, ' ').replace(/\.\./g, '.'));
}
