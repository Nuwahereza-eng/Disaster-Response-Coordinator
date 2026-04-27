/* DIRECO PWA bootstrap — registers SW, exposes window.drcPwa, renders SOS FAB. */
(function () {
  const API = (window.DRC_API || '').replace(/\/$/, '');

  // -------- Service worker registration --------
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('/sw.js').then((reg) => {
        console.info('[DIRECO] Service worker registered:', reg.scope);
        // Pass API base to the SW (it can't read window directly).
        navigator.serviceWorker.ready.then((r) => {
          if (r.active) r.active.postMessage({ type: 'config', apiBase: API });
        });
      }).catch((e) => console.warn('[DIRECO] SW register failed:', e));

      navigator.serviceWorker.addEventListener('message', (ev) => {
        if (ev.data && ev.data.type === 'sos-flushed') {
          showToast(`✅ ${ev.data.sent} queued SOS report(s) delivered.`, 'success');
          updateOutboxBadge(ev.data.remaining);
        }
      });
    });

    window.addEventListener('online', () => {
      showToast('🛜 Back online — sending queued SOS reports…', 'info');
      requestSync();
    });
    window.addEventListener('offline', () => {
      showToast('📡 Offline — SOS reports will be queued.', 'warn');
    });
  }

  // -------- IndexedDB outbox (mirror of SW) --------
  const DB_NAME = 'direco', DB_VER = 1, STORE = 'sosOutbox';

  function openDb() {
    return new Promise((res, rej) => {
      const r = indexedDB.open(DB_NAME, DB_VER);
      r.onupgradeneeded = () => {
        const db = r.result;
        if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: 'clientId' });
      };
      r.onsuccess = () => res(r.result);
      r.onerror = () => rej(r.error);
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
  async function outboxCount() {
    const db = await openDb();
    return new Promise((res, rej) => {
      const tx = db.transaction(STORE, 'readonly');
      const r = tx.objectStore(STORE).count();
      r.onsuccess = () => res(r.result);
      r.onerror = () => rej(r.error);
    });
  }

  function uuid() {
    if (crypto && crypto.randomUUID) return crypto.randomUUID();
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  // -------- Geolocation (best-effort, fast timeout) --------
  function getLocation(timeoutMs) {
    return new Promise((resolve) => {
      if (!navigator.geolocation) return resolve(null);
      let done = false;
      const t = setTimeout(() => { if (!done) { done = true; resolve(null); } }, timeoutMs || 4000);
      navigator.geolocation.getCurrentPosition(
        (pos) => { if (done) return; done = true; clearTimeout(t);
          resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude });
        },
        () => { if (done) return; done = true; clearTimeout(t); resolve(null); },
        { enableHighAccuracy: true, timeout: timeoutMs || 4000, maximumAge: 60000 }
      );
    });
  }

  // -------- Submit / queue --------
  async function fireSos(type, opts) {
    opts = opts || {};
    const loc = await getLocation(3500);
    const item = {
      clientId: uuid(),
      type: (type || 'SOS').toUpperCase(),
      severity: opts.severity || 'High',
      location: opts.location || (loc ? `${loc.latitude.toFixed(5)},${loc.longitude.toFixed(5)}` : null),
      latitude: loc ? loc.latitude : null,
      longitude: loc ? loc.longitude : null,
      description: opts.description || null,
      phone: opts.phone || null,
      occurredAt: new Date().toISOString()
    };

    // Vibrate to confirm tap
    try { if (navigator.vibrate) navigator.vibrate([90, 50, 90]); } catch {}

    // Try direct send first
    try {
      const resp = await fetch(`${API}/api/sos`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(item)
      });
      if (resp.ok) {
        const data = await resp.json().catch(() => ({}));
        showToast(`🚨 ${item.type} SOS sent. Help is being coordinated.`, 'success');
        return { sent: true, ...data };
      }
      throw new Error('non-OK ' + resp.status);
    } catch (e) {
      // Offline / failed — queue + register sync
      await outboxPut(item);
      const remaining = await outboxCount();
      updateOutboxBadge(remaining);
      requestSync();
      showToast(`📡 No connection — SOS queued (${remaining} pending). Will send when you are online.`, 'warn');
      return { sent: false, queued: true, clientId: item.clientId };
    }
  }

  function requestSync() {
    if (!('serviceWorker' in navigator)) return;
    navigator.serviceWorker.ready.then((reg) => {
      if (reg.sync && reg.sync.register) {
        reg.sync.register('sos-flush').catch(() => sendFlushMsg(reg));
      } else {
        sendFlushMsg(reg);
      }
    });
  }
  function sendFlushMsg(reg) {
    if (reg && reg.active) reg.active.postMessage({ type: 'sos-flush' });
  }

  // -------- Floating SOS button --------
  function injectFab() {
    if (document.getElementById('drc-sos-fab')) return;
    const wrap = document.createElement('div');
    wrap.id = 'drc-sos-fab';
    wrap.innerHTML = `
      <button class="drc-sos-main" type="button" aria-label="Open SOS menu">
        <span class="drc-sos-ring"></span>
        <span class="drc-sos-label">SOS</span>
        <span class="drc-sos-badge" id="drc-outbox-badge" hidden>0</span>
      </button>
      <div class="drc-sos-menu" hidden>
        <button data-type="FIRE"     class="drc-sos-opt drc-sos-fire">🔥 Fire</button>
        <button data-type="MEDICAL"  class="drc-sos-opt drc-sos-med">🏥 Medical</button>
        <button data-type="FLOOD"    class="drc-sos-opt drc-sos-flood">🌊 Flood / Landslide</button>
      </div>
    `;
    document.body.appendChild(wrap);

    const main = wrap.querySelector('.drc-sos-main');
    const menu = wrap.querySelector('.drc-sos-menu');
    main.addEventListener('click', () => {
      menu.hidden = !menu.hidden;
      main.classList.toggle('open', !menu.hidden);
    });
    wrap.querySelectorAll('.drc-sos-opt').forEach((b) => {
      b.addEventListener('click', async () => {
        const type = b.dataset.type;
        menu.hidden = true; main.classList.remove('open');
        const ok = window.confirm(
          `Send a ${type} emergency report now?\n\n` +
          `Your location will be attached if available. You will get an SMS / WhatsApp confirmation.`
        );
        if (!ok) return;
        await fireSos(type);
      });
    });

    // Auto-launch via shortcut: /?sos=FIRE
    try {
      const params = new URLSearchParams(location.search);
      const auto = params.get('sos');
      if (auto) {
        setTimeout(() => fireSos(auto), 600);
        params.delete('sos');
        history.replaceState({}, '', location.pathname + (params.toString() ? '?' + params.toString() : ''));
      }
    } catch {}

    // Show queued count on load
    outboxCount().then(updateOutboxBadge).catch(() => {});
  }

  function updateOutboxBadge(n) {
    const el = document.getElementById('drc-outbox-badge');
    if (!el) return;
    if (!n || n <= 0) { el.hidden = true; el.textContent = '0'; }
    else { el.hidden = false; el.textContent = String(n > 99 ? '99+' : n); }
  }

  // -------- Toast --------
  function showToast(text, kind) {
    let host = document.getElementById('drc-toast-host');
    if (!host) {
      host = document.createElement('div');
      host.id = 'drc-toast-host';
      document.body.appendChild(host);
    }
    const t = document.createElement('div');
    t.className = 'drc-toast drc-toast-' + (kind || 'info');
    t.textContent = text;
    host.appendChild(t);
    setTimeout(() => { t.classList.add('drc-toast-fade'); }, 3500);
    setTimeout(() => { t.remove(); }, 4500);
  }

  // -------- Public API --------
  window.drcPwa = {
    fireSos,
    queuedCount: outboxCount,
    flushNow: requestSync
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', injectFab);
  } else {
    injectFab();
  }
})();
