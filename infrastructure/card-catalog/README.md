# Local card search catalog

## Monthly reviewed refreshes

`.github/workflows/card-data-refresh.yml` runs on `dev` at 04:23 UTC on the
third day of each month, or through **Run workflow**. It updates one
`codex/card-data-refresh` PR against `dev`; it never merges or releases it.
The Cloudflare Pages project excludes this exact branch from preview builds.
Other branches and the `main` production branch retain their existing behavior.
The workflow needs repository permission for Actions to create pull requests.
It uses the explicit public `github-actions[bot]` identity through the publication
guard, and dispatches `tests.yml` explicitly because token-authored pushes do
not trigger its push event. No Cloudflare secret is required.

Manual acquisition and generation use the same commands as the schedule:

```sh
python infrastructure/card-catalog/fetch-refresh.py --output /tmp/card-data-sources
node infrastructure/card-catalog/refresh-card-data.mjs /tmp/card-data-sources
```

Choose a new, nonexistent source directory. Acquisition downloads one full
YGOPRODeck `misc=yes` response and candidate vocabulary, then resolves YAML Yugi
to an immutable commit and downloads its archive. Only individual OCG/TCG card
records and series vocabulary are read; no upstream code executes. The name
adapter retains the existing password/fake-password and unique-artwork join rules.
YAML Yugi is the sole translation source. The raw archive and canonical response
stay in the source directory, outside the app and committed projections.

Download the run's `card-data-refresh-RUN_ID` artifact for offline replay with
the second command. Source hashes and sizes are verified before generation.
Artifacts have 90-day retention; download needed historical inputs before expiry.
YAML Yugi remains retrievable by pinned revision; exact historical YGOPRODeck
responses cannot be recovered from its moving endpoint after artifact expiry.
Permanent private R2 archival is a separate future option, not provisioned here.
Normal builds, tests and app startup never run acquisition.

Review `snapshots/refresh/audit.json`: canonical additions/changes, German label
changes, source hashes, candidate additions/removals and source evidence,
unresolved identity declarations, membership differences/overlaps, provider
conflicts and example-session coverage. `names.de.json` is the compact retained
name input. Generated timestamps and irrelevant upstream fields do not create
repeat PRs. Only German is currently enabled; future locale support must extend
the retained projection and invoke the same validation per locale.

Some upstream responses move a primary ID to another artwork. An established
canonical ID survives only if exactly one source record owns that numeric artwork
ID and its English name/type are unchanged. Every such change is audited.
Names alone never join identities; ambiguous owners or changed identities fail.
Unexplained card/label removals fail, as do more than 25 newly unresolved cards
or a German coverage-ratio drop over 0.5 percentage points. Existing null/empty
membership semantics and all reviewed eligibility/name policies remain intact.

The bot never writes policy files. Review new candidate evidence, update
`archetype-policy.json` or reviewed clauses explicitly, and regenerate. Existing
reviewer commits are preserved when the automation branch rebases onto `dev`;
conflicts or publication-identity failures stop rather than overwriting edits.
If the base changes during generation or a push lease fails, rerun after
reconciling the branch. Source/validation failures leave the remote branch and
published data intact. A local I/O failure during file promotion can leave a
partial working-tree bundle: discard only generated refresh files and replay;
the workflow never commits after a failed step. Legitimate removals require a
separate reviewed manual refresh using the generators' exact-ID removal files,
never a lowered baseline. GitHub may delay schedules or disable them after 60
days of repository inactivity; re-enable and use the manual trigger to catch up.

## Archetype source assessment (2026-11-01)

The bounded evidence is retained in
`snapshots/archetype-source-assessment.2026-11-01.json`: the first 20 newly seen
eligible IDs in provider order plus eight existing identity/mention edge cases.
The response contained 14,648 records and 36 newly seen eligible IDs. Nine
apparent missing identities were primary-artwork changes with unique numeric
links, not evidence to remove their established canonical identities.

