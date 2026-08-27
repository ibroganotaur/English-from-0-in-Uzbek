/* ===========================================================================
   Service worker — this is what makes the course work with no internet and
   lets it be installed to a phone's home screen.

   Strategy is deliberately network-first for the page itself: she should get
   new lessons whenever there is a signal, and fall back to the cached copy
   only when there isn't. Icons and the manifest barely change, so those are
   served cache-first.
   =========================================================================== */

var VERSION = "noldan-v4";
var SHELL = [
  "./",
  "./index.html",
  "./manifest.json",
  "./icon-192.png",
  "./icon-512.png"
];

self.addEventListener("install", function (e) {
  e.waitUntil(
    caches.open(VERSION)
      // addAll fails the whole install if any single file 404s, so add them
      // individually and let the page still install if an icon is missing.
      .then(function (c) {
        return Promise.all(SHELL.map(function (url) {
          return c.add(url).catch(function () { /* skip */ });
        }));
      })
      .then(function () { return self.skipWaiting(); })
  );
});

self.addEventListener("activate", function (e) {
  e.waitUntil(
    caches.keys()
      .then(function (keys) {
        return Promise.all(keys.map(function (k) {
          return k === VERSION ? null : caches.delete(k);
        }));
      })
      .then(function () { return self.clients.claim(); })
  );
});

self.addEventListener("fetch", function (e) {
  var req = e.request;

  // Never touch anything but our own GETs — no cross-origin, no POSTs.
  if (req.method !== "GET") return;
  if (new URL(req.url).origin !== self.location.origin) return;

  var isPage = req.mode === "navigate" ||
               (req.headers.get("accept") || "").indexOf("text/html") !== -1;

  if (isPage) {
    // Network first: fresh lessons win, cache is the safety net.
    e.respondWith(
      fetch(req)
        .then(function (res) {
          var copy = res.clone();
          caches.open(VERSION).then(function (c) { c.put("./index.html", copy); });
          return res;
        })
        .catch(function () {
          return caches.match("./index.html").then(function (hit) {
            return hit || caches.match("./");
          });
        })
    );
    return;
  }

  // Everything else: cache first, then network, and remember what we fetched.
  e.respondWith(
    caches.match(req).then(function (hit) {
      return hit || fetch(req).then(function (res) {
        var copy = res.clone();
        caches.open(VERSION).then(function (c) { c.put(req, copy); });
        return res;
      }).catch(function () { return hit; });
    })
  );
});
