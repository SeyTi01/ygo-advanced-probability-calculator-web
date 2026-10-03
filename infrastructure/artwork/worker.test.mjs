import { test } from "node:test";
import assert from "node:assert/strict";
import { createHandler, validJpeg } from "./worker.mjs";

// Small structural JPEG fixture; tests exercise delivery/storage, not a live provider.
const jpeg = Uint8Array.from([255,216,255,192,0,11,8,1,135,1,12,1,1,17,0,
  255,218,0,8,1,1,0,0,63,0,1,2,3,255,0,4,255,217]);
class Bucket {
  objects = new Map();
  version = 0;
  async get(key) {
    const value = this.objects.get(key);
    if (!value) return null;
    return { etag: value.etag, httpEtag: `"${value.etag}"`, size: value.bytes.length,
      body: new Response(value.bytes).body,
      json: async () => JSON.parse(new TextDecoder().decode(value.bytes)) };
  }
  async head(key) { return this.get(key); }
  async put(key, bytes, options = {}) {
    const previous = this.objects.get(key);
    const condition = options.onlyIf;
    if (condition instanceof Headers && condition.get("If-None-Match") === "*" && previous) return null;
    if (condition?.etagMatches && previous?.etag !== condition.etagMatches) return null;
    this.objects.set(key, { bytes: typeof bytes === "string" ? new TextEncoder().encode(bytes) : new Uint8Array(bytes),
      etag: String(++this.version) });
    return this.get(key);
  }
}
async function setup(options = {}) {
  const bucket = new Bucket();
  await bucket.put("_control/manifest.json", JSON.stringify({ version: 1, variant: "small", ids: [1,2,3] }));
  let time = 100000, calls = [];
  const handlerOptions = { now: () => time, sleep: async ms => { time += ms; await new Promise(r => setImmediate(r)); },
    fetchImage: async (url, opts) => { calls.push({ url, opts, time }); return new Response(jpeg, { headers: { "Content-Type": "image/jpeg" } }); },
    ...options };
  const handler = createHandler(handlerOptions);
  const request = (path = "/small/1.jpg", method = "GET", selected = handler) =>
    selected.fetch(new Request("https://art.example" + path, { method }), { ARTWORK: bucket }, { waitUntil: promise => promise });
  return { bucket, calls, request, handlerOptions, advance: ms => time += ms };
}

