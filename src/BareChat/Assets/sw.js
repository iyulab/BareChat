"use strict";
// BareChat Service Worker — offline app-shell cache for the PWA profile (?shell=pwa).
// Scope is confined to the mount prefix so the add-on never intercepts host requests.
// Live data (/api, /hub) is always network: only the static shell is cached.

const BASE = "{{BASE}}";
const VERSION = "{{VERSION}}";  // asset content hash injected at serve time → auto cache-bust on change
const CACHE = "barechat-shell-" + VERSION;

// Static app shell precached on install. Canonical entry is BASE + "/" (SW-controlled URL).
const SHELL = [
  BASE + "/",
  BASE + "/app.css",
  BASE + "/app.js",
  BASE + "/signalr.min.js",
  BASE + "/manifest.webmanifest",
  BASE + "/branding/icon.svg"
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

function isLiveData(url) {
  return url.pathname.startsWith(BASE + "/api/") || url.pathname.startsWith(BASE + "/hub");
}

self.addEventListener("fetch", (event) => {
  const req = event.request;
  if (req.method !== "GET") return;                  // uploads / negotiate POST → network
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;   // cross-origin → default
  if (isLiveData(url)) return;                        // live chat data → network only

  if (req.mode === "navigate") {
    // Navigations: network-first, fall back to the cached shell when offline.
    event.respondWith(
      fetch(req).catch(() => caches.match(BASE + "/", { ignoreSearch: true }))
    );
    return;
  }

  // Static assets: cache-first, populate the cache on a network hit.
  event.respondWith(
    caches.match(req).then((cached) =>
      cached || fetch(req).then((res) => {
        if (res.ok) {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put(req, copy));
        }
        return res;
      }).catch(() => cached)
    )
  );
});

// ---------- Web Push (wake-up) ----------
self.addEventListener("push", (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = {}; }
  const title = data.title || "New message";
  event.waitUntil(self.registration.showNotification(title, {
    body: data.body || "",
    tag: data.tag || data.channelId || "barechat",
    data: { channelId: data.channelId || "" },
    icon: BASE + "/branding/icon.svg",
    badge: BASE + "/branding/icon.svg"
  }));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const channelId = (event.notification.data && event.notification.data.channelId) || "";
  const target = BASE + "/?shell=pwa";
  event.waitUntil(self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
    for (const client of clients) {
      if (client.url.indexOf(self.location.origin + BASE) === 0) {
        if (channelId && "postMessage" in client) client.postMessage({ type: "focusChannel", channelId });
        return "focus" in client ? client.focus() : undefined;
      }
    }
    return self.clients.openWindow(channelId ? target : target);
  }));
});
