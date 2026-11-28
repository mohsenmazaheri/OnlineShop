/*
 * PWA service worker.
 * - Pages (navigations): network first, never cached — prices and stock must always be fresh for users and crawlers.
 *   Without a network, the precached offline page for the visitor's locale is shown.
 * - Build assets (/_next/static/, content-hashed): cache first.
 * - API calls and everything else: not touched.
 */
const VERSION = "v1";
const STATIC_CACHE = `static-${VERSION}`;
const OFFLINE_CACHE = `offline-${VERSION}`;

const params = new URL(self.location.href).searchParams;
const locales = (params.get("locales") || "fa").split(",").filter(Boolean);
const offlineUrl = (locale) => `/${locale}/offline`;

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(OFFLINE_CACHE)
      .then((cache) => cache.addAll(locales.map(offlineUrl)))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== STATIC_CACHE && k !== OFFLINE_CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request).catch(async () => {
        const first = url.pathname.split("/")[1];
        const locale = locales.includes(first) ? first : locales[0];
        return (await caches.match(offlineUrl(locale))) || Response.error();
      }),
    );
    return;
  }

  if (url.pathname.startsWith("/_next/static/")) {
    event.respondWith(
      caches.open(STATIC_CACHE).then(async (cache) => {
        const cached = await cache.match(request);
        if (cached) return cached;
        const response = await fetch(request);
        if (response.ok) cache.put(request, response.clone());
        return response;
      }),
    );
  }
});

/*
 * Notifications (Web Push, optional): the API sends {title, body, url} encrypted for this browser. A click opens the
 * shop's page (only the shop's own pages), or focuses it when it is already open.
 */
self.addEventListener("push", (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { title: event.data ? event.data.text() : "" };
  }
  event.waitUntil(
    self.registration.showNotification(data.title || "", {
      body: data.body || "",
      icon: "/icon/192",
      badge: "/icon/192",
      data: { url: typeof data.url === "string" ? data.url : "/" },
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const url = new URL((event.notification.data && event.notification.data.url) || "/", self.location.origin);
  if (url.origin !== self.location.origin) return;
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      const open = windows.find((w) => w.url === url.href && "focus" in w);
      return open ? open.focus() : self.clients.openWindow(url.href);
    }),
  );
});
