/* DIRECO Service Worker — offline shell + IndexedDB SOS outbox + background sync.
   Bumped on every meaningful change to force update. */
const SW_VERSION = 'direco-sw-v5-2026-05-02';
const STATIC_CACHE = `static-${SW_VERSION}`;
const SHELL_URLS = [
  '/manifest.webmanifest',
  '/icons/icon.svg'
];

// ---------------- IndexedDB outbox helpers (used in fetch & sync) ----------------
const DB_NAME = 'direco';
const DB_VER = 1;
const STORE = 'sosOutbox';

function openDb() {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VER);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) {
        db.createObjectStore(STORE, { keyPath: 'clientId' });
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function outboxPut(item) {
  const db = await openDb();
  return new Promise((res, rej) => {
    const tx = db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).put(item);
    tx.oncomplete = () => res();
    tx.onerror = () => rej(tx.error);
  });
}

async function outboxAll() {
  const db = await openDb();
  return new Promise((res, rej) => {
    const tx = db.transaction(STORE, 'readonly');
    const r = tx.objectStore(STORE).getAll();
    r.onsuccess = () => res(r.result || []);
    r.onerror = () => rej(r.error);
  });
}

async function outboxDelete(clientId) {
  const db = await openDb();
  return new Promise((res, rej) => {
    const tx = db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).delete(clientId);
    tx.oncomplete = () => res();
    tx.onerror = () => rej(tx.error);
  });
}

// ---------------- Lifecycle ----------------
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(STATIC_CACHE)
      .then(c => c.addAll(SHELL_URLS).catch(() => { /* ignore individual misses */ }))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(keys.filter(k => k !== STATIC_CACHE).map(k => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

// ---------------- Fetch routing ----------------
self.addEventListener('fetch', (event) => {
  const req = event.request;
  const url = new URL(req.url);

  // Don't try to cache websockets / Blazor circuit / SignalR / SSE
  if (req.method !== 'GET') return; // POSTs are handled directly via fetch in pwa.js

  // Stale-while-revalidate for our static shell
  if (url.origin === self.location.origin) {
    if (SHELL_URLS.includes(url.pathname) || url.pathname.startsWith('/icons/')) {
      event.respondWith(staleWhileRevalidate(req));
      return;
    }
    // Network-first for the home page so updates appear, with cache fallback offline
    if (url.pathname === '/' || url.pathname === '/index.html') {
      event.respondWith(networkFirst(req));
      return;
    }
  }
});

async function staleWhileRevalidate(req) {
  const cache = await caches.open(STATIC_CACHE);
  const cached = await cache.match(req);
  const networkPromise = fetch(req).then(resp => {
    if (resp && resp.ok) cache.put(req, resp.clone());
    return resp;
  }).catch(() => cached);
  return cached || networkPromise;
}

async function networkFirst(req) {
  try {
    const resp = await fetch(req);
    const cache = await caches.open(STATIC_CACHE);
    cache.put(req, resp.clone());
    return resp;
  } catch {
    const cached = await caches.match(req);
    return cached || new Response('<h1>Offline</h1><p>You are offline. SOS reports will be queued and sent automatically when you are back online.</p>',
      { headers: { 'Content-Type': 'text/html' } });
  }
}

// ---------------- Background Sync — drain outbox ----------------
self.addEventListener('sync', (event) => {
  if (event.tag === 'sos-flush') {
    event.waitUntil(flushOutbox());
  }
});

// Manual trigger from the page (e.g. when navigator.onLine flips back to true)
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'sos-flush') {
    event.waitUntil(flushOutbox());
  }
});

async function flushOutbox() {
  const items = await outboxAll();
  if (!items.length) return;

  const apiBase = (self.DRC_API || '').replace(/\/$/, '');
  let sent = 0, failed = 0;

  for (const item of items) {
    try {
      const resp = await fetch(`${apiBase}/api/sos`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(item)
      });
      if (resp.ok) {
        await outboxDelete(item.clientId);
        sent++;
      } else {
        failed++;
      }
    } catch {
      failed++;
      // stop on first network error so we retry on next sync
      break;
    }
  }

  // Tell pages
  const clients = await self.clients.matchAll({ includeUncontrolled: true });
  for (const c of clients) {
    c.postMessage({ type: 'sos-flushed', sent, failed, remaining: (await outboxAll()).length });
  }
}
