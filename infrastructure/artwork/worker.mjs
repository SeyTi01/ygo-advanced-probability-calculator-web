// Browser delivery and controlled acquisition are deliberately separate.
// R2 retains originals; the optional edge cache can be discarded at any time.
const manifestKey = "_control/manifest.json";
const lockKey = "_control/acquisition.json";
const maxImageBytes = 262144;
const maxImages = 30000;
const intervalMs = 1100;
const leaseMs = 30000;

export function validJpeg(bytes) {
  if (bytes.length < 20 || bytes.length > maxImageBytes || bytes[0] !== 255 || bytes[1] !== 216 ||
      bytes.at(-2) !== 255 || bytes.at(-1) !== 217) return false;
  let offset = 2, dimensions = false, scan = false;
  while (offset < bytes.length - 2) {
    if (bytes[offset++] !== 255) return false;
    while (bytes[offset] === 255) offset++;
    const marker = bytes[offset++];
    if (marker === 217) return dimensions && scan && offset === bytes.length;
    if (marker === 0 || marker === 216) return false;
    const length = (bytes[offset] << 8) | bytes[offset + 1];
    if (length < 2 || offset + length > bytes.length - 2) return false;
    if ([192, 193, 194].includes(marker)) {
      const height = (bytes[offset + 3] << 8) | bytes[offset + 4];
      const width = (bytes[offset + 5] << 8) | bytes[offset + 6];
      if (length < 8 || width < 100 || width > 512 || height < 150 || height > 768) return false;
      dimensions = true;
    }
    offset += length;
    if (marker === 218) {
      scan = true;
      // Scan entropy until a real marker (stuffed FF00 and restart markers are data).
      while (offset < bytes.length - 2) {
        if (bytes[offset] === 255 && bytes[offset + 1] !== 0 &&
            !(bytes[offset + 1] >= 208 && bytes[offset + 1] <= 215)) break;
        offset += bytes[offset] === 255 ? 2 : 1;
      }
    }
  }
  return dimensions && scan && offset === bytes.length - 2;
}

function unavailable(status = 503, retry = 60) {
  return new Response(null, { status, headers: {
    "Cache-Control": "no-store", "Retry-After": String(retry),
    "X-Content-Type-Options": "nosniff"
  }});
}

