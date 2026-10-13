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
