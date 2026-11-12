# Card-data maintenance

Use for reviewed catalog/name refreshes or a new locale's card-name baseline. Read the relevant source, identity, coverage and artifact contracts in `repo:infrastructure/card-catalog/README.md`. Run commands from the repository root; choose only the workflow needed. Keep raw provider responses/archives outside the app and commits; retain compact projections and compilation/rights-holder credits.

## Monthly refresh, retry or replay

- After a workflow/code fix, start a new **Run workflow** for `card-data-refresh.yml` on `dev`; **Re-run jobs** retains the old workflow definition. Set `source_run_id` to an earlier refresh run to reuse its inputs, or leave it blank for fresh acquisition.
- For a retry without code changes, **Re-run jobs** reuses that run's completed checkpoint. Preserve historical `card-data-refresh-RUN_ID` artifacts before their 90-day expiry; an expired replay requires a new acquisition, never a silent replacement baseline.
- For local acquisition, choose a new nonexistent writable directory outside tracked source. For offline replay, restore the retained artifact and run only generation:

```sh
python infrastructure/card-catalog/fetch-refresh.py --output <source-directory>
node infrastructure/card-catalog/refresh-card-data.mjs <source-directory>
```

Review `repo:infrastructure/card-catalog/snapshots/refresh/audit.json`: canonical and per-locale changes, hashes, candidate evidence, unresolved identities, membership differences/overlaps, provider conflicts and example coverage. Review new candidate evidence before explicitly changing `archetype-policy.json` or reviewed clauses; regenerate afterward.

Reconcile the branch before retrying a changed base or failed push lease. After a local promotion I/O failure, discard only generated refresh files and replay. Legitimate removals require a separate reviewed manual refresh with exact-ID removal files, never a lowered baseline. If GitHub disabled the schedule for inactivity, re-enable it and use the manual trigger to catch up. The automation's PR remains subject to ordinary review; a refresh does not authorize a release.

## Manual canonical catalog repair

Regenerate from a retained official response without network access:

```sh
node infrastructure/card-catalog/prepare-catalog.mjs --catalog cardinfo.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --audit membership-audit.json
```

Omitting `--catalog` makes one full metadata request with `misc=yes`; `--save-source <cardinfo.json>` retains its exact bytes for later replay. Always supply `--previous <shipped-catalog>` when output moves. Reviewed `--allow-removals <file>` permits only the listed canonical IDs; never lower coverage baselines or accept removals merely to pass a partial response.

If candidate vocabulary needs changing, acquire the candidate endpoint separately, review additions/removals and actual English name references, then edit `archetype-policy.json` and regenerate. Review membership additions/removals and overlaps across the full source before publication; zero unresolved recognized declarations does not prove the absence of unsupported wording or localization anomalies.

## Manual localized-name repair or first baseline

Use the manual generator for an explicitly reviewed repair/source migration, historical snapshot reproduction or a new locale. Routine registered-locale refreshes use the complete bundle workflow above.

For a new name locale, first prepare its `cardNames.asset` entry in `repo:YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json` so the generator recognizes it. Ship that registration and its reviewed baseline together; monthly refresh requires the baseline. UI satellite/locale setup follows [localization](localization.md).

```sh
node infrastructure/card-catalog/prepare-localized-names.mjs --locale <locale> --snapshot <name-projection.json> --catalog YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --example YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.<locale>.v1.json --audit <audit.json>
```

For historical reproduction, select the retained dated projection/audit under `repo:infrastructure/card-catalog/snapshots/`: German uses `names.de.yaml-yugi.2026-10-31.*`; Japanese, French, Italian and Portuguese use `names.<locale>.yaml-yugi.2026-11-01.*`. These are historical inputs, not the latest monthly bundle.

For a source refresh, acquire or reuse the pinned bundle's `yaml-cards.json` and read its immutable `revision` and `retrievedAt` from `sources.json`. Verify the name/ID projection against that revision, add `--provider <yaml-cards.json> --revision <commit-sha> --retrieved-at <timestamp>`, and choose new dated snapshot/audit paths.

Keep the shipped asset as the coverage baseline; add `--previous <shipped-asset>` when output moves. Only a new locale's first generation may use `--initial`, which rejects an existing output, a previous baseline or removal exemptions. Reviewed removals use `--allow-removals <file>`, a JSON array of exactly the accepted canonical IDs; other removals must still fail. Review per-locale identity, normalization, missing-name and example coverage before publication.

## Verification and related artwork

Run the relevant generator tests offline; the full catalog set includes localized-name validation:

```sh
node --test infrastructure/card-catalog/*.test.mjs
```

If the task also refreshes artwork, reuse the acquired `cardinfo.json` through [artwork operations](artwork.md); catalog refresh alone does not require an artwork update.
