# Local card search catalog

`prepare-catalog.mjs` projects the YGOPRODeck full response into the compact `CardInfo` JSON asset used by local search. It keeps card identity, name, objective metadata, and numeric artwork IDs. Version 2 also contains offline-resolved `resolvedArchetypes`; it drops the provider's association tag, descriptions, prices, set data, and image URLs. It never fetches artwork. The asset URL remains `card-catalog.v1.json` for deployment compatibility; its internal format version is independent of session schema 5.

Refresh from an already downloaded official response without network access:

```sh
node infrastructure/card-catalog/prepare-catalog.mjs --catalog cardinfo.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --audit membership-audit.json
```

Omitting `--catalog` makes one full metadata request. Add `--save-source cardinfo.json` to retain its exact bytes for subsequent offline rebuilds. The catalog is validated and serialized before the output is atomically replaced. The normal app build and test suite never call the provider. Identical saved source and checked-in policy produce byte-identical catalog and audit JSON, independently of response order.

## Membership policy and limits

`archetype-policy.json` is the reviewed candidate vocabulary and per-name matching policy, initially seeded from the provider's `archetypes.php` endpoint. That endpoint supplies candidates, not membership evidence. Excluded labels include thematic groupings, malformed entries, and unreviewed series without a quoted name reference in the source snapshot. They are deliberately not guessed from provider association. Refresh the candidate endpoint separately, review additions/removals and actual English name references, then edit this file; regeneration never silently adopts a new candidate vocabulary.

Default matching uses English token boundaries, including plural `s`. Explicit prefix/substring/suffix rules cover joined names such as Twilightsworn, Orcustrated, Evigishki, Purrelyly, Electromagnet Warrior, and XX-Saber. Matching folds case and typographic apostrophes, double quotes and dash variants, but does not remove arbitrary punctuation or whitespace. `HERO` is intentionally case sensitive to exclude Hero Kid; Umi requires an exact effective name. Phantom Knights retains the established key spelling while accepting the printed `The Phantom Knights` declaration. Nested names can contribute multiple properties. These rules are conservative English-name coverage, not a universal Japanese-name or card-rules interpreter.

The parser scans complete descriptions, including text after flavor, materials and Pendulum sections. Whole standalone sentences recognize permanent card/monster membership, exclusions, and unconditional name aliases; multiple quoted memberships are supported. A permanent name substitution replaces printed-name matches, whereas an `also` alias adds another effective name. Added compound memberships include matching parent names; explicit exclusions take precedence. Conditional/activated name changes and ordinary mentions do not add membership. Card identity, direct-card references, type and deck-construction rules are unaffected.

Unknown/malformed permanent declarations and missing descriptions produce an offline audit entry with ID and a bounded excerpt. Such cards ship `null` (unverified), rather than a confidently empty set. A verified absence ships `[]`. The audit also lists every addition/removal relative to provider association and every overlap, so review it across the full source before publication. Zero unresolved recognized declarations does **not** prove the absence of unsupported wording or localization anomalies. Non-identity Level/Rank conditions are explicitly ignored. The candidate list and naming policy remain maintained data.

Quoted identity statements without an explicit permanence marker or zone/activation condition are also audited. `reviewed-name-clauses.json` accepts three verified older clauses only when their original ID, English name, type and complete normalized sentence match: Neo-Spacian Marine Dolphin and Neo-Spacian Twinkle Moss add a name; Lemuria, the Forgotten City replaces its effective name with Umi, consistent with Konami's current permanent wording. Changed or unreviewed wording still requires audit. This affects archetype classification only: the catalog and workspace retain original names and IDs, and direct combo card requirements select the exact workspace entry even when two entries share a treated-as name.