The comparison supports retaining the reviewed English-name/permanent-text
resolver. For example, Aleister's provider association is Invoked while its
resolved name membership is Aleister; Dramatic Rescue's provider association is
Amazoness while its resolved membership is empty. Summoned Skull's permanent
Archfiend declaration, Hardened Armed Dragon's exclusion, Double Iris Magician's
overlapping memberships, and Ocean/Substitute/Lemuria name substitutions remain
covered. The controlled refresh fixture proves that a new Lightsworn card gains
membership, while an unknown permanent family remains null and becomes
classifiable only after an explicit candidate rule is supplied.

[YAML Yugi](https://github.com/DawnbrandBots/yaml-yugi#aggregations), pinned to
`86c467a8e14e633a6210458905f5a479c4836405`, offers multilingual series vocabulary,
not an authoritative per-card gameplay membership map.
[YGOJSON's schema](https://github.com/iconmaster5326/YGOJSON/blob/main/schema/v1/series.json)
does distinguish archetypes and explicit member UUIDs, but its published
`v1/individual` head `ab5016e14cc6c3296a85d52de2c8b61b9db55cfa` is still dated
2026-04-07. It cannot establish new-card coverage for this refresh. No empirical
cross-dataset membership parity is claimed.
[Ignis databases](https://github.com/ProjectIgnis/BabelCDB) introduce setcode
decoding, separate prerelease IDs and another redistribution assessment; no
observed gap justifies adding that dependency here. No external pipeline code
is copied. Existing YAML Yugi compilation credit and rights-holder attribution
apply. New spelling, aliases, match rules and disputed memberships remain human
review decisions; the monthly bot publishes evidence rather than adopting tags.

`prepare-catalog.mjs` projects the YGOPRODeck full response into the compact `CardInfo` JSON asset used by local search. It keeps card identity, name, objective metadata, and numeric artwork IDs. Version 2 also contains offline-resolved `resolvedArchetypes`; it drops the provider's association tag, descriptions, prices, set data, and image URLs. It never fetches artwork. The asset URL remains `card-catalog.v1.json` for deployment compatibility; its internal format version is independent of session schema 5.

Refresh from an already downloaded official response without network access:

```sh
node infrastructure/card-catalog/prepare-catalog.mjs --catalog cardinfo.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --audit membership-audit.json
```

Omitting `--catalog` makes one full metadata request with `misc=yes` (including format metadata). Add `--save-source cardinfo.json` to retain its exact bytes for subsequent offline rebuilds. The catalog is validated and serialized before the output is atomically replaced. The normal app build and test suite never call the provider. Identical saved source and checked-in policy produce byte-identical catalog and audit JSON, independently of response order.

## Main Deck eligibility

`card-eligibility.mjs` is the shared policy for canonical generation, retained
translation projections, locale assets and coverage denominators:

- Keep the explicitly recognized Main Deck monster types, including Ritual,
  Ritual/Pendulum, Normal/Pendulum, Tuner and Flip variants, plus Spells and Traps.
- Fusion, Synchro, Xyz or Link in either type or frame classification wins over
  Pendulum/Effect/Ritual classifications. Exclude Tokens and Skills.
- Exclude records whose supplied format metadata lists Speed Duel without TCG or
  OCG. Shared TCG/OCG/Speed Duel cards remain eligible. Missing formats do not by
  themselves exclude unreleased cards; save the full `misc=yes` response for refreshes.
- Promotional printing is not a card type or an exclusion rule. Ordinary Main Deck
  cards remain eligible even if their only release is a magazine/promo release,
  they have Speed Duel/promotional printings, or they are currently forbidden.
  The reviewed `eligibility-policy.json` excludes special promotional identities:
  the four Anotherverse prize monsters, seven early World Championship prize
  monsters, Get Your Game On!, Tyler the Great Warrior and Decoy. It also excludes
  the provider's unrevealed deck-profile placeholder. Entries include exact ID,
  English name, type and evidence; changed identities require review. These are
  scope decisions, not claims that all prize cards are illegal. New Main Deck
  records with exclusively Championship-prize/Make-A-Wish set metadata stop for
  review; never exclude every card with a matching individual printing.
- Unrecognized types stop generation for review rather than silently disappearing.
  No ID-range, translated-name, arbitrary substring or banlist heuristics apply.

The initial trim filters the existing catalogue from commit `b3c3296` through
`eligibleCatalog`, without refreshing provider identities or recomputing memberships.
Every retained object, including English identity, artwork aliases, metadata,
resolved archetypes and reviewed name clauses, is byte-for-byte equivalent as JSON.
`snapshots/main-deck-migration.audit.json` records the input/output hashes and counts.
To reproduce this one-time trim offline, extract the old asset from that commit,
parse it and serialize `eligibleCatalog(previous)` followed by one newline.
Subsequent full-source refreshes use the normal offline command above.

| Pool | Before | Shipped eligible pool |
| --- | ---: | ---: |
| Canonical cards | 14,607 | 12,156 |
| Retained YAML Yugi name records | 14,414 | 12,106 |
| German canonical labels | 14,125 | 11,956 |
| Missing/withheld German labels | 482 | 200 |

Removed canonical records: 2,206 Extra Deck, 124 Skills, 106 Tokens, 14 special
promotional identities and one placeholder. Canonical JSON is 3,597,796 bytes;
German JSON is 415,971 bytes. All 24 example entries remain covered.

The full-response floor is 14,000 records **before filtering**, with a separate
11,000 eligible-card floor. The canonical CLI compares against the existing output
(or explicit `--previous FILE`) before writing; unexplained eligible-card removals
fail even above those floors. A reviewed `--allow-removals FILE` lists exact IDs,
not a percentage exemption. Always supply `--previous` when generating a new path
for a refresh. Translation removals likewise require explicit accepted IDs; the
initial trim allowed only names belonging to policy-excluded canonical cards.
Do not lower baselines or automatically accept removals to make a partial response pass.

## Membership policy and limits

`archetype-policy.json` is the reviewed candidate vocabulary and per-name matching policy, initially seeded from the provider's `archetypes.php` endpoint. That endpoint supplies candidates, not membership evidence. Excluded labels include thematic groupings, malformed entries, and unreviewed series without a quoted name reference in the source snapshot. They are deliberately not guessed from provider association. Refresh the candidate endpoint separately, review additions/removals and actual English name references, then edit this file; regeneration never silently adopts a new candidate vocabulary.

Default matching uses English token boundaries, including plural `s`. Explicit prefix/substring/suffix rules cover joined names such as Twilightsworn, Orcustrated, Evigishki, Purrelyly, Electromagnet Warrior, and XX-Saber. Matching folds case and typographic apostrophes, double quotes and dash variants, but does not remove arbitrary punctuation or whitespace. `HERO` is intentionally case sensitive to exclude Hero Kid; Umi requires an exact effective name. Phantom Knights retains the established key spelling while accepting the printed `The Phantom Knights` declaration. Nested names can contribute multiple properties. These rules are conservative English-name coverage, not a universal Japanese-name or card-rules interpreter.

The parser scans complete descriptions, including text after flavor, materials and Pendulum sections. Whole standalone sentences recognize permanent card/monster membership, exclusions, and unconditional name aliases; multiple quoted memberships are supported. A permanent name substitution replaces printed-name matches, whereas an `also` alias adds another effective name. Added compound memberships include matching parent names; explicit exclusions take precedence. Conditional/activated name changes and ordinary mentions do not add membership. Card identity, direct-card references, type and deck-construction rules are unaffected.

Unknown/malformed permanent declarations and missing descriptions produce an offline audit entry with ID and a bounded excerpt. Such cards ship `null` (unverified), rather than a confidently empty set. A verified absence ships `[]`. The audit also lists every addition/removal relative to provider association and every overlap, so review it across the full source before publication. Zero unresolved recognized declarations does **not** prove the absence of unsupported wording or localization anomalies. Non-identity Level/Rank conditions are explicitly ignored. The candidate list and naming policy remain maintained data.

Quoted identity statements without an explicit permanence marker or zone/activation condition are also audited. `reviewed-name-clauses.json` accepts three verified older clauses only when their original ID, English name, type and complete normalized sentence match: Neo-Spacian Marine Dolphin and Neo-Spacian Twinkle Moss add a name; Lemuria, the Forgotten City replaces its effective name with Umi, consistent with Konami's current permanent wording. Changed or unreviewed wording still requires audit. This affects archetype classification only: the catalog and workspace retain original names and IDs, and direct combo card requirements select the exact workspace entry even when two entries share a treated-as name.

Skill identity clauses remain out of scope in the membership parser. Skills and
Extra Deck cards no longer ship in the eligible catalogue; parser unit cases and
reviewed clauses remain available for source review. Lemuria's permanent Umi name
rule remains in the shipped pool.

Representative primary checks: [Summoned Skull](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=4028&ope=2&request_locale=en), [Hardened Armed Dragon](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=8374&ope=2&request_locale=en), [Double Iris Magician](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=12908&ope=2&request_locale=en), and [Harpie Perfumer](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=14165&ope=2&request_locale=en). Provider field/endpoint documentation: [API guide](https://ygoprodeck.com/api-guide/).

## Runtime and saved-state compatibility

All objective archetype properties consume resolved data. Catalog lookups support canonical IDs and unique artwork aliases; an editable display name cannot override a known ID. Unknown live/API/cache records have no objective archetype claim. Old v1 catalogs remain readable, but their association tags cannot become memberships. Historical caches cannot smuggle resolved claims into the current catalog.

Session load, share acceptance and recovery reconcile only automatic archetype properties against the bundled catalog before optional legacy enrichment. Exact unique names are used only when there is no external ID. Unknown/ambiguous identities and unverified membership sets remain unchanged. Cards absent after eligibility trimming remain in saved sessions; import still attempts targeted metadata lookup and preserves an unresolved imported ID/copy count when lookup fails. User categories, unrelated metadata, explicit manual keys and saved simulation plans/provenance are preserved. If an old automatic membership previously absorbed a manual assignment, the missing provenance cannot be reconstructed: only explicitly stored override keys are preserved. An already-correct card retains its reference; reconciliation does not itself write browser storage or bump session schema.

The same saved provider response can also refresh the artwork allowlist:

```sh
python infrastructure/artwork/prepare-manifest.py --catalog cardinfo.json --output manifest.json
```

Run the generator's offline tests with:

```sh
node --test infrastructure/card-catalog/*.test.mjs
```

## German search names

[**YAML Yugi**](https://github.com/DawnbrandBots/yaml-yugi) is the sole source of
German card names. YGOPRODeck continues to supply the canonical English metadata
catalog and artwork; it is no longer a second translation source. The runtime
asset contains only `[canonical ID, German name]` pairs, used for display and
bilingual search. Selecting either language returns the original English card.
Saved names, custom labels, session/share JSON, archetype keys and membership,
effects and probability inputs remain unchanged.

### Source and compilation credit

The name-only projection is pinned to
[86c467a8e14e633a6210458905f5a479c4836405](https://github.com/DawnbrandBots/yaml-yugi/tree/86c467a8e14e633a6210458905f5a479c4836405)
(October 31, 2026). The downloaded OCG/TCG aggregate has 110,194,011 bytes and
SHA-256 `c41ad997e6bf5fa3431feaf5c425967c5c9306d06ad1eae5d4a592277f55222b`.
Before this eligibility trim, its complete identity/English/German-name projection was checked against
all individual `data/cards/*.json` records from that revision. The pinned source
archive has SHA-256
`b37373450e0c7d4dde033150984b3a11e8cb9d1e3fd72b3d081f935929d2ecd9`.
Verify the moving Pages aggregate against an immutable revision before refreshing;
the old `aggregate` branch is not the current published snapshot.

Credit **YAML Yugi and its upstream contributors for compiling translation data**, including
the [Yugipedia source cache](https://github.com/DawnbrandBots/yaml-yugipedia).
The [upstream README](https://github.com/DawnbrandBots/yaml-yugi/blob/86c467a8e14e633a6210458905f5a479c4836405/README.md)
attributes underlying card content to Studio Dice/SHUEISHA, TV TOKYO and KONAMI;
its AGPL license covers pipeline code. No upstream pipeline code is copied, and
this project's software license does not relicense rights-holder card content.
Compilation credit appears in Help and the repository README.

`snapshots/names.de.yaml-yugi.2026-10-31.json` retains only eligible canonical/unique-artwork source IDs and
English/German names, explicit source-assigned-ID markers, revision and retrieval
metadata, and the full aggregate's hash/size. No descriptions, artwork, prices,
foreign rules or archetype data are retained. The old German YGOPRODeck projection
and the two-source merge path have been removed.

### Identity and validation

Printed `password` values map first to exact canonical IDs, then to **unique**
artwork aliases already verified in the canonical catalog. This uses the same
precedence as the runtime catalog index. Source `fake_password` values explicitly
identify passwordless records such as the Egyptian Gods; they are marked
`assigned: true` in the retained projection and audited separately. These are
source-assigned identifiers, not claims about printed passcodes.

No English/German name matching establishes identity. Unknown or ambiguous IDs
are withheld. Repeated aliases on the same card do not create ambiguity. Distinct
canonical cards sharing a German name remain distinct choices; multiple source
IDs resolving to one canonical card coalesce only when their German labels agree.
A disagreement fails generation for review. Spelling differences from canonical
English are audited without changing the English identity.

The existing conservative label policy withholds controls, HTML entities and
angle brackets. Seven eligible literal Maliss `<P>` or `<C>` names therefore remain
withheld; this does not mean they are malicious or lack official translations.
Missing/withheld labels use English fallback.

### Coverage and source choice

Coverage uses the 12,156 eligible canonical cards as its denominator: 11,956
German labels and 200 missing/withheld labels. The retained projection contains
12,106 eligible source identities, including alias records and missing labels.
Excluded cards, unknown identities and ambiguous artwork joins are not retained
as translation-maintenance gaps. The audit lists missing eligible IDs, rejected
labels, alias/source-assigned matches, spelling differences, repeated labels,
source hashes, sizes and actual example labels/fallback reasons. All 24 example
entries are covered; five K9 names equal their English spelling. Coverage does not
establish complete official translations or native-speaker review.

### Offline generation and refresh

Regenerate the shipped asset and complete audit **offline**:

```sh
node infrastructure/card-catalog/prepare-localized-names.mjs --snapshot infrastructure/card-catalog/snapshots/names.de.yaml-yugi.2026-10-31.json --catalog YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --example YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.de.v1.json --audit infrastructure/card-catalog/snapshots/names.de.yaml-yugi.2026-10-31.audit.json
node --test infrastructure/card-catalog/prepare-localized-names.test.mjs
```

To refresh, download the aggregate separately and verify its name/ID projection
against the chosen immutable repository revision. Add
`--provider PATH_TO_AGGREGATE.json --revision COMMIT_SHA --retrieved-at ISO_TIMESTAMP`
to the command above and choose a new dated snapshot/audit path. The existing
output is the required coverage baseline; use `--previous PATH_TO_SHIPPED_ASSET`
when writing a new output path. Removed labels fail validation before any output
is written. Intentional changes such as a reviewed source migration require
`--allow-removals FILE`, a JSON array listing the exact accepted canonical IDs;
it does not permit any other removals. Review all source/identity/coverage changes
before publishing. Retain only the name projection, never the full aggregate.

The generator never fetches the network. Builds, tests, publishing and startup
make no translation-provider requests. The existing lazy loader requests just
`data/card-names.de.v1.json` when German search or verified deck display needs it.
English, Help, an empty calculator and synchronous label reads do not load names.
Concurrent display/search requests share one task, including a failed result.
Failure falls back to English and manual entry; regional UI cultures use their
supported parent locale. The runtime name asset format and loader are unchanged.

Exact, prefix and substring searches use both languages with the existing English
name/ID tie order and 20-result limit. Matching is ordinal and case-insensitive;
umlauts and ß remain distinct, with no accent stripping or ß/ss conflation.
Display labels require a verified external ID and an exact stored English name;
custom labels stay verbatim. The data-label accessor accepts only verified
catalog objects, and no localized fields are added to sessions.
