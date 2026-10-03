# Card artwork delivery

The calculator shows small full-card thumbnails automatically beside deck-card names, with a larger preview in the expanded editor. An IntersectionObserver includes a 160px near-viewport margin and prioritizes visible rows from top to bottom. Both metadata and image requests wait for visibility. Manual cards keep an aligned quiet placeholder without a lookup. Neither local storage nor exported sessions contain image bytes or provider image URLs; saved-session schema v2 is unchanged.

The shared page loader deduplicates external-passcode metadata and selected-artwork image URLs across thumbnail/preview consumers. It permits two metadata resolutions, four image deliveries and 64 pending jobs, with 256 completed entries in each display cache. Unused queued jobs/timers are dropped, the last departing image consumer aborts its request, and stale component generations cannot update replaced rows. An already-started shared metadata request may finish (with a 15-second HTTP timeout), but cannot start image bytes or retries after its consumers leave. Image delivery has a 25-second timeout. Artwork resolves only the requested passcode using the existing metadata cache, including stale validated artwork IDs; it never initializes the full catalog. New artwork-only JSON request starts are spaced at least 100ms apart; validated cache hits remain immediate. Artwork HTTP/storage does not hold the import/enrichment gate; import/session enrichment retains its existing behavior.

Authoritative missing metadata is cached; transient metadata failures are evicted and can retry. Each visible automatic job has at most three attempts, with 60/120-second backoff and exposed metadata `Retry-After` honored. A delay above ten minutes ends the automatic job rather than retrying early. The unchanged Worker permits cross-origin image display, but does **not** expose HTTP status or `Retry-After` to browser JavaScript via CORS. `Image.onerror` cannot distinguish its 404, 429, lease-contention or 5xx responses: image errors use the same bounded conservative backoff and unchanged cacheable URL. The Worker still enforces its durable missing markers/cooldown on every attempt. No CORS or Worker rollout is required; extended operator cooldowns can leave a quiet placeholder after the automatic attempt budget is exhausted. Reopening a successfully resolved preview reuses the shared source and normal browser HTTP cache.

## Infrastructure

`worker.mjs` runs separately from the calculator. Its only binding is `ARTWORK`, the dedicated Standard R2 bucket `ygo-calculator-card-artwork`. The bucket itself is private. The Worker accepts only GET and HEAD for numeric small-image paths; control objects and public writes are inaccessible. No client or source credentials are needed. Deploying this infrastructure does not publish the calculator.

The Worker reads `_control/manifest.json`, prepared from official image metadata. On a cold GET it acquires one validated small JPEG from the fixed provider host, publishes it to R2, and then serves the retained object. It never forwards unretained bytes or redirects to the provider. Successful copies have no expiry; the optional Workers Cache API and browser HTTP cache can be evicted without reacquisition. Every subsequent visitor and a new Worker deployment use R2 first.

An R2 conditional-write lease serializes all cold acquisition across instances. Starts are at least 1.1 seconds apart, with bounded contention, a 10-second upstream timeout, a 30-second fenced lease, and durable exponential backoff respecting `Retry-After`. A 404 creates a durable missing marker. Redirects, HTML, oversized bodies, malformed/truncated JPEG structure and persistence failures fail closed. The JPEG validator checks dimensions, segment bounds, scan and terminal markers; browser decoding failures retain the aligned quiet placeholder.

These are conservative project limits, not a provider promise of a safe image rate. Follow the [YGOPRODeck API guide](https://ygoprodeck.com/api-guide/): download, retain and re-host artwork; do not hotlink, repeatedly download, or burst image requests. Card artwork remains third-party material and is not licensed by the calculator's software license.

## Updating the allowlist

Run manually when new cards are needed, reusing an existing official catalog response where possible:

```sh
python infrastructure/artwork/prepare-manifest.py --catalog catalog.json --output manifest.json
# Omitting --catalog makes one official JSON catalog request, never image requests.
```

Upload the output to `_control/manifest.json` through the authenticated R2 dashboard. Review the count and budget first. Allowlist changes take up to five minutes to reach existing Worker instances. New IDs outside the allowlist remain unavailable until this update. Do not clear existing images during refreshes, builds, deploys or metadata TTL expiry. A confirmed repaired provider 404 requires an operator to remove only its corresponding `_control/missing/{id}.json` marker; do not bulk-clear negative records or the global cooldown.

The authenticated dashboard was used for initial deployment. For subsequent operator deployments, `wrangler.jsonc` supplies the same dedicated bucket binding. Use official Wrangler with authenticated operator access; keep credentials in its appropriate credential/secret store. Enable runtime caching and keep persistent invocation logs disabled as in the initial deployment. Do not enable paid Workers plans, image transformations or other add-ons.

## Budget and verification

Standard R2 includes 10 GB-month storage, one million Class A operations and ten million Class B operations monthly. Workers Free includes 100,000 requests daily. Check current [R2 pricing](https://developers.cloudflare.com/r2/pricing/) and [Workers limits](https://developers.cloudflare.com/workers/platform/limits/) before changing operation volume; account allowances are shared. R2 is usage-billed, so these allowances are not a spending cap. Edge hits avoid R2 reads; retained misses use one R2 read, while cold acquisitions need additional reads and conditional writes.

The initial allowlist contains 14,767 image IDs. The 256 KiB per-image limit gives a 3,871,080,448-byte upper bound for that catalog, not a measured catalog size. No bulk binary synchronization runs during CI, builds or ordinary imports. Check actual bucket objects/storage and Class A/B usage in the R2 dashboard, and request counts in Workers metrics. Storage totals can lag object listings. Account email usage alerts are configured at 8,000,000,000 stored bytes, 800,000 Class A operations and 8,000,000 Class B operations, with a separate $1 billing budget alert. These provide notice, not enforcement or a spending cap. Stop and review before bulk operations or changes expected to exceed included allowances.

Offline tests never contact the provider:

```sh
node --test scripts/artwork/scheduler.test.mjs
node --test infrastructure/artwork/worker.test.mjs
python infrastructure/artwork/test_manifest.py
```

Live verification should acquire only a few representative images, then reuse them. Inspect browser network requests to confirm that image traffic uses the project endpoint. Do not treat deterministic fixtures as proof of deployed delivery.
