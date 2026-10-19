// Shared helpers for Vigil's phone relay.
//
// The PC and the phone share one secret key (from the pairing QR code). The server never stores
// that key, only its SHA-256, which names the device's records.
//
// Storage, in two layers:
//  - Runtime Cache (fast, generous): the live status, rewritten on every change from the PC.
//  - Blob (durable, but only ~2,000 writes/month on the free plan, shared by every user): the phone's
//    push subscription, written once at pairing. Status goes there only if the cache is unavailable;
//    an evicted status comes back within 30 minutes, because every PC re-sends at least that often.
// Reads try the cache first and fall back to the blob.
import { createHash } from 'node:crypto';
import { put, get, del } from '@vercel/blob';
import { getCache } from '@vercel/functions';

const KEY_PATTERN = /^[A-Za-z0-9_-]{32,128}$/;
const CACHE_TTL_S = 3 * 24 * 3600;

export function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json', 'cache-control': 'no-store' },
  });
}

export function idFromKey(key) {
  return KEY_PATTERN.test(key || '') ? createHash('sha256').update(key).digest('hex') : null;
}

/** Reads "Authorization: Bearer <key>" and returns the device id, or null. */
export function deviceId(request) {
  const auth = request.headers.get('authorization') || '';
  return idFromKey(auth.startsWith('Bearer ') ? auth.slice(7).trim() : '');
}

const paths = {
  subscription: (id) => `devices/${id}/subscription.json`,
  status: (id) => `devices/${id}/status.json`,
};
const keys = {
  status: (id) => `status:${id}`,
  subscription: (id) => `sub:${id}`,
};

function cache() {
  try { return getCache({ namespace: 'vigil' }); } catch { return null; }
}

async function cacheGet(key) {
  try { return (await cache()?.get(key)) ?? null; } catch { return null; }
}

/** Returns false if the cache couldn't take it. */
async function cacheSet(key, value) {
  const c = cache();
  if (!c) return false;
  try { await c.set(key, value, { ttl: CACHE_TTL_S, name: '' }); return true; } catch { return false; }
}

async function cacheDelete(key) {
  try { await cache()?.delete(key); } catch { /* already gone */ }
}

async function blobRead(path) {
  try {
    const result = await get(path, { access: 'private', useCache: false });
    if (!result || result.statusCode !== 200) return null;
    return JSON.parse(await new Response(result.stream).text());
  } catch {
    return null;
  }
}

async function blobWrite(path, value) {
  await put(path, JSON.stringify(value), {
    access: 'private',
    allowOverwrite: true,
    addRandomSuffix: false,
    contentType: 'application/json',
    cacheControlMaxAge: 60,
  });
}

async function blobDelete(path) {
  try { await del(path); } catch { /* already gone */ }
}

// ---------- status ----------

export async function loadStatus(id) {
  const hot = await cacheGet(keys.status(id));
  if (hot) return hot;
  const cold = await blobRead(paths.status(id));
  if (cold) await cacheSet(keys.status(id), cold);
  return cold;
}

/** Updates the live copy. Writes the durable copy only when the cache is unavailable, to save blob writes. */
export async function saveStatus(id, status) {
  if (await cacheSet(keys.status(id), status)) return false;
  await blobWrite(paths.status(id), status);
  return true;
}

// ---------- push subscription ----------

export async function loadSubscription(id) {
  const hot = await cacheGet(keys.subscription(id));
  if (hot) return hot;
  const cold = await blobRead(paths.subscription(id));
  if (cold) await cacheSet(keys.subscription(id), cold);
  return cold;
}

export async function saveSubscription(id, record) {
  await blobWrite(paths.subscription(id), record);
  await cacheSet(keys.subscription(id), record);
}

export async function forgetDevice(id, { keepStatus = false } = {}) {
  await Promise.all([
    blobDelete(paths.subscription(id)),
    cacheDelete(keys.subscription(id)),
    ...(keepStatus ? [] : [blobDelete(paths.status(id)), cacheDelete(keys.status(id))]),
  ]);
}
