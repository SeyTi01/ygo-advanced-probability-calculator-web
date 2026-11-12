# Plugin validation and publication

From the repository root:

```sh
node plugins/ygo-work/scripts/package.mjs --check
node --test plugins/ygo-work/scripts/package.test.mjs
node plugins/ygo-work/scripts/package.mjs /absolute/writable/path/ygo-work.tar.gz
```

The packager checks source, links and final archive. Run its tests when packaging code changes. Keep archives outside the plugin directory and out of commits. Validate realistic routing as well as syntax: initialized Windows, cloud publication, limited release requests and installed-package paths.

For release-workflow edits, walk through a new complete release and retries after each completed stage. Check notes-only/preparation-only limits; missing CLI PATH versus unavailable authentication; pending/failed checks and changed heads; an existing matching or mismatched tag; a draft or already-published Release; concurrent `main`/latest changes; and a missing/failed production deployment. Verify that custom notes never gain an automatic changelog inventory, the predecessor never becomes the new release itself, and the weakest optional highlight is removed. Use read-only current state and hypothetical scenarios; do not create a real release to test instructions.

Publish the canonical source through Plugin Creator's update workflow; never edit an installed cache as another source of truth. Preserve stored identity, presentation, integrations and audience, synchronize manifest versions, and read back changed files after publication. Remove deleted packaged paths explicitly so old instructions do not survive an overlay update.
