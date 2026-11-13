# Plugin validation and publication

From the repository root:

```sh
node plugins/ygo-work/scripts/package.mjs --check
node --test plugins/ygo-work/scripts/package.test.mjs
node plugins/ygo-work/scripts/package.mjs /absolute/writable/path/ygo-work.tar.gz
```

The packager checks source, links and final archive. Run its tests when packaging code changes. Keep archives outside the plugin directory and out of commits. Validate realistic routing as well as syntax: initialized Windows, cloud publication, limited release requests and installed-package paths.

Check module boundaries: callers state concrete loading triggers; each selected module applies in full. Extract instructions with a different trigger into their own reference instead of linking to a section of a broader file. Keep shared rules in one owner and reuse loaded modules.

Load additional checks by the changed behavior:

| Trigger | Checks |
| --- | --- |
| Browser/computer authorization or routing changed | [Browser authorization checks](plugin-browser-checks.md) |
| Release workflow or routing changed | [Release workflow checks](plugin-release-checks.md) |

Publish the canonical source through Plugin Creator's update workflow; never edit an installed cache as another source of truth. Preserve stored identity, presentation, integrations and audience, synchronize manifest versions, and read back changed files after publication. Remove deleted packaged paths explicitly so old instructions do not survive an overlay update.
