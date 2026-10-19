// The public half of the push key pair. Safe to share; the phone needs it to subscribe.
import { json } from './_lib.js';

export function GET() {
  return json({ publicKey: process.env.VAPID_PUBLIC_KEY || null });
}
