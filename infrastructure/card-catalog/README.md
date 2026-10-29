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

`prepare-localized-names.mjs` joins the German endpoint's top-level passcodes to
the existing canonical catalog by **exact ID only**. It never joins by translated
name, `name_en`, or artwork ID, and never derives archetype/name-rule membership
from German. The runtime asset contains only `[canonical ID, German name]` pairs.
These names act as search aliases for the canonical English entries; they are
not a second card metadata catalog.

The October 28, 2026 response from
[`cardinfo.php?language=de`](https://db.ygoprodeck.com/api/v7/cardinfo.php?language=de)
contained 11,769 records (18,992,044 response bytes). All 11,769 match exact IDs in
the checked-in 14,607-card catalog: 2,838 canonical cards have no German record.
There were zero duplicate IDs, duplicate German names, unknown IDs or artwork-only
ID matches. All records supplied `name_en`; eight differ from the canonical
English spelling (seven capitalization differences and Cu/Cú Chulainn). These are
reported for review, never used to override identity. 631 supplied German names
equal their English names; this does **not** establish whether each is an official
unchanged name or an untranslated provider record. No complete/native-speaker
translation-coverage claim is made. The [provider guide](https://ygoprodeck.com/api-guide/)
documents incomplete foreign-language coverage and English artwork.

`snapshots/names.de.2026-10-28.json` retains the original `id`, `name` and `name_en`
fields plus endpoint, retrieval time, original byte count and SHA-256. It is a
**name-only projection** of the downloaded response, not its full card text.
Descriptions, images, prices, set data and foreign rule metadata are deliberately
not retained or deployed. The adjacent audit records missing IDs, spelling
differences and ambiguous/unknown identity candidates. The projection has 924,993
bytes; the deployable `card-names.de.v1.json` has 412,046 UTF-8 bytes (173,885 gzip,
135,945 Brotli using Node's defaults; actual host compression may differ).

Regenerate byte-identical runtime names and audit entirely offline:

```sh
node infrastructure/card-catalog/prepare-localized-names.mjs --snapshot infrastructure/card-catalog/snapshots/names.de.2026-10-28.json --catalog YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --output YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.de.v1.json --audit infrastructure/card-catalog/snapshots/names.de.2026-10-28.audit.json
node --test infrastructure/card-catalog/prepare-localized-names.test.mjs
```

To refresh, download the documented German endpoint once to a local file, then
add `--provider PATH_TO_RESPONSE.json` and choose a new dated snapshot/audit path.
Pass `--previous YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.de.v1.json`
when writing to a new output path. Refreshes fail before writing if a previously
shipped ID loses its label. Without `--previous`, an existing output is the
baseline; a provider refresh cannot silently start without either baseline.
This validates the names before replacing output; review the audit and update the
coverage figures before publication. The tool itself never fetches the network.
Normal builds, tests, publishing and startup never call the provider for names.

A German card search with at least two non-space characters or a populated deck
requiring verified display labels requests the name asset. Concurrent search and
display requests share the same load, including a failed result, for the service
lifetime. Synchronous label reads, Help, an empty calculator, artwork and checkpoint
capture do not request it. Rendering an imported/restored deck may request it.
Searches while the UI language is English do not request it. A missing, invalid, oversized or timed-out
name asset falls back to the existing English catalog search and manual entry.
The service has an explicit supported-name-asset map (currently only `de`) and a
shared lazy task per locale. Regional UI cultures resolve through their parents;
a future reviewed locale can reuse this loader by adding its own map entry and
asset. Merely registering a UI locale never guesses or downloads a name asset.

Populated display preparation uses one batch API and overlaps cold catalog/name
downloads. Empty/manual-only decks still fetch neither; with a warm catalog,
custom or unverified names do not request the language asset. Validated catalog
records build a shared canonical-ID/artwork-alias index once. Exact canonical IDs
take precedence, repeated artwork IDs on one record remain unique, and aliases
owned by multiple records remain unresolved. All languages reuse this index and
the same display fallback path; no localized fields are added to sessions.

German search ranks exact, then prefix, then substring matches across both names,
with the existing canonical English name/ID tie order and 20-result limit. Each
canonical card appears once. Matching is ordinal and case-insensitive, retaining
umlauts and ß without accent stripping or ß/ss conflation. Distinct cards with
identical localized names remain separate choices. Suggestions use `DisplayLabels`
with the loaded German label; selecting one passes the original `CardInfo` to the
existing import/effect path. Deck display labels require a verified external ID
(exact canonical or unique artwork alias) and an exact stored canonical English
name. Custom names retain their text. This presentation rule never mutates saved
`Card.Name`; name-field focus/blur does not save a translation. User labels, objective
metadata and English-derived memberships remain unchanged. The provider's
abbreviations are retained (for example, `Blauäugiger w. Drache`), not expanded by
guessing.
The data-label accessor accepts only the original verified catalog objects;
matching IDs on unverified live/cache objects do not confer a translated label.

### YAML Yugi evaluation (October 29, 2026; research only)

The candidate is [DawnbrandBots/YAML Yugi](https://github.com/DawnbrandBots/yaml-yugi),
at source commit `296ed20acd86883fe04a485caf7a4fea67d250de` (08:01:19 UTC).
The downloaded OCG/TCG aggregate has 110,067,238 bytes and SHA-256
`ff00889b46ceb7df7d1a2a53cf76ab0f5ea39446bce6896f7f04cef42fdf31f7`.
Its complete passcode/German-name projection was compared with all individual
`data/cards/*.json` records at that commit and matched. This pins the data behind
the moving Pages URL; the stale `aggregate` branch is **not** this snapshot.
Upstream's merge workflow runs on dispatch/update events, not a promised release
schedule. The observed revision is current to the retrieval date.

**Redistribution is unresolved, so no YAML Yugi snapshot or new runtime names are
checked in.** The pinned [README](https://github.com/DawnbrandBots/yaml-yugi/blob/296ed20acd86883fe04a485caf7a4fea67d250de/README.md)
attributes card data to Studio Dice/SHUEISHA, TV TOKYO and KONAMI, and assigns
AGPL-3.0-or-later to the remaining pipeline source. It does not clearly grant
redistribution rights for a bulk name/passcode projection. Publication and the
code license alone do not resolve that question. No pipeline code was copied.
This is an unresolved permission question, not a conclusion that names may never
be reused. Resolve the name-only data rights and attribution before shipping it.

The existing generator now has an **audit-only** comparison path. It never writes
a candidate runtime asset and refuses `--output` in comparison mode. It retains
only passcodes and English/German names in memory, rejects duplicate/malformed
non-null passcodes, ignores missing passcodes, and reports invalid labels instead
of substituting translations. Names with angle brackets, controls or HTML entities
are withheld conservatively. In this snapshot, the ten withheld labels contain
literal Maliss `<P>`, `<Q>` or `<C>` markers; this is not evidence of malicious HTML
or absent official translations. They need a reviewed literal-label policy.

| Coverage of the 14,607 canonical cards | Existing YGOPRODeck | Usable YAML Yugi | Hypothetical fill-only union |
| --- | ---: | ---: | ---: |
| Exact canonical matches | 11,769 | 14,103 | 14,240 |
| Name differs from English | 11,138 | 13,343 | 13,470 |
| Name equals English | 631 | 760 | 770 |
| Missing / withheld | 2,838 | 504 | 367 |
| Actual example entries resolved | 4/24 | 24/24 | 24/24 |

The raw candidate has 14,409 records, 118 without a passcode, 271 without a German
name, 14,132 with both, and no duplicate passcodes. Before label validation it has
14,113 exact canonical matches; ten are withheld by the rule above. Its remaining
19 German-name passcodes match only artwork aliases, so none is joined. There is
one repeated display name shared by distinct canonical cards, which remain
separate. All **63 disagreements** are listed in the local comparison output with
both spellings and the explicit decision `retain-ygoprodeck`. Every existing
YGOPRODeck label stays unchanged; no conflict is silently declared authoritative.

The example report reads `wwwroot/sample-data/example_session_state.json`, then
looks up its exact IDs and canonical stored names in the same generated name
pairs. It does not maintain a separate example mapping. The candidate fills all
20 current gaps; five K9 names legitimately remain identical to English. There
are no candidate conflicts or unresolved example entries. The 367 full-catalog
gaps are provider/validation gaps, **not** a claim that those cards have no official
German name. Current production still resolves only 4/24 because the candidate
has not been cleared for redistribution.

Official German spot checks confirmed [Razen](https://www.db.yugioh-card.com/yugiohdb/card_search.action?ope=2&cid=18727&request_locale=de),
[Fuwalos](https://www.db.yugioh-card.com/yugiohdb/card_search.action?ope=2&cid=20500&request_locale=de)
and [Magnamhut](https://www.db.yugioh-card.com/yugiohdb/card_search.action?ope=2&cid=17762&request_locale=de).
The material [Fairy Tale Tails disagreement](https://www.db.yugioh-card.com/yugiohdb/card_search.action?ope=2&cid=15853&request_locale=de)
also matches the candidate's spelling; the existing label is nevertheless retained
pending a separate explicit correction. Two other conflict-page retrievals were
unavailable; no full official verification of the candidate is claimed.

The hypothetical compact asset would be 506,533 bytes (214,412 gzip / 165,761
Brotli), versus 412,046 (173,885 / 135,945) currently deployed. These are Node
default compression measurements, not hosted transfer measurements. Runtime
loading, HTTP requests, saved state, canonical catalog and solver are unchanged.

Repeat the comparison locally, with the externally obtained, hash-verified input:

```sh
node infrastructure/card-catalog/prepare-localized-names.mjs --snapshot infrastructure/card-catalog/snapshots/names.de.2026-10-28.json --catalog YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json --compare-yaml-yugi /tmp/yaml-yugi-cards.json --revision 296ed20acd86883fe04a485caf7a4fea67d250de --retrieved-at 2026-10-29T11:57:50.615Z --example YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json --audit /tmp/yaml-yugi-comparison.json
node --test infrastructure/card-catalog/prepare-localized-names.test.mjs
```

The audit includes source/catalog/example hashes, all missing IDs, additions,
invalid labels, unknown/artwork-only IDs, spelling differences, disagreements,
per-example labels/fallback reasons, and sizes. Identical input bytes and explicit
retrieval metadata reproduce the same audit. Source hashes identify exact bytes;
the old Windows-generated audit's canonical-file hash includes CRLF, whereas a
Linux checkout uses LF. Its coverage fields and regenerated runtime asset agree.
An in-memory name-only projection also has its own hash; no full aggregate or
foreign descriptions/images need to be retained in this repository.

The narrow current alternative is the existing documented YGOPRODeck integration
with English fallback. A fresh full German response on October 29 still has
11,769 records and no Razen entry. YGOJSON aggregates YAML Yugi/Yugipedia and is
not independent evidence of data permission; swapping to its MIT-licensed tooling
would not by itself resolve upstream data rights. No adequate additional source
with verified redistribution permission was established in this evaluation.