test("retained image survives new clients, handler restart and edge cache eviction", async () => {
  const s = await setup();
  const cold = await s.request();
  assert.equal(cold.status, 200);
  assert.deepEqual(new Uint8Array(await cold.arrayBuffer()), jpeg);
  for (let i = 0; i < 3; i++) assert.equal((await s.request("/small/1.jpg", "GET", createHandler(s.handlerOptions))).status, 200);
  assert.equal(s.calls.length, 1);
  assert.equal(s.calls[0].url, "https://images.ygoprodeck.com/images/cards_small/1.jpg");
  assert.equal(s.calls[0].opts.redirect, "manual");
  assert.equal(cold.headers.get("Content-Type"), "image/jpeg");
  assert.match(cold.headers.get("Cache-Control"), /immutable/);
  assert.equal(cold.headers.get("Location"), null);
});
test("independent instances deduplicate concurrent cold misses with a persistent lease", async () => {
  const s = await setup();
  const results = await Promise.all(Array.from({length: 5}, () => s.request("/small/1.jpg", "GET", createHandler(s.handlerOptions))));
  assert.ok(results.every(r => r.status === 200));
  assert.equal(s.calls.length, 1);
});
test("different cold IDs obey the durable global interval", async () => {
  const s = await setup();
  assert.equal((await s.request()).status, 200);
  assert.equal((await s.request("/small/2.jpg")).status, 200);
  assert.ok(s.calls[1].time - s.calls[0].time >= 1100);
});
test("unsafe URLs, unlisted IDs, control objects, queries and writes never acquire", async () => {
  const s = await setup();
  for (const path of ["/small/4.jpg", "/small/0.jpg", "/small/01.jpg", "/small/2147483648.jpg", "/small/1.jpg?url=https://evil.test", "/_control/manifest.json", "/https://evil.test/a.jpg"])
    assert.equal((await s.request(path)).status, 404);
  assert.equal((await s.request("/small/1.jpg", "PUT")).status, 405);
  assert.equal((await s.request("/small/1.jpg", "HEAD")).status, 404);
  assert.equal(s.calls.length, 0);
});
test("provider image IDs beyond eight digits do not invalidate the allowlist", async () => {
  const s = await setup();
  await s.bucket.put("_control/manifest.json", JSON.stringify({ version: 1, variant: "small", ids: [1,100000101] }));
  assert.equal((await s.request("/small/100000101.jpg")).status, 200);
  assert.equal(s.calls.length, 1);
});
test("known upstream 404 is retained across restarts without repeated fetching", async () => {
  let calls = 0;
  const s = await setup({ fetchImage: async () => { calls++; return new Response(null, {status: 404}); } });
  for (let i = 0; i < 3; i++) assert.equal((await s.request("/small/1.jpg", "GET", createHandler(s.handlerOptions))).status, 404);
  assert.equal(calls, 1);
});
test("server cooldown is durable and warm artwork remains available", async () => {
  let calls = 0;
  const s = await setup({ fetchImage: async () => { calls++; return new Response(null, {status: 429, headers:{"Retry-After":"3600"}}); } });
  assert.equal((await s.request()).headers.get("Retry-After"), "3600");
  s.advance(60000);
  assert.equal((await s.request("/small/2.jpg", "GET", createHandler(s.handlerOptions))).status, 503);
  assert.equal(calls, 1);
  await s.bucket.put("small/3.jpg", jpeg);
  assert.equal((await s.request("/small/3.jpg")).status, 200);
});
for (const [name, response] of [
  ["redirect", () => new Response(null, {status:302,headers:{Location:"https://evil.test/image.jpg"}})],
  ["HTML", () => new Response("error", {headers:{"Content-Type":"text/html"}})],
  ["truncated JPEG", () => new Response(jpeg.slice(0,-2), {headers:{"Content-Type":"image/jpeg"}})],
  ["oversized JPEG", () => new Response(new Uint8Array(262145), {headers:{"Content-Type":"image/jpeg"}})],
  ["timeout", () => { throw new Error("timeout"); }]
]) test(`${name} never publishes a partial object and enters backoff`, async () => {
  let calls = 0;
  const s = await setup({fetchImage:async () => {calls++; return response();}});
  assert.equal((await s.request()).status, 503);
  assert.equal(await s.bucket.get("small/1.jpg"), null);
  assert.equal((await s.request()).status, 503);
  assert.equal(calls, 1);
});
test("storage failure preserves existing copies and cannot return an unretained download", async () => {
  const s = await setup();
  await s.bucket.put("small/2.jpg", jpeg);
  const put = s.bucket.put.bind(s.bucket);
  s.bucket.put = async (key,...args) => { if (key === "small/1.jpg") throw new Error("storage full"); return put(key,...args); };
  assert.equal((await s.request()).status, 503);
  assert.equal((await s.request("/small/2.jpg")).status, 200);
  assert.equal(await s.bucket.get("small/1.jpg"), null);
});
test("JPEG validation rejects malformed segments and truncated content", () => {
  assert.ok(validJpeg(jpeg));
  assert.equal(validJpeg(jpeg.slice(0,-3)), false);
  const invalid = jpeg.slice(); invalid[4] = 255; invalid[5] = 255;
  assert.equal(validJpeg(invalid), false);
});

test("a crashed owner's lease blocks briefly then permits recovery", async () => {
  const s = await setup();
  await s.bucket.put("_control/acquisition.json", JSON.stringify({owner:"crashed",leaseUntil:130000,nextAllowed:101100,failures:0}));
  assert.equal((await s.request()).status, 503);
  assert.equal(s.calls.length, 0);
  s.advance(30000);
  assert.equal((await s.request()).status, 200);
  assert.equal(s.calls.length, 1);
});
test("an expired acquisition cannot publish or release another owner's lease", async () => {
  const s = await setup();
  const newer = JSON.stringify({owner:"newer",leaseUntil:200000,nextAllowed:171100,failures:0});
  const handler = createHandler({...s.handlerOptions,fetchImage:async () => {
    s.advance(30000);
    await s.bucket.put("_control/acquisition.json", newer);
    return new Response(jpeg,{headers:{"Content-Type":"image/jpeg"}});
  }});
  assert.equal((await s.request("/small/1.jpg","GET",handler)).status, 503);
  assert.equal(await s.bucket.get("small/1.jpg"), null);
  assert.deepEqual(await (await s.bucket.get("_control/acquisition.json")).json(),JSON.parse(newer));
});
test("an evictable edge cache never replaces durable retention", async () => {
  const s = await setup();
  const entries = new Map();
  const cache = {match:async request => entries.get(request.url)?.clone() || null,
    put:async (request,response) => { await response.arrayBuffer(); entries.set(request.url,new Response(jpeg)); }};
  const handler = createHandler({...s.handlerOptions,cache});
  assert.equal((await s.request("/small/1.jpg","GET",handler)).status, 200);
  await new Promise(r=>setImmediate(r));
  assert.equal((await s.request("/small/1.jpg","GET",handler)).status, 200);
  entries.clear();
  assert.equal((await s.request("/small/1.jpg","GET",createHandler({...s.handlerOptions,cache}))).status, 200);
  assert.equal(s.calls.length, 1);
  const head = await s.request("/small/1.jpg","HEAD");
  assert.equal(head.status,200);
  assert.equal(await head.text(),"");
});