It's a Toon World! remains `null` with audit reason `out-of-scope-skill-identity-clause`. Skill identity is applied through Speed Duel setup/effects, outside this deck/hand model; unqualified Skill identity statements are not accepted as Main Deck aliases, even if they say `always`. The full-source review additionally surfaces Tomb of the Pharaoh's permanent Necrovalley wording as out of scope (previously a verified empty set); explicit field conditions such as Mythic Depths remain ignored. The follow-up changes the unresolved count from four to two out-of-scope Skill records; only the three reviewed records gain memberships. See the [official Skill rules update](https://yugiohblog.konami.com/2021/news-updates/skillcard-update/). The reviewed clauses link their primary card-text sources and remain offline data; neither descriptions nor alternate-name tags are shipped.

Representative primary checks: [Summoned Skull](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=4028&ope=2&request_locale=en), [Hardened Armed Dragon](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=8374&ope=2&request_locale=en), [Double Iris Magician](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=12908&ope=2&request_locale=en), and [Harpie Perfumer](https://www.db.yugioh-card.com/yugiohdb/card_search.action?cid=14165&ope=2&request_locale=en). Provider field/endpoint documentation: [API guide](https://ygoprodeck.com/api-guide/).

## Runtime and saved-state compatibility

All objective archetype properties consume resolved data. Catalog lookups support canonical IDs and unique artwork aliases; an editable display name cannot override a known ID. Unknown live/API/cache records have no objective archetype claim. Old v1 catalogs remain readable, but their association tags cannot become memberships. Historical caches cannot smuggle resolved claims into the current catalog.

Session load, share acceptance and recovery reconcile only automatic archetype properties against the bundled catalog before optional legacy enrichment. Exact unique names are used only when there is no external ID. Unknown/ambiguous identities and unverified membership sets remain unchanged. User categories, unrelated metadata, explicit manual keys and saved simulation plans/provenance are preserved. If an old automatic membership previously absorbed a manual assignment, the missing provenance cannot be reconstructed: only explicitly stored override keys are preserved. An already-correct card retains its reference; reconciliation does not itself write browser storage or bump session schema.

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
Its entire retained identity/English/German-name projection was checked against
all individual `data/cards/*.json` records from that revision. The pinned source
archive has SHA-256
`b37373450e0c7d4dde033150984b3a11e8cb9d1e3fd72b3d081f935929d2ecd9`.
Verify the moving Pages aggregate against an immutable revision before refreshing;
the old `aggregate` branch is not the current published snapshot.

Credit **YAML Yugi and its upstream contributors for their compilation**, including
the [Yugipedia source cache](https://github.com/DawnbrandBots/yaml-yugipedia).
The [upstream README](https://github.com/DawnbrandBots/yaml-yugi/blob/86c467a8e14e633a6210458905f5a479c4836405/README.md)
attributes underlying card content to Studio Dice/SHUEISHA, TV TOKYO and KONAMI;
its AGPL license covers pipeline code. No upstream pipeline code is copied, and
this project's software license does not relicense rights-holder card content.
Compilation credit appears in Help and the repository README.

`snapshots/names.de.yaml-yugi.2026-10-31.json` retains only source IDs and
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
angle brackets. Ten literal Maliss `<P>`, `<Q>` or `<C>` names therefore remain
withheld; this does not mean they are malicious or lack official translations.
Missing/withheld labels use English fallback.

### Coverage and source choice

| Current 14,607-card catalog | German labels | Missing / withheld | Example entries |
| --- | ---: | ---: | ---: |
| Previous YGOPRODeck names | 11,769 | 2,838 | 4/24 |
| YAML Yugi, printed-ID exact matches only | 14,103 | 504 | 24/24 |
| Shipped YAML Yugi, verified IDs and aliases | 14,125 | 482 | 24/24 |

The source contains 14,414 records. The audit records all missing IDs, rejected
labels, unknown/ambiguous IDs, alias/source-assigned matches, spelling differences,
repeated labels, source hashes, sizes and actual example labels/fallback reasons.
Five example K9 names equal their English spelling. Neither equal names nor
coverage totals establish complete official translation coverage or native-speaker
review.

A single translation source was chosen after comparison with the fill-only union.
With verified mappings, 116 old provider labels fall back to English: 85 Skills,
16 Tokens and these 15 other records: Sanctity of Dragon; Iron Knight of Revolution;
Holactie the Creator of Light; Shuttleroid; Aroma Strategy; Mooyan Curry; Tyler the
Great Warrior; Get Your Game On!; Ulevo; Meteo the Matchless; King of Destruction -
Xexex; Queen of Fate - Eternia; Testament of the Arcane Lords; Chimaera, the Master
of Beasts; and Emperor of Lightning. This tradeoff was explicitly accepted in
favor of one translation source. No claim is made that every old provider label
was an official German release. Skills/Tokens do not justify maintaining another
translation provider for this calculator.

The compact asset has 502,498 UTF-8 bytes. Compression sizes in the audit use
Node's defaults, not hosted transfer measurements.

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
