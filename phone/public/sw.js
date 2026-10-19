// Vigil's service worker: shows notifications and opens the right place when you tap one.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = { title: 'Vigil', body: event.data?.text() || '' }; }
  const title = data.title || 'Vigil';
  event.waitUntil((async () => {
    await self.registration.showNotification(title, {
      body: data.body || '',
      tag: data.tag || 'vigil',
      renotify: true,                 // a repeat still alerts instead of silently replacing
      requireInteraction: !!data.urgent, // stays on screen where the platform allows it
      silent: false,
      timestamp: data.at || Date.now(),
      icon: '/icon-192.png',
      badge: '/icon-192.png',
      data: { url: data.url || '/' },
    });
    // Red number on the Home Screen icon = sessions waiting on you.
    if (typeof data.badge === 'number' && self.navigator.setAppBadge) {
      try { data.badge > 0 ? await self.navigator.setAppBadge(data.badge) : await self.navigator.clearAppBadge(); } catch {}
    }
    // If the app is open, refresh it right away.
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const w of windows) w.postMessage({ type: 'refresh' });
  })());
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = event.notification.data?.url || '/';
  event.waitUntil((async () => {
    if (url.startsWith('https://claude.ai/')) return self.clients.openWindow(url);
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    if (windows.length) {
      windows[0].postMessage({ type: 'refresh' });
      return windows[0].focus();
    }
    return self.clients.openWindow('/');
  })());
});
