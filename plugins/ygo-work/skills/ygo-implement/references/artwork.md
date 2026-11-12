# Artwork operations

Use for artwork allowlist, Worker or storage maintenance. Read the relevant resource/cache/budget contracts in `repo:infrastructure/artwork/README.md`; [infrastructure](infrastructure.md) owns authenticated changes and rollback preparation. Run commands from the repository root.

## Allowlist refresh or repaired image

Reuse an existing official catalog response where possible:

```sh
python infrastructure/artwork/prepare-manifest.py --catalog catalog.json --output manifest.json
```

Omitting `--catalog` makes one official JSON catalog request, never image requests. Review the generated count and storage bound, then upload to `_control/manifest.json` through authenticated R2 access. Existing Worker instances can take five minutes to see it; IDs outside the allowlist remain unavailable.

Preserve retained images through refreshes, builds, deployments and metadata expiry. For a confirmed repaired provider 404, remove only its `_control/missing/{id}.json` marker; do not bulk-clear negative records or the global cooldown.

## Deployment or volume changes

Use the authenticated route selected by [infrastructure](infrastructure.md), using official Wrangler when CLI access is available. Preserve `repo:infrastructure/artwork/wrangler.jsonc`'s dedicated bucket binding and enabled runtime cache; keep persistent invocation logs disabled. Keep credentials in the supported credential/secret store. Do not enable paid Workers plans, image transformations or other add-ons.

Before changing operation volume, check current Cloudflare R2 pricing, Workers limits, actual bucket storage/Class A/Class B usage and Worker request counts. Account allowances are shared; alerts and included allowances are not spending caps. Review bulk operations or changes expected to exceed allowances before proceeding.

## Verification

For artwork code/generator changes, use the applicable offline tests (none contacts the provider):

```sh
node --test scripts/artwork/scheduler.test.mjs
node --test infrastructure/artwork/worker.test.mjs
python infrastructure/artwork/test_manifest.py
```

For live delivery, acquire only a few representative images and reuse them. Inspect browser requests for the project endpoint; deterministic fixtures do not prove deployed delivery.
