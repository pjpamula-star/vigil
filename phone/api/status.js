// The phone app reads the latest status; Vigil on the PC checks whether pairing finished.
import { deviceId, json, loadStatus, loadSubscription } from './_lib.js';

export async function GET(request) {
  const id = deviceId(request);
  if (!id) return json({ error: 'bad key' }, 401);
  const [sub, status] = await Promise.all([loadSubscription(id), loadStatus(id)]);
  return json({ paired: !!sub?.subscription, pairedAt: sub?.pairedAt || null, status: status || null, now: new Date().toISOString() });
}
