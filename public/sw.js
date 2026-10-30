// Bewaart de app en alle geluiden in de cache, zodat het bord blijft werken als de wifi
// tijdens de act wegvalt. Eerst het netwerk, zodat updates direct verschijnen als je online bent.
const CACHE = 'monster-soundboard-v2';
const SHELL = ['./', 'index.html', 'style.css', 'app.js', 'sounds.json', 'manifest.webmanifest', 'icon.svg', 'icon-192.png'];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE);
    await cache.addAll(SHELL);
    const sounds = await (await fetch('sounds.json')).json();
    await cache.addAll(sounds.map((s) => s.file));
    self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    for (const key of await caches.keys()) if (key !== CACHE) await caches.delete(key);
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    try {
      const res = await fetch(event.request);
      if (res.ok) cache.put(event.request, res.clone());
      return res;
    } catch (e) {
      const hit = await cache.match(event.request, { ignoreSearch: true });
      if (hit) return hit;
      throw e;
    }
  })());
});