export function createHandler({ fetchImage = fetch, now = Date.now,
  sleep = ms => new Promise(resolve => setTimeout(resolve, ms)),
  cache = null, token = () => crypto.randomUUID() } = {}) {
  let manifest;
  let manifestLoadedAt = -Infinity;

  async function allowed(bucket, id) {
    if (!manifest || now() - manifestLoadedAt > 300000) {
      const object = await bucket.get(manifestKey);
      if (!object) return false;
      const data = await object.json();
      if (data.version !== 1 || data.variant !== "small" || !Array.isArray(data.ids) ||
          !data.ids.length || data.ids.length > maxImages ||
          data.ids.some(value => !Number.isInteger(value) || value < 1 || value > 2147483647)) return false;
      manifest = new Set(data.ids);
      manifestLoadedAt = now();
    }
    return manifest.has(id);
  }

  function serve(object, method, request) {
    const headers = new Headers({ "Content-Type": "image/jpeg",
      "Cache-Control": "public, max-age=604800, immutable",
      "X-Content-Type-Options": "nosniff", "Cross-Origin-Resource-Policy": "cross-origin",
      "ETag": object.httpEtag });
    if (request.headers.get("If-None-Match") === object.httpEtag)
      return new Response(null, { status: 304, headers });
    headers.set("Content-Length", String(object.size));
    return new Response(method === "HEAD" ? null : object.body, { headers });
  }

  async function acquire(bucket, id, key) {
    const missingKey = `_control/missing/${id}.json`;
    if (await bucket.head(missingKey)) return unavailable(404);
    for (let attempt = 0; attempt < 8; attempt++) {
      // A competing request may already have published this retained copy.
      const retained = await bucket.get(key);
      if (retained) return retained;
      const previous = await bucket.get(lockKey);
      const state = previous ? await previous.json() : { leaseUntil: 0, nextAllowed: 0, failures: 0 };
      const startedAt = now();
      if (state.nextAllowed > startedAt && !state.leaseUntil) {
        const remaining = state.nextAllowed - startedAt;
        if (remaining > intervalMs) return unavailable(503, Math.ceil(remaining / 1000));
        await sleep(remaining); continue;
      }
      if (state.leaseUntil > startedAt) { await sleep(intervalMs); continue; }
      const owner = token();
      const locked = await bucket.put(lockKey, JSON.stringify({ ...state, owner,
        leaseUntil: startedAt + leaseMs, nextAllowed: startedAt + intervalMs }), {
        onlyIf: previous ? { etagMatches: previous.etag } : new Headers({ "If-None-Match": "*" }),
        httpMetadata: { contentType: "application/json" }, storageClass: "Standard"
      });
      if (!locked) { await sleep(intervalMs); continue; }
      let cooldown = intervalMs, failures = state.failures;
      try {
        const existing = await bucket.get(key);
        if (existing) return existing;
        if (await bucket.head(missingKey)) return unavailable(404);
        // Numeric IDs must have been validated against official metadata by the operator.
        // No browser-supplied URL, redirect or arbitrary upstream is ever followed.
        const response = await fetchImage(`https://images.ygoprodeck.com/images/cards_small/${id}.jpg`, {
          redirect: "manual", signal: AbortSignal.timeout(10000)
        });
        if (response.status === 404) {
          await bucket.put(missingKey, JSON.stringify({ status: 404, observedAt: now() }), {
            httpMetadata: { contentType: "application/json" }, storageClass: "Standard"
          });
          return unavailable(404);
        }
        if (!response.ok) {
          const retry = response.headers.get("Retry-After");
          const seconds = retry && /^\d+$/.test(retry) ? Number(retry) : 0;
          const date = retry && !/^\d+$/.test(retry) ? Date.parse(retry) : NaN;
          cooldown = Math.max(60000 * 2 ** Math.min(failures++, 6), seconds * 1000,
            Number.isFinite(date) ? date - now() : 0);
          return unavailable(503, Math.ceil(cooldown / 1000));
        }
        if (response.headers.get("Content-Type")?.split(";")[0].trim() !== "image/jpeg" ||
            Number(response.headers.get("Content-Length") || 0) > maxImageBytes) throw new Error("Invalid image");
        const reader = response.body.getReader();
        const chunks = [];
        let size = 0;
        try {
          for (;;) {
            const { done, value } = await reader.read();
            if (done) break;
            size += value.length;
            if (size > maxImageBytes) throw new Error("Oversized image");
            chunks.push(value);
          }
        } finally { await reader.cancel(); }
        const bytes = new Uint8Array(size);
        let offset = 0;
        for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
        if (!validJpeg(bytes)) throw new Error("Incomplete image");
        const current = await bucket.head(lockKey);
        if (current?.etag !== locked.etag || now() >= startedAt + leaseMs) return unavailable();
        await bucket.put(key, bytes, { onlyIf: new Headers({ "If-None-Match": "*" }),
          httpMetadata: { contentType: "image/jpeg", cacheControl: "public, max-age=604800, immutable" },
          sha256: await crypto.subtle.digest("SHA-256", bytes), storageClass: "Standard" });
        failures = 0;
        return await bucket.get(key) || unavailable();
      } catch {
        cooldown = 60000 * 2 ** Math.min(failures++, 6);
        return unavailable(503, Math.ceil(cooldown / 1000));
      } finally {
        // Compare-and-swap prevents an expired owner from releasing a newer owner's lease.
        await bucket.put(lockKey, JSON.stringify({ owner, leaseUntil: 0,
          nextAllowed: Math.max(startedAt + intervalMs, now() + (cooldown > intervalMs ? cooldown : 0)), failures }), {
          onlyIf: { etagMatches: locked.etag }, httpMetadata: { contentType: "application/json" },
          storageClass: "Standard"
        }).catch(() => {});
      }
    }
    return unavailable(503, 2);
  }

  return {
    async fetch(request, env, ctx) {
      const url = new URL(request.url);
      if (!["GET", "HEAD"].includes(request.method))
        return new Response(null, { status: 405, headers: { Allow: "GET, HEAD" } });
      const match = /^\/small\/([1-9]\d{0,9})\.jpg$/.exec(url.pathname);
      if (!match || Number(match[1]) > 2147483647 || url.search) return unavailable(404);
      const key = `small/${match[1]}.jpg`;
      try {
        const cached = request.method === "GET" && cache && await cache.match(request);
        if (cached) return cached;
        let object = request.method === "HEAD" ? await env.ARTWORK.head(key) : await env.ARTWORK.get(key);
        if (!object && request.method === "GET") {
          if (!await allowed(env.ARTWORK, Number(match[1]))) return unavailable(404);
          object = await acquire(env.ARTWORK, Number(match[1]), key);
          if (object instanceof Response) return object;
        }
        if (!object) return unavailable(404);
        const response = serve(object, request.method, request);
        if (cache && response.status === 200 && request.method === "GET")
          ctx.waitUntil(cache.put(request, response.clone()).catch(() => {}));
        return response;
      } catch { return unavailable(); }
    }
  };
}

let productionHandler;
export default { fetch(request, env, ctx) {
  productionHandler ??= createHandler({ cache: caches.default });
  return productionHandler.fetch(request, env, ctx);
}};