function assertPublicImage(response, status = 200) {
  assert.equal(response.status, status);
  assert.equal(response.headers.get("Cache-Control"), "public, max-age=31536000, immutable");
  assert.equal(response.headers.get("Access-Control-Allow-Origin"), "*");
  assert.equal(response.headers.get("Access-Control-Allow-Credentials"), null);
  assert.equal(response.headers.get("Timing-Allow-Origin"), "*");
  assert.equal(response.headers.get("Cross-Origin-Resource-Policy"), "cross-origin");
  assert.equal(response.headers.get("Content-Type"), "image/jpeg");
  for (const name of ["ETag", "Retry-After", "Age", "Date", "CF-Cache-Status"])
    assert.ok(response.headers.get("Access-Control-Expose-Headers").split(", ").includes(name));
}

test("retained bytes and validators survive one-year expiry and deployment without upstream reads or writes", async () => {
  const s = await setup();
  await s.bucket.put("small/1.jpg", jpeg);
  const original = structuredClone(s.bucket.objects);
  const request = (method, headers = {}) => createHandler(s.handlerOptions).fetch(
    new Request("https://art.example/small/1.jpg", { method, headers }),
    { ARTWORK: s.bucket }, { waitUntil: () => assert.fail("no edge cache configured") });
  const first = await request("GET");
  const etag = first.headers.get("ETag");
  assertPublicImage(first);
  assert.deepEqual(new Uint8Array(await first.arrayBuffer()), jpeg);
  for (const days of [364, 1, 1]) {
    s.advance(days * 86400000);
    const hit = await request("GET");
    assertPublicImage(hit);
    assert.equal(hit.headers.get("ETag"), etag);
    assert.deepEqual(new Uint8Array(await hit.arrayBuffer()), jpeg);
    const head = await request("HEAD");
    assertPublicImage(head);
    assert.equal(await head.text(), "");
    for (const condition of [etag, `W/${etag}`, `"other", W/${etag}`, "*"]) {
      for (const method of ["GET", "HEAD"]) {
        const unchanged = await request(method, { "If-None-Match": condition });
        assertPublicImage(unchanged, 304);
        assert.equal(await unchanged.text(), "");
        assert.equal(unchanged.headers.get("ETag"), etag);
      }
    }
    assert.equal((await request("GET", { "If-None-Match": '"other"' })).status, 200);
  }
  assert.equal(s.calls.length, 0);
  assert.deepEqual(s.bucket.objects, original);
});

test("old edge entries receive new headers and conditionals never poison the cached body", async () => {
  const s = await setup();
  const stored = new Response(jpeg, { headers: { "Cache-Control": "public, max-age=604800, immutable",
    ETag: '"retained"', Age: "120", Date: "Sat, 03 Oct 2026 12:00:00 GMT" } });
  let puts = 0;
  const cache = {
    match: async key => {
      assert.equal(key.url, "https://art.example/small/1.jpg");
      assert.equal(key.headers.get("If-None-Match"), null);
      return stored.clone();
    },
    put: async () => { puts++; }
  };
  s.bucket.get = s.bucket.head = async () => assert.fail("edge hit must not read R2");
  const handler = createHandler({ ...s.handlerOptions, cache });
  const request = (headers = {}) => handler.fetch(new Request("https://art.example/small/1.jpg", { headers }),
    { ARTWORK: s.bucket }, { waitUntil: () => assert.fail("edge hit must not replace cache") });
  const conditional = await request({ "If-None-Match": 'W/"retained"' });
  assertPublicImage(conditional, 304);
  assert.equal(await conditional.text(), "");
  const complete = await request();
  assertPublicImage(complete);
  assert.equal(complete.headers.get("Age"), "120");
  assert.equal(complete.headers.get("Date"), "Sat, 03 Oct 2026 12:00:00 GMT");
  assert.deepEqual(new Uint8Array(await complete.arrayBuffer()), jpeg);
  assert.equal(puts, 0);
  assert.equal(s.calls.length, 0);
});

