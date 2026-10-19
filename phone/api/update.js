// Vigil on the PC calls this: save the latest status, and optionally send a notification.
import webpush from 'web-push';
import { deviceId, json, saveStatus, loadSubscription, forgetDevice } from './_lib.js';

const MAX_BODY_BYTES = 256 * 1024; // 12 sessions with full details is about 70 KB at most

export async function POST(request) {
  const id = deviceId(request);
  if (!id) return json({ error: 'bad key' }, 401);

  const text = await request.text();
  if (text.length > MAX_BODY_BYTES) return json({ error: 'too big' }, 413);
  let body;
  try { body = JSON.parse(text); } catch { return json({ error: 'bad json' }, 400); }

  const n = body.notify && typeof body.notify.title === 'string' ? body.notify : null;
  let durable = false;
  if (body.status && typeof body.status === 'object') {
    durable = await saveStatus(id, { ...body.status, receivedAt: new Date().toISOString() });
  }

  let pushed = false;
  if (n) {
    const record = await loadSubscription(id);
    if (record?.subscription) {
      webpush.setVapidDetails(process.env.VAPID_SUBJECT, process.env.VAPID_PUBLIC_KEY, process.env.VAPID_PRIVATE_KEY);
      const payload = JSON.stringify({
        title: n.title.slice(0, 120),
        body: String(n.body || '').slice(0, 400),
        tag: String(n.tag || 'vigil').slice(0, 64),
        url: typeof n.url === 'string' && n.url.startsWith('https://') ? n.url : '/',
        urgent: !!n.urgent,
        badge: Number.isInteger(n.badge) && n.badge >= 0 ? Math.min(n.badge, 99) : undefined,
        at: Date.now(),
      });
      try {
        // Always "high": Apple delivers right away instead of batching for battery,
        // and web.push.apple.com has been known to reject "normal" (BadUrgency).
        await webpush.sendNotification(record.subscription, payload, { TTL: 12 * 3600, urgency: 'high' });
        pushed = true;
      } catch (err) {
        // 404/410: the phone removed the app or turned notifications off. Forget the subscription.
        if (err?.statusCode === 404 || err?.statusCode === 410) await forgetDevice(id, { keepStatus: true });
        return json({ ok: true, durable, pushed: false, error: `push failed (${err?.statusCode || 'network'})` });
      }
    }
  }
  return json({ ok: true, durable, pushed });
}
