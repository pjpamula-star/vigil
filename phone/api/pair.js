// The phone calls this once, after you allow notifications: it hands over its push subscription.
import { deviceId, json, saveSubscription, forgetDevice } from './_lib.js';

export async function POST(request) {
  const id = deviceId(request);
  if (!id) return json({ error: 'bad key' }, 401);

  let body;
  try { body = await request.json(); } catch { return json({ error: 'bad json' }, 400); }
  const sub = body?.subscription;
  if (!sub?.endpoint || !sub?.keys?.p256dh || !sub?.keys?.auth) return json({ error: 'bad subscription' }, 400);
  // Only real push services (Apple, Google, Mozilla, Microsoft).
  const host = new URL(sub.endpoint).hostname;
  if (!/(push\.apple\.com|googleapis\.com|mozilla\.com|notify\.windows\.com)$/.test(host)) return json({ error: 'unknown push service' }, 400);

  await saveSubscription(id, {
    subscription: { endpoint: sub.endpoint, keys: { p256dh: sub.keys.p256dh, auth: sub.keys.auth } },
    pairedAt: new Date().toISOString(),
    device: String(body.device || '').slice(0, 80),
  });
  return json({ ok: true });
}

/** "Disconnect this phone" from either side: deletes everything stored for it. */
export async function DELETE(request) {
  const id = deviceId(request);
  if (!id) return json({ error: 'bad key' }, 401);
  await forgetDevice(id);
  return json({ ok: true });
}