test("a conditional R2 miss never inserts an empty 304 into edge storage", async () => {
  const s = await setup();
  await s.bucket.put("small/1.jpg", jpeg);
  const etag = (await s.bucket.get("small/1.jpg")).httpEtag;
  const writes = [];
  const pending = [];
  const handler = createHandler({ ...s.handlerOptions, cache: {
    match: async () => null,
    put: async (key, value) => writes.push({ key: key.url, status: value.status,
      bytes: new Uint8Array(await value.arrayBuffer()), headers: value.headers })
  }});
  const request = headers => handler.fetch(new Request("https://art.example/small/1.jpg", { headers }),
    { ARTWORK: s.bucket }, { waitUntil: p => pending.push(p) });
  assertPublicImage(await request({ "If-None-Match": etag }), 304);
  assert.equal(writes.length, 0);
  assertPublicImage(await request({ "If-None-Match": '"other"' }));
  await Promise.all(pending);
  assert.equal(writes.length, 1);
  assert.equal(writes[0].status, 200);
  assert.deepEqual(writes[0].bytes, jpeg);
  assert.equal(writes[0].headers.get("Cache-Control"), "public, max-age=31536000, immutable");
  assert.equal(s.calls.length, 0);
});

test("public preflight permits conditional reads without R2 access or acquisition", async () => {
  const s = await setup();
  s.bucket.get = s.bucket.head = s.bucket.put = async () => assert.fail("preflight must not touch R2");
  const handler = createHandler(s.handlerOptions);
  const request = (path, method) => handler.fetch(new Request("https://art.example" + path, {
    method: "OPTIONS", headers: { Origin: "https://calculator.example",
      "Access-Control-Request-Method": method, "Access-Control-Request-Headers": "if-none-match" }
  }), { ARTWORK: s.bucket }, { waitUntil: () => assert.fail("no preflight caching") });
  for (const method of ["GET", "HEAD"]) {
    const response = await request("/small/1.jpg", method);
    assert.equal(response.status, 204);
    assert.equal(response.headers.get("Access-Control-Allow-Origin"), "*");
    assert.equal(response.headers.get("Access-Control-Allow-Methods"), "GET, HEAD");
    assert.equal(response.headers.get("Access-Control-Allow-Headers"), "If-None-Match");
    assert.equal(response.headers.get("Cache-Control"), "no-store");
  }
  assert.equal((await request("/small/1.jpg", "PUT")).status, 405);
  for (const path of ["/_control/manifest.json", "/small/1.jpg?x=1", "/small/01.jpg"]) {
    const response = await request(path, "GET");
    assert.equal(response.status, 404);
    assert.equal(response.headers.get("Access-Control-Allow-Origin"), null);
  }
  assert.equal(s.calls.length, 0);
});

test("public image failures expose retry information but remain non-cacheable", async () => {
  const s = await setup({ fetchImage: async () => new Response(null, {
    status: 429, headers: { "Retry-After": "3600" }
  }) });
  for (const [path, status, retry] of [["/small/1.jpg", 503, "3600"], ["/small/4.jpg", 404, "60"]]) {
    const response = await s.request(path);
    assert.equal(response.status, status);
    assert.equal(response.headers.get("Cache-Control"), "no-store");
    assert.equal(response.headers.get("Retry-After"), retry);
    assert.equal(response.headers.get("Access-Control-Allow-Origin"), "*");
    assert.equal(response.headers.get("Timing-Allow-Origin"), "*");
    assert.ok(response.headers.get("Access-Control-Expose-Headers").includes("Retry-After"));
  }
  assert.equal(await s.bucket.get("small/1.jpg"), null);
});
